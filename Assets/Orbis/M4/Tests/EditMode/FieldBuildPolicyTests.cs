using System.Linq;
using NUnit.Framework;
using Orbis.EditorSupport;
using Orbis.M4.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;

namespace Orbis.M4.Tests
{
    public sealed class FieldBuildPolicyTests
    {
        [Test]
        public void RegressionSceneScopeRestoresProductBuildAndPlayStartScene()
        {
            Assert.That(FieldSceneBuildPolicy.IsProductMode, Is.True, "The editable Field must be delivered.");
            Assert.That(FieldSceneBuildPolicy.IsRegressionTestScopeActive, Is.False);
            var previousScenes = EditorBuildSettings.scenes;
            var previousStart = EditorSceneManager.playModeStartScene;
            var source = AssetDatabase.LoadAssetAtPath<SceneAsset>(FieldSceneBuildPolicy.FieldScenePath);
            Assert.That(source, Is.Not.Null);
            try
            {
                FieldSceneBuildPolicy.ApplyProductLayout();
                EditorSceneManager.playModeStartScene = source;
                FieldSceneBuildPolicy.BeginRegressionTestScope();
                Assert.That(FieldSceneBuildPolicy.IsRegressionTestScopeActive, Is.True);
                Assert.That(EditorSceneManager.playModeStartScene, Is.Null,
                    "The field must not replace the TestRunner bootstrap when entering Play Mode.");
                var fixtures = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
                Assert.That(fixtures[0], Is.EqualTo(FieldSceneBuildPolicy.RuntimeScenePath));
                foreach (string path in FieldSceneBuildPolicy.LegacyBuiltInScenes)
                    Assert.That(fixtures, Does.Contain(path));
                foreach (var region in M4RegionCatalog.All)
                    Assert.That(fixtures, Does.Not.Contain(M4ProjectSetup.ScenePath(region.Id)),
                        "Addressable region scenes must remain separate even during regression tests.");
                Assert.That(fixtures, Does.Not.Contain(FieldSceneBuildPolicy.FieldScenePath));
                Assert.DoesNotThrow(FieldSceneBuildPolicy.ValidateBuildLayout);

                // Duplicate begin is expected when both sceneSaved and RunStarted fire.
                FieldSceneBuildPolicy.BeginRegressionTestScope();
                FieldSceneBuildPolicy.EndRegressionTestScope();
                Assert.That(FieldSceneBuildPolicy.IsRegressionTestScopeActive, Is.False);
                Assert.That(EditorSceneManager.playModeStartScene, Is.SameAs(source));
                var product = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
                Assert.That(product, Is.EqualTo(new[] { FieldSceneBuildPolicy.RuntimeScenePath }));
                Assert.DoesNotThrow(FieldSceneBuildPolicy.ValidateBuildLayout);

                // An accidentally re-enabled old launcher must fail validation, not silently ship.
                var invalid = EditorBuildSettings.scenes;
                invalid.Single(scene => scene.path == M4ProjectSetup.LauncherScene).enabled = true;
                EditorBuildSettings.scenes = invalid;
                Assert.Throws<BuildFailedException>(FieldSceneBuildPolicy.ValidateBuildLayout);
            }
            finally
            {
                FieldSceneBuildPolicy.EndRegressionTestScope();
                EditorBuildSettings.scenes = previousScenes;
                EditorSceneManager.playModeStartScene = previousStart;
                FieldSceneBuildPolicy.ApplyProductLayout();
            }
        }
    }
}
