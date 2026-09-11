using Orbis.M3;
using Orbis.M4;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbis.Art
{
    public sealed partial class ArtHud
    {
        bool islandDetails;
        Texture2D islandMap;
        void Update()
        {
            if(scene!=null&&scene.IsIsland&&Keyboard.current!=null&&Keyboard.current.hKey.wasPressedThisFrame)
                islandDetails=!islandDetails;
        }
        void DrawIslandHud()
        {
            float w=Screen.width,h=Screen.height;
            // The island uses compact information so the landscape remains visible. H opens the existing full task panel.
            float mapSize=Mathf.Min(190,w*.24f);
            Rect map=new Rect(w-mapSize-20,20,mapSize,mapSize);
            Panel(new Rect(map.x-8,map.y-8,map.width+16,map.height+48),new Color(.10f,.15f,.20f,.88f));
            if(islandMap==null)islandMap=Resources.Load<Texture2D>("IslandMap");
            if(islandMap!=null)GUI.DrawTexture(map,islandMap,ScaleMode.StretchToFill);
            foreach(var region in scene.Regions)
            {
                Vector2 p=MapPoint(region.Center,map);
                Icon(region.Id==scene.Region?"check":"star",new Rect(p.x-6,p.y-6,12,12),M3Palette.Primary(M4RegionCatalog.Get(region.Id).Element));
            }
            Vector2 player=MapPoint(scene.Traversal.transform.position,map);
            Icon("arrow_up",new Rect(player.x-6,player.y-6,12,12),Color.white);
            // The Kenney pack may not expose an up arrow; a white dot always marks the player.
            Color previous=GUI.color;GUI.color=Color.white;GUI.DrawTexture(new Rect(player.x-2,player.y-2,4,4),Texture2D.whiteTexture);GUI.color=previous;
            Text(new Rect(map.x,map.y+map.height+7,map.width,25),"N ↑   섬 지도 · F10 이동",small);
            Panel(new Rect(16,16,Mathf.Min(380,w-mapSize-56),61),new Color(.10f,.15f,.20f,.82f));
            Text(new Rect(30,24,320,28),M4RegionCatalog.Get(scene.Region).DisplayName,title);
            Text(new Rect(30,51,320,20),"다섯 원소의 섬   ·   H 위임 / 조작 안내",small);
            for(int i=0;i<scene.Party.Members.Count;i++)
            {
                var member=scene.Party.Members[i];float y=h-242+i*44;
                Panel(new Rect(w-190,y,174,38),i==scene.Party.ActiveIndex?new Color(.24f,.34f,.43f,.90f):new Color(.10f,.15f,.20f,.76f));
                Icon(i==scene.Party.ActiveIndex?"check":"star",new Rect(w-179,y+8,21,21),M3Palette.Primary(member.Actor.Element));
                Text(new Rect(w-149,y+7,125,26),(i+1)+" "+(member.IsPermanent?member.Name:Name(member.Actor.Element)),label);
            }
            float left=Mathf.Max(16,w*.5f-145);
            Bar(new Rect(left,h-53,290,12),scene.Vitals.Health,scene.Vitals.MaximumHealth,new Color(.40f,.86f,.55f));
            Bar(new Rect(left+28,h-34,234,7),scene.Traversal.Stamina.Current,scene.Traversal.Stamina.Maximum,new Color(.9f,.79f,.38f));
            Text(new Rect(left,h-80,300,22),scene.Party.ActiveMember.Name+"  ·  "+scene.Traversal.Mode,small);
            Text(new Rect(20,h-73,280,57),"F 상호작용  ·  F1–F6 지역 내 이동\nWASD / Shift / Space  ·  E 등반\nG 원소 스킬  ·  Tab 원소  ·  Q 연출",small);
            if(Vector3.Distance(scene.Traversal.transform.position,scene.Layout.BossCenter)<13)
            {
                float x=w*.5f-180;Panel(new Rect(x,87,360,57));
                Text(new Rect(x+12,94,336,23),"필드 수호자 · 약점 "+Element(scene.Boss.Weakness),label);
                Bar(new Rect(x+12,125,336,10),scene.Boss.HitPoints,scene.Boss.MaximumHitPoints,new Color(.9f,.40f,.27f));
            }
            if(scene.Progress.IsReadOnly||!string.IsNullOrEmpty(scene.Progress.LastSaveError))
                Text(new Rect(24,89,w-mapSize-70,45),scene.Progress.LoadMessage+" "+scene.Progress.LastSaveError,label);
            else Text(new Rect(25,87,Mathf.Min(420,w-mapSize-70),45),scene.Notice,small);
        }
        static Vector2 MapPoint(Vector3 point,Rect map)=>new Vector2(map.x+Mathf.Clamp01((point.x+1000)/2000)*map.width,
            map.y+(1-Mathf.Clamp01((point.z+1000)/2000))*map.height);
    }
}
