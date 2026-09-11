using Orbis.M0;
using Orbis.M1;
using Orbis.M4;
using UnityEngine;

namespace Orbis.M16
{
    public sealed class ExplorerStatusHud : MonoBehaviour
    {
        M4SceneBootstrap scene;
        ExplorerController explorer;
        PlayerMotor motor;
        GUIStyle body;
        public void Configure(M4SceneBootstrap world, ExplorerController controller)
        { scene = world; explorer = controller; motor = controller.GetComponent<PlayerMotor>(); }

        void OnGUI()
        {
            if (scene == null || explorer == null || scene.TravelMenuOpen) return;
            if (body == null) body = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            if (scene.IsIsland)
            {
                GUI.Label(new Rect(Mathf.Max(16, Screen.width * .5f - 145), Screen.height - 103, 300, 22),
                    "선택 원소: " + ElementName(explorer.SelectedElement) + " · G " + explorer.CooldownRemaining.ToString("0.0") + "초", body);
                return;
            }
            // Unspecified debug HUD layout: centre panel above the world notices; no new art assets.
            float width = Mathf.Min(340f, Screen.width - 24f);
            float x = Screen.width >= 980 ? 410f : Mathf.Max(12f, Screen.width - width - 12f);
            GUILayout.BeginArea(new Rect(x, 190f, width, 175f), GUI.skin.box);
            GUILayout.Label(explorer.Definition.DisplayName + " · 고정 파티 1번", body);
            GUILayout.Label("여정의 검 · 일반공격: 물리\n선택 원소: " + ElementName(explorer.SelectedElement), body);
            GUILayout.Label("1: 주인공 / 2–4: 동료\nTab: 원소 전환 / G: 원소 스킬 / Q: 궁극기 연출", body);
            GUILayout.Label("스킬 대기 " + explorer.CooldownRemaining.ToString("0.0") + "초 · 상태 " + motor.StateName, body);
            if (explorer.IsCasting) GUILayout.Label("시전 원소: " + ElementName(explorer.CurrentCastElement), body);
            GUILayout.EndArea();
        }

        static string ElementName(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return "화";
                case ElementType.Water: return "수";
                case ElementType.Wind: return "풍";
                case ElementType.Rock: return "암";
                case ElementType.Lightning: return "뢰";
                default: return "-";
            }
        }
    }
}

