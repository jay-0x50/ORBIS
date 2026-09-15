using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Game.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Evidence from the saved authoring scene, before any gameplay Start/Initialize method runs.</summary>
    public static class FieldSceneEvidence
    {
        const string DirectoryPath = "TestResults/WorldDev";

        [Serializable] sealed class HierarchyNode
        {
            public string name;
            public bool active;
            public string[] components;
            public int childCount;
            public HierarchyNode[] children;
        }

        [Serializable] sealed class RegionEvidence
        {
            public string id, root;
            public int objects, renderers, colliders, lodGroups, grassFields;
            public long grassInstances;
        }

        [Serializable] sealed class Report
        {
            public string scene, capturedUtc, graphics;
            public bool isPlaying, gameEntryHasStarted, sceneWasDirty;
            public int loadedScenes, objects, terrainCount, rendererCount, colliderCount, lodGroupCount;
            public int missingScripts, missingObjectReferences, missingMeshes, missingMaterials;
            public string[] issues;
            public RegionEvidence[] regions;
            public HierarchyNode[] hierarchy;
            public string note = "Saved Field scene rendered in Unity Edit Mode. No gameplay initialization, runtime scenery loading or FPS claim. Hierarchy expands four levels; object counts include all descendants.";
        }

        [Serializable] sealed class Shot
        {
            public string stage, view, scene, graphics;
            public bool isPlaying, monochrome;
            public Vector3 camera, focus;
            public float fov;
            public int vram;
            public string note = "Actual Unity URP Edit Mode render. Village/Overview/Silhouette use the same world camera coordinates as WorldVisualTests. No performance benchmark.";
        }

        [MenuItem("Orbis/Field/Capture Edit Mode Evidence", priority = 24)]
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Field evidence must be captured outside Play Mode.");
            FieldSceneAuthoring.OpenField();
            var scene = SceneManager.GetActiveScene();
            var field = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<FieldAuthoring>(true)).SingleOrDefault();
            if (field == null || scene.path != FieldAuthoring.ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open exactly one saved Field authoring scene before capturing evidence.");
            Directory.CreateDirectory(DirectoryPath);
            string stage = Argument("-worldStep") ?? "Field_EditMode";
            if (!stage.All(c => char.IsLetterOrDigit(c) || c == '_'))
                throw new ArgumentException("Evidence stage accepts letters, digits and underscore only.");
            var report = Inspect(scene, field);
            File.WriteAllText(Path.Combine(DirectoryPath, stage + "_Hierarchy.json"), JsonUtility.ToJson(report, true));
            if (report.issues.Length != 0)
                throw new InvalidOperationException("Saved Field validation failed: " + string.Join("; ", report.issues));
            foreach (var wind in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<WorldWind>(true)))
                wind.RefreshNow();
            Render(scene, field, stage, "Village", new Vector3(2, 28, -227), new Vector3(-120, 34, -225), 58);
            Render(scene, field, stage, "Overview", new Vector3(-1150, 1250, -1360), new Vector3(0, 45, 0), 59);
            Render(scene, field, stage, "Silhouette", new Vector3(0, 2500, 0), Vector3.zero, 48, true);
            Debug.Log($"ORBIS Field Edit Mode evidence: {report.objects} saved objects, {report.terrainCount} terrains, {report.rendererCount} renderers, {report.regions.Length} editable regions; {DirectoryPath}/{stage}_Hierarchy.json");
        }

        static Report Inspect(Scene scene, FieldAuthoring field)
        {
            var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray();
            var report = new Report
            {
                scene = scene.path, capturedUtc = DateTime.UtcNow.ToString("O"), graphics = SystemInfo.graphicsDeviceName,
                isPlaying = Application.isPlaying, gameEntryHasStarted = field.Entry != null && field.Entry.HasStarted,
                loadedScenes = SceneManager.sceneCount, sceneWasDirty = scene.isDirty, objects = objects.Length,
                terrainCount = objects.Sum(go => go.GetComponents<Terrain>().Length),
                rendererCount = objects.Sum(go => go.GetComponents<Renderer>().Length),
                colliderCount = objects.Sum(go => go.GetComponents<Collider>().Length),
                lodGroupCount = objects.Sum(go => go.GetComponents<LODGroup>().Length),
                hierarchy = scene.GetRootGameObjects().Select(root => Tree(root.transform, 0)).ToArray()
            };
            var issues = new List<string>();
            if (report.isPlaying || report.gameEntryHasStarted) issues.Add("Gameplay initialized before Edit Mode capture.");
            if (field.Entry == null || field.Entry.gameObject.scene != scene) issues.Add("Field entry reference is missing or belongs to another scene.");
            if (field.Regions == null || field.Regions.Length != 5 || field.Regions.Any(region => region == null || region.Root == null || region.Root.scene != scene))
                issues.Add("All five editable environment roots must be saved in Field.");
            else if (field.Regions.Select(region => region.Id).Distinct().Count() != 5 || field.Regions.Select(region => region.Root).Distinct().Count() != 5)
                issues.Add("Field region IDs and roots must be unique.");
            if (report.terrainCount != 4) issues.Add("Expected four saved terrain tiles.");
            if (report.rendererCount < 1000) issues.Add("Field does not contain the expected saved visual scenery.");
            foreach (var go in objects)
            {
                report.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                foreach (var filter in go.GetComponents<MeshFilter>())
                    if (filter.sharedMesh == null) report.missingMeshes++;
                foreach (var renderer in go.GetComponents<Renderer>())
                    foreach (var material in renderer.sharedMaterials)
                        if (material == null || material.shader == null) report.missingMaterials++;
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    using (var serialized = new SerializedObject(component))
                    {
                        var property = serialized.GetIterator();
                        while (property.Next(true))
                        {
                            if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != null || property.objectReferenceEntityIdValue == EntityId.None) continue;
                            report.missingObjectReferences++;
                            if (issues.Count < 20) issues.Add($"Missing reference: {PathOf(go.transform)} :: {component.GetType().Name}.{property.propertyPath}");
                        }
                    }
                }
            }
            if (report.missingScripts != 0) issues.Add($"Missing scripts: {report.missingScripts}.");
            if (report.missingMeshes != 0) issues.Add($"Missing meshes: {report.missingMeshes}.");
            if (report.missingMaterials != 0) issues.Add($"Missing material/shader slots: {report.missingMaterials}.");
            report.regions = (field.Regions ?? Array.Empty<FieldAuthoringRegion>()).Where(region => region != null && region.Root != null).Select(region =>
            {
                var grass = region.Root.GetComponentsInChildren<WorldGrassField>(true);
                return new RegionEvidence
                {
                    id = region.Id.ToString(), root = PathOf(region.Root.transform),
                    objects = region.Root.GetComponentsInChildren<Transform>(true).Length,
                    renderers = region.Root.GetComponentsInChildren<Renderer>(true).Length,
                    colliders = region.Root.GetComponentsInChildren<Collider>(true).Length,
                    lodGroups = region.Root.GetComponentsInChildren<LODGroup>(true).Length, grassFields = grass.Length,
                    grassInstances = grass.Sum(item => item.Data == null ? 0L : item.Data.Cells.Sum(cell => (long)cell.Instances.Length))
                };
            }).ToArray();
            report.issues = issues.ToArray();
            return report;
        }

        static HierarchyNode Tree(Transform transform, int depth)
        {
            return new HierarchyNode
            {
                name = transform.name, active = transform.gameObject.activeSelf, childCount = transform.childCount,
                components = transform.GetComponents<Component>().Select(component => component == null ? "MISSING SCRIPT" : component.GetType().Name).ToArray(),
                children = depth < 3 ? transform.Cast<Transform>().Select(child => Tree(child, depth + 1)).ToArray() : Array.Empty<HierarchyNode>()
            };
        }

        static string PathOf(Transform transform)
        {
            var path = new Stack<string>();
            while (transform != null) { path.Push(transform.name); transform = transform.parent; }
            return string.Join("/", path);
        }

        static void Render(Scene scene, FieldAuthoring field, string stage, string name, Vector3 position, Vector3 focus, float fov, bool monochrome = false)
        {
            var cameraObject = new GameObject("Temporary Field evidence camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            var originalCamera = field.Entry != null ? field.Entry.OverviewCamera : Camera.main;
            if (originalCamera != null) camera.CopyFrom(originalCamera);
            camera.enabled = false; camera.fieldOfView = fov; camera.farClipPlane = 6000;
            // Authoring transforms invalidate the old baked visibility data. The source capture shows actual saved geometry.
            camera.useOcclusionCulling = false;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation((focus - position).normalized, monochrome ? Vector3.forward : Vector3.up));
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            Volume volume = null;
            VolumeProfile profile = null;
            if (monochrome)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>(); profile.hideFlags = HideFlags.HideAndDontSave;
                profile.Add<ColorAdjustments>(true).saturation.Override(-100);
                var volumeObject = new GameObject("Temporary Field monochrome volume") { hideFlags = HideFlags.HideAndDontSave };
                volumeObject.SetActive(false);
                volume = volumeObject.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 2000; volume.sharedProfile = profile;
                volumeObject.SetActive(true);
            }
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var resolved = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            VolumeStack previousStack = null;
            bool asynchronous = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                target.Create(); resolved.Create();
                if (!VolumeManager.instance.isInitialized)
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting); camera.UpdateVolumeStack();
                var cameraStack = camera.GetUniversalAdditionalCameraData().volumeStack;
                if (cameraStack == null) throw new InvalidOperationException("URP camera volume stack did not initialize.");
                if (monochrome && !Mathf.Approximately(cameraStack.GetComponent<ColorAdjustments>().saturation.value, -100))
                    throw new InvalidOperationException("Monochrome inspection volume did not apply.");
                previousStack = VolumeManager.instance.stack; VolumeManager.instance.stack = cameraStack;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                target.ResolveAntiAliasedSurface(resolved); RenderTexture.active = resolved;
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
                string file = Path.Combine(DirectoryPath, stage + "_" + name);
                File.WriteAllBytes(file + ".png", pixels.EncodeToPNG());
                File.WriteAllText(file + ".json", JsonUtility.ToJson(new Shot
                {
                    stage = stage, view = name, scene = scene.path, isPlaying = Application.isPlaying, camera = position,
                    focus = focus, fov = fov, monochrome = monochrome, graphics = SystemInfo.graphicsDeviceName, vram = SystemInfo.graphicsMemorySize
                }, true));
            }
            finally
            {
                RenderTexture.active = active;
                if (previousStack != null) VolumeManager.instance.stack = previousStack;
                ShaderUtil.allowAsyncCompilation = asynchronous;
                Object.DestroyImmediate(cameraObject);
                if (volume != null) Object.DestroyImmediate(volume.gameObject);
                if (profile != null) Object.DestroyImmediate(profile);
                Object.DestroyImmediate(pixels); target.Release(); resolved.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(resolved);
            }
        }

        static string Argument(string key)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++) if (arguments[i] == key) return arguments[i + 1];
            return null;
        }
    }
}
