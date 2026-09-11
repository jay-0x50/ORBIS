using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.M1;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orbis.M15.Editor
{
    public static class M15ProjectSetup
    {
        public const string Root = "Assets/Orbis/M15";
        public const string CatalogPath = Root + "/Resources/M15/Catalog.asset";
        public const string ScenePath = Root + "/Scenes/M15_GachaDemo.unity";
        public const string LimitedBannerId = "limited.maris.001";
        public const string StandardBannerId = "standard.companions";
        private const string DataRoot = Root + "/Resources/M15";
        private const string DemoScript = Root + "/Runtime/Presentation/M15GachaDemo.cs";

        private readonly struct Definition
        {
            public readonly string Id, Name, Role;
            public readonly ElementType Element;
            public readonly CharacterRarity Rarity;
            public readonly WeaponType Weapon;
            public readonly bool Starter;
            public Definition(string id, string name, ElementType element, CharacterRarity rarity,
                string role, WeaponType weapon, bool starter = false)
            { Id = id; Name = name; Element = element; Rarity = rarity; Role = role; Weapon = weapon; Starter = starter; }
        }

        // Names, elements and roles come from document 06. Starter/4-star weapons are unspecified:
        // use a sword for balanced melee, catalyst for healing/support, bow for scouting/debuffs,
        // greatsword for tanks, dual blades for speed and a spear for stun reach. These are data only.
        private static readonly Definition[] Definitions =
        {
            new Definition("kyren", "카이런", ElementType.Fire, CharacterRarity.Three, "밸런스형 검사", WeaponType.Longsword, true),
            new Definition("mila", "밀라", ElementType.Water, CharacterRarity.Three, "힐 서포터", WeaponType.Catalyst, true),
            new Definition("torvan", "토르반", ElementType.Rock, CharacterRarity.Three, "탱커", WeaponType.Greatsword, true),
            new Definition("loren", "로렌", ElementType.Fire, CharacterRarity.Four, "서포터(치유)", WeaponType.Catalyst),
            new Definition("kai", "카이", ElementType.Fire, CharacterRarity.Four, "스피드 딜러", WeaponType.DualBlades),
            new Definition("celine", "셀린", ElementType.Water, CharacterRarity.Four, "힐러", WeaponType.Catalyst),
            new Definition("noah", "노아", ElementType.Water, CharacterRarity.Four, "디버퍼", WeaponType.Bow),
            new Definition("finn", "핀", ElementType.Wind, CharacterRarity.Four, "정찰형 딜러", WeaponType.Bow),
            new Definition("ria", "리아", ElementType.Wind, CharacterRarity.Four, "버프 서포터", WeaponType.Catalyst),
            new Definition("dorin", "도린", ElementType.Rock, CharacterRarity.Four, "서브 탱커", WeaponType.Greatsword),
            new Definition("marco", "마르코", ElementType.Rock, CharacterRarity.Four, "방어형 딜러", WeaponType.Longsword),
            new Definition("joy", "조이", ElementType.Lightning, CharacterRarity.Four, "스턴형 딜러", WeaponType.Spear),
            new Definition("bella", "벨라", ElementType.Lightning, CharacterRarity.Four, "에너지 서포터", WeaponType.Catalyst),
            // Five-star weapons and regional guardian concepts are explicitly specified in document 06.
            new Definition("ignis", "이그니스", ElementType.Fire, CharacterRarity.Five, "아그니아 고원의 수호자", WeaponType.Longsword),
            new Definition("maris", "마리스", ElementType.Water, CharacterRarity.Five, "텔루나 군도의 수호자", WeaponType.Bow),
            new Definition("aura", "아우라", ElementType.Wind, CharacterRarity.Five, "자피르 초원의 수호자", WeaponType.DualBlades),
            new Definition("grom", "그롬", ElementType.Rock, CharacterRarity.Five, "그라니테 산맥의 수호자", WeaponType.Greatsword),
            new Definition("sparkle", "스파클", ElementType.Lightning, CharacterRarity.Five, "볼트하임의 수호자", WeaponType.Spear)
        };

        [MenuItem("Orbis/M1.5/Setup and Validate")]
        public static void SetupAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureFolder(DataRoot + "/Characters");
            EnsureFolder(DataRoot + "/Banners");
            EnsureFolder(Root + "/Scenes");
            var rules = GetOrCreate<GachaRules>(DataRoot + "/Rules.asset");
            // GetOrCreate preserves existing SO values: repeating setup must not undo balancing edits.
            rules.Validate();
            var characters = new List<CharacterDefinition>();
            foreach (var entry in Definitions)
            {
                var asset = GetOrCreate<CharacterDefinition>(DataRoot + "/Characters/" + entry.Id + ".asset");
                asset.name = entry.Id; asset.Id = entry.Id; asset.DisplayName = entry.Name;
                asset.Element = entry.Element; asset.Rarity = entry.Rarity; asset.Role = entry.Role;
                asset.Weapon = entry.Weapon; asset.IsStarter = entry.Starter;
                EditorUtility.SetDirty(asset); characters.Add(asset);
            }
            var three = characters.Where(x => x.Rarity == CharacterRarity.Three).ToArray();
            var four = characters.Where(x => x.Rarity == CharacterRarity.Four).ToArray();
            var five = characters.Where(x => x.Rarity == CharacterRarity.Five).ToArray();
            var limited = GetOrCreate<GachaBanner>(DataRoot + "/Banners/Limited_Maris.asset");
            FillBanner(limited, LimitedBannerId, "별을 가르는 물결", BannerType.Limited, rules, three, four, five,
                characters.Single(x => x.Id == "maris"));
            var standard = GetOrCreate<GachaBanner>(DataRoot + "/Banners/Standard_Companions.asset");
            FillBanner(standard, StandardBannerId, "여정의 동행", BannerType.Standard, rules, three, four, five, null);
            var catalog = GetOrCreate<GachaCatalog>(CatalogPath);
            catalog.Characters = characters.ToArray(); catalog.Banners = new[] { limited, standard }; catalog.Rules = rules;
            catalog.Validate(); EditorUtility.SetDirty(catalog);
            EnsureDemoScene();
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!buildScenes.Any(x => x.path == ScenePath))
            { buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = buildScenes.ToArray(); }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Validate();
            Debug.Log("ORBIS M1.5 ready: 18 character definitions, two independent banners, rules and Gacha Demo. Existing gameplay scenes preserved.");
        }

        private static void FillBanner(GachaBanner asset, string id, string displayName, BannerType type,
            GachaRules rules, CharacterDefinition[] three, CharacterDefinition[] four, CharacterDefinition[] five, CharacterDefinition featured)
        {
            asset.Id = id; asset.DisplayName = displayName; asset.Type = type; asset.Rules = rules;
            asset.ThreeStars = three; asset.FourStars = four; asset.FiveStars = five; asset.Featured = featured;
            // User-confirmed pool: starters are also 3-star pulls; all five guardians are in standard
            // and limited pools. Limited off-banner selection excludes Featured in GachaEngine.
            EditorUtility.SetDirty(asset);
        }

        [MenuItem("Orbis/M1.5/Validate Data")]
        public static void Validate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GachaCatalog>(CatalogPath);
            if (catalog == null) throw new BuildFailedException("M1.5 catalog is missing.");
            catalog.Validate();
            if (catalog.Characters.Length != 18 || catalog.Characters.Count(x => x.Rarity == CharacterRarity.Three) != 3 ||
                catalog.Characters.Count(x => x.Rarity == CharacterRarity.Four) != 10 || catalog.Characters.Count(x => x.Rarity == CharacterRarity.Five) != 5 ||
                catalog.Characters.Count(x => x.IsStarter) != 3 || catalog.Banners.Length != 2)
                throw new BuildFailedException("M1.5 must contain 3 starters, 10 four-stars, 5 guardians and two banners.");
            if (catalog.Banners[0].Id != LimitedBannerId || catalog.Banners[0].Featured == null || catalog.Banners[0].Featured.Id != "maris" ||
                catalog.Banners[1].Id != StandardBannerId)
                throw new BuildFailedException("M1.5 banner identity or featured character differs from the prototype contract.");
            string scriptGuid = AssetDatabase.AssetPathToGUID(DemoScript);
            if (string.IsNullOrEmpty(scriptGuid) || AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null ||
                !File.ReadAllText(ScenePath).Contains("guid: " + scriptGuid))
                throw new BuildFailedException("M1.5 demo scene is missing its bootstrap script reference.");
            if (!EditorBuildSettings.scenes.Any(x => x.path == ScenePath))
                throw new BuildFailedException("M1.5 demo must be appended to Build Settings.");
        }

        [MenuItem("Orbis/M1.5/Open Gacha Demo")]
        public static void OpenGachaDemo()
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
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path); return asset;
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
        private static void EnsureDemoScene()
        {
            if (File.Exists(ScenePath)) return; // Preserve user scene edits and the existing scene GUID.
            string guid = AssetDatabase.AssetPathToGUID(DemoScript);
            if (string.IsNullOrEmpty(guid)) throw new BuildFailedException("Import M15GachaDemo.cs before generating its scene.");
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
  m_Name: ORBIS M1.5 Gacha Demo
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
  m_EditorClassIdentifier: Orbis.M15.Runtime::Orbis.M15.M15GachaDemo
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