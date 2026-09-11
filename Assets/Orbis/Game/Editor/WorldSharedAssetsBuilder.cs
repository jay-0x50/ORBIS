using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Orbis.M4;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEngine;

namespace Orbis.Game.Editor
{
    /// <summary>
    /// Addresses existing shared environment dependencies without changing their contents or scene owners.
    /// Sources are selected by dependency use in at least two of the five environment scenes, not by biome.
    /// </summary>
    public static class WorldSharedAssetsBuilder
    {
        public const string GroupName = "ORBIS World Shared Environment";
        const string AddressPrefix = "orbis.world.shared.";
        const string Root = "Assets/Orbis/Game/World/";
        const string ReportPath = "TestResults/WorldDev/World06_SharedAssets.json";

        [Serializable] sealed class AssetRecord
        {
            public string guid, path, sha256, action, existingGroup, address;
            public string[] environmentScenes;
            public int environmentSceneCount;
            public bool referencedByResidentCore;
        }
        [Serializable] sealed class ResourceRecord
        {
            public string guid, path;
            public string[] environmentScenes;
            public int environmentSceneCount;
        }
        [Serializable] sealed class Report
        {
            public string utc, unityVersion, operation, groupName, groupGuid, localBuildPath, localLoadPath;
            public string validatedOcclusionData;
            public long occlusionPayloadBytes;
            public string policy = "Existing Nature/Ground/Architecture data or shared World shaders referenced by at least two environment scenes; one local PackTogether group. Existing foreign addresses are never moved.";
            public string limitation = "This removes copies between environment bundles only after rebuilding content. Direct built-in core references and Resources can still create separate player copies; no saved bytes or VRAM amount is inferred here.";
            public int candidateCount, addedCount, alreadyOwnedCount, skippedForeignCount;
            public bool foreignEntriesPreserved, sourceBytesPreserved;
            public AssetRecord[] assets;
            public ResourceRecord[] resourcesAlsoUsedByEnvironment;
        }

        [MenuItem("Orbis/World Performance/Analyze Shared Environment Dependencies")]
        public static void Analyze() => Run(false);

        [MenuItem("Orbis/World Performance/Register Shared Environment Dependencies")]
        public static void Apply() => Run(true);

