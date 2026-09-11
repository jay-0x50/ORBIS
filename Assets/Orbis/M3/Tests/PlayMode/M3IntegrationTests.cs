using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.VFX;

namespace Orbis.M3.Tests
{
    public sealed class M3IntegrationTests
    {
        private Scene loaded;
        private Keyboard keyboard;
        private Gamepad gamepad;
        private M3SceneBootstrap scene;
        private M3Presentation presentation;
        private M0Input input;
        private float savedCapture, savedScale, savedFixed;
        private bool savedBackground, savedCursorVisible;
        private CursorLockMode savedCursor;
        private InputSettings.EditorInputBehaviorInPlayMode savedEditorInput;
        private InputSettings.BackgroundBehavior savedInputBackground;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            savedCapture=Time.captureDeltaTime; savedScale=Time.timeScale; savedFixed=Time.fixedDeltaTime;
            savedBackground=Application.runInBackground; savedCursor=Cursor.lockState; savedCursorVisible=Cursor.visible;
            savedEditorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            savedInputBackground=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground=true; Time.captureDeltaTime=0f; Time.timeScale=1f;
            keyboard=InputSystem.AddDevice<Keyboard>(); gamepad=InputSystem.AddDevice<Gamepad>();
            Keys(); Pad(false);
            yield return Load("M3_EffectsPrototype");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (presentation != null) presentation.Ultimate.Cancel();
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
            Scene empty=SceneManager.CreateScene("M3 Test Cleanup");
            SceneManager.SetActiveScene(empty);
            if (loaded.IsValid() && loaded.isLoaded) yield return SceneManager.UnloadSceneAsync(loaded);
            Time.timeScale=savedScale; Time.fixedDeltaTime=savedFixed; Time.captureDeltaTime=savedCapture;
            Application.runInBackground=savedBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode=savedEditorInput;
            InputSystem.settings.backgroundBehavior=savedInputBackground;
            Cursor.lockState=savedCursor; Cursor.visible=savedCursorVisible;
        }

