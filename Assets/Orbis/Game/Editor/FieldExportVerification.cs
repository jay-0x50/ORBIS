using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Orbis.Game.World;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>A reversible real-scene export check. Its temporary edit is never saved to the Field source.</summary>
    public static class FieldExportVerification
    {
        const string StatePath = "Assets/Orbis/Game/World/Authoring/FieldExportState.json";
        const string ReportPath = "TestResults/WorldDev/Field_ExportRoundtrip.json";
        [Serializable] sealed class FileProof { public string path, originalSha256, restoredSha256, afterStableExportSha256; }
        [Serializable] sealed class SceneProof { public string path; public bool loaded, active; }
        [Serializable] sealed class Audit
        {
            public bool passed, sourceFileUnchangedWhileDirty, loadedSourceStillDirty, noUnexpectedLoadedEnvironment,
                movedVisualMatches, movedCollisionMatches, cloneVisualMatches, cloneCollisionMatches, deletionMatches,
                authoringComponentStripped, residentCollisionRetained, restoredAllFiles, stableExportChangedNoFiles;
            public string error, restorationError, movedTree, clonedTree, deletedTree, region;
            public int originalLodCount, editedLodCount, originalColliderCount, editedColliderCount;
            public Vector3 movedPosition, clonedPosition;
            public SceneProof[] originalSceneSetup;
            public FileProof[] files;
        }

        public static void Run()
        {
            if (EditorApplication.isPlaying || !FieldSceneAuthoring.Exists)
                throw new InvalidOperationException("Verify an existing canonical Field in Edit Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save your own scene edits before running the reversible Field export verification.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            FieldSceneAuthoring.OpenField();
            FieldSceneAuthoring.EnsureExported();
            var scene = SceneManager.GetSceneByPath(FieldAuthoring.ScenePath);
            Require(scene.IsValid() && scene.isLoaded && !scene.isDirty && SceneManager.sceneCount == 1,
                "The verification needs the clean Field as the only open scene.");
            var paths = new[] { FieldAuthoring.ScenePath, IslandSceneBuilder.ScenePath }
                .Concat(Enum.GetValues(typeof(M4RegionId)).Cast<M4RegionId>().Select(WorldStreamingBuilder.ScenePath))
                .Concat(new[] { WorldStreamingBuilder.CatalogPath, StatePath }).ToArray();
            Require(paths.All(File.Exists), "A canonical Field/core/environment/catalog/export-state file is missing.");
            var protectedPaths = paths.Concat(paths.Select(p => p + ".meta").Where(File.Exists)).Distinct().ToArray();
            var original = protectedPaths.ToDictionary(p => p, File.ReadAllBytes);
            var audit = new Audit
            {
                originalSceneSetup = setup.Select(s => new SceneProof { path = s.path, loaded = s.isLoaded, active = s.isActive }).ToArray(),
                files = original.Select(p => new FileProof { path = p.Key, originalSha256 = Hash(p.Value) }).ToArray()
            };
            string sourceHash = audit.files.Single(p => p.path == FieldAuthoring.ScenePath).originalSha256;
            try
            {
                var field = Find<FieldAuthoring>(scene).Single();
                var selected = field.Regions.OrderBy(r => (int)r.Id)
                    .Select(r => new { Region = r, Trees = r.Root.GetComponentsInChildren<LODGroup>(true)
                        .Where(l => l.GetComponentsInChildren<CapsuleCollider>(true).Length == 1)
                        .OrderBy(l => l.name, StringComparer.Ordinal).ToArray() })
                    .First(r => r.Trees.Length >= 3);
                var region = selected.Region;
                var moved = selected.Trees[0].transform;
                var deleted = selected.Trees[1].transform;
                audit.region = region.Id.ToString();
                audit.originalLodCount = region.Root.GetComponentsInChildren<LODGroup>(true).Length;
                audit.originalColliderCount = region.Root.GetComponentsInChildren<Collider>(true).Length;
                // Unique names identify these exact probes in the exported visuals and resident transform chains.
                string token = Guid.NewGuid().ToString("N");
                moved.name = "FieldExportProbe_Moved_" + token;
                audit.movedTree = moved.name;
                moved.position += new Vector3(2.25f, .75f, -1.5f);
                moved.rotation = Quaternion.AngleAxis(17, Vector3.up) * moved.rotation;
                moved.localScale *= 1.08f;
                var clone = Object.Instantiate(selected.Trees[2].gameObject, selected.Trees[2].transform.parent).transform;
                clone.name = "FieldExportProbe_Clone_" + token;
                clone.position += new Vector3(-3.25f, .5f, 2.5f);
                audit.clonedTree = clone.name;
                audit.deletedTree = deleted.name;
                Require(Find<LODGroup>(scene).Count(t => t.name == audit.deletedTree) == 1,
                    "Deletion probe must be uniquely named before editing.");
                Object.DestroyImmediate(deleted.gameObject);
                audit.movedPosition = moved.position; audit.clonedPosition = clone.position;
                Matrix4x4 movedVisual = moved.localToWorldMatrix, cloneVisual = clone.localToWorldMatrix;
                Matrix4x4 movedCollider = moved.GetComponentInChildren<CapsuleCollider>(true).transform.localToWorldMatrix;
                Matrix4x4 cloneCollider = clone.GetComponentInChildren<CapsuleCollider>(true).transform.localToWorldMatrix;
                audit.editedLodCount = region.Root.GetComponentsInChildren<LODGroup>(true).Length;
                audit.editedColliderCount = region.Root.GetComponentsInChildren<Collider>(true).Length;
                EditorSceneManager.MarkSceneDirty(scene);

                FieldSceneAuthoring.EnsureExported();

                audit.sourceFileUnchangedWhileDirty = Hash(File.ReadAllBytes(FieldAuthoring.ScenePath)) == sourceHash;
                audit.loadedSourceStillDirty = scene.isLoaded && scene.isDirty && moved != null && clone != null &&
                    SameMatrix(moved.localToWorldMatrix, movedVisual) && SameMatrix(clone.localToWorldMatrix, cloneVisual);
                audit.noUnexpectedLoadedEnvironment = SceneManager.sceneCount == 1 && SceneManager.GetSceneAt(0).path == FieldAuthoring.ScenePath;
                Require(audit.sourceFileUnchangedWhileDirty, "Export wrote temporary edits to the canonical source file.");
                Require(audit.loadedSourceStillDirty, "Save-as-copy discarded or saved the designer's unsaved Field edits.");
                Require(audit.noUnexpectedLoadedEnvironment, "Export left duplicate runtime environment scenes loaded in Field.");

                var environment = EditorSceneManager.OpenScene(WorldStreamingBuilder.ScenePath(region.Id), OpenSceneMode.Additive);
                var core = EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath, OpenSceneMode.Additive);
                var movedExport = Find<LODGroup>(environment).Single(t => t.name == audit.movedTree);
                var cloneExport = Find<LODGroup>(environment).Single(t => t.name == audit.clonedTree);
                audit.movedVisualMatches = SameMatrix(movedExport.transform.localToWorldMatrix, movedVisual);
                audit.cloneVisualMatches = SameMatrix(cloneExport.transform.localToWorldMatrix, cloneVisual);
                Require(audit.movedVisualMatches && audit.cloneVisualMatches, "Edited visual transforms did not reach the exported region.");
                var resident = core.GetRootGameObjects().Single(g => g.name == FieldGeometryBinding.ResidentRootName);
                var movedResident = resident.GetComponentsInChildren<Transform>(true).Single(t => t.name == audit.movedTree);
                var cloneResident = resident.GetComponentsInChildren<Transform>(true).Single(t => t.name == audit.clonedTree);
                audit.movedCollisionMatches = SameMatrix(movedResident.GetComponentInChildren<CapsuleCollider>(true).transform.localToWorldMatrix, movedCollider);
                audit.cloneCollisionMatches = SameMatrix(cloneResident.GetComponentInChildren<CapsuleCollider>(true).transform.localToWorldMatrix, cloneCollider);
                Require(audit.movedCollisionMatches && audit.cloneCollisionMatches, "Exported resident collision differs from the edited visuals.");
                audit.deletionMatches = !Find<LODGroup>(environment).Any(t => t.name == audit.deletedTree) &&
                    !resident.GetComponentsInChildren<Transform>(true).Any(t => t.name == audit.deletedTree);
                Require(audit.deletionMatches, "A deleted tree or its resident collision survived export.");
                audit.authoringComponentStripped = !Find<FieldAuthoring>(core).Any() && !Find<FieldAuthoring>(environment).Any();
                audit.residentCollisionRetained = Find<Collider>(environment).Count() == 0 &&
                    resident.GetComponentsInChildren<Collider>(true).Length > 0 && Find<TerrainCollider>(core).Count() == 4;
                Require(audit.authoringComponentStripped && audit.residentCollisionRetained,
                    "Runtime scenes did not split visual scenery and persistent collision correctly.");
                // Inspection is over. The source is still open, dirty and unchanged on disk until finally restores it.
                EditorSceneManager.CloseScene(environment, true);
                EditorSceneManager.CloseScene(core, true);
                Require(scene.isDirty && SameMatrix(moved.localToWorldMatrix, movedVisual), "Inspection changed the loaded source edits.");
            }
            catch (Exception exception)
            {
                audit.error = exception.ToString();
                throw;
            }
            finally
            {
                try
                {
                    // Close only this test's loaded scenes without saving. An empty keeper avoids closing the last scene.
                    var keeper = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    var opened = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s != keeper).ToArray();
                    foreach (var loaded in opened) EditorSceneManager.CloseScene(loaded, true);
                    foreach (var item in original) File.WriteAllBytes(item.Key, item.Value);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    FieldSceneAuthoring.OpenField();
                    foreach (var proof in audit.files) proof.restoredSha256 = Hash(File.ReadAllBytes(proof.path));
                    audit.restoredAllFiles = audit.files.All(p => p.originalSha256 == p.restoredSha256);
                    Require(audit.restoredAllFiles, "Original field/export files were not restored byte for byte.");
                    FieldSceneAuthoring.EnsureExported();
                    foreach (var proof in audit.files) proof.afterStableExportSha256 = Hash(File.ReadAllBytes(proof.path));
                    audit.stableExportChangedNoFiles = audit.files.All(p => p.originalSha256 == p.afterStableExportSha256);
                    Require(audit.stableExportChangedNoFiles, "An unchanged restored Field needlessly regenerated export output.");
                    Require(SceneManager.sceneCount == 1 && !SceneManager.GetActiveScene().isDirty &&
                        SceneManager.GetActiveScene().path == FieldAuthoring.ScenePath, "Verification did not leave a clean canonical Field open.");
                    audit.passed = string.IsNullOrEmpty(audit.error);
                }
                catch (Exception exception)
                {
                    audit.restorationError = exception.ToString();
                    // A failed stable-export assertion must also leave the original files protected.
                    foreach (var item in original) File.WriteAllBytes(item.Key, item.Value);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    throw;
                }
                finally
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                    File.WriteAllText(ReportPath, JsonUtility.ToJson(audit, true));
                }
            }
            Debug.Log("ORBIS_FIELD_EXPORT_ROUNDTRIP_PASSED " + ReportPath);
        }

        static IEnumerable<T> Find<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
        static bool SameMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > .003f) return false;
            return true;
        }
        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
