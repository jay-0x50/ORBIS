using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.M1;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.Art.Editor
{
    /// <summary>
    /// Rebuilds derived character adapters from licensed, unmodified source FBXs.
    /// Source proportions remain intact; ArtCharacterRoster fits the visual to the existing pawn.
    /// </summary>
    public static class ArtCharacterImport
    {
        private const string Sources = "Assets/ImportedAssets/KayKit/Adventurers";
        private const string Animations = "Assets/ImportedAssets/KayKit/CharacterAnimations/Rig_Medium";
        private const string Output = "Assets/Orbis/Art/Resources/Art/Characters";
        private const string BaseControllerPath = "Assets/Orbis/M0/Resources/M0/PlayerAnimator.controller";
        private const string MixamoDirectory = "Assets/ImportedAssets/Mixamo";

        private static readonly string[] StateNames = { "Idle", "Walk", "Run", "Jump", "Attack1", "Attack2", "Attack3" };
        private static readonly string[] KayKitClips =
        {
            "Idle_A", "Walking_A", "Running_A", "Jump_Idle",
            "Melee_1H_Attack_Slice_Horizontal", "Melee_1H_Attack_Slice_Diagonal", "Melee_1H_Attack_Chop"
        };
        private static readonly string[] MixamoFiles =
            { "Source_TPose", "Idle", "Walk", "Run", "Jump", "Attack01", "Attack02", "Attack03" };
        private static readonly string[] SourceJoints =
        {
            "root", "hips", "spine", "chest", "head",
            "upperarm.l", "lowerarm.l", "wrist.l", "hand.l", "handslot.l",
            "upperarm.r", "lowerarm.r", "wrist.r", "hand.r", "handslot.r",
            "upperleg.l", "lowerleg.l", "foot.l", "toes.l",
            "upperleg.r", "lowerleg.r", "foot.r", "toes.r"
        };
        // The source has 23 joints, of which 18 correspond to Unity human slots.
        // Its wrist is the anatomical hand pivot. Palm/weapon sockets and root remain extra bones.
        // No artificial neck, clavicle, or finger mapping is introduced.
        private static readonly (string source, string human)[] KayKitHuman =
        {
            ("hips", "Hips"), ("spine", "Spine"), ("chest", "Chest"), ("head", "Head"),
            ("upperarm.l", "LeftUpperArm"), ("lowerarm.l", "LeftLowerArm"), ("wrist.l", "LeftHand"),
            ("upperarm.r", "RightUpperArm"), ("lowerarm.r", "RightLowerArm"), ("wrist.r", "RightHand"),
            ("upperleg.l", "LeftUpperLeg"), ("lowerleg.l", "LeftLowerLeg"), ("foot.l", "LeftFoot"), ("toes.l", "LeftToes"),
            ("upperleg.r", "RightUpperLeg"), ("lowerleg.r", "RightLowerLeg"), ("foot.r", "RightFoot"), ("toes.r", "RightToes")
        };
        private static readonly (string source, string human)[] MixamoHuman =
        {
            ("Hips", "Hips"), ("Spine", "Spine"), ("Spine1", "Chest"), ("Spine2", "UpperChest"),
            ("Neck", "Neck"), ("Head", "Head"), ("LeftShoulder", "LeftShoulder"), ("RightShoulder", "RightShoulder"),
            ("LeftArm", "LeftUpperArm"), ("LeftForeArm", "LeftLowerArm"), ("LeftHand", "LeftHand"),
            ("RightArm", "RightUpperArm"), ("RightForeArm", "RightLowerArm"), ("RightHand", "RightHand"),
            ("LeftUpLeg", "LeftUpperLeg"), ("LeftLeg", "LeftLowerLeg"), ("LeftFoot", "LeftFoot"), ("LeftToeBase", "LeftToes"),
            ("RightUpLeg", "RightUpperLeg"), ("RightLeg", "RightLowerLeg"), ("RightFoot", "RightFoot"), ("RightToeBase", "RightToes")
        };

        public static void Build(ArtAssetCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            RuntimeAnimatorController baseController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BaseControllerPath);
            if (baseController == null) throw new InvalidOperationException("Build the existing M0 PlayerAnimator before importing character art.");

            string[] animationFiles = Directory.GetFiles(Animations, "*.fbx").OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (animationFiles.Length != 8)
                throw new InvalidOperationException("Expected the eight official KayKit Medium animation FBXs: " + Animations);
            foreach (string file in animationFiles)
                ImportKayKitHumanoid(file.Replace('\\', '/'), true);
            AnimationClip[] clips = KayKitClips.Select(LoadKayKitClip).ToArray();
            bool usesMixamo = TryLoadMixamo(out AnimationClip[] mixamoClips);
            if (usesMixamo) clips = mixamoClips;

            Shader shader = catalog.DefaultToonMaterial != null ? catalog.DefaultToonMaterial.shader : Shader.Find("Orbis/Art/UnifiedToon");
            if (shader == null) throw new InvalidOperationException("The shared Orbis/Art/UnifiedToon shader is missing.");
            Material swordMaterial = BuildMaterial("Sword_1H", Sources + "/Weapons/knight_texture.png", catalog, shader);
            GameObject sword = BuildWeapon(swordMaterial, out Vector3 bladeTip);
            var definitions = new[]
            {
                (name: "Ignis", source: "Knight", element: ElementType.Fire),
                (name: "Maris", source: "Mage", element: ElementType.Water),
                (name: "Aura", source: "Ranger", element: ElementType.Wind),
                (name: "Grom", source: "Barbarian", element: ElementType.Rock),
                (name: "Sparkle", source: "Rogue", element: ElementType.Lightning)
            };
            var characters = new List<ArtCharacterAsset>();
            foreach (var definition in definitions)
            {
                string modelPath = Sources + "/Characters/" + definition.source + ".fbx";
                Avatar avatar = ImportKayKitHumanoid(modelPath, false);
                Material material = BuildMaterial(definition.name,
                    Sources + "/Characters/" + definition.source.ToLowerInvariant() + "_texture.png", catalog, shader);
                AnimatorOverrideController controller = BuildOverride(definition.name, baseController, clips);
                GameObject prefab = BuildCharacter(definition.name, modelPath, material, avatar, controller);
                characters.Add(new ArtCharacterAsset
                {
                    DisplayName = definition.name, Element = definition.element, Prefab = prefab,
                    Avatar = avatar, Controller = controller, Weapon = sword, WeaponBoneName = "handslot.r",
                    // The official Unity-ready sword and authored hand socket share their native orientation.
                    // One shared sword preserves the existing one-handed melee gameplay for every element.
                    WeaponLocalPosition = Vector3.zero, WeaponLocalEuler = Vector3.zero,
                    WeaponLocalScale = Vector3.one, TrailTipLocalPosition = bladeTip
                });
            }
            catalog.Characters = characters.ToArray();
            catalog.UsesMixamo = usesMixamo;
            catalog.AnimationSource = usesMixamo
                ? "User-provided Mixamo FBX / Humanoid retargeted to each KayKit Avatar"
                : "KayKit Character Animations 1.1 / CC0 (complete Mixamo input unavailable)";
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("ORBIS character import: five own Humanoid Avatars, five seven-state overrides, five textured prefabs. Source: " +
                catalog.AnimationSource + ". Native sword trail tip: " + bladeTip);
        }

        /// <summary>Read-only importer messages, including the same serialized diagnostics shown by Unity's Animation tab.</summary>
        [MenuItem("Orbis/Art/Report Animation Import Messages")]
        public static void DiagnoseAnimationImports()
        {
            var report = new System.Text.StringBuilder();
            const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var getLog = typeof(AssetImporter).GetMethod("GetImportLog",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                null, new[] { typeof(string) }, null);
            foreach (string file in Directory.GetFiles(Animations, "*.fbx").OrderBy(path => path, StringComparer.Ordinal))
            {
                string path = file.Replace('\\', '/');
                var importer = RequireImporter(path);
                report.AppendLine(path);
                bool any = false;
                using (var serialized = new SerializedObject(importer))
                    foreach (string field in new[] { "m_AnimationImportErrors", "m_AnimationImportWarnings", "m_AnimationRetargetingWarnings" })
                    {
                        SerializedProperty property = serialized.FindProperty(field);
                        if (property == null || string.IsNullOrWhiteSpace(property.stringValue)) continue;
                        report.AppendLine(field + ": " + property.stringValue); any = true;
                    }
                // GetImportLog is internal in some Editor versions. Reflection only reads existing log entries.
                object log = getLog?.Invoke(null, new object[] { path });
                object entries = log?.GetType().GetProperty("logEntries", members)?.GetValue(log);
                if (entries is System.Collections.IEnumerable enumerable)
                    foreach (object entry in enumerable)
                    {
                        object message = entry.GetType().GetField("message", members)?.GetValue(entry) ??
                            entry.GetType().GetProperty("message", members)?.GetValue(entry);
                        object flags = entry.GetType().GetField("flags", members)?.GetValue(entry) ??
                            entry.GetType().GetProperty("flags", members)?.GetValue(entry);
                        if (message == null || string.IsNullOrWhiteSpace(message.ToString())) continue;
                        report.AppendLine(flags + ": " + message); any = true;
                    }
                if (!any) report.AppendLine("No stored animation or import-log messages.");
                report.AppendLine();
            }
            Directory.CreateDirectory("TestResults");
            const string output = "TestResults/ArtImportDiagnostics.txt";
            File.WriteAllText(output, report.ToString());
            Debug.Log("ORBIS animation import diagnostics: " + Path.GetFullPath(output) + "\n" + report);
        }

        private static Avatar ImportKayKitHumanoid(string path, bool importAnimation)
        {
            var importer = RequireImporter(path);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new InvalidOperationException("Cannot load source model " + path);
            var names = new HashSet<string>(model.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            string[] missing = SourceJoints.Where(name => !names.Contains(name)).ToArray();
            if (missing.Length != 0)
                throw new InvalidOperationException(path + " is not the verified 23-joint Medium rig. Missing: " + string.Join(", ", missing));
            ConfigureImporter(importer, importAnimation);
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.humanDescription = Describe(model, KayKitHuman, false);
            if (importAnimation) ConfigureClips(importer, null);
            importer.SaveAndReimport();
            return RequireAvatar(path);
        }

        private static void ConfigureImporter(ModelImporter importer, bool importAnimation)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.importAnimation = importAnimation;
            importer.optimizeGameObjects = false; // Bone sockets and M3 skinned-mesh snapshots need real transforms.
            // Runtime height normalization reads static body vertices before equipping the weapon.
            // Keep the small character meshes CPU-readable in player builds as well as in the Editor.
            importer.isReadable = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            // Preserve the source coordinate/unit conversion and authored proportions.
        }

        private static HumanDescription Describe(GameObject model, (string source, string human)[] mapping, bool mixamo)
        {
            Transform[] transforms = model.GetComponentsInChildren<Transform>(true);
            var human = new List<HumanBone>();
            foreach (var entry in mapping)
            {
                Transform transform = transforms.FirstOrDefault(t =>
                    mixamo ? Unprefix(t.name) == entry.source : t.name == entry.source);
                if (transform == null)
                {
                    int index = Array.IndexOf(HumanTrait.BoneName, entry.human);
                    if (index >= 0 && HumanTrait.RequiredBone(index))
                        throw new InvalidOperationException(model.name + " lacks required human bone " + entry.source);
                    continue;
                }
                human.Add(new HumanBone
                {
                    boneName = transform.name, humanName = entry.human,
                    limit = new HumanLimit { useDefaultValues = true }
                });
            }
            return new HumanDescription
            {
                human = human.ToArray(),
                skeleton = transforms.Select(t => new SkeletonBone
                {
                    name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale
                }).ToArray(),
                upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f,
                armStretch = .05f, legStretch = .05f, feetSpacing = 0f, hasTranslationDoF = false
            };
        }

        private static string Unprefix(string name)
        {
            int colon = name.LastIndexOf(':');
            return colon >= 0 ? name.Substring(colon + 1) : name;
        }

        private static void ConfigureClips(ModelImporter importer, bool? forceLoop)
        {
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips.Length == 0) throw new InvalidOperationException("No animation takes in " + importer.assetPath);
            foreach (ModelImporterClipAnimation clip in clips)
            {
                bool loop = forceLoop ?? (clip.name.StartsWith("Idle_", StringComparison.Ordinal) ||
                    clip.name.StartsWith("Walking_", StringComparison.Ordinal) ||
                    clip.name.StartsWith("Running_", StringComparison.Ordinal) || clip.name == "Jump_Idle");
                clip.loopTime = loop;
                clip.loopPose = loop;
                // The existing CharacterController owns translation, gravity, facing, and attack timing.
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        private static ModelImporter RequireImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new FileNotFoundException("Source FBX is missing or not imported.", path);
            return importer;
        }

        private static Avatar RequireAvatar(string path)
        {
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault(a => a.isValid && a.isHuman);
            if (avatar == null) throw new InvalidOperationException("Humanoid Avatar validation failed: " + path);
            return avatar;
        }

        private static AnimationClip LoadKayKitClip(string name)
        {
            string file = name == "Idle_A" ? "Rig_Medium_General.fbx" :
                name.StartsWith("Melee_", StringComparison.Ordinal) ? "Rig_Medium_CombatMelee.fbx" : "Rig_Medium_MovementBasic.fbx";
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(Animations + "/" + file)
                .OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
            if (clip == null || !clip.isHumanMotion || clip.length <= 0f)
                throw new InvalidOperationException("Required Humanoid animation missing or invalid: " + name + " in " + file);
            return clip;
        }

        private static bool TryLoadMixamo(out AnimationClip[] clips)
        {
            clips = null;
            string[] paths = MixamoFiles.Select(name => MixamoDirectory + "/" + name + ".fbx").ToArray();
            int supplied = paths.Count(File.Exists);
            if (supplied == 0)
            {
                Debug.Log("ORBIS Mixamo: no user FBXs supplied; using the licensed KayKit clips. The optional retarget import path has not been tested with actual Mixamo files.");
                return false;
            }
            string[] missing = paths.Where(path => !File.Exists(path)).ToArray();
            if (missing.Length > 0)
            {
                Debug.LogWarning("ORBIS Mixamo: partial input retained without mixing motion sources. Using KayKit. Missing: " + string.Join(", ", missing));
                return false;
            }

            var sourceImporter = RequireImporter(paths[0]);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(paths[0]);
            if (source == null) throw new InvalidOperationException("Cannot load Mixamo Source_TPose.fbx.");
            ConfigureImporter(sourceImporter, false);
            sourceImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            sourceImporter.sourceAvatar = null;
            sourceImporter.humanDescription = Describe(source, MixamoHuman, true);
            sourceImporter.SaveAndReimport();
            Avatar sourceAvatar = RequireAvatar(paths[0]);
            clips = new AnimationClip[StateNames.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                var importer = RequireImporter(paths[i + 1]);
                ConfigureImporter(importer, true);
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = sourceAvatar;
                ConfigureClips(importer, i < 3);
                importer.SaveAndReimport();
                AnimationClip[] motions = AssetDatabase.LoadAllAssetsAtPath(paths[i + 1]).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
                if (motions.Length != 1 || !motions[0].isHumanMotion || motions[0].length <= 0f)
                    throw new InvalidOperationException("Each Mixamo motion FBX must contain exactly one valid Humanoid clip: " + paths[i + 1]);
                clips[i] = motions[0];
            }
            Debug.Log("ORBIS Mixamo: all eight supplied FBXs imported and source Humanoid validated. Runtime uses each character's own KayKit Avatar; inspect the retargeted poses in Play mode.");
            return true;
        }

        private static Material BuildMaterial(string name, string texturePath, ArtAssetCatalog catalog, Shader shader)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) throw new FileNotFoundException("Character-specific atlas missing.", texturePath);
            string path = Output + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = catalog.DefaultToonMaterial != null ? new Material(catalog.DefaultToonMaterial) : new Material(shader);
                material.name = name + " / KayKit Atlas";
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_OutlineOnly", 0f);
            material.SetFloat("_Threshold", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static AnimatorOverrideController BuildOverride(string name, RuntimeAnimatorController baseController, AnimationClip[] clips)
        {
            string path = Output + "/" + name + ".overrideController";
            AnimatorOverrideController controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (controller == null)
            {
                controller = new AnimatorOverrideController(baseController) { name = name + " / Seven States" };
                AssetDatabase.CreateAsset(controller, path);
            }
            controller.runtimeAnimatorController = baseController;
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            controller.GetOverrides(overrides);
            if (overrides.Count != StateNames.Length)
                throw new InvalidOperationException("The M0 controller must retain exactly seven source clips.");
            for (int i = 0; i < overrides.Count; i++)
            {
                int index = Array.IndexOf(StateNames, overrides[i].Key.name);
                if (index < 0) throw new InvalidOperationException("Unexpected M0 clip: " + overrides[i].Key.name);
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, clips[index]);
            }
            controller.ApplyOverrides(overrides);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static GameObject BuildCharacter(string name, string modelPath, Material material, Avatar avatar, RuntimeAnimatorController controller)
        {
            GameObject instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            try
            {
                instance.name = name;
                ApplyMaterial(instance, material);
                if (instance.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Character visual must not contain gameplay colliders: " + name);
                Animator animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    renderer.updateWhenOffscreen = true;
                return PrefabUtility.SaveAsPrefabAsset(instance, Output + "/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static GameObject BuildWeapon(Material material, out Vector3 tip)
        {
            const string sourcePath = Sources + "/Weapons/sword_1handed.fbx";
            var importer = RequireImporter(sourcePath);
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.addCollider = false;
            importer.SaveAndReimport();
            GameObject instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
            try
            {
                instance.name = "KayKit Sword 1H";
                ApplyMaterial(instance, material);
                bool found = false;
                Bounds bounds = default;
                foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    Bounds mesh = filter.sharedMesh.bounds;
                    for (int x = -1; x <= 1; x += 2)
                        for (int y = -1; y <= 1; y += 2)
                            for (int z = -1; z <= 1; z += 2)
                            {
                                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3(x, y, z));
                                Vector3 point = instance.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                                else bounds.Encapsulate(point);
                            }
                }
                if (!found) throw new InvalidOperationException("The source sword has no mesh.");
                int axis = bounds.size.x > bounds.size.y ? 0 : 1;
                if (bounds.size.z > bounds.size[axis]) axis = 2;
                tip = bounds.center;
                // The authored grip is at the origin; the far endpoint on the longest axis is the blade tip.
                tip[axis] = Mathf.Abs(bounds.max[axis]) >= Mathf.Abs(bounds.min[axis]) ? bounds.max[axis] : bounds.min[axis];
                return PrefabUtility.SaveAsPrefabAsset(instance, Output + "/Sword_1H.prefab");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static void ApplyMaterial(GameObject root, Material material)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                int slots = renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null ? skin.sharedMesh.subMeshCount :
                    renderer.GetComponent<MeshFilter>()?.sharedMesh?.subMeshCount ?? renderer.sharedMaterials.Length;
                renderer.sharedMaterials = Enumerable.Repeat(material, Math.Max(1, slots)).ToArray();
            }
        }
    }
}
