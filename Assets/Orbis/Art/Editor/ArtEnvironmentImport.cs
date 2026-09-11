using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.Art.Editor
{
    /// <summary>Rebuilds adapters from unmodified Kenney CC0 source files; never edits source meshes/materials.</summary>
    public static class ArtEnvironmentImport
    {
        private const string SourceRoot = "Assets/ImportedAssets/Kenney/";
        private const string OutputRoot = "Assets/Orbis/Art/Resources/Art/Environment";

        private readonly struct ModelMapping
        {
            public readonly string Key, Pack, Model;
            public ModelMapping(string key, string pack, string model)
            { Key = key; Pack = pack; Model = model; }
            public string Path => SourceRoot + Pack + "/Models/" + Model + ".fbx";
        }

        // Literal source filenames are verified against the downloaded archive. Semantic substitutions
        // (crystal/crate/lamp) are documented in Docs/Art_Environment_Mapping.md.
        private static readonly ModelMapping[] Models =
        {
            new ModelMapping("floor_stone", "ModularDungeonKit", "template-floor"),
            new ModelMapping("ground_grass", "NatureKit", "platform_grass"),
            new ModelMapping("ground_stone", "NatureKit", "platform_stone"),
            new ModelMapping("ground_dirt", "MiniDungeon", "dirt"),
            new ModelMapping("wall_stone", "CastleKit", "wall"),
            new ModelMapping("rock_large", "NatureKit", "rock_largeA"),
            new ModelMapping("rock_small", "NatureKit", "rock_smallA"),
            new ModelMapping("rock_bare", "NatureKit", "stone_largeA"),
            new ModelMapping("tree_oak", "NatureKit", "tree_oak"),
            new ModelMapping("tree_pine", "NatureKit", "tree_pineTallA_detailed"),
            new ModelMapping("tree_palm", "NatureKit", "tree_palm"),
            new ModelMapping("bush", "NatureKit", "plant_bushDetailed"),
            new ModelMapping("crystal", "NatureKit", "stone_tallA"),
            new ModelMapping("bridge", "NatureKit", "bridge_wood"),
            new ModelMapping("column", "MiniDungeon", "column"),
            new ModelMapping("arch", "CastleKit", "tower-square-arch"),
            new ModelMapping("fence", "NatureKit", "fence_simple"),
            new ModelMapping("barrel", "MiniDungeon", "barrel"),
            new ModelMapping("crate", "MiniDungeon", "chest"),
            new ModelMapping("chest", "MiniDungeon", "chest"),
            new ModelMapping("lamp", "NatureKit", "campfire_stones"),
            new ModelMapping("statue", "NatureKit", "statue_head"),
            new ModelMapping("enemy", "MiniDungeon", "character-orc"),
            new ModelMapping("banner", "CastleKit", "flag-banner-long"),
            new ModelMapping("roof", "CastleKit", "tower-square-top-roof-high"),
            new ModelMapping("stairs", "CastleKit", "stairs-stone"),
            new ModelMapping("board", "NatureKit", "sign"),
            // Regional silhouettes retain the source cave mouths, cliff profiles and mining supports.
            new ModelMapping("cliff", "NatureKit", "cliff_large_rock"),
            new ModelMapping("cave", "NatureKit", "cliff_cave_rock"),
            new ModelMapping("cliff_slope", "NatureKit", "cliff_blockSlope_rock"),
            new ModelMapping("ground_sand", "NatureKit", "platform_beach"),
            new ModelMapping("bridge_stone", "CastleKit", "bridge-straight"),
            new ModelMapping("mine_support", "MiniDungeon", "wood-support"),
            new ModelMapping("mine_structure", "MiniDungeon", "wood-structure"),
            new ModelMapping("boat", "NatureKit", "canoe"),
            new ModelMapping("obelisk", "NatureKit", "statue_obelisk"),
            new ModelMapping("coin", "MiniDungeon", "coin"),
            new ModelMapping("shield", "MiniDungeon", "shield-round")
        };

        private static readonly ModelMapping[] TowerParts =
        {
            new ModelMapping("tower_base", "CastleKit", "tower-square-base"),
            new ModelMapping("tower_mid", "CastleKit", "tower-square-mid-windows"),
            new ModelMapping("tower_top", "CastleKit", "tower-square-top-roof-high")
        };

        private static readonly (string Key, string Name)[] Ui =
        {
            ("panel", "button_square_border"),
            ("button", "button_rectangle_border"),
            ("bar_back", "slide_horizontal_grey"),
            ("bar_fill", "slide_horizontal_grey_section_wide"),
            ("star", "star"),
            ("check", "icon_checkmark"),
            ("arrow_left", "arrow_basic_w"),
            ("arrow_right", "arrow_basic_e"),
            ("close", "icon_cross")
        };

        public static void Build(ArtAssetCatalog catalog)
        {
            if (catalog == null || catalog.DefaultToonMaterial == null)
                throw new InvalidOperationException("Create ArtAssetCatalog.DefaultToonMaterial before importing environment art.");
            EnsureFolder(OutputRoot);
            EnsureFolder(OutputRoot + "/Materials");
            EnsureFolder(OutputRoot + "/Animations");

            // Import settings live in .meta files. The licensed FBX/PNG bytes remain unchanged.
            foreach (var mapping in Models.Concat(TowerParts)) PrepareModel(mapping.Path, mapping.Key == "enemy");
            foreach (string pack in new[] { "CastleKit", "ModularDungeonKit", "MiniDungeon" })
                PrepareTexture(SourceRoot + pack + "/Models/Textures/colormap.png", true);

            var materialCache = new Dictionary<Material, Material>();
            var environment = new List<ArtEnvironmentAsset>();
            foreach (var mapping in Models)
                environment.Add(new ArtEnvironmentAsset { Key = mapping.Key, Prefab = BuildModel(mapping, catalog, materialCache) });
            environment.Add(new ArtEnvironmentAsset { Key = "tower", Prefab = BuildTower(catalog, materialCache) });

            var icons = new List<ArtIconAsset>();
            foreach (var entry in Ui)
            {
                string path = SourceRoot + "UIPack/PNG/Grey/Double/" + entry.Name + ".png";
                PrepareTexture(path, false);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) throw new InvalidOperationException("Missing Kenney UI texture: " + path);
                icons.Add(new ArtIconAsset { Key = entry.Key, Texture = texture });
            }
            catalog.Environment = environment.ToArray();
            catalog.Icons = icons.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Art] Generated {environment.Count} environment prefabs and mapped {icons.Count} Kenney UI textures.");
        }

        private static void PrepareModel(string path, bool animated)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Missing selected Kenney model.", path);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("No ModelImporter for " + path);
            bool dirty = false;
            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard)
            { importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; dirty = true; }
            if (importer.materialLocation != ModelImporterMaterialLocation.InPrefab)
            { importer.materialLocation = ModelImporterMaterialLocation.InPrefab; dirty = true; }
            if (importer.addCollider) { importer.addCollider = false; dirty = true; }
            if (importer.importAnimation != animated) { importer.importAnimation = animated; dirty = true; }
            if (animated && importer.animationType != ModelImporterAnimationType.Generic)
            { importer.animationType = ModelImporterAnimationType.Generic; dirty = true; }
            if (dirty) importer.SaveAndReimport();
        }

        private static void PrepareTexture(string path, bool palette)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Missing selected Kenney texture.", path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("No TextureImporter for " + path);
            var filter = palette ? FilterMode.Point : FilterMode.Bilinear;
            bool dirty = importer.textureType != TextureImporterType.Default || importer.mipmapEnabled ||
                importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != filter ||
                importer.textureCompression != TextureImporterCompression.Uncompressed ||
                !importer.sRGBTexture || importer.alphaIsTransparency != !palette;
            if (!dirty) return;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false; // Palette UVs must not blend into adjacent colors at distant mip levels.
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = filter;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = !palette;
            importer.SaveAndReimport();
        }

        private static GameObject BuildModel(ModelMapping mapping, ArtAssetCatalog catalog, Dictionary<Material, Material> cache)
        {
            var root = new GameObject("Art / " + mapping.Key);
            try
            {
                var model = AddModel(root.transform, mapping, catalog, cache);
                AlignBase(model.transform, 0f);
                if (mapping.Key == "enemy") ConfigureIdle(model, mapping.Path);
                return Save(root, mapping.Key);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject BuildTower(ArtAssetCatalog catalog, Dictionary<Material, Material> cache)
        {
            var root = new GameObject("Art / tower");
            try
            {
                float top = 0f;
                foreach (var mapping in TowerParts)
                {
                    var part = AddModel(root.transform, mapping, catalog, cache);
                    AlignBase(part.transform, top);
                    top = BoundsOf(part).max.y;
                }
                return Save(root, "tower");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject AddModel(Transform parent, ModelMapping mapping, ArtAssetCatalog catalog, Dictionary<Material, Material> cache)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(mapping.Path);
            if (source == null) throw new InvalidOperationException("FBX did not import as a model: " + mapping.Path);
            var instance = Object.Instantiate(source, parent, false);
            instance.name = mapping.Pack + " / " + mapping.Model;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = AdaptMaterial(materials[i], mapping, catalog.DefaultToonMaterial, cache);
                renderer.sharedMaterials = materials;
            }
            return instance;
        }

        private static Material AdaptMaterial(Material original, ModelMapping mapping, Material template, Dictionary<Material, Material> cache)
        {
            if (original == null) throw new InvalidOperationException("Missing source material in " + mapping.Path);
            if (cache.TryGetValue(original, out var cached)) return cached;
            Color color = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") :
                original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white;
            string sourceProperty = original.HasProperty("_BaseMap") ? "_BaseMap" :
                original.HasProperty("_MainTex") ? "_MainTex" : null;
            Texture texture = sourceProperty != null ? original.GetTexture(sourceProperty) : null;
            Vector2 scale = sourceProperty != null ? original.GetTextureScale(sourceProperty) : Vector2.one;
            Vector2 offset = sourceProperty != null ? original.GetTextureOffset(sourceProperty) : Vector2.zero;
            // Nature's FBX has authored diffuse colors. Other three packs use their own separate UV palette.
            if (mapping.Pack != "NatureKit")
            {
                string atlasPath = SourceRoot + mapping.Pack + "/Models/Textures/colormap.png";
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
                if (texture == null) throw new InvalidOperationException("Missing palette: " + atlasPath);
                scale = Vector2.one; offset = Vector2.zero;
            }
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string guid, out long localId);
            string identity = string.IsNullOrEmpty(guid) ? Hash128.Compute(mapping.Path + original.name).ToString() : guid;
            string folder = OutputRoot + "/Materials/" + mapping.Pack;
            EnsureFolder(folder);
            string path = folder + "/" + SafeName(original.name) + "_" + identity.Substring(0, 8) + "_" + localId + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            { material = new Material(template); AssetDatabase.CreateAsset(material, path); }
            else { material.shader = template.shader; material.CopyPropertiesFromMaterial(template); }
            material.name = mapping.Pack + " / " + original.name;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTextureOffset("_BaseMap", offset);
            EditorUtility.SetDirty(material);
            cache.Add(original, material);
            return material;
        }

        private static void ConfigureIdle(GameObject model, string sourcePath)
        {
            var idle = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__") &&
                    (clip.name.Equals("idle", StringComparison.OrdinalIgnoreCase) || clip.name.EndsWith("|idle", StringComparison.OrdinalIgnoreCase)));
            if (idle == null)
            {
                Debug.LogWarning("[Art] Kenney orc imported without an idle clip; retaining its authored rest pose.");
                return;
            }
            string clipPath = OutputRoot + "/Animations/OrcIdle.anim";
            var loop = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (loop == null) { loop = Object.Instantiate(idle); AssetDatabase.CreateAsset(loop, clipPath); }
            else EditorUtility.CopySerialized(idle, loop);
            loop.name = "Kenney Orc Idle";
            var settings = AnimationUtility.GetAnimationClipSettings(loop);
            settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(loop, settings);
            EditorUtility.SetDirty(loop);
            string controllerPath = OutputRoot + "/Animations/OrcIdle.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.FirstOrDefault(entry => entry.state.name == "Idle").state;
            if (state == null) state = machine.AddState("Idle");
            state.motion = loop; machine.defaultState = state;
            EditorUtility.SetDirty(controller);
            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
        }

        private static void AlignBase(Transform model, float height)
        {
            Bounds bounds = BoundsOf(model.gameObject);
            model.position += new Vector3(-bounds.center.x, height - bounds.min.y, -bounds.center.z);
        }

        private static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Imported model has no renderers: " + root.name);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.sqrMagnitude < 0.000001f) throw new InvalidOperationException("Imported model has empty bounds: " + root.name);
            return bounds;
        }

        private static GameObject Save(GameObject root, string key)
        {
            string path = OutputRoot + "/" + key + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (prefab == null) throw new InvalidOperationException("Could not save generated environment prefab: " + path);
            return prefab;
        }

        private static string SafeName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value.Replace(' ', '_');
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}