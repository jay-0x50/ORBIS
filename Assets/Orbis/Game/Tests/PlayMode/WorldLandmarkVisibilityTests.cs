using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.Game.World;
using Orbis.M4;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Orbis.Game.Tests
{
    /// <summary>Sampled solid-geometry sightlines, explicitly not proof of continuous visual readability.</summary>
    public sealed class WorldLandmarkVisibilityTests
    {
        // Inspection defaults: 40m samples, standing eye, the accepted 58-degree/1080p world review lens.
        const float Spacing = 40, EyeHeight = 1.8f, Range = 650, MinimumPixels = 12;
        const float GroundEdgeEpsilon = .001f; // One millimetre per shared-edge axis; never a general missing-ground fallback.
        WorldVisualTests fixture;
        M4SceneBootstrap world;
        Terrain[] terrain;
        Camera camera;
        readonly Plane[] planes = new Plane[6];

        [UnitySetUp] public IEnumerator Setup()
        {
            fixture = new WorldVisualTests(); yield return fixture.Setup();
            world = Object.FindAnyObjectByType<M4SceneBootstrap>(); Assert.That(world, Is.Not.Null);
            yield return world.GetComponent<WorldRegionStreamer>().LoadAll();
            terrain = world.GetComponentsInChildren<Terrain>(); Assert.That(terrain.Length, Is.EqualTo(4));
            camera = new GameObject("Route sightline inspection camera").AddComponent<Camera>();
            camera.enabled = false; camera.fieldOfView = 58; camera.aspect = 16f / 9;
            camera.nearClipPlane = .1f; camera.farClipPlane = Range;
            Physics.SyncTransforms();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (camera != null) Object.Destroy(camera.gameObject);
            if (fixture != null) yield return fixture.Cleanup();
            float deadline = Time.realtimeSinceStartup + 45;
            while (WorldRegionStreamer.OwnedSceneHandleCount != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(WorldRegionStreamer.OwnedSceneHandleCount, Is.Zero);
        }

        [UnityTest] public IEnumerator ReportActualRouteSightlinesAndUnseenIntervalsWithoutAssumingEveryLandmarkIsVisible()
        {
            var markers = Object.FindObjectsByType<WorldLandmark>(FindObjectsSortMode.None);
            Assert.That(markers.Length, Is.GreaterThanOrEqualTo(20));
            var colliderGroups = world.GetComponentsInChildren<MeshCollider>().Where(c => c.enabled && c.sharedMesh != null)
                .GroupBy(c => c.sharedMesh).ToDictionary(g => g.Key, g => g.ToArray());
            var targets = markers.Select(m => BuildTarget(m, colliderGroups)).OrderBy(t => t.Id, StringComparer.Ordinal).ToArray();
            foreach (var target in targets)
                Assert.That(target.Colliders.Length, Is.GreaterThan(0), "No corresponding persistent collision for " + target.Id);
            Vector3[] guide = AuthoringRoute();
            var route = Resample(guide, out float length);
            Assert.That(route.Count, Is.GreaterThan(20)); Assert.That(Finite(length), Is.True);
            var report = new Report { routeLengthMetres = length, landmarks = targets.Select(t => t.Describe()).ToArray() };
            foreach (int direction in new[] { 1, -1 })
            {
                var rows = new List<Row>();
                for (int i = 0; i < route.Count; i++)
                {
                    int index = direction > 0 ? i : (route.Count - i) % route.Count;
                    var point = route[index];
                    Vector3 eye = point.Position; eye.y = GroundHeight(eye, out var groundProbe) + EyeHeight;
                    Vector3 forward = point.Forward * direction;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(forward, Vector3.up));
                    GeometryUtility.CalculateFrustumPlanes(camera, planes);
                    var row = new Row { pathMetres = direction > 0 ? point.Distance : index == 0 ? 0 : length - point.Distance,
                        eye = eye, forward = forward, groundProbe = groundProbe, trailWeight = TrailWeight(eye) };
                    foreach (var target in targets)
                    {
                        float distance = Vector3.Distance(eye, target.Bounds.center);
                        if (distance > Range || distance < 2) continue;
                        float pixels = 1080f * 2 * Mathf.Atan(target.Bounds.size.y * .5f / distance) / (camera.fieldOfView * Mathf.Deg2Rad);
                        if (pixels < MinimumPixels) continue;
                        bool inFrustum = GeometryUtility.TestPlanesAABB(planes, target.Bounds);
                        var candidate = Probe(target, eye, inFrustum);
                        candidate.id = target.Id; candidate.distance = distance; candidate.projectedHeightPixels = pixels;
                        row.candidates.Add(candidate);
                    }
                    row.forwardVisible = row.candidates.Where(c => c.forwardVisible).Select(c => c.id).ToArray();
                    row.turnableVisible = row.candidates.Where(c => c.clearRays > 0).Select(c => c.id).ToArray();
                    Assert.That(Finite(eye.x) && Finite(eye.y) && Finite(eye.z), Is.True);
                    rows.Add(row);
                }
                var run = new DirectionReport { direction = direction > 0 ? "Forward" : "Reverse", samples = rows.ToArray() };
                run.forwardGaps = Gaps(rows, length, false); run.turnableGaps = Gaps(rows, length, true);
                run.forwardCoverage = rows.Count(r => r.forwardVisible.Length > 0) / (float)rows.Count;
                run.turnableCoverage = rows.Count(r => r.turnableVisible.Length > 0) / (float)rows.Count;
                report.directions.Add(run);
            }
            // Data-integrity assertions only. A long unseen interval is an actionable audit result, not a suppressed failure.
            Assert.That(report.directions.SelectMany(r => r.samples).All(r => Finite(r.pathMetres)), Is.True);
            Directory.CreateDirectory("TestResults/WorldDev");
            File.WriteAllText("TestResults/WorldDev/World06_LandmarkVisibility.json", JsonUtility.ToJson(report, true));
            string step = WorldVisualTests.Argument("-worldStep");
            if (step == "World06_Visibility" || step == "World06_After")
            {
                int shot = 0;
                foreach (var gap in report.directions.SelectMany(r => r.forwardGaps).OrderByDescending(g => g.lengthMetres).Take(3))
                    WorldVisualTests.Capture("World06_Visibility", "Gap" + (++shot), gap.inspectionEye,
                        gap.inspectionEye + gap.inspectionForward * 100, camera.fieldOfView);
            }
            foreach (var run in report.directions)
                Debug.Log("ORBIS_ROUTE_VISIBILITY " + run.direction + ": forward sampled coverage=" + run.forwardCoverage.ToString("P1") +
                    ", max sampled gap=" + (run.forwardGaps.Length == 0 ? 0 : run.forwardGaps.Max(g => g.lengthMetres)).ToString("F1") + "m; solid geometry only.");
            yield return null;
        }

        Target BuildTarget(WorldLandmark marker, Dictionary<Mesh, MeshCollider[]> colliderGroups)
        {
            var renderers = marker.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0), marker.Label);
            Bounds bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var owned = new HashSet<MeshCollider>();
            // Visuals and their collider copies live in DIFFERENT scenes. Parent/child identity cannot match them.
            foreach (var filter in marker.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !colliderGroups.TryGetValue(filter.sharedMesh, out var candidates)) continue;
                foreach (var collider in candidates)
                    if (SameTransform(filter.transform.localToWorldMatrix, collider.transform.localToWorldMatrix)) owned.Add(collider);
            }
            string id = marker.Region + "/" + marker.Label + "@" + marker.transform.position.x.ToString("F2", CultureInfo.InvariantCulture) +
                "," + marker.transform.position.z.ToString("F2", CultureInfo.InvariantCulture);
            return new Target { Id = id, Bounds = bounds, LookPoint = marker.LookPoint, Colliders = owned.ToArray() };
        }
        Candidate Probe(Target target, Vector3 eye, bool inFrustum)
        {
            var result = new Candidate();
            Bounds b = target.Bounds;
            Vector3 center = b.center;
            var points = new[] { target.LookPoint, center,
                new Vector3(center.x, b.min.y + b.size.y * .85f, center.z),
                new Vector3(center.x - b.extents.x * .65f, b.min.y + b.size.y * .55f, center.z),
                new Vector3(center.x + b.extents.x * .65f, b.min.y + b.size.y * .55f, center.z),
                new Vector3(center.x, b.min.y + b.size.y * .55f, center.z - b.extents.z * .65f),
                new Vector3(center.x, b.min.y + b.size.y * .55f, center.z + b.extents.z * .65f) };
            foreach (var point in points)
            {
                Vector3 delta = point - eye; if (delta.sqrMagnitude < .01f) continue;
                var ray = new Ray(eye, delta.normalized);
                float nearest = float.PositiveInfinity; RaycastHit ownHit = default;
                foreach (var collider in target.Colliders)
                    if (collider.Raycast(ray, out var hit, Range + b.size.magnitude) && hit.distance < nearest)
                    { nearest = hit.distance; ownHit = hit; }
                // A ray passing through the empty centre of an arch is not evidence that the arch is visible.
                if (float.IsPositiveInfinity(nearest)) { result.noSurfaceRays++; continue; }
                if (Physics.Raycast(ray, out var blocker, Mathf.Max(0, nearest - .04f), 1 << 8, QueryTriggerInteraction.Ignore)
                    && !target.Colliders.Contains(blocker.collider))
                {
                    result.blockedRays++;
                    if (string.IsNullOrEmpty(result.firstBlocker)) result.firstBlocker = blocker.collider.gameObject.scene.name + "/" + blocker.collider.name;
                    continue;
                }
                result.clearRays++;
                Vector3 viewport = camera.WorldToViewportPoint(ownHit.point);
                if (inFrustum && viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1)
                    result.forwardVisible = true;
            }
            return result;
        }
        static bool SameTransform(Matrix4x4 a, Matrix4x4 b)
        { for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > .02f) return false; return true; }
        float GroundHeight(Vector3 point, out Vector3 groundProbe)
        {
            // PhysX may reject the outermost edge of an individual heightfield. Test every tile sharing
            // the exact X/Z before nudging, so array order does not decide which half of a seam exists.
            var candidates = terrain.Where(t => Contains(t, point)).ToArray();
            Assert.That(candidates.Length, Is.GreaterThan(0), "Route leaves resident terrain at " + point.ToString("F6"));
            foreach (var tile in candidates)
                if (TryGroundRay(tile, point, out groundProbe)) return groundProbe.y;
            // Only a shared tile boundary may use this epsilon. Interior misses and holes spanning the
            // boundary still fail. Save the actual hit point in every JSON row rather than hiding the offset.
            if (candidates.Length > 1)
            {
                foreach (var tile in candidates)
                {
                    Vector3 origin = tile.transform.position, size = tile.terrainData.size, inset = point;
                    if (candidates.Any(t => t.transform.position.x != origin.x))
                        inset.x = Mathf.Clamp(point.x, origin.x + GroundEdgeEpsilon, origin.x + size.x - GroundEdgeEpsilon);
                    if (candidates.Any(t => t.transform.position.z != origin.z))
                        inset.z = Mathf.Clamp(point.z, origin.z + GroundEdgeEpsilon, origin.z + size.z - GroundEdgeEpsilon);
                    if (TryGroundRay(tile, inset, out groundProbe)) return groundProbe.y;
                }
            }
            Assert.Fail("No actual TerrainCollider surface at route X/Z " + point.ToString("F6") +
                "; containing tiles: " + string.Join(", ", candidates.Select(t => t.name)) +
                ". Exact rays failed; a shared-edge-only 0.001m-per-axis inset is the sole permitted retry.");
            groundProbe = default; return 0;
        }
        static bool TryGroundRay(Terrain tile, Vector3 point, out Vector3 groundProbe)
        {
            Vector3 origin = tile.transform.position, size = tile.terrainData.size;
            var collider = tile.GetComponent<TerrainCollider>();
            Assert.That(collider, Is.Not.Null, tile.name); Assert.That(collider.enabled, Is.True, tile.name);
            Assert.That(collider.terrainData, Is.SameAs(tile.terrainData), tile.name);
            bool found = collider.Raycast(new Ray(new Vector3(point.x, origin.y + size.y + 5, point.z), Vector3.down), out var hit, size.y + 10);
            groundProbe = hit.point; return found;
        }
        float TrailWeight(Vector3 point)
        {
            var tile = TileAt(point); var data = tile.terrainData; Vector3 o = tile.transform.position;
            int x = Mathf.Clamp(Mathf.RoundToInt((point.x - o.x) / data.size.x * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((point.z - o.z) / data.size.z * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);
            return data.alphamapLayers > 5 ? data.GetAlphamaps(x, z, 1, 1)[0, 0, 5] : 0;
        }
        Terrain TileAt(Vector3 p)
        {
            foreach (var tile in terrain)
                if (Contains(tile, p)) return tile;
            Assert.Fail("Route leaves the actual resident terrain: " + p); return null;
        }
        static bool Contains(Terrain tile, Vector3 p)
        {
            Vector3 o = tile.transform.position, s = tile.terrainData.size;
            return p.x >= o.x && p.z >= o.z && p.x <= o.x + s.x && p.z <= o.z + s.z;
        }
        static Vector3[] AuthoringRoute()
        {
            #if UNITY_EDITOR
            var type = Type.GetType("Orbis.Game.Editor.IslandTerrainBuilder, Orbis.Game.Editor");
            Assert.That(type, Is.Not.Null);
            var method = type.GetMethod("RoutePoints"); Assert.That(method, Is.Not.Null);
            // Only the original road's X/Z guide is read. Every Y comes from the saved TerrainCollider.
            return (Vector3[])method.Invoke(null, null);
            #else
            Assert.Ignore("This editor audit uses the island authoring route guide; it is not a standalone runtime benchmark.");
            return Array.Empty<Vector3>();
            #endif
        }
        static List<RoutePoint> Resample(Vector3[] guide, out float length)
        {
            Assert.That(guide.Length, Is.GreaterThan(2));
            Assert.That(Vector2.Distance(new Vector2(guide[0].x, guide[0].z), new Vector2(guide[guide.Length - 1].x, guide[guide.Length - 1].z)),
                Is.LessThan(.1f), "Unseen-interval wrapping requires the authored closed island circuit.");
            var distances = new float[guide.Length];
            for (int i = 1; i < guide.Length; i++) distances[i] = distances[i - 1] + Vector2.Distance(new Vector2(guide[i - 1].x, guide[i - 1].z), new Vector2(guide[i].x, guide[i].z));
            length = distances[distances.Length - 1]; var result = new List<RoutePoint>(); int segment = 0;
            for (float at = 0; at < length; at += Spacing)
            {
                while (segment + 1 < distances.Length - 1 && distances[segment + 1] < at) segment++;
                Vector3 a = guide[segment], b = guide[segment + 1]; a.y = b.y = 0;
                result.Add(new RoutePoint { Position = Vector3.Lerp(a, b, Mathf.InverseLerp(distances[segment], distances[segment + 1], at)),
                    Forward = (b - a).normalized, Distance = at });
            }
            return result;
        }
        static Gap[] Gaps(List<Row> rows, float length, bool turnable)
        {
            bool Seen(int i) => (turnable ? rows[i].turnableVisible.Length : rows[i].forwardVisible.Length) > 0;
            int anchor = rows.FindIndex(r => (turnable ? r.turnableVisible.Length : r.forwardVisible.Length) > 0);
            if (anchor < 0) return new[] { new Gap { lengthMetres = length, failedSamples = rows.Count,
                inspectionEye = rows[rows.Count / 2].eye, inspectionForward = rows[rows.Count / 2].forward } };
            var result = new List<Gap>(); int first = -1, count = 0; float accumulated = 0;
            for (int step = 1; step <= rows.Count; step++)
            {
                int i = (anchor + step) % rows.Count;
                if (!Seen(i))
                {
                    if (first < 0) first = i;
                    int next = (i + 1) % rows.Count;
                    accumulated += next > i ? rows[next].pathMetres - rows[i].pathMetres : length - rows[i].pathMetres;
                    count++;
                }
                else if (first >= 0)
                {
                    var middle = rows[(first + count / 2) % rows.Count];
                    result.Add(new Gap { startPathMetres = rows[first].pathMetres, lengthMetres = accumulated,
                        failedSamples = count, inspectionEye = middle.eye, inspectionForward = middle.forward });
                    first = -1; count = 0; accumulated = 0;
                }
            }
            return result.ToArray();
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        sealed class RoutePoint { public Vector3 Position, Forward; public float Distance; }
        sealed class Target
        {
            public string Id; public Bounds Bounds; public Vector3 LookPoint; public MeshCollider[] Colliders;
            public Landmark Describe() => new Landmark { id = Id, center = Bounds.center, size = Bounds.size,
                lookPoint = LookPoint, lookPointInsideBounds = Bounds.Contains(LookPoint), matchedColliders = Colliders.Length };
        }
        [Serializable] sealed class Report
        {
            public string note = "Potential visibility against solid layer-8 geometry only. Leaves/alpha cards, fog, lighting, real LOD silhouettes and intervening gameplay actors are not visibility proof. Unseen intervals are sampled estimates (40m resolution), not assertions that a next landmark is always visible.";
            public string routeSource = "Original IslandTerrainBuilder road X/Z guide; actual saved TerrainCollider heights; Trail alpha recorded for guide/paint mismatch review. Side branches are outside this circuit audit.";
            public string groundSampling = "Exact TerrainCollider rays try every containing tile. Only after all exact rays fail at a shared boundary, a maximum 0.001m inset per shared X/Z axis is permitted. Interior misses remain failures; each row records its actual groundProbe hit position.";
            public float routeLengthMetres, sampleSpacingMetres = Spacing, eyeHeightMetres = EyeHeight, maxRangeMetres = Range, minimumProjectedHeightPixels = MinimumPixels;
            public Landmark[] landmarks; public List<DirectionReport> directions = new List<DirectionReport>();
        }
        [Serializable] sealed class Landmark { public string id; public Vector3 center, size, lookPoint; public bool lookPointInsideBounds; public int matchedColliders; }
        [Serializable] sealed class DirectionReport
        { public string direction; public float forwardCoverage, turnableCoverage; public Row[] samples; public Gap[] forwardGaps, turnableGaps; }
        [Serializable] sealed class Row
        { public float pathMetres, trailWeight; public Vector3 eye, forward, groundProbe; public string[] forwardVisible, turnableVisible; public List<Candidate> candidates = new List<Candidate>(); }
        [Serializable] sealed class Candidate
        { public string id, firstBlocker; public float distance, projectedHeightPixels; public bool forwardVisible; public int clearRays, blockedRays, noSurfaceRays; }
        [Serializable] sealed class Gap
        { public float startPathMetres, lengthMetres; public int failedSamples; public Vector3 inspectionEye, inspectionForward; }
    }
}
