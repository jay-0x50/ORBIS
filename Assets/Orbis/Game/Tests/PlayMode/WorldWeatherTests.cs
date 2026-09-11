using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.Game.World;
using Orbis.M1;
using Orbis.M4;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Orbis.Game.Tests
{
    public sealed class WorldWeatherTests
    {
        WorldVisualTests fixture;
        M4SceneBootstrap world;
        WorldWeather weather;
        WorldRegionStreamer streamer;
        GameObject probe;
        bool wasEnabled;
        WorldWeatherKind originalWeather;

        [UnitySetUp] public IEnumerator Setup()
        {
            fixture = new WorldVisualTests();
            yield return fixture.Setup();
            world = Object.FindAnyObjectByType<M4SceneBootstrap>();
            Assert.That(world, Is.Not.Null);
            weather = world.GetComponent<WorldWeather>();
            Assert.That(weather, Is.Not.Null, "Build world Step 5 before its actual-scene regression test.");
            Assert.That(weather.Effects, Is.Not.Null);
            Assert.That(weather.Effects.RainMaterial, Is.Not.Null);
            Assert.That(weather.Effects.StreakMaterial, Is.Not.Null);
            streamer = world.GetComponent<WorldRegionStreamer>();
            Assert.That(streamer, Is.Not.Null);
            wasEnabled = weather.enabled; originalWeather = weather.TargetWeather;
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (weather != null)
            {
                weather.enabled = wasEnabled;
                weather.SetWeather(originalWeather, 0);
            }
            if (probe != null) Object.Destroy(probe);
            if (fixture != null) yield return fixture.Cleanup();
            float deadline = Time.realtimeSinceStartup + 45;
            while (WorldRegionStreamer.OwnedSceneHandleCount != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(WorldRegionStreamer.OwnedSceneHandleCount, Is.Zero, "Weather inspection leaked environment scenes.");
        }

        [UnityTest] public IEnumerator WeatherBlendsContinuouslyAndSurvivesRealEnvironmentStreamingWithoutChangingTheParty()
        {
            var party = world.Party;
            var members = party.Members.ToArray();
            var identities = members.Select(m => m.Actor.SourceId).ToArray();
            var elements = members.Select(m => m.Actor.Element).ToArray();
            var auras = members.Select(m => m.Actor.AuraElement).ToArray();
            var activeMember = party.ActiveMember;
            var wind = world.GetComponent<WorldWind>();
            Assert.That(wind, Is.Not.Null); Assert.That(wind.Zone, Is.Not.Null);
            var zone = wind.Zone;
            var residentTerrain = world.GetComponentsInChildren<Terrain>();
            var bindings = world.GetComponentsInChildren<M4AuthoredRegion>();
            AssertSingleWeather();

            // Explicit advancement must not race Update's real unscaled delta in a busy shader/import frame.
            weather.enabled = false;
            float disabledWind = zone.windMain;
            weather.SetWeather(WorldWeatherKind.Clear, 0); Vector3 clear = State();
            weather.SetWeather(WorldWeatherKind.Thunderstorm, 0); Vector3 storm = State();
            Assert.That(storm.x, Is.GreaterThan(clear.x + .1f));
            Assert.That(storm.y, Is.GreaterThan(clear.y + .1f));
            weather.SetWeather(WorldWeatherKind.Clear, 0);
            weather.SetWeather(WorldWeatherKind.Thunderstorm, 6);
            Assert.That(weather.TargetWeather, Is.EqualTo(WorldWeatherKind.Thunderstorm));
            AssertState(clear);
            weather.Evaluate(3); Vector3 midpoint = State();
            Assert.That(midpoint.x, Is.GreaterThan(clear.x).And.LessThan(storm.x));
            Assert.That(midpoint.y, Is.GreaterThan(clear.y).And.LessThan(storm.y));
            weather.Evaluate(3); AssertState(storm);
            weather.Evaluate(20); AssertState(storm);
            Assert.That(zone.windMain, Is.EqualTo(disabledWind), "Disabled deterministic evaluation must not mutate the live wind.");
            CollectionAssert.AreEqual(auras, party.Members.Select(m => m.Actor.AuraElement).ToArray(), "Visual rain/storm must not apply gameplay elements.");

            float maximumSpatialDifference = 0;
            // Cross a grid of real biome boundaries in both axes, rather than testing one handpicked interior.
            for (int z = -850; z <= 850; z += 50) for (int x = -850; x <= 850; x += 50)
            {
                Vector3 p = new Vector3(x, 30, z);
                Vector3 a = Sample(p), b = Sample(p + Vector3.right), c = Sample(p + Vector3.forward);
                for (int channel = 0; channel < 3; channel++)
                {
                    Assert.That(a[channel], Is.InRange(0f, 1f));
                    float difference = Mathf.Max(Mathf.Abs(a[channel] - b[channel]), Mathf.Abs(a[channel] - c[channel]));
                    maximumSpatialDifference = Mathf.Max(maximumSpatialDifference, difference);
                    Assert.That(difference, Is.LessThan(.01f), "Weather must not jump at a one-metre biome boundary: " + p);
                }
            }

            yield return streamer.LoadAll();
            Assert.That(streamer.LoadedRegionCount, Is.EqualTo(5));
            AssertSingleWeather(); AssertState(storm);
            var samplesBeforeUnload = world.IslandRegions.Select(r => Sample(r.Center.position)).ToArray();
            probe = new GameObject("Weather streaming position probe");
            var catalog = streamer.Catalog;
            probe.transform.position = new Vector3(catalog.Regions.Max(r => r.WorldBounds.max.x) + catalog.UnloadDistance + 1000,
                0, catalog.Regions.Max(r => r.WorldBounds.max.z) + catalog.UnloadDistance + 1000);
            streamer.FollowTarget = probe.transform; streamer.HoldAllRegions = false;
            streamer.RefreshNow(); yield return streamer.WaitReady();
            Assert.That(streamer.LoadedRegionCount, Is.Zero);
            Assert.That(WorldRegionStreamer.OwnedSceneHandleCount, Is.Zero);
            AssertSingleWeather(); AssertState(storm);
            for (int i = 0; i < world.IslandRegions.Length; i++)
                Assert.That(Vector3.Distance(samplesBeforeUnload[i], Sample(world.IslandRegions[i].Center.position)), Is.LessThan(.00001f));
            CollectionAssert.AreEquivalent(residentTerrain, world.GetComponentsInChildren<Terrain>());
            CollectionAssert.AreEquivalent(bindings, world.GetComponentsInChildren<M4AuthoredRegion>());
            yield return streamer.LoadAll();
            Assert.That(streamer.LoadedRegionCount, Is.EqualTo(5));
            AssertSingleWeather(); AssertState(storm);
            streamer.FollowTarget = null;

            // WorldWeather.OnDisable restores the original wind. Check the live bridge while enabled.
            weather.enabled = true;
            weather.SetWeather(WorldWeatherKind.Clear, 0);
            yield return null; yield return null;
            float clearWind = zone.windMain;
            weather.SetWeather(WorldWeatherKind.StrongWind, 0);
            yield return null; yield return null;
            float strongWind = zone.windMain;
            Assert.That(wind.Zone, Is.SameAs(zone));
            Assert.That(strongWind, Is.GreaterThan(clearWind), "Strong wind must reach the existing WindZone.");
            Assert.That(world.Party, Is.SameAs(party));
            CollectionAssert.AreEqual(members, party.Members);
            CollectionAssert.AreEqual(identities, party.Members.Select(m => m.Actor.SourceId).ToArray());
            CollectionAssert.AreEqual(elements, party.Members.Select(m => m.Actor.Element).ToArray());
            Assert.That(party.ActiveMember, Is.SameAs(activeMember));

            if (WorldVisualTests.Argument("-worldStep") == "World05_After")
            {
                Capture(WorldWeatherKind.Rain, "Rain", 2.4f);
                Capture(WorldWeatherKind.StrongWind, "StrongWind", 2.4f);
                // The authored thunder flash phase is inspected at the requested presentation time.
                Capture(WorldWeatherKind.Thunderstorm, "Thunderstorm", 4.08f);
                Capture(WorldWeatherKind.Thunderstorm, "ThunderstormCloud", 2.4f);
            }
            Directory.CreateDirectory("TestResults/WorldDev");
            File.WriteAllText("TestResults/WorldDev/World05_WeatherChecks.json", JsonUtility.ToJson(new Report
            {
                clear = clear, halfway = midpoint, thunderstorm = storm,
                maximumOneMetreChange = maximumSpatialDifference, clearWind = clearWind, strongWind = strongWind,
                partyIds = identities, environmentLoads = new[] { 5, 0, 5 }
            }, true));
        }

        void Capture(WorldWeatherKind kind, string name, float time)
        {
            weather.SetWeather(kind, 0); weather.Evaluate(time);
            // Same camera recipe as the accepted MeadowHighland view. Camera-relative weather must also
            // render in this inspection camera, even though the player's camera remains at the village.
            // Keeping the view name also reuses WorldVisualTests' saved baseline height correction.
            WorldVisualTests.Capture("World05_After_" + name, "MeadowHighland", new Vector3(-265, 55, -350), new Vector3(-415, 95, -500), 58);
        }
        Vector3 State() => new Vector3(weather.CurrentRain, weather.CurrentStorm, weather.CurrentWind);
        Vector3 Sample(Vector3 point)
        {
            WorldWeatherSample sample = weather.Sample(point);
            return new Vector3(sample.Rain, sample.Storm, sample.Wind);
        }
        void AssertState(Vector3 expected) => Assert.That(Vector3.Distance(State(), expected), Is.LessThan(.00001f));
        void AssertSingleWeather()
        {
            var all = Object.FindObjectsByType<WorldWeather>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(all.Length, Is.EqualTo(1)); Assert.That(all[0], Is.SameAs(weather));
            Assert.That(weather.gameObject, Is.SameAs(world.gameObject));
            Assert.That(weather.gameObject.scene, Is.EqualTo(world.gameObject.scene));
        }
        [Serializable] sealed class Report
        {
            public string note = "Actual world scene, deterministic weather progression, biome samples and real 5→0→5 Addressables loading. Presentation only; not a performance benchmark.";
            public Vector3 clear, halfway, thunderstorm;
            public float maximumOneMetreChange, clearWind, strongWind;
            public string[] partyIds; public int[] environmentLoads;
        }
    }
}
