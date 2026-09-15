using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.Art;
using Orbis.Game.World;
using Orbis.M0;
using Orbis.M4;
using Orbis.M16;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Orbis.Game.Tests
{
    /// <summary>
    /// Explicit opt-in evidence: run this fixture with -motionStage Before|After.
    /// Uses the actual saved Field runtime and player clocks; never manually poses an avatar.
    /// This measures/captures a baseline, not an assertion that the baseline motion is good.
    /// </summary>
    public sealed class CharacterMotionCaptureTests
    {
        static readonly string DefaultRunId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        static readonly Vector3 FloorCenter = new Vector3(3000f, 100f, 3000f);
        string stage, runId, profileDirectory, candidateRoot, requestedCharacter;
        bool initialized, runInBackground, cursorVisible, candidateMotion;
        float timeScale, fixedDelta, captureDelta;
        CursorLockMode cursorLock;
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
        InputSettings.BackgroundBehavior backgroundInput;
        Keyboard keyboard;
        M4ProgressService progress;
        M4SceneBootstrap world;
        CharacterMotionRecorder recorder;
        readonly List<Material> materials = new List<Material>();
#if UNITY_EDITOR
        bool asyncCompilation;
        CharacterMotionCandidateOverride candidateOverride;
#endif

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            stage = Argument("-motionStage");
            if (stage == null)
                Assert.Ignore("Motion evidence is opt-in. Supply -motionStage Before or -motionStage After.");
            Assert.That(stage == "Before" || stage == "After", Is.True, "-motionStage accepts Before or After only.");
            runId = Argument("-motionRun") ?? DefaultRunId;
            Assert.That(runId.Length, Is.InRange(1, 64));
            Assert.That(runId.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'), Is.True,
                "-motionRun is a label, not a path.");
            candidateRoot = Argument("-motionCandidateRoot");
            string motionMode = Argument("-motionCandidateMotion");
            Assert.That(motionMode == null || motionMode == "true", Is.True, "-motionCandidateMotion accepts true or omit for unchanged baseline controller.");
            candidateMotion = motionMode == "true";
            Assert.That(!candidateMotion || candidateRoot != null && stage == "After", Is.True, "Reviewed motion override requires After and a candidate root.");
            requestedCharacter = Argument("-motionCharacter");
            Assert.That(requestedCharacter == null || requestedCharacter == "Stella" || requestedCharacter == "Polaris", Is.True,
                "-motionCharacter accepts Stella or Polaris; omit to capture both.");
#if !UNITY_EDITOR
            if (candidateRoot != null) Assert.Ignore("Candidate asset override requires an Editor PlayMode test; it never modifies a player build.");
#endif
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "A graphics-enabled Unity run is required; do not pass -nographics.");

            timeScale = Time.timeScale; fixedDelta = Time.fixedDeltaTime; captureDelta = Time.captureDeltaTime;
            runInBackground = Application.runInBackground; cursorLock = Cursor.lockState; cursorVisible = Cursor.visible;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            backgroundInput = InputSystem.settings.backgroundBehavior;
            initialized = true;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true;
            Time.timeScale = 1f; Time.captureDeltaTime = CharacterMotionRecorder.FrameDuration;
#if UNITY_EDITOR
            asyncCompilation = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
#endif
            keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            profileDirectory = Path.Combine(Path.GetTempPath(), "Orbis-Motion-Capture-" + Guid.NewGuid().ToString("N"));
            progress = new M4ProgressService(Path.Combine(profileDirectory, "profile.json"));
            ExplorerJourney.Stop();
            M4Session.UseProgressForTests(progress);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (!initialized) yield break;
#if UNITY_EDITOR
            // Restore the exact original array before any scene cleanup can fail. Spawned views own
            // their already instantiated visual; no production asset is ever marked dirty or saved.
            candidateOverride?.Dispose(); candidateOverride = null;
#endif
            if (recorder != null) recorder.StopAndRelease();
            if (keyboard != null && keyboard.added) InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            if (world != null && world.Presentation != null) world.Presentation.Ultimate.Cancel();
            ExplorerJourney.Stop();

            SceneCleanup();
            yield return null;
            // The streamer releases its actual Addressables handles on disable. Wait for ownership cleanup;
            // do not unload its scenes independently while a handle release is pending.
            float deadline = Time.realtimeSinceStartup + 60f;
            while (WorldRegionStreamer.OwnedSceneHandleCount != 0 && Time.realtimeSinceStartup < deadline)
                yield return null;

            M4Session.UseProgressForTests(null);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            foreach (var material in materials) if (material != null) Object.Destroy(material);
            materials.Clear();
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            InputSystem.settings.backgroundBehavior = backgroundInput;
            Time.timeScale = timeScale; Time.fixedDeltaTime = fixedDelta; Time.captureDeltaTime = captureDelta;
            Application.runInBackground = runInBackground;
            Cursor.lockState = cursorLock; Cursor.visible = cursorVisible;
#if UNITY_EDITOR
            UnityEditor.ShaderUtil.allowAsyncCompilation = asyncCompilation;
#endif
            if (!string.IsNullOrEmpty(profileDirectory))
            {
                string resolved = Path.GetFullPath(profileDirectory);
                Assert.That(Path.GetFileName(resolved), Does.StartWith("Orbis-Motion-Capture-"));
                Assert.That(Path.GetDirectoryName(resolved),
                    Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
            Assert.That(WorldRegionStreamer.OwnedSceneHandleCount, Is.Zero, "Motion capture must release the field scenery handles.");
        }

        void SceneCleanup()
        {
            Scene previous = SceneManager.GetActiveScene();
            // A filtered-out character may never load Island, leaving the previous cleanup
            // scene active. Unique names also make repeated opt-in fixtures safe to tear down.
            Scene empty = SceneManager.CreateScene("Character motion capture cleanup " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            if (previous.IsValid() && previous.isLoaded) SceneManager.UnloadSceneAsync(previous);
            var router = Object.FindAnyObjectByType<M4RegionRouter>();
            if (router != null) Object.Destroy(router.gameObject);
        }

        [UnityTest]
        public IEnumerator CaptureStellaLocomotionBeforeOrAfter()
        { yield return Capture(ExplorerChoice.Stella, "stella", "Stella"); }

        [UnityTest]
        public IEnumerator CapturePolarisLocomotionBeforeOrAfter()
        { yield return Capture(ExplorerChoice.Polaris, "polaris", "Polaris"); }

        IEnumerator Capture(ExplorerChoice choice, string sourceId, string displayName)
        {
            if (requestedCharacter != null && requestedCharacter != displayName)
                Assert.Ignore("This opt-in capture selects only " + requestedCharacter + ".");
            string output = Path.Combine("TestResults", "CharacterPipeline", "Motion", stage, runId, displayName);
            Assert.That(Directory.Exists(output), Is.False,
                "Preserve previous motion evidence. Choose a fresh -motionRun label to repeat a capture.");
            Assert.That(progress.TrySelectExplorer(choice), Is.True);
#if UNITY_EDITOR
            if (candidateRoot != null)
                candidateOverride = new CharacterMotionCandidateOverride(Resources.Load<ArtAssetCatalog>("Art/Catalog"),
                    candidateRoot, sourceId, displayName, candidateMotion);
#endif
            yield return SceneManager.LoadSceneAsync(ExplorerJourney.IslandScene, LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(1.25f);
            for (int i = 0; i < 60; i++) yield return null;

            world = Object.FindAnyObjectByType<M4SceneBootstrap>();
            Assert.That(world, Is.Not.Null);
            Assert.That(world.IsInitialized, Is.True);
            var presentation = world.GetComponent<ArtScenePresentation>();
            Assert.That(presentation, Is.Not.Null);
            var art = presentation.Characters;
            Assert.That(art, Is.Not.Null);
            Assert.That(art.PermanentSourceId, Is.EqualTo(sourceId));
            Assert.That(art.ActiveVisual, Is.SameAs(art.PermanentVisual));
            var animator = art.ActiveAnimator;
            Assert.That(animator.isHuman && animator.avatar != null && animator.avatar.isValid, Is.True);
            Assert.That(animator.applyRootMotion, Is.False);
#if UNITY_EDITOR
            candidateOverride?.ValidateSpawn(animator);
#endif
            var motor = world.Traversal.GetComponent<PlayerMotor>();
            var input = motor.GetComponent<M0Input>();
            var explorer = ExplorerJourney.Current;
            Assert.That(explorer, Is.Not.Null);

            // Test-only ground outside the continent. The saved scene, colliders, authored content and
            // all normal player/party/combat components remain untouched. Identical marker spacing
            // and a world-oriented follow camera expose sliding and actual direction changes.
            CreateGround(Resources.Load<ArtAssetCatalog>("Art/Catalog"));
            world.Traversal.Teleport(FloorCenter + Vector3.up * .03f);
            motor.transform.rotation = Quaternion.identity;
            var orbit = Object.FindAnyObjectByType<M0CameraRig>();
            Assert.That(orbit, Is.Not.Null);
            orbit.SetOrbit(0f, 15f);
            input.SetPresentationLocked(false);
            Physics.SyncTransforms();
            for (int i = 0; i < 35; i++) yield return null;
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(input.GameplayEnabled, Is.True);
            Assert.That(explorer.IsCasting, Is.False);
            Assert.That(explorer.CooldownRemaining, Is.EqualTo(0f));

            int skillStarted = 0;
            Action<ExplorerCast> onSkill = _ => skillStarted++;
            explorer.SkillStarted += onSkill;
            try
            {
                var recorderObject = new GameObject("Motion capture recorder / test only");
                recorder = recorderObject.AddComponent<CharacterMotionRecorder>();
                recorder.Begin(stage, runId, displayName, output, animator, motor, input, keyboard, FloorCenter);
                float deadline = Time.realtimeSinceStartup + 1800f;
                while (recorder.Recording && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(recorder.Recording, Is.False, "The 300-frame capture did not finish within 30 minutes.");
                Assert.That(recorder.Error, Is.Null.Or.Empty, recorder.Error);
                Assert.That(recorder.FrameCount, Is.EqualTo(CharacterMotionRecorder.TotalFrames));
                Assert.That(skillStarted, Is.EqualTo(1), "G must trigger the actual protagonist skill exactly once.");
                Assert.That(recorder.Report.frames.Any(f => f.gameplayState == PlayerActionState.Skill.ToString()), Is.True);
                Assert.That(recorder.Report.frames.All(f => f.finite), Is.True, "All measured transforms must be finite.");
                Assert.That(recorder.Report.frames.Any(f => f.inputMove.sqrMagnitude > .5f && f.measuredSpeed > .5f), Is.True,
                    "The clip must depict actual controlled movement, not animation on a stationary pawn.");
                Assert.That(recorder.Report.frames.Any(f => f.phase == "Run" && f.measuredSpeed > 4f), Is.True);
                Assert.That(recorder.Report.frames.Last().measuredSpeed, Is.LessThan(.1f), "The final stop must settle.");
                Assert.That(Directory.GetFiles(output, "frame_*.jpg").Length, Is.EqualTo(CharacterMotionRecorder.TotalFrames));
                Assert.That(File.Exists(Path.Combine(output, "motion.json")), Is.True);
                if(candidateMotion)
                {
                    // A finite skeleton alone failed to catch After01's folded IK legs. Check
                    // actual skin support against this known flat floor in stable straight gait.
                    var contactFeet=recorder.Report.frames.Where(f=>(f.index>=45 && f.index<90 || f.index>=110 && f.index<150))
                        .SelectMany(f=>f.skinFeet).Where(f=>f.contact>=.95f).ToArray();
                    Assert.That(contactFeet.Length,Is.GreaterThan(15));
                    Assert.That(contactFeet.All(f=>f.minimumY>=FloorCenter.y-.025f && f.minimumY<=FloorCenter.y+.04f),Is.True,
                        "Measured planted shoe soles must stay near the flat stage; this is not an artistic/contact-slide acceptance claim.");
                }
#if UNITY_EDITOR
                candidateOverride?.WriteEvidence(output);
#endif
                if (candidateMotion && Argument("-motionActions") == "true")
                {
                    recorder.StopAndRelease();
                    yield return CharacterMotionActionChecks.Run(world,animator,motor,input,keyboard,stage,runId,displayName,Path.Combine(output,"Actions"),FloorCenter);
                }
                if (candidateMotion && Argument("-motionTerrain") == "true")
                {
                    recorder.StopAndRelease();
                    yield return CharacterMotionTerrainChecks.Run(world,animator,motor,input,keyboard,stage,runId,
                        displayName,Path.Combine(output,"Terrain"),materials[0]);
                }
                if (candidateMotion && Argument("-motionTerrainLifecycle") == "true")
                {
                    recorder.StopAndRelease();
                    yield return CharacterMotionTerrainLifecycleChecks.Run(world,animator,motor,input,keyboard,
                        displayName,Path.Combine(output,"TerrainLifecycle"),materials[0],FloorCenter);
                }
                Debug.Log("ORBIS_MOTION_CAPTURE_COMPLETE " + Path.GetFullPath(output));
            }
            finally { explorer.SkillStarted -= onSkill; }
        }

        void CreateGround(ArtAssetCatalog catalog)
        {
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.DefaultToonMaterial, Is.Not.Null);
            var groundMaterial = new Material(catalog.DefaultToonMaterial) { name = "Motion test neutral floor" };
            groundMaterial.SetColor("_BaseColor", new Color(.30f, .34f, .39f, 1f));
            groundMaterial.SetTexture("_BaseMap", null);
            var markerMaterial = new Material(groundMaterial) { name = "Motion test two-metre markers" };
            markerMaterial.SetColor("_BaseColor", new Color(.65f, .67f, .67f, 1f));
            materials.Add(groundMaterial); materials.Add(markerMaterial);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Motion capture test floor / 80 m / top y 100";
            ground.layer = 8;
            ground.transform.position = FloorCenter - Vector3.up * .25f;
            ground.transform.localScale = new Vector3(80f, .5f, 80f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            for (int i = -20; i <= 20; i++)
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    line.name = "Motion two metre grid " + axis + " / " + i;
                    line.layer = 8;
                    line.transform.SetParent(ground.transform, true);
                    line.transform.position = FloorCenter + new Vector3(axis == 0 ? i * 2f : 0f, .005f, axis == 1 ? i * 2f : 0f);
                    // Set world dimensions before parenting would inherit the scaled floor; these local scales compensate explicitly.
                    line.transform.localScale = axis == 0 ? new Vector3(.018f / 80f, .01f / .5f, 1f) : new Vector3(1f, .01f / .5f, .018f / 80f);
                    line.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                    line.GetComponent<Collider>().enabled = false;
                }
            }
        }

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return args[i].Substring(name.Length + 1);
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                Assert.That(i + 1, Is.LessThan(args.Length), "Supply a value after " + name);
                return args[i + 1];
            }
            return null;
        }
    }
}
