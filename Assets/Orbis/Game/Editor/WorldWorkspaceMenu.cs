using System;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Orbis.Game.Editor
{
    public static class WorldWorkspaceMenu
    {
        [MenuItem("Orbis/World/Restore Play from Current Scene")]
        public static void RestorePlayFromCurrentScene()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before changing its start scene.");
            // The full-continent workspace pins its core scene. Release that editor-only preference
            // explicitly when returning to existing M0–M4 demos; no scene or player asset is changed.
            EditorSceneManager.playModeStartScene=null;
        }

        [MenuItem("Orbis/World/Open Full Continent for Editing")]
        public static void Open()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before opening the authored environment workspace.");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath,OpenSceneMode.Single);
            foreach(M4RegionId id in Enum.GetValues(typeof(M4RegionId)))
                EditorSceneManager.OpenScene(WorldStreamingBuilder.ScenePath(id),OpenSceneMode.Additive);
            // Runtime owns Addressables handles. Start from the core scene so editor-preview scenery is not duplicated.
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(IslandSceneBuilder.ScenePath);
        }
    }
}
