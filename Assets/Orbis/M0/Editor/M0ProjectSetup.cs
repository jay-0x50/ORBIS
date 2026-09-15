using System;
using System.Collections.Generic;
using System.IO;
using Orbis.EditorSupport;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Orbis.M0.Editor
{
    /// <summary>
    /// Creates only missing M0 assets on first import. Existing materials, clips and
    /// controllers remain editable and are never regenerated over the user's tuning.
    /// </summary>
    [InitializeOnLoad]
    public static class M0ProjectSetup
    {
        public const string ScenePath = "Assets/Orbis/M0/Scenes/M0_Prototype.unity";
        private const string ResourcePath = "Assets/Orbis/M0/Resources/M0";
        private const string SettingsPath = "Assets/Orbis/M0/Settings";
        private const string PipelinePath = SettingsPath + "/M0_URP.asset";
        private const string RendererPath = SettingsPath + "/M0_Renderer.asset";
        private const string ControllerPath = ResourcePath + "/PlayerAnimator.controller";
        private static bool running;

        private static readonly string[] BonePaths =
        {
            "Torso", "Head", "LeftArmPivot", "RightArmPivot", "LeftLegPivot", "RightLegPivot"
        };

        private static readonly Vector3[] RestPositions =
        {
            new Vector3(0f, 1.15f, 0f), new Vector3(0f, 1.65f, 0f),
            new Vector3(-0.42f, 1.43f, 0f), new Vector3(0.42f, 1.43f, 0f),
            new Vector3(-0.18f, 0.88f, 0f), new Vector3(0.18f, 0.88f, 0f)
        };

        static M0ProjectSetup()
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

        // Also callable in batch mode: -executeMethod Orbis.M0.Editor.M0ProjectSetup.EnsureAssets
        public static void EnsureAssets()
        {
            if (running || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            running = true;
            try
            {
                EnsureFolder(ResourcePath);
                EnsureFolder(SettingsPath);
                EnsurePipeline();
                EnsureMaterial("Ground", new Color(0.19f, 0.29f, 0.31f));
                EnsureMaterial("Platform", new Color(0.31f, 0.43f, 0.47f));
                EnsureMaterial("PlayerBody", new Color(0.13f, 0.6f, 0.71f));
                EnsureMaterial("PlayerAccent", new Color(0.055f, 0.14f, 0.23f));
                EnsureMaterial("Skin", new Color(0.9f, 0.74f, 0.56f));
                EnsureMaterial("Sword", new Color(0.82f, 0.91f, 0.94f), 0.65f);
                EnsureMaterial("Dummy", new Color(0.88f, 0.45f, 0.17f));
                EnsureMaterial("Marking", new Color(0.6f, 0.75f, 0.69f));
                EnsureAnimation();
                EnsureBuildScene();
                FieldSceneBuildPolicy.ApplyProductLayout();
                AssetDatabase.SaveAssets();
            }
            finally
            {
                running = false;
            }
        }

        [MenuItem("Orbis/Development/Legacy/M0/Setup and Validate")]
        public static void SetupAndValidate()
        {
            EnsureAssets();
            ValidateAssets();
            Debug.Log("ORBIS M0 assets are ready. Open Orbis > Development > Legacy > M0 > Open Prototype, then press Play.");
        }

        [MenuItem("Orbis/Development/Legacy/M0/Open Prototype")]
        public static void OpenPrototype()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            EnsureAssets();
            Scene loaded = SceneManager.GetSceneByPath(ScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(loaded);
                return;
            }
            // Preserve any unsaved scene work without a confirmation dialog.
            bool hasDirtyScene = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                hasDirtyScene |= SceneManager.GetSceneAt(i).isDirty;
            loaded = EditorSceneManager.OpenScene(ScenePath,
                hasDirtyScene ? OpenSceneMode.Additive : OpenSceneMode.Single);
            SceneManager.SetActiveScene(loaded);
        }

        public static void ValidateAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                throw new BuildFailedException("M0 prototype scene is missing: " + ScenePath);
            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath) == null ||
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath) == null)
                throw new BuildFailedException("M0 URP assets are missing. Run Orbis > Development > Legacy > M0 > Setup and Validate.");
            if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset))
                throw new BuildFailedException("M0 requires a Universal Render Pipeline asset in Graphics Settings.");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null || controller.layers.Length == 0)
                throw new BuildFailedException("M0 PlayerAnimator is missing.");
            var found = new HashSet<string>();
            foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
                if (child.state.motion != null)
                    found.Add(child.state.name);
            foreach (string state in new[] { "Idle", "Walk", "Run", "Jump", "Attack1", "Attack2", "Attack3" })
                if (!found.Contains(state))
                    throw new BuildFailedException("M0 Animator is missing state or clip: " + state);
            foreach (string material in new[] { "Ground", "Platform", "PlayerBody", "PlayerAccent", "Skin", "Sword", "Dummy", "Marking" })
                if (AssetDatabase.LoadAssetAtPath<Material>(ResourcePath + "/" + material + ".mat") == null)
                    throw new BuildFailedException("M0 material is missing: " + material);
        }

        private static void EnsurePipeline()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "M0_Renderer";
                AssetDatabase.CreateAsset(renderer, RendererPath);
                ResourceReloader.ReloadAllNullIn(renderer, "Packages/com.unity.render-pipelines.universal");
                EditorUtility.SetDirty(renderer);
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "M0_URP";
                // Conservative prototype defaults; no post-processing or later-milestone VFX.
                pipeline.msaaSampleCount = 4;
                pipeline.shadowDistance = 45f;
                pipeline.supportsHDR = true;
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            if (GraphicsSettings.defaultRenderPipeline == null)
            {
                // Initial URP project setup uses linear lighting; preserve later project edits.
                PlayerSettings.colorSpace = ColorSpace.Linear;
                GraphicsSettings.defaultRenderPipeline = pipeline;
            }

            int originalQuality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline == null)
                    QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(originalQuality, false);
        }

        private static void EnsureMaterial(string name, Color color, float metallic = 0f)
        {
            string path = ResourcePath + "/" + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                return;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP/Lit shader has not imported. Wait for Package Manager, then run Orbis > Development > Legacy > M0 > Setup and Validate.");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", metallic > 0f ? 0.6f : 0.25f);
            material.SetFloat("_Metallic", metallic);
            AssetDatabase.CreateAsset(material, path);
        }

        private static void EnsureAnimation()
        {
            var clips = new List<AnimationClip>
            {
                CreateClip("Idle", 1.6f, true, new[]
                {
                    Pose(0f), Pose(0.5f, bob: 0.018f), Pose(1f)
                }),
                CreateClip("Walk", 0.8f, true, new[]
                {
                    Gait(0f, 25f, 0f), Gait(0.25f, 0f, 0.025f), Gait(0.5f, -25f, 0f),
                    Gait(0.75f, 0f, 0.025f), Gait(1f, 25f, 0f)
                }),
                CreateClip("Run", 0.5f, true, new[]
                {
                    Gait(0f, 43f, 0f, 10f), Gait(0.25f, 0f, 0.07f, 10f),
                    Gait(0.5f, -43f, 0f, 10f), Gait(0.75f, 0f, 0.07f, 10f), Gait(1f, 43f, 0f, 10f)
                }),
                CreateClip("Jump", 0.6f, false, new[]
                {
                    Pose(0f, leftArm: new Vector3(-35f, 0f, -16f), rightArm: new Vector3(-35f, 0f, 16f),
                        leftLeg: new Vector3(-20f, 0f, 0f), rightLeg: new Vector3(15f, 0f, 0f)),
                    Pose(1f, leftArm: new Vector3(-25f, 0f, -12f), rightArm: new Vector3(-25f, 0f, 12f),
                        leftLeg: new Vector3(-10f, 0f, 0f), rightLeg: new Vector3(10f, 0f, 0f))
                }),
                // Default timings match BasicAttackCombo: 0.50 / 0.55 / 0.65 seconds.
                // Generic keyframed clips are deliberately simple and use no root motion.
                CreateClip("Attack1", 0.5f, false, new[]
                {
                    Pose(0f),
                    Pose(0.23f, torso: new Vector3(0f, -22f, 0f), rightArm: new Vector3(-22f, -90f, -15f)),
                    Pose(0.5f, torso: new Vector3(0f, 25f, 0f), rightArm: new Vector3(-35f, 70f, 20f), leftArm: new Vector3(-20f, 0f, -10f)),
                    Pose(0.76f, rightArm: new Vector3(-15f, 25f, 10f)), Pose(1f)
                }),
                CreateClip("Attack2", 0.55f, false, new[]
                {
                    Pose(0f),
                    Pose(0.23f, torso: new Vector3(0f, 25f, 0f), rightArm: new Vector3(-40f, 75f, 25f)),
                    Pose(0.5f, torso: new Vector3(0f, -25f, 0f), rightArm: new Vector3(-25f, -85f, -25f), leftArm: new Vector3(-10f, 0f, -25f)),
                    Pose(0.8f, rightArm: new Vector3(-15f, -30f, -10f)), Pose(1f)
                }),
                CreateClip("Attack3", 0.65f, false, new[]
                {
                    Pose(0f),
                    Pose(0.25f, torso: new Vector3(-12f, 0f, 0f), rightArm: new Vector3(-155f, 0f, 5f), leftArm: new Vector3(-40f, 0f, -10f)),
                    Pose(0.52f, torso: new Vector3(20f, 0f, 0f), rightArm: new Vector3(-10f, 0f, -12f), leftArm: new Vector3(10f, 0f, -15f)),
                    Pose(0.8f, torso: new Vector3(8f, 0f, 0f), rightArm: new Vector3(-5f, 0f, 0f)), Pose(1f)
                })
            };

            // Existing controller edits are retained; create missing states and repair null
            // motions only, such as after a single generated clip has been removed.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            if (controller.layers.Length == 0)
                controller.AddLayer("Base Layer");
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            for (int i = 0; i < clips.Count; i++)
            {
                AnimationClip clip = clips[i];
                AnimatorState state = null;
                foreach (ChildAnimatorState child in machine.states)
                    if (child.state.name == clip.name)
                        state = child.state;
                if (state == null)
                {
                    state = machine.AddState(clip.name, new Vector3(220f * (i % 3), 90f * (i / 3), 0f));
                    state.writeDefaultValues = false;
                    state.motion = clip;
                }
                else if (state.motion == null)
                    state.motion = clip;
                if (clip.name == "Idle" && machine.defaultState == null)
                    machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
        }

        private static AnimationClip CreateClip(string clipName, float duration, bool loop, PoseFrame[] frames)
        {
            string path = ResourcePath + "/" + clipName + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip != null)
                return clip;
            clip = new AnimationClip { name = clipName, frameRate = 60f, legacy = false };
            for (int bone = 0; bone < BonePaths.Length; bone++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    var rotation = new AnimationCurve();
                    var position = new AnimationCurve();
                    foreach (PoseFrame frame in frames)
                    {
                        rotation.AddKey(frame.Time * duration, frame.Rotations[bone][axis]);
                        position.AddKey(frame.Time * duration, RestPositions[bone][axis] + (axis == 1 ? frame.Bob : 0f));
                    }
                    string suffix = axis == 0 ? ".x" : axis == 1 ? ".y" : ".z";
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(BonePaths[bone], typeof(Transform), "localEulerAnglesRaw" + suffix), rotation);
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(BonePaths[bone], typeof(Transform), "m_LocalPosition" + suffix), position);
                }
            }
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.loopBlend = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static PoseFrame Gait(float time, float stride, float bob, float lean = 0f)
        {
            return Pose(time, new Vector3(lean, 0f, 0f),
                leftArm: new Vector3(-stride * 0.8f, 0f, -4f),
                rightArm: new Vector3(stride * 0.65f - 10f, 0f, 4f),
                leftLeg: new Vector3(stride, 0f, 0f),
                rightLeg: new Vector3(-stride, 0f, 0f), bob: bob);
        }

        private static PoseFrame Pose(float time, Vector3 torso = default, Vector3 head = default,
            Vector3 leftArm = default, Vector3 rightArm = default, Vector3 leftLeg = default,
            Vector3 rightLeg = default, float bob = 0f)
        {
            return new PoseFrame
            {
                Time = time, Bob = bob,
                Rotations = new[] { torso, head, leftArm, rightArm, leftLeg, rightLeg }
            };
        }

        private sealed class PoseFrame
        {
            public float Time;
            public float Bob;
            public Vector3[] Rotations;
        }

        private static void EnsureBuildScene()
        {
            if (!File.Exists(ScenePath))
                return;
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(scene => scene.path == ScenePath))
                return;
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    public sealed class M0BuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            M0ProjectSetup.EnsureAssets();
            M0ProjectSetup.ValidateAssets();
        }
    }
}
