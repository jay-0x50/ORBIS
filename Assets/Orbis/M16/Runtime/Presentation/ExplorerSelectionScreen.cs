using System;
using Orbis.M1;
using UnityEngine;

namespace Orbis.M16
{
    /// <summary>Explicit selection/continue screen. Reading this scene never selects a character or loads the world.</summary>
    public sealed class ExplorerSelectionScreen : MonoBehaviour
    {
        private ExplorerChoice candidate = ExplorerChoice.Unselected;
        private string notice = "함께 여정을 시작할 탐구자를 선택하세요.";
        private bool ready, journeyStarting;
        private Vector2 scroll;
        private GUIStyle heading, body, small, cardTitle;
        public ExplorerCatalog Catalog { get; private set; }
        public ExplorerChoice Choice => ExplorerJourney.Profile.GetExplorerSnapshot().choice;
        public ExplorerChoice Candidate => candidate;
        public string Notice => notice;
        public bool UseSceneCamera { get; set; }
        public Func<bool> JourneyStarter { get; set; }

        private void Awake()
        {
            if (!UseSceneCamera)
            {
            var cameraObject = new GameObject("Explorer Selection Camera");
            cameraObject.transform.SetParent(transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .09f, .13f);
            camera.cullingMask = 0; camera.orthographic = true;
            }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Catalog = Resources.Load<ExplorerCatalog>("M16/Catalog");
            if (Catalog == null)
            {
                notice = "탐구자 데이터가 없습니다. Orbis > M1.6 > Setup and Validate를 실행하세요.";
                return;
            }
            try
            {
                Catalog.Validate();
                ExplorerChoice saved = Choice; // Read only. No Select or BeginJourney call is made from Awake.
                if (saved != ExplorerChoice.Unselected)
                    notice = Catalog.Get(saved).DisplayName + "의 여정이 저장되어 있습니다. 이어하기를 눌러 시작하세요.";
                if (ExplorerJourney.Profile.IsReadOnly)
                    notice = "저장 파일을 읽기 전용으로 열었습니다. " + ExplorerJourney.Profile.LoadMessage;
                ready = true;
            }
            catch (Exception exception) { notice = "탐구자 선택 초기화 실패: " + exception.Message; }
        }

        public bool Select(ExplorerChoice choice)
        {
            if (!ready || journeyStarting) return false;
            if (!ExplorerJourney.Select(choice))
            {
                notice = ExplorerJourney.LastError; return false;
            }
            candidate = choice;
            notice = Catalog.Get(Choice).DisplayName + "로 확정했습니다. 여정 시작을 눌러 월드에 들어가세요.";
            return true;
        }

        public bool BeginJourney()
        {
            if (!ready || journeyStarting) return false;
            if (Choice == ExplorerChoice.Unselected)
            {
                notice = "먼저 탐구자를 선택하고 확정하세요."; return false;
            }
            if (!(JourneyStarter != null ? JourneyStarter() : ExplorerJourney.BeginJourney()))
            {
                notice = ExplorerJourney.LastError; return false;
            }
            journeyStarting = true;
            notice = "여정을 준비하고 있습니다.";
            return true;
        }

        private void OnGUI()
        {
            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, wordWrap = true };
                cardTitle = new GUIStyle(heading) { fontSize = 20 };
                body = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
                small = new GUIStyle(body) { fontSize = 13 };
            }
            float width = Mathf.Min(820f, Mathf.Max(260f, Screen.width - 32f));
            GUILayout.BeginArea(new Rect(Mathf.Max(8f, (Screen.width - width) * .5f), 18f, width, Mathf.Max(180f, Screen.height - 36f)), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("ORBIS / 탐구자의 여정", heading);
            GUILayout.Label(notice, body);
            if (!ready)
            {
                GUILayout.EndScrollView(); GUILayout.EndArea(); return;
            }
            ExplorerChoice saved = Choice;
            GUILayout.Space(12f);
            bool wide = width >= 620f;
            if (wide) GUILayout.BeginHorizontal();
            DrawCard(ExplorerChoice.Stella, "여성형", saved);
            DrawCard(ExplorerChoice.Polaris, "남성형", saved);
            if (wide) GUILayout.EndHorizontal();
            GUILayout.Space(10f);
            GUILayout.Label("두 탐구자는 각자의 외형과 이름을 사용하며, 기본 능력과 조작 방식, 장검은 공유합니다. 선택은 저장됩니다.", body);
            var weapon = Catalog.Get(ExplorerChoice.Stella).Weapon;
            GUILayout.Label(weapon.DisplayName + " / " + weapon.EnglishName + " · 장검 · 기본 공격력 " + weapon.BaseAttack.ToString("0.##"), body);
            GUILayout.Label("화 · 수 · 풍 · 암 · 뢰의 5원소를 자유 전환합니다. 다섯 원소 스킬은 같은 판정과 동작을 쓰며, " +
                Catalog.SharedSkillCooldown.ToString("0.##") + "초 재사용 시간을 공유합니다.", body);
            GUILayout.Label("월드 조작: Tab 원소 순환 · G 원소 스킬 · 왼쪽 클릭 기본 공격 · 1~4 파티 전환\n" +
                "1번 슬롯은 선택한 주인공입니다. 기본 공격은 물리 판정이며 원소를 바꾸거나 파티를 전환해도 스킬 재사용 시간은 유지됩니다.", small);
            GUILayout.Space(12f);
            bool previous = GUI.enabled;
            if (saved == ExplorerChoice.Unselected)
            {
                GUILayout.Label("선택 확정 후에는 이 저장에서 다른 탐구자로 변경할 수 없습니다.", small);
                GUI.enabled = candidate != ExplorerChoice.Unselected && !ExplorerJourney.Profile.IsReadOnly && !journeyStarting;
                string text = candidate == ExplorerChoice.Unselected ? "위에서 탐구자를 선택하세요" : Catalog.Get(candidate).DisplayName + "로 선택 확정";
                if (GUILayout.Button(text, GUILayout.MinHeight(42f))) Select(candidate);
            }
            else
            {
                GUILayout.Label("선택 완료: " + Catalog.Get(saved).DisplayName + " · 저장된 선택은 유지됩니다.", body);
                GUI.enabled = !journeyStarting;
                if (GUILayout.Button("여정 시작 / 이어하기", GUILayout.MinHeight(46f))) BeginJourney();
            }
            GUI.enabled = previous;
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        private void DrawCard(ExplorerChoice choice, string gender, ExplorerChoice saved)
        {
            var definition = Catalog.Get(choice);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(definition.DisplayName + " / " + (choice == ExplorerChoice.Stella ? "Stella" : "Polaris"), cardTitle);
            GUILayout.Label(gender + " · 별을 보고 길을 찾는 탐구자", body);
            GUILayout.Label("공유 무기: " + definition.Weapon.DisplayName + "\n각자의 외형 · 동일한 기본 능력", small);
            bool previous = GUI.enabled;
            GUI.enabled = saved == ExplorerChoice.Unselected && !ExplorerJourney.Profile.IsReadOnly && !journeyStarting;
            string state = saved == choice ? "확정됨" : candidate == choice ? "후보로 선택됨" : "이 탐구자 선택";
            if (GUILayout.Button(state, GUILayout.MinHeight(36f))) candidate = choice;
            GUI.enabled = previous;
            GUILayout.EndVertical();
        }
    }
}