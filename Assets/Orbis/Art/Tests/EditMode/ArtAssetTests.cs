using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.M1;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Orbis.Art.Tests
{
    /// <summary>Read-only checks of the imported result. Run Art/Setup once before these tests.</summary>
    public sealed class ArtAssetTests
    {
        private const string CatalogPath = "Assets/Orbis/Art/Resources/Art/Catalog.asset";
        private const string ControllerPath = "Assets/Orbis/M0/Resources/M0/PlayerAnimator.controller";
        private const string KayKitCharacters = "Assets/ImportedAssets/KayKit/Adventurers/Characters/";
        private static readonly string[] States = { "Idle", "Walk", "Run", "Jump", "Attack1", "Attack2", "Attack3" };
        private static readonly string[] MotionNames =
        {
            "Idle_A", "Walking_A", "Running_A", "Jump_Idle",
            "Melee_1H_Attack_Slice_Horizontal", "Melee_1H_Attack_Slice_Diagonal", "Melee_1H_Attack_Chop"
        };
        private static readonly (string name, string model, ElementType element)[] Characters =
        {
            ("Ignis", "Knight", ElementType.Fire), ("Maris", "Mage", ElementType.Water),
            ("Aura", "Ranger", ElementType.Wind), ("Grom", "Barbarian", ElementType.Rock),
            ("Sparkle", "Rogue", ElementType.Lightning)
        };
        private ArtAssetCatalog catalog;

        [OneTimeSetUp]
        public void CaptureImportDiagnostics() => Orbis.Art.Editor.ArtCharacterImport.DiagnoseAnimationImports();

        [SetUp]
        public void LoadGeneratedCatalog()
        {
            catalog = AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null, "Run Orbis/Art/Setup and Validate before testing the generated assets.");
        }

        [Test]
        public void FiveCharactersUseTheirOwnValidHumanoidAvatarAndMatchingAtlas()
        {
            Assert.That(catalog.Characters.Length, Is.EqualTo(5));
            Assert.That(catalog.Characters.Select(c => c.Element).Distinct().Count(), Is.EqualTo(5));
            Assert.That(catalog.Characters.Select(c => c.Avatar).Distinct().Count(), Is.EqualTo(5),
                "Characters must not borrow another model's Avatar.");
            foreach (var expected in Characters)
            {
                ArtCharacterAsset character = catalog.Characters.Single(c => c.DisplayName == expected.name);
                Assert.That(character.Element, Is.EqualTo(expected.element), expected.name);
                string path = KayKitCharacters + expected.model + ".fbx";
                Assert.That(character.Prefab, Is.Not.Null, expected.name);
                Assert.That(character.Avatar, Is.Not.Null, expected.name);
                Assert.That(character.Avatar.isValid && character.Avatar.isHuman, Is.True, expected.name);
                Assert.That(AssetDatabase.GetAssetPath(character.Avatar), Is.EqualTo(path), expected.name);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human), expected.name);
                Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel), expected.name);
                Assert.That(importer.optimizeGameObjects, Is.False, "M3 and weapons need real bone transforms.");
                Assert.That(importer.isReadable, Is.True, "Player-build height normalization requires CPU access to character meshes.");

                Animator[] animators = character.Prefab.GetComponentsInChildren<Animator>(true);
                Assert.That(animators.Length, Is.EqualTo(1), expected.name);
                Assert.That(animators[0].avatar, Is.SameAs(character.Avatar), expected.name);
                Assert.That(animators[0].runtimeAnimatorController, Is.SameAs(character.Controller), expected.name);
                Assert.That(animators[0].applyRootMotion, Is.False, expected.name);
                Assert.That(character.Prefab.GetComponentsInChildren<Collider>(true), Is.Empty, expected.name);
                Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    KayKitCharacters + expected.model.ToLowerInvariant() + "_texture.png");
                Assert.That(atlas, Is.Not.Null, expected.name);
                Renderer[] renderers = character.Prefab.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(0), expected.name);
                foreach (MeshFilter filter in character.Prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    Assert.That(filter.sharedMesh, Is.Not.Null, expected.name + "/" + filter.name);
                    Assert.That(filter.sharedMesh.isReadable, Is.True, "Static body geometry must be readable outside the Editor: " + filter.name);
                }
                foreach (Renderer renderer in renderers)
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        AssertToon(material, renderer.name);
                        Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(atlas), expected.name + "/" + renderer.name);
                    }
            }
        }

        [Test]
        public void OverridesPreserveTheOriginalSevenStateControllerAndUseActualHumanoidClips()
        {
            var original = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(original, Is.Not.Null);
            Assert.That(original.layers.Length, Is.EqualTo(1));
            Assert.That(original.parameters, Is.Empty, "M0 drives the seven states directly; art must not change its interface.");
            AnimatorStateMachine machine = original.layers[0].stateMachine;
            CollectionAssert.AreEquivalent(States, machine.states.Select(s => s.state.name).ToArray());
            Assert.That(machine.stateMachines, Is.Empty);
            Assert.That(machine.anyStateTransitions, Is.Empty);
            Assert.That(machine.defaultState.name, Is.EqualTo("Idle"));
            foreach (ChildAnimatorState child in machine.states)
            {
                Assert.That(child.state.transitions, Is.Empty, child.state.name);
                Assert.That(child.state.motion.name, Is.EqualTo(child.state.name), "M0 source motions must remain intact.");
                Assert.That(AssetDatabase.GetAssetPath(child.state.motion), Does.StartWith("Assets/Orbis/M0/Resources/M0/"));
            }
            foreach (ArtCharacterAsset character in catalog.Characters)
            {
                Assert.That(character.Controller, Is.InstanceOf<AnimatorOverrideController>(), character.DisplayName);
                var controller = (AnimatorOverrideController)character.Controller;
                Assert.That(controller.runtimeAnimatorController, Is.SameAs(original), character.DisplayName);
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                controller.GetOverrides(overrides);
                Assert.That(overrides.Count, Is.EqualTo(7), character.DisplayName);
                CollectionAssert.AreEquivalent(States, overrides.Select(pair => pair.Key.name).ToArray());
                foreach (var pair in overrides)
                {
                    Assert.That(pair.Value, Is.Not.Null, pair.Key.name);
                    Assert.That(pair.Value.isHumanMotion, Is.True, pair.Value.name);
                    Assert.That(pair.Value.length, Is.GreaterThan(.01f), pair.Value.name);
                    string sourcePath = AssetDatabase.GetAssetPath(pair.Value);
                    if (!catalog.UsesMixamo)
                    {
                        Assert.That(pair.Value.name, Is.EqualTo(MotionNames[Array.IndexOf(States, pair.Key.name)]));
                        Assert.That(sourcePath, Does.StartWith("Assets/ImportedAssets/KayKit/CharacterAnimations/Rig_Medium/"));
                    }
                    else Assert.That(sourcePath, Does.StartWith("Assets/ImportedAssets/Mixamo/"));
                    Assert.That(sourcePath, Does.EndWith(".fbx"), "The override must reference an imported source clip.");
                }
            }
        }

        [Test]
        public void ActiveMotionImportsBakeRootMotionAndKeepAttacksNonLooping()
        {
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ((AnimatorOverrideController)catalog.Characters[0].Controller).GetOverrides(overrides);
            foreach (var pair in overrides)
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(pair.Value)) as ModelImporter;
                Assert.That(importer, Is.Not.Null, pair.Value.name);
                Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human), pair.Value.name);
                Assert.That(importer.importAnimation, Is.True);
                ModelImporterClipAnimation settings = importer.clipAnimations.Single(c => c.name == pair.Value.name);
                Assert.That(settings.lockRootRotation && settings.lockRootHeightY && settings.lockRootPositionXZ,
                    Is.True, pair.Value.name + " would compete with CharacterController movement.");
                Assert.That(settings.keepOriginalOrientation && settings.keepOriginalPositionY && settings.keepOriginalPositionXZ,
                    Is.True, pair.Value.name);
                if (pair.Key.name.StartsWith("Attack", StringComparison.Ordinal)) Assert.That(settings.loopTime, Is.False, pair.Value.name);
                if (pair.Key.name == "Idle" || pair.Key.name == "Walk" || pair.Key.name == "Run")
                    Assert.That(settings.loopTime, Is.True, pair.Value.name);
            }
        }

        [Test]
        public void GeneratedCharactersRetainSourceHierarchyScaleAndMeshes()
        {
            foreach (var expected in Characters)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(KayKitCharacters + expected.model + ".fbx");
                GameObject prefab = catalog.Characters.Single(c => c.DisplayName == expected.name).Prefab;
                var sourceTransforms = source.GetComponentsInChildren<Transform>(true)
                    .ToDictionary(t => AnimationUtility.CalculateTransformPath(t, source.transform));
                var generatedTransforms = prefab.GetComponentsInChildren<Transform>(true)
                    .ToDictionary(t => AnimationUtility.CalculateTransformPath(t, prefab.transform));
                CollectionAssert.AreEquivalent(sourceTransforms.Keys, generatedTransforms.Keys, expected.name);
                foreach (var pair in sourceTransforms)
                {
                    Transform actual = generatedTransforms[pair.Key];
                    Assert.That(Vector3.Distance(actual.localPosition, pair.Value.localPosition), Is.LessThan(.00001f), expected.name + "/" + pair.Key);
                    Assert.That(Vector3.Distance(actual.localScale, pair.Value.localScale), Is.LessThan(.00001f), expected.name + "/" + pair.Key);
                    Assert.That(Quaternion.Angle(actual.localRotation, pair.Value.localRotation), Is.LessThan(.001f), expected.name + "/" + pair.Key);
                }
                var sourceMeshes = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh).ToArray();
                var generatedMeshes = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh).ToArray();
                CollectionAssert.AreEquivalent(sourceMeshes, generatedMeshes, expected.name);
                Assert.That(sourceMeshes.Length, Is.GreaterThan(0), expected.name);
                // Full hierarchy equality catches an accidental editor-time 1.8m normalization or shifted feet.
                // The runtime roster alone applies the common gameplay height.
            }
        }

        [Test]
        public void SharedSwordUsesAnImportedMeshExistingSocketAndVisibleBladeTip()
        {
            GameObject sword = catalog.Characters[0].Weapon;
            Assert.That(sword, Is.Not.Null);
            MeshFilter[] filters = sword.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(filters.Length, Is.GreaterThan(0));
            foreach (MeshFilter filter in filters)
            {
                Assert.That(filter.sharedMesh, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(filter.sharedMesh),
                    Is.EqualTo("Assets/ImportedAssets/KayKit/Adventurers/Weapons/sword_1handed.fbx"));
            }
            Assert.That(sword.GetComponentsInChildren<Collider>(true), Is.Empty);
            foreach (ArtCharacterAsset character in catalog.Characters)
            {
                Assert.That(character.Weapon, Is.SameAs(sword));
                Assert.That(character.Prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == character.WeaponBoneName), Is.True);
                Assert.That(character.TrailTipLocalPosition.magnitude, Is.GreaterThan(.25f));
                Assert.That(character.TrailTipLocalPosition.magnitude, Is.LessThan(3f));
                Assert.That(character.WeaponLocalScale, Is.EqualTo(Vector3.one));
            }
            foreach (Renderer renderer in sword.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    AssertToon(material, renderer.name);
                    Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")),
                        Is.EqualTo("Assets/ImportedAssets/KayKit/Adventurers/Weapons/knight_texture.png"));
                }
        }

        [Test]
        public void EnvironmentModelsUseLicensedMeshesAndPackSpecificColorsWithoutColliders()
        {
            Assert.That(catalog.Environment.Length, Is.EqualTo(39));
            Assert.That(catalog.Environment.Select(entry => entry.Key).Distinct().Count(), Is.EqualTo(39));
            AssertEnvironmentSource("ground_stone", "NatureKit/Models/platform_stone.fbx");
            AssertEnvironmentSource("rock_bare", "NatureKit/Models/stone_largeA.fbx");
            AssertEnvironmentSource("floor_stone", "ModularDungeonKit/Models/template-floor.fbx");
            int palettes = 0, diffuseColors = 0;
            foreach (ArtEnvironmentAsset entry in catalog.Environment)
            {
                Assert.That(entry.Prefab, Is.Not.Null, entry.Key);
                Assert.That(entry.Prefab.GetComponentsInChildren<Collider>(true), Is.Empty, entry.Key);
                Renderer[] renderers = entry.Prefab.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(0), entry.Key);
                foreach (Renderer renderer in renderers)
                {
                    Mesh mesh = MeshOf(renderer);
                    Assert.That(mesh, Is.Not.Null, entry.Key + "/" + renderer.name);
                    Assert.That(mesh.vertexCount, Is.GreaterThan(0), entry.Key);
                    string sourcePath = AssetDatabase.GetAssetPath(mesh);
                    Assert.That(sourcePath, Does.StartWith("Assets/ImportedAssets/Kenney/"), "Built-in primitives are not replacement art: " + entry.Key);
                    Assert.That(sourcePath, Does.EndWith(".fbx"));
                    string pack = sourcePath.Split('/')[3];
                    Material[] materials = renderer.sharedMaterials;
                    Assert.That(materials.Length, Is.EqualTo(mesh.subMeshCount), entry.Key);
                    GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                    Renderer originalRenderer = source.GetComponentsInChildren<Renderer>(true).First(r => MeshOf(r) == mesh);
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material material = materials[i];
                        AssertToon(material, entry.Key);
                        if (pack == "NatureKit")
                        {
                            Material original = originalRenderer.sharedMaterials[i];
                            Color expected = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") :
                                original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white;
                            Color actual = material.GetColor("_BaseColor");
                            Assert.That(Vector4.Distance(expected, actual), Is.LessThan(.00001f), entry.Key + " lost the authored Nature color.");
                            diffuseColors++;
                        }
                        else
                        {
                            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")),
                                Is.EqualTo("Assets/ImportedAssets/Kenney/" + pack + "/Models/Textures/colormap.png"), entry.Key);
                            palettes++;
                        }
                    }
                }
            }
            Assert.That(palettes, Is.GreaterThan(0), "Castle and dungeon models require their own palette textures.");
            Assert.That(diffuseColors, Is.GreaterThan(0), "Nature models retain their authored diffuse colors.");
        }

        [Test]
        public void NineUiIconsReferenceDistinctImportedPngFilesWithAlpha()
        {
            string[] keys = { "panel", "button", "bar_back", "bar_fill", "star", "check", "arrow_left", "arrow_right", "close" };
            CollectionAssert.AreEquivalent(keys, catalog.Icons.Select(icon => icon.Key).ToArray());
            Assert.That(catalog.Icons.Select(icon => icon.Texture).Distinct().Count(), Is.EqualTo(9));
            foreach (ArtIconAsset icon in catalog.Icons)
            {
                Assert.That(icon.Texture, Is.Not.Null, icon.Key);
                Assert.That(icon.Texture.width * icon.Texture.height, Is.GreaterThan(1), icon.Key);
                string path = AssetDatabase.GetAssetPath(icon.Texture);
                Assert.That(path, Does.StartWith("Assets/ImportedAssets/Kenney/UIPack/PNG/"), icon.Key);
                Assert.That(path, Does.EndWith(".png"));
                Assert.That(File.Exists(path), Is.True, icon.Key);
                byte[] signature = File.ReadAllBytes(path).Take(8).ToArray();
                CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, signature, icon.Key);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.alphaIsTransparency, Is.True, icon.Key);
                Assert.That(importer.mipmapEnabled, Is.False, icon.Key);
            }
        }

        [Test]
        public void SharedToonAndOutlineShaderCompileAndAnimationSourceIsHonest()
        {
            AssertToon(catalog.DefaultToonMaterial, "Default");
            AssertToon(catalog.OutlineMaterial, "Outline");
            AssertToon(catalog.WaterMaterial, "Water");
            Assert.That(ShaderUtil.ShaderHasError(catalog.DefaultToonMaterial.shader), Is.False);
            Assert.That(catalog.OutlineMaterial.GetFloat("_OutlineOnly"), Is.EqualTo(1f));
            Assert.That(catalog.OutlineMaterial.GetFloat("_OutlinePixels"), Is.EqualTo(catalog.DefaultToonMaterial.GetFloat("_OutlinePixels")));
            Assert.That(catalog.AnimationSource, Is.Not.Empty);
            if (catalog.UsesMixamo)
            {
                foreach (string name in new[] { "Source_TPose", "Idle", "Walk", "Run", "Jump", "Attack01", "Attack02", "Attack03" })
                    Assert.That(File.Exists("Assets/ImportedAssets/Mixamo/" + name + ".fbx"), Is.True, name);
                Assert.That(catalog.AnimationSource, Does.Contain("Mixamo"));
            }
            else Assert.That(catalog.AnimationSource, Does.Contain("KayKit"));
        }

        private void AssertEnvironmentSource(string key, string expectedSource)
        {
            ArtEnvironmentAsset entry = catalog.Environment.Single(item => item.Key == key);
            Assert.That(entry.Prefab, Is.Not.Null, key);
            Mesh[] meshes = entry.Prefab.GetComponentsInChildren<Renderer>(true).Select(MeshOf).ToArray();
            Assert.That(meshes.Length, Is.GreaterThan(0), key);
            foreach (Mesh mesh in meshes)
                Assert.That(AssetDatabase.GetAssetPath(mesh), Is.EqualTo("Assets/ImportedAssets/Kenney/" + expectedSource), key);
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin) return skin.sharedMesh;
            return renderer.GetComponent<MeshFilter>()?.sharedMesh;
        }

        private static void AssertToon(Material material, string context)
        {
            Assert.That(material, Is.Not.Null, context);
            Assert.That(material.shader, Is.Not.Null, context);
            Assert.That(material.shader.name, Is.EqualTo("Orbis/Art/UnifiedToon"), context);
            Assert.That(material.HasProperty("_BaseMap") && material.HasProperty("_BaseColor"), Is.True, context);
        }
    }
}
