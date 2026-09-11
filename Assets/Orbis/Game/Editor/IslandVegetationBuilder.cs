using System;
using System.Collections.Generic;
using Orbis.Art;
using Orbis.M4;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Terrain instances and two mesh LODs provide forests without one GameObject per tree.</summary>
    public static class IslandVegetationBuilder
    {
        public static void Build(Transform root,ArtAssetCatalog catalog,Terrain[] terrains)
        {
            GameObject[] prefabs={TreePrefab(catalog,"Oak","tree_oak","tree_default",8f),
                TreePrefab(catalog,"Pine","tree_pine","tree_pineDefaultA",12f),
                TreePrefab(catalog,"Palm","tree_palm","tree_palm",9f),
                TreePrefab(catalog,"Shrub","bush","plant_bushDetailed",1.4f)};
            var prototypes=new TreePrototype[prefabs.Length];
            for(int i=0;i<prefabs.Length;i++)prototypes[i]=new TreePrototype{prefab=prefabs[i],bendFactor=0};
            int total=0;
            foreach(Terrain terrain in terrains)
            {
                TerrainData data=terrain.terrainData;data.treePrototypes=prototypes;
                var instances=new List<TreeInstance>();Vector3 origin=terrain.transform.position;
                var random=new System.Random(7361+(int)origin.x*3+(int)origin.z*7);
                // Fourteen-metre candidates, filtered by patch noise, produce irregular forest edges and open vistas.
                for(float z=8;z<995;z+=14)for(float x=8;x<995;x+=14)
                {
                    float lx=x+(float)random.NextDouble()*10-5,lz=z+(float)random.NextDouble()*10-5;
                    float wx=origin.x+lx,wz=origin.z+lz,height=IslandTerrainBuilder.HeightAt(wx,wz);
                    if(height<3||height>195||IslandTerrainBuilder.SiteDistance(wx,wz)<108||IslandTerrainBuilder.RoadDistance(wx,wz,out _)<18)continue;
                    if(IsLandmarkSpace(wx,wz))continue;
                    // The lake shore must remain clear and allow visual access to the waterline.
                    float lakeEllipse=Mathf.Pow((wx-230)/155,2)+Mathf.Pow((wz-35)/120,2);
                    if(lakeEllipse<1.15f)continue;
                    float slope=data.GetSteepness(lx/1000,lz/1000);if(slope>31)continue;
                    M4RegionId biome=IslandTerrainBuilder.BiomeAt(wx,wz);
                    float patch=Mathf.PerlinNoise(wx*.009f+33,wz*.009f+16);
                    float keep=biome==M4RegionId.Agnia?.68f:biome==M4RegionId.Zephyr?.52f:.42f;
                    if(patch<keep||random.NextDouble()>.82)continue;
                    int prototype=biome==M4RegionId.Teluna?2:biome==M4RegionId.Granite||biome==M4RegionId.Agnia?1:0;
                    instances.Add(Instance(prototype,lx,lz,(float)random.NextDouble(),random));
                    if(random.NextDouble()<.42)
                    {
                        float bx=Mathf.Clamp(lx+3,0,999),bz=Mathf.Clamp(lz-3,0,999);
                        instances.Add(Instance(3,bx,bz,(float)random.NextDouble(),random));
                    }
                }
                data.SetTreeInstances(instances.ToArray(),true);data.RefreshPrototypes();
                EditorUtility.SetDirty(data);total+=instances.Count;
            }
            Debug.Log("Island vegetation: "+total+" Terrain tree/shrub instances using shared materials and two LOD meshes; no per-tree scene GameObjects.");
        }
        private static bool IsLandmarkSpace(float x,float z)
        {
            if(x>-205&&x<-50&&z>-355&&z<-120)return true;
            if(x>320&&x<630&&z>355&&z<615)return true;
            Vector2 p=new Vector2(x,z);
            foreach(Vector2 center in new[]{new Vector2(95,-280),new Vector2(-660,310),new Vector2(-625,-390),
                new Vector2(620,-245),new Vector2(695,-310),new Vector2(710,-535)})
                if(Vector2.Distance(p,center)<30)return true;
            return false;
        }
        private static TreeInstance Instance(int prototype,float x,float z,float randomScale,System.Random random)
        {
            float scale=.78f+randomScale*.55f;
            return new TreeInstance{prototypeIndex=prototype,position=new Vector3(x/1000,0,z/1000),widthScale=scale,heightScale=scale,
                rotation=(float)random.NextDouble()*Mathf.PI*2,color=Color.white,lightmapColor=Color.white};
        }
        private static GameObject TreePrefab(ArtAssetCatalog catalog,string name,string highKey,string lowFile,float height)
        {
            string path=IslandTerrainBuilder.AssetRoot+"/Vegetation/"+name+".prefab";
            GameObject saved=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(saved!=null)return saved;
            var root=new GameObject("Island "+name);
            try
            {
                GameObject high=Object.Instantiate(catalog.Model(highKey),root.transform);high.name="Near Foliage";
                GameObject lowSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ImportedAssets/Kenney/NatureKit/Models/"+lowFile+".fbx");
                if(lowSource==null)throw new InvalidOperationException("Missing existing foliage source: "+lowFile);
                GameObject low=Object.Instantiate(lowSource,root.transform);low.name="Distant Foliage";
                foreach(var model in new[]{high,low})
                {
                    foreach(var collider in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                    var renderers=model.GetComponentsInChildren<Renderer>(true);
                    Bounds b=renderers[0].bounds;foreach(var renderer in renderers)b.Encapsulate(renderer.bounds);
                    float scale=height/Mathf.Max(.01f,b.size.y);model.transform.localScale*=scale;
                    model.transform.localPosition-=new Vector3(b.center.x,b.min.y,b.center.z)*scale;
                    foreach(var renderer in renderers)
                    {
                        Material[] materials=renderer.sharedMaterials;
                        for(int i=0;i<materials.Length;i++)
                        {
                            string materialName=materials[i]!=null?materials[i].name.ToLowerInvariant():"leaf";
                            bool wood=materialName.Contains("wood")||materialName.Contains("bark")||materialName.Contains("trunk");
                            Color color=wood?new Color(.26f,.19f,.12f):name=="Pine"?new Color(.20f,.33f,.18f):name=="Palm"?new Color(.27f,.45f,.20f):new Color(.25f,.42f,.19f);
                            materials[i]=IslandTerrainBuilder.PlainMaterial(catalog,wood?"IslandTreeBark":"Island"+name+"Leaves",color);
                        }
                        renderer.sharedMaterials=materials;
                    }
                }
                if(name!="Shrub")
                {
                    var trunk=root.AddComponent<CapsuleCollider>();
                    trunk.height=name=="Palm"?6:name=="Pine"?4:3;
                    trunk.center=Vector3.up*(trunk.height*.5f);trunk.radius=name=="Palm"?.22f:.30f;
                    root.layer=8;
                }
                var lod=root.AddComponent<LODGroup>();
                lod.SetLODs(new[]{new LOD(.13f,high.GetComponentsInChildren<Renderer>(true)),new LOD(.008f,low.GetComponentsInChildren<Renderer>(true))});
                lod.RecalculateBounds();lod.fadeMode=LODFadeMode.None;
                return PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{Object.DestroyImmediate(root);}
        }
    }
}
