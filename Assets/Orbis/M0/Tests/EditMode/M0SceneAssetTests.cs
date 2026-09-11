using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Orbis.M0.Tests
{
    public sealed class M0SceneAssetTests
    {
        private const string ScenePath = "Assets/Orbis/M0/Scenes/M0_Prototype.unity";

        [Test]
        public void PrototypeSceneHasOneBootstrapAndNoMissingScripts()
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (openedForTest)
                    scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                Assert.That(scene.isLoaded, Is.True, "The delivered prototype scene must load.");
                int bootstrapCount = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    bootstrapCount += root.GetComponentsInChildren<M0SceneBootstrap>(true).Length;
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                        Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject),
                            Is.Zero, "Missing script on scene object: " + child.name);
                }
                Assert.That(bootstrapCount, Is.EqualTo(1), "The scene must create one playable prototype.");
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void RuntimeAssetsUseUrpAndAttackClipsMatchCombatTimings()
        {
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            Assert.That(pipeline, Is.Not.Null, "A render pipeline must be assigned before Play or Build.");
            Assert.That(pipeline.GetType().FullName,
                Is.EqualTo("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset"));

            foreach (string name in new[] { "Ground", "Platform", "PlayerBody", "PlayerAccent", "Skin", "Sword", "Dummy", "Marking" })
            {
                Material material = Resources.Load<Material>("M0/" + name);
                Assert.That(material, Is.Not.Null, "Missing runtime material: " + name);
                Assert.That(material.shader, Is.Not.Null, "Missing material shader: " + name);
                Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"), name);
            }

            RuntimeAnimatorController controller = Resources.Load<RuntimeAnimatorController>("M0/PlayerAnimator");
            Assert.That(controller, Is.Not.Null, "The bootstrap must resolve its Animator controller.");
            for (int step = 1; step <= ComboSequence.StepCount; step++)
            {
                AnimationClip attack = null;
                foreach (AnimationClip clip in controller.animationClips)
                    if (clip.name == "Attack" + step)
                        attack = clip;
                Assert.That(attack, Is.Not.Null, "Missing attack clip for step " + step);
                Assert.That(attack.length, Is.EqualTo(ComboSequence.GetTiming(step).Duration).Within(0.001f),
                    "Animation and combat clocks must agree for step " + step);
                Assert.That(attack.isLooping, Is.False, "An attack must finish once without looping.");
            }
        }
    }
}