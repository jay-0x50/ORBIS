using System;
using System.IO;
using System.Linq;
using Orbis.EditorSupport;
using Orbis.Game.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orbis.Game.Editor
{
    [InitializeOnLoad]
    public static class WorldWorkspaceMenu
    {
        const string OpenRequest = "Library/OrbisFieldOpenRequest.json";
        static double nextPoll;
        static WorldWorkspaceMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += PollExplicitOpenRequest;
        }

        [MenuItem("Orbis/Field/Open Field", priority = 1)]
        public static void Open() => FieldSceneAuthoring.OpenField();
        [MenuItem("Orbis/Field/Export Runtime Scenes", priority = 2)]
        public static void Export() => FieldSceneAuthoring.EnsureExported(true);
        [MenuItem("Orbis/Development/Legacy/Create Field from Existing Runtime World")]
        public static void Create() => FieldSceneAuthoring.CreateFromCurrentWorld();
        [MenuItem("Orbis/Development/Legacy/Restore Play from Current Scene")]
        public static void RestorePlayFromCurrentScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            EditorSceneManager.playModeStartScene = null;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode || FieldSceneAuthoring.IsExporting ||
                FieldSceneBuildPolicy.IsRegressionTestScopeActive || FieldSceneBuildPolicy.IsRegressionTestSceneOpen()) return;
            var source = SceneManager.GetSceneByPath(FieldAuthoring.ScenePath);
            if (!source.IsValid() || !source.isLoaded) return;
            try
            {
                FieldSceneAuthoring.EnsureExported();
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(IslandSceneBuilder.ScenePath);
            }
            catch (Exception error)
            {
                EditorApplication.isPlaying = false;
                Debug.LogException(error);
                Debug.LogError("Field export failed. Play was canceled; Field and unsaved edits are retained.");
            }
        }

        // Fixed, explicit editor handoff. No arbitrary command execution or scene-path input.
        static void PollExplicitOpenRequest()
        {
            if (Application.isBatchMode || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 2;
            if (!File.Exists(OpenRequest) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.ReadAllText(OpenRequest).Trim() != "open-field") return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    File.WriteAllText("Library/OrbisFieldOpenResult.json", "{\"status\":\"waiting-for-unsaved-scene\"}");
                    return; // Preserve all unsaved user work.
                }
            try
            {
                Open();
                var scene = SceneManager.GetSceneByPath(FieldAuthoring.ScenePath);
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
                File.WriteAllText("Library/OrbisFieldOpenResult.json", JsonUtility.ToJson(new OpenResult {
                    scene = scene.path, isPlaying = EditorApplication.isPlaying, loadedScenes = SceneManager.sceneCount,
                    objects = all.Length, terrains = all.Count(t => t.GetComponent<Terrain>() != null),
                    renderers = all.Count(t => t.GetComponent<Renderer>() != null),
                    roots = scene.GetRootGameObjects().Select(g => g.name).ToArray()
                }, true));
                File.Delete(OpenRequest);
            }
            catch (Exception error)
            {
                File.WriteAllText("Library/OrbisFieldOpenResult.json", error.ToString());
                File.Delete(OpenRequest);
                Debug.LogException(error);
            }
        }

        [Serializable] sealed class OpenResult
        {
            public string scene;
            public bool isPlaying;
            public int loadedScenes, objects, terrains, renderers;
            public string[] roots;
        }
    }
}

