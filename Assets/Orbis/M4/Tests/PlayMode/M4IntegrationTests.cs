using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Orbis.M4.Tests
{
    public sealed class M4IntegrationTests
    {
        private M4SceneBootstrap scene;
        private M4RegionRouter router;
        private M4ProgressService progress;
        private string saveDirectory;
        private Keyboard keyboard;
        private Gamepad gamepad;
        private float captureDelta, scale, fixedDelta;
        private bool background, cursorVisible;
        private CursorLockMode cursor;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private InputSettings.BackgroundBehavior inputBackground;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            captureDelta=Time.captureDeltaTime; scale=Time.timeScale; fixedDelta=Time.fixedDeltaTime;
            background=Application.runInBackground; cursor=Cursor.lockState; cursorVisible=Cursor.visible;
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            inputBackground=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground=true; Time.captureDeltaTime=0; Time.timeScale=1;
            keyboard=InputSystem.AddDevice<Keyboard>(); gamepad=InputSystem.AddDevice<Gamepad>();
            Keys(); Pad(false);
            saveDirectory=Path.Combine(Path.GetTempPath(),"OrbisM4-Integration-"+Guid.NewGuid().ToString("N"));
            progress=new M4ProgressService(Path.Combine(saveDirectory,"progress.json"),
                ()=>new DateTime(2026,9,10,3,0,0,DateTimeKind.Utc));
            M4Session.UseProgressForTests(progress);
            yield return SceneManager.LoadSceneAsync("Assets/Orbis/M4/Scenes/M4_Launcher.unity",LoadSceneMode.Single);
            router=M4RegionRouter.Instance;
            yield return AwaitRegion(M4RegionId.Agnia);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if(scene!=null) scene.Presentation.Ultimate.Cancel();
            var empty=SceneManager.CreateScene("M4 Cleanup");
            var current=SceneManager.GetActiveScene(); SceneManager.SetActiveScene(empty);
            if(current.IsValid()&&current.isLoaded&&current!=empty) yield return SceneManager.UnloadSceneAsync(current);
            if(router!=null) Object.Destroy(router.gameObject);
            if(keyboard!=null&&keyboard.added) InputSystem.RemoveDevice(keyboard);
            if(gamepad!=null&&gamepad.added) InputSystem.RemoveDevice(gamepad);
            M4Session.UseProgressForTests(null);
            Time.timeScale=scale; Time.fixedDeltaTime=fixedDelta; Time.captureDeltaTime=captureDelta;
            Application.runInBackground=background;
            InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
            InputSystem.settings.backgroundBehavior=inputBackground;
            Cursor.lockState=cursor; Cursor.visible=cursorVisible;
            // Only this fixture's GUID-named temporary directory is removed; real player saves are never opened.
            if(saveDirectory!=null&&Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory,true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FiveAddressableRegionsHavePlayablePuzzlesRoomsAndSafeArrivalPoints()
        {
            foreach(var profile in M4RegionCatalog.All)
            {
                if(scene.Region!=profile.Id) yield return Travel(profile.Id);
                Assert.That(Object.FindObjectsByType<M4SceneBootstrap>(FindObjectsSortMode.None).Length,Is.EqualTo(1));
                Assert.That(Object.FindObjectsByType<ElementalReactionManager>(FindObjectsSortMode.None).Length,Is.EqualTo(1));
                CollectionAssert.AreEqual(M4SceneBootstrap.Roster(profile.Id),scene.Party.Members.Select(x=>x.Actor.Element).ToArray());
                Assert.That(scene.Content.Statues.Length,Is.EqualTo(3));
                Assert.That(scene.Content.Targets.Length,Is.EqualTo(3));
                Assert.That(scene.Boss.Weakness,Is.EqualTo(profile.Weakness));
                foreach(var point in new[]{scene.Layout.Spawn,scene.Layout.Chest,scene.Layout.ChallengeEntry,
                    scene.Layout.BossCenter,scene.Layout.Npc,scene.Layout.Portal})
                {
                    Assert.That(Physics.Raycast(point+Vector3.up*1.5f,Vector3.down,out var hit,2f,1<<8),Is.True,profile.Id+" ground under "+point);
                    Assert.That(hit.normal.y,Is.GreaterThan(.7f));
                }
                SolvePuzzle();
                Assert.That(scene.Content.Puzzle.Model.IsUnlocked,Is.True);
                scene.Content.Puzzle.TryClaimReward(); scene.Content.Puzzle.ResetPuzzle();
                Assert.That(scene.Content.Puzzle.Model.IsUnlocked,Is.False,"M4 allows daily replay after material reward.");
                SolvePuzzle(); Assert.That(scene.Content.Puzzle.TryClaimReward(),Is.False,"Replay cannot duplicate materials.");
                CompleteRoom();
                scene.Content.Challenge.TryClaimReward(); scene.Content.Challenge.ResetChallenge();
                Assert.That(scene.Content.Challenge.TryStart(),Is.True,"All regional rooms remain replayable.");
                scene.Content.Challenge.ResetChallenge();
                if(profile.Id!=M4RegionId.Agnia&&SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null)
                    CaptureOverview(profile.Id);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator NpcDailyObjectivesTurnInAndWeeklyBossPersistAcrossTravelAndReload()
        {
            yield return Tap(Key.F5); yield return Tap(Key.F);
            Assert.That(progress.DailyQuests.All(x=>x.State==M4QuestState.Accepted),Is.True);
            var requests=progress.DailyQuests.Select(x=>x.Definition).ToArray();
            foreach(var request in requests)
            {
                if(scene.Region!=request.Region) yield return Travel(request.Region);
                switch(request.Kind)
                {
                    case M4ObjectiveKind.Survey: yield return Tap(Key.F6); break;
                    case M4ObjectiveKind.FieldPuzzle: SolvePuzzle(); break;
                    case M4ObjectiveKind.ChallengeRoom: CompleteRoom(); break;
                    case M4ObjectiveKind.FieldBoss:
                        scene.Boss.AutoTick=false;
                        scene.Manager.Apply(scene.Boss.Actor,scene.Boss.Weakness,scene.Party.ActiveMember.Actor,1000f);
                        Assert.That(progress.IsBossDefeated(request.Region),Is.True); break;
                }
                Assert.That(progress.DailyQuests.Single(x=>x.Definition.Id==request.Id).State,Is.EqualTo(M4QuestState.Completed));
            }
            yield return Tap(Key.F5); yield return Tap(Key.F);
            Assert.That(progress.DailyQuests.All(x=>x.State==M4QuestState.Claimed),Is.True);
            Assert.That(progress.Coins,Is.EqualTo(150));
            yield return Tap(Key.F); Assert.That(progress.Coins,Is.EqualTo(150));
            var restored=new M4ProgressService(progress.SavePath,()=>new DateTime(2026,9,10,3,0,0,DateTimeKind.Utc));
            Assert.That(restored.Coins,Is.EqualTo(150));
            Assert.That(restored.DailyQuests.All(x=>x.State==M4QuestState.Claimed),Is.True);
            var bossRegion=requests.Single(x=>x.Kind==M4ObjectiveKind.FieldBoss).Region;
            yield return Travel(bossRegion);
            Assert.That(scene.Boss.State,Is.EqualTo(M4BossState.Defeated));
            Assert.That(scene.Boss.Actor.IsOnField,Is.False);
            scene.ResetEncounter(); Assert.That(scene.Boss.State,Is.EqualTo(M4BossState.Defeated));
            Assert.That(progress.Coins,Is.EqualTo(150));
        }

        [UnityTest]
        public IEnumerator BasicAttackWeaknessExposureTelegraphAndRescueUseExistingCombat()
        {
            scene.Boss.AutoTick=false;
            yield return Tap(Key.Digit2);
            scene.Warp(scene.Layout.BossCenter+Vector3.back*1.45f+Vector3.up*.1f);
            yield return new WaitForSecondsRealtime(.3f);
            float before=scene.Boss.HitPoints;
            Pad(true); yield return null; Pad(false);
            yield return new WaitForSecondsRealtime(.65f);
            Assert.That(scene.Boss.HitPoints,Is.LessThan(before),"M0 collider query must hit the M4 boss.");
            Assert.That(scene.Boss.WeakHitCount,Is.EqualTo(1));
            var source=scene.Party.ActiveMember.Actor;
            scene.Manager.Apply(scene.Boss.Actor,ElementType.Water,source,10f);
            scene.Manager.Apply(scene.Boss.Actor,ElementType.Water,source,10f);
            Assert.That(scene.Boss.State,Is.EqualTo(M4BossState.Exposed));
            float exposed=scene.Boss.HitPoints;
            scene.Manager.Apply(scene.Boss.Actor,ElementType.Water,source,10f);
            Assert.That(exposed-scene.Boss.HitPoints,Is.EqualTo(20f).Within(.001f));
            scene.Presentation.ClearTransient();
            scene.Boss.ResetEncounter(false); scene.Boss.Tick(0f);
            Assert.That(scene.Boss.TelegraphVisible,Is.True);
            if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null) Capture(Camera.main,"M4_Boss_Telegraph.png");
            scene.Vitals.Restore(5f); scene.Boss.Tick(scene.Boss.Profile.TelegraphDuration);
            Assert.That(scene.Vitals.IsKnockedOut,Is.True);
            yield return null; yield return null;
            Assert.That(scene.Vitals.Health,Is.EqualTo(100f));
            Assert.That(Vector3.Distance(scene.Traversal.transform.position,scene.Layout.Spawn),Is.LessThan(.4f));
            Assert.That(scene.Boss.HitPoints,Is.EqualTo(scene.Boss.MaximumHitPoints));
            scene.Boss.enabled=false; yield return null;
            Assert.That(scene.Boss.Actor.IncomingDamageScale,Is.Null);
            Assert.That(scene.Boss.Actor.IsOnField,Is.False);
            Assert.That(scene.Boss.GetComponent<Collider>().enabled,Is.False);
            scene.Boss.enabled=true; yield return null;
            Assert.That(scene.Boss.Actor.IsOnField,Is.True);
        }

        [UnityTest]
        public IEnumerator TravelMenuInputAndTraversalRetainHealthStaminaAndExistingCharacter()
        {
            scene.Vitals.Restore(63f); scene.Traversal.Stamina.SetCurrent(45f);
            yield return Tap(Key.Digit2);
            yield return Tap(Key.F10);
            Assert.That(scene.TravelMenuOpen,Is.True);
            Assert.That(scene.GetComponentInChildren<M0Input>().PresentationLocked,Is.True);
            yield return Tap(Key.DownArrow); yield return Tap(Key.Enter);
            yield return AwaitRegion(M4RegionId.Teluna);
            Assert.That(scene.GetComponentInChildren<M0Input>().GameplayEnabled,Is.True);
            Assert.That(scene.Vitals.Health,Is.EqualTo(63f));
            Assert.That(scene.Traversal.Stamina.Current,Is.InRange(45f,80f));
            Assert.That(scene.Party.ActiveMember.Actor.Element,Is.EqualTo(ElementType.Water));
            scene.Warp(new Vector3(12f,-1.2f,6f));
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(scene.Traversal.Mode,Is.EqualTo(ExplorationMode.Swimming));
            scene.Warp(new Vector3(0f,8f,-7f));
            yield return new WaitForSecondsRealtime(.3f); yield return Tap(Key.Space);
            Assert.That(scene.Traversal.Mode,Is.EqualTo(ExplorationMode.Gliding));
            yield return Tap(Key.F1); yield return Tap(Key.Q);
            Assert.That(scene.Presentation.Ultimate.IsPlaying,Is.True);
            yield return Travel(M4RegionId.Granite);
            Assert.That(Time.timeScale,Is.EqualTo(1f));
            Assert.That(scene.GetComponentInChildren<M0Input>().PresentationLocked,Is.False);
            Assert.That(scene.Party.ActiveMember.Actor.Element,Is.EqualTo(ElementType.Water));
        }

        [UnityTest]
        public IEnumerator ClaimingAnEarlierSolutionDoesNotCompleteANewDailyRequest()
        {
            var requests=progress.DailyQuests.Select(x=>x.Definition)
                .Where(x=>x.Kind==M4ObjectiveKind.FieldPuzzle||x.Kind==M4ObjectiveKind.ChallengeRoom).ToArray();
            foreach(var request in requests)
            {
                if(scene.Region!=request.Region) yield return Travel(request.Region);
                // Explicitly start with a fresh material ledger because other fixture cases also replay regions.
                if(request.Kind==M4ObjectiveKind.FieldPuzzle)
                    scene.Content.Puzzle.Configure(scene.Manager,scene.Content.Statues,new RewardInventory(),
                        "M4.Test.PreAcceptPuzzle",scene.Content.PuzzleElement,true);
                else
                    {
                    foreach(var target in scene.Content.Targets) target.GetComponent<Collider>().enabled=true;
                    scene.Content.Challenge.Configure(scene.Manager,scene.Content.Targets,new RewardInventory(),
                        "M4.Test.PreAcceptRoom",scene.Content.Challenge.Model.RequiredReaction,true);
                }
                if(request.Kind==M4ObjectiveKind.FieldPuzzle) SolvePuzzle(); else CompleteRoom();
                Assert.That(progress.Accept(request.Id),Is.True);
                if(request.Kind==M4ObjectiveKind.FieldPuzzle) scene.Content.Puzzle.TryClaimReward();
                else scene.Content.Challenge.TryClaimReward();
                Assert.That(progress.DailyQuests.Single(x=>x.Definition.Id==request.Id).State,Is.EqualTo(M4QuestState.Accepted));
                if(request.Kind==M4ObjectiveKind.FieldPuzzle) {scene.Content.Puzzle.ResetPuzzle();SolvePuzzle();}
                else {scene.Content.Challenge.ResetChallenge();CompleteRoom();}
                Assert.That(progress.DailyQuests.Single(x=>x.Definition.Id==request.Id).State,Is.EqualTo(M4QuestState.Completed));
            }
        }
        private void SolvePuzzle()
        {
            foreach(var statue in scene.Content.Statues)
                scene.Manager.Apply(statue,scene.Content.PuzzleElement,scene.Party.ActiveMember.Actor,10f);
        }
        private void CompleteRoom()
        {
            var room=scene.Content.Challenge;
            Assert.That(room.TryStart(),Is.True);
            Assert.That(scene.Content.Targets.All(x=>x.IsOnField&&x.GetComponent<Collider>().enabled),Is.True,"Legacy M2 reconfiguration must restore hit colliders.");
            var reaction=room.Model.RequiredReaction;
            ElementType seed=reaction==ReactionType.ElectroCharged?ElementType.Water:ElementType.Fire;
            ElementType incoming=reaction==ReactionType.Vaporize?ElementType.Water:
                reaction==ReactionType.Swirl?ElementType.Wind:reaction==ReactionType.Crystallize?ElementType.Rock:ElementType.Lightning;
            foreach(var target in scene.Content.Targets)
            {
                scene.Manager.Apply(target,seed,scene.Party.ActiveMember.Actor,0f);
                scene.Manager.Apply(target,incoming,scene.Party.ActiveMember.Actor,10f);
            }
            Assert.That(room.State,Is.EqualTo(ChallengeState.Completed));
            Assert.That(scene.Content.Targets.All(x=>!x.IsOnField&&!x.GetComponent<Collider>().enabled),Is.True);
        }
        private IEnumerator Travel(M4RegionId id)
        {
            Assert.That(router.Travel(id),Is.True);
            Assert.That(router.Travel(id),Is.False,"Concurrent travel is rejected.");
            yield return AwaitRegion(id);
        }
        private IEnumerator AwaitRegion(M4RegionId id)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+30f;
            do { yield return null; scene=Object.FindFirstObjectByType<M4SceneBootstrap>(); }
            while((router.IsLoading||scene==null||scene.Region!=id)&&Time.realtimeSinceStartupAsDouble<deadline);
            Assert.That(router.LastError,Is.Null);
            Assert.That(router.IsLoading,Is.False);
            Assert.That(scene,Is.Not.Null); Assert.That(scene.Region,Is.EqualTo(id));
            yield return new WaitForSecondsRealtime(.35f);
        }
        private IEnumerator Tap(Key key) { Keys(key); yield return null; yield return null; Keys(); yield return null; }
        private void Keys(params Key[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        private void Pad(bool attack)=>InputSystem.QueueStateEvent(gamepad,attack?new GamepadState().WithButton(GamepadButton.West):new GamepadState());
        private static void CaptureOverview(M4RegionId id)
        {
            var go=new GameObject("M4 Review Camera"); var camera=go.AddComponent<Camera>(); camera.enabled=false;
            camera.GetUniversalAdditionalCameraData(); camera.farClipPlane=220; camera.fieldOfView=55;
            camera.transform.position=new Vector3(53,58,-49);
            camera.transform.LookAt(new Vector3(0,id==M4RegionId.Granite?5:0,20));
            try { Capture(camera,"M4_"+id+".png"); } finally { Object.Destroy(go); }
        }
        private static void Capture(Camera camera,string name)
        {
            var previous=RenderTexture.active; var previousTarget=camera.targetTexture; float aspect=camera.aspect;
#if UNITY_EDITOR
            bool async=UnityEditor.ShaderUtil.allowAsyncCompilation; UnityEditor.ShaderUtil.allowAsyncCompilation=false;
#endif
            var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            Texture2D pixels=null;
            try
            {
                target.Create(); camera.aspect=1280f/720f;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target; pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,1280,720),0,0); pixels.Apply();
                var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..","TestResults")); Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory,name),pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous; camera.targetTexture=previousTarget; camera.aspect=aspect;
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation=async;
#endif
                if(pixels!=null) Object.Destroy(pixels); target.Release(); Object.Destroy(target);
            }
        }
    }
}
