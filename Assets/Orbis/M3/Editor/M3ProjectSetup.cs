using System;
using System.Collections.Generic;
using System.IO;
using Orbis.M2.Editor;
using Orbis.EditorSupport;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orbis.M3.Editor
{
    public static class M3ProjectSetup
    {
        public const string WindScene = "Assets/Orbis/M3/Scenes/M3_EffectsPrototype.unity";
        public const string RockScene = "Assets/Orbis/M3/Scenes/M3_CrystallizeEffects.unity";
        public const string ExplorationScene = "Assets/Orbis/M3/Scenes/M3_ExplorationEffects.unity";

        [MenuItem("Orbis/Development/Legacy/M3/Setup and Validate")]
        public static void SetupAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            M2ProjectSetup.EnsureAssets();
            M3ShaderBuilder.Build();
            M3VfxBuilder.Build();
            M3TimelineBuilder.Build();
            BuildAudio();
            BuildScene(WindScene, false, false);
            BuildScene(RockScene, true, false);
            BuildScene(ExplorationScene, false, true);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (string path in new[] { WindScene, RockScene, ExplorationScene })
                if (!scenes.Exists(scene => scene.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            FieldSceneBuildPolicy.ApplyProductLayout();
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Validate();
            Debug.Log("ORBIS M3 setup and validation passed. Open Orbis > Development > Legacy > M3 > Open Effects Prototype.");
        }

        public static void Validate()
        {
            M3ShaderBuilder.Validate(); M3VfxBuilder.Validate(); M3TimelineBuilder.Validate();
            if (AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Orbis/M3/Resources/M3/Audio/ElementImpact.wav") == null)
                throw new BuildFailedException("M3 original impact audio is missing.");
            foreach (string path in new[] { WindScene, RockScene, ExplorationScene })
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                    throw new BuildFailedException("Missing M3 scene: " + path);
        }

        [MenuItem("Orbis/Development/Legacy/M3/Open Effects Prototype")]
        public static void OpenWind() => Open(WindScene);
        [MenuItem("Orbis/Development/Legacy/M3/Open Crystallize Effects")]
        public static void OpenRock() => Open(RockScene);
        [MenuItem("Orbis/Development/Legacy/M3/Open Exploration Effects")]
        public static void OpenExploration() => Open(ExplorationScene);

        private static void Open(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) SetupAndValidate();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static void BuildScene(string path, bool rock, bool exploration)
        {
            // Missing-only serialization of the existing single-bootstrap scene preserves the open editor scene.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string source = File.ReadAllText("Assets/Orbis/M2/Scenes/M2_ExplorationPrototype.unity");
            string sourceGuid = AssetDatabase.AssetPathToGUID("Assets/Orbis/M2/Runtime/Presentation/M2SceneBootstrap.cs");
            string scriptGuid = AssetDatabase.AssetPathToGUID("Assets/Orbis/M3/Runtime/Presentation/M3SceneBootstrap.cs");
            if (string.IsNullOrEmpty(scriptGuid)) throw new InvalidOperationException("M3 bootstrap script has not imported.");
            source = source.Replace(sourceGuid, scriptGuid)
                .Replace("ORBIS M2 Exploration Prototype", "ORBIS M3 " + (exploration ? "Exploration Effects" : rock ? "Crystallize Effects" : "Effects Prototype"))
                .Replace("Orbis.M2.Runtime::Orbis.M2.M2SceneBootstrap", "Orbis.M3.Runtime::Orbis.M3.M3SceneBootstrap")
                .Replace("--- !u!1660057539", "  useRockFourthMember: " + (rock ? "1" : "0") + "\n  useExplorationRegion: " + (exploration ? "1" : "0") + "\n--- !u!1660057539");
            File.WriteAllText(path, source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        private static void BuildAudio()
        {
            const string path = "Assets/Orbis/M3/Resources/M3/Audio/ElementImpact.wav";
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Original procedural sound: damped low thump + descending tone + seeded filtered noise.
            // Unspecified default .55 s mono PCM, 44.1 kHz; optional character voice is intentionally absent.
            const int rate = 44100;
            int count = Mathf.RoundToInt(rate * 0.55f);
            var random = new System.Random(304);
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                float filtered = 0f;
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)rate;
                    filtered = Mathf.Lerp(filtered, (float)random.NextDouble() * 2f - 1f, .55f);
                    float thump = Mathf.Sin(2f * Mathf.PI * (95f*t - 42f*t*t)) * Mathf.Exp(-t*16f);
                    float snap = filtered * Mathf.Exp(-t*30f);
                    float tone = Mathf.Sin(2f * Mathf.PI * (700f*t - 340f*t*t)) * Mathf.Exp(-t*12f);
                    float sample = (thump*.55f + snap*.3f + tone*.12f) * Mathf.Clamp01(t/.002f);
                    writer.Write((short)(Mathf.Clamp(sample,-1f,1f) * short.MaxValue));
                }
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
    }

    public sealed class M3BuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => 30;
        public void OnPreprocessBuild(BuildReport report) => M3ProjectSetup.Validate();
    }
}