        [UnityTest]
        public IEnumerator FixedPoolReusesAndReinitializesWithoutGrowing()
        {
            M3VfxPool pool=presentation.Pool;
            pool.Clear(); pool.AutoTick=false;
            Assert.That(pool.Capacity, Is.EqualTo(40));
            int count=scene.GetComponentsInChildren<VisualEffect>(true).Length;
            var acquired=new HashSet<VisualEffect>();
            for(int i=0;i<32;i++)
                acquired.Add(pool.Play(M3EffectKind.Impact, Vector3.up, i%2==0 ? ElementType.Fire : ElementType.Water));
            Assert.That(acquired.Count, Is.EqualTo(12));
            Assert.That(pool.ActiveCount, Is.EqualTo(12));
            Assert.That(pool.RecycledWhileActive, Is.EqualTo(20));
            Assert.That(scene.GetComponentsInChildren<VisualEffect>(true).Length, Is.EqualTo(count));
            pool.Tick(3.1f);
            Assert.That(pool.ActiveCount, Is.Zero);
            foreach(VisualEffect item in acquired) Assert.That(item.gameObject.activeSelf, Is.False);
            var residue=pool.Play(M3EffectKind.Residue, Vector3.zero, ElementType.Wind);
            Assert.That(residue.GetVector4("PrimaryColor"), Is.EqualTo((Vector4)M3Palette.Primary(ElementType.Wind).linear));
            pool.Tick(3.99f); Assert.That(pool.ActiveCount, Is.EqualTo(1));
            pool.Tick(.02f); Assert.That(pool.ActiveCount, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AllFiveRealReactionsDriveGraphsAndShieldTracksActualActor()
        {
            ElementType[] seed={ElementType.Fire,ElementType.Water,ElementType.Fire,ElementType.Fire,ElementType.Fire};
            ElementType[] incoming={ElementType.Water,ElementType.Lightning,ElementType.Lightning,ElementType.Wind,ElementType.Rock};
            ReactionType[] expected={ReactionType.Vaporize,ReactionType.ElectroCharged,ReactionType.Overload,ReactionType.Swirl,ReactionType.Crystallize};
            for(int i=0;i<expected.Length;i++)
            {
                scene.Manager.ResetState(); presentation.ClearTransient();
                var target=scene.PrimaryTarget; var source=scene.Party.ActiveMember.Actor;
                target.transform.position=new Vector3(0f,0f,2f); Physics.SyncTransforms();
                scene.Manager.Apply(target,seed[i],source,0f,4f);
                var result=scene.Manager.Apply(target,incoming[i],source,10f,4f);
                Assert.That(result, Is.EqualTo(expected[i]));
                Assert.That(presentation.LastReaction, Is.EqualTo(expected[i]));
                Assert.That(presentation.Pool.PlayCount, Is.GreaterThan(0));
                Assert.That(target.DamageTaken, Is.GreaterThan(0f));
                yield return null;
            }
            Assert.That(presentation.MeshEffects.ShieldVisible, Is.True);
            Assert.That(scene.Party.ActiveMember.Actor.ShieldAmount, Is.EqualTo(25f));
            scene.Party.TrySwitch(1); yield return null;
            Assert.That(presentation.MeshEffects.ShieldVisible, Is.False);
            scene.Party.TrySwitch(0); yield return null;
            Assert.That(presentation.MeshEffects.ShieldVisible, Is.True);
            scene.Manager.Tick(6.1f); yield return null;
            Assert.That(presentation.MeshEffects.ShieldVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator ElectroChainFollowsOnlyValidatedDamageLinks()
        {
            var source=scene.Party.ActiveMember.Actor; var target=scene.PrimaryTarget;
            scene.Manager.Apply(target,ElementType.Water,source,0f,4f);
            scene.Manager.Apply(target,ElementType.Lightning,source,10f,4f);
            scene.Manager.AutoTick=false; scene.Manager.Tick(1f);
            Assert.That(presentation.MeshEffects.ActiveChains, Is.EqualTo(2));
            if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null) Capture(Camera.main,"M3_Chain.png");
            Assert.That(target.DamageTaken, Is.EqualTo(14f).Within(.001f));
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(presentation.MeshEffects.ActiveChains, Is.Zero);
            presentation.Pool.Clear();
            foreach(var actor in scene.GetComponentsInChildren<ElementalActor>())
                if(actor.Team==ActorTeam.Enemy && actor!=target) actor.IsOnField=false;
            scene.Manager.Tick(1f);
            Assert.That(presentation.MeshEffects.ActiveChains, Is.Zero);
        }

        [UnityTest]
        public IEnumerator BasicAttackAndPartyInputProduceHitsTrailsAndVaporize()
        {
            Assert.That(input.GameplayEnabled, Is.True);
            Pad(true); yield return null; Pad(false);
            yield return new WaitForSecondsRealtime(.22f);
            Assert.That(presentation.ComboCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(presentation.MeshEffects.TrailEmitting, Is.True);
            Assert.That(presentation.Feedback.LastImpulseAmplitude, Is.EqualTo(.1f));
            Assert.That(scene.PrimaryTarget.AuraElement, Is.EqualTo(ElementType.Fire));
            yield return Tap(Key.Digit2);
            Pad(true); yield return null; Pad(false);
            yield return new WaitForSecondsRealtime(.24f);
            Assert.That(scene.Party.ActiveIndex, Is.EqualTo(1));
            Assert.That(presentation.LastReaction, Is.EqualTo(ReactionType.Vaporize));
            Assert.That(presentation.AudioPlayCount, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator QPlaysFiveTimelineStagesAndRestoresCameraInputAndClock()
        {
            var stages=new List<M3UltimateStage>();
            presentation.Ultimate.StageChanged+=(stage,element,position)=>stages.Add(stage);
            var gameplay=scene.GetComponentInChildren<CinemachineCamera>();
            var brain=Camera.main.GetComponent<CinemachineBrain>();
            var originalBlend=brain.DefaultBlend;
            float damage=scene.PrimaryTarget.DamageTaken;
            yield return Tap(Key.Q);
            Assert.That(presentation.Ultimate.IsPlaying, Is.True);
            Assert.That(presentation.Ultimate.Director.timeUpdateMode, Is.EqualTo(DirectorUpdateMode.UnscaledGameTime));
            Assert.That(input.PresentationLocked, Is.True);
            yield return WaitForStage(M3UltimateStage.Finished, 4f);
            CollectionAssert.AreEqual(new[] { M3UltimateStage.CloseUp,M3UltimateStage.SlowMotion,M3UltimateStage.ElementBurst,
                M3UltimateStage.SoundImpact,M3UltimateStage.RestoreAndResidue,M3UltimateStage.Finished },stages);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(input.PresentationLocked, Is.False);
            Assert.That(presentation.Ultimate.CloseUpCamera.gameObject.activeSelf, Is.False);
            Assert.That(brain.DefaultBlend.Time, Is.EqualTo(originalBlend.Time));
            Assert.That(scene.PrimaryTarget.DamageTaken, Is.EqualTo(damage), "Q is presentation only.");
            Assert.That(presentation.Pool.LastPlayed, Is.EqualTo(M3EffectKind.Residue));
            Assert.That(presentation.Feedback.LastImpulseAmplitude, Is.EqualTo(.6f));
            yield return new WaitForSecondsRealtime(4.1f);
            Assert.That(presentation.Pool.ActiveCount, Is.Zero);
            Assert.That(presentation.PlayUltimatePresentation(), Is.True, "Timeline signals must replay after completion.");
            yield return WaitForStage(M3UltimateStage.SlowMotion, 2f);
            Assert.That(Time.timeScale, Is.EqualTo(.2f).Within(.001f));
        }

        [UnityTest]
        public IEnumerator EscapeAndDisableCancelWithoutLeakingGlobalState()
        {
            Assert.That(presentation.PlayUltimatePresentation(), Is.True);
            yield return WaitForStage(M3UltimateStage.SlowMotion, 2f);
            yield return Tap(Key.Escape);
            Assert.That(presentation.Ultimate.IsPlaying, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(input.PresentationLocked, Is.False);
            // Reacquire cursor using the same attack input contract as M0.
            Pad(true); yield return null; Pad(false); yield return Frames(3);
            Assert.That(presentation.PlayUltimatePresentation(), Is.True);
            yield return WaitForStage(M3UltimateStage.ElementBurst, 2f);
            presentation.enabled=false;
            yield return Frames(2);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(savedFixed));
            Assert.That(input.PresentationLocked, Is.False);
            Assert.That(presentation.Pool.ActiveCount, Is.Zero);
            Assert.That(presentation.MeshEffects.ActiveRings, Is.Zero);
            presentation.enabled=true; yield return Frames(3);
            Assert.That(presentation.PlayUltimatePresentation(), Is.True);
        }

        [UnityTest]
        public IEnumerator RockRosterAndExistingExplorationRemainPlayable()
        {
            yield return Load("M3_CrystallizeEffects");
            Assert.That(scene.Party.Members[3].Actor.Element, Is.EqualTo(ElementType.Rock));
            scene.Manager.Apply(scene.PrimaryTarget,ElementType.Wind,scene.Party.ActiveMember.Actor,0f,4f);
            yield return Tap(Key.Digit4);
            Pad(true); yield return null; Pad(false);
            yield return new WaitForSecondsRealtime(.24f);
            Assert.That(presentation.LastReaction, Is.EqualTo(ReactionType.Crystallize));
            Assert.That(scene.Party.ActiveMember.Actor.ShieldElement, Is.EqualTo(ElementType.Wind));
            yield return Load("M3_ExplorationEffects");
            Assert.That(scene.IsExploration, Is.True);
            var field=scene.GetComponentInChildren<M2SceneBootstrap>();
            Assert.That(field.Water, Is.Not.Null); Assert.That(field.Cliff, Is.Not.Null);
            Assert.That(field.FieldPuzzle, Is.Not.Null); Assert.That(field.Challenge, Is.Not.Null);
            yield return Tap(Key.F2);
            yield return new WaitForSecondsRealtime(.25f); // M2 teleport intentionally blocks re-grab for .2 s.
            yield return Tap(Key.E);
            Assert.That(field.Traversal.Mode, Is.EqualTo(ExplorationMode.Climbing));
            yield return Tap(Key.E);
            Assert.That(field.Traversal.Mode, Is.Not.EqualTo(ExplorationMode.Climbing));
        }

        [UnityTest]
        public IEnumerator RealGpuGraphsRenderAndReturnToPool()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null || !SystemInfo.supportsComputeShaders)
                Assert.Ignore("Real GPU VFX validation requires a graphics device supporting compute shaders.");
            presentation.Pool.AutoTick=false;
            var camera=Camera.main; var brain=camera.GetComponent<CinemachineBrain>();
            brain.enabled=false;
            camera.transform.SetPositionAndRotation(new Vector3(4f,3.5f,-4f),
                Quaternion.LookRotation(new Vector3(0f,1f,2f)-new Vector3(4f,3.5f,-4f)));
            foreach(M3EffectKind kind in System.Enum.GetValues(typeof(M3EffectKind)))
            {
                presentation.Pool.Clear();
                var vfx=presentation.Pool.Play(kind,new Vector3(0f,1f,1.7f),
                    kind==M3EffectKind.ElectroCharged ? ElementType.Lightning : kind==M3EffectKind.Vaporize ? ElementType.Water : ElementType.Fire);
                // Batch mode has no continuously painted Game view. Submit a real camera render each
                // frame so VFX culling/compute work runs before querying the asynchronous alive count.
                for (int frame = 0; frame < 10; frame++)
                {
                    Capture(camera,"M3_"+kind+".png");
                    yield return null;
                }
                Debug.Log("M3 GPU " + kind + " alive=" + vfx.aliveParticleCount + " culled=" + vfx.culled + " awake=" + vfx.HasAnySystemAwake());
                var systemNames = new List<string>();
                vfx.GetParticleSystemNames(systemNames);
                foreach (string system in systemNames)
                {
                    var info = vfx.GetParticleSystemInfo(system);
                    Debug.Log("M3 system " + system + " alive=" + info.aliveCount + " capacity=" + info.capacity + " sleeping=" + info.sleeping + " bounds=" + info.bounds);
                }
                Assert.That(vfx.aliveParticleCount, Is.GreaterThan(0), kind+" must emit actual GPU particles.");
                presentation.Pool.Tick(4.1f);
                Assert.That(presentation.Pool.ActiveCount, Is.Zero);
            }
            brain.enabled=true;
        }

        [UnityTest]
        public IEnumerator ShaderPresentationAndUltimateProduceReviewCaptures()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) Assert.Ignore("Graphics capture requires a device.");
            var source=scene.Party.ActiveMember.Actor;
            scene.Manager.Apply(scene.PrimaryTarget,ElementType.Water,source,0f,4f);
            scene.Manager.Apply(scene.PrimaryTarget,ElementType.Rock,source,10f,4f);
            yield return Frames(2);
            Capture(Camera.main,"M3_Shield.png");
            presentation.MeshEffects.EmitAfterimage(ElementType.Wind);
            presentation.MeshEffects.EmitRing(Vector3.forward,ElementType.Wind);
            Pad(true); yield return null; Pad(false);
            yield return new WaitForSecondsRealtime(.12f);
            Capture(Camera.main,"M3_Trail_Afterimage.png");
            Assert.That(presentation.PlayUltimatePresentation(),Is.True);
            yield return WaitForStage(M3UltimateStage.SlowMotion,2f);
            Capture(Camera.main,"M3_Ultimate_CloseUp.png");
            yield return WaitForStage(M3UltimateStage.ElementBurst,2f);
            presentation.Ultimate.Director.Pause();
            // Hold this authored stage just for review; normal five-stage playback is verified separately.
            for(int frame=0;frame<4;frame++) { Capture(Camera.main,"M3_Ultimate_Burst.png"); yield return null; }
            presentation.Ultimate.Cancel();
            Assert.That(presentation.Ultimate.Director.playableGraph.IsValid(),Is.False,"Cancelling a paused graph must dispose it.");
            Assert.That(Time.timeScale,Is.EqualTo(1f));
        }
        private IEnumerator Load(string name)
        {
            if(presentation!=null) presentation.Ultimate.Cancel();
            yield return SceneManager.LoadSceneAsync("Assets/Orbis/M3/Scenes/"+name+".unity",LoadSceneMode.Single);
            loaded=SceneManager.GetActiveScene();
            scene=Object.FindFirstObjectByType<M3SceneBootstrap>();
            Assert.That(scene, Is.Not.Null);
            presentation=scene.Presentation; input=scene.GetComponentInChildren<M0Input>();
            yield return Frames(4);
            yield return new WaitForSecondsRealtime(.32f);
        }

        private IEnumerator WaitForStage(M3UltimateStage target,float timeout)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+timeout;
            while(presentation.Ultimate.CurrentStage<target && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
            Assert.That(presentation.Ultimate.CurrentStage, Is.EqualTo(target));
        }
        private IEnumerator Tap(Key key) { Keys(key); yield return Frames(2); Keys(); yield return null; }
        private void Keys(params Key[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        private void Pad(bool attack)=>InputSystem.QueueStateEvent(gamepad,attack ? new GamepadState().WithButton(GamepadButton.West) : new GamepadState());
        private static IEnumerator Frames(int count) { for(int i=0;i<count;i++) yield return null; }

        private static void Capture(Camera camera,string name)
        {
            RenderTexture previous=RenderTexture.active, previousTarget=camera.targetTexture;
            float previousAspect=camera.aspect;
#if UNITY_EDITOR
            bool async=UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation=false;
#endif
            var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            Texture2D pixels=null;
            try
            {
                target.Create(); camera.aspect=1280f/720f;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;
                pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0f,0f,1280f,720f),0,0); pixels.Apply();
                string directory=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"..","TestResults"));
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory,name),pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous; camera.targetTexture=previousTarget; camera.aspect=previousAspect;
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation=async;
#endif
                if(pixels!=null) Object.Destroy(pixels);
                target.Release(); Object.Destroy(target);
            }
        }
    }
}
