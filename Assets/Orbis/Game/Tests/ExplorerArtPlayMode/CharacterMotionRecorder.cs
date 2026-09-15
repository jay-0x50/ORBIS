using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.M0;
using Orbis.M0.Animation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orbis.Game.Tests
{
    /// <summary>
    /// Test-only late-frame observer. The live Animator, motor, action FSM, effects and IK evaluate normally.
    /// Queue the next frame's keyboard state only AFTER measuring this one; no extra InputSystem.Update,
    /// Animator.Update, normalized-time posing or WaitForEndOfFrame (which can stall a batch editor).
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class CharacterMotionRecorder : MonoBehaviour
    {
        public const int FrameRate = 30;
        public const int TotalFrames = 300;
        public const float FrameDuration = 1f / FrameRate;
        const int Width = 960, Height = 720;
        static readonly Vector3 CameraOffset = new Vector3(3.2f, 1.2f, 4.5f);
        static readonly HumanBodyBones[] TrackedBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes
        };

        [Serializable] public sealed class ClipSample { public string name; public float weight, length; }
        [Serializable] public sealed class LayerSample
        {
            public string layer;
            public int index, stateHash, nextStateHash;
            public float weight, normalizedTime, nextNormalizedTime, transitionNormalizedTime;
            public bool transitioning;
            public ClipSample[] currentClips, nextClips;
        }
        [Serializable] public sealed class BoneSample
        {
            public string slot, transformName;
            public Vector3 worldPosition, rootLocalPosition, localPosition;
            public Quaternion worldRotation, localRotation;
            // This is an ankle/toe BONE-to-floor distance, NOT a skinned sole penetration metric.
            public bool floorHit;
            public Vector3 floorPoint, floorNormal;
            public float boneHeightAboveFloor;
        }
        [Serializable] public sealed class MotionFrame
        {
            public int index, unityFrame;
            public float sampleTime, deltaTime, gameplayTime, measuredSpeed, requestedSpeed, animatorSpeed;
            public string phase, gameplayState;
            public string[] requestedKeys;
            public bool grounded, actionLocked, finite;
            public Vector2 inputMove;
            public Vector3 rootPosition, measuredVelocity, cameraPosition, lookAt;
            public Quaternion rootRotation;
            public LayerSample[] layers;
            public BoneSample[] bones;
            public float imageSampleMin, imageSampleMax;
            public float visualYawOffset, motionSpeed, strideRate, motionX, motionZ;
            public CharacterMotionSkinProbe.FootSample[] skinFeet;
            public int attackStep;
            public float attackClock, attackParameter, actionParameter, timeScale;
            public bool stopping;
            public float stopNormalizedTime;
        }
        [Serializable] public sealed class MotionReport
        {
            public int recipeVersion = 1, frameRate = FrameRate, expectedFrameCount = TotalFrames, width = Width, height = Height;
            public string stage, runId, character, capturedUtc, scene, animatorController, avatar, outputDirectory;
            public string graphicsDevice, unityVersion, renderPipeline, colorSpace, failure;
            public string captureSource = "Actual Unity URP SingleCameraRequest JPEG frames, live gameplay KeyboardState input and late-frame bone measurements. No generated-image or manually sampled animation frames.";
            public string clock = "Time.captureDeltaTime = 1/30. Frame sampleTime = index/30. Camera follows root translation with a fixed WORLD offset, never rotates with the pawn.";
            public string groundMetric = "Bone-to-plane distance only; no assertion about skin sole contact or motion quality. The flat test floor is outside the authored continent; this is not a terrain-IK acceptance run.";
            public string[] phases = { "0..29 Idle", "30..89 Walk W", "90..149 Run W+LeftShift", "150..179 Stop", "180..209 Left turn A", "210..239 Right turn D", "240..269 Skill while walking W (G pressed only on frame 240)", "270..299 Final stop" };
            public string[] missingOptionalBones;
            public string[] sourceMeshes;
            public int sourceVertexCount, msaa;
            public float avatarHumanScale;
            public string motionProfile;
            public float walkCycleDistance, runCycleDistance;
            public Vector3 animatorLossyScale;
            public Vector3 floorCenter, cameraWorldOffset = CameraOffset;
            public float floorGridMetres = 2f, fieldOfView = 28f;
            public List<MotionFrame> frames = new List<MotionFrame>(TotalFrames);
        }

        Animator animator;
        HumanAnimationDriver motionDriver;
        CharacterMotionSkinProbe skinProbe;
        BasicAttackCombo combat;
        Func<int,Key[]> inputRecipe;
        Func<int,string> phaseRecipe;
        Action<int> prepareFrame;
        bool allowTimeScale;
        PlayerMotor motor;
        M0Input input;
        Keyboard keyboard;
        Camera captureCamera;
        UniversalAdditionalCameraData cameraData;
        RenderTexture target, resolved;
        Texture2D pixels;
        VolumeStack volumeStack;
        Transform[] boneTransforms;
        Vector3 previousPosition;
        string output;
        bool resourcesReleased;
        public bool Recording { get; private set; }
        public string Error { get; private set; }
        public int FrameCount => Report?.frames.Count ?? 0;
        public MotionReport Report { get; private set; }

        public void Begin(string stage, string runId, string character, string directory, Animator liveAnimator,
            PlayerMotor liveMotor, M0Input liveInput, Keyboard testKeyboard, Vector3 floorCenter,
            Func<int,Key[]> inputRecipe = null, Func<int,string> phaseRecipe = null, Action<int> prepareFrame = null, bool allowTimeScale = false)
        {
            if (Recording || Report != null) throw new InvalidOperationException("A recorder captures one sequence.");
            animator = liveAnimator; motor = liveMotor; input = liveInput; keyboard = testKeyboard; output = directory;
            this.inputRecipe=inputRecipe; this.phaseRecipe=phaseRecipe; this.prepareFrame=prepareFrame; this.allowTimeScale=allowTimeScale;
            combat=motor.GetComponent<BasicAttackCombo>();
            if (animator == null || motor == null || input == null || keyboard == null)
                throw new ArgumentNullException("The actual live player, animator and test keyboard are required.");
            if (!animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
                throw new InvalidOperationException("The player capture requires its valid Humanoid avatar.");
            boneTransforms = TrackedBones.Select(animator.GetBoneTransform).ToArray();
            foreach (var required in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand,
                HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                if (animator.GetBoneTransform(required) == null) throw new InvalidOperationException("Missing required capture bone: " + required);
            var skins = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s.sharedMesh != null && !s.name.StartsWith("Art Outline", StringComparison.Ordinal) &&
                    !s.name.StartsWith("M3 Silhouette", StringComparison.Ordinal)).ToArray();
            if (skins.Length == 0) throw new InvalidOperationException("The captured avatar has no skinned body.");
            motionDriver = animator.GetComponent<HumanAnimationDriver>();
            if (motionDriver != null)
            {
                if (!motionDriver.IsReady) throw new InvalidOperationException(motionDriver.ValidationError);
                skinProbe = new CharacterMotionSkinProbe(animator,motionDriver.Profile);
            }
            Report = new MotionReport
            {
                stage = stage, runId = runId, character = character, capturedUtc = DateTime.UtcNow.ToString("O"),
                scene = motor.gameObject.scene.path, animatorController = animator.runtimeAnimatorController.name,
                avatar = animator.avatar.name, avatarHumanScale = animator.humanScale, animatorLossyScale = animator.transform.lossyScale,
                outputDirectory = Path.GetFullPath(directory), floorCenter = floorCenter,
                graphicsDevice = SystemInfo.graphicsDeviceName, unityVersion = Application.unityVersion,
                colorSpace = QualitySettings.activeColorSpace.ToString(),
                renderPipeline = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "<none>",
                missingOptionalBones = TrackedBones.Where((bone, index) => boneTransforms[index] == null).Select(b => b.ToString()).ToArray(),
                sourceMeshes = skins.Select(s => s.sharedMesh.name + " / vertices " + s.sharedMesh.vertexCount).ToArray(),
                sourceVertexCount = skins.Sum(s => s.sharedMesh.vertexCount)
            };
            if (motionDriver != null)
            {
                Report.motionProfile = motionDriver.Profile.name;
                Report.walkCycleDistance = motionDriver.Profile.Get(HumanMotionSlot.Walk).CycleDistanceMetres;
                Report.runCycleDistance = motionDriver.Profile.Get(HumanMotionSlot.Run).CycleDistanceMetres;
                Report.groundMetric = "Actual fixed skin-sole vertex IDs selected once from the measured source sole plane and re-baked each late frame; calibrated foot-local point also recorded. Compare same near-ground vertex intervals, not sole centroid alone. Flat test stage, not terrain acceptance.";
            }
            if (allowTimeScale)
            {
                Report.recipeVersion=2;
                Report.phases=new[]{"0..29 Idle", "30..59 W", "60..74 Stop", "75..127 3-step combo with W/Space attempted", "150 Hurt", "180 Burst", "195 damage interrupts Burst", "215 Burst replay", "270 Cancel", "280 Warp", "285 release/reset", "299 Rest"};
                Report.clock="30fps frame schedule with live Time.timeScale/Timeline/hit-stop; scaled gameplay delta recorded. No manual Animator.Update or action Tick.";
            }
            Directory.CreateDirectory(directory);
            CreateCamera();
            previousPosition = motor.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Keys(0)));
            prepareFrame?.Invoke(0);
            Recording = true;
        }

        void CreateCamera()
        {
            Camera main = Camera.main;
            if (main == null) throw new InvalidOperationException("The live field did not create a main camera.");
            var cameraObject = new GameObject("Motion evidence camera / no brain or listener");
            cameraObject.transform.SetParent(transform, false);
            captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.CopyFrom(main);
            captureCamera.enabled = false; captureCamera.targetTexture = null;
            captureCamera.fieldOfView = Report.fieldOfView; captureCamera.nearClipPlane = .02f; captureCamera.farClipPlane = 65f;
            captureCamera.aspect = (float)Width / Height;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = new Color(.16f, .20f, .26f, 1f);
            cameraData = captureCamera.GetUniversalAdditionalCameraData();
            var sourceData = main.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = sourceData.renderPostProcessing;
            cameraData.antialiasing = sourceData.antialiasing; cameraData.antialiasingQuality = sourceData.antialiasingQuality;
            cameraData.volumeLayerMask = sourceData.volumeLayerMask; cameraData.volumeTrigger = captureCamera.transform;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var descriptor = new RenderTextureDescriptor(Width, Height, RenderTextureFormat.ARGB32, 24)
            {
                msaaSamples = main.allowMSAA && pipeline != null ? pipeline.msaaSampleCount : 1,
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear
            };
            descriptor.msaaSamples = Mathf.Max(1, SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor));
            target = new RenderTexture(descriptor) { name = "Motion capture target" }; target.Create();
            Report.msaa = target.antiAliasing;
            if (target.antiAliasing > 1)
            {
                descriptor.msaaSamples = 1; descriptor.depthBufferBits = 0;
                resolved = new RenderTexture(descriptor) { name = "Motion capture resolved" }; resolved.Create();
            }
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            captureCamera.transform.position = motor.transform.position + Vector3.up * .9f + Report.cameraWorldOffset;
            captureCamera.transform.LookAt(motor.transform.position + Vector3.up * .9f);
            // Batch PlayMode has no Game view. Initialize the real URP before creating a VolumeStack.
            // This warm-up is not part of the 300 measured frames.
            if (!VolumeManager.instance.isInitialized)
                RenderPipeline.SubmitRenderRequest(captureCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            volumeStack = VolumeManager.instance.CreateStack(); cameraData.volumeStack = volumeStack;
        }

        void LateUpdate()
        {
            if (!Recording) return;
            try
            {
                int index = FrameCount;
                Vector3 position = motor.transform.position;
                Vector3 velocity = (position - previousPosition) / FrameDuration;
                previousPosition = position;
                Vector3 lookAt = position + Vector3.up * .9f;
                captureCamera.transform.position = lookAt + Report.cameraWorldOffset;
                captureCamera.transform.LookAt(lookAt);
                var frame = new MotionFrame
                {
                    index = index, unityFrame = Time.frameCount, sampleTime = index * FrameDuration,
                    deltaTime = Time.deltaTime, gameplayTime = Time.time, phase = phaseRecipe != null ? phaseRecipe(index) : PhaseFor(index),
                    requestedKeys = Keys(index).Select(k => k.ToString()).ToArray(),
                    gameplayState = motor.State.ToString(), grounded = motor.IsGrounded, actionLocked = motor.ActionLocked,
                    inputMove = input.Move, rootPosition = position, rootRotation = motor.transform.rotation,
                    measuredVelocity = velocity, measuredSpeed = new Vector2(velocity.x, velocity.z).magnitude,
                    requestedSpeed = motor.HorizontalSpeed, animatorSpeed = animator.speed,
                    cameraPosition = captureCamera.transform.position, lookAt = lookAt,
                    layers = CaptureLayers(), bones = CaptureBones()
                };
                frame.timeScale=Time.timeScale;
                frame.attackStep=combat.CurrentStep; frame.attackClock=combat.NormalizedTime;
                if (motionDriver != null)
                {
                    frame.visualYawOffset = motionDriver.VisualYawOffset;
                    frame.motionSpeed = animator.GetFloat(HumanAnimationDriver.SpeedParameter);
                    frame.strideRate = animator.GetFloat(HumanAnimationDriver.RateParameter);
                    frame.motionX = animator.GetFloat(HumanAnimationDriver.XParameter);
                    frame.motionZ = animator.GetFloat(HumanAnimationDriver.ZParameter);
                    frame.skinFeet = skinProbe.Measure();
                    frame.stopping=motionDriver.IsStopping; frame.stopNormalizedTime=motionDriver.StopNormalizedTime;
                    if(frame.attackStep>0) frame.attackParameter=animator.GetFloat("Attack"+frame.attackStep+"Time");
                    if(motor.State>=PlayerActionState.Skill) frame.actionParameter=animator.GetFloat(motor.State+"Time");
                }
                frame.finite = IsFinite(frame);
                if (!frame.finite) throw new InvalidOperationException("Non-finite transform/time at capture frame " + index);
                if (!allowTimeScale && Mathf.Abs(Time.deltaTime - FrameDuration) > .002f)
                    throw new InvalidOperationException("The deterministic 30fps clock changed at frame " + index + ": " + Time.deltaTime);
                CaptureImage(frame);
                Report.frames.Add(frame);
                if (FrameCount == TotalFrames)
                {
                    Recording = false;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    Persist();
                }
                else
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Keys(FrameCount)));
                    prepareFrame?.Invoke(FrameCount);
                }
            }
            catch (Exception exception)
            {
                Error = exception.ToString(); Recording = false;
                if (keyboard != null && keyboard.added) InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                try { Report.failure = Error; Persist(); } catch (Exception io) { Error += "\nPartial evidence write failed: " + io; }
            }
        }

        LayerSample[] CaptureLayers()
        {
            var layers = new LayerSample[animator.layerCount];
            for (int i = 0; i < layers.Length; i++)
            {
                var state = animator.GetCurrentAnimatorStateInfo(i);
                bool transitioning = animator.IsInTransition(i);
                var next = transitioning ? animator.GetNextAnimatorStateInfo(i) : default;
                layers[i] = new LayerSample
                {
                    index = i, layer = animator.GetLayerName(i), weight = i == 0 ? 1f : animator.GetLayerWeight(i),
                    stateHash = state.fullPathHash, normalizedTime = state.normalizedTime, transitioning = transitioning,
                    nextStateHash = next.fullPathHash, nextNormalizedTime = next.normalizedTime,
                    transitionNormalizedTime = transitioning ? animator.GetAnimatorTransitionInfo(i).normalizedTime : 0f,
                    currentClips = Clips(animator.GetCurrentAnimatorClipInfo(i)),
                    nextClips = transitioning ? Clips(animator.GetNextAnimatorClipInfo(i)) : Array.Empty<ClipSample>()
                };
            }
            return layers;
        }

        static ClipSample[] Clips(AnimatorClipInfo[] clips) => clips.Select(c => new ClipSample
        { name = c.clip != null ? c.clip.name : "<missing>", weight = c.weight, length = c.clip != null ? c.clip.length : 0f }).ToArray();

        BoneSample[] CaptureBones()
        {
            var samples = new List<BoneSample>(boneTransforms.Length);
            for (int i = 0; i < boneTransforms.Length; i++)
            {
                Transform bone = boneTransforms[i]; if (bone == null) continue;
                var sample = new BoneSample
                {
                    slot = TrackedBones[i].ToString(), transformName = bone.name, worldPosition = bone.position,
                    rootLocalPosition = motor.transform.InverseTransformPoint(bone.position), localPosition = bone.localPosition,
                    worldRotation = bone.rotation, localRotation = bone.localRotation
                };
                if (TrackedBones[i] == HumanBodyBones.LeftFoot || TrackedBones[i] == HumanBodyBones.RightFoot ||
                    TrackedBones[i] == HumanBodyBones.LeftToes || TrackedBones[i] == HumanBodyBones.RightToes)
                {
                    sample.floorHit = Physics.Raycast(bone.position + Vector3.up * .5f, Vector3.down, out var hit, 2f, 1 << 8, QueryTriggerInteraction.Ignore);
                    if (sample.floorHit)
                    {
                        sample.floorPoint = hit.point; sample.floorNormal = hit.normal;
                        sample.boneHeightAboveFloor = bone.position.y - hit.point.y;
                    }
                }
                samples.Add(sample);
            }
            return samples.ToArray();
        }

        void CaptureImage(MotionFrame frame)
        {
            RenderTexture previous = RenderTexture.active;
            VolumeStack previousStack = VolumeManager.instance.stack;
            try
            {
                VolumeManager.instance.Update(volumeStack, captureCamera.transform, cameraData.volumeLayerMask);
                // SingleCameraRequest skips UpdateVolumeFramework; URP's LUT reads the global stack.
                VolumeManager.instance.stack = volumeStack;
                RenderPipeline.SubmitRenderRequest(captureCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = previous;
                if (resolved != null) target.ResolveAntiAliasedSurface(resolved);
                RenderTexture.active = resolved != null ? resolved : target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(false, false);
                frame.imageSampleMin = 1f; frame.imageSampleMax = 0f;
                for (int i = 0; i < 24; i++)
                {
                    Color color = pixels.GetPixel((i * 137 + 83) % Width, (i * 73 + 149) % Height);
                    float luma = color.grayscale;
                    frame.imageSampleMin = Mathf.Min(frame.imageSampleMin, luma);
                    frame.imageSampleMax = Mathf.Max(frame.imageSampleMax, luma);
                }
                File.WriteAllBytes(Path.Combine(output, "frame_" + frame.index.ToString("D4") + ".jpg"), pixels.EncodeToJPG(95));
            }
            finally { RenderTexture.active = previous; VolumeManager.instance.stack = previousStack; }
        }

        void Persist() => File.WriteAllText(Path.Combine(output, "motion.json"), JsonUtility.ToJson(Report, true));

        public void StopAndRelease()
        {
            Recording = false;
            if (resourcesReleased) return;
            resourcesReleased = true;
            if (keyboard != null && keyboard.added) InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            if (cameraData != null) cameraData.volumeStack = null;
            if (volumeStack != null) { VolumeManager.instance.DestroyStack(volumeStack); volumeStack = null; }
            if (captureCamera != null) captureCamera.targetTexture = null;
            if (target != null) { target.Release(); Destroy(target); target = null; }
            if (resolved != null) { resolved.Release(); Destroy(resolved); resolved = null; }
            if (pixels != null) { Destroy(pixels); pixels = null; }
            skinProbe?.Dispose(); skinProbe = null;
        }
        void OnDestroy() => StopAndRelease();

        Key[] Keys(int index) => inputRecipe != null ? inputRecipe(index) : KeysFor(index);

        static Key[] KeysFor(int index)
        {
            if (index < 30) return Array.Empty<Key>();
            if (index < 90) return new[] { Key.W };
            if (index < 150) return new[] { Key.W, Key.LeftShift };
            if (index < 180) return Array.Empty<Key>();
            if (index < 210) return new[] { Key.A };
            if (index < 240) return new[] { Key.D };
            if (index == 240) return new[] { Key.W, Key.G };
            if (index < 270) return new[] { Key.W };
            return Array.Empty<Key>();
        }
        static string PhaseFor(int index) => index < 30 ? "Idle" : index < 90 ? "Walk" : index < 150 ? "Run" :
            index < 180 ? "Stop" : index < 210 ? "TurnLeft" : index < 240 ? "TurnRight" : index < 270 ? "SkillWhileWalking" : "FinalStop";

        static bool IsFinite(MotionFrame frame)
        {
            if (!Finite(frame.rootPosition) || !Finite(frame.rootRotation) || !Finite(frame.measuredVelocity) ||
                !Finite(frame.cameraPosition) || !Finite(frame.lookAt) || !Finite(frame.deltaTime) ||
                !Finite(frame.gameplayTime) || !Finite(frame.requestedSpeed) || !Finite(frame.animatorSpeed) ||
                !Finite(frame.inputMove.x) || !Finite(frame.inputMove.y)) return false;
            foreach (var bone in frame.bones)
                if (!Finite(bone.worldPosition) || !Finite(bone.rootLocalPosition) || !Finite(bone.localPosition) ||
                    !Finite(bone.worldRotation) || !Finite(bone.localRotation) || !Finite(bone.boneHeightAboveFloor)) return false;
            foreach (var layer in frame.layers)
            {
                if (!Finite(layer.weight) || !Finite(layer.normalizedTime) || !Finite(layer.nextNormalizedTime) ||
                    !Finite(layer.transitionNormalizedTime)) return false;
                if (layer.currentClips.Concat(layer.nextClips).Any(c => !Finite(c.weight) || !Finite(c.length))) return false;
            }
            if (!Finite(frame.visualYawOffset) || !Finite(frame.motionSpeed) || !Finite(frame.strideRate)) return false;
            if (frame.skinFeet != null && frame.skinFeet.Any(f => !Finite(f.minimumY) || !Finite(f.maximumY) ||
                !Finite(f.contact) || !Finite(f.calibratedSoleWorld) || f.pointsWorld.Any(p=>!Finite(p)))) return false;
            return true;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
    }
}
