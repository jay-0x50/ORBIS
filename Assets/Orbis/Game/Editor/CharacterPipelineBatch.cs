#if UNITY_EDITOR
using System;
using Orbis.Game.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orbis.Game.Editor
{
    /// <summary>Batch orchestration only. Keep opening Field and its dependent operation in one process.</summary>
    public static class CharacterPipelineBatch
    {
        static void OpenSavedField()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Use an idle batch Editor; interactive editing uses the existing Field menu.");
            // Do not save or silently discard dirty user scenes. A fresh batch session has a clean
            // untitled startup scene; leaving it open prevents Unity's additive scene creation.
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Preserve the dirty scene before a batch Field operation.");
            WorldWorkspaceMenu.Open();
            var field = SceneManager.GetSceneByPath(FieldAuthoring.ScenePath);
            if (!field.IsValid() || !field.isLoaded || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Field must be the only open scene in this batch process.");
        }

        public static void OpenAndExportField()
        {
            OpenSavedField();
            WorldWorkspaceMenu.Export();
        }

        public static void OpenAndBakeOcclusion()
        {
            OpenSavedField();
            WorldPerformanceBuilder.BakeOcclusion(); // Asynchronous: do not pass -quit.
        }

        public static void OpenAndBuildWindows()
        {
            OpenSavedField();
            WorldPerformanceBuilder.BuildWindows();
        }
    }
}
#endif
