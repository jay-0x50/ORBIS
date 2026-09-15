using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Orbis.EditorSupport;
using Orbis.M1;
using Orbis.M2;
using Orbis.M4.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Orbis.M4.Tests
{
    public sealed class M4ContentTests
    {
        [TestCase(M4RegionId.Agnia, "아그니아 고원", ElementType.Fire, ElementType.Water, ElementType.Fire, ElementType.Water, ReactionType.Vaporize)]
        [TestCase(M4RegionId.Teluna, "텔루나 군도", ElementType.Water, ElementType.Lightning, ElementType.Water, ElementType.Lightning, ReactionType.ElectroCharged)]
        [TestCase(M4RegionId.Zephyr, "자피르 초원", ElementType.Wind, ElementType.Rock, ElementType.Water, ElementType.Wind, ReactionType.Swirl)]
        [TestCase(M4RegionId.Granite, "그라니테 산맥", ElementType.Rock, ElementType.Fire, ElementType.Fire, ElementType.Rock, ReactionType.Crystallize)]
        [TestCase(M4RegionId.Voltheim, "볼트하임", ElementType.Lightning, ElementType.Water, ElementType.Fire, ElementType.Lightning, ReactionType.Overload)]
        public void RegionalRosterCanSolveItsPuzzleRoomAndBoss(M4RegionId id, string name, ElementType element,
            ElementType weakness, ElementType first, ElementType second, ReactionType reaction)
        {
            var profile = M4RegionCatalog.Get(id);
            Assert.That(profile.DisplayName, Is.EqualTo(name));
            Assert.That(profile.Element, Is.EqualTo(element));
            Assert.That(profile.Weakness, Is.EqualTo(weakness));
            var roster = M4SceneBootstrap.Roster(id);
            Assert.That(roster, Has.Length.EqualTo(4));
            Assert.That(roster.Distinct().Count(), Is.EqualTo(4));
            foreach(var required in new[]{element,weakness,first,second}) CollectionAssert.Contains(roster,required);
            Assert.That(M4RegionContent.RequiredReaction(id), Is.EqualTo(reaction));
            Assert.That(ElementReactionResolver.Resolve(first, second), Is.EqualTo(reaction));
        }

        [TestCase(M4RegionId.Agnia)]
        [TestCase(M4RegionId.Teluna)]
        [TestCase(M4RegionId.Zephyr)]
        [TestCase(M4RegionId.Granite)]
        [TestCase(M4RegionId.Voltheim)]
        public void ShippedRegionSceneHasOneCorrectBootstrapAndNoMissingScripts(M4RegionId id)
        {
            string path = "Assets/Orbis/M4/Scenes/M4_" + id + ".unity";
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null, path);
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var roots = scene.GetRootGameObjects();
                var bootstraps = roots.SelectMany(x => x.GetComponentsInChildren<M4SceneBootstrap>(true)).ToArray();
                Assert.That(bootstraps, Has.Length.EqualTo(1), path);
                Assert.That(bootstraps[0].Region, Is.EqualTo(id), "The scene address must enter the matching region.");
                foreach (var transform in roots.SelectMany(x => x.GetComponentsInChildren<Transform>(true)))
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, transform.name);
            }
            finally
            {
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        [Test]
        public void FiveAddressableScenesAreDistinctAndProductOrLegacyEntryIsValid()
        {
            Assert.That(M4RegionCatalog.All.Select(x => x.Id).Distinct().Count(), Is.EqualTo(5));
            Assert.That(M4RegionCatalog.All.Select(x => M4ProjectSetup.Address(x.Id)).Distinct().Count(), Is.EqualTo(5));
            // Reads the actual Addressables settings/group entries and imported scene GUIDs.
            Assert.DoesNotThrow(M4ProjectSetup.Validate);
            var builtIn = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
            const string selection = "Assets/Orbis/M16/Scenes/M16_CharacterSelection.unity";
            if (FieldSceneBuildPolicy.IsProductMode)
            {
                Assert.That(builtIn[0], Is.EqualTo(FieldSceneBuildPolicy.RuntimeScenePath));
                if (FieldSceneBuildPolicy.IsRegressionTestScopeActive)
                    Assert.That(builtIn, Does.Contain(M4ProjectSetup.LauncherScene));
                else
                    Assert.That(builtIn, Is.EqualTo(new[] { FieldSceneBuildPolicy.RuntimeScenePath }),
                        "The normal player ships one entry, with legacy built-in demos disabled.");
                foreach (string path in FieldSceneBuildPolicy.LegacyBuiltInScenes)
                    Assert.That(EditorBuildSettings.scenes.Any(scene => scene.path == path), Is.True,
                        "Retain the legacy fixture entry so automatic setup does not recreate it: " + path);
            }
            else
            {
                string expectedEntry = AssetDatabase.LoadAssetAtPath<SceneAsset>(selection) != null
                    ? selection : M4ProjectSetup.LauncherScene;
                Assert.That(builtIn[0], Is.EqualTo(expectedEntry));
                Assert.That(builtIn, Does.Contain(M4ProjectSetup.LauncherScene));
            }
            foreach (var region in M4RegionCatalog.All)
                Assert.That(builtIn, Does.Not.Contain(M4ProjectSetup.ScenePath(region.Id)));
            Assert.Throws<ArgumentOutOfRangeException>(() => M4RegionCatalog.Get((M4RegionId)99));
        }

        [TestCase(M4RegionId.Agnia, ElementType.Fire, ElementType.Water)]
        [TestCase(M4RegionId.Teluna, ElementType.Water, ElementType.Lightning)]
        [TestCase(M4RegionId.Zephyr, ElementType.Water, ElementType.Wind)]
        [TestCase(M4RegionId.Granite, ElementType.Fire, ElementType.Rock)]
        [TestCase(M4RegionId.Voltheim, ElementType.Fire, ElementType.Lightning)]
        public void ParameterizedM2ComponentsAcceptRealRegionalHitsAndReplayWithoutRewardDuplication(
            M4RegionId id, ElementType first, ElementType second)
        {
            var objects = new List<GameObject>();
            try
            {
                var owner = Create(objects, "Regional content event fixture", new Vector3(31000, 0, 31000));
                var manager = owner.AddComponent<ElementalReactionManager>(); manager.AutoTick = false;
                var attacker = Actor(objects, "Attacker", owner.transform.position + Vector3.back * 10, ActorTeam.Player);
                var statues = new ElementalActor[3]; var targets = new ElementalActor[3];
                for (int i = 0; i < 3; i++)
                {
                    statues[i] = Actor(objects, "Statue " + i, owner.transform.position + Vector3.right * (i * 8), ActorTeam.Enemy);
                    targets[i] = Actor(objects, "Target " + i, owner.transform.position + new Vector3(i * 8, 0, 25), ActorTeam.Enemy);
                    targets[i].gameObject.AddComponent<BoxCollider>();
                }
                var inventory = new RewardInventory();
                var puzzle = owner.AddComponent<FieldElementPuzzle>();
                var room = owner.AddComponent<ChallengeRoom>(); room.AutoTick = false;
                ElementType required = M4RegionCatalog.Get(id).Element;
                puzzle.Configure(manager, statues, inventory, "regional.field", required, true);
                room.Configure(manager, targets, inventory, "regional.room", M4RegionContent.RequiredReaction(id), true);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    Assert.That(puzzle.Model.IsUnlocked, Is.False, "M4 replay must reset even a previously claimed field.");
                    foreach (var statue in statues) manager.Apply(statue, required, attacker, 1);
                    Assert.That(puzzle.Model.IsUnlocked, Is.True, required + " must flow through the M1 applied-event subscription.");
                    Assert.That(puzzle.TryClaimReward(), Is.EqualTo(attempt == 0));
                    Assert.That(room.TryStart(), Is.True);
                    foreach (var target in targets)
                    {
                        Assert.That(target.GetComponent<BoxCollider>().enabled, Is.True);
                        manager.Apply(target, first, attacker, 1);
                        manager.Apply(target, second, attacker, 1);
                    }
                    Assert.That(room.State, Is.EqualTo(ChallengeState.Completed));
                    Assert.That(room.DefeatedCount, Is.EqualTo(3));
                    Assert.That(targets.All(x => !x.GetComponent<BoxCollider>().enabled), Is.True);
                    Assert.That(room.TryClaimReward(), Is.EqualTo(attempt == 0));
                    Assert.That(inventory.EnhancementMaterials, Is.EqualTo(8), "Replaying objectives must not mint more session materials.");
                    puzzle.ResetPuzzle(); room.ResetChallenge();
                }
                var reconstructedPuzzle = new OrderedFirePuzzleModel(inventory, "regional.field", requiredElement: required, allowReplayAfterClaim: true);
                var reconstructedRoom = new ChallengeRoomModel(inventory, "regional.room", requiredReaction: M4RegionContent.RequiredReaction(id), allowReplayAfterClaim: true);
                Assert.That(reconstructedPuzzle.IsUnlocked, Is.False);
                Assert.That(reconstructedRoom.State, Is.EqualTo(ChallengeState.Ready));
                Assert.That(reconstructedPuzzle.RewardClaimed && reconstructedRoom.RewardClaimed, Is.True);
            }
            finally { DestroyAll(objects); }
        }

        [Test]
        public void LegacyM2DefaultsStillReconstructClaimedContentAsCompleted()
        {
            var inventory = new RewardInventory();
            var puzzle = new OrderedFirePuzzleModel(inventory, "legacy.field");
            var room = new ChallengeRoomModel(inventory, "legacy.room");
            Assert.That(puzzle.RequiredElement, Is.EqualTo(ElementType.Fire));
            Assert.That(room.RequiredReaction, Is.EqualTo(ReactionType.Vaporize));
            Assert.That(room.TimeLimit, Is.EqualTo(60));
            for (int i = 0; i < 3; i++) puzzle.RegisterHit(i, ElementType.Fire);
            room.TryStart();
            for (int i = 0; i < 3; i++) room.RegisterReaction(i, ReactionType.Vaporize);
            Assert.That(puzzle.TryClaimReward() && room.TryClaimReward(), Is.True);
            puzzle.ResetProgress();
            Assert.That(puzzle.IsUnlocked, Is.True);
            Assert.That(new OrderedFirePuzzleModel(inventory, "legacy.field").IsUnlocked, Is.True);
            Assert.That(new ChallengeRoomModel(inventory, "legacy.room").State, Is.EqualTo(ChallengeState.Completed));
            Assert.That(inventory.EnhancementMaterials, Is.EqualTo(8));
        }

        [TestCase(M4RegionId.Teluna)]
        [TestCase(M4RegionId.Zephyr)]
        [TestCase(M4RegionId.Granite)]
        [TestCase(M4RegionId.Voltheim)]
        public void RegionalContentAnchorsStandOnSolidGroundAndBossCourtIsSeparated(M4RegionId id)
        {
            var owner = new GameObject("Region geometry fixture");
            try
            {
                var layout = M4RegionGeometry.Build(owner.transform, id);
                var anchors = new[] { layout.Spawn, layout.Chest, layout.ChallengeEntry, layout.BossCenter, layout.Npc, layout.Portal }
                    .Concat(layout.PuzzleStatuePositions).Concat(layout.ChallengeTargets);
                foreach (Vector3 anchor in anchors)
                {
                    Assert.That(Physics.Raycast(anchor + Vector3.up * .5f, Vector3.down, out var hit, 1f, 1 << 8, QueryTriggerInteraction.Ignore),
                        Is.True, id + " unsupported content feet: " + anchor);
                    Assert.That(Mathf.Abs(hit.point.y - anchor.y), Is.LessThan(.2f), id + " floor mismatch: " + anchor);
                }
                foreach (Vector3 other in layout.PuzzleStatuePositions.Concat(layout.ChallengeTargets).Concat(new[] { layout.Npc, layout.Portal }))
                    Assert.That(Vector3.Distance(layout.BossCenter, other), Is.GreaterThan(16), "Eight-meter boss leash needs room away from other objectives.");
                if (id == M4RegionId.Teluna)
                {
                    var water = owner.GetComponentInChildren<WaterVolume>();
                    Assert.That(water, Is.Not.Null);
                    Assert.That(water.Contains(new Vector3(10, -1, 7)), Is.True);
                    Assert.That(water.Contains(layout.Spawn + Vector3.up), Is.False);
                    Assert.That(water.SurfaceY, Is.EqualTo(0).Within(.001f));
                }
                else Assert.That(owner.GetComponentsInChildren<ClimbableSurface>().Length, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(owner); Physics.SyncTransforms(); }
        }

        private static GameObject Create(List<GameObject> objects, string name, Vector3 position)
        {
            var item = new GameObject(name); item.transform.position = position; objects.Add(item); return item;
        }
        private static ElementalActor Actor(List<GameObject> objects, string name, Vector3 position, ActorTeam team)
        {
            var actor = Create(objects, name, position).AddComponent<ElementalActor>();
            actor.Configure(name, ElementType.Fire, team); return actor;
        }
        private static void DestroyAll(List<GameObject> objects)
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            Physics.SyncTransforms();
        }
    }
}
