#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Orbis.Art;
using Orbis.M0.Animation;
using UnityEditor;
using UnityEngine;

namespace Orbis.Game.Tests
{
    /// <summary>
    /// Test-only Resources catalog overlay: replace the array with cloned entries in memory,
    /// then restore the original array reference exactly. No SaveAssets/SetDirty/serialized writes.
    /// This is necessary because ArtScenePresentation resolves Resources directly during scene load.
    /// </summary>
    internal sealed class CharacterMotionCandidateOverride : IDisposable
    {
        [Serializable] sealed class SourceFile { public string path, sha256; }
        [Serializable] sealed class GripBinding
        {
            public string prefab,gripEvidence,gripEvidenceSha256;
            public Vector3 socketLocalPosition,socketLocalEuler,socketLocalScale;
        }
        [Serializable] sealed class Evidence
        {
            public string sourceId, candidateRoot, prefab, avatar, controller, catalog, catalogFileSha256;
            public string mode = "Test-only in-memory Explorer entry: candidate prefab/avatar with unchanged existing controller, weapon, gameplay and world.";
            public SourceFile[] files;
        }

        readonly ArtAssetCatalog catalog;
        readonly ArtExplorerAsset[] originalExplorers;
        readonly string originalJson, catalogPath, catalogHash, catalogMetaHash, candidateRoot, sourceId, prefabPath;
        readonly bool originallyDirty;
        readonly Avatar avatar;
        readonly RuntimeAnimatorController controller;
        readonly GameObject prefab;
        readonly bool candidateMotion;
        readonly string gripBindingPath;
        bool restored;

        public CharacterMotionCandidateOverride(ArtAssetCatalog catalog, string root, string sourceId, string displayName, bool candidateMotion = false)
        {
            Assert.That(catalog, Is.Not.Null);
            root = root.Replace('\\', '/').TrimEnd('/');
            Assert.That(root.StartsWith("Assets/", StringComparison.Ordinal) && !root.Split('/').Any(p => p == ".." || p == "."), Is.True,
                "Candidate root must be an existing project Assets path.");
            prefabPath = root + "/" + displayName + "/" + displayName + ".prefab";
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, "Import the reviewed candidate before running this capture: " + prefabPath);
            var candidateAnimator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(candidateAnimator, Is.Not.Null);
            avatar = candidateAnimator.avatar;
            Assert.That(avatar != null && avatar.isValid && avatar.isHuman, Is.True, "Candidate requires a validated Humanoid Avatar.");
            Assert.That(candidateAnimator.applyRootMotion, Is.False);
            var original = catalog.Explorer(sourceId);
            Assert.That(original, Is.Not.Null);
            this.candidateMotion = candidateMotion;
            controller = candidateMotion ? candidateAnimator.runtimeAnimatorController : original.Controller;
            Assert.That(controller, Is.Not.Null);
            if (candidateMotion)
            {
                var driver = candidateAnimator.GetComponent<HumanAnimationDriver>();
                Assert.That(driver != null && driver.Profile != null, Is.True, "Motion After requires the reviewed candidate profile/driver.");
                driver.Profile.ValidateForBuild();
            }

            this.catalog = catalog; this.candidateRoot = root; this.sourceId = sourceId;
            originalExplorers = catalog.Explorers;
            originalJson = EditorJsonUtility.ToJson(catalog);
            originallyDirty = EditorUtility.IsDirty(catalog);
            catalogPath = AssetDatabase.GetAssetPath(catalog);
            catalogHash = Hash(catalogPath); catalogMetaHash = Hash(catalogPath + ".meta");
            var replacement = new ArtCharacterAsset
            {
                DisplayName = original.DisplayName, Element = original.Element, Prefab = prefab, Avatar = avatar,
                Controller = controller, Weapon = original.Weapon, WeaponBoneName = original.WeaponBoneName,
                WeaponLocalPosition = original.WeaponLocalPosition, WeaponLocalEuler = original.WeaponLocalEuler,
                WeaponLocalScale = original.WeaponLocalScale, TrailTipLocalPosition = original.TrailTipLocalPosition
            };
            string bindingPath=root+"/"+displayName+"/GripBinding.json";
            if(candidateMotion && File.Exists(bindingPath))
            {
                var grip=JsonUtility.FromJson<GripBinding>(File.ReadAllText(bindingPath));
                Assert.That(grip.prefab,Is.EqualTo(prefabPath));
                Assert.That(Hash(grip.gripEvidence),Is.EqualTo(grip.gripEvidenceSha256));
                Assert.That(grip.socketLocalScale.x>0 && grip.socketLocalScale.y>0 && grip.socketLocalScale.z>0,Is.True);
                replacement.WeaponLocalPosition=grip.socketLocalPosition;
                replacement.WeaponLocalEuler=grip.socketLocalEuler;
                replacement.WeaponLocalScale=grip.socketLocalScale;
                gripBindingPath=bindingPath;
            }
            Assert.That(originalExplorers.Count(x => x != null && x.SourceId == sourceId), Is.EqualTo(1));
            // Every entry is cloned, but only the selected character definition changes. Original
            // definitions, candidate prefab/Avatar and imported source assets are never edited.
            catalog.Explorers = originalExplorers.Select(x => x == null ? null : new ArtExplorerAsset
                { SourceId = x.SourceId, Character = x.SourceId == sourceId ? replacement : x.Character }).ToArray();
        }

