using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.Art;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Orbis.M4;
using Orbis.M16;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Orbis.Game.Tests
{
    /// <summary>Loads the saved, editable Agnia scene; every save operation uses a temporary profile.</summary>
    public sealed class AuthoredGameSceneTests
    {
        const string ScenePath = "Assets/Orbis/Game/Scenes/Orbis_OpenWorld.unity";
        string directory;
        M4ProgressService profile;
        GameSceneEntry entry;
        M4SceneBootstrap world;
        Scene loadedScene;
        Keyboard keyboard;
        bool cursorVisible, background;
        CursorLockMode cursorLock;
        float scale, fixedDelta, captureDelta;
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
        InputSettings.BackgroundBehavior backgroundInput;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            cursorVisible = Cursor.visible; cursorLock = Cursor.lockState;
            scale = Time.timeScale; fixedDelta = Time.fixedDeltaTime; captureDelta = Time.captureDeltaTime;
            background = Application.runInBackground;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            backgroundInput = InputSystem.settings.backgroundBehavior;
            // Preserve the global settings instance: only temporary input routing values are changed.
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true; Time.timeScale = 1f; Time.captureDeltaTime = 1f / 60f;
            keyboard = InputSystem.AddDevice<Keyboard>(); Keys();
            directory = Path.Combine(Path.GetTempPath(), "Orbis-Game-Play-" + Guid.NewGuid().ToString("N"));
            profile = new M4ProgressService(Path.Combine(directory, "m4-progress.json"));
            ExplorerJourney.Stop();
            M4Session.UseProgressForTests(profile);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Keys();
            if (world != null && world.Presentation != null) world.Presentation.Ultimate.Cancel();
            ExplorerJourney.Stop();
            var router = Object.FindAnyObjectByType<M4RegionRouter>();
            if (router != null)
            {
                float deadline = Time.realtimeSinceStartup + 15f;
                while (router.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            }
            Scene previous = SceneManager.GetActiveScene();
            Scene empty = SceneManager.CreateScene("Authored game cleanup");
            SceneManager.SetActiveScene(empty);
            if (previous.IsValid() && previous.isLoaded) yield return SceneManager.UnloadSceneAsync(previous);
            if (router != null) Object.Destroy(router.gameObject);
            yield return null;
            M4Session.UseProgressForTests(null);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            InputSystem.settings.backgroundBehavior = backgroundInput;
            Time.timeScale = scale; Time.fixedDeltaTime = fixedDelta; Time.captureDeltaTime = captureDelta;
            Application.runInBackground = background;
            Cursor.visible = cursorVisible; Cursor.lockState = cursorLock;
            Assert.That(Path.GetFileName(directory), Does.StartWith("Orbis-Game-Play-"));
            Assert.That(Path.GetDirectoryName(directory),
                Is.EqualTo(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        IEnumerator LoadGame()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            loadedScene = SceneManager.GetActiveScene();
            yield return Frames(3); // Start installs selection or starts the saved explorer.
            entry = SceneObjects<GameSceneEntry>().Single();
            world = entry.World;
            Assert.That(loadedScene.path, Is.EqualTo(ScenePath));
            Assert.That(world, Is.Not.Null);
            Assert.That(world.AuthoredRegion, Is.Not.Null);
        }

        IEnumerator ConfirmAndStart(ExplorerChoice choice = ExplorerChoice.Stella)
        {
            ExplorerSelectionScreen selection = SceneObjects<ExplorerSelectionScreen>().Single();
            Assert.That(selection.Select(choice), Is.True, selection.Notice);
            Assert.That(entry.HasStarted, Is.False, "Selection confirmation must not start the world.");
            Assert.That(selection.BeginJourney(), Is.True, selection.Notice);
            yield return Frames(35); // Finish appearance and initial input capture before behavioral checks.
            Assert.That(entry.HasStarted, Is.True, entry.LastError);
            Assert.That(world.IsInitialized, Is.True);
        }

        [UnityTest]
        public IEnumerator UnselectedWorldKeepsAuthoredInstancesAndStartsInTheSameSceneOnlyAfterConfirmation()
        {
            yield return LoadGame();
            Assert.That(entry.HasStarted, Is.False);
            Assert.That(world.IsInitialized, Is.False);
            Assert.That(profile.GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Unselected));
            Assert.That(SceneObjects<PlayerMotor>(), Is.Empty);
            Assert.That(entry.OverviewCamera.gameObject.activeInHierarchy, Is.True);
            Assert.That(entry.BeginWorld(), Is.False, "A direct game scene may not silently choose the explorer.");

            var authored = world.AuthoredRegion;
            var actors = authored.Statues.Concat(authored.Targets)
                .Append(authored.BossObject.GetComponent<ElementalActor>()).ToArray();
            Assert.That(actors.Length, Is.EqualTo(7));
            Assert.That(actors.All(x => x != null), Is.True);
            Assert.That(SceneObjects<ElementalActor>().Length, Is.EqualTo(7));
            Transform[] parents = actors.Select(x => x.transform.parent).ToArray();
            Vector3[] positions = actors.Select(x => x.transform.position).ToArray();
            Transform terrain = FindTransform("01 Terrain and Traversal");
            var sceneHandle = loadedScene.handle;
            Capture(entry.OverviewCamera, "Game_Overview.png");

            yield return ConfirmAndStart();

            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(sceneHandle),
                "Confirming the embedded selection must not reload or replace the authored scene.");
            Assert.That(FindTransform("01 Terrain and Traversal"), Is.SameAs(terrain));
            for (int i = 0; i < actors.Length; i++)
            {
                Assert.That(actors[i], Is.Not.Null);
                Assert.That(actors[i].transform.parent, Is.SameAs(parents[i]));
                Assert.That(actors[i].transform.position, Is.EqualTo(positions[i]));
            }
            CollectionAssert.AreEqual(authored.Statues, world.Content.Statues);
            CollectionAssert.AreEqual(authored.Targets, world.Content.Targets);
            Assert.That(world.Content.BossObject, Is.SameAs(authored.BossObject));
            Assert.That(world.Content.Puzzle, Is.SameAs(authored.Puzzle));
            Assert.That(world.Content.Challenge, Is.SameAs(authored.Challenge));

            int actorCount = SceneObjects<ElementalActor>().Length;
            var player = world.Traversal.GetComponent<PlayerMotor>();
            world.Initialize(); world.Initialize();
            Assert.That(entry.BeginWorld(), Is.True);
            yield return Frames(2);
            Assert.That(world.Traversal.GetComponent<PlayerMotor>(), Is.SameAs(player));
            Assert.That(SceneObjects<PlayerMotor>().Length, Is.EqualTo(1));
            Assert.That(SceneObjects<PartyManager>().Length, Is.EqualTo(1));
            Assert.That(SceneObjects<ArtScenePresentation>().Length, Is.EqualTo(1));
            Assert.That(SceneObjects<ElementalActor>().Length, Is.EqualTo(actorCount));
            Assert.That(actorCount, Is.EqualTo(actors.Length + PartyManager.Capacity));
            Assert.That(entry.OverviewCamera.gameObject.activeInHierarchy, Is.False);
            var glider = player.transform.Find("Placeholder Glider");
            Assert.That(glider, Is.Not.Null);
            var importedWing = glider.Find("Imported banner");
            Assert.That(importedWing, Is.Not.Null);
            Assert.That(importedWing.gameObject.activeInHierarchy, Is.False, "The imported wing must stay hidden while walking.");
            glider.gameObject.SetActive(true);
            Assert.That(importedWing.gameObject.activeInHierarchy, Is.True);
            glider.gameObject.SetActive(false);
            Capture(Camera.main, "Game_Player.png");
        }

        [UnityTest]
        public IEnumerator SavedPolarisStartsDirectlyWithItsPersistedElementAndOnePermanentSlot()
        {
            Assert.That(profile.TrySelectExplorer(ExplorerChoice.Polaris), Is.True);
            Assert.That(profile.TrySetExplorerElement(ElementType.Wind), Is.True);
            yield return LoadGame();
            yield return Frames(35);
            Assert.That(entry.HasStarted, Is.True, entry.LastError);
            Assert.That(world.IsInitialized, Is.True);
            Assert.That(SceneObjects<ExplorerSelectionScreen>().Length, Is.Zero);
            Assert.That(world.Party.Members.Count, Is.EqualTo(4));
            Assert.That(world.Party.PermanentMember, Is.SameAs(world.Party.Members[0]));
            Assert.That(world.Party.ActiveMember.Actor.SourceId, Is.EqualTo("polaris"));
            Assert.That(ExplorerJourney.Current.Actor, Is.SameAs(world.Party.PermanentMember.Actor));
            Assert.That(ExplorerJourney.Current.SelectedElement, Is.EqualTo(ElementType.Wind));
            Assert.That(SceneObjects<ElementalActor>().Any(x => x.SourceId == "stella"), Is.False);
            Assert.That(SceneObjects<PlayerMotor>().Length, Is.EqualTo(1));
            var art = world.GetComponent<ArtScenePresentation>().Characters;
            Assert.That(art.ActiveVisual, Is.SameAs(art.PermanentVisual));
            Assert.That(art.ActiveVisual.activeInHierarchy, Is.True);
            Assert.That(world.Party.TrySwitch(3), Is.True);
            Assert.That(world.Party.ActiveMember.Actor.SourceId, Is.Not.EqualTo("polaris"));
            Assert.That(world.Party.TrySwitch(0), Is.True);
            Assert.That(art.ActiveVisual, Is.SameAs(art.PermanentVisual));
            var reloaded = new M4ProgressService(profile.SavePath).GetExplorerSnapshot();
            Assert.That(reloaded.choice, Is.EqualTo(ExplorerChoice.Polaris));
            Assert.That(reloaded.element, Is.EqualTo(ElementType.Wind));
        }

        [UnityTest]
        public IEnumerator RegionalRoundTripReturnsToAuthoredAgniaWithHealthAndCompanionIdentityPreserved()
        {
            Assert.That(profile.TrySelectExplorer(ExplorerChoice.Polaris, ElementType.Rock), Is.True);
            yield return LoadGame();
            yield return Frames(35);
            Assert.That(entry.HasStarted, Is.True, entry.LastError);
            Assert.That(world.Party.TrySwitch(3), Is.True);
            string companionId = world.Party.ActiveMember.Actor.SourceId;
            world.Vitals.Restore(68f);
            var router = M4RegionRouter.Instance;
            Assert.That(router.AgniaSceneOverride, Is.EqualTo(ScenePath));
            Assert.That(router.Travel(M4RegionId.Teluna), Is.True);
            yield return AwaitRegion(M4RegionId.Teluna, false);
            Assert.That(world.Party.ActiveMember.Actor.SourceId, Is.EqualTo(companionId));
            Assert.That(world.Vitals.Health, Is.EqualTo(68f));
            Assert.That(ExplorerJourney.Current.SelectedElement, Is.EqualTo(ElementType.Rock));

            Assert.That(router.Travel(M4RegionId.Agnia), Is.True);
            yield return AwaitRegion(M4RegionId.Agnia, true);
            Assert.That(loadedScene.path, Is.EqualTo(ScenePath));
            Assert.That(entry.World, Is.SameAs(world));
            Assert.That(entry.HasStarted, Is.True, entry.LastError);
            Assert.That(world.AuthoredRegion, Is.Not.Null);
            Assert.That(world.Party.PermanentMember.Actor.SourceId, Is.EqualTo("polaris"));
            Assert.That(world.Party.ActiveMember.Actor.SourceId, Is.EqualTo(companionId));
            Assert.That(world.Vitals.Health, Is.EqualTo(68f));
            Assert.That(ExplorerJourney.Current.SelectedElement, Is.EqualTo(ElementType.Rock));
            Assert.That(SceneObjects<PlayerMotor>().Length, Is.EqualTo(1));
            Assert.That(SceneObjects<M4SceneBootstrap>().Length, Is.EqualTo(1));
            Assert.That(SceneObjects<GameSceneEntry>().Length, Is.EqualTo(1));
            Assert.That(SceneObjects<ArtScenePresentation>().Length, Is.EqualTo(1));
            Assert.That(SceneObjects<WaterVolume>().Length, Is.EqualTo(1));
            Assert.That(SceneObjects<ClimbableSurface>().Length, Is.EqualTo(1));
            Assert.That(SceneObjects<ElementalActor>().Length, Is.EqualTo(7 + PartyManager.Capacity));
            Assert.That(world.Content.Puzzle, Is.SameAs(world.AuthoredRegion.Puzzle));
            Assert.That(world.Content.Challenge, Is.SameAs(world.AuthoredRegion.Challenge));
            Assert.That(world.Party.TrySwitch(0), Is.True);
            Assert.That(world.GetComponent<ArtScenePresentation>().Characters.ActiveVisual,
                Is.SameAs(world.GetComponent<ArtScenePresentation>().Characters.PermanentVisual));
        }

        IEnumerator AwaitRegion(M4RegionId region, bool authoredGame)
        {
            float deadline = Time.realtimeSinceStartup + 45f;
            do
            {
                yield return null;
                world = Object.FindAnyObjectByType<M4SceneBootstrap>();
                entry = Object.FindAnyObjectByType<GameSceneEntry>();
                if (!M4RegionRouter.Instance.IsLoading && world != null && world.Region == region &&
                    world.IsInitialized && ExplorerJourney.Current != null &&
                    (!authoredGame || (entry != null && entry.HasStarted))) break;
            } while (Time.realtimeSinceStartup < deadline);
            Assert.That(M4RegionRouter.Instance.LastError, Is.Null);
            Assert.That(M4RegionRouter.Instance.IsLoading, Is.False);
            Assert.That(world, Is.Not.Null);
            Assert.That(world.Region, Is.EqualTo(region));
            Assert.That(world.IsInitialized, Is.True);
            if (authoredGame)
            {
                Assert.That(entry, Is.Not.Null);
                Assert.That(entry.HasStarted, Is.True, entry.LastError);
            }
            loadedScene = world.gameObject.scene;
            yield return Frames(8);
        }

        [UnityTest]
        public IEnumerator PreInitializationGeometryAndColliderEditsRemainAndDriveClimbingAndSwimming()
        {
            yield return LoadGame();
            var ground = FindTransform("South Ground").GetComponent<BoxCollider>();
            var cliff = SceneObjects<ClimbableSurface>().Single();
            var cliffCollider = cliff.GetComponent<BoxCollider>();
            var water = SceneObjects<WaterVolume>().Single();
            Assert.That(ground, Is.Not.Null);
            Assert.That(cliffCollider, Is.Not.Null);
            var visibleGround = ground.GetComponentsInChildren<MeshRenderer>(true).First(x => x.enabled);
            Material groundMaterial = visibleGround.sharedMaterial;
            Transform groundVisualParent = visibleGround.transform.parent;
            // The uninitialized scene is the same data boundary as an authored edit before pressing Play.
            ground.center += new Vector3(.15f, 0f, 0f);
            ground.size = new Vector3(ground.size.x * .97f, ground.size.y, ground.size.z);
            cliff.transform.position += Vector3.right * 1.25f;
            Vector3 editedCenter = ground.center, editedSize = ground.size, editedCliff = cliff.transform.position;
            Collider[] authoredColliders = ground.transform.parent.GetComponentsInChildren<Collider>(true);
            Physics.SyncTransforms();

            yield return ConfirmAndStart();

            Assert.That(ground.center, Is.EqualTo(editedCenter));
            Assert.That(ground.size, Is.EqualTo(editedSize));
            Assert.That(cliff.transform.position, Is.EqualTo(editedCliff));
            Assert.That(visibleGround.enabled, Is.True);
            Assert.That(visibleGround.sharedMaterial, Is.SameAs(groundMaterial));
            Assert.That(visibleGround.transform.parent, Is.SameAs(groundVisualParent));
            CollectionAssert.AreEquivalent(authoredColliders,
                ground.transform.parent.GetComponentsInChildren<Collider>(true));
            Assert.That(SceneObjects<WaterVolume>().Single(), Is.SameAs(water));
            Assert.That(SceneObjects<ClimbableSurface>().Single(), Is.SameAs(cliff));
            Assert.That(WaterVolume.ActiveVolumes.Contains(water), Is.True);
            Assert.That(water.GetComponent<BoxCollider>().isTrigger, Is.True);

            Bounds wall = cliffCollider.bounds;
            world.Warp(new Vector3(wall.center.x, wall.min.y + .1f, wall.min.z - .6f));
            yield return Frames(12);
            Keys(Key.E); yield return Frames(1); Keys(); yield return Frames(1);
            Assert.That(world.Traversal.Mode, Is.EqualTo(ExplorationMode.Climbing),
                "The shifted authored cliff must participate in the existing E grab query.");
            float climbedFrom = world.Traversal.transform.position.y;
            Keys(Key.W); yield return Frames(20); Keys();
            Assert.That(world.Traversal.transform.position.y, Is.GreaterThan(climbedFrom + .25f));
            Bounds lake = water.Bounds;
            world.Warp(new Vector3(lake.min.x + 2f, water.SurfaceY - .95f, lake.center.z));
            yield return Frames(12);
            Assert.That(world.Traversal.Mode, Is.EqualTo(ExplorationMode.Swimming));
            Assert.That(world.Traversal.Stamina.Current, Is.LessThan(world.Traversal.Stamina.Maximum));
        }

        [UnityTest]
        public IEnumerator BoundPuzzleChallengeAndGuardianUseTheExistingReactionAndInteractionLogic()
        {
            yield return LoadGame();
            var authored = world.AuthoredRegion;
            yield return ConfirmAndStart();
            var source = world.Party.ActiveMember.Actor;
            world.Manager.AutoTick = false;
            world.Content.Challenge.AutoTick = false;
            world.Boss.AutoTick = false;
            Assert.That(world.Boss.gameObject, Is.SameAs(authored.BossObject));
            Assert.That(world.Boss.Actor, Is.SameAs(authored.BossObject.GetComponent<ElementalActor>()));
            Assert.That(world.Content.Puzzle.Model.LitCount, Is.Zero);
            for (int i = 0; i < authored.Statues.Length; i++)
            {
                world.Manager.Apply(authored.Statues[i], ElementType.Fire, source, 0f, 4f);
                Assert.That(world.Content.Puzzle.Model.LitCount, Is.EqualTo(i + 1));
            }
            Assert.That(world.Content.Puzzle.Model.IsUnlocked, Is.True);
            Assert.That(world.Content.Challenge.State, Is.EqualTo(ChallengeState.Ready));
            Assert.That(authored.Targets.All(x => !x.IsOnField), Is.True);
            world.Warp(authored.ChallengeEntry.position + Vector3.up * .1f);
            world.Interact();
            Assert.That(world.Content.Challenge.State, Is.EqualTo(ChallengeState.Running));
            foreach (ElementalActor target in authored.Targets)
            {
                Assert.That(target.IsOnField, Is.True);
                Assert.That(target.GetComponent<Collider>().enabled, Is.True);
                world.Manager.Apply(target, ElementType.Fire, source, 0f, 4f);
                Assert.That(world.Manager.Apply(target, ElementType.Water, source, 10f, 4f),
                    Is.EqualTo(ReactionType.Vaporize));
            }
            Assert.That(world.Content.Challenge.DefeatedCount, Is.EqualTo(3));
            Assert.That(world.Content.Challenge.State, Is.EqualTo(ChallengeState.Completed));
            Assert.That(authored.Targets.All(x => !x.IsOnField), Is.True);
            for (int i = 0; i < 2; i++)
            {
                world.Manager.Apply(world.Boss.Actor, world.Boss.Weakness, source, 10f, 4f);
                Assert.That(world.Boss.WeakHitCount, Is.EqualTo(i + 1));
            }
            world.Manager.Apply(world.Boss.Actor, world.Boss.Weakness, source, 10f, 4f);
            Assert.That(world.Boss.WeakHitCount, Is.Zero);
            Assert.That(world.Boss.State, Is.EqualTo(M4BossState.Exposed));
            Assert.That(world.Boss.HitPoints, Is.LessThan(world.Boss.MaximumHitPoints));
            Assert.That(profile.IsBossDefeated(M4RegionId.Agnia), Is.False);
            yield return null;
        }

        T[] SceneObjects<T>() where T : Component => loadedScene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        Transform FindTransform(string name) => SceneObjects<Transform>().Single(x => x.name == name);
        void Keys(params Key[] values)
        {
            if (keyboard != null && keyboard.added)
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(values));
        }
        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        static void Capture(Camera camera, string name)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            Assert.That(camera, Is.Not.Null, "A real scene camera is required for visual review.");
            RenderTexture previous = RenderTexture.active, previousTarget = camera.targetTexture;
            float aspect = camera.aspect;
#if UNITY_EDITOR
            bool async = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
#endif
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D pixels = null;
            try
            {
                target.Create(); camera.aspect = 1280f / 720f;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
                Directory.CreateDirectory(output);
                File.WriteAllBytes(Path.Combine(output, name), pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = previousTarget; camera.aspect = aspect;
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation = async;
#endif
                if (pixels != null) Object.Destroy(pixels);
                target.Release(); Object.Destroy(target);
            }
        }
    }
}
