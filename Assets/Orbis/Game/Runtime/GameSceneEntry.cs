using System;
using Orbis.Art;
using Orbis.M4;
using Orbis.M16;
using UnityEngine;

namespace Orbis.Game
{
    /// <summary>The saved world stays in place. Only its player/session systems start after selection.</summary>
    [DefaultExecutionOrder(-1500)]
    public sealed class GameSceneEntry : MonoBehaviour
    {
        public M4SceneBootstrap World;
        public Camera OverviewCamera;
        public Transform OverviewFocus;
        public bool HasStarted { get; private set; }
        public string LastError { get; private set; }
        private GameObject selectionObject;

        private void Start()
        {
            if (ExplorerJourney.Profile.GetExplorerSnapshot().choice != ExplorerChoice.Unselected && BeginWorld())
                return;
            // No implicit character selection. The existing two-step selection UI runs over the authored world.
            selectionObject = new GameObject("Choose Explorer");
            selectionObject.SetActive(false);
            selectionObject.transform.SetParent(transform, false);
            var screen = selectionObject.AddComponent<ExplorerSelectionScreen>();
            screen.UseSceneCamera = true;
            screen.JourneyStarter = BeginWorld;
            selectionObject.SetActive(true);
        }

        public bool BeginWorld()
        {
            if (HasStarted) return true;
            if (World == null || World.AuthoredRegion == null)
            {
                LastError = "게임 씬의 World / Authored Region 연결을 확인하세요.";
                return false;
            }
            if (!ExplorerJourney.PrepareWorldSession())
            {
                LastError = ExplorerJourney.LastError;
                return false;
            }
            bool overviewWasActive = OverviewCamera != null && OverviewCamera.gameObject.activeSelf;
            try
            {
                // M3 resolves Camera.main during initialization; the overview must not participate.
                if (OverviewCamera != null) OverviewCamera.gameObject.SetActive(false);
                World.Initialize();
                ArtScenePresentation.EnsureInstalled(World);
                // Island travel uses the current scene; the older authored arena retains its regional return route.
                M4RegionRouter.Instance.AgniaSceneOverride = World.IsIsland ? ExplorerJourney.IslandScene : ExplorerJourney.GameScene;
                if (selectionObject != null) selectionObject.SetActive(false);
                HasStarted = true;
                LastError = null;
                return true;
            }
            catch (Exception exception)
            {
                if (OverviewCamera != null) OverviewCamera.gameObject.SetActive(overviewWasActive);
                LastError = exception.Message;
                Debug.LogError("ORBIS game scene initialization failed: " + LastError, this);
                return false;
            }
        }

        private void OnDrawGizmos()
        {
            if (World == null || World.AuthoredRegion == null || World.AuthoredRegion.Spawn == null) return;
            // Editor-only navigation aid, never a second player or collision object.
            var spawn = World.AuthoredRegion.Spawn;
            Gizmos.color = new Color(.4f, 1f, .75f);
            Gizmos.DrawWireSphere(spawn.position + Vector3.up, .45f);
            Gizmos.DrawLine(spawn.position, spawn.position + Vector3.up * 1.8f);
        }
    }
}
