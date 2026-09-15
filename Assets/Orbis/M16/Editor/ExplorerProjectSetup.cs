using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.M1;
using Orbis.EditorSupport;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orbis.M16.Editor
{
    public static class ExplorerProjectSetup
    {
        public const string CatalogPath = "Assets/Orbis/M16/Resources/M16/Catalog.asset";
        public const string ScenePath = "Assets/Orbis/M16/Scenes/M16_CharacterSelection.unity";
        private const string DataRoot = "Assets/Orbis/M16/Resources/M16";
        private const string ScreenScript = "Assets/Orbis/M16/Runtime/Presentation/ExplorerSelectionScreen.cs";

        [MenuItem("Orbis/Development/Legacy/M1.6/Setup and Validate")]
        public static void SetupAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureFolder(DataRoot + "/Explorers"); EnsureFolder(DataRoot + "/Skills");
            EnsureFolder("Assets/Orbis/M16/Scenes");
            var weapon = GetOrCreate<WayfarerWeaponDefinition>(DataRoot + "/WayfarersBlade.asset");
            weapon.Id = "wayfarers_blade"; weapon.DisplayName = "여정의 검";
            weapon.EnglishName = "Wayfarer's Blade"; weapon.WeaponType = "Longsword";
            // BaseAttack and every skill/cooldown number use SO field defaults only on first creation.
            // Existing numerical edits survive repeated setup; only canonical IDs and references are refreshed.
            EditorUtility.SetDirty(weapon);
            var stella = BuildExplorer("stella", "스텔라", ExplorerChoice.Stella, weapon);
            var polaris = BuildExplorer("polaris", "폴라리스", ExplorerChoice.Polaris, weapon);
            var skills = new List<ExplorerSkillDefinition>();
            foreach (ElementType element in new[] { ElementType.Fire, ElementType.Water, ElementType.Wind, ElementType.Rock, ElementType.Lightning })
            {
                var skill = GetOrCreate<ExplorerSkillDefinition>(DataRoot + "/Skills/" + element + ".asset");
                skill.Id = "explorer." + element.ToString().ToLowerInvariant(); skill.Element = element;
                EditorUtility.SetDirty(skill); skills.Add(skill);
            }
            var catalog = GetOrCreate<ExplorerCatalog>(CatalogPath);
            catalog.Explorers = new[] { stella, polaris }; catalog.Skills = skills.ToArray();
            catalog.Validate(); EditorUtility.SetDirty(catalog);
            EnsureScene();
            // The standalone legacy demo stays available; the authored Field takes over the product entry.
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            scenes.AddRange(EditorBuildSettings.scenes.Where(x => x.path != ScenePath));
            EditorBuildSettings.scenes = scenes.ToArray();
            FieldSceneBuildPolicy.ApplyProductLayout();
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Validate();
            Debug.Log("ORBIS M1.6 ready: Stella/Polaris selection, one shared Wayfarer weapon and five skill definitions. No gacha roster changes.");
        }

        private static ExplorerDefinition BuildExplorer(string id, string name, ExplorerChoice choice, WayfarerWeaponDefinition weapon)
        {
            var explorer = GetOrCreate<ExplorerDefinition>(DataRoot + "/Explorers/" + id + ".asset");
            explorer.Id = id; explorer.DisplayName = name; explorer.Choice = choice; explorer.Weapon = weapon;
            EditorUtility.SetDirty(explorer); return explorer;
        }

        [MenuItem("Orbis/Development/Legacy/M1.6/Validate Data")]
        public static void Validate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ExplorerCatalog>(CatalogPath);
            if (catalog == null) throw new BuildFailedException("M1.6 explorer catalog is missing.");
            catalog.Validate();
            if (catalog.Get(ExplorerChoice.Stella).Id != "stella" || catalog.Get(ExplorerChoice.Polaris).Id != "polaris" ||
                catalog.Get(ExplorerChoice.Stella).Weapon.Id != "wayfarers_blade")
                throw new BuildFailedException("M1.6 canonical explorer/weapon IDs are inconsistent.");
            string guid = AssetDatabase.AssetPathToGUID(ScreenScript);
            if (string.IsNullOrEmpty(guid) || AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null ||
                !File.ReadAllText(ScenePath).Contains("guid: " + guid))
                throw new BuildFailedException("The M1.6 selection scene is missing its screen script.");
            if (FieldSceneBuildPolicy.IsProductMode)
                FieldSceneBuildPolicy.ValidateBuildLayout();
            else
            {
                var firstEnabled = EditorBuildSettings.scenes.FirstOrDefault(x => x.enabled);
                if (firstEnabled == null || firstEnabled.path != ScenePath)
                    throw new BuildFailedException("M1.6 character selection must be the first enabled legacy build scene.");
            }
        }

        [MenuItem("Orbis/Development/Legacy/M1.6/Open Character Selection")]
        public static void OpenCharacterSelection()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SetupAndValidate();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); string parent = path.Substring(0, slash);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
        private static void EnsureScene()
        {
            if (File.Exists(ScenePath)) return;
            string guid = AssetDatabase.AssetPathToGUID(ScreenScript);
            if (string.IsNullOrEmpty(guid)) throw new BuildFailedException("Import ExplorerSelectionScreen.cs before generating the scene.");
            string yaml = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &1000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 1001}
  - component: {fileID: 1002}
  m_Layer: 0
  m_Name: ORBIS M1.6 Character Selection
  m_TagString: Untagged
  m_IsActive: 1
--- !u!4 &1001
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 1000}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Children: []
  m_Father: {fileID: 0}
--- !u!114 &1002
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 1000}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: __GUID__, type: 3}
  m_Name:
  m_EditorClassIdentifier: Orbis.M16.Runtime::Orbis.M16.ExplorerSelectionScreen
--- !u!1660057539 &9223372036854775807
SceneRoots:
  m_ObjectHideFlags: 0
  m_Roots:
  - {fileID: 1001}
";
            File.WriteAllText(ScenePath, yaml.Replace("__GUID__", guid), new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}