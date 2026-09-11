using System;
using System.Collections.Generic;
using Orbis.M0.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orbis.M1.Editor
{
    [InitializeOnLoad]
    public static class M1ProjectSetup
    {
        public const string PartyScenePath = "Assets/Orbis/M1/Scenes/M1_PartyPrototype.unity";
        public const string CrystallizeScenePath = "Assets/Orbis/M1/Scenes/M1_CrystallizePrototype.unity";
        private static bool running;

        static M1ProjectSetup()
        {
            EditorApplication.delayCall += FirstImportSetup;
        }

        private static void FirstImportSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += FirstImportSetup;
                return;
            }
            EnsureAssets();
        }

        // Batch mode: -executeMethod Orbis.M1.Editor.M1ProjectSetup.EnsureAssets
        public static void EnsureAssets()
        {
            if (running || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            running = true;
            try
            {
                M0ProjectSetup.EnsureAssets();
                var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                foreach (string path in new[] { PartyScenePath, CrystallizeScenePath })
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null && !scenes.Exists(scene => scene.path == path))
                        scenes.Add(new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                AssetDatabase.SaveAssets();
            }
            finally
            {
                running = false;
            }
        }

        [MenuItem("Orbis/M1/Setup and Validate")]
        public static void SetupAndValidate()
        {
            EnsureAssets();
            ValidateAssets();
            Debug.Log("ORBIS M1 ready. Use Orbis > M1 > Open Party Prototype or Open Crystallize Prototype, then Play.");
        }

        [MenuItem("Orbis/M1/Open Party Prototype")]
        public static void OpenPartyPrototype() => OpenPrototype(PartyScenePath);

        [MenuItem("Orbis/M1/Open Crystallize Prototype")]
        public static void OpenCrystallizePrototype() => OpenPrototype(CrystallizeScenePath);

        private static void OpenPrototype(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            EnsureAssets();
            Scene loaded = SceneManager.GetSceneByPath(path);
            if (loaded.IsValid() && loaded.isLoaded && SceneManager.sceneCount == 1)
            {
                SceneManager.SetActiveScene(loaded);
                return;
            }
            // Use Unity's normal unsaved-scene dialog only when the user invokes this menu.
            // Opening a single prototype prevents duplicate cameras and reaction managers.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            loaded = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            SceneManager.SetActiveScene(loaded);
        }

        public static void ValidateAssets()
        {
            M0ProjectSetup.ValidateAssets();
            if (!Application.unityVersion.StartsWith("6000.", StringComparison.Ordinal))
                throw new BuildFailedException("ORBIS M1 requires the configured Unity 6 editor.");
            if (Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine") == null)
                throw new BuildFailedException("M1 requires Cinemachine used by the M0 camera rig.");
            if (Type.GetType("UnityEngine.InputSystem.InputAction, Unity.InputSystem") == null)
                throw new BuildFailedException("M1 requires the Unity Input System.");
            ValidateScene(PartyScenePath, false);
            ValidateScene(CrystallizeScenePath, true);
        }

        private static void ValidateScene(string path, bool expectsRock)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                throw new BuildFailedException("M1 scene is missing: " + path);
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (openedHere)
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                int count = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                            throw new BuildFailedException("M1 scene has a missing script: " + child.name);
                    foreach (M1SceneBootstrap bootstrap in root.GetComponentsInChildren<M1SceneBootstrap>(true))
                    {
                        count++;
                        if (bootstrap.UsesRockFourthMember != expectsRock)
                            throw new BuildFailedException("M1 scene has the wrong slot 4 roster: " + path);
                    }
                }
                if (count != 1)
                    throw new BuildFailedException("M1 scene must contain exactly one bootstrap: " + path);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
                if (openedHere && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    public sealed class M1BuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => 1;
        public void OnPreprocessBuild(BuildReport report)
        {
            M1ProjectSetup.EnsureAssets();
            M1ProjectSetup.ValidateAssets();
        }
    }
}