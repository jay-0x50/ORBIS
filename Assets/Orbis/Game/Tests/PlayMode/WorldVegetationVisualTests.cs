using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.Game.World;
using Orbis.M0;
using Orbis.M4;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Orbis.Game.Tests
{
    /// <summary>Loads the actual island and authored foliage. PNGs are opt-in, never a full-suite side effect.</summary>
    public sealed class WorldVegetationVisualTests
    {
        const string CaptureStage = "World02_Dynamics";
        readonly WorldVisualTests fixture = new WorldVisualTests();

        [UnitySetUp] public IEnumerator Setup() => fixture.Setup();
        [UnityTearDown] public IEnumerator Cleanup() => fixture.Cleanup();

        [UnityTest]
        public IEnumerator AuthoredVegetationUsesWindZoneAndTheControlledExplorer()
        {
            var world = Object.FindAnyObjectByType<M4SceneBootstrap>();
            Assert.That(world, Is.Not.Null);
            var stream = world.GetComponent<WorldRegionStreamer>();
            Assert.That(stream, Is.Not.Null);
            yield return stream.LoadAll();
            Assert.That(stream.LoadedRegionCount, Is.EqualTo(5));
            var wind = world.GetComponent<WorldWind>();
            Assert.That(wind, Is.Not.Null);
            Assert.That(wind.Zone, Is.Not.Null);
            Assert.That(wind.Zone.mode, Is.EqualTo(WindZoneMode.Directional));
            var controller = world.GetComponentInChildren<CharacterController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(world.Party.ActiveMember.Actor.SourceId, Is.EqualTo("stella"));
            wind.RefreshNow();
            Assert.That(wind.PlayerController, Is.SameAs(controller));
            Assert.That(Shader.GetGlobalFloat("_OrbisPlayerRadius"), Is.EqualTo(1.25f).Within(.001f));
            AssertPawnPosition(controller);

            var fields = Object.FindObjectsByType<WorldGrassField>();
            Assert.That(fields.Length, Is.EqualTo(5));
            Assert.That(fields.All(x => x.Data != null && x.Data.InstanceCount > 0), Is.True);
            Assert.That(fields.All(x => x.InstanceMaterial != null && x.InstanceMaterial.enableInstancing), Is.True);
            var shader = fields[0].InstanceMaterial.shader;
            Assert.That(shader.name, Is.EqualTo("Orbis/World/FoliageToon"));
            Assert.That(shader.isSupported, Is.True);
            var tree = NearestTree(controller.transform.position);
            var lods = tree.GetLODs();
            Assert.That(lods.Length, Is.EqualTo(3));
            Assert.That(tree.fadeMode, Is.EqualTo(LODFadeMode.CrossFade));
            Assert.That(lods.All(x => x.renderers.Length == 1), Is.True);
            Assert.That(lods[2].renderers[0].sharedMaterial.GetFloat("_BillboardMode"), Is.EqualTo(1));
            Assert.That(lods[2].renderers[0].sharedMaterial.GetTexture("_BillboardAtlas"), Is.Not.Null);

            Vector3 originalPosition = controller.transform.position;
            Quaternion originalRotation = controller.transform.rotation;
            var input = controller.GetComponent<M0Input>();
            bool wasLocked = input.PresentationLocked;
            Vector4 originalWind = ReadZone(wind.Zone);
            var weather = world.GetComponent<WorldWeather>();
            bool weatherOwnsWind = weather != null && weather.isActiveAndEnabled;
            WorldWeatherKind originalWeather = weather != null ? weather.TargetWeather : WorldWeatherKind.Clear;
            bool weatherChanged = false;
            float originalScale = Time.timeScale;
            Vector3 grass = SafeGrassPoint(fields, originalPosition);
            float health = world.Vitals.Health;
            try
            {
                input.SetPresentationLocked(true);
                world.Warp(grass + Vector3.up * .08f);
                yield return null;
                wind.RefreshNow();
                AssertPawnPosition(controller);
                Assert.That(Shader.GetGlobalFloat("_OrbisPlayerRadius"), Is.GreaterThan(0));
                Assert.That(world.Vitals.Health, Is.EqualTo(health), "The inspection site must be outside encounters.");
                var expected = ReadZone(wind.Zone);
                Assert.That(Vector4.Distance(Shader.GetGlobalVector("_OrbisWindParams"), expected), Is.LessThan(.0001f));

                // Full suites stop at the integration checks. Explicit visual runs additionally inspect actual renders.
                if (WorldVisualTests.Argument("-worldStep") != CaptureStage) yield break;
                Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
                tree = NearestTree(grass);
                var sourceRenderer = tree.GetLODs()[0].renderers[0];
                float height = Mathf.Max(5, sourceRenderer.bounds.size.y);
                Vector3 focus = tree.transform.position + Vector3.up * height * .48f;
                Vector3 camera = tree.transform.position + new Vector3(.8f, .7f, -1.65f) * height;
                camera.y = Mathf.Max(camera.y, GroundAt(camera) + 1.2f);
                // Same real tree/camera for all three authored representations; ForceLOD is diagnostic only.
                // This deliberate comparison is separate from distance-driven LOD behaviour in normal gameplay.
                Time.timeScale = 0;
                for (int i = 0; i < 3; i++)
                {
                    tree.ForceLOD(i);
                    WorldVisualTests.Capture(CaptureStage, "Tree_ForcedLOD" + i, camera, focus, 42);
                }
                tree.ForceLOD(0);
                ApplyZone(wind.Zone, originalWind); wind.RefreshNow();
                WorldVisualTests.Capture(CaptureStage, "Wind_Normal", camera, focus, 42);
                // Step 5 owns this WindZone every Update. Use that live controller so the 42-frame
                // second sample keeps strong wind; older scenes without weather retain the direct diagnostic.
                if (weatherOwnsWind)
                {
                    weather.SetWeather(WorldWeatherKind.StrongWind, 0);
                    weatherChanged = true;
                }
                else ApplyZone(wind.Zone, new Vector4(2.4f, 1.2f, .35f, .22f));
                wind.RefreshNow();
                Vector4 strongWind = ReadZone(wind.Zone);
                WorldVisualTests.Capture(CaptureStage, "Wind_Strong_A", camera, focus, 42);
                Time.timeScale = 1;
                for (int i = 0; i < 42; i++) yield return null;
                Time.timeScale = 0;
                wind.RefreshNow();
                Assert.That(Vector4.Distance(ReadZone(wind.Zone), strongWind), Is.LessThan(.0001f),
                    "Both strong-wind images must retain the same wind through the intervening frames.");
                WorldVisualTests.Capture(CaptureStage, "Wind_Strong_B", camera, focus, 42);
                tree.ForceLOD(-1);

                // Zero wind isolates player bending. The live pawn and its real controller supply the globals.
                // These synchronous captures share one frame, so no weather Update can overwrite zero wind.
                if (weatherChanged) weather.SetWeather(WorldWeatherKind.Clear, 0);
                ApplyZone(wind.Zone, Vector4.zero); wind.RefreshNow();
                Vector3 grassFocus = grass + Vector3.up * .48f;
                Vector3 grassCamera = grass + new Vector3(2.7f, 2.0f, -3.4f);
                grassCamera.y = Mathf.Max(grassCamera.y, GroundAt(grassCamera) + .8f);
                Vector3 outside = grass + new Vector3(-4, 0, 1);
                outside.y = GroundAt(outside) + .08f;
                world.Warp(outside); wind.RefreshNow();
                WorldVisualTests.Capture(CaptureStage, "Grass_PlayerOutside", grassCamera, grassFocus, 45);
                world.Warp(grass + Vector3.up * .08f); wind.RefreshNow();
                AssertPawnPosition(controller);
                WorldVisualTests.Capture(CaptureStage, "Grass_PlayerInside", grassCamera, grassFocus, 45);
                Assert.That(fields.Sum(x => x.VisibleInstanceCount), Is.GreaterThan(0), "The review camera must actually submit authored grass.");
                // Same frame, pawn, camera and no wind: removing only the bridge gives a useful bend-only control.
                wind.enabled = false;
                try
                {
                    Assert.That(Shader.GetGlobalFloat("_OrbisPlayerRadius"), Is.Zero);
                    WorldVisualTests.Capture(CaptureStage, "Grass_SamePawn_BendOffControl", grassCamera, grassFocus, 45);
                }
                finally { wind.enabled = true; wind.RefreshNow(); }
                Assert.That(world.Vitals.Health, Is.EqualTo(health));
                #if UNITY_EDITOR
                Assert.That(UnityEditor.ShaderUtil.ShaderHasError(shader), Is.False, "Actual render requests must compile the foliage shader.");
                #endif
                Directory.CreateDirectory("TestResults/WorldDev");
                File.WriteAllText("TestResults/WorldDev/World02_Dynamics_Notes.txt",
                    "Actual Unity island, saved Stella selection and controlled CharacterController.\n" +
                    "Tree: " + tree.name + "; scene: " + tree.gameObject.scene.name + "; world position: " + tree.transform.position.ToString("F3") + "\n" +
                    "Tree_ForcedLOD0/1/2 share one camera. Forced representation comparison, not a distance-transition benchmark.\n" +
                    "Wind_Normal/Strong_A share camera and simulation time; Strong_B advances 42 actual frames.\n" +
                    "Strong wind source: " + (weatherOwnsWind ? "live WorldWeather.StrongWind" : "direct WindZone diagnostic (no active weather)") + ".\n" +
                    "Grass site: " + grass.ToString("F3") + ". Outside/Inside move the actual pawn.\n" +
                    "Grass_SamePawn_BendOffControl preserves the Inside pawn pose/camera/time while disabling only WorldWind. Wind is zero for all grass images.\n" +
                    "No actor, tree or blade surrogate was generated for these images. Images are visual evidence, not a performance benchmark.\n");
            }
            finally
            {
                if (tree != null) tree.ForceLOD(-1);
                wind.enabled = true;
                ApplyZone(wind.Zone, originalWind);
                // This fresh-scene fixture starts Clear; restore its weather after the optional diagnostics.
                if (weatherChanged && weather != null) weather.SetWeather(originalWeather, 0);
                world.Warp(originalPosition); controller.transform.rotation = originalRotation;
                input.SetPresentationLocked(wasLocked);
                Time.timeScale = originalScale;
                wind.RefreshNow();
            }
        }

        static void AssertPawnPosition(CharacterController controller)
        {
            Vector3 global = Shader.GetGlobalVector("_OrbisPlayerPosition");
            Assert.That(Vector3.Distance(global, controller.transform.position), Is.LessThan(.0001f));
        }
        static Vector4 ReadZone(WindZone zone) => new Vector4(zone.windMain, zone.windTurbulence, zone.windPulseMagnitude, zone.windPulseFrequency);
        static void ApplyZone(WindZone zone, Vector4 p)
        { zone.windMain = p.x; zone.windTurbulence = p.y; zone.windPulseMagnitude = p.z; zone.windPulseFrequency = p.w; }
        static LODGroup NearestTree(Vector3 point)
        {
            var trees = Object.FindObjectsByType<LODGroup>().Where(x => x.name.StartsWith("CommonTree_3 / ", StringComparison.Ordinal)).ToArray();
            Assert.That(trees, Is.Not.Empty, "The actual authored CommonTree population is required.");
            return trees.OrderBy(x => (x.transform.position - point).sqrMagnitude).First();
        }
        static Vector3 SafeGrassPoint(WorldGrassField[] fields, Vector3 origin)
        {
            var bosses = Object.FindObjectsByType<M4FieldBoss>();
            var terrains = Object.FindObjectsByType<Terrain>();
            float best = float.MaxValue; Vector3 result = default; bool found = false;
            foreach (var field in fields) foreach (var cell in field.Data.Cells) foreach (var placement in cell.Instances)
            {
                var p = placement.Position; float distance = (p - origin).sqrMagnitude;
                if (distance >= best || p.y < 3) continue;
                if (bosses.Any(b => HorizontalDistance(b.transform.position, p) < Mathf.Max(60, b.Profile.LeashRadius + 10))) continue;
                if (terrains.Any(t => Contains(t, p) && t.terrainData.GetSteepness(
                    (p.x - t.transform.position.x) / t.terrainData.size.x, (p.z - t.transform.position.z) / t.terrainData.size.z) > 15)) continue;
                best = distance; result = p; found = true;
            }
            Assert.That(found, Is.True, "A real grass placement outside all boss leashes is required.");
            return result;
        }
        static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static bool Contains(Terrain t, Vector3 p) => p.x >= t.transform.position.x && p.z >= t.transform.position.z
            && p.x <= t.transform.position.x + t.terrainData.size.x && p.z <= t.transform.position.z + t.terrainData.size.z;
        static float GroundAt(Vector3 p)
        {
            foreach (var terrain in Object.FindObjectsByType<Terrain>())
                if (Contains(terrain, p)) return terrain.SampleHeight(p) + terrain.transform.position.y;
            Assert.Fail("Review position is outside the authored island terrain."); return p.y;
        }
    }
}
