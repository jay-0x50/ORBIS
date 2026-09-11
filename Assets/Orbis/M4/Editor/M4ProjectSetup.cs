using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Orbis.M3.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orbis.M4.Editor
{
    public static class M4ProjectSetup
    {
        public const string SceneDirectory = "Assets/Orbis/M4/Scenes";
        public const string LauncherScene = SceneDirectory + "/M4_Launcher.unity";
        public const string RegionGroup = "ORBIS M4 Regions";
        private const string BootstrapScript = "Assets/Orbis/M4/Runtime/Presentation/M4SceneBootstrap.cs";
        private const string LauncherScript = "Assets/Orbis/M4/Runtime/Presentation/M4Launcher.cs";
        private static bool prerequisitesReady;
        private const string ExplorerEntry = "Assets/Orbis/M16/Scenes/M16_CharacterSelection.unity";

        public static string ScenePath(M4RegionId id) => SceneDirectory + "/M4_" + id + ".unity";
        public static string Address(M4RegionId id) => "orbis.region." + id.ToString().ToLowerInvariant();

        [MenuItem("Orbis/M4/Setup and Validate")]
        public static void SetupAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!prerequisitesReady)
            {
                M3ProjectSetup.SetupAndValidate();
                prerequisitesReady = true;
            }
            foreach (M4RegionProfile profile in M4RegionCatalog.All) BuildScene(ScenePath(profile.Id), BootstrapScript, profile.Id);
            BuildScene(LauncherScene, LauncherScript, null);
            ConfigureAddressables();
            // The player starts in the lightweight launcher. Old prototype scene enable flags are
            // preserved; Addressable region scenes are never duplicated as built-in scenes.
            var regionPaths = new HashSet<string>(M4RegionCatalog.All.Select(x => ScenePath(x.Id)));
            var scenes = new List<EditorBuildSettingsScene>();
            // Once M1.6 exists, keep character selection as the player entry even when rebuilding old content.
            bool explorerEntryExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ExplorerEntry) != null;
            if (explorerEntryExists) scenes.Add(new EditorBuildSettingsScene(ExplorerEntry, true));
            scenes.Add(new EditorBuildSettingsScene(LauncherScene, true));
            scenes.AddRange(EditorBuildSettings.scenes.Where(x => x.path != LauncherScene && (!explorerEntryExists || x.path != ExplorerEntry) && !regionPaths.Contains(x.path)));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Validate();
            Debug.Log("ORBIS M4 setup passed: launcher + five local Addressable regions. Open Orbis > M4 > Open Launcher.");
        }

        [MenuItem("Orbis/M4/Build Local Addressable Content")]
        public static void BuildContent()
        {
            SetupAndValidate();
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (result == null || !string.IsNullOrEmpty(result.Error))
                throw new BuildFailedException("M4 Addressables content build failed: " + (result == null ? "no build result" : result.Error));
            Debug.Log("ORBIS M4 local Addressable content built successfully: " + result.OutputPath);
        }

        public static void Validate()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new BuildFailedException("M4 Addressables settings are missing.");
            var group = settings.FindGroup(RegionGroup);
            if (group == null) throw new BuildFailedException("M4 Addressables region group is missing.");
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null || !schema.IncludeInBuild || schema.BundleMode != BundledAssetGroupSchema.BundlePackingMode.PackSeparately)
                throw new BuildFailedException("M4 regions must be included as individual local packed bundles.");
            foreach (M4RegionProfile profile in M4RegionCatalog.All)
            {
                string path = ScenePath(profile.Id);
                ValidateScene(path, BootstrapScript, profile.Id);
                var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
                if (entry == null || entry.address != Address(profile.Id) || entry.parentGroup != group)
                    throw new BuildFailedException("M4 region address is missing or incorrect: " + Address(profile.Id));
                if (EditorBuildSettings.scenes.Any(x => x.path == path && x.enabled))
                    throw new BuildFailedException("M4 region scene is duplicated in built-in scenes: " + path);
            }
            ValidateScene(LauncherScene, LauncherScript, null);
            var enabled = EditorBuildSettings.scenes.Where(x => x.enabled).ToArray();
            if (enabled.Length == 0 || (enabled[0].path != LauncherScene && enabled[0].path != ExplorerEntry) || !enabled.Any(x => x.path == LauncherScene))
                throw new BuildFailedException("The player entry must be the M4 launcher or M1.6 selection, with the M4 launcher enabled.");
        }

        private static void ConfigureAddressables()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group = settings.FindGroup(RegionGroup);
            if (group == null)
                group = settings.CreateGroup(RegionGroup, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null) schema = group.AddSchema<BundledAssetGroupSchema>();
            schema.IncludeInBuild = true;
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            foreach (var profile in M4RegionCatalog.All)
            {
                var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(ScenePath(profile.Id)), group);
                entry.address = Address(profile.Id);
            }
            int fast = settings.DataBuilders.FindIndex(x => x is BuildScriptFastMode);
            int packed = settings.DataBuilders.FindIndex(x => x is BuildScriptPackedMode);
            if (fast < 0 || packed < 0) throw new BuildFailedException("Addressables Fast Mode / Packed Mode data builders are unavailable.");
            // Editor travel uses the actual Addressables API with AssetDatabase-backed content.
            // A player uses the separately built local bundles; no remote host/CDN is required.
            settings.ActivePlayModeDataBuilderIndex = fast;
            settings.ActivePlayerDataBuilderIndex = packed;
            EditorUtility.SetDirty(schema); EditorUtility.SetDirty(group); EditorUtility.SetDirty(settings);
        }

        private static void BuildScene(string path, string scriptPath, M4RegionId? region)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) return;
            Directory.CreateDirectory(SceneDirectory);
            string source = File.ReadAllText(M3ProjectSetup.WindScene);
            string sourceGuid = AssetDatabase.AssetPathToGUID("Assets/Orbis/M3/Runtime/Presentation/M3SceneBootstrap.cs");
            string scriptGuid = AssetDatabase.AssetPathToGUID(scriptPath);
            if (string.IsNullOrEmpty(scriptGuid)) throw new BuildFailedException("M4 runtime script is not imported: " + scriptPath);
            string className = region.HasValue ? "M4SceneBootstrap" : "M4Launcher";
            source = source.Replace(sourceGuid, scriptGuid)
                .Replace("ORBIS M3 Effects Prototype", "ORBIS M4 " + (region.HasValue ? region.Value.ToString() : "Launcher"))
                .Replace("Orbis.M3.Runtime::Orbis.M3.M3SceneBootstrap", "Orbis.M4.Runtime::Orbis.M4." + className);
            source = Regex.Replace(source, @"(?m)^  useRockFourthMember:.*\r?\n", "");
            source = Regex.Replace(source, @"(?m)^  useExplorationRegion:.*\r?\n", "");
            if (region.HasValue) source = source.Replace("--- !u!1660057539", "  region: " + (int)region.Value + "\n--- !u!1660057539");
            File.WriteAllText(path, source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ValidateScene(string path, string scriptPath, M4RegionId? region)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) throw new BuildFailedException("M4 scene missing: " + path);
            string yaml = File.ReadAllText(path);
            string guid = AssetDatabase.AssetPathToGUID(scriptPath);
            if (string.IsNullOrEmpty(guid) || !yaml.Contains("guid: " + guid)) throw new BuildFailedException("M4 scene bootstrap reference is invalid: " + path);
            if (region.HasValue && !Regex.IsMatch(yaml, @"(?m)^  region: " + (int)region.Value + @"\r?$"))
                throw new BuildFailedException("M4 scene region does not match its address: " + path);
        }

        [MenuItem("Orbis/M4/Open Launcher")] public static void OpenLauncher() => Open(LauncherScene);
        [MenuItem("Orbis/M4/Open Agnia")] public static void OpenAgnia() => Open(ScenePath(M4RegionId.Agnia));
        [MenuItem("Orbis/M4/Open Teluna")] public static void OpenTeluna() => Open(ScenePath(M4RegionId.Teluna));
        [MenuItem("Orbis/M4/Open Zephyr")] public static void OpenZephyr() => Open(ScenePath(M4RegionId.Zephyr));
        [MenuItem("Orbis/M4/Open Granite")] public static void OpenGranite() => Open(ScenePath(M4RegionId.Granite));
        [MenuItem("Orbis/M4/Open Voltheim")] public static void OpenVoltheim() => Open(ScenePath(M4RegionId.Voltheim));
        private static void Open(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) SetupAndValidate();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }
    }
}
