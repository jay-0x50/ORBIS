using System;
using System.Collections.Generic;
using Orbis.M1;
using Orbis.M4;
using UnityEngine;

namespace Orbis.M15
{
    /// <summary>Plain debug UI for the committed economy. It never rolls RNG or edits a save snapshot itself.</summary>
    public sealed class M15GachaDemo : MonoBehaviour
    {
        private int selectedIndex;
        private PullResult[] lastResults = Array.Empty<PullResult>();
        private Vector2 scroll;
        private GUIStyle heading, body, small, resultStyle;
        private string notice = "새 저장의 재화는 0입니다. 개발용 지급 버튼으로 교환과 모집을 시험하세요.";
        private string presentationCue;
        private float presentationUntil;
        private bool ready;
        public GachaCatalog Catalog { get; private set; }
        public CurrencyManager Manager { get; private set; }
        public GachaBanner SelectedBanner => Catalog != null && Catalog.Banners.Length > 0 ? Catalog.Banners[selectedIndex] : null;
        public IReadOnlyList<PullResult> LastResults => lastResults;
        public bool SkipPresentation { get; set; }
        public int PresentationRequestCount { get; private set; }
        public string Notice => notice;

        private void Awake()
        {
            Catalog = Resources.Load<GachaCatalog>("M15/Catalog");
            var cameraObject = new GameObject("M1.5 Demo Camera");
            cameraObject.transform.SetParent(transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .09f, .13f);
            camera.cullingMask = 0; camera.orthographic = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (Catalog == null)
            {
                notice = "카탈로그가 없습니다. Unity 메뉴 Orbis > M1.5 > Setup and Validate를 실행하세요.";
                return;
            }
            try
            {
                Catalog.Validate();
                Manager = CurrencyManager.Instance;
                Manager.Initialize(M4Session.Progress, Catalog);
                Manager.PresentationRequested += OnPresentationRequested;
                ready = true;
                if (Manager.IsReadOnly) notice = "저장 파일을 읽기 전용으로 열었습니다. " + M4Session.Progress.LoadMessage;
            }
            catch (Exception exception) { notice = "M1.5 초기화 실패: " + exception.Message; }
        }

        public bool SelectBanner(int index)
        {
            if (Catalog == null || index < 0 || index >= Catalog.Banners.Length) return false;
            selectedIndex = index; return true;
        }
        public bool PullSingle()
        {
            if (!ready || SelectedBanner == null) return false;
            if (!Manager.PullSingle(SelectedBanner, out PullResult result, SkipPresentation)) return Failed("1회 모집");
            lastResults = new[] { result };
            notice = "1회 모집 결과와 재화를 저장했습니다."; return true;
        }
        public bool PullTen()
        {
            if (!ready || SelectedBanner == null) return false;
            if (!Manager.PullTen(SelectedBanner, out PullResult[] results, SkipPresentation)) return Failed("10회 모집");
            lastResults = results;
            notice = "10회 모집 전체 결과와 재화를 저장했습니다."; return true;
        }
        public bool ExchangeTickets(int tickets)
        {
            if (!ready || SelectedBanner == null) return false;
            CurrencyType ticket = SelectedBanner.Type == BannerType.Limited ? CurrencyType.LimitedPledge : CurrencyType.StandardPledge;
            if (!Manager.Exchange(ticket, tickets)) return Failed("서약서 교환");
            notice = CurrencyName(ticket) + " " + tickets + "장으로 교환했습니다."; return true;
        }
        public bool GrantStarters()
        {
            if (!ready) return false;
            if (!Manager.GrantStarters()) return Failed("스타터 지급");
            notice = "스타터 지급을 확인했습니다. 같은 저장에 다시 지급하지 않습니다."; return true;
        }
        public bool Save()
        {
            if (!ready) return false;
            if (!Manager.Save()) return Failed("저장");
            notice = "현재 재화와 보유 현황을 저장했습니다."; return true;
        }
        public bool Reload()
        {
            if (!ready) return false;
            if (!Manager.Reload()) return Failed("다시 읽기");
            notice = "저장된 재화·보유 현황·배너별 천장을 다시 읽었습니다."; return true;
        }
        public bool DebugAdd(CurrencyType currency, int amount)
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
            {
                notice = "개발용 재화 지급은 Editor 또는 Development Build에서만 사용할 수 있습니다.";
                return false;
            }
            if (!ready) return false;
            if (!Manager.Add(currency, amount)) return Failed("개발용 재화 지급");
            notice = "개발용 지급: " + CurrencyName(currency) + " +" + amount; return true;
        }
        private bool Failed(string action)
        {
            notice = action + " 실패: " + Manager.LastError; return false;
        }
        private void OnPresentationRequested(CharacterRarity rarity)
        {
            PresentationRequestCount++;
            presentationCue = (int)rarity + "성 결과 표시 요청";
            // Unspecified M1.5 debug display time: .8 s. This is an event indicator, not new VFX/art.
            presentationUntil = Time.unscaledTime + .8f;
        }
        private void OnDestroy()
        {
            if (Manager != null) Manager.PresentationRequested -= OnPresentationRequested;
        }

