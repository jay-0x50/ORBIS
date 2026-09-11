using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbis.M3
{
    /// <summary>Reuses completed gameplay; these three M3 scenes are presentation integration fixtures.</summary>
    [DefaultExecutionOrder(-1300)]
    public sealed class M3SceneBootstrap : MonoBehaviour
    {
        [SerializeField] private bool useRockFourthMember;
        [SerializeField] private bool useExplorationRegion;
        private M1SceneBootstrap core;
        private M0Input input;
        private PlayerMotor motor;
        private GUIStyle style;
        private GUIStyle title;
        private string status = "Ready";
        public M3Presentation Presentation { get; private set; }
        public PartyManager Party => core.Party;
        public ElementalReactionManager Manager => core.Manager;
        public ElementalActor PrimaryTarget => core.PrimaryTarget;
        public bool IsExploration => useExplorationRegion;

        private void Awake()
        {
            var world = new GameObject("M3 Existing Gameplay");
            world.transform.SetParent(transform, false);
            world.SetActive(false);
            if (useExplorationRegion) world.AddComponent<M2SceneBootstrap>();
            else
            {
                core = world.AddComponent<M1SceneBootstrap>();
                core.UseRockFourthMember = useRockFourthMember;
                core.ShowHud = false; core.EnableDebugKeys = true; core.ShowPlaceholderAuraTint = false;
            }
            world.SetActive(true);
            if (core == null) core = world.GetComponentInChildren<M1SceneBootstrap>();
            core.ShowPlaceholderAuraTint = false;
            motor = world.GetComponentInChildren<PlayerMotor>();
            input = motor.GetComponent<M0Input>();
            Presentation = gameObject.AddComponent<M3Presentation>();
            Presentation.Configure(core.Manager, core.Party, motor);
        }

        private void LateUpdate()
        {
            if (Presentation == null || Keyboard.current == null) return;
            var keyboard = Keyboard.current;
            if (Presentation.Ultimate.IsPlaying)
            {
                if (keyboard.escapeKey.wasPressedThisFrame || keyboard.rKey.wasPressedThisFrame)
                    Presentation.Ultimate.Cancel();
                return;
            }
            if (!input.GameplayEnabled) return;
            if (keyboard.qKey.wasPressedThisFrame)
            {
                status = "Ultimate presentation: " + Party.ActiveMember.Actor.Element;
                Presentation.PlayUltimatePresentation();
            }
            if (input.ResetPressed || (!useExplorationRegion && keyboard.f6Key.wasPressedThisFrame))
                Presentation.ClearTransient();
        }

        private void OnGUI()
        {
            if (Party == null || Presentation == null) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
                title = new GUIStyle(style) { fontSize = 18, fontStyle = FontStyle.Bold };
            }
            float width = Mathf.Min(400f, Screen.width - 24f);
            // M2's existing left HUD keeps traversal/puzzle instructions; this adapter occupies the right.
            float x = useExplorationRegion ? Mathf.Max(12f, Screen.width - width - 12f) : 12f;
            GUILayout.BeginArea(new Rect(x, 12f, width, Mathf.Min(400f, Screen.height - 24f)), GUI.skin.box);
            GUILayout.Label("ORBIS / M3 ELEMENT EFFECTS", title);
            GUILayout.Label("1 Ignis / Fire   2 Maris / Water\n3 Sparkle / Lightning   4 " +
                (useRockFourthMember ? "Grom / Rock" : "Aura / Wind"), style);
            GUILayout.Label("Left click: basic attack (3-hit combo)\nQ: ultimate presentation | Esc: cancel / cursor\nWASD / Shift / Space: move | R: reset", style);
            if (!useExplorationRegion)
            {
                GUILayout.Label("F6: reset targets | F7: Water hit on self\nF9: reset + Wind aura seed (Rock pairing)\nFire > Water: Vaporize | Water > Lightning: chain\nFire > Lightning: Overload | Fire > Wind: Swirl\nFire > Rock: Crystallize (Rock roster scene)", style);
            }
            GUILayout.Space(4f);
            GUILayout.Label("Active: " + Party.ActiveMember.Name + " / " + Party.ActiveMember.Actor.Element, style);
            GUILayout.Label("Reaction: " + Presentation.LastReaction + " | count " + Presentation.ReactionCount, style);
            GUILayout.Label("GPU pool: " + Presentation.Pool.ActiveCount + "/" + Presentation.Pool.Capacity +
                " | peak " + Presentation.Pool.PeakActiveCount + " | reused " + Presentation.Pool.RecycledWhileActive, style);
            GUILayout.Label("Ultimate: " + (Presentation.Ultimate.IsPlaying ? Presentation.Ultimate.CurrentStage.ToString() : "ready") +
                "\n" + status, style);
            GUILayout.Label("Q previews the five-stage presentation. Elemental skill / energy gameplay is not added.", style);
            GUILayout.EndArea();
        }
    }
}
