using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Art;
using Orbis.M2;
using Orbis.M4;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Game.Editor
{
    public sealed class IslandTerrainResult
    {
        public Terrain[] Tiles;
        public WaterVolume Ocean, Lake;
        public IReadOnlyDictionary<M4RegionId,Vector3> Sites;
        public Vector3 Spawn;
        public Vector3[] Route;
    }

    /// <summary>Original continuous 2 km island; generated TerrainData/materials remain editable assets.</summary>
    public static class IslandTerrainBuilder
    {
        public const string AssetRoot="Assets/Orbis/Game/Island";
        public const float Extent=1000f, SeaLevel=0f, LakeLevel=17f;
        private const float BaseY=-60f, HeightRange=320f, TileSize=1000f;
        private const int HeightResolution=513, PaintResolution=256;
        private static readonly Vector2[] sites={new Vector2(-520,340),new Vector2(520,-380),new Vector2(0,-180),new Vector2(-450,-450),new Vector2(450,430)};
        private static readonly float[] siteHeights={90,12,25,115,90};
        private static readonly int[] roadOrder={2,1,4,0,3};
        private static Vector3[] cachedRoute;
        public static Vector3 SiteCenter(M4RegionId id) {int i=(int)id;return new Vector3(sites[i].x,siteHeights[i],sites[i].y);}
        public static Vector3 Sample(float x,float z,float offset=0)=>new Vector3(x,HeightAt(x,z)+offset,z);
        public static M4RegionId BiomeAt(float x,float z)
        {
            int selected=0;float best=float.MaxValue;
            for(int i=0;i<sites.Length;i++) {float distance=(sites[i]-new Vector2(x,z)).sqrMagnitude;if(distance<best){best=distance;selected=i;}}
            return (M4RegionId)selected;
        }
        public static float SiteDistance(float x,float z)
        {
            float distance=float.MaxValue;
            foreach(var site in sites)distance=Mathf.Min(distance,Vector2.Distance(site,new Vector2(x,z)));
            return distance;
        }
        public static Vector3[] RoutePoints()
        {
            if(cachedRoute!=null)return cachedRoute;
            var result=new List<Vector3>();
            for(int segment=0;segment<roadOrder.Length;segment++)
            {
                Vector2 p0=sites[roadOrder[(segment+4)%5]],p1=sites[roadOrder[segment]],p2=sites[roadOrder[(segment+1)%5]],p3=sites[roadOrder[(segment+2)%5]];
                for(int step=0;step<12;step++)
                {
                    float t=step/12f,t2=t*t,t3=t2*t;
                    Vector2 p=.5f*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t2+(-p0+3*p1-3*p2+p3)*t3);
                    float y=Mathf.Lerp(siteHeights[roadOrder[segment]],siteHeights[roadOrder[(segment+1)%5]],Mathf.SmoothStep(0,1,t));
                    result.Add(new Vector3(p.x,y,p.y));
                }
            }
            result.Add(result[0]);cachedRoute=result.ToArray();return cachedRoute;
        }
        public static float RoadDistance(float x,float z,out float elevation)
        {
            Vector3[] road=RoutePoints();Vector2 p=new Vector2(x,z);float best=float.MaxValue;elevation=0;
            for(int i=0;i<road.Length-1;i++)
            {
                Vector2 a=new Vector2(road[i].x,road[i].z),b=new Vector2(road[i+1].x,road[i+1].z),edge=b-a;
                float t=Mathf.Clamp01(Vector2.Dot(p-a,edge)/edge.sqrMagnitude);float sq=(p-(a+edge*t)).sqrMagnitude;
                if(sq<best){best=sq;elevation=Mathf.Lerp(road[i].y,road[i+1].y,t);}
            }
            return Mathf.Sqrt(best);
        }
        // The shared Step 1 sampler is authoritative for terrain, content placement and later vegetation builders.
        public static float HeightAt(float x,float z) => WorldTerrainUpgrade.HeightAt(x,z);
        private static float Gaussian(float x,float z,float cx,float cz,float radius)=>Mathf.Exp(-((x-cx)*(x-cx)+(z-cz)*(z-cz))/(radius*radius));
        private static float Smooth(float start,float end,float value)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,end,value));

        public static IslandTerrainResult Build(Transform root,ArtAssetCatalog catalog)
        {
            if(root==null||catalog==null)throw new ArgumentNullException();
            foreach(string folder in new[]{"Terrain","Layers","Textures","Materials","Meshes","Vegetation"})Directory.CreateDirectory(AssetRoot+"/"+folder);
            AssetDatabase.Refresh();
            var land=Node("01 Continuous Island Terrain",root,Vector3.zero);
            TerrainLayer[] layers=CreateLayers();
            Material terrainMaterial=AssetDatabase.LoadAssetAtPath<Material>(AssetRoot+"/Materials/IslandTerrain.mat");
            if(terrainMaterial==null)
            {
                Shader shader=Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if(shader==null)throw new InvalidOperationException("URP Terrain Lit shader is missing.");
                terrainMaterial=new Material(shader){name="Island Terrain Lit"};
                AssetDatabase.CreateAsset(terrainMaterial,AssetRoot+"/Materials/IslandTerrain.mat");
            }
            var result=new IslandTerrainResult{Tiles=new Terrain[4],Spawn=SiteCenter(M4RegionId.Zephyr)+new Vector3(0,.1f,-30),Route=(Vector3[])RoutePoints().Clone()};
            var positions=new Dictionary<M4RegionId,Vector3>();foreach(M4RegionId id in Enum.GetValues(typeof(M4RegionId)))positions.Add(id,SiteCenter(id));result.Sites=positions;
            for(int tz=0;tz<2;tz++)for(int tx=0;tx<2;tx++)
            {
                string path=AssetRoot+"/Terrain/Island_"+tx+"_"+tz+".asset";
                var data=AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                if(data==null){data=new TerrainData();AssetDatabase.CreateAsset(data,path);}
                data.heightmapResolution=HeightResolution;data.alphamapResolution=PaintResolution;data.baseMapResolution=512;
                data.size=new Vector3(TileSize,HeightRange,TileSize);data.terrainLayers=layers;
                float ox=-Extent+tx*TileSize,oz=-Extent+tz*TileSize;
                var heights=new float[HeightResolution,HeightResolution];
                for(int z=0;z<HeightResolution;z++)for(int x=0;x<HeightResolution;x++)
                    heights[z,x]=(HeightAt(ox+x*TileSize/(HeightResolution-1),oz+z*TileSize/(HeightResolution-1))-BaseY)/HeightRange;
                data.SetHeights(0,0,heights);
                Paint(data,ox,oz);
                GameObject go=Terrain.CreateTerrainGameObject(data);go.name="Island Terrain "+tx+" "+tz;go.layer=8;
                go.transform.SetParent(land,false);go.transform.position=new Vector3(ox,BaseY,oz);
                Terrain terrain=go.GetComponent<Terrain>();terrain.materialTemplate=terrainMaterial;terrain.drawInstanced=true;
                terrain.heightmapPixelError=5;terrain.basemapDistance=1500;terrain.treeDistance=850;terrain.treeBillboardDistance=750;terrain.treeCrossFadeLength=80;terrain.treeMaximumFullLODCount=600;
                result.Tiles[tz*2+tx]=terrain;EditorUtility.SetDirty(data);
            }
            for(int z=0;z<2;z++)for(int x=0;x<2;x++)result.Tiles[z*2+x].SetNeighbors(x>0?result.Tiles[z*2+x-1]:null,z<1?result.Tiles[(z+1)*2+x]:null,x<1?result.Tiles[z*2+x+1]:null,z>0?result.Tiles[(z-1)*2+x]:null);
            BuildWater(root,catalog,result);
            IslandVegetationBuilder.Build(root,catalog,result.Tiles);
            IslandLandmarkBuilder.Build(root,catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("Island terrain created: 2000 x 2000 m, four editable TerrainData assets, five level content sites, continuous curved road and natural coastline.");
            return result;
        }
        internal static Transform Node(string name,Transform parent,Vector3 position)
        {var item=new GameObject(name).transform;item.SetParent(parent,false);item.position=position;return item;}
        private static TerrainLayer[] CreateLayers()
        {
            string[] names={"Meadow","Earth","Stone","Sand","VolcanicAsh","Trail"};
            Color[] colors={new Color(.28f,.43f,.19f),new Color(.42f,.32f,.21f),new Color(.38f,.39f,.37f),new Color(.67f,.58f,.40f),new Color(.25f,.23f,.22f),new Color(.51f,.42f,.28f)};
            var layers=new TerrainLayer[names.Length];
            for(int i=0;i<names.Length;i++)
            {
                string texturePath=AssetRoot+"/Textures/"+names[i]+".asset";
                Texture2D texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if(texture==null)
                {
                    const int size=128;texture=new Texture2D(size,size,TextureFormat.RGB24,true){name=names[i]+" Original Ground Color",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
                    var pixels=new Color[size*size];
                    for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                    {
                        float u=x/(size-1f),v=y/(size-1f),ox=10+i*7;
                        float a=Mathf.Lerp(Mathf.PerlinNoise(u*4+ox,v*4+13),Mathf.PerlinNoise((u-1)*4+ox,v*4+13),u);
                        float b=Mathf.Lerp(Mathf.PerlinNoise(u*4+ox,(v-1)*4+13),Mathf.PerlinNoise((u-1)*4+ox,(v-1)*4+13),u);
                        float n=Mathf.Lerp(a,b,v);
                        float grain=Mathf.Sin(u*Mathf.PI*32)*Mathf.Sin(v*Mathf.PI*28)*.012f;
                        pixels[y*size+x]=colors[i]*(.94f+n*.12f+grain);
                    }
                    texture.SetPixels(pixels);texture.Apply();AssetDatabase.CreateAsset(texture,texturePath);
                }
                string layerPath=AssetRoot+"/Layers/"+names[i]+".terrainlayer";
                var layer=AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
                if(layer==null){layer=new TerrainLayer();AssetDatabase.CreateAsset(layer,layerPath);}
                layer.diffuseTexture=texture;layer.tileSize=Vector2.one*(i==2?18:12);layer.metallic=0;layer.smoothness=0;
                layers[i]=layer;EditorUtility.SetDirty(layer);
            }
            return layers;
        }
        private static void Paint(TerrainData data,float ox,float oz) => WorldTerrainUpgrade.Paint(data,ox,oz);
        private static void BuildWater(Transform root,ArtAssetCatalog catalog,IslandTerrainResult result)
        {
            Transform water=Node("02 Ocean and Freshwater Lake",root,Vector3.zero);
            Material ocean=PlainMaterial(catalog,"IslandOcean",new Color(.10f,.34f,.43f,.86f),true);
            Material lake=PlainMaterial(catalog,"IslandLake",new Color(.12f,.40f,.42f,.80f),true);
            var sea=Node("Ocean Water Volume",water,new Vector3(0,-30,0));sea.gameObject.layer=11;
            result.Ocean=sea.gameObject.AddComponent<WaterVolume>();result.Ocean.Configure(Vector3.zero,new Vector3(2600,60,2600),0);
            var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);plane.name="Ocean Surface";plane.transform.SetParent(sea,false);
            plane.transform.position=Vector3.zero;plane.transform.localScale=new Vector3(400,1,400);plane.layer=11;
            UnityEngine.Object.DestroyImmediate(plane.GetComponent<Collider>());
            var renderer=plane.GetComponent<Renderer>();renderer.sharedMaterial=ocean;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            var lakeRoot=Node("Highland Lake Water Volume",water,new Vector3(230,LakeLevel-6,35));lakeRoot.gameObject.layer=11;
            result.Lake=lakeRoot.gameObject.AddComponent<WaterVolume>();result.Lake.Configure(Vector3.zero,new Vector3(330,12,240),LakeLevel);
            Mesh disk=AssetDatabase.LoadAssetAtPath<Mesh>(AssetRoot+"/Meshes/LakeSurface.asset");
            if(disk==null)
            {
                const int count=64;var vertices=new Vector3[count+1];var uv=new Vector2[count+1];var triangles=new int[count*3];
                uv[0]=Vector2.one*.5f;
                for(int i=0;i<count;i++)
                {
                    float a=i*Mathf.PI*2/count;vertices[i+1]=new Vector3(Mathf.Cos(a)*163,0,Mathf.Sin(a)*117);
                    uv[i+1]=new Vector2(Mathf.Cos(a)*.5f+.5f,Mathf.Sin(a)*.5f+.5f);
                    triangles[i*3]=0;triangles[i*3+1]=(i+1)%count+1;triangles[i*3+2]=i+1;
                }
                disk=new Mesh{name="Original Highland Lake Surface",vertices=vertices,uv=uv,triangles=triangles};disk.RecalculateNormals();disk.RecalculateBounds();
                AssetDatabase.CreateAsset(disk,AssetRoot+"/Meshes/LakeSurface.asset");
            }
            var lakeSurface=Node("Highland Lake Surface",lakeRoot,new Vector3(230,LakeLevel,35));lakeSurface.gameObject.layer=11;
            lakeSurface.gameObject.AddComponent<MeshFilter>().sharedMesh=disk;
            var lakeRenderer=lakeSurface.gameObject.AddComponent<MeshRenderer>();lakeRenderer.sharedMaterial=lake;
            lakeRenderer.shadowCastingMode=ShadowCastingMode.Off;lakeRenderer.receiveShadows=false;
        }
        internal static Material PlainMaterial(ArtAssetCatalog catalog,string name,Color color,bool transparent=false)
        {
            string path=AssetRoot+"/Materials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null)
            {
                material=new Material(transparent?catalog.WaterMaterial:catalog.DefaultToonMaterial){name=name};
                material.SetTexture("_BaseMap",null);material.SetColor("_BaseColor",color);material.enableInstancing=true;AssetDatabase.CreateAsset(material,path);
            }
            return material;
        }
    }
}
