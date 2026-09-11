using System.Collections.Generic;
using System.IO;
using Orbis.M1.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Orbis.M2.Editor
{
    [InitializeOnLoad]
    public static class M2ProjectSetup
    {
        public const string ScenePath = "Assets/Orbis/M2/Scenes/M2_ExplorationPrototype.unity";
        private const string MaterialsPath = "Assets/Orbis/M2/Resources/M2";
        private static bool running;

        static M2ProjectSetup()
        {
            EditorApplication.delayCall += FirstImportSetup;
        }

        private static void FirstImportSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += FirstImportSetup;
                return;
            }
            EnsureAssets();
        }

        // Batch mode: -executeMethod Orbis.M2.Editor.M2ProjectSetup.EnsureAssets
        public static void EnsureAssets()
        {
            if (running || EditorApplication.isPlayingOrWillChangePlaymode) return;
            running = true;
            try
            {
                M1ProjectSetup.EnsureAssets();
                EnsureFolder(MaterialsPath);
                EnsureMaterial("Ground", new Color(0.25f, 0.31f, 0.27f));
                EnsureMaterial("Cliff", new Color(0.38f, 0.41f, 0.43f));
                EnsureMaterial("Sand", new Color(0.6f, 0.55f, 0.38f));
                EnsureMaterial("Stone", new Color(0.22f, 0.25f, 0.28f));
                EnsureMaterial("Highlight", new Color(0.9f, 0.74f, 0.34f));
                EnsureMaterial("Chest", new Color(0.4f, 0.25f, 0.12f));
                EnsureMaterial("Metal", new Color(0.12f, 0.16f, 0.2f));
                EnsureMaterial("Water", new Color(0.1f, 0.45f, 0.67f, 0.58f), true);
                var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null && !scenes.Exists(scene => scene.path == ScenePath))
                {
                    scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                AssetDatabase.SaveAssets();
            }
            finally { running = false; }
        }

        [MenuItem("Orbis/M2/Setup and Validate")]
        public static void SetupAndValidate()
        {
            EnsureAssets();
            ValidateAssets();
            Debug.Log("ORBIS M2 ready. Open Orbis > M2 > Open Exploration Prototype, then press Play.");
        }

        [MenuItem("Orbis/M2/Open Exploration Prototype")]
        public static void OpenExplorationPrototype()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureAssets();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount == 1)
            {
                SceneManager.SetActiveScene(scene);
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        public static void ValidateAssets()
        {
            M1ProjectSetup.ValidateAssets();
            foreach (string name in new[] { "Ground", "Cliff", "Sand", "Stone", "Highlight", "Chest", "Metal", "Water" })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/" + name + ".mat");
                if (material == null || material.shader == null)
                    throw new BuildFailedException("M2 material or shader is missing: " + name);
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                throw new BuildFailedException("M2 scene is missing: " + ScenePath);
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                int count = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    count += root.GetComponentsInChildren<M2SceneBootstrap>(true).Length;
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                            throw new BuildFailedException("M2 scene has a missing script: " + child.name);
                }
                if (count != 1) throw new BuildFailedException("M2 scene must contain exactly one bootstrap.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (openedHere && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void EnsureMaterial(string name, Color color, bool transparent = false)
        {
            string path = MaterialsPath + "/" + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new BuildFailedException("URP/Lit has not imported.");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Smoothness", transparent ? 0.7f : 0.2f);
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetShaderPassEnabled("ShadowCaster", false);
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(material, path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    public sealed class M2BuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => 2;
        public void OnPreprocessBuild(BuildReport report)
        {
            M2ProjectSetup.EnsureAssets();
            M2ProjectSetup.ValidateAssets();
        }
    }
}