using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.Game.World;
using Orbis.M1;
using Orbis.M2;
using Orbis.M4;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Orbis.Game.Tests
{
    /// <summary>Actual saved scene bindings and collision; optional views reuse the normal URP capture recipe.</summary>
    public sealed class WorldLandmarkTests
    {
        WorldVisualTests fixture;
        M4SceneBootstrap world;
        Terrain[] terrains;

        [UnitySetUp] public IEnumerator Setup()
        {
            fixture = new WorldVisualTests();
            yield return fixture.Setup();
            world = Object.FindAnyObjectByType<M4SceneBootstrap>();
            Assert.That(world, Is.Not.Null);
            var streamer = world.GetComponent<WorldRegionStreamer>();
            Assert.That(streamer, Is.Not.Null);
            yield return streamer.LoadAll();
            Assert.That(streamer.LoadedRegionCount, Is.EqualTo(5), streamer.LastError);
            terrains = world.GetComponentsInChildren<Terrain>();
            Assert.That(terrains.Length, Is.EqualTo(4));
            Physics.SyncTransforms();
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (fixture != null) yield return fixture.Cleanup();
            float deadline = Time.realtimeSinceStartup + 45;
            while (WorldRegionStreamer.OwnedSceneHandleCount != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(WorldRegionStreamer.OwnedSceneHandleCount, Is.Zero, "The inspection must release its five streamed scenes.");
        }

        [UnityTest] public IEnumerator SavedLandmarksPreserveRelocatedContentGroundAndAnimatedRotors()
        {
            Assert.That(world.IsInitialized, Is.True);
            Assert.That(world.Regions.Count, Is.EqualTo(5));
            Assert.That(world.GetComponentsInChildren<M4AuthoredRegion>(true).Length, Is.EqualTo(5));
            Assert.That(world.GetComponentsInChildren<FieldElementPuzzle>(true).Length, Is.EqualTo(5));
            Assert.That(world.GetComponentsInChildren<ChallengeRoom>(true).Length, Is.EqualTo(5));
            Assert.That(world.Regions.Count(r => r.Boss != null), Is.EqualTo(5));
            var ids = new HashSet<string>();
            var report = new Report();
            foreach (var region in world.IslandRegions)
            {
                var authored = region.Authored;
                Assert.DoesNotThrow(authored.Validate);
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(authored.Statues[i].SourceId, Is.EqualTo(region.Id + " Statue " + (i + 1)));
                    Assert.That(authored.Targets[i].SourceId, Is.EqualTo(region.Id + " Challenge " + (i + 1)));
                }
                foreach (var actor in authored.Statues.Concat(authored.Targets))
                {
                    Assert.That(ids.Add(actor.SourceId), Is.True, "Duplicate authored identity: " + actor.SourceId);
                    Assert.That(actor.gameObject.scene, Is.EqualTo(world.gameObject.scene), "Gameplay actors must remain resident.");
                    float height = Height(actor.transform.position);
                    Assert.That(actor.transform.position.y, Is.EqualTo(height).Within(.25f), "Floating/buried actor: " + actor.SourceId);
                    report.actors.Add(new ActorSample { id = actor.SourceId, feet = actor.transform.position, terrainHeight = height });
                }
                Vector3 puzzle = Mean(authored.Statues), challenge = Mean(authored.Targets);
                Assert.That(Horizontal(puzzle, region.Center.position), Is.GreaterThan(85));
                Assert.That(Horizontal(challenge, region.Center.position), Is.GreaterThan(89));
                Assert.That(Horizontal(puzzle, challenge), Is.InRange(59.9f, 180.1f));
                Assert.That(authored.ChallengeEntry.position.y, Is.EqualTo(Height(authored.ChallengeEntry.position)).Within(.25f));
                Assert.That(authored.Chest.position.y, Is.EqualTo(Height(authored.Chest.position)).Within(.25f));
                Vector3 boss = authored.BossObject.transform.position - region.Center.position;
                Vector3 spawn = authored.Spawn.position - region.Center.position;
                Assert.That(boss.x, Is.EqualTo(25).Within(.01f)); Assert.That(boss.z, Is.EqualTo(38).Within(.01f));
                Assert.That(spawn.x, Is.EqualTo(0).Within(.01f)); Assert.That(spawn.z, Is.EqualTo(-30).Within(.01f));
                Assert.That(authored.BossObject.transform.position.y, Is.EqualTo(Height(authored.BossObject.transform.position)).Within(.25f));
                // Runtime layout must point at the same moved scene objects, rather than stale hub coordinates.
                var runtime = world.Regions.Single(r => r.Id == region.Id);
                CollectionAssert.AreEqual(authored.Statues.Select(a => a.transform.position).ToArray(), runtime.Layout.PuzzleStatuePositions);
                CollectionAssert.AreEqual(authored.Targets.Select(a => a.transform.position).ToArray(), runtime.Layout.ChallengeTargets);
                Assert.That(runtime.Layout.ChallengeEntry, Is.EqualTo(authored.ChallengeEntry.position));
            }
            Assert.That(ids.Count, Is.EqualTo(30));

            var landmarks = Object.FindObjectsByType<WorldLandmark>(FindObjectsSortMode.None);
            Assert.That(landmarks.Length, Is.GreaterThanOrEqualTo(20));
            Assert.That(landmarks.Select(m => m.gameObject.scene).Distinct().Count(), Is.EqualTo(5));
            foreach (var marker in landmarks)
            {
                Assert.That(marker.gameObject.scene, Is.Not.EqualTo(world.gameObject.scene), "Landmark visuals belong to additive environment scenes.");
                Assert.That(marker.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0), marker.Label);
            }
            report.landmarkCount = landmarks.Length;
            var overlooks = landmarks.Where(m => m.Label.StartsWith("Climbable overlook", StringComparison.Ordinal)).Take(3).ToArray();
            Assert.That(overlooks.Length, Is.EqualTo(3), "Inspect three actual trail outcrops.");
            foreach (var marker in overlooks)
            {
                var renderers = marker.GetComponentsInChildren<Renderer>();
                Bounds bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                Vector3 origin = new Vector3(bounds.center.x, bounds.max.y + 3, bounds.center.z);
                Assert.That(Physics.Raycast(origin, Vector3.down, out var hit, bounds.size.y + 8, 1 << 8, QueryTriggerInteraction.Ignore), Is.True, marker.Label);
                Assert.That(hit.collider.GetComponentInParent<ClimbableSurface>(), Is.Not.Null, marker.Label + " has visible art but no climbable top collision.");
                Assert.That(hit.collider.gameObject.scene, Is.EqualTo(world.gameObject.scene), "Climbable collision must survive environment unload.");
                Assert.That(hit.point.y, Is.GreaterThan(Height(hit.point) + 1), "The probe should hit the outcrop, not bare terrain.");
                Vector3 approach = bounds.center + Vector3.right * (bounds.extents.x + 2);
                report.collision.Add(new CollisionSample { label = marker.Label, hit = hit.point, approach = new Vector3(approach.x, Height(approach), approach.z) });
            }

            var rotors = Object.FindObjectsByType<WorldWindmill>(FindObjectsSortMode.None);
            Assert.That(rotors.Length, Is.GreaterThanOrEqualTo(3));
            var rotations = rotors.Select(r => r.transform.localRotation).ToArray();
            for (int i = 0; i < 42; i++) yield return null;
            for (int i = 0; i < rotors.Length; i++)
            {
                Quaternion after = rotors[i].transform.localRotation;
                float angle = Quaternion.Angle(rotations[i], after);
                Assert.That(angle, Is.GreaterThan(.1f), "A loaded windmill rotor did not animate.");
                // Spinning around local Z preserves its front-facing local axis; no exact-speed implementation copy.
                Assert.That(Vector3.Angle(rotations[i] * Vector3.forward, after * Vector3.forward), Is.LessThan(.05f));
                report.rotors.Add(new RotorSample { name = rotors[i].name, degrees = angle });
            }
            Directory.CreateDirectory("TestResults/WorldDev");
            File.WriteAllText("TestResults/WorldDev/World03_RuntimeBindings.json", JsonUtility.ToJson(report, true));

            if (WorldVisualTests.Argument("-worldStep") == "World03_Detail" || WorldVisualTests.Argument("-worldStep") == "World03_After")
            {
                Vector3 camera = OnGround(new Vector3(-73, 0, -226), 3.5f);
                WorldVisualTests.Capture("World03_Detail", "VillageClose", camera, OnGround(new Vector3(-126, 0, -257), 5), 58);
                // Bounds come from the actual imported cascade renderer, not a second waterfall construction formula.
                var cascade = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Eastern Silverfall" && t.GetComponentInChildren<Renderer>() != null);
                Assert.That(cascade, Is.Not.Null);
                var cascadeRenderers = cascade.GetComponentsInChildren<Renderer>();
                Bounds waterBounds = cascadeRenderers[0].bounds; foreach (var renderer in cascadeRenderers.Skip(1)) waterBounds.Encapsulate(renderer.bounds);
                Vector3 focus = waterBounds.center;
                Vector3 view = focus + new Vector3(-58, 3, -48);
                view.y = Mathf.Max(view.y, Height(view) + 3);
                WorldVisualTests.Capture("World03_Detail", "WaterfallClose", view, focus, 58);
            }
        }

        float Height(Vector3 p)
        {
            foreach (var terrain in terrains)
            {
                Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
                if (p.x < origin.x || p.z < origin.z || p.x > origin.x + size.x || p.z > origin.z + size.z) continue;
                // Also use the actual TerrainCollider to catch an accidentally detached collision asset.
                var collider = terrain.GetComponent<TerrainCollider>();
                Assert.That(collider, Is.Not.Null); Assert.That(collider.terrainData, Is.SameAs(terrain.terrainData));
                Assert.That(collider.Raycast(new Ray(new Vector3(p.x, origin.y + size.y + 5, p.z), Vector3.down), out var hit, size.y + 10), Is.True);
                return hit.point.y;
            }
            Assert.Fail("No resident Terrain below " + p); return 0;
        }
        Vector3 OnGround(Vector3 p, float offset) { p.y = Height(p) + offset; return p; }
        static Vector3 Mean(ElementalActor[] actors) { Vector3 sum = Vector3.zero; foreach (var actor in actors) sum += actor.transform.position; return sum / actors.Length; }
        static float Horizontal(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        [Serializable] sealed class Report
        {
            public string note = "Actual saved-scene binding, terrain-collider and rotor checks. This is not a frame-time benchmark or a claim that the next landmark is always visible.";
            public int landmarkCount; public List<ActorSample> actors = new List<ActorSample>();
            public List<CollisionSample> collision = new List<CollisionSample>(); public List<RotorSample> rotors = new List<RotorSample>();
        }
        [Serializable] sealed class ActorSample { public string id; public Vector3 feet; public float terrainHeight; }
        [Serializable] sealed class CollisionSample { public string label; public Vector3 hit, approach; }
        [Serializable] sealed class RotorSample { public string name; public float degrees; }
    }
}
