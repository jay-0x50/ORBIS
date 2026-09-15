#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Orbis.Art;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    public static class CharacterMotionReview
    {
        [Serializable] public sealed class PoseSample
        {
            public float normalized, leftContact, rightContact;
            public Vector3 leftSole, rightSole, hips, leftHand, rightHand;
            public CharacterMotionSoleProbe.Measurement leftSkinSole, rightSkinSole;
            public bool finite;
        }
        [Serializable] public sealed class ClipReview
        {
            public string slot, clip;
            public bool humanoid;
            public float duration, authoredCycleDistance, previewScaleRatio;
            public PoseSample[] samples;
        }
        [Serializable] public sealed class Review
        {
            public string sourceManifest, candidateModelSha256, utc;
            public string scope = "Actual Unity Humanoid Animator samples of candidate clips, before gameplay driver activation. Positions are measured; no sole/contact quality success inferred.";
            public ClipReview[] clips;
        }
        [Serializable] public sealed class PreviewFrame
        {
            public int index;
            public string phase;
            public float normalizedTime;
            public Vector3 rootPosition, leftSole, rightSole, hips, leftHand, rightHand;
            public CharacterMotionSoleProbe.Measurement leftSkinSole, rightSkinSole;
            public float leftContact, rightContact;
            public bool finite;
        }
        [Serializable] public sealed class PreviewVideo
        {
            public int frameRate = 30, expectedFrameCount = 300, width = 960, height = 720;
            public string character, stage = "AuthoredClipReview", failure, sourceManifest;
            public string captureSource = "Actual Unity URP frames. Candidate Humanoid clips manually sampled through Animator at 30fps; native cycle translation is a visual review fixture, NOT actual PlayerMotor input or final After gameplay evidence.";
            public PreviewFrame[] frames;
        }
        sealed class Renderer : IDisposable
        {
            public readonly Camera Camera;
            readonly RenderTexture target, resolved;
            readonly Texture2D pixels;
            readonly bool previousAsync;
            public Renderer()
            {
                Camera = new GameObject("Clip review camera").AddComponent<Camera>();
                Camera.enabled = false; Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.14f,.16f,.20f);
                Camera.nearClipPlane = .03f; Camera.farClipPlane = 100f; Camera.fieldOfView = 28f; Camera.allowHDR = true; Camera.allowMSAA = true;
                Camera.useOcclusionCulling = false; Camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                target = new RenderTexture(960,720,24,RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                resolved = new RenderTexture(960,720,0,RenderTextureFormat.ARGB32); pixels = new Texture2D(960,720,TextureFormat.RGB24,false);
                target.Create(); resolved.Create(); previousAsync = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            }
            public void Capture(string path, Vector3 root, bool close = false)
            {
                Vector3 aim = root + Vector3.up * (close ? 1.25f : .9f);
                Vector3 position = close ? root + new Vector3(1.7f,1.4f,2.2f) : aim + new Vector3(3.2f,1.2f,4.5f);
                Camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(aim-position)); Camera.fieldOfView = close ? 22f : 28f;
                RenderTexture previous = RenderTexture.active; VolumeStack previousStack = null;
                try
                {
                    if (!VolumeManager.instance.isInitialized) RenderPipeline.SubmitRenderRequest(Camera,new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    Camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting); Camera.UpdateVolumeStack();
                    previousStack = VolumeManager.instance.stack; VolumeManager.instance.stack = Camera.GetUniversalAdditionalCameraData().volumeStack;
                    RenderPipeline.SubmitRenderRequest(Camera,new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    target.ResolveAntiAliasedSurface(resolved); RenderTexture.active = resolved;
                    pixels.ReadPixels(new Rect(0,0,960,720),0,0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToJPG(95));
                }
                finally { RenderTexture.active = previous; if (previousStack != null) VolumeManager.instance.stack = previousStack; }
            }
            public void Dispose()
            { ShaderUtil.allowAsyncCompilation = previousAsync; target.Release(); resolved.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(resolved); Object.DestroyImmediate(pixels); Object.DestroyImmediate(Camera.gameObject); }
        }

        public static void ReviewRequested()
        {
            string manifestPath = Argument("-motionManifest") ?? "Assets/Orbis/Game/Characters/MotionCandidates/Motion01/Polaris/MotionManifest.json";
            string run = Argument("-motionReviewRun") ?? "Review01";
            bool quick = string.Equals(Argument("-motionReviewQuick"),"true",StringComparison.OrdinalIgnoreCase);
            if (!run.All(c => char.IsLetterOrDigit(c) || c == '_')) throw new ArgumentException("Simple fresh review label required.");
            var manifest = JsonUtility.FromJson<CharacterMotionAuthor.Manifest>(File.ReadAllText(manifestPath));
            string output = "TestResults/CharacterPipeline/MotionReview/" + manifest.character + "/" + run;
            if (Directory.Exists(output)) throw new IOException("Preserve previous motion review evidence.");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var host = new GameObject("Authored motion review / no gameplay driver");
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(manifest.candidatePrefab),host.transform);
            Animator animator = model.GetComponentInChildren<Animator>(true);
            // Select actual sole mesh indices before Animator evaluates any candidate pose.
            var soleProbe = new CharacterMotionSoleProbe(animator);
            string controllerPath = manifest.output + "/Review.controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                foreach (var slot in manifest.clips)
                {
                    var state = controller.layers[0].stateMachine.AddState(slot.slot);
                    state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(slot.clip); state.writeDefaultValues = false;
                    if (slot.slot == "Idle") controller.layers[0].stateMachine.defaultState = state;
                }
                AssetDatabase.SaveAssets();
            }
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.Play("Idle",0,0); animator.Update(0);
            ArtCharacterRoster.NormalizeVisibleModelHeight(host,1.8f,Vector3.zero);
            float ratio = host.transform.lossyScale.y / manifest.finalModelScale;
            var catalog = Resources.Load<ArtAssetCatalog>("Art/Catalog");
            var entry = catalog.Explorer(manifest.character.ToLowerInvariant());
            if (entry.Weapon != null)
            {
                var weapon = Object.Instantiate(entry.Weapon,animator.GetBoneTransform(HumanBodyBones.RightHand));
                weapon.transform.localPosition = entry.WeaponLocalPosition; weapon.transform.localRotation = Quaternion.Euler(entry.WeaponLocalEuler); weapon.transform.localScale = entry.WeaponLocalScale;
            }
            ArtStyle.ApplyToHierarchy(host,catalog,true);
            foreach (var skin in host.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.updateWhenOffscreen = true;
                // Synchronous editor render requests may share a frame: explicitly refresh
                // skin matrices after each actual Animator pose, including outline renderers.
                skin.forceMatrixRecalculationPerRender = true;
            }
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.28f,.30f,.34f); RenderSettings.fog = false;
            var key = new GameObject("Review key").AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.1f;
            key.shadows = LightShadows.Soft; key.transform.rotation = Quaternion.Euler(42,-35,0); RenderSettings.sun = key;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Review floor"; floor.transform.position = new Vector3(0,-.05f,0); floor.transform.localScale = new Vector3(80,.1f,80);
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); floorMaterial.SetColor("_BaseColor",new Color(.32f,.36f,.40f)); floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
            var clips = new List<ClipReview>();
            try
            {
                using (var renderer = new Renderer())
                {
                    foreach (var slot in manifest.clips.Where(s => !quick || s.slot == "Idle" || s.slot == "Walk" || s.slot == "Run"))
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(slot.clip);
                        var samples = new List<PoseSample>();
                        for (int i = 0; i <= 60; i++)
                        {
                            host.transform.position = Vector3.zero; float t = i / 60f;
                            animator.Play(slot.slot,0,t); animator.Update(0);
                            PoseSample sample = Measure(animator,manifest,t,slot,soleProbe); samples.Add(sample);
                            if (!sample.finite) throw new InvalidOperationException("Nonfinite evaluated pose " + slot.slot);
                            if ((slot.slot == "Walk" || slot.slot == "Run" || slot.slot.StartsWith("Turn") || slot.slot.StartsWith("Stop")) && (i == 0 || i == 15 || i == 30 || i == 45))
                                renderer.Capture(output + "/" + slot.slot + "_" + i + ".jpg",host.transform.position);
                            if (slot.slot == "Idle" && i == 0) renderer.Capture(output + "/IdleHands.jpg",host.transform.position,true);
                            if (!quick && new[] { "Attack1","Attack2","Attack3","Skill","Burst","Hurt","Dead" }.Contains(slot.slot) && (i == 15 || i == 45))
                                renderer.Capture(output + "/" + slot.slot + "_" + i + ".jpg",host.transform.position);
                        }
                        clips.Add(new ClipReview { slot = slot.slot,clip = slot.clip,humanoid = clip.isHumanMotion,duration = clip.length,
                            authoredCycleDistance = slot.cycleDistanceMetres,previewScaleRatio = ratio,samples = samples.ToArray() });
                    }
                    File.WriteAllText(output + "/playback_review.json",JsonUtility.ToJson(new Review { sourceManifest = manifestPath,
                        candidateModelSha256 = manifest.sourceModelHash,utc = DateTime.UtcNow.ToString("o"),clips = clips.ToArray() },true));
                    if (quick)
                    {
                        Debug.Log("ORBIS_MOTION_QUICK_REVIEW " + output + " / Idle + Walk + Run actual samples/stills only");
                        return;
                    }
                    var video = new PreviewVideo { character = manifest.character,sourceManifest = manifestPath };
                    var frames = new List<PreviewFrame>(); Vector3 root = Vector3.zero;
                    string[] sections = { "Idle", "Walk", "Walk", "Run", "Run", "TurnLeft90", "TurnRight180", "WalkLeft", "WalkBack", "Skill" };
                    for (int i = 0; i < 300; i++)
                    {
                        string name = sections[i / 30]; var slot = manifest.clips.Single(s => s.slot == name);
                        int start = (i / 30) * 30; if (i / 30 == 2 || i / 30 == 4) start -= 30;
                        float seconds = (i-start)/30f; bool gait = slot.cycleDistanceMetres > 0f;
                        float normalized = gait || name == "Idle" ? Mathf.Repeat(seconds / slot.duration,1f) : Mathf.Clamp01(seconds / slot.duration);
                        if (gait) root += Direction(name) * (slot.cycleDistanceMetres * ratio / slot.duration / 30f);
                        host.transform.position = root; animator.Play(name,0,normalized); animator.Update(0);
                        var pose = Measure(animator,manifest,normalized,slot,soleProbe);
                        frames.Add(new PreviewFrame { index = i,phase = name,normalizedTime = normalized,rootPosition = root,
                            leftSole = pose.leftSole,rightSole = pose.rightSole,hips = pose.hips,leftHand = pose.leftHand,rightHand = pose.rightHand,finite = pose.finite,
                            leftSkinSole = pose.leftSkinSole,rightSkinSole = pose.rightSkinSole,leftContact = pose.leftContact,rightContact = pose.rightContact });
                        renderer.Capture(output + "/frame_" + i.ToString("D4") + ".jpg",root);
                    }
                    video.frames = frames.ToArray(); File.WriteAllText(output + "/motion.json",JsonUtility.ToJson(video,true));
                }
            }
            finally { soleProbe.Dispose(); Object.DestroyImmediate(host); Object.DestroyImmediate(key.gameObject); Object.DestroyImmediate(floor); Object.DestroyImmediate(floorMaterial); }
            Debug.Log("ORBIS_MOTION_REVIEW_COMPLETE " + output + " / actual Humanoid playback sampled; review quality before activation");
        }
        static PoseSample Measure(Animator animator, CharacterMotionAuthor.Manifest manifest,float t,CharacterMotionAuthor.Slot slot,CharacterMotionSoleProbe soleProbe)
        {
            var result = new PoseSample { normalized = t,leftContact = Evaluate(slot.leftPlant,t),rightContact = Evaluate(slot.rightPlant,t),
                leftSole = animator.GetBoneTransform(HumanBodyBones.LeftFoot).TransformPoint(manifest.leftSole.solePointInFootLocal),
                rightSole = animator.GetBoneTransform(HumanBodyBones.RightFoot).TransformPoint(manifest.rightSole.solePointInFootLocal),
                hips = animator.GetBoneTransform(HumanBodyBones.Hips).position,leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand).position,
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand).position };
            soleProbe.Measure(out result.leftSkinSole,out result.rightSkinSole);
            result.finite = new[] { result.leftSole,result.rightSole,result.hips,result.leftHand,result.rightHand,result.leftSkinSole.centroid,result.rightSkinSole.centroid }
                .All(v => !float.IsNaN(v.sqrMagnitude) && !float.IsInfinity(v.sqrMagnitude));
            return result;
        }
        static float Evaluate(CharacterMotionAuthor.CurvePoint[] points,float t)
        {
            if (points == null || points.Length == 0) return 0;
            for (int i = 1; i < points.Length; i++) if (points[i].time >= t) return Mathf.Lerp(points[i-1].value,points[i].value,Mathf.InverseLerp(points[i-1].time,points[i].time,t));
            return points[points.Length-1].value;
        }
        static Vector3 Direction(string name) => name.EndsWith("Left") ? Vector3.left : name.EndsWith("Right") ? Vector3.right : name.EndsWith("Back") ? Vector3.back : Vector3.forward;
        static string Argument(string name) { string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args,name); return index >= 0 && index+1 < args.Length ? args[index+1] : null; }
    }
}
#endif
