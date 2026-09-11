using System;
using System.IO;
using System.Linq;
using Orbis.Art;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Orbis.M4;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>One editable island. The earlier small arena remains available for regression tests.</summary>
    public static class IslandSceneBuilder
    {
        public const string ScenePath = "Assets/Orbis/Game/Scenes/Orbis_Island.unity";
        public const string Generated = "Assets/Orbis/Game/Island";
        [MenuItem("Orbis/Game/Create Unified Island")]
        public static void CreateAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(ScenePath)) { BuildEntry(); Validate(); return; }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Generated + "/Materials");
            Directory.CreateDirectory(Generated + "/Meshes");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>("Assets/Orbis/Art/Resources/Art/Catalog.asset");
            if (catalog == null) throw new BuildFailedException("Import the Orbis Art catalog first.");
            var root = Node("ORBIS / Five Elements Island / 2 km", null, Vector3.zero);
            var world = root.gameObject.AddComponent<M4SceneBootstrap>();
            world.InitializeOnAwake = false;
            // Start on a gentle grassy trail; the initial selected character remains the saved protagonist.
            var serialized = new SerializedObject(world);
            serialized.FindProperty("region").enumValueIndex = (int)M4RegionId.Zephyr;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            IslandTerrainBuilder.Build(Node("01 Island Landscape", root, Vector3.zero), catalog);
            var regionsRoot = Node("02 Elemental Regions", root, Vector3.zero);
            var bindings = new M4IslandRegion[5];
            foreach (var profile in M4RegionCatalog.All)
            {
                Vector3 site = IslandTerrainBuilder.SiteCenter(profile.Id);
                var regionRoot = Node(profile.Id + " / " + profile.DisplayName, regionsRoot, site);
                var binding = regionRoot.gameObject.AddComponent<M4AuthoredRegion>();
                var builder = new IslandContentBuilder(catalog, binding, profile, site);
                builder.Build();
                bindings[(int)profile.Id] = new M4IslandRegion { Id = profile.Id, Authored = binding, Center = regionRoot };
            }
            world.IslandRegions = bindings;
            world.AuthoredRegion = bindings[(int)M4RegionId.Zephyr].Authored;
            BuildLighting(root, world, catalog);
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsPartOfPrefabInstance(transform))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(transform.gameObject);
                    foreach (var component in transform.GetComponents<Component>())
                        if (component != null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new BuildFailedException("Cannot save unified island.");
            BuildEntry();
            IslandScenePolish.ApplyAndValidate();
            IslandPresentationPolish.Apply();
            ExplorerArtImport.TryBuild();
            Validate();
            Debug.Log("ORBIS: authored 2,000 x 2,000 m island, five regions, one party; " + ScenePath);
        }

        [MenuItem("Orbis/Game/Open Unified Island")]
        public static void OpenIsland()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath)) CreateAndValidate();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var root = scene.GetRootGameObjects().First(x => x.GetComponent<M4SceneBootstrap>() != null);
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.LookAt(new Vector3(0, 50, 0), Quaternion.Euler(42, -30, 0), 1900);
        }

        [MenuItem("Orbis/Game/Validate Unified Island")]
        public static void Validate()
        {
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var all = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
                var worlds = all.Select(x => x.GetComponent<M4SceneBootstrap>()).Where(x => x != null).ToArray();
                if (worlds.Length != 1 || !worlds[0].IsIsland || worlds[0].InitializeOnAwake)
                    throw new BuildFailedException("Island requires one deferred world and five distinct bindings.");
                worlds[0].ValidateWorldBindings();
                var terrains = all.Select(x => x.GetComponent<Terrain>()).Where(x => x != null).ToArray();
                if (terrains.Length != 4 || terrains.Any(x => x.terrainData == null || x.GetComponent<TerrainCollider>() == null))
                    throw new BuildFailedException("Four persistent terrain tiles with collision are required.");
                if (all.Any(x => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(x.gameObject) > 0))
                    throw new BuildFailedException("Missing script in island.");
                foreach (var renderer in all.SelectMany(x => x.GetComponents<Renderer>()))
                    if (renderer.sharedMaterials.Any(x => x == null || !EditorUtility.IsPersistent(x)))
                        throw new BuildFailedException("Unsaved material: " + renderer.name);
                foreach (var filter in all.SelectMany(x => x.GetComponents<MeshFilter>()))
                    if (filter.sharedMesh == null || !EditorUtility.IsPersistent(filter.sharedMesh))
                        throw new BuildFailedException("Unsaved mesh: " + filter.name);
                Debug.Log("Island validation: " + all.Length + " objects, " + terrains.Length + " terrain tiles, five region bindings, all persistent.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void BuildEntry()
        {
            var entries = EditorBuildSettings.scenes.ToList();
            int index = entries.FindIndex(x => x.path == ScenePath);
            if (index < 0) entries.Add(new EditorBuildSettingsScene(ScenePath, true));
            else entries[index] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = entries.ToArray();
        }

        private static void BuildLighting(Transform root, M4SceneBootstrap world, ArtAssetCatalog catalog)
        {
            var view = Node("03 Sky and Light", root, Vector3.zero);
            var shader = Shader.Find("Orbis/Island Sky");
            if (shader == null) throw new BuildFailedException("Island sky shader is missing.");
            var sky = AssetDatabase.LoadAssetAtPath<Material>(Generated + "/Materials/IslandSky.mat");
            if (sky == null) { sky = new Material(shader) { name = "Island morning sky" }; AssetDatabase.CreateAsset(sky, Generated + "/Materials/IslandSky.mat"); }
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.58f, .71f, .83f);
            RenderSettings.ambientEquatorColor = new Color(.48f, .57f, .56f);
            RenderSettings.ambientGroundColor = new Color(.27f, .31f, .32f);
            // Art defaults for 2 km terrain: long aerial perspective, readable near-field contrast.
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.66f, .79f, .84f);
            RenderSettings.fogStartDistance = 500; RenderSettings.fogEndDistance = 2600;
            var sun = Node("Morning Sun", view, Vector3.zero).gameObject.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.4f; sun.color = new Color(1, .96f, .86f);
            sun.transform.rotation = Quaternion.Euler(42, -38, 0); sun.shadows = LightShadows.Soft;
            sun.shadowBias = .035f; sun.shadowNormalBias = .35f; RenderSettings.sun = sun;
            var focus = Node("Island Overview Focus", view, new Vector3(0, 35, 0));
            var camera = Node("Island Overview Camera", view, new Vector3(-1420, 1510, -1710)).gameObject.AddComponent<Camera>();
            camera.transform.LookAt(focus); camera.fieldOfView = 51; camera.farClipPlane = 6500;
            camera.clearFlags = CameraClearFlags.Skybox; camera.gameObject.AddComponent<AudioListener>();
            var entry = root.gameObject.AddComponent<GameSceneEntry>();
            entry.World = world; entry.OverviewCamera = camera; entry.OverviewFocus = focus;
        }

        internal static Transform Node(string name, Transform parent, Vector3 position)
        {
            var value = new GameObject(name).transform; value.SetParent(parent, false); value.position = position; return value;
        }
    }
}