        static void Run(bool apply)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                throw new BuildFailedException("Compile and stop other builds/Play Mode before analyzing or registering shared dependencies.");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new BuildFailedException("Existing Addressables settings are required.");
            WorldStreamingBuilder.Validate();
            string sharedPvs = FindValidatedOcclusionData(out long pvsBytes);
            var group = settings.FindGroup(GroupName);
            // A pre-existing group with unrelated addresses belongs to its author, not this tool.
            if (group != null && group.entries.Any(e => !e.address.StartsWith(AddressPrefix, StringComparison.Ordinal)))
                throw new BuildFailedException("The shared group contains a foreign address; refusing to adopt or modify it.");
            string foreignBefore = ForeignEntries(settings);
            string defaultGuid = settings.DefaultGroup.Guid;
            string profile = settings.activeProfileId;
            int playBuilder = settings.ActivePlayModeDataBuilderIndex, playerBuilder = settings.ActivePlayerDataBuilderIndex;
            var usage = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (M4RegionId id in Enum.GetValues(typeof(M4RegionId)))
            {
                string scene = WorldStreamingBuilder.ScenePath(id);
                if (!File.Exists(scene)) throw new BuildFailedException("Missing environment scene: " + scene);
                foreach (string dependency in AssetDatabase.GetDependencies(scene, true))
                {
                    if (!usage.TryGetValue(dependency, out var scenes))
                        usage.Add(dependency, scenes = new HashSet<string>(StringComparer.Ordinal));
                    scenes.Add(scene);
                }
            }
            var core = new HashSet<string>(AssetDatabase.GetDependencies(IslandSceneBuilder.ScenePath, true), StringComparer.Ordinal);
            var candidates = usage.Where(p => p.Value.Count >= 2 && Allowed(p.Key, sharedPvs))
                .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
                {
                    if (!File.Exists(p.Key)) throw new BuildFailedException("Dependency has no source file: " + p.Key);
                    string guid = AssetDatabase.AssetPathToGUID(p.Key);
                    if (string.IsNullOrEmpty(guid)) throw new BuildFailedException("Dependency has no asset GUID: " + p.Key);
                    var existing = settings.FindAssetEntry(guid, true);
                    bool owned = existing != null && group != null && existing.parentGroup == group;
                    return new AssetRecord
                    {
                        guid = guid, path = p.Key, sha256 = Hash(p.Key), environmentSceneCount = p.Value.Count,
                        environmentScenes = p.Value.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                        referencedByResidentCore = core.Contains(p.Key), existingGroup = existing?.parentGroup?.Name,
                        action = existing == null ? (apply ? "Added" : "WouldAdd") : owned ? "AlreadyOwned" : "SkippedForeignEntry",
                        address = existing?.address ?? AddressPrefix + guid
                    };
                }).ToArray();
            if (candidates.Length == 0) throw new BuildFailedException("No shared environment dependencies satisfy the allowlist.");
            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                operation = apply ? "Register" : "Analyze", groupName = GroupName, groupGuid = group?.Guid,
                validatedOcclusionData = sharedPvs, occlusionPayloadBytes = pvsBytes,
                candidateCount = candidates.Length, assets = candidates,
                alreadyOwnedCount = candidates.Count(a => a.action == "AlreadyOwned"),
                skippedForeignCount = candidates.Count(a => a.action == "SkippedForeignEntry"),
                resourcesAlsoUsedByEnvironment = usage.Where(p => p.Key.StartsWith("Assets/", StringComparison.Ordinal) &&
                    p.Key.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p.Key))
                    .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new ResourceRecord
                    {
                        guid = AssetDatabase.AssetPathToGUID(p.Key), path = p.Key, environmentSceneCount = p.Value.Count,
                        environmentScenes = p.Value.OrderBy(s => s, StringComparer.Ordinal).ToArray()
                    }).ToArray()
            };
            if (apply)
            {
                if (group == null) group = settings.CreateGroup(GroupName, false, false, true, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                var schema = group.GetSchema<BundledAssetGroupSchema>() ?? group.AddSchema<BundledAssetGroupSchema>();
                schema.IncludeInBuild = true;
                schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
                schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
                foreach (var candidate in candidates.Where(c => c.action == "Added"))
                {
                    // Check immediately before registration; CreateOrMoveEntry is only used for absent entries.
                    if (settings.FindAssetEntry(candidate.guid, true) != null)
                        throw new BuildFailedException("An address entry appeared during analysis: " + candidate.path);
                    var entry = settings.CreateOrMoveEntry(candidate.guid, group, false, true);
                    entry.SetAddress(candidate.address, true);
                    report.addedCount++;
                }
                EditorUtility.SetDirty(schema); EditorUtility.SetDirty(group); EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(schema); AssetDatabase.SaveAssetIfDirty(group); AssetDatabase.SaveAssetIfDirty(settings);
                report.groupGuid = group.Guid;
                report.localBuildPath = schema.BuildPath.GetValue(settings);
                report.localLoadPath = schema.LoadPath.GetValue(settings);
            }
            report.foreignEntriesPreserved = foreignBefore == ForeignEntries(settings) && settings.DefaultGroup.Guid == defaultGuid &&
                settings.activeProfileId == profile && settings.ActivePlayModeDataBuilderIndex == playBuilder &&
                settings.ActivePlayerDataBuilderIndex == playerBuilder;
            report.sourceBytesPreserved = candidates.All(c => Hash(c.path) == c.sha256);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            if (!report.foreignEntriesPreserved || !report.sourceBytesPreserved)
                throw new BuildFailedException("Shared dependency registration changed a protected source or existing address entry; inspect " + ReportPath);
            WorldStreamingBuilder.Validate();
            Debug.Log("ORBIS_WORLD_SHARED_ASSETS: " + report.operation + "; " + report.candidateCount + " candidates, " +
                report.addedCount + " added, " + report.alreadyOwnedCount + " already owned, " + report.skippedForeignCount +
                " foreign entries preserved. Rebuild packed content before measuring the result.");
        }

        static string FindValidatedOcclusionData(out long bytes)
        {
            bytes = 0;
            var scenes = new[] { IslandSceneBuilder.ScenePath }.Concat(Enum.GetValues(typeof(M4RegionId))
                .Cast<M4RegionId>().Select(WorldStreamingBuilder.ScenePath)).ToArray();
            var references = scenes.Select(p => Regex.Match(File.ReadAllText(p),
                @"m_OcclusionCullingData:\s*\{[^}]*guid:\s*([a-fA-F0-9]{32})")).ToArray();
            // A pre-bake run can share nature data; never adopt a leftover, unbound PVS file from a failed bake.
            if (!references[0].Success) return null;
            if (references.Any(m => !m.Success) || references.Select(m => m.Groups[1].Value).Distinct().Count() != 1)
                throw new BuildFailedException("Bake all six scenes together before sharing their occlusion data.");
            string path = AssetDatabase.GUIDToAssetPath(references[0].Groups[1].Value);
            string expected = Path.ChangeExtension(IslandSceneBuilder.ScenePath, null) + "/OcclusionCullingData.asset";
            if (!string.Equals(path, expected, StringComparison.Ordinal) || !File.Exists(path))
                throw new BuildFailedException("Only the core scene's generated occlusion asset can be shared by this tool.");
            bytes = WorldPerformanceBuilder.ValidateOcclusionPayload(path, scenes);
            return path;
        }

        static bool Allowed(string path, string validatedPvs)
        {
            if (validatedPvs != null && string.Equals(path, validatedPvs, StringComparison.Ordinal)) return true;
            if (!path.StartsWith(Root, StringComparison.Ordinal)) return false;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            // Never address scenes, code/includes, authored grass placements, prefabs, or per-cascade meshes.
            if (extension == ".unity" || extension == ".cs" || extension == ".hlsl" || extension == ".prefab") return false;
            bool texture = extension == ".png" || extension == ".jpg" || extension == ".tga" || extension == ".exr" || extension == ".asset";
            bool model = extension == ".fbx" || extension == ".obj" || extension == ".asset";
            if (path.StartsWith(Root + "Nature/Textures/", StringComparison.Ordinal)) return texture;
            if (path.StartsWith(Root + "Nature/Materials/", StringComparison.Ordinal)) return extension == ".mat";
            if (path.StartsWith(Root + "Nature/Meshes/", StringComparison.Ordinal) || path.StartsWith(Root + "Nature/Models/", StringComparison.Ordinal)) return model;
            if (path.StartsWith(Root + "Architecture/Materials/", StringComparison.Ordinal)) return extension == ".mat";
            if (path.StartsWith(Root + "Architecture/Models/", StringComparison.Ordinal)) return model;
            if (path.StartsWith(Root + "Ground/Textures/", StringComparison.Ordinal)) return texture;
            if (path.StartsWith(Root + "Ground/Materials/", StringComparison.Ordinal)) return extension == ".mat";
            if (path.StartsWith(Root + "Ground/TerrainLayers/", StringComparison.Ordinal)) return extension == ".terrainlayer";
            if (path.StartsWith(Root + "Shaders/", StringComparison.Ordinal) || path.StartsWith(Root + "Ground/Shaders/", StringComparison.Ordinal) ||
                path.StartsWith(Root + "Nature/Shaders/", StringComparison.Ordinal))
                return extension == ".shader" || extension == ".shadergraph";
            return false;
        }
        static string ForeignEntries(AddressableAssetSettings settings) => string.Join("\n", settings.groups.Where(g => g != null && g.Name != GroupName)
            .SelectMany(g => new[] { "GROUP|" + g.Guid + "|" + g.Name }.Concat(g.entries.Select(e =>
                g.Guid + "|" + e.guid + "|" + e.address + "|" + string.Join(",", e.labels.OrderBy(x => x, StringComparer.Ordinal)))))
            .OrderBy(s => s, StringComparer.Ordinal));
        static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
