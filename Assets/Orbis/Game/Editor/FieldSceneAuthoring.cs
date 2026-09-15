using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Orbis.EditorSupport;
using Orbis.Game.World;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Field is the editable source; existing scene GUIDs remain the runtime streaming outputs.</summary>
    public static class FieldSceneAuthoring
    {
        const string Folder = "Assets/Orbis/Game/World/Authoring";
        const string Working = Folder + "/_FieldWorking.unity";
        const string StatePath = Folder + "/FieldExportState.json";
        public static bool Exists => File.Exists(FieldAuthoring.ScenePath);
        public static bool IsExporting { get; private set; }
        [Serializable] sealed class Digest { public string path, sha256; }
        [Serializable] sealed class ExportState
        {
            public string sourceDependencyHash;
            public bool needsOcclusionBake;
            public Digest[] outputs;
        }
        static string[] Outputs => new[] { IslandSceneBuilder.ScenePath }
            .Concat(Enum.GetValues(typeof(M4RegionId)).Cast<M4RegionId>().Select(WorldStreamingBuilder.ScenePath))
            .Concat(new[] { WorldStreamingBuilder.CatalogPath }).ToArray();

        public static void RequireGeneratedEditingAllowed()
        {
            if (Exists && !IsExporting)
                throw new InvalidOperationException("Edit Assets/Scenes/Field.unity with Orbis > Field > Open Field. The old world generators would overwrite generated runtime scenes.");
        }

        public static void CreateFromCurrentWorld()
        {
            if (Exists) { OpenField(); return; }
            RequireIdle();
            const string migration = Folder + "/_FieldMigration.unity";
            if (File.Exists(migration) || File.Exists(migration + ".meta"))
                throw new IOException("A previous Field migration snapshot exists; inspect it before retrying: " + migration);
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            bool ownsMigration = false;
            Scene scene = default;
            FieldGeometryReport geometry;
            try
            {
                var core = EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath, OpenSceneMode.Single);
                ownsMigration = true;
                if (!EditorSceneManager.SaveScene(core, migration, true))
                    throw new IOException("Could not create the temporary Field migration scene.");
                scene = EditorSceneManager.OpenScene(migration, OpenSceneMode.Single);
                var entry = Find<GameSceneEntry>(scene).Single();
                var field = entry.gameObject.AddComponent<FieldAuthoring>();
                field.Entry = entry;
                var scenery = new GameObject("04 Scenery - editable regions");
                scenery.transform.SetParent(entry.World.transform, false);
                field.Regions = Enum.GetValues(typeof(M4RegionId)).Cast<M4RegionId>().Select(id =>
                {
                    var environment = EditorSceneManager.OpenScene(WorldStreamingBuilder.ScenePath(id), OpenSceneMode.Additive);
                    try
                    {
                        var roots = environment.GetRootGameObjects();
                        if (roots.Length != 1) throw new InvalidOperationException("Expected one regional environment root: " + id);
                        var root = roots[0];
                        SceneManager.MoveGameObjectToScene(root, scene);
                        root.transform.SetParent(scenery.transform, true);
                        return new FieldAuthoringRegion { Id = id, Root = root };
                    }
                    finally
                    {
                        // Source environment assets are never saved after moving their loaded objects.
                        if (environment.IsValid() && environment.isLoaded) EditorSceneManager.CloseScene(environment, true);
                    }
                }).ToArray();
                geometry = FieldGeometryBinding.AttachResidentGeometry(field);
                foreach (var grass in Find<WorldGrassField>(scene)) grass.EnableTransformEditing();
                ValidateSource(field);
                ValidateReferences(field);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the complete Field migration scene.");
                // Commit only a complete, validated scene. A failed conversion never leaves a bare
                // canonical Field that would make subsequent Create calls believe migration succeeded.
                EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath, OpenSceneMode.Single);
                scene = default;
                if (Exists || File.Exists(FieldAuthoring.ScenePath + ".meta"))
                    throw new IOException("Field appeared while migration was running; it has not been overwritten.");
                File.WriteAllText(migration, Regex.Replace(File.ReadAllText(migration),
                    @"m_OcclusionCullingData: \{[^\r\n]*\}", "m_OcclusionCullingData: {fileID: 0}"));
                AssetDatabase.ImportAsset(migration);
                string error = AssetDatabase.MoveAsset(migration, FieldAuthoring.ScenePath);
                if (!string.IsNullOrEmpty(error)) throw new IOException("Could not commit Field migration: " + error);
                EditorSceneManager.OpenScene(FieldAuthoring.ScenePath, OpenSceneMode.Single);
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded && scene.path == migration)
                    EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath, OpenSceneMode.Single);
                if (ownsMigration && File.Exists(migration)) AssetDatabase.DeleteAsset(migration);
            }
            Debug.Log("ORBIS_FIELD_CREATED " + JsonUtility.ToJson(geometry));
            // Export failure preserves the fully assembled canonical source for a normal retry.
            EnsureExported(true);
            OpenField();
        }

        public static void OpenField()
        {
            RequireIdle();
            if (!Exists) throw new FileNotFoundException("Create the Field source before opening it.", FieldAuthoring.ScenePath);
            var scene = SceneManager.GetSceneByPath(FieldAuthoring.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded || SceneManager.sceneCount != 1)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(FieldAuthoring.ScenePath, OpenSceneMode.Single);
            }
            ValidateSource(Find<FieldAuthoring>(scene).Single());
            FieldSceneBuildPolicy.ApplyProductLayout();
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(IslandSceneBuilder.ScenePath);
            Selection.activeGameObject = Find<FieldAuthoring>(scene).Single().Regions.Single(r => r.Id == M4RegionId.Zephyr).Root;
            EditorGUIUtility.PingObject(Selection.activeGameObject);
            if (!Application.isBatchMode)
            {
                // Authoring view default: village at human scale, not the 2 km scene bounds.
                var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
                view.in2DMode = false;
                view.sceneLighting = true;
                view.LookAt(new Vector3(-90, 35, -225), Quaternion.Euler(12, -91, 0), 135, false, true);
                view.Focus();
                view.Repaint();
            }
            EditorApplication.QueuePlayerLoopUpdate();
        }

        public static void EnsureExported(bool force = false)
        {
            if (!Exists || IsExporting) return;
            RequireIdle();
            var source = SceneManager.GetSceneByPath(FieldAuthoring.ScenePath);
            bool opened = !source.IsValid() || !source.isLoaded;
            var active = SceneManager.GetActiveScene();
            try
            {
                if (opened) source = EditorSceneManager.OpenScene(FieldAuthoring.ScenePath, OpenSceneMode.Additive);
                var field = Find<FieldAuthoring>(source).Single();
                ValidateSource(field);
                var dependency = AssetDatabase.GetAssetDependencyHash(FieldAuthoring.ScenePath).ToString();
                var state = File.Exists(StatePath) ? JsonUtility.FromJson<ExportState>(File.ReadAllText(StatePath)) : null;
                if (!force && !source.isDirty && state != null && state.sourceDependencyHash == dependency &&
                    state.outputs != null && state.outputs.Length == Outputs.Length &&
                    state.outputs.All(d => File.Exists(d.path) && Hash(d.path) == d.sha256))
                { FieldSceneBuildPolicy.ApplyProductLayout(); return; }
                Export(field, source.isDirty ? "" : dependency);
            }
            finally
            {
                if (opened && source.IsValid() && source.isLoaded) EditorSceneManager.CloseScene(source, true);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                foreach (var wind in Object.FindObjectsByType<WorldWind>(FindObjectsSortMode.None)) wind.RefreshNow();
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        static void Export(FieldAuthoring source, string dependency)
        {
            foreach (var path in Outputs.Where(p => p.EndsWith(".unity", StringComparison.Ordinal)))
            {
                var loaded = SceneManager.GetSceneByPath(path);
                if (loaded.IsValid() && loaded.isLoaded)
                    throw new InvalidOperationException("Open Field alone before exporting. Close the generated runtime scene (preserve any unsaved edits first): " + path);
            }
            Directory.CreateDirectory(Folder);
            var backup = Outputs.Where(File.Exists).ToDictionary(p => p, File.ReadAllBytes);
            IsExporting = true;
            Scene copy = default;
            bool ownsWorking = false;
            try
            {
                if (File.Exists(Working)) throw new IOException("A previous working snapshot exists; inspect it before retrying: " + Working);
                ownsWorking = true;
                if (!EditorSceneManager.SaveScene(source.gameObject.scene, Working, true))
                    throw new IOException("Could not snapshot Field. The source has not been modified.");
                copy = EditorSceneManager.OpenScene(Working, OpenSceneMode.Additive);
                var field = Find<FieldAuthoring>(copy).Single();
                ValidateReferences(field);
                var report = FieldGeometryBinding.ExtractResidentGeometry(field);
                var emptyParents = field.Regions.Select(r => r.Root.transform.parent).Distinct().ToArray();
                foreach (var region in field.Regions)
                {
                    var destination = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    try
                    {
                        region.Root.transform.SetParent(null, true);
                        SceneManager.MoveGameObjectToScene(region.Root, destination);
                        if (!EditorSceneManager.SaveScene(destination, WorldStreamingBuilder.ScenePath(region.Id)))
                            throw new IOException("Could not export " + region.Id);
                    }
                    finally { EditorSceneManager.CloseScene(destination, true); }
                }
                foreach (var parent in emptyParents)
                    if (parent != null && parent.childCount == 0) Object.DestroyImmediate(parent.gameObject);
                Object.DestroyImmediate(field);
                if (!EditorSceneManager.SaveScene(copy, IslandSceneBuilder.ScenePath))
                    throw new IOException("Could not export runtime Field core.");
                EditorSceneManager.CloseScene(copy, true);
                copy = default;
                // Moving renderer ownership invalidates the old PVS. Clear references instead of retaining stale visibility.
                foreach (var path in Outputs.Where(p => p.EndsWith(".unity", StringComparison.Ordinal)))
                    File.WriteAllText(path, Regex.Replace(File.ReadAllText(path),
                        @"m_OcclusionCullingData: \{[^\r\n]*\}", "m_OcclusionCullingData: {fileID: 0}"));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                WorldStreamingBuilder.RefreshEnvironmentBounds();
                WorldStreamingBuilder.Validate();
                FieldSceneBuildPolicy.ApplyProductLayout();
                WriteState(string.IsNullOrEmpty(dependency) ? "" : AssetDatabase.GetAssetDependencyHash(FieldAuthoring.ScenePath).ToString(), true);
                Debug.Log("ORBIS_FIELD_EXPORTED " + JsonUtility.ToJson(report));
            }
            catch
            {
                // Output replacement is transactional; user source and unsaved authoring changes stay untouched.
                if (copy.IsValid() && copy.isLoaded) { EditorSceneManager.CloseScene(copy, true); copy = default; }
                foreach (var item in backup) File.WriteAllBytes(item.Key, item.Value);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
            finally
            {
                if (copy.IsValid() && copy.isLoaded) EditorSceneManager.CloseScene(copy, true);
                if (ownsWorking && File.Exists(Working)) AssetDatabase.DeleteAsset(Working);
                IsExporting = false;
            }
        }

        public static void RecordBakedOutputs()
        {
            if (!Exists) return;
            var state = File.Exists(StatePath) ? JsonUtility.FromJson<ExportState>(File.ReadAllText(StatePath)) : null;
            WriteState(state?.sourceDependencyHash ?? "", false);
        }

        static void WriteState(string dependency, bool needsBake)
        {
            File.WriteAllText(StatePath, JsonUtility.ToJson(new ExportState {
                sourceDependencyHash = dependency, needsOcclusionBake = needsBake,
                outputs = Outputs.Select(p => new Digest { path = p, sha256 = Hash(p) }).ToArray()
            }, true));
            AssetDatabase.ImportAsset(StatePath);
        }

        public static void ValidateSource(FieldAuthoring field)
        {
            if (field == null || field.Entry == null || field.Entry.World == null ||
                field.Regions == null || field.Regions.Length != 5 ||
                field.Regions.Select(r => r.Id).Distinct().Count() != 5 ||
                field.Regions.Any(r => r.Root == null || r.Root.scene != field.gameObject.scene))
                throw new InvalidOperationException("Field must contain the saved gameplay world and five editable regional roots.");
            foreach (var a in field.Regions)
                foreach (var b in field.Regions)
                    if (a != b && a.Root.transform.IsChildOf(b.Root.transform))
                        throw new InvalidOperationException("Field region roots cannot nest.");
            if (Find<Terrain>(field.gameObject.scene).Count() < 4)
                throw new InvalidOperationException("The four resident terrain tiles are missing.");
        }

        static void ValidateReferences(FieldAuthoring field)
        {
            int Region(GameObject go)
            {
                for (int i = 0; i < field.Regions.Length; i++)
                    if (go.transform.IsChildOf(field.Regions[i].Root.transform)) return i;
                return -1;
            }
            foreach (var component in Find<Component>(field.gameObject.scene))
            {
                if (component == null) throw new InvalidOperationException("Missing script in Field.");
                if (component is Transform || component is FieldAuthoring) continue;
                int owner = Region(component.gameObject);
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var reference = property.objectReferenceValue;
                    var go = reference is GameObject g ? g : (reference as Component)?.gameObject;
                    if (go == null || EditorUtility.IsPersistent(go)) continue;
                    if (go.scene != field.gameObject.scene || Region(go) != owner)
                        throw new InvalidOperationException("Cross-streaming scene reference: " + component.name + "." +
                            property.propertyPath + " -> " + go.name + ". Keep gameplay under the resident world.");
                }
            }
        }

        static System.Collections.Generic.IEnumerable<T> Find<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
        static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static void RequireIdle()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before editing or exporting Field.");
        }
    }
}
