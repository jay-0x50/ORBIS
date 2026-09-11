using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.M1;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orbis.Game.Editor
{
    /// <summary>Relocates the existing M4 composites; no replacement actors or gameplay configuration.</summary>
    public static class WorldContentRedistribution
    {
        public const string AuditPath = "TestResults/WorldDev/World03_ContentLayout.json";
        // Provisional art dimensions in metres. The 65m existing gameplay hubs are never sculpted.
        const float ProtectedRadius = 65, PuzzleInner = 12, PuzzleOuter = 20;
        const float ChallengeInner = 14, ChallengeOuter = 24, BranchCore = 2, BranchOuter = 5;
        // Mountain/coastal terrain constrains safe directions. Keep the 20m + 24m feather circles
        // separated by at least 16m, allowing a wider pair instead of flattening a steep hillside.
        const float MinimumSeparation = 60, MaximumSeparation = 180;
        // Rise/run limits: 23 degrees along a path, 27 degrees at a feathered pad edge.
        const float MaximumPathGrade = .42f, MaximumPadGrade = .50f;

        public static List<Vector4> Apply(M4SceneBootstrap world)
        {
            if (EditorApplication.isPlaying || world == null || world.IsInitialized)
                throw new InvalidOperationException("Redistribute the saved island outside Play Mode.");
            var regions = world.IslandRegions;
            if (regions == null || regions.Length != 5 || regions.Any(r => r == null) || regions.Select(r => r.Id).Distinct().Count() != 5)
                throw new InvalidOperationException("Exactly five existing island region bindings are required.");
            var ground = new Ground(world.GetComponentsInChildren<Terrain>(true));
            var snapshots = new List<BindingSnapshot>();
            var plans = new List<Plan>();
            // Preflight every site before moving any object or changing any TerrainData.
            foreach (var region in regions)
            {
                ValidateComposite(region);
                snapshots.Add(new BindingSnapshot(region));
                Vector3 shrine = Average(region.Authored.Statues), trial = Average(region.Authored.Targets);
                bool shrineOutside = Distance(shrine, region.Center.position) > ProtectedRadius + PuzzleOuter;
                bool trialOutside = Distance(trial, region.Center.position) > ProtectedRadius + ChallengeOuter;
                if (shrineOutside || trialOutside)
                {
                    // The authored transforms are the persisted placement. Reusing them also avoids repeatedly
                    // applying a feather to already modified terrain when an art-only rebuild is requested.
                    if (!shrineOutside || !trialOutside || !PairFits(shrine, trial))
                        throw new InvalidOperationException(region.Id + ": partially relocated content; inspect the existing authored roots.");
                    var a = Existing(region, shrine, true, ground, regions);
                    var b = Existing(region, trial, false, ground, regions);
                    plans.Add(a); plans.Add(b);
                    continue;
                }
                var puzzles = Candidates(region, true, ground, regions);
                var challenges = Candidates(region, false, ground, regions);
                Plan bestPuzzle = null, bestChallenge = null; float best = float.PositiveInfinity;
                foreach (var a in puzzles) foreach (var b in challenges)
                {
                    if (!PairFits(a.Center, b.Center)) continue;
                    float score = a.Score + b.Score + Mathf.Abs(Distance(a.Center, b.Center) - 105) * .012f;
                    if (score >= best) continue;
                    best = score; bestPuzzle = a; bestChallenge = b;
                }
                if (bestPuzzle == null)
                    throw new InvalidOperationException(region.Id + ": no safe 60–180m clearing pair (puzzle candidates " +
                        puzzles.Count + ", challenge candidates " + challenges.Count + "). No content or terrain has changed.");
                plans.Add(bestPuzzle); plans.Add(bestChallenge);
            }

            ground.Flatten(plans);
            ground.PaintBranches(plans);
            foreach (var plan in plans)
            {
                if (!plan.Existing)
                {
                    var root = plan.Puzzle ? plan.Region.Authored.Puzzle.transform : plan.Region.Authored.Challenge.transform;
                    root.position += plan.Center - plan.BeforeCenter;
                    EditorUtility.SetDirty(root);
                    if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.RecordPrefabInstancePropertyModifications(root);
                }
                plan.Region.Authored.Validate();
                // CreateLayout reads the moved bindings, so startup, challenges and save/reward IDs keep their objects.
                plan.Region.Authored.CreateLayout();
            }
            foreach (var snapshot in snapshots) snapshot.ValidateUnchanged();
            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            var audit = new Audit { regions = snapshots.Select(s => s.Report()).ToArray(),
                clearings = plans.Select(p => p.Report()).ToArray() };
            Directory.CreateDirectory(Path.GetDirectoryName(AuditPath));
            File.WriteAllText(AuditPath, JsonUtility.ToJson(audit, true));

            var footprints = new List<Vector4>();
            foreach (var p in plans)
            {
                footprints.Add(new Vector4(p.Center.x, p.Center.y, p.Center.z, p.Outer + 2));
                int steps = Mathf.CeilToInt(Distance(p.BranchStart, p.Entry) / 8);
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 point = Vector3.Lerp(p.BranchStart, p.Entry, i / (float)Mathf.Max(1, steps));
                    point.y = ground.Sample(point.x, point.z, false);
                    // Overlapping 6m circles cover the complete 5m path brush, including gaps between samples.
                    footprints.Add(new Vector4(point.x, point.y, point.z, 6.5f));
                }
            }
            Debug.Log("ORBIS_WORLD_CONTENT: ten existing composites retained, safe local pads/branches authored; " + AuditPath);
            return footprints;
        }

        static void ValidateComposite(M4IslandRegion region)
        {
            if (region == null || region.Authored == null || region.Center == null)
                throw new InvalidOperationException("A region binding or hub centre is missing.");
            var a = region.Authored; a.Validate();
            Transform p = a.Puzzle.transform, c = a.Challenge.transform;
            if (p == c || p.IsChildOf(c) || c.IsChildOf(p) || !p.IsChildOf(a.transform) || !c.IsChildOf(a.transform) ||
                !a.Chest.IsChildOf(p) || !a.ChallengeEntry.IsChildOf(c) ||
                a.Statues.Any(s => !s.transform.IsChildOf(p)) || a.Targets.Any(s => !s.transform.IsChildOf(c)))
                throw new InvalidOperationException(region.Id + ": puzzle and challenge must remain separate whole composites.");
            foreach (var fixedRoot in new[] { a.Spawn, a.Npc, a.Portal, a.BossObject.transform, region.Center })
                if (fixedRoot.IsChildOf(p) || fixedRoot.IsChildOf(c))
                    throw new InvalidOperationException(region.Id + ": a fixed gameplay anchor is inside a movable composite.");
        }

        static Plan Existing(M4IslandRegion region, Vector3 center, bool puzzle, Ground ground, M4IslandRegion[] regions)
        {
            var p = MakePlan(region, center, puzzle); p.Existing = true;
            if (!OutsideHubs(p, regions) || !CheckPath(p, ground, out float grade))
                throw new InvalidOperationException(region.Id + ": the saved relocated clearing no longer has a safe route.");
            p.PathGrade = grade;
            return p;
        }

        static List<Plan> Candidates(M4IslandRegion region, bool puzzle, Ground ground, M4IslandRegion[] regions)
        {
            var result = new List<Plan>();
            // Search the nearer radii too: a close, walkable clearing is preferable to flattening a hillside.
            float[] radii = { 102, 110, 118, 94, 126, 90 };
            foreach (float radius in radii) for (int angle = 0; angle < 360; angle += 5)
            {
                float r = angle * Mathf.Deg2Rad;
                Vector3 center = region.Center.position + new Vector3(Mathf.Cos(r) * radius, 0, Mathf.Sin(r) * radius);
                center.y = ground.Sample(center.x, center.z);
                var p = MakePlan(region, center, puzzle);
                if (!OutsideHubs(p, regions) || IslandTerrainBuilder.BiomeAt(center.x, center.z) != region.Id) continue;
                if (!CheckPad(p, ground, out float grade, out float displacement) || !CheckPath(p, ground, out float pathGrade)) continue;
                p.PadGrade = grade; p.PathGrade = pathGrade;
                float roadDistance = IslandTerrainBuilder.RoadDistance(center.x, center.z, out _);
                // No analytical elevation is used; RoadDistance only supplies a small visual route proximity preference.
                p.Score = grade * 22 + pathGrade * 16 + displacement * 2 + roadDistance * .018f + radius * .012f;
                result.Add(p);
            }
            return result;
        }

        static Plan MakePlan(M4IslandRegion region, Vector3 center, bool puzzle)
        {
            var a = region.Authored;
            Vector3 before = Average(puzzle ? a.Statues : a.Targets);
            Vector3 entry = puzzle ? a.Statues[1].transform.position + Vector3.back * 3 : a.ChallengeEntry.position;
            return new Plan { Region = region, Puzzle = puzzle, BeforeCenter = before, Center = center,
                Inner = puzzle ? PuzzleInner : ChallengeInner, Outer = puzzle ? PuzzleOuter : ChallengeOuter,
                BranchStart = region.Center.position, Entry = entry + center - before };
        }

        static bool OutsideHubs(Plan p, M4IslandRegion[] regions)
        {
            foreach (var r in regions)
                if (Distance(p.Center, r.Center.position) < ProtectedRadius + p.Outer + 2) return false;
            return true;
        }

        static bool CheckPad(Plan p, Ground ground, out float grade, out float displacement)
        {
            grade = displacement = 0;
            // Grid includes a two-metre rim outside the blend to reject an abrupt feather-to-hillside join.
            for (float z = -p.Outer - 2; z <= p.Outer + 2; z += 2)
                for (float x = -p.Outer - 2; x <= p.Outer + 2; x += 2)
                {
                    if (x * x + z * z > (p.Outer + 2) * (p.Outer + 2)) continue;
                    float wx = p.Center.x + x, wz = p.Center.z + z, old = ground.Sample(wx, wz);
                    if (!Dry(wx, wz, old)) return false;
                    float h = p.Height(wx, wz, old);
                    displacement = Mathf.Max(displacement, Mathf.Abs(old - h));
                    float dx = (p.Height(wx + 1, wz, ground.Sample(wx + 1, wz)) - p.Height(wx - 1, wz, ground.Sample(wx - 1, wz))) * .5f;
                    float dz = (p.Height(wx, wz + 1, ground.Sample(wx, wz + 1)) - p.Height(wx, wz - 1, ground.Sample(wx, wz - 1))) * .5f;
                    grade = Mathf.Max(grade, Mathf.Sqrt(dx * dx + dz * dz));
                    if (grade > MaximumPadGrade || displacement > 4.5f) return false;
                }
            return true;
        }

        static bool CheckPath(Plan p, Ground ground, out float grade)
        {
            grade = 0;
            // Keep the trail outside the existing guardian arena; its boss, columns and leash stay unchanged.
            if (SegmentDistance(p.Region.Authored.BossObject.transform.position, p.BranchStart, p.Entry) < 23) return false;
            int steps = Mathf.CeilToInt(Distance(p.BranchStart, p.Entry));
            Vector3 direction = (p.Entry - p.BranchStart); direction.y = 0; direction.Normalize();
            Vector3 side = new Vector3(-direction.z, 0, direction.x);
            // Validate both walking lanes as well as the centre; decorative brush edges need not be a flat road.
            for (int lane = -1; lane <= 1; lane++)
            {
                float previous = 0;
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 point = Vector3.Lerp(p.BranchStart, p.Entry, i / (float)Mathf.Max(1, steps)) + side * (lane * BranchCore);
                    float old = ground.Sample(point.x, point.z);
                    float h = p.Existing ? old : p.Height(point.x, point.z, old);
                    if (!Dry(point.x, point.z, h)) return false;
                    if (i > 0) grade = Mathf.Max(grade, Mathf.Abs(h - previous) * steps / Distance(p.BranchStart, p.Entry));
                    previous = h;
                    if (grade > MaximumPathGrade) return false;
                }
            }
            return true;
        }

        static bool Dry(float x, float z, float height)
        {
            if (float.IsNaN(height) || float.IsInfinity(height) || height < 2.8f) return false;
            // The existing M2 freshwater trigger is rectangular (330 x 240), larger than its visible lake disk.
            return !(Mathf.Abs(x - 230) < 168 && Mathf.Abs(z - 35) < 123 && height < IslandTerrainBuilder.LakeLevel + 1.5f);
        }
        static Vector3 Average(ElementalActor[] actors)
        { Vector3 sum = Vector3.zero; foreach (var a in actors) sum += a.transform.position; return sum / actors.Length; }
        static bool PairFits(Vector3 a, Vector3 b)
        { float d = Distance(a, b); return d >= MinimumSeparation && d <= MaximumSeparation; }
        static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static float SegmentDistance(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector2 p = new Vector2(point.x, point.z), aa = new Vector2(a.x, a.z), edge = new Vector2(b.x - a.x, b.z - a.z);
            return Vector2.Distance(p, aa + edge * Mathf.Clamp01(Vector2.Dot(p - aa, edge) / Mathf.Max(.001f, edge.sqrMagnitude)));
        }

        sealed class Plan
        {
            public M4IslandRegion Region;
            public bool Puzzle, Existing;
            public Vector3 BeforeCenter, Center, BranchStart, Entry;
            public float Inner, Outer, Score, PadGrade, PathGrade;
            public float Height(float x, float z, float original)
            {
                float distance = Distance(Center, new Vector3(x, 0, z));
                return distance >= Outer ? original : Mathf.Lerp(Center.y, original,
                    Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Inner, Outer, distance)));
            }
            public ClearingReport Report() => new ClearingReport { region = Region.Id.ToString(), content = Puzzle ? "Puzzle" : "Challenge",
                reusedPlacement = Existing, before = BeforeCenter, after = Center, entry = Entry, branchStart = BranchStart,
                flatRadius = Inner, featherRadius = Outer, maximumPadGrade = PadGrade, maximumBranchGrade = PathGrade };
        }

        sealed class Ground
        {
            sealed class Tile
            {
                public Terrain Terrain; public TerrainData Data; public Vector3 Origin, Size;
                public float[,] Original, Working; public int N;
            }
            readonly Tile[] tiles;
            public Ground(Terrain[] terrains)
            {
                if (terrains.Length == 0) throw new InvalidOperationException("The existing resident terrain is required.");
                tiles = terrains.Select(t =>
                {
                    if (t.terrainData == null || t.transform.lossyScale != Vector3.one || t.transform.rotation != Quaternion.identity)
                        throw new InvalidOperationException("Terrain must use its authored unscaled world axes.");
                    var d = t.terrainData;
                    if (d.alphamapLayers < 6 || d.terrainLayers[1].name != "Earth" || d.terrainLayers[5].name != "Trail")
                        throw new InvalidOperationException("Expected Earth and Trail terrain layers from world Step 2.");
                    var source = d.GetHeights(0, 0, d.heightmapResolution, d.heightmapResolution);
                    return new Tile { Terrain = t, Data = d, Origin = t.transform.position, Size = d.size,
                        N = d.heightmapResolution, Original = source, Working = (float[,])source.Clone() };
                }).ToArray();
            }
            public float Sample(float x, float z, bool original = true)
            {
                foreach (var t in tiles)
                {
                    float u = (x - t.Origin.x) / t.Size.x, v = (z - t.Origin.z) / t.Size.z;
                    if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                    float px = u * (t.N - 1), pz = v * (t.N - 1);
                    int ix = Mathf.Min(t.N - 2, Mathf.FloorToInt(px)), iz = Mathf.Min(t.N - 2, Mathf.FloorToInt(pz));
                    var h = original ? t.Original : t.Working;
                    return t.Origin.y + t.Size.y * Mathf.Lerp(Mathf.Lerp(h[iz, ix], h[iz, ix + 1], px - ix),
                        Mathf.Lerp(h[iz + 1, ix], h[iz + 1, ix + 1], px - ix), pz - iz);
                }
                return float.NaN;
            }
            public void Flatten(List<Plan> plans)
            {
                if (plans.All(p => p.Existing)) return;
                foreach (var t in tiles)
                {
                    bool changed = false;
                    for (int z = 0; z < t.N; z++) for (int x = 0; x < t.N; x++)
                    {
                        float wx = t.Origin.x + x * t.Size.x / (t.N - 1), wz = t.Origin.z + z * t.Size.z / (t.N - 1);
                        foreach (var p in plans)
                        {
                            if (p.Existing || Distance(p.Center, new Vector3(wx, 0, wz)) >= p.Outer) continue;
                            float original = t.Origin.y + t.Original[z, x] * t.Size.y;
                            t.Working[z, x] = (p.Height(wx, wz, original) - t.Origin.y) / t.Size.y; changed = true;
                        }
                    }
                    if (!changed) continue;
                    t.Data.SetHeights(0, 0, t.Working); t.Terrain.Flush(); EditorUtility.SetDirty(t.Data);
                }
            }
            public void PaintBranches(List<Plan> plans)
            {
                foreach (var t in tiles)
                {
                    int n = t.Data.alphamapResolution; var map = t.Data.GetAlphamaps(0, 0, n, n); bool changed = false;
                    for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
                    {
                        var point = new Vector3(t.Origin.x + x * t.Size.x / (n - 1), 0, t.Origin.z + z * t.Size.z / (n - 1));
                        float brush = 0;
                        foreach (var p in plans)
                            brush = Mathf.Max(brush, 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(BranchCore, BranchOuter,
                                SegmentDistance(point, p.BranchStart, p.Entry))));
                        if (brush <= 0) continue;
                        // Keep the original circular island trail channel byte-identical. A max target is rerun-safe.
                        float available = Mathf.Max(0, 1 - map[z, x, 5]);
                        float target = Mathf.Max(map[z, x, 1], available * brush * .90f);
                        float others = 0; for (int l = 0; l < map.GetLength(2); l++) if (l != 1 && l != 5) others += map[z, x, l];
                        if (target <= map[z, x, 1] || others <= .000001f) continue;
                        float factor = Mathf.Max(0, available - target) / others;
                        for (int l = 0; l < map.GetLength(2); l++) if (l != 1 && l != 5) map[z, x, l] *= factor;
                        map[z, x, 1] = target; changed = true;
                    }
                    if (!changed) continue;
                    t.Data.SetAlphamaps(0, 0, map); EditorUtility.SetDirty(t.Data);
                }
            }
        }

        sealed class BindingSnapshot
        {
            readonly M4IslandRegion region;
            readonly ElementalActor[] statues, targets;
            readonly string[] statueIds, targetIds;
            readonly Transform[] fixedTransforms;
            readonly Vector3[] fixedPositions, statueBefore, targetBefore;
            public BindingSnapshot(M4IslandRegion r)
            {
                region = r; statues = (ElementalActor[])r.Authored.Statues.Clone(); targets = (ElementalActor[])r.Authored.Targets.Clone();
                statueIds = statues.Select(a => a.SourceId).ToArray(); targetIds = targets.Select(a => a.SourceId).ToArray();
                statueBefore = statues.Select(a => a.transform.position).ToArray(); targetBefore = targets.Select(a => a.transform.position).ToArray();
                var authored = r.Authored;
                fixedTransforms = new[] { r.Center, authored.transform, authored.Spawn, authored.Npc, authored.Portal, authored.BossObject.transform, authored.Survey };
                fixedPositions = fixedTransforms.Select(t => t == null ? Vector3.zero : t.position).ToArray();
            }
            public void ValidateUnchanged()
            {
                if (!statues.SequenceEqual(region.Authored.Statues) || !targets.SequenceEqual(region.Authored.Targets) ||
                    !statueIds.SequenceEqual(statues.Select(a => a.SourceId)) || !targetIds.SequenceEqual(targets.Select(a => a.SourceId)))
                    throw new InvalidOperationException(region.Id + ": ordered actor identities unexpectedly changed.");
                for (int i = 0; i < fixedTransforms.Length; i++)
                    if (fixedTransforms[i] != null && fixedTransforms[i].position != fixedPositions[i])
                        throw new InvalidOperationException(region.Id + ": a boss, hub or travel anchor unexpectedly moved.");
            }
            public RegionReport Report()
            {
                var profile = M4RegionCatalog.Get(region.Id);
                return new RegionReport { id = region.Id.ToString(), displayName = profile.DisplayName,
                    element = profile.Element.ToString(), bossWeakness = profile.Weakness.ToString(),
                    // These unchanged runtime reward keys are derived by M4RegionContent.Configure.
                    puzzleRewardKey = "m4." + region.Id + ".puzzle", challengeRewardKey = "m4." + region.Id + ".challenge",
                    contentSeparation = Distance(Average(statues), Average(targets)),
                    statues = Actors(statues, statueBefore), targets = Actors(targets, targetBefore),
                    stationary = fixedTransforms.Select((t, i) => new ActorReport { sourceId = t == null ? "Optional survey (none)" : t.name,
                        before = fixedPositions[i], after = t == null ? Vector3.zero : t.position }).ToArray() };
            }
            static ActorReport[] Actors(ElementalActor[] actors, Vector3[] before) => actors.Select((a, i) =>
                new ActorReport { sourceId = a.SourceId, before = before[i], after = a.transform.position }).ToArray();
        }
        [Serializable] sealed class Audit
        {
            public string source = "Existing TerrainData samples; editor relocation only; M4 IDs, order, rewards and boss/travel anchors retained.";
            public RegionReport[] regions; public ClearingReport[] clearings;
        }
        [Serializable] sealed class ActorReport { public string sourceId; public Vector3 before, after; }
        [Serializable] sealed class RegionReport
        {
            public string id, displayName, element, bossWeakness, puzzleRewardKey, challengeRewardKey;
            public float contentSeparation;
            public ActorReport[] statues, targets, stationary;
        }
        [Serializable] sealed class ClearingReport
        {
            public string region, content; public bool reusedPlacement; public Vector3 before, after, entry, branchStart;
            public float flatRadius, featherRadius, maximumPadGrade, maximumBranchGrade;
        }
    }
}
