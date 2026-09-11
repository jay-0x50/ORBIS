using Orbis.M1;
using Orbis.M3;
using Orbis.M4;
using UnityEngine;

namespace Orbis.Art
{
    /// <summary>Read-only HUD skin: Kenney panels, bars and icons; M4 remains the owner of every input/action.</summary>
    public sealed partial class ArtHud : MonoBehaviour
    {
        private M4SceneBootstrap scene;
        private ArtAssetCatalog catalog;
        private GUIStyle panel,label,small,title;
        private Vector2 hudScroll;
        public void Configure(M4SceneBootstrap owner,ArtAssetCatalog assets) {scene=owner;catalog=assets;scene.ShowHud=false;}
        private void OnGUI()
        {
            if(scene==null||catalog==null) return;
            if(panel==null)
            {
                panel=new GUIStyle(GUI.skin.box){border=new RectOffset(10,10,10,10)};
                panel.normal.background=catalog.Icon("panel");
                label=new GUIStyle(GUI.skin.label){fontSize=14,wordWrap=true}; label.normal.textColor=new Color(.95f,.95f,.94f);
                small=new GUIStyle(label){fontSize=12};
                title=new GUIStyle(label){fontSize=19,fontStyle=FontStyle.Bold};
            }
            if(scene.IsIsland && !islandDetails && !scene.TravelMenuOpen) { DrawIslandHud(); return; }
            float w=Screen.width,h=Screen.height;
            // Unspecified desktop reference size: keep all objectives readable in small docked Game views.
            // Scrolling preserves icon/text size instead of letting the party and quest panels overlap.
            bool scroll=w<980||h<680;
            if(scroll)
            {
                hudScroll=GUI.BeginScrollView(new Rect(0,0,w,h),hudScroll,new Rect(0,0,1280,720));
                w=1280;h=720;
            }
            Panel(new Rect(16,14,Mathf.Min(530,w-32),60));
            Icon("star",new Rect(29,29,30,30),M3Palette.Primary(scene.Party.ActiveMember.Actor.Element));
            Text(new Rect(72,22,420,29),M4RegionCatalog.Get(scene.Region).DisplayName,title);
            Text(new Rect(72,49,420,20),"루멘 "+scene.Progress.Coins+"    F10 지역 이동",small);
            Panel(new Rect(16,86,300,93));
            Bar(new Rect(30,111,271,16),scene.Vitals.Health,scene.Vitals.MaximumHealth,new Color(.92f,.27f,.28f));
            Text(new Rect(30,88,270,22),"HP  "+scene.Vitals.Health.ToString("0")+" / "+scene.Vitals.MaximumHealth.ToString("0"),small);
            Bar(new Rect(30,152,271,12),scene.Traversal.Stamina.Current,scene.Traversal.Stamina.Maximum,new Color(.32f,.86f,.56f));
            Text(new Rect(30,130,270,22),"스태미나  "+scene.Traversal.Stamina.Current.ToString("0")+"  ·  "+scene.Traversal.Mode,small);

            float partyY=Mathf.Max(190,h-238);
            for(int i=0;i<scene.Party.Members.Count;i++)
            {
                var member=scene.Party.Members[i]; bool active=i==scene.Party.ActiveIndex;
                Color element=M3Palette.Primary(member.Actor.Element);
                Rect card=new Rect(16+i*95,partyY,89,60); Panel(card,active?new Color(.33f,.4f,.5f):new Color(.12f,.16f,.22f));
                Icon(active?"check":"star",new Rect(card.x+8,card.y+7,22,22),element);
                Text(new Rect(card.x+38,card.y+5,40,22),(member.IsPermanent ? (i+1)+"★" : (i+1).ToString()),title);
                Text(new Rect(card.x+8,card.y+33,79,22),(member.IsPermanent ? member.Name : Name(member.Actor.Element)),small);
            }
            Panel(new Rect(16,partyY+70,380,148));
            Text(new Rect(29,partyY+80,354,38),"WASD 이동 · Shift 달리기 · Space 점프\n왼쪽 클릭 공격 · 1–4 전환 · Q 궁극기 연출",small);
            Text(new Rect(29,partyY+123,354,38),"E 등반 · 공중 Space 활공 · 물속 Ctrl/Space\nF 상호작용 · R 연습 초기화",small);
            Text(new Rect(29,partyY+164,354,42),"F1 시작 / F2 퍼즐 / F3 도전\nF4 보스 / F5 NPC / F6 조사",small);

            float right=Mathf.Max(420,w-322);
            if(w>=980)
            {
                Panel(new Rect(right,14,306,305));
                Icon("star",new Rect(right+14,29,22,22),new Color(1,.85f,.36f));
                Text(new Rect(right+48,24,240,28),"오늘의 위임",title);
                Text(new Rect(right+14,56,278,24),"NPC 앞 F로 수락 · 완료 후 F로 보상",small);
                int row=0;
                foreach(var quest in scene.Progress.DailyQuests)
                {
                    float y=88+row*53;
                    Icon(quest.State==M4QuestState.Completed||quest.State==M4QuestState.Claimed?"check":"star",new Rect(right+14,y,20,20),
                        quest.State==M4QuestState.Completed?new Color(.4f,1,.6f):Color.white);
                    Text(new Rect(right+43,y-3,250,46),M4RegionCatalog.Get(quest.Definition.Region).DisplayName+" · "+Objective(quest.Definition.Kind)+"\n"+Status(quest.State),small);
                    row++;
                }
                Panel(new Rect(right,330,306,127));
                Text(new Rect(right+14,341,278,44),"석상 I → II → III : "+Element(scene.Content.PuzzleElement)+" 원소\n진행 "+scene.Content.Puzzle.Model.LitCount+" / 3",label);
                Text(new Rect(right+14,392,278,54),"도전 : "+Reaction(scene.Content.Challenge.Model.RequiredReaction)+"\n"+scene.Content.Challenge.DefeatedCount+" / 3 · "+scene.Content.Challenge.RemainingTime.ToString("0")+"초 · "+scene.Content.Challenge.State,small);
            }
            else
            {
                // A narrow Game view still shows the current objective summary without hiding it.
                Panel(new Rect(16,190,300,130)); int row=0;
                foreach(var quest in scene.Progress.DailyQuests)
                    Text(new Rect(28,200+row++*28,274,28),M4RegionCatalog.Get(quest.Definition.Region).DisplayName+" · "+Objective(quest.Definition.Kind)+" · "+Status(quest.State),small);
            }
            if(Vector3.Distance(scene.Traversal.transform.position,scene.Layout.BossCenter)<13f)
            {
                float x=w*.5f-205;
                Panel(new Rect(x,87,410,89));
                Text(new Rect(x+14,97,382,23),"필드 수호자 · 약점 "+Element(scene.Boss.Weakness),label);
                Bar(new Rect(x+14,126,382,13),scene.Boss.HitPoints,scene.Boss.MaximumHitPoints,new Color(.9f,.51f,.25f));
                Text(new Rect(x+14,145,382,23),scene.Boss.State+" · 약점 적중 "+scene.Boss.WeakHitCount+" / 3",small);
            }
            if(w>=980)
            {
                Panel(new Rect(418,h-75,w-436,55));
                Text(new Rect(431,h-66,w-462,45),scene.Notice,small);
            }
            if(scene.Progress.IsReadOnly||!string.IsNullOrEmpty(scene.Progress.LastSaveError))
                Text(new Rect(20,180,w-40,55),scene.Progress.LoadMessage+" "+scene.Progress.LastSaveError,label);
            if(scene.TravelMenuOpen)
            {
                float x=w*.5f-220,y=h*.5f-180; Panel(new Rect(x,y,440,360),new Color(.13f,.18f,.25f));
                Text(new Rect(x+25,y+22,390,28),"지역 이동",title);
                Text(new Rect(x+25,y+61,390,24),"↑↓ 선택  ·  Enter 이동  ·  Esc 취소",small);
                for(int i=0;i<5;i++)
                {
                    if(scene.TravelSelection==i) Icon("arrow_right",new Rect(x+24,y+108+i*43,22,22),new Color(1,.85f,.35f));
                    Text(new Rect(x+60,y+104+i*43,340,30),M4RegionCatalog.Get((M4RegionId)i).DisplayName,label);
                }
            }
            if(M4RegionRouter.Instance.IsLoading) Text(new Rect(w*.5f-150,12,300,40),"지역 로딩 "+(M4RegionRouter.Instance.Progress*100).ToString("0")+"%",title);
            if(scroll)GUI.EndScrollView();
        }
        private void Panel(Rect rect,Color? tint=null)
        {
            Color old=GUI.color;GUI.color=tint??new Color(.12f,.16f,.22f,.96f);GUI.Box(rect,GUIContent.none,panel);GUI.color=old;
        }
        private void Icon(string key,Rect rect,Color tint)
        {
            var texture=catalog.Icon(key);if(texture==null)return;
            Color old=GUI.color;GUI.color=tint;GUI.DrawTexture(rect,texture,ScaleMode.ScaleToFit);GUI.color=old;
        }
        private void Bar(Rect rect,float value,float maximum,Color color)
        {
            var back=catalog.Icon("bar_back");
            if(back!=null){Color saved=GUI.color;GUI.color=new Color(.4f,.43f,.49f);GUI.DrawTexture(rect,back,ScaleMode.StretchToFill);GUI.color=saved;}
            var texture=catalog.Icon("bar_fill"); if(texture==null) return;
            float progress=Mathf.Clamp01(value/Mathf.Max(1,maximum));
            var fill=new Rect(rect.x,rect.y,rect.width*progress,rect.height);
            Color old=GUI.color;GUI.color=color;GUI.DrawTextureWithTexCoords(fill,texture,new Rect(0,0,progress,1));GUI.color=old;
        }
        private static void Text(Rect rect,string text,GUIStyle style)=>GUI.Label(rect,text,style);
        private static string Name(ElementType value)=>value==ElementType.Fire?"이그니스":value==ElementType.Water?"마리스":value==ElementType.Wind?"아우라":value==ElementType.Rock?"그롬":"스파클";
        private static string Element(ElementType value)=>value==ElementType.Fire?"화":value==ElementType.Water?"수":value==ElementType.Wind?"풍":value==ElementType.Rock?"암":"뢰";
        private static string Objective(M4ObjectiveKind value)=>value==M4ObjectiveKind.Survey?"조사":value==M4ObjectiveKind.FieldPuzzle?"퍼즐":value==M4ObjectiveKind.ChallengeRoom?"도전":"보스 기록";
        private static string Status(M4QuestState value)=>value==M4QuestState.Offered?"수락 대기":value==M4QuestState.Accepted?"진행 중":value==M4QuestState.Completed?"보상 수령 가능":"수령 완료";
        private static string Reaction(ReactionType value)=>value==ReactionType.Vaporize?"증발":value==ReactionType.ElectroCharged?"감전":value==ReactionType.Overload?"과부하":value==ReactionType.Swirl?"확산":"결정화";
        private void OnDisable(){if(scene!=null)scene.ShowHud=true;}
        private void OnEnable(){if(scene!=null)scene.ShowHud=false;}
    }
}
