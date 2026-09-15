using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine.SceneManagement;

namespace Orbis.EditorSupport
{
    /// <summary>Enables real legacy SceneManager fixtures only for a PlayMode test run.</summary>
    [InitializeOnLoad]
    public sealed class FieldSceneTestRunScope : IErrorCallbacks
    {
        private static double recoveryAt = -1;

        static FieldSceneTestRunScope()
        {
            // Verified against the installed Unity Test Framework public callback API.
            TestRunnerApi.RegisterTestCallback(new FieldSceneTestRunScope(), 10000);
            EditorSceneManager.sceneSaved += SceneSaved;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            EditorApplication.update += RecoverAbortedRun;
            // Also cover a domain reload after an interrupted setup, before Play Mode began.
            recoveryAt = EditorApplication.timeSinceStartup + 5;
        }

        private static void SceneSaved(Scene scene)
        {
            // UTF saves Assets/InitTestScene<guid>.unity before entering Play Mode.
            if (FieldSceneBuildPolicy.IsRegressionTestSceneOpen())
                FieldSceneBuildPolicy.BeginRegressionTestScope();
        }

        private static void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode && FieldSceneBuildPolicy.IsRegressionTestSceneOpen())
                FieldSceneBuildPolicy.BeginRegressionTestScope();
            if (state == PlayModeStateChange.EnteredEditMode && FieldSceneBuildPolicy.IsRegressionTestScopeActive)
                recoveryAt = EditorApplication.timeSinceStartup + 5;
        }

        private static void RecoverAbortedRun()
        {
            // RunFinished normally restores immediately. A canceled run with no callback gets
            // five seconds for the TestRunner scene cleanup, then returns to the product list.
            if (recoveryAt < 0 || EditorApplication.timeSinceStartup < recoveryAt ||
                EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (FieldSceneBuildPolicy.IsRegressionTestSceneOpen()) return;
            recoveryAt = -1;
            FieldSceneBuildPolicy.EndRegressionTestScope();
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            if ((testsToRun.TestMode & TestMode.PlayMode) != 0)
                FieldSceneBuildPolicy.BeginRegressionTestScope();
        }

        public void RunFinished(ITestResultAdaptor result) => FieldSceneBuildPolicy.EndRegressionTestScope();
        public void OnError(string message) => FieldSceneBuildPolicy.EndRegressionTestScope();
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
    }
}
