using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orbis.EditorSupport
{
    /// <summary>The editable Field is source; its streamed Island export is the only player entry.</summary>
    [InitializeOnLoad]
    public static class FieldSceneBuildPolicy
    {
        public const string FieldScenePath = "Assets/Scenes/Field.unity";
        public const string RuntimeScenePath = "Assets/Orbis/Game/Scenes/Orbis_Island.unity";
        private const string RecoveryPath = "Library/OrbisFieldTestSceneScope.json";
        private static readonly int ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
        private static TestSnapshot activeSnapshot;

        // These are historical built-in test fixtures, not the five Addressable region scenes.
        public static readonly string[] LegacyBuiltInScenes =
        {
            "Assets/Orbis/M16/Scenes/M16_CharacterSelection.unity",
            "Assets/Orbis/M4/Scenes/M4_Launcher.unity",
            "Assets/Orbis/M0/Scenes/M0_Prototype.unity",
            "Assets/Orbis/M1/Scenes/M1_PartyPrototype.unity",
            "Assets/Orbis/M1/Scenes/M1_CrystallizePrototype.unity",
            "Assets/Orbis/M2/Scenes/M2_ExplorationPrototype.unity",
            "Assets/Orbis/M3/Scenes/M3_EffectsPrototype.unity",
            "Assets/Orbis/M3/Scenes/M3_CrystallizeEffects.unity",
            "Assets/Orbis/M3/Scenes/M3_ExplorationEffects.unity",
            "Assets/Orbis/M15/Scenes/M15_GachaDemo.unity",
            "Assets/Orbis/Game/Scenes/Orbis_OpenWorld.unity"
        };

        public static bool IsProductMode => File.Exists(FieldScenePath);
        public static bool IsRegressionTestScopeActive => activeSnapshot != null;

        static FieldSceneBuildPolicy()
        {
            // The Library marker survives domain reloads and lets the next Editor process
            // recover settings if a test crashes before RunFinished. Never ship this marker.
            if (File.Exists(RecoveryPath))
            {
                try { activeSnapshot = JsonUtility.FromJson<TestSnapshot>(File.ReadAllText(RecoveryPath)); }
                catch (Exception exception) { Debug.LogWarning("Field test recovery data could not be read: " + exception.Message); }
            }
            EditorApplication.delayCall += InitializeAfterImport;
        }

        private static void InitializeAfterImport()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += InitializeAfterImport;
                return;
            }
            if (activeSnapshot != null && activeSnapshot.processId != ProcessId)
                EndRegressionTestScope();
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !IsRegressionTestScopeActive)
                ApplyProductLayout();
        }

        public static void ApplyProductLayout()
        {
            if (!IsProductMode) return;
            if (!File.Exists(RuntimeScenePath))
                throw new BuildFailedException("Export Assets/Scenes/Field.unity before building the field.");
            var paths = new List<string> { RuntimeScenePath };
            paths.AddRange(EditorBuildSettings.scenes.Select(scene => scene.path));
            paths.AddRange(LegacyBuiltInScenes.Where(File.Exists));
            var next = paths.Where(path => !string.IsNullOrEmpty(path)).Distinct()
                .Select(path => new EditorBuildSettingsScene(path, path == RuntimeScenePath ||
                    (IsRegressionTestScopeActive && LegacyBuiltInScenes.Contains(path)))).ToArray();
            var previous = EditorBuildSettings.scenes;
            if (previous.Length != next.Length || previous.Where((scene, index) =>
                scene.path != next[index].path || scene.enabled != next[index].enabled).Any())
                EditorBuildSettings.scenes = next;
        }

        public static void ValidateBuildLayout()
        {
            if (!IsProductMode) return;
            var enabled = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (enabled.Length == 0 || enabled[0] != RuntimeScenePath)
                throw new BuildFailedException("The exported Island must be the first field player scene.");
            if (!IsRegressionTestScopeActive && enabled.Length != 1)
                throw new BuildFailedException("Only the exported Island is enabled in the normal field build. Legacy demos belong to tests.");
            if (enabled.Any(path => path != RuntimeScenePath &&
                (!IsRegressionTestScopeActive || !LegacyBuiltInScenes.Contains(path))))
                throw new BuildFailedException("A source/Addressable scene was accidentally included in the field player build.");
        }

        /// <summary>Early TestRunner bootstrap detection avoids a custom play start scene replacing its controller.</summary>
        public static bool IsRegressionTestSceneOpen()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !scene.path.StartsWith("Assets/InitTestScene", StringComparison.Ordinal)) continue;
                if (scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                    .Any(component => component != null && component.GetType().FullName ==
                        "UnityEngine.TestTools.TestRunner.PlaymodeTestsController")) return true;
            }
            return false;
        }

        // Called only by the Editor TestRunner bridge. Kept independent of any milestone assembly.
        public static void BeginRegressionTestScope()
        {
            if (!IsProductMode || IsRegressionTestScopeActive) return;
            ApplyProductLayout();
            activeSnapshot = new TestSnapshot
            {
                processId = ProcessId,
                startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                scenes = EditorBuildSettings.scenes.Select(scene => new SceneEntry { path = scene.path, enabled = scene.enabled }).ToArray()
            };
            File.WriteAllText(RecoveryPath, JsonUtility.ToJson(activeSnapshot, true));
            EditorSceneManager.playModeStartScene = null;
            ApplyProductLayout();
        }

        public static void EndRegressionTestScope()
        {
            if (activeSnapshot == null) return;
            TestSnapshot snapshot = activeSnapshot;
            activeSnapshot = null;
            if (snapshot.scenes != null)
                EditorBuildSettings.scenes = snapshot.scenes.Select(scene => new EditorBuildSettingsScene(scene.path, scene.enabled)).ToArray();
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(snapshot.startScene)
                ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(snapshot.startScene);
            ApplyProductLayout();
            if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath);
        }

        [Serializable] private sealed class SceneEntry { public string path; public bool enabled; }
        [Serializable] private sealed class TestSnapshot
        {
            public int processId;
            public string startScene;
            public SceneEntry[] scenes;
        }
    }
}