        public void ValidateSpawn(Animator actual)
        {
            Assert.That(actual.avatar, Is.SameAs(avatar), "The recorded actor must actually use the candidate Avatar.");
            Assert.That(actual.runtimeAnimatorController, Is.SameAs(controller), "Capture must use the explicitly selected controller.");
            if (candidateMotion)
            {
                var driver = actual.GetComponent<HumanAnimationDriver>();
                Assert.That(driver, Is.Not.Null);
                Assert.That(driver.IsReady, Is.True, driver.ValidationError);
                Assert.That(driver.PresentationPivot.name, Is.EqualTo("Visual Facing"));
                Assert.That(actual.GetComponent<HumanFootIK>(), Is.Not.Null);
            }
            var candidateMeshes = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.sharedMesh).Where(m => m != null).ToArray();
            var spawned = actual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var mesh in candidateMeshes)
                Assert.That(spawned.Any(s => s.sharedMesh == mesh), Is.True, "Candidate skin must be present in the recorded actor: " + mesh.name);
        }

        public void WriteEvidence(string output)
        {
            var dependencies = AssetDatabase.GetDependencies(new[] { prefabPath, AssetDatabase.GetAssetPath(controller),
                AssetDatabase.GetAssetPath(catalog.Explorer(sourceId).Weapon) }.Where(p => !string.IsNullOrEmpty(p)).ToArray(), true)
                .Concat(new[] { catalogPath, "Assets/Scenes/Field.unity", "Assets/Orbis/M0/Runtime/Player/PlayerMotor.cs",
                    "Assets/Orbis/M0/Runtime/Combat/BasicAttackCombo.cs", "Assets/Orbis/M0/Runtime/Combat/ComboSequence.cs",
                    "Assets/Orbis/Game/Tests/ExplorerArtPlayMode/CharacterMotionCaptureTests.cs",
                    "Assets/Orbis/Game/Tests/ExplorerArtPlayMode/CharacterMotionRecorder.cs",
                    "Assets/Orbis/Game/Tests/ExplorerArtPlayMode/CharacterMotionCandidateOverride.cs",
                    "Assets/Orbis/Game/Tests/ExplorerArtPlayMode/CharacterMotionSkinProbe.cs",
                    "Assets/Orbis/Game/Tests/ExplorerArtPlayMode/CharacterMotionActionChecks.cs",
                    "Assets/Orbis/Art/Runtime/Characters/ArtCharacterRoster.cs", "Assets/Orbis/M2/Runtime/Traversal/ExplorationMotor.cs",
                    "Assets/Orbis/M16/Runtime/Combat/ExplorerController.cs", "Assets/Orbis/M3/Runtime/Presentation/M3Presentation.cs" })
                .Concat(Directory.GetFiles("Assets/Orbis/M0/Runtime/Animation", "*.cs"))
                .Concat(gripBindingPath==null?Array.Empty<string>():new[]{gripBindingPath})
                .SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists).Distinct().OrderBy(p => p).ToArray();
            var evidence = new Evidence { sourceId = sourceId, candidateRoot = candidateRoot, prefab = prefabPath,
                avatar = AssetDatabase.GetAssetPath(avatar), controller = AssetDatabase.GetAssetPath(controller),
                catalog = catalogPath, catalogFileSha256 = catalogHash,
                mode = candidateMotion ? "Test-only in-memory Explorer entry: same model/Avatar/weapon/world with reviewed motion controller/profile/driver/IK. "+(gripBindingPath==null?"Original socket retained.":"Explicit measured GripBinding socket applied in memory.")+" Live catalog restored exactly." :
                    "Test-only in-memory Explorer entry: candidate prefab/avatar with unchanged original controller, weapon, gameplay and world.",
                files = dependencies.Select(p => new SourceFile { path = p, sha256 = Hash(p) }).ToArray() };
            File.WriteAllText(Path.Combine(output, "source_provenance.json"), JsonUtility.ToJson(evidence, true));
        }

        public void Dispose()
        {
            if (restored) return;
            // Restore first, then assert. A failed evidence assertion must not leave a candidate selected.
            catalog.Explorers = originalExplorers; restored = true;
            Assert.That(catalog.Explorers, Is.SameAs(originalExplorers));
            Assert.That(EditorJsonUtility.ToJson(catalog), Is.EqualTo(originalJson), "The complete original catalog data must be restored.");
            Assert.That(EditorUtility.IsDirty(catalog), Is.EqualTo(originallyDirty), "Capture must not mark the production catalog dirty.");
            Assert.That(Hash(catalogPath), Is.EqualTo(catalogHash), "Production catalog file changed during capture.");
            Assert.That(Hash(catalogPath + ".meta"), Is.EqualTo(catalogMetaHash), "Production catalog metadata changed during capture.");
        }

        static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
