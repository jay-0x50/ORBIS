using System;
using System.Collections.Generic;
using Orbis.Art;
using Orbis.M2;
using Orbis.M4;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Original landmark compositions using already licensed models; source assets remain unchanged.</summary>
    public static class IslandLandmarkBuilder
    {
        private static ArtAssetCatalog catalog;
        private static readonly Dictionary<string,Bounds> bounds=new Dictionary<string,Bounds>();
        public static void Build(Transform root,ArtAssetCatalog assets)
        {
            catalog=assets;bounds.Clear();
            var landmarks=IslandTerrainBuilder.Node("03 Regional Landmarks and Trail Stops",root,Vector3.zero);
            Zephyr(landmarks);Agnia(landmarks);Teluna(landmarks);Granite(landmarks);Voltheim(landmarks);
            Vector3[] route=IslandTerrainBuilder.RoutePoints();
            for(int i=3;i<route.Length-1;i+=6)
            {
                Vector3 point=route[i];Vector3 forward=(route[i+1]-point).normalized;
                Vector3 side=new Vector3(forward.z,0,-forward.x)*14;
                Vector3 feet=IslandTerrainBuilder.Sample(point.x+side.x,point.z+side.z);
                if(IslandTerrainBuilder.SiteDistance(feet.x,feet.z)<112)continue;
                var stop=IslandTerrainBuilder.Node("Trail Rest "+i,landmarks,feet);
                Model("board","Wayfinding Sign",stop,feet,new Vector3(1.3f,1.8f,.4f));
                Model("rock_bare","Trail Seat",stop,feet+new Vector3(3,0,0),new Vector3(2,.7f,1.3f),0,true);
                Model("obelisk","Small Trail Cairn",stop,feet+new Vector3(-2,0,1),new Vector3(.6f,1.4f,.6f));
            }
            foreach(var t in landmarks.GetComponentsInChildren<Transform>(true))
            {
                if(!PrefabUtility.IsPartOfPrefabInstance(t))continue;
                PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                foreach(var component in t.GetComponents<Component>())if(component!=null)PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }
        private static void Zephyr(Transform parent)
        {
            var village=IslandTerrainBuilder.Node("Zephyr Windward Village",parent,Vector3.zero);
            Vector2[] homes={new Vector2(-100,-245),new Vector2(-120,-260),new Vector2(-145,-235),new Vector2(-110,-285),new Vector2(-155,-285),new Vector2(-175,-250)};
            for(int i=0;i<homes.Length;i++)
            {
                Vector3 p=IslandTerrainBuilder.Sample(homes[i].x,homes[i].y);
                Model("tower","Village House "+(i+1),village,p,new Vector3(6+i%2,7+i%3,6+i%2),i%2==0?20:-15,true);
                Model("fence","Garden Fence "+i,village,p+new Vector3(-4,0,3),new Vector3(7,1.2f,.25f),90);
                Model("barrel","Market Barrel "+i,village,p+new Vector3(4,0,-1),new Vector3(.8f,1.1f,.8f));
            }
            Windmill(village,-95,-325,0);Windmill(village,-165,-165,25);Windmill(village,95,-280,-30);
            Model("bridge_stone","Village Stone Arch",village,IslandTerrainBuilder.Sample(-85,-257),new Vector3(8,3,3),90,true);
        }
        private static void Windmill(Transform parent,float x,float z,float yaw)
        {
            Vector3 feet=IslandTerrainBuilder.Sample(x,z);var mill=IslandTerrainBuilder.Node("Windmill "+x+" "+z,parent,feet);
            Model("tower","Stone Windmill Tower",mill,feet,new Vector3(5,13,5),yaw,true);
            var hub=IslandTerrainBuilder.Node("Windmill Sails",mill,feet+new Vector3(0,10,-2.8f));hub.rotation=Quaternion.Euler(0,yaw,0);
            Material wood=IslandTerrainBuilder.PlainMaterial(catalog,"MillWood",new Color(.28f,.21f,.14f));
            Material cloth=IslandTerrainBuilder.PlainMaterial(catalog,"MillLinen",new Color(.83f,.79f,.62f));
            for(int i=0;i<4;i++)
            {
                var arm=IslandTerrainBuilder.Node("Sail "+i,hub,hub.position);arm.localRotation=Quaternion.Euler(0,0,i*90+20);
                Box("Sail Beam",arm,new Vector3(0,2.6f,0),new Vector3(.18f,5.4f,.20f),wood,false);
                Box("Linen Sail",arm,new Vector3(.45f,3.3f,0),new Vector3(.95f,3.4f,.06f),cloth,false);
            }
        }
        private static void Agnia(Transform parent)
        {
            var forge=IslandTerrainBuilder.Node("Agnia Volcano and Forge Trail",parent,Vector3.zero);
            for(int i=0;i<9;i++)
            {
                float a=i*Mathf.PI*2/9;float x=-610+Mathf.Cos(a)*105,z=535+Mathf.Sin(a)*105;
                Model("rock_bare","Basalt Rim "+i,forge,IslandTerrainBuilder.Sample(x,z),new Vector3(16,8+i%3*3,12),i*37,true);
            }
            Material lava=IslandTerrainBuilder.PlainMaterial(catalog,"VolcanicMagma",new Color(.96f,.27f,.06f));
            var crater=GameObject.CreatePrimitive(PrimitiveType.Cylinder);crater.name="Crater Magma Surface";crater.transform.SetParent(forge,false);
            crater.transform.position=new Vector3(-610,IslandTerrainBuilder.HeightAt(-610,535)+1,535);crater.transform.localScale=new Vector3(44,.25f,44);
            Object.DestroyImmediate(crater.GetComponent<Collider>());crater.GetComponent<Renderer>().sharedMaterial=lava;
            Vector3 camp=IslandTerrainBuilder.Sample(-660,310);
            Model("arch","Blacksmith Trail Gate",forge,camp,new Vector3(8,7,5),70,true);
            Model("lamp","Forge Hearth",forge,camp+new Vector3(6,0,0),new Vector3(3,1,3));
            Model("mine_structure","Forge Timber Canopy",forge,camp+new Vector3(-8,0,0),new Vector3(8,6,7),0,true);
            ClimbingOutcrop(forge,-720,230,14,10);
        }
        private static void Teluna(Transform parent)
        {
            var shore=IslandTerrainBuilder.Node("Teluna Cove and Coral Arches",parent,Vector3.zero);
            Vector2[] arches={new Vector2(620,-245),new Vector2(695,-310),new Vector2(710,-535)};
            for(int i=0;i<arches.Length;i++)
            {
                Vector3 p=IslandTerrainBuilder.Sample(arches[i].x,arches[i].y);p.y=Mathf.Max(0,p.y);
                Model("cave","Coastal Stone Arch "+i,shore,p,new Vector3(19,13,13),i*65,true);
                for(int j=0;j<3;j++)Model("crystal","Coral Pillar "+i+" "+j,shore,p+new Vector3((j-1)*4,0,5),new Vector3(2,4+j,2),j*35,false,new Color(.62f,.38f,.39f));
            }
            for(int i=0;i<4;i++)
            {
                float x=630+i*12,z=-455;Vector3 p=IslandTerrainBuilder.Sample(x,z);p.y=Mathf.Max(.5f,p.y);
                Model("bridge","Harbor Walkway "+i,shore,p,new Vector3(4,.65f,12),90,true);
            }
            Model("boat","Cove Canoe",shore,new Vector3(745,.05f,-405),new Vector3(2.5f,.7f,6),15);
            Model("boat","Beach Canoe",shore,IslandTerrainBuilder.Sample(640,-520),new Vector3(2,.65f,5),75);
        }
        private static void Granite(Transform parent)
        {
            var mine=IslandTerrainBuilder.Node("Granite High Mines",parent,Vector3.zero);
            for(int i=0;i<13;i++)
            {
                float a=i*2.4f,x=-590+Mathf.Cos(a)*(120+i*3),z=-585+Mathf.Sin(a)*(90+i*2);
                if(IslandTerrainBuilder.SiteDistance(x,z)<110)continue;
                Model("rock_bare","Mountain Crag "+i,mine,IslandTerrainBuilder.Sample(x,z),new Vector3(14+i%3*3,12+i%4*4,13),i*29,true);
            }
            Vector3 entrance=IslandTerrainBuilder.Sample(-625,-390);
            Model("cave","Mine Rock Arch",mine,entrance,new Vector3(20,14,16),90,true);
            Model("mine_support","Mine Portal Support",mine,entrance+new Vector3(6,0,0),new Vector3(5,7,4),90);
            for(int i=0;i<4;i++)Model("crystal","Exposed Mineral "+i,mine,entrance+new Vector3(-6,0,(i-1.5f)*3),new Vector3(2,4+i,2),i*19,false,new Color(.45f,.57f,.64f));
            ClimbingOutcrop(mine,-310,-610,18,12);
        }
        private static void Voltheim(Transform parent)
        {
            var city=IslandTerrainBuilder.Node("Voltheim Storm Spire District",parent,Vector3.zero);
            Vector2[] towers={new Vector2(550,470),new Vector2(570,520),new Vector2(490,570),new Vector2(380,550),new Vector2(590,390),new Vector2(350,525)};
            for(int i=0;i<towers.Length;i++)
            {
                Vector3 p=IslandTerrainBuilder.Sample(towers[i].x,towers[i].y);
                Model("tower","Storm Spire "+i,city,p,new Vector3(9,21+i%3*7,9),i*15,true);
                Model("crystal","Conductor Crown "+i,city,p+Vector3.up*(21+i%3*7),new Vector3(2,6,2),0,false,new Color(.43f,.36f,.66f));
                Model("banner","Spire Banner "+i,city,p+new Vector3(0,7,-5),new Vector3(2,6,.2f));
            }
            ClimbingOutcrop(city,630,610,16,12);
        }
        private static void ClimbingOutcrop(Transform parent,float x,float z,float width,float height)
        {
            // A short climb segment fits the current 100-stamina pool; the summit is a rest/glide launch point.
            Model("cliff","Climbable Trail Outcrop",parent,IslandTerrainBuilder.Sample(x,z),new Vector3(width,height,width*.7f),20,true);
        }
        private static GameObject Model(string key,string name,Transform parent,Vector3 feet,Vector3 size,float yaw=0,bool solid=false,Color? tint=null)
        {
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(catalog.Model(key),parent);instance.name=name;
            if(!bounds.TryGetValue(key,out Bounds box))
            {
                bool first=true;box=default;
                foreach(Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Bounds b=renderer.bounds;
                    for(int i=0;i<8;i++)
                    {
                        Vector3 p=instance.transform.InverseTransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                        if(first){box=new Bounds(p,Vector3.zero);first=false;}else box.Encapsulate(p);
                    }
                }
                bounds.Add(key,box);
            }
            Vector3 scale=new Vector3(size.x/Mathf.Max(.001f,box.size.x),size.y/Mathf.Max(.001f,box.size.y),size.z/Mathf.Max(.001f,box.size.z));
            instance.transform.localScale=scale;instance.transform.rotation=Quaternion.Euler(0,yaw,0);
            instance.transform.position=feet-instance.transform.rotation*Vector3.Scale(new Vector3(box.center.x,box.min.y,box.center.z),scale);
            foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Color? color=tint;
                if(!color.HasValue&&(key=="cliff"||key=="cave"||key=="rock_bare"||key=="arch"))color=new Color(.39f,.39f,.36f);
                if(!color.HasValue&&key=="tower")color=renderer.name.Contains("roof")?new Color(.27f,.34f,.42f):new Color(.56f,.52f,.43f);
                if(color.HasValue)
                {
                    Color c=color.Value;string materialName="Landmark_"+ColorUtility.ToHtmlStringRGB(c);
                    Material material=IslandTerrainBuilder.PlainMaterial(catalog,materialName,c);
                    var list=renderer.sharedMaterials;for(int i=0;i<list.Length;i++)list[i]=material;renderer.sharedMaterials=list;
                }
                if(solid&&renderer.TryGetComponent<MeshFilter>(out var filter))
                {
                    renderer.gameObject.layer=8;var collider=renderer.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;
                }
            }
            if(solid)instance.AddComponent<ClimbableSurface>();
            return instance;
        }
        private static void Box(string name,Transform parent,Vector3 local,Vector3 size,Material material,bool solid)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=local;go.transform.localScale=size;
            go.GetComponent<Renderer>().sharedMaterial=material;if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());else go.layer=8;
        }
    }
}