        private void OnGUI()
        {
            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
                body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
                small = new GUIStyle(body) { fontSize = 12 };
                resultStyle = new GUIStyle(body) { fontStyle = FontStyle.Bold };
            }
            float width = Mathf.Max(260f, Screen.width - 32f);
            GUILayout.BeginArea(new Rect(16f, 12f, width, Mathf.Max(180f, Screen.height - 24f)), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("ORBIS / M1.5 재화 · 동료 모집", heading);
            GUILayout.Label(notice, body);
            if (!ready)
            {
                GUILayout.EndScrollView(); GUILayout.EndArea(); return;
            }
            // Snapshot is a clone. GetPity may add empty entries to this clone without touching the live save.
            EconomySaveData snapshot = Manager.Snapshot;
            DrawBalances(snapshot);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Catalog.Banners.Length; i++)
            {
                string prefix = selectedIndex == i ? "● " : "○ ";
                if (GUILayout.Button(prefix + Catalog.Banners[i].DisplayName, GUILayout.MinHeight(34))) SelectBanner(i);
            }
            GUILayout.EndHorizontal();
            DrawBanner(snapshot);
            DrawDeveloperControls(snapshot);
            GUILayout.Space(8);
            bool wide = Screen.width >= 1000;
            if (wide) GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUI.skin.box, wide ? new[] { GUILayout.Width(width * .48f) } : Array.Empty<GUILayoutOption>());
            DrawResults();
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUI.skin.box);
            DrawOwned(snapshot);
            GUILayout.EndVertical();
            if (wide) GUILayout.EndHorizontal();
            GUILayout.Label("저장: " + Manager.SavePath + " | revision " + snapshot.revision + (Manager.IsReadOnly ? " | READ ONLY" : ""), small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("저장")) Save();
            if (GUILayout.Button("저장 다시 읽기")) Reload();
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        private void DrawBalances(EconomySaveData snapshot)
        {
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < 3; column++)
                {
                    var currency = (CurrencyType)(row * 3 + column);
                    GUILayout.Label(CurrencyName(currency) + "  " + snapshot.GetBalance(currency), body, GUILayout.MinWidth(80));
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("오르빗 결정: 빛나는 별의 조각. 새로운 만남을 부른다.\n루멘: 세상을 이야기하는 기본 재화입니다.", small);
        }
        private void DrawBanner(EconomySaveData snapshot)
        {
            GachaBanner banner = SelectedBanner;
            BannerPityData pity = snapshot.GetPity(banner.Id);
            GachaRules rules = banner.Rules;
            GachaOdds odds = GachaEngine.NextOdds(banner, pity);
            GUILayout.Label(banner.Type == BannerType.Limited ?
                "한정 · 픽업 " + banner.Featured.DisplayName + " | 특별한 인연이 기다리고 있습니다." :
                "상시 · 여정의 동행 | 언제나, 새로운 만남.", body);
            GUILayout.Label("기본 확률  3성 " + Percent(rules.ThreeStarProbability) + " / 4성 " + Percent(rules.FourStarProbability) + " / 5성 " + Percent(rules.FiveStarProbability) +
                "\n다음 1회  3성 " + Percent(odds.Three) + " / 4성 " + Percent(odds.Four) + " / 5성 " + Percent(odds.Five), body);
            GUILayout.Label("5성 미당첨 " + pity.fiveStarMisses + "회 · 다음은 " + (pity.fiveStarMisses + 1) + "회차" +
                " | 4성 이상 미당첨 " + pity.fourStarMisses + "회\n" +
                rules.SoftPityStart + "회부터 5성 확률 +" + Percent(rules.SoftPityStep) + "p/회 · " + rules.HardPity + "회 5성 보장 · " + rules.FourStarHardPity + "회 내 4성 이상 보장", small);
            if (banner.Type == BannerType.Limited)
                GUILayout.Label("5성 당첨 시 픽업 확률 " + (pity.guaranteedFeatured ? "100% (직전 픽업 실패 보장)" : Percent(rules.FeaturedChance)) +
                    " · 픽업 외 5성은 나머지 수호자 4명", small);
            GUILayout.Label("배너별 천장은 독립 저장됩니다. 같은 등급의 후보 선택은 균등하며, 한정 5성은 픽업 판정을 먼저 합니다.", small);
            bool previous = GUI.enabled; GUI.enabled = !Manager.IsReadOnly;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("서약서 1장 교환 (결정 " + Catalog.Rules.ExchangeCost + ")", GUILayout.MinHeight(32))) ExchangeTickets(1);
            if (GUILayout.Button("서약서 10장 교환 (결정 " + ((long)Catalog.Rules.ExchangeCost * 10) + ")", GUILayout.MinHeight(32))) ExchangeTickets(10);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1회 모집", GUILayout.MinHeight(38))) PullSingle();
            if (GUILayout.Button("10회 모집", GUILayout.MinHeight(38))) PullTen();
            GUILayout.EndHorizontal(); GUI.enabled = previous;
            SkipPresentation = GUILayout.Toggle(SkipPresentation, "연출 스킵 (저장과 결과 표시는 동일)");
            GUILayout.Label("표시 요청 이벤트 " + PresentationRequestCount + "회" +
                (Time.unscaledTime < presentationUntil ? " · " + presentationCue : ""), small);
        }
        private void DrawResults()
        {
            GUILayout.Label("최근 성공한 모집 결과", heading);
            if (lastResults.Length == 0) GUILayout.Label("모집을 실행하면 결과와 NEW 표시가 여기에 남습니다.", body);
            for (int i = 0; i < lastResults.Length; i++)
            {
                PullResult result = lastResults[i];
                GUILayout.Label((i + 1) + ". " + (result.IsNew ? "NEW  " : "") + result.Character.DisplayName + "  " + (int)result.Rarity + "성 · " +
                    ElementName(result.Character.Element) + " · 보유 " + result.OwnedCopies + "장", resultStyle);
                if (result.IsNew) GUILayout.Label("당신의 여정에, 새로운 인연이 찾아왔습니다.", small);
            }
        }
        private void DrawOwned(EconomySaveData snapshot)
        {
            GUILayout.Label("동료 도감 / 전체 18명", heading);
            foreach (CharacterDefinition character in Catalog.Characters)
            {
                int copies = snapshot.CopiesOf(character.Id);
                GUILayout.Label((copies > 0 ? "보유 " + copies + "장" : "미보유") + " | " + character.DisplayName + " " + (int)character.Rarity + "성 · " +
                    ElementName(character.Element) + " · " + WeaponName(character.Weapon) + (character.IsStarter ? " · 스타터" : ""), body);
                GUILayout.Label(character.Role, small);
            }
        }
        private void DrawDeveloperControls(EconomySaveData snapshot)
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return;
            GUILayout.Space(10); GUILayout.Label("개발용 지급 / 저장 동작 점검", heading);
            GUILayout.Label("새 재화·동료는 자동 지급되지 않습니다. 아래 버튼은 이 로컬 저장에 시험 재화를 지급합니다.", small);
            bool previous = GUI.enabled; GUI.enabled = !Manager.IsReadOnly;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("결정 +1600")) DebugAdd(CurrencyType.OrbitShard, 1600);
            if (GUILayout.Button("루멘 +1000")) DebugAdd(CurrencyType.Lumen, 1000);
            if (GUILayout.Button("승급석 +10")) DebugAdd(CurrencyType.AscensionStone, 10);
            if (GUILayout.Button("도감 재료 +10")) DebugAdd(CurrencyType.CodexMaterial, 10);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("한정 서약서 +10")) DebugAdd(CurrencyType.LimitedPledge, 10);
            if (GUILayout.Button("표준 서약서 +10")) DebugAdd(CurrencyType.StandardPledge, 10);
            if (GUILayout.Button(snapshot.startersGranted ? "스타터 지급 완료 (재호출 점검)" : "튜토리얼 완료 모의: 스타터 3명 지급")) GrantStarters();
            GUILayout.EndHorizontal(); GUI.enabled = previous;

        }
        private static string Percent(double value) => (value * 100d).ToString("0.###") + "%";
        private static string CurrencyName(CurrencyType value)
        {
            switch (value)
            {
                case CurrencyType.Lumen: return "루멘";
                case CurrencyType.OrbitShard: return "오르빗 결정";
                case CurrencyType.LimitedPledge: return "인연의 서약서";
                case CurrencyType.StandardPledge: return "표준 서약서";
                case CurrencyType.AscensionStone: return "승급석";
                default: return "도감 재료";
            }
        }
        private static string ElementName(ElementType value) => value == ElementType.Fire ? "화" : value == ElementType.Water ? "수" :
            value == ElementType.Wind ? "풍" : value == ElementType.Rock ? "암" : "뢰";
        private static string WeaponName(WeaponType value)
        {
            switch (value)
            {
                case WeaponType.Longsword: return "장검";
                case WeaponType.Bow: return "활";
                case WeaponType.DualBlades: return "쌍검";
                case WeaponType.Greatsword: return "대검";
                case WeaponType.Spear: return "창";
                default: return "법구";
            }
        }
    }
}