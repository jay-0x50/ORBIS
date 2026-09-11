using Orbis.M4;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Game.World
{
    /// <summary>Presentation-only WindZone/pawn bridge. Never moves a controller or changes its gameplay state.</summary>
    [ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(950)]
    public sealed class WorldWind : MonoBehaviour
    {
        [SerializeField, Tooltip("The world's directional WindZone. Its main/turbulence/pulse values feed the editable foliage graph.")]
        WindZone zone;
        [SerializeField] CharacterController playerController;
        [SerializeField, Min(.1f), Tooltip("Art default: grass bends within 1.25 metres of the controlled pawn.")]
        float playerRadius = 1.25f;

        static readonly int DirectionId = Shader.PropertyToID("_OrbisWindDirection");
        static readonly int ParamsId = Shader.PropertyToID("_OrbisWindParams");
        static readonly int PlayerId = Shader.PropertyToID("_OrbisPlayerPosition");
        static readonly int RadiusId = Shader.PropertyToID("_OrbisPlayerRadius");
        static WorldWind owner;
        M4SceneBootstrap world;
        float nextLookup;

        public WindZone Zone { get => zone; set { zone = value; RefreshNow(); } }
        public CharacterController PlayerController { get => playerController; set { playerController = value; RefreshNow(); } }
        public float PlayerRadius { get => playerRadius; set { playerRadius = Mathf.Clamp(value, .1f, 3f); RefreshNow(); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOwner() => owner = null;

        public void Configure(WindZone source, CharacterController controller = null)
        {
            zone = source;
            playerController = controller;
            nextLookup = 0;
            RefreshNow();
        }

        void OnEnable()
        {
            owner = this;
            M4SceneBootstrap.PlayerReady += OnPlayerReady;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RefreshNow();
        }

        void OnPlayerReady(M4SceneBootstrap source)
        {
            // Additive scenery has no pawn; follow the one owned by the persistent gameplay bootstrap.
            if (source == null || source.gameObject.scene != gameObject.scene) return;
            world = source;
            playerController = source.GetComponentInChildren<CharacterController>();
            RefreshNow();
        }

        void LateUpdate() => RefreshNow();

        void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            // SingleCameraRequest/offscreen and edit-mode captures also need the current global values.
            RefreshNow();
        }

        public void RefreshNow()
        {
            if (!isActiveAndEnabled) return;
            if (owner == null || !owner.isActiveAndEnabled) owner = this;
            if (owner != this) return;
            if ((zone == null || playerController == null) && Time.realtimeSinceStartup >= nextLookup)
            {
                nextLookup = Time.realtimeSinceStartup + .5f; // Art-only lookup, not an every-frame scene scan.
                if (zone == null) zone = GetComponent<WindZone>();
                if (zone == null)
                    foreach (var candidate in FindObjectsByType<WindZone>())
                        if (candidate.gameObject.activeInHierarchy && candidate.mode == WindZoneMode.Directional)
                        { zone = candidate; break; }
                if (world == null) world = GetComponent<M4SceneBootstrap>() ?? FindAnyObjectByType<M4SceneBootstrap>();
                if (playerController == null && world != null)
                    playerController = world.GetComponentInChildren<CharacterController>();
            }

            Vector3 direction = Vector3.right;
            Vector4 parameters = Vector4.zero;
            // WindZone derives from Component, not Behaviour; active hierarchy is its available activation state.
            if (zone != null && zone.gameObject.activeInHierarchy && zone.mode == WindZoneMode.Directional)
            {
                direction = Vector3.ProjectOnPlane(zone.transform.forward, Vector3.up);
                if (direction.sqrMagnitude < .0001f) direction = Vector3.right;
                direction.Normalize();
                parameters = new Vector4(Mathf.Max(0, zone.windMain), Mathf.Max(0, zone.windTurbulence),
                    Mathf.Max(0, zone.windPulseMagnitude), Mathf.Max(0, zone.windPulseFrequency));
            }
            Shader.SetGlobalVector(DirectionId, direction);
            Shader.SetGlobalVector(ParamsId, parameters);
            bool hasPawn = playerController != null && playerController.enabled && playerController.gameObject.activeInHierarchy;
            Shader.SetGlobalVector(PlayerId, hasPawn ? playerController.transform.position : Vector3.zero);
            Shader.SetGlobalFloat(RadiusId, hasPawn ? Mathf.Clamp(playerRadius, .1f, 3f) : 0f);
        }

        void OnValidate()
        {
            playerRadius = Mathf.Clamp(playerRadius, .1f, 3f);
        }

        void OnDisable()
        {
            M4SceneBootstrap.PlayerReady -= OnPlayerReady;
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            if (owner != this) return;
            owner = null;
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            Shader.SetGlobalFloat(RadiusId, 0f);
        }
    }
}
