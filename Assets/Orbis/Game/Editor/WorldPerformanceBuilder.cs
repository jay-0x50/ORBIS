using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Orbis.Game.World;
using Orbis.M4;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Orbis.Game.Editor
{
    /// <summary>
    /// Explicit Step 6 authoring/build entry points. Never regenerates geometry or a gameplay scene.
    /// BakeOcclusion is asynchronous: omit -quit; batch mode exits after the bake and all scene saves.
    /// </summary>
    public static class WorldPerformanceBuilder
    {
        const string Evidence = "TestResults/WorldDev";
        const StaticEditorFlags OcclusionFlags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
        static BakeOperation bake;

        [Serializable] sealed class FileDigest { public string path; public string sha256; }
        [Serializable] sealed class BuildFileEntry { public string path; public string role; public long bytes; }
        [Serializable] sealed class PackedAudit
        {
            public string outputPath, localBuildPath, localLoadPath, profile, playerBuilder;
            public string[] layoutReports;
            public double seconds;
        }
        [Serializable] sealed class PlayerAudit
        {
            public string utc, unityVersion, target, kind, executable, result, error, options, scriptingBackend;
            public string[] scenes, graphicsApis;
            public bool frameTimingStatsDuringBuild, frameTimingStatsOriginal, restoredBuildSettings;
            public long bytes;
            public int warnings, errors;
            public double seconds;
            public PackedAudit addressables;
            public BuildFileEntry[] files;
        }
        [Serializable] sealed class OcclusionAudit
        {
            public string utc, unityVersion, result, error, dataGuid, dataAsset;
            public string policy = "Only opaque Architecture/MossRock renderers: LOD0 solid occluders at least 5m, all eligible LODs occludees. Rotors, foliage, grass, particles, characters and terrain flags are untouched.";
            public string bakeSettings = "Preserve scene bake defaults: smallestOccluder=5, smallestHole=0.25, backfaceThreshold=100.";
            public string[] scenes, occluders;
            public FileDigest[] sceneFilesBefore, sceneFilesAfter;
            public int occluderCount, occludeeCount, skippedRotors, skippedSmallOccluders;
            public long pvsBytes, assetBytes, apiPvsBytesBeforeSave, apiPvsBytesAfterSave;
            public int boundSceneCount, boundRendererCount;
            public bool observedRunning;
            public double seconds;
        }
        sealed class BakeOperation
        {
            public SceneSetup[] previous;
            public Scene[] scenes;
            public OcclusionAudit audit;
            public string reportPath;
            public double started, deadline;
            public int finishedUpdates;
        }

        /// <summary>Build only the packed groups, using the existing profile and restoring editor preferences.</summary>
        [MenuItem("Orbis/World Performance/Build Packed Addressables")]
        public static void BuildAddressables()
        {
            RequireIdle(); RequireWindowsTarget();
            var settings = RequireAddressables();
            int oldBuilder = settings.ActivePlayerDataBuilderIndex;
            bool oldLayout = ProjectConfigData.GenerateBuildLayout;
            PackedAudit audit = null; Exception failure = null;
            try
            {
                settings.ActivePlayerDataBuilderIndex = PackedBuilderIndex(settings);
                ProjectConfigData.GenerateBuildLayout = true;
                audit = BuildPacked(settings);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                settings.ActivePlayerDataBuilderIndex = oldBuilder;
                ProjectConfigData.GenerateBuildLayout = oldLayout;
                EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings);
            }
            if (audit != null) WriteJson(Path.Combine(Evidence, "World06_Addressables.json"), audit);
            Finish("ORBIS_WORLD_PACKED_CONTENT", failure);
        }

        /// <summary>
        /// -worldBuildKind Development|Release; -worldBuildOutput absolute-or-project-relative.exe;
        /// -worldBuildReport optional.json. The player's own -orbisWorldBenchmark switch controls measurement.
        /// </summary>
        [MenuItem("Orbis/World Performance/Build Windows Player")]
        public static void BuildWindows()
        {
            RequireIdle(); RequireWindowsTarget();
            string kind = Argument("-worldBuildKind", "Development");
            bool development = string.Equals(kind, "Development", StringComparison.OrdinalIgnoreCase);
            if (!development && !string.Equals(kind, "Release", StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("-worldBuildKind must be Development or Release.");
            kind = development ? "Development" : "Release";
            string output = Path.GetFullPath(Argument("-worldBuildOutput", "Builds/WorldBenchmark/" + kind + "/Orbis.exe"));
            if (!string.Equals(Path.GetExtension(output), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("The Windows output must name an .exe file.");
            RequireOutsideAssets(output);
            string reportPath = Argument("-worldBuildReport", Path.Combine(Evidence, "World06_Build_" + kind + ".json"));
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (!scenes.Contains(IslandSceneBuilder.ScenePath) || scenes.Any(s => !File.Exists(s)))
                throw new BuildFailedException("The enabled normal scene list must include the authored island and existing scene files.");
            var streamed = new HashSet<string>(EnvironmentPaths());
            if (scenes.Any(streamed.Contains))
                throw new BuildFailedException("Environment scenes must remain Addressable, not also built-in player scenes.");

            var settings = RequireAddressables();
            int oldBuilder = settings.ActivePlayerDataBuilderIndex;
            var oldPlayerBuild = settings.BuildAddressablesWithPlayerBuild;
            bool oldLayout = ProjectConfigData.GenerateBuildLayout;
            bool oldTiming = PlayerSettings.enableFrameTimingStats;
            var options = BuildOptions.DetailedBuildReport | (development ? BuildOptions.Development : BuildOptions.None);
            var audit = new PlayerAudit
            {
                utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                target = BuildTarget.StandaloneWindows64.ToString(), kind = kind, executable = output,
                scenes = scenes, options = options.ToString(), frameTimingStatsOriginal = oldTiming,
                frameTimingStatsDuringBuild = true,
                scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString(),
                graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64).Select(a => a.ToString()).ToArray()
            };
            var elapsed = Stopwatch.StartNew(); Exception failure = null;
            try
            {
                settings.ActivePlayerDataBuilderIndex = PackedBuilderIndex(settings);
                // One explicit packed build; avoid a second build driven by the developer's global preference.
                settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
                ProjectConfigData.GenerateBuildLayout = true;
                PlayerSettings.enableFrameTimingStats = true;
                audit.addressables = BuildPacked(settings);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes, locationPathName = output, target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone, subtarget = (int)StandaloneBuildSubtarget.Player,
                    // Deliberately no DeepProfilingSupport, ConnectWithProfiler, AutoRunPlayer or script debugging.
                    options = options
                });
                audit.result = report.summary.result.ToString(); audit.bytes = (long)report.summary.totalSize;
                audit.errors = (int)report.summary.totalErrors; audit.warnings = (int)report.summary.totalWarnings;
                audit.files = report.GetFiles().Select(f => new BuildFileEntry
                    { path = f.path, role = f.role, bytes = (long)f.size }).ToArray();
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("Windows player build ended with " + report.summary.result + ".");
            }
            catch (Exception exception) { failure = exception; audit.error = exception.ToString(); audit.result = "Failed"; }
            finally
            {
                PlayerSettings.enableFrameTimingStats = oldTiming;
                settings.ActivePlayerDataBuilderIndex = oldBuilder;
                settings.BuildAddressablesWithPlayerBuild = oldPlayerBuild;
                ProjectConfigData.GenerateBuildLayout = oldLayout;
                EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings);
                audit.restoredBuildSettings = PlayerSettings.enableFrameTimingStats == oldTiming &&
                    settings.ActivePlayerDataBuilderIndex == oldBuilder && settings.BuildAddressablesWithPlayerBuild == oldPlayerBuild &&
                    ProjectConfigData.GenerateBuildLayout == oldLayout;
                audit.seconds = elapsed.Elapsed.TotalSeconds;
            }
            WriteJson(reportPath, audit);
            Finish("ORBIS_WORLD_WINDOWS_BUILD " + output, failure);
        }

        /// <summary>Open core + all five environment scenes, jointly bake one PVS, save only those scenes.</summary>
        [MenuItem("Orbis/World Performance/Bake Joint Occlusion")]
        public static void BakeOcclusion()
        {
            RequireIdle();
            if (Application.isBatchMode && Environment.GetCommandLineArgs().Any(a => a == "-quit"))
                throw new BuildFailedException("BakeOcclusion finishes asynchronously: remove -quit; this entry point exits after completion.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new BuildFailedException("Save the currently modified scene before the explicit occlusion bake.");
            string[] paths = new[] { IslandSceneBuilder.ScenePath }.Concat(EnvironmentPaths()).ToArray();
            if (paths.Any(p => !File.Exists(p))) throw new BuildFailedException("Create the existing core and five environment scenes first.");
            bake = new BakeOperation
            {
                previous = EditorSceneManager.GetSceneManagerSetup(), scenes = new Scene[paths.Length],
                reportPath = Argument("-worldOcclusionReport", Path.Combine(Evidence, "World06_OcclusionBake.json")),
                started = EditorApplication.timeSinceStartup,
                // Unspecified tool timeout: 30 minutes is a failure guard, not a bake quality setting.
                deadline = EditorApplication.timeSinceStartup + 1800,
                audit = new OcclusionAudit
                {
                    utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                    scenes = paths, sceneFilesBefore = paths.Select(Digest).ToArray()
                }
            };
            try
            {
                var chosen = new List<string>();
                for (int i = 0; i < paths.Length; i++)
                {
                    bake.scenes[i] = EditorSceneManager.OpenScene(paths[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);
                    // The core remains the active scene and owns the shared PVS asset.
                    if (i == 0) SceneManager.SetActiveScene(bake.scenes[i]);
                    foreach (var root in bake.scenes[i].GetRootGameObjects())
                        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                            ConfigureOpaqueOcclusion(renderer, paths[i], chosen, bake.audit);
                }
                SceneManager.SetActiveScene(bake.scenes[0]);
                bake.audit.occluders = chosen.ToArray();
                if (chosen.Count == 0) throw new BuildFailedException("No supported opaque architecture/rock occluders were found.");
                if (!StaticOcclusionCulling.GenerateInBackground()) throw new BuildFailedException("Unity refused to start the occlusion bake.");
                bake.audit.observedRunning = StaticOcclusionCulling.isRunning;
                EditorApplication.update += PollBake;
                Debug.Log("ORBIS_WORLD_OCCLUSION_STARTED: " + chosen.Count + " opaque LOD0 occluders across six jointly opened scenes.");
            }
            catch (Exception exception) { CompleteBake(exception); }
        }

        static void ConfigureOpaqueOcclusion(MeshRenderer renderer, string scenePath, List<string> chosen, OcclusionAudit audit)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) return;
            if (renderer.GetComponentInParent<WorldWindmill>() != null) { audit.skippedRotors++; return; }
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            var materials = renderer.sharedMaterials;
            if (materials.Length == 0 || materials.Any(m => m == null || m.shader == null ||
                (m.shader.name != "Orbis/World/Architecture" && m.shader.name != "Orbis/World/MossRock") ||
                m.renderQueue > 2500 || m.GetTag("RenderType", false, "") != "Opaque")) return;
            var group = renderer.GetComponentInParent<LODGroup>();
            var levels = group != null ? group.GetLODs() : Array.Empty<LOD>();
            bool highestDetail = levels.Length == 0 || levels[0].renderers.Contains(renderer);
            Vector3 size = renderer.bounds.size;
            // Match the existing 5m smallest-occluder setting; pebbles do not justify fine PVS cells.
            bool substantial = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) >= 5f;
            bool occluder = highestDetail && substantial;
            var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & ~OcclusionFlags;
            flags |= StaticEditorFlags.OccludeeStatic;
            if (occluder) flags |= StaticEditorFlags.OccluderStatic;
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags);
            if (PrefabUtility.IsPartOfPrefabInstance(renderer.gameObject))
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.gameObject);
            EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
            audit.occludeeCount++;
            if (!substantial) audit.skippedSmallOccluders++;
            if (!occluder) return;
            audit.occluderCount++;
            string hierarchy = renderer.name;
            for (var p = renderer.transform.parent; p != null; p = p.parent) hierarchy = p.name + "/" + hierarchy;
            chosen.Add(scenePath + " :: " + hierarchy);
        }

        static void PollBake()
        {
            if (bake == null) { EditorApplication.update -= PollBake; return; }
            if (EditorApplication.timeSinceStartup > bake.deadline)
            {
                StaticOcclusionCulling.Cancel();
                CompleteBake(new TimeoutException("The joint occlusion bake exceeded 30 minutes.")); return;
            }
            if (StaticOcclusionCulling.isRunning)
            {
                bake.audit.observedRunning = true; bake.finishedUpdates = 0; return;
            }
            // Native generation, asset import, and scene-reference publication finish in separate editor phases.
            if (EditorApplication.isUpdating || EditorApplication.timeSinceStartup - bake.started < .25) return;
            if (++bake.finishedUpdates < 2) return;
            CompleteBake(null);
        }

        static void CompleteBake(Exception failure)
        {
            EditorApplication.update -= PollBake;
            var operation = bake; bake = null;
            if (operation == null) return;
            try
            {
                if (failure == null)
                {
                    // On this multi-scene bake the active core contains no marked renderers. Unity 6000.6
                    // reports umbraDataSize=0 even after publishing a populated shared PVS asset. Do not
                    // equate this active-scene diagnostic with the actual six-scene payload size.
                    operation.audit.apiPvsBytesBeforeSave = StaticOcclusionCulling.umbraDataSize;
                    foreach (var scene in operation.scenes)
                        if (!scene.IsValid() || !scene.isLoaded || !EditorSceneManager.SaveScene(scene))
                            throw new BuildFailedException("Could not save every scene in the jointly baked group.");
                    var references = operation.audit.scenes.Select(p => Regex.Match(File.ReadAllText(p),
                        @"m_OcclusionCullingData:\s*\{[^}]*guid:\s*([a-fA-F0-9]{32})")).ToArray();
                    if (references.Any(m => !m.Success) || references.Select(m => m.Groups[1].Value).Distinct().Count() != 1)
                        throw new BuildFailedException("The six scenes do not reference the same occlusion data GUID.");
                    operation.audit.dataGuid = references[0].Groups[1].Value;
                    operation.audit.dataAsset = AssetDatabase.GUIDToAssetPath(operation.audit.dataGuid);
                    if (!File.Exists(operation.audit.dataAsset)) throw new BuildFailedException("The shared PVS asset file is missing.");
                    ValidatePvsPayload(operation.audit);
                    operation.audit.apiPvsBytesAfterSave = StaticOcclusionCulling.umbraDataSize;
                    operation.audit.assetBytes = new FileInfo(operation.audit.dataAsset).Length;
                    operation.audit.sceneFilesAfter = operation.audit.scenes.Select(Digest).ToArray();
                    operation.audit.result = "Succeeded";
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                // Keep an empty temporary scene open: Unity cannot unload the last loaded scene.
                // This also permits discarding the operation's unsaved flags on failure without a save prompt.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                for (int i = operation.scenes.Length - 1; i >= 0; i--)
                    if (operation.scenes[i].IsValid() && operation.scenes[i].isLoaded)
                        EditorSceneManager.CloseScene(operation.scenes[i], true);
                if (operation.previous.Length != 0) EditorSceneManager.RestoreSceneManagerSetup(operation.previous);
            }
            operation.audit.seconds = EditorApplication.timeSinceStartup - operation.started;
            if (failure != null) { operation.audit.result = "Failed"; operation.audit.error = failure.ToString(); }
            WriteJson(operation.reportPath, operation.audit);
            Finish("ORBIS_WORLD_OCCLUSION_FINISHED", failure);
        }

        /// <summary>Read-only validation used before explicitly sharing a jointly baked native PVS asset.</summary>
        public static long ValidateOcclusionPayload(string assetPath, string[] scenePaths)
        {
            var audit = new OcclusionAudit { dataAsset = assetPath, scenes = scenePaths };
            ValidatePvsPayload(audit);
            return audit.pvsBytes;
        }

        static void ValidatePvsPayload(OcclusionAudit audit)
        {
            var sceneGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const string payloadKey = "  m_PVSData:";
            long payloadBytes = 0;
            bool sceneTable = false;
            int rendererCount = 0;
            // This project serializes native assets as text. Inspect actual payload and scene bindings,
            // not the YAML file length (a header-only asset would otherwise falsely pass).
            foreach (string line in File.ReadLines(audit.dataAsset))
            {
                if (line.StartsWith(payloadKey, StringComparison.Ordinal))
                {
                    int start = payloadKey.Length, end = line.Length;
                    while (start < end && char.IsWhiteSpace(line[start])) start++;
                    while (end > start && char.IsWhiteSpace(line[end - 1])) end--;
                    // Unity's native YAML byte scalar ends in a 'y' type suffix; it is not a payload byte.
                    if (end > start && line[end - 1] == 'y') end--;
                    if ((end - start) % 2 != 0) throw new BuildFailedException("The PVS payload has an invalid hexadecimal byte length.");
                    for (int i = start; i < end; i++)
                        if (!Uri.IsHexDigit(line[i])) throw new BuildFailedException("The PVS payload contains non-hexadecimal data.");
                    payloadBytes = (end - start) / 2; continue;
                }
                if (line == "  m_Scenes:") { sceneTable = true; continue; }
                if (sceneTable && line.StartsWith("  m_", StringComparison.Ordinal)) sceneTable = false;
                if (!sceneTable) continue;
                string value = line.Trim();
                if (value.StartsWith("scene: ", StringComparison.Ordinal)) sceneGuids.Add(value.Substring(7).Trim());
                if (value.StartsWith("sizeRenderers: ", StringComparison.Ordinal) && int.TryParse(value.Substring(15), out int count))
                    rendererCount += count;
            }
            var expected = new HashSet<string>(audit.scenes.Select(AssetDatabase.AssetPathToGUID), StringComparer.OrdinalIgnoreCase);
            if (payloadBytes <= 0 || rendererCount <= 0 || !sceneGuids.SetEquals(expected))
                throw new BuildFailedException("The shared PVS must contain actual bytes, renderer bindings, and exactly the six baked scene GUIDs.");
            audit.pvsBytes = payloadBytes; audit.boundSceneCount = sceneGuids.Count; audit.boundRendererCount = rendererCount;
        }

        static PackedAudit BuildPacked(AddressableAssetSettings settings)
        {
            WorldStreamingBuilder.Validate();
            var watch = Stopwatch.StartNew();
            var started = DateTime.UtcNow;
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (result == null || !string.IsNullOrEmpty(result.Error))
                throw new BuildFailedException(result?.Error ?? "No packed Addressables build result.");
            string profile = settings.activeProfileId;
            var reports = new List<string>();
            string legacy = Path.Combine(Addressables.LibraryPath, "buildlayout.json");
            if (File.Exists(legacy) && File.GetLastWriteTimeUtc(legacy) >= started.AddSeconds(-2)) reports.Add(legacy);
            if (Directory.Exists(Addressables.BuildReportPath))
                reports.AddRange(Directory.GetFiles(Addressables.BuildReportPath, "*.json", SearchOption.AllDirectories)
                    .Where(p => File.GetLastWriteTimeUtc(p) >= started.AddSeconds(-2)));
            return new PackedAudit
            {
                outputPath = result.OutputPath, seconds = watch.Elapsed.TotalSeconds,
                profile = settings.profileSettings.GetProfileName(profile), playerBuilder = settings.ActivePlayerDataBuilder.GetType().FullName,
                localBuildPath = settings.profileSettings.EvaluateString(profile, settings.profileSettings.GetValueByName(profile, AddressableAssetSettings.kLocalBuildPath)),
                localLoadPath = settings.profileSettings.EvaluateString(profile, settings.profileSettings.GetValueByName(profile, AddressableAssetSettings.kLocalLoadPath)),
                layoutReports = reports.Distinct().ToArray()
            };
        }
        static AddressableAssetSettings RequireAddressables() => AddressableAssetSettingsDefaultObject.Settings ??
            throw new BuildFailedException("Existing Addressables settings are required.");
        static int PackedBuilderIndex(AddressableAssetSettings settings)
        {
            for (int i = 0; i < settings.DataBuilders.Count; i++) if (settings.DataBuilders[i] is BuildScriptPackedMode) return i;
            throw new BuildFailedException("The existing BuildScriptPackedMode builder is missing.");
        }
        static string[] EnvironmentPaths() => Enum.GetValues(typeof(M4RegionId)).Cast<M4RegionId>().Select(WorldStreamingBuilder.ScenePath).ToArray();
        static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer ||
                bake != null || StaticOcclusionCulling.isRunning)
                throw new BuildFailedException("Compile and stop Play Mode/other builds before this explicit operation.");
        }
        static void RequireWindowsTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new BuildFailedException("Launch this operation with -buildTarget Win64; target changes must finish before the method runs.");
        }
        static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++) if (args[i] == name)
            {
                if (i + 1 == args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new BuildFailedException("Missing value after " + name);
                return args[i + 1];
            }
            return fallback;
        }
        static FileDigest Digest(string path)
        {
            using (var file = File.OpenRead(path)) using (var hash = SHA256.Create())
                return new FileDigest { path = path, sha256 = BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant() };
        }
        static void RequireOutsideAssets(string path)
        {
            string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(path).StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("Builds and evidence must be outside Assets.");
        }
        static void WriteJson(string path, object value)
        {
            path = Path.GetFullPath(path); RequireOutsideAssets(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(value, true), new UTF8Encoding(false));
        }
        static void Finish(string message, Exception failure)
        {
            if (failure != null) Debug.LogException(failure); else Debug.Log(message);
            if (Application.isBatchMode) EditorApplication.Exit(failure == null ? 0 : 1);
            else if (failure != null) throw new BuildFailedException(failure.Message);
        }
    }
}
