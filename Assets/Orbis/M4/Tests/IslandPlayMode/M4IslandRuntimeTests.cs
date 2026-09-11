using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Orbis.M16;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Orbis.M4.Tests
{
    /// <summary>Runtime integration only. Island geometry and screenshots belong to the Game scene tests.</summary>
    public sealed class M4IslandRuntimeTests
    {
        const string IslandScene = "Assets/Orbis/Game/Scenes/Orbis_Island.unity";
        M4SceneBootstrap world;
        M4ProgressService progress;
        M4RegionRouter router;
        string directory;
        DateTime clock;
        Keyboard keyboard;
        bool background, cursorVisible;
        CursorLockMode cursor;
        float scale, fixedDelta, captureDelta;
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
        InputSettings.BackgroundBehavior backgroundInput;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scale=Time.timeScale; fixedDelta=Time.fixedDeltaTime; captureDelta=Time.captureDeltaTime;
            background=Application.runInBackground; cursor=Cursor.lockState; cursorVisible=Cursor.visible;
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            backgroundInput=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground=true; Time.timeScale=1f; Time.captureDeltaTime=1f/60f;
            keyboard=InputSystem.AddDevice<Keyboard>(); Keys();
            directory=Path.Combine(Path.GetTempPath(),"Orbis-Island-Runtime-"+Guid.NewGuid().ToString("N"));
            clock=new DateTime(2026,9,10,3,0,0,DateTimeKind.Utc);
            progress=new M4ProgressService(Path.Combine(directory,"profile.json"),()=>clock);
            ExplorerJourney.Stop(); M4Session.UseProgressForTests(progress);
            Assert.That(progress.TrySelectExplorer(ExplorerChoice.Stella),Is.True);
            yield return SceneManager.LoadSceneAsync(IslandScene,LoadSceneMode.Single);
            yield return Frames(8);
            world=Object.FindAnyObjectByType<M4SceneBootstrap>();
            Assert.That(world,Is.Not.Null); Assert.That(world.IsInitialized,Is.True);
            Assert.That(world.IsIsland,Is.True);
            router=M4RegionRouter.Instance;
            world.Manager.AutoTick=false;
            foreach(var context in world.Regions) context.Boss.AutoTick=false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Keys();
            if(world!=null && world.Presentation!=null) world.Presentation.Ultimate.Cancel();
            ExplorerJourney.Stop();
            var previous=SceneManager.GetActiveScene();
            var empty=SceneManager.CreateScene("Island runtime cleanup"); SceneManager.SetActiveScene(empty);
            if(previous.IsValid()&&previous.isLoaded) yield return SceneManager.UnloadSceneAsync(previous);
            var loader=Object.FindAnyObjectByType<M4RegionRouter>();
            if(loader!=null) Object.Destroy(loader.gameObject);
            yield return null;
            M4Session.UseProgressForTests(null);
            if(keyboard!=null&&keyboard.added) InputSystem.RemoveDevice(keyboard);
            Time.timeScale=scale; Time.fixedDeltaTime=fixedDelta; Time.captureDeltaTime=captureDelta;
            Application.runInBackground=background;
            InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
            InputSystem.settings.backgroundBehavior=backgroundInput;
            Cursor.lockState=cursor; Cursor.visible=cursorVisible;
            Assert.That(Path.GetFileName(directory),Does.StartWith("Orbis-Island-Runtime-"));
            Assert.That(Path.GetDirectoryName(directory),Is.EqualTo(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
            if(Directory.Exists(directory)) Directory.Delete(directory,true);
        }

        [UnityTest]
        public IEnumerator FiveDistrictsShareOnePawnAndCameraAndPositionChangesOnlyRetargetTheContext()
        {
            Assert.That(world.Regions.Count,Is.EqualTo(5));
            CollectionAssert.AreEquivalent(M4RegionCatalog.All.Select(x=>x.Id),world.Regions.Select(x=>x.Id));
            Assert.That(world.Region,Is.EqualTo(M4RegionId.Zephyr));
            Assert.That(Object.FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None).Length,Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<PartyManager>(FindObjectsSortMode.None).Length,Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<ElementalReactionManager>(FindObjectsSortMode.None).Length,Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<ElementalActor>(FindObjectsSortMode.None).Length,Is.EqualTo(39));
            var player=world.Traversal; var party=world.Party; var explorer=ExplorerJourney.Current;
            var camera=Camera.main; var handle=world.gameObject.scene.handle;
            Assert.That(camera.clearFlags,Is.EqualTo(CameraClearFlags.Skybox));
            Assert.That(camera.farClipPlane,Is.EqualTo(3000f).Within(.01f));
            foreach(var virtualCamera in world.GetComponentsInChildren<CinemachineCamera>(true))
                Assert.That(virtualCamera.Lens.FarClipPlane,Is.EqualTo(3000f).Within(.01f));
            world.Vitals.Restore(67f); world.Traversal.Stamina.SetCurrent(53f);
            var first=world.GetRegion(M4RegionId.Agnia);
            world.Manager.Apply(first.Content.Statues[0],ElementType.Fire,party.ActiveMember.Actor);
            foreach(var context in world.Regions)
            {
                var content=context.Content;
                Assert.That(content.PuzzleElement,Is.EqualTo(M4RegionCatalog.Get(context.Id).Element));
                Assert.That(content.Challenge.Model.RequiredReaction,Is.EqualTo(M4RegionContent.RequiredReaction(context.Id)));
                // Direct position changes exercise the same region lookup as walking without a transport callback.
                player.Teleport(context.Layout.Spawn); world.RefreshActiveRegion();
                Assert.That(world.Region,Is.EqualTo(context.Id));
                Assert.That(world.Content,Is.SameAs(content)); Assert.That(world.Boss,Is.SameAs(context.Boss));
                Assert.That(world.Traversal,Is.SameAs(player)); Assert.That(world.Party,Is.SameAs(party));
                Assert.That(ExplorerJourney.Current,Is.SameAs(explorer)); Assert.That(Camera.main,Is.SameAs(camera));
                Assert.That(world.gameObject.scene.handle,Is.EqualTo(handle));
                Assert.That(world.Vitals.Health,Is.EqualTo(67f)); Assert.That(player.Stamina.Current,Is.EqualTo(53f));
            }
            Assert.That(first.Content.Puzzle.Model.LitCount,Is.EqualTo(1),"Border crossing must not reconfigure an earlier puzzle.");
            world.Initialize(); Assert.That(world.Regions.Count,Is.EqualTo(5));
            yield return null;
        }

        [UnityTest]
        public IEnumerator F10FastTravelKeepsTheSameScenePartyResourcesAndSharedSkillCooldown()
        {
            Assert.That(world.Party.TrySwitch(2),Is.True);
            var member=world.Party.ActiveMember; var player=world.Traversal; var explorer=ExplorerJourney.Current;
            var camera=Camera.main; var handle=world.gameObject.scene.handle;
            world.Vitals.Restore(61f); player.Stamina.SetCurrent(49f); explorer.RestoreCooldown(2f);
            // Exercise actual F10 input at normal gameplay time. Rest recovery and cooldown ticking are expected during these frames.
            Keys(Key.F10); yield return Frames(1); Keys(); yield return Frames(1);
            Assert.That(world.TravelMenuOpen,Is.True);
            // Initial Zephyr index 2 -> Granite 3 -> Voltheim 4.
            Keys(Key.DownArrow); yield return Frames(1); Keys(); yield return Frames(1);
            Keys(Key.DownArrow); yield return Frames(1); Keys(); yield return Frames(1);
            Assert.That(world.TravelSelection,Is.EqualTo((int)M4RegionId.Voltheim));
            Keys(Key.Enter); yield return Frames(1); Keys(); yield return Frames(2);
            Assert.That(world.Region,Is.EqualTo(M4RegionId.Voltheim));
            Assert.That(router.IsLoading,Is.False); Assert.That(router.LastError,Is.Null);
            Assert.That(world.TravelMenuOpen,Is.False);
            Assert.That(world.Traversal,Is.SameAs(player)); Assert.That(world.Party.ActiveMember,Is.SameAs(member));
            Assert.That(ExplorerJourney.Current,Is.SameAs(explorer)); Assert.That(Camera.main,Is.SameAs(camera));
            Assert.That(world.gameObject.scene.handle,Is.EqualTo(handle));
            Assert.That(world.Vitals.Health,Is.EqualTo(61f));
            Assert.That(explorer.CooldownRemaining,Is.GreaterThan(0f).And.LessThan(2f));
            Assert.That(player.GetComponent<M0Input>().PresentationLocked,Is.False);
            Assert.That(Vector3.Distance(player.transform.position,world.Layout.Spawn),Is.LessThan(.5f));

            // Check the same transport's resource contract synchronously: no frame elapses in which normal regeneration could occur.
            float staminaBefore=player.Stamina.Current, cooldownBefore=explorer.CooldownRemaining;
            float healthBefore=world.Vitals.Health;
            Assert.That(staminaBefore,Is.GreaterThan(0f).And.LessThan(player.Stamina.Maximum));
            int loaded=0;
            Action<M4RegionId> observe=id=>
            {
                Assert.That(id,Is.EqualTo(M4RegionId.Agnia));
                Assert.That(player.Stamina.Current,Is.EqualTo(staminaBefore));
                Assert.That(explorer.CooldownRemaining,Is.EqualTo(cooldownBefore));
                Assert.That(world.Vitals.Health,Is.EqualTo(healthBefore));
                loaded++;
            };
            router.Loaded+=observe;
            try
            {
                Assert.That(router.Travel(M4RegionId.Agnia),Is.True);
                Assert.That(loaded,Is.EqualTo(1),"Same-scene travel must finish synchronously without recreating the scene.");
                Assert.That(world.gameObject.scene.handle,Is.EqualTo(handle));
                Assert.That(world.Party.ActiveMember,Is.SameAs(member));
                Assert.That(ExplorerJourney.Current,Is.SameAs(explorer));
                Assert.That(player.Stamina.Current,Is.EqualTo(staminaBefore));
                Assert.That(explorer.CooldownRemaining,Is.EqualTo(cooldownBefore));
            }
            finally {router.Loaded-=observe;}
            yield return Frames(2);
            Assert.That(explorer.CooldownRemaining,Is.GreaterThan(0f).And.LessThan(cooldownBefore),
                "The preserved skill cooldown must continue ticking normally after same-scene transport.");
        }
        [UnityTest]
        public IEnumerator RemotePuzzleAndChallengeEventsCreditTheirOwnDistrictAndResetClearsEveryEncounter()
        {
            foreach(var quest in progress.DailyQuests) Assert.That(progress.Accept(quest.Definition.Id),Is.True);
            var puzzleQuest=progress.DailyQuests.Single(x=>x.Definition.Kind==M4ObjectiveKind.FieldPuzzle);
            var puzzle=world.GetRegion(puzzleQuest.Definition.Region);
            var other=world.Regions.First(x=>x.Id!=puzzle.Id);
            Assert.That(router.Travel(other.Id),Is.True);
            var source=world.Party.ActiveMember.Actor;
            foreach(var statue in puzzle.Content.Statues) world.Manager.Apply(statue,puzzle.Content.PuzzleElement,source);
            Assert.That(world.Region,Is.EqualTo(other.Id));
            Assert.That(progress.DailyQuests.Single(x=>x.Definition.Id==puzzleQuest.Definition.Id).State,Is.EqualTo(M4QuestState.Completed));
            var roomQuest=progress.DailyQuests.Single(x=>x.Definition.Kind==M4ObjectiveKind.ChallengeRoom);
            var room=world.GetRegion(roomQuest.Definition.Region);
            Assert.That(router.Travel(world.Regions.First(x=>x.Id!=room.Id).Id),Is.True);
            Assert.That(room.Content.Challenge.TryStart(),Is.True);
            foreach(var target in room.Content.Targets) ApplyPair(world,room.Content.Challenge.Model.RequiredReaction,target,source);
            Assert.That(room.Content.Challenge.State,Is.EqualTo(ChallengeState.Completed));
            Assert.That(progress.DailyQuests.Single(x=>x.Definition.Id==roomQuest.Definition.Id).State,Is.EqualTo(M4QuestState.Completed));
            var farTarget=world.GetRegion(M4RegionId.Teluna).Content.Statues[0];
            world.Manager.ResetTarget(farTarget);
            world.Manager.Apply(farTarget,ElementType.Water,source); world.Manager.Apply(farTarget,ElementType.Lightning,source);
            Assert.That(world.Manager.PendingElectroChargedCount,Is.GreaterThan(0));
            world.ResetEncounter();
            Assert.That(world.Manager.PendingElectroChargedCount,Is.Zero);
            foreach(var context in world.Regions)
            {
                Assert.That(context.Content.Puzzle.Model.LitCount,Is.Zero);
                Assert.That(context.Content.Challenge.State,Is.EqualTo(ChallengeState.Ready));
                Assert.That(context.Boss.HitPoints,Is.EqualTo(context.Boss.MaximumHitPoints));
                foreach(var actor in context.Content.Statues.Concat(context.Content.Targets))
                    Assert.That(actor.DamageTaken,Is.Zero);
            }
            world.Manager.Tick(10f); Assert.That(farTarget.DamageTaken,Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WeeklyBoundaryRestoresAllFiveDefeatedBossesWithoutReloadingTheWorld()
        {
            var source=world.Party.ActiveMember.Actor;
            foreach(var context in world.Regions)
            {
                world.Manager.DealPhysicalDamage(context.Boss.Actor,source,10000f);
                Assert.That(context.Boss.State,Is.EqualTo(M4BossState.Defeated));
                Assert.That(progress.IsBossDefeated(context.Id),Is.True);
            }
            world.ResetEncounter();
            Assert.That(world.Regions.All(x=>x.Boss.State==M4BossState.Defeated),Is.True);
            clock=clock.AddDays(7); Assert.That(progress.RefreshPeriods(),Is.True);
            float deadline=Time.realtimeSinceStartup+3f;
            while(world.Regions.Any(x=>x.Boss.State==M4BossState.Defeated)&&Time.realtimeSinceStartup<deadline) yield return null;
            foreach(var context in world.Regions)
            {
                Assert.That(context.Boss.State,Is.EqualTo(M4BossState.Dormant));
                Assert.That(context.Boss.HitPoints,Is.EqualTo(context.Boss.MaximumHitPoints));
                Assert.That(context.Boss.Actor.IsOnField,Is.True);
                Assert.That(progress.IsBossDefeated(context.Id),Is.False);
            }
        }

        static void ApplyPair(M4SceneBootstrap scene,ReactionType reaction,ElementalActor target,ElementalActor source)
        {
            ElementType first=ElementType.Fire,second=ElementType.Water;
            if(reaction==ReactionType.ElectroCharged) {first=ElementType.Water;second=ElementType.Lightning;}
            else if(reaction==ReactionType.Overload) second=ElementType.Lightning;
            else if(reaction==ReactionType.Swirl) second=ElementType.Wind;
            else if(reaction==ReactionType.Crystallize) second=ElementType.Rock;
            scene.Manager.Apply(target,first,source); scene.Manager.Apply(target,second,source);
        }
        void Keys(params Key[] keys) {if(keyboard!=null&&keyboard.added) InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));}
        static IEnumerator Frames(int count) {for(int i=0;i<count;i++) yield return null;}
    }
}
