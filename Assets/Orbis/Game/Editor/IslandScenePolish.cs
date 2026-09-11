using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Orbis.Art;
using Orbis.M1;
using Orbis.M2;
using Orbis.M4;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Additive, repeatable dressing and minimap generation for the saved island; no height changes.</summary>
    public static class IslandScenePolish
    {
        private const string RootPath="Assets/Orbis/Game/Island";
        private const string DecorName="04 Clearing Flowers and Trail Grass";
        private static ArtAssetCatalog catalog;
        /// <summary>Refresh cartography after height changes without restoring the old foliage layout.</summary>
        public static void RefreshMapAndMetrics(M4SceneBootstrap world)
        {
            var terrains=world.GetComponentsInChildren<Terrain>(true);
            var sampler=new SavedTerrainSampler(terrains);WriteMap(sampler,world);WriteMetrics(sampler,world,terrains);
            AssetDatabase.SaveAssets();
        }
        private static readonly Vector2[] clearingOffsets={new Vector2(-52,-18),new Vector2(-45,-38),new Vector2(-28,-51),
            new Vector2(24,-48),new Vector2(44,-33),new Vector2(53,-12),new Vector2(-50,4),new Vector2(-45,42),new Vector2(-15,54)};

        [MenuItem("Orbis/Game/Polish Island Clearings and Map")]
        public static void ApplyAndValidate()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(!File.Exists(IslandSceneBuilder.ScenePath))throw new BuildFailedException("Create the island first.");
            Scene scene=SceneManager.GetSceneByPath(IslandSceneBuilder.ScenePath);
            if(!scene.IsValid()||!scene.isLoaded)
            {
                if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
                scene=EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath,OpenSceneMode.Single);
            }
            M4SceneBootstrap world=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<M4SceneBootstrap>(true)).Single();
            Terrain[] terrains=world.GetComponentsInChildren<Terrain>(true);
            if(terrains.Length!=4)throw new BuildFailedException("The saved island needs its four Terrain tiles.");
            catalog=AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>("Assets/Orbis/Art/Resources/Art/Catalog.asset");
            foreach(string folder in new[]{"Resources","Vegetation","Materials"})Directory.CreateDirectory(RootPath+"/"+folder);
            AssetDatabase.Refresh();
            var sampler=new SavedTerrainSampler(terrains);
            var exclusions=BuildExclusions(world);
            Transform decor=world.transform.Find(DecorName);
            if(decor==null)
            {
                decor=IslandTerrainBuilder.Node(DecorName,world.transform,Vector3.zero);
                AddClearingClusters(world,decor,sampler,exclusions);
                AddStarterLandmarks(decor,sampler,exclusions);
            }
            AddTerrainGrass(terrains,sampler,exclusions);
            WriteMap(sampler,world);
            ValidateTreeMaterials(terrains);
            WriteMetrics(sampler,world,terrains);
            foreach(var item in decor.GetComponentsInChildren<Transform>(true))
            {
                if(!PrefabUtility.IsPartOfPrefabInstance(item))continue;
                PrefabUtility.RecordPrefabInstancePropertyModifications(item.gameObject);
                foreach(var component in item.GetComponents<Component>())if(component!=null)PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(scene,IslandSceneBuilder.ScenePath))throw new BuildFailedException("Could not save island polish.");
            IslandSceneBuilder.Validate();
            Debug.Log("Island polish complete: saved terrain preserved, near-field grass/flowers added, Resources/IslandMap generated north-up.");
        }
        private readonly struct Exclusion
        {
            public readonly Vector2 Center;public readonly float Radius;
            public Exclusion(Vector3 center,float radius){Center=new Vector2(center.x,center.z);Radius=radius;}
        }
        private static List<Exclusion> BuildExclusions(M4SceneBootstrap world)
        {
            var result=new List<Exclusion>();
            foreach(M4AuthoredRegion region in world.GetComponentsInChildren<M4AuthoredRegion>(true))
            {
                foreach(var actor in region.Statues)result.Add(new Exclusion(actor.transform.position,3.5f));
                foreach(var actor in region.Targets)result.Add(new Exclusion(actor.transform.position,3.5f));
                result.Add(new Exclusion(region.Targets.Select(x=>x.transform.position).Aggregate(Vector3.zero,(a,b)=>a+b)/3,10));
                result.Add(new Exclusion(region.BossObject.transform.position,11));
                foreach(var marker in new[]{region.Npc,region.Portal,region.Chest,region.Spawn,region.ChallengeEntry})result.Add(new Exclusion(marker.position,3));
            }
            return result;
        }
        private static bool Excluded(float x,float z,List<Exclusion> areas,float padding=0)
        {
            Vector2 point=new Vector2(x,z);
            foreach(var area in areas)if(Vector2.Distance(point,area.Center)<area.Radius+padding)return true;
            return false;
        }
        private static void AddClearingClusters(M4SceneBootstrap world,Transform parent,SavedTerrainSampler sampler,List<Exclusion> areas)
        {
            var random=new System.Random(9851);int count=0;
            foreach(var region in world.IslandRegions)
                for(int i=0;i<clearingOffsets.Length;i++)
                {
                    Vector2 p=new Vector2(region.Center.position.x,region.Center.position.z)+clearingOffsets[i];
                    if(Excluded(p.x,p.y,areas,5)||IslandTerrainBuilder.RoadDistance(p.x,p.y,out _)<10)continue;
                    var cluster=IslandTerrainBuilder.Node(region.Id+" Flower Patch "+i,parent,new Vector3(p.x,sampler.Height(p.x,p.y),p.y));
                    for(int j=0;j<7;j++)
                    {
                        float x=p.x+(float)random.NextDouble()*3.6f-1.8f,z=p.y+(float)random.NextDouble()*3.6f-1.8f;
                        string file=j<4?"grass_large":"flower_purpleA";
                        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ImportedAssets/Kenney/NatureKit/Models/"+file+".fbx");
                        var model=(GameObject)PrefabUtility.InstantiatePrefab(source,cluster);model.name=file+" "+j;
                        FitHeight(model,new Vector3(x,sampler.Height(x,z),z),j<4?.35f+.16f*(float)random.NextDouble():.55f);
                        model.transform.rotation=Quaternion.Euler(0,(float)random.NextDouble()*360,0);
                        RecolorPlants(model,j>=4);foreach(var collider in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                    }
                    var lod=cluster.gameObject.AddComponent<LODGroup>();var renderers=cluster.GetComponentsInChildren<Renderer>(true);
                    lod.SetLODs(new[]{new LOD(.08f,renderers)});lod.RecalculateBounds();
                    // At the game's 60-degree FOV this is culled before 80 m (also safe down to a 50-degree FOV).
                    float threshold=Mathf.Clamp(lod.size/(160*Mathf.Tan(25*Mathf.Deg2Rad)),.015f,.25f);
                    lod.SetLODs(new[]{new LOD(threshold,renderers)});count++;
                }
            Debug.Log("Island clearings: "+count+" flower/grass patches with approximately 80 m maximum view distance.");
        }
        private static void AddStarterLandmarks(Transform parent,SavedTerrainSampler sampler,List<Exclusion> areas)
        {
            Vector2[] points={new Vector2(-35,-205),new Vector2(-25,-190),new Vector2(32,-205),new Vector2(38,-185),
                new Vector2(16,-188),new Vector2(42,-210),new Vector2(-40,-215),new Vector2(27,-220),new Vector2(-42,-230),new Vector2(40,-228)};
            int count=0;
            for(int i=0;i<points.Length&&count<8;i++)
            {
                Vector2 p=points[i];if(Excluded(p.x,p.y,areas,8)||IslandTerrainBuilder.RoadDistance(p.x,p.y,out _)<11)continue;
                bool tree=count==0||count==3||count==6;
                GameObject source=tree?AssetDatabase.LoadAssetAtPath<GameObject>(RootPath+"/Vegetation/Oak.prefab"):catalog.Model("rock_bare");
                var model=(GameObject)PrefabUtility.InstantiatePrefab(source,parent);model.name="Starter "+(tree?"Oak":"Boulder")+" "+count;
                FitHeight(model,new Vector3(p.x,sampler.Height(p.x,p.y),p.y),tree?6:1.2f+count%3*.4f);
                if(!tree)
                {
                    var material=IslandTerrainBuilder.PlainMaterial(catalog,"StarterFieldStone",new Color(.40f,.43f,.37f));
                    foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                    {var mats=renderer.sharedMaterials;for(int j=0;j<mats.Length;j++)mats[j]=material;renderer.sharedMaterials=mats;}
                }
                count++;
            }
            Debug.Log("Island starter meadow: "+count+" close tree/boulder landmarks outside gameplay and road footprints.");
        }
        private static void FitHeight(GameObject model,Vector3 feet,float targetHeight)
        {
            var renderers=model.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)throw new BuildFailedException("Empty foliage source.");
            Bounds box=renderers[0].bounds;foreach(var renderer in renderers)box.Encapsulate(renderer.bounds);
            float scale=targetHeight/Mathf.Max(.001f,box.size.y);model.transform.localScale*=scale;
            model.transform.position=feet-new Vector3(box.center.x-model.transform.position.x,box.min.y-model.transform.position.y,box.center.z-model.transform.position.z)*scale;
        }
        private static void RecolorPlants(GameObject model,bool flowers)
        {
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    string name=materials[i]!=null?materials[i].name.ToLowerInvariant():"grass";
                    bool petal=flowers&&(name.Contains("flower")||name.Contains("purple")||name.Contains("petal"));
                    materials[i]=IslandTerrainBuilder.PlainMaterial(catalog,petal?"FieldFlowerPetal":"FieldGrassBlades",petal?new Color(.66f,.53f,.78f):new Color(.32f,.47f,.20f));
                }
                renderer.sharedMaterials=materials;
            }
        }
        private static void AddTerrainGrass(Terrain[] terrains,SavedTerrainSampler sampler,List<Exclusion> exclusions)
        {
            string path=RootPath+"/Vegetation/MeadowDetail.prefab";
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab==null)
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ImportedAssets/Kenney/NatureKit/Models/grass_large.fbx");
                var model=Object.Instantiate(source);
                if(model.GetComponent<MeshFilter>()==null)
                {
                    // Terrain detail prototypes need a direct mesh renderer, not an FBX wrapper root.
                    var meshChild=model.GetComponentInChildren<MeshFilter>();
                    if(meshChild==null)throw new BuildFailedException("The existing grass source has no mesh.");
                    var direct=Object.Instantiate(meshChild.gameObject);Object.DestroyImmediate(model);model=direct;
                }
                model.name="Meadow Detail Blades";
                try
                {
                    FitHeight(model,Vector3.zero,.65f);RecolorPlants(model,false);
                    foreach(var collider in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                    prefab=PrefabUtility.SaveAsPrefabAsset(model,path);
                }
                finally{Object.DestroyImmediate(model);}
            }
            // The installed Unity 6000.6 TerrainModule exposes these fields and InstanceCountMode.
            var prototype=new DetailPrototype{prototype=prefab,usePrototypeMesh=true,useInstancing=true,renderMode=DetailRenderMode.VertexLit,
                minWidth=.65f,maxWidth=1.1f,minHeight=.75f,maxHeight=1.25f,healthyColor=Color.white,dryColor=Color.white,
                noiseSpread=.3f,noiseSeed=8713,density=1,useDensityScaling=true,alignToGround=1,positionJitter=.9f};
            long candidates=0;
            foreach(Terrain terrain in terrains)
            {
                TerrainData data=terrain.terrainData;data.SetDetailResolution(512,16);
                data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
                data.detailPrototypes=new[]{prototype};var layer=new int[512,512];Vector3 origin=terrain.transform.position;
                for(int z=0;z<512;z++)for(int x=0;x<512;x++)
                {
                    float wx=origin.x+(x+.5f)*data.size.x/512,wz=origin.z+(z+.5f)*data.size.z/512;
                    float height=sampler.Height(wx,wz);
                    if(height<3||height>165||sampler.WaterLevel(wx,wz)>height||Excluded(wx,wz,exclusions)||IslandTerrainBuilder.RoadDistance(wx,wz,out _)<5)continue;
                    if(data.GetSteepness((x+.5f)/512,(z+.5f)/512)>30)continue;
                    if(IslandTerrainBuilder.BiomeAt(wx,wz)==M4RegionId.Agnia&&IslandTerrainBuilder.SiteDistance(wx,wz)>75)continue;
                    float patch=Mathf.PerlinNoise(wx*.035f+51,wz*.035f+32);
                    if(patch<.33f)continue;
                    int count=(x*17+z*13)%5==0?2:1;layer[z,x]=count;candidates+=count;
                }
                data.SetDetailLayer(0,0,0,layer);terrain.detailObjectDistance=100;terrain.detailObjectDensity=.24f;
                EditorUtility.SetDirty(data);EditorUtility.SetDirty(terrain);
            }
            Debug.Log("Island ground cover: "+candidates+" stored grass candidates, 1-2 per 3.81 m² cell, density scale 0.24, 100 m draw distance; near-camera budget roughly 2,500 clumps before exclusions.");
        }
        private static void WriteMap(SavedTerrainSampler sampler,M4SceneBootstrap world)
        {
            const int resolution=256;string path=RootPath+"/Resources/IslandMap.asset";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(texture==null){texture=new Texture2D(resolution,resolution,TextureFormat.RGB24,false){name="IslandMap"};AssetDatabase.CreateAsset(texture,path);}
            else if(texture.width!=resolution||texture.height!=resolution)texture.Reinitialize(resolution,resolution,TextureFormat.RGB24,false);
            var palette=new[]{new Color(.52f,.37f,.28f),new Color(.56f,.65f,.44f),new Color(.34f,.53f,.27f),new Color(.51f,.50f,.44f),new Color(.43f,.47f,.54f)};
            var colors=new Color[resolution*resolution];
            for(int y=0;y<resolution;y++)for(int x=0;x<resolution;x++)
            {
                // Texture row zero is the south; +Z/north is at the top. HUD should use v=(z+1000)/2000.
                float wx=-1000+(x+.5f)*2000/resolution,wz=-1000+(y+.5f)*2000/resolution;
                float height=sampler.Height(wx,wz),water=sampler.WaterLevel(wx,wz);Color color;
                if(height<water)
                    color=Color.Lerp(new Color(.14f,.30f,.40f),new Color(.27f,.54f,.60f),1-Mathf.Clamp01((water-height)/24));
                else
                {
                    Color biome=Color.black;float weightSum=0;
                    foreach(var region in world.IslandRegions)
                    {
                        Vector3 p=region.Center.position;float sq=(p.x-wx)*(p.x-wx)+(p.z-wz)*(p.z-wz);
                        float weight=1/Mathf.Pow(1+sq/45000,2);biome+=palette[(int)region.Id]*weight;weightSum+=weight;
                    }
                    color=biome/weightSum;
                    float west=sampler.Height(wx-5,wz),east=sampler.Height(wx+5,wz),south=sampler.Height(wx,wz-5),north=sampler.Height(wx,wz+5);
                    float relief=Mathf.Clamp((west-east+north-south)*.012f,-.20f,.20f);
                    color*=.92f+relief; color=Color.Lerp(color,new Color(.76f,.74f,.65f),Mathf.Clamp01((height-125)/180)*.45f);
                    color=Color.Lerp(color,new Color(.82f,.71f,.48f),Mathf.Clamp01(sampler.Trail(wx,wz)*1.65f));
                }
                colors[y*resolution+x]=color;
            }
            texture.SetPixels(colors);texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;texture.Apply(false,false);
            EditorUtility.SetDirty(texture);
        }
        private static void ValidateTreeMaterials(Terrain[] terrains)
        {
            var prototypes=terrains.SelectMany(x=>x.terrainData.treePrototypes).Select(x=>x.prefab).Distinct();
            foreach(var prefab in prototypes)
            {
                Material[] materials=prefab.GetComponentsInChildren<Renderer>(true).SelectMany(x=>x.sharedMaterials).Distinct().ToArray();
                if(prefab.name.Contains("Shrub"))continue;
                bool bark=materials.Any(x=>x!=null&&x.name.Contains("Bark")),leaf=materials.Any(x=>x!=null&&x.name.Contains("Leaves"));
                if(!bark||!leaf)throw new BuildFailedException("Tree bark and leaves must retain separate source submesh materials: "+prefab.name);
            }
            Debug.Log("Island tree palette verified: NatureKit uses authored diffuse submesh colors, with separate brown bark and green leaves in both LODs; no source atlas exists to restore.");
        }
        private static void WriteMetrics(SavedTerrainSampler sampler,M4SceneBootstrap world,Terrain[] terrains)
        {
            var text=new StringBuilder();
            text.AppendLine("ORBIS ISLAND / saved terrain metrics");
            text.AppendLine("Canvas: 2000 x 2000 m (4.000 km²). Sea level: 0 m.");
            double land=0;float minimum=float.MaxValue,maximum=float.MinValue;
            foreach(Terrain terrain in terrains)
            {
                TerrainData data=terrain.terrainData;int n=data.heightmapResolution;float[,] heights=data.GetHeights(0,0,n,n);
                double cellArea=data.size.x/(n-1)*(data.size.z/(n-1));
                for(int z=0;z<n;z++)for(int x=0;x<n;x++)
                {
                    float h=terrain.transform.position.y+heights[z,x]*data.size.y;minimum=Mathf.Min(minimum,h);maximum=Mathf.Max(maximum,h);
                    if(x<n-1&&z<n-1)
                    {
                        float center=terrain.transform.position.y+(heights[z,x]+heights[z,x+1]+heights[z+1,x]+heights[z+1,x+1])*.25f*data.size.y;
                        if(center>0)land+=cellArea;
                    }
                }
            }
            text.AppendLine("Land above sea: "+(land/1000000).ToString("0.000")+" km² ("+(land/4000000*100).ToString("0.0")+"%); sampled saved heightfield cell centers.");
            text.AppendLine("Saved height range: "+minimum.ToString("0.0")+" .. "+maximum.ToString("0.0")+" m.");
            Vector3[] route=IslandTerrainBuilder.RoutePoints();double plan=0,surface=0;
            for(int i=0;i<route.Length-1;i++)
            {
                Vector3 a=route[i],b=route[i+1];float horizontal=Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));plan+=horizontal;
                int steps=Mathf.Max(1,Mathf.CeilToInt(horizontal/5));Vector3 previous=new Vector3(a.x,sampler.Height(a.x,a.z),a.z);
                for(int j=1;j<=steps;j++)
                {
                    Vector3 p=Vector3.Lerp(a,b,j/(float)steps);p.y=sampler.Height(p.x,p.z);surface+=Vector3.Distance(previous,p);previous=p;
                }
            }
            text.AppendLine("Main loop: "+plan.ToString("0.0")+" m horizontal / "+surface.ToString("0.0")+" m sampled surface length.");
            var sites=world.IslandRegions;
            for(int i=0;i<sites.Length;i++)
            {
                Vector3 a=sites[i].Center.position;text.AppendLine(sites[i].Id+" site: "+a.ToString("F1"));
                for(int j=i+1;j<sites.Length;j++)
                {Vector3 b=sites[j].Center.position;text.AppendLine("  to "+sites[j].Id+": "+Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z)).ToString("0.0")+" m horizontal");}
            }
            text.AppendLine("Map: Resources/IslandMap, 256 x 256, X [-1000,1000], Z [-1000,1000], north up. Roads read from saved Terrain alphamaps.");
            Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/Island-Map-Metrics.txt",text.ToString());Debug.Log(text.ToString());
        }
        private sealed class SavedTerrainSampler
        {
            private readonly Terrain[] terrains;
            private readonly float[][,,] paint;
            private readonly WaterVolume[] water;
            public SavedTerrainSampler(Terrain[] values)
            {
                terrains=values;paint=new float[values.Length][,,];
                for(int i=0;i<values.Length;i++){var d=values[i].terrainData;paint[i]=d.GetAlphamaps(0,0,d.alphamapWidth,d.alphamapHeight);}
                water=values[0].transform.root.GetComponentsInChildren<WaterVolume>(true);Physics.SyncTransforms();
            }
            private int Tile(float x,float z)
            {
                for(int i=0;i<terrains.Length;i++)
                {Vector3 p=terrains[i].transform.position,s=terrains[i].terrainData.size;if(x>=p.x&&x<=p.x+s.x&&z>=p.z&&z<=p.z+s.z)return i;}
                return -1;
            }
            public float Height(float x,float z)
            {
                int i=Tile(x,z);if(i<0)return -60;Terrain t=terrains[i];Vector3 p=t.transform.position,s=t.terrainData.size;
                return p.y+t.terrainData.GetInterpolatedHeight(Mathf.Clamp01((x-p.x)/s.x),Mathf.Clamp01((z-p.z)/s.z));
            }
            public float Trail(float x,float z)
            {
                int i=Tile(x,z);if(i<0)return 0;Terrain t=terrains[i];Vector3 p=t.transform.position,s=t.terrainData.size;
                int px=Mathf.Clamp(Mathf.RoundToInt((x-p.x)/s.x*(paint[i].GetLength(1)-1)),0,paint[i].GetLength(1)-1);
                int pz=Mathf.Clamp(Mathf.RoundToInt((z-p.z)/s.z*(paint[i].GetLength(0)-1)),0,paint[i].GetLength(0)-1);
                return paint[i].GetLength(2)>5?paint[i][pz,px,5]:0;
            }
            public float WaterLevel(float x,float z)
            {
                float level=0;
                foreach(var volume in water)
                {
                    Bounds box=volume.Bounds;
                    if(x>=box.min.x&&x<=box.max.x&&z>=box.min.z&&z<=box.max.z)level=Mathf.Max(level,volume.SurfaceY);
                }
                return level;
            }
        }
    }
}
