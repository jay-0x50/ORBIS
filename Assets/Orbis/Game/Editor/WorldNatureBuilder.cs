using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Game.World;
using Orbis.M1;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Authored, reproducible scenery. Region ownership is separate from continuous species probabilities.</summary>
    public static class WorldNatureBuilder
    {
        public const string Root="Assets/Orbis/Game/World/Nature";
        public const string GeneratedName="Nature / Biome Blending";
        [Serializable] public sealed class SourceTexture {public int slice;public string name,path;}
        [Serializable] public sealed class Species {public string id,lod0Path,lod1Path,billboardPath;public float billboardWidth,billboardHeight;public float[] boundsUnityMin,boundsUnityMax;}
        [Serializable] public sealed class Manifest {public SourceTexture[] textures;public Species[] species,groundCover;public Species grass;}
        static readonly string[] Names={"CommonTree_3","Pine_5","DeadTree_5","TwistedTree_2","BirchTree_1","MapleTree_1","PalmTree_1"};
        // Art defaults: at least three silhouettes in every biome; warm foliage remains a small shared accent.
        // Rows follow M4RegionId Agnia/Teluna/Zephyr/Granite/Voltheim, columns follow Names above.
        static readonly float[,] SpeciesWeights={{0,.50f,.35f,0,0,.15f,0},{.32f,0,0,.08f,0,0,.60f},
            {.55f,0,0,0,.32f,.13f,0},{0,.68f,.05f,0,.27f,0,0},{0,.45f,0,.15f,.40f,0,0}};
        static readonly float[] Heights={11f,13f,11f,13f,9f,8.5f,10f};

        [MenuItem("Orbis/Development/Legacy/World Art/02 Biome Vegetation")]
        public static void Step2()
        {
            FieldSceneAuthoring.RequireGeneratedEditingAllowed();
            var scene=EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath,OpenSceneMode.Single);
            var world=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<M4SceneBootstrap>(true)).Single();
            if(world.GetComponent<WorldRegionStreamer>()==null)throw new InvalidOperationException("Complete world Step 1 first.");
            var terrains=world.GetComponentsInChildren<Terrain>();
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Root+"/NatureModelManifest.json"));
            PrepareImports(manifest);
            foreach(string folder in new[]{"Materials","Prefabs","Meshes","Data"})Directory.CreateDirectory(Root+"/"+folder);
            AssetDatabase.Refresh();
            var array=BuildTextureArray(manifest);var ramp=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Orbis/Game/LookDev/Resources/LookDev/CelRamp.asset");
            if(ramp==null||ramp.width!=3)throw new InvalidOperationException("The shared 3-band ramp is required.");
            Material trees=FoliageMaterial("Canopy",array,ramp),grassMaterial=FoliageMaterial("MeadowGrass",array,ramp);
            grassMaterial.SetFloat("_GrassBend",1);grassMaterial.SetFloat("_WindStrength",.10f);grassMaterial.SetFloat("_FlutterStrength",.7f);
            var prefabs=new GameObject[7];
            for(int i=0;i<7;i++)prefabs[i]=TreePrefab(manifest.species.Single(s=>s.id==Names[i]),trees,array,ramp);
            Mesh grassMesh=BakedMesh(manifest.grass.lod0Path,manifest.grass.id+"_LOD0");
            WorldGroundBuilder.Apply(terrains);
            var fern=manifest.groundCover.Single(s=>s.id=="Fern_1");
            var fernPrefab=GroundPrefab(fern,trees);
            var rocks=manifest.groundCover.Where(s=>s.id.StartsWith("Rock_")).Select(s=>GroundPrefab(s,WorldGroundBuilder.RockMaterial)).ToArray();
            var environment=new Scene[5];var roots=new Transform[5];
            for(int i=0;i<5;i++)
            {
                environment[i]=EditorSceneManager.OpenScene(WorldStreamingBuilder.ScenePath((M4RegionId)i),OpenSceneMode.Additive);
                var container=environment[i].GetRootGameObjects().Single().transform;
                foreach(Transform child in container.Cast<Transform>().ToArray())
                    if(child.name==GeneratedName||child.name.Contains("Flower Patch")||child.name.StartsWith("Starter "))Object.DestroyImmediate(child.gameObject);
                roots[i]=Node(GeneratedName,container);
            }
            foreach(var t in world.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Persistent nature collision"||t.name.StartsWith("Starter ")).ToArray())
                if(t!=null)Object.DestroyImmediate(t.gameObject);
            var collisions=Node("Persistent nature collision",world.transform);
            foreach(var terrain in terrains)
            {
                var data=terrain.terrainData;data.treeInstances=Array.Empty<TreeInstance>();data.treePrototypes=Array.Empty<TreePrototype>();
                data.detailPrototypes=Array.Empty<DetailPrototype>();terrain.detailObjectDensity=0;terrain.drawInstanced=true;
                EditorUtility.SetDirty(data);EditorUtility.SetDirty(terrain);
            }
            var sampler=new Sampler(terrains);var exclusions=Exclusions(world);
            var counts=new int[5,7];var random=new System.Random(9112602);int total=0,groundCount=0;
            // 13m jittered candidates, patchy glades and broad transition probabilities preserve long sightlines.
            for(float z=-962;z<962;z+=13)for(float x=-962;x<962;x+=13)
            {
                float wx=x+Jitter(random,5),wz=z+Jitter(random,5);float h=sampler.Height(wx,wz);
                if(!Plantable(wx,wz,h,sampler,exclusions,11,true))continue;
                float patch=Mathf.PerlinNoise(wx*.008f+31,wz*.008f+19);
                var weights=WorldBiome.Sample(wx,wz);
                float threshold=.33f+weights.Agnia*.13f+weights.Zephyr*.12f;
                if(patch<threshold||random.NextDouble()>.66)continue;
                int species=SelectSpecies(weights,(float)random.NextDouble());int owner=(int)IslandTerrainBuilder.BiomeAt(wx,wz);
                float scale=Heights[species]/manifest.species.Single(s=>s.id==Names[species]).boundsUnityMax[1]*(.78f+(float)random.NextDouble()*.42f);
                Vector3 p=new Vector3(wx,h-.04f,wz);var tree=Place(prefabs[species],roots[owner],p,Vector3.one*scale,(float)random.NextDouble()*360);
                tree.name=Names[species]+" / "+total;counts[owner,species]++;total++;
                var trunk=Node("Trunk / "+total,collisions);trunk.gameObject.layer=8;trunk.position=p;
                var c=trunk.gameObject.AddComponent<CapsuleCollider>();c.radius=species==6?.23f:.30f;c.height=3.3f;c.center=Vector3.up*1.65f;
                // Understory reinforces tree clusters, with clear paths and core encounters kept accessible.
                if(random.NextDouble()<.40)
                {
                    Vector3 q=p+new Vector3(2.3f,0,-1.4f);q.y=sampler.Height(q.x,q.z);
                    Place(fernPrefab,roots[owner],q,Vector3.one*(.6f+(float)random.NextDouble()*.5f),(float)random.NextDouble()*360);groundCount++;
                }
                if(random.NextDouble()<.14)
                {
                    Vector3 q=p+new Vector3(-2.2f,-.1f,2.4f);q.y=sampler.Height(q.x,q.z)-.15f;
                    Place(rocks[random.Next(rocks.Length)],roots[owner],q,Vector3.one*(.55f+(float)random.NextDouble()*.85f),(float)random.NextDouble()*360);groundCount++;
                }
            }
            var placements=new List<WorldGrassPlacement>[5];for(int i=0;i<5;i++)placements[i]=new List<WorldGrassPlacement>();
            // 0.95m candidates only in continuous meadow patches and path shoulders. Around 0.5m-tall tufts.
            // A single shared 155-triangle mesh, 32m cells and 100m submission range avoid per-tuft GameObjects.
            for(float z=-930;z<930;z+=.95f)for(float x=-930;x<930;x+=.95f)
            {
                float wx=x+Jitter(random,.39f),wz=z+Jitter(random,.39f);
                float patch=Mathf.PerlinNoise(wx*.023f+57,wz*.023f+93);
                if(patch<.46f||random.NextDouble()>.80)continue;
                float h=sampler.Height(wx,wz);
                if(!Plantable(wx,wz,h,sampler,exclusions,4,false))continue;
                var weights=WorldBiome.Sample(wx,wz);if(h>120&&random.NextDouble()<weights.Agnia*.85f)continue;
                int owner=(int)IslandTerrainBuilder.BiomeAt(wx,wz);float s=.28f+(float)random.NextDouble()*.15f;
                placements[owner].Add(new WorldGrassPlacement{Position=new Vector3(wx,h-.025f,wz),Yaw=(float)random.NextDouble()*360,Scale=new Vector3(1.3f,s,1.3f)});
            }
            for(int i=0;i<5;i++)
            {
                string path=Root+"/Data/Grass_"+(M4RegionId)i+".asset";
                var data=AssetDatabase.LoadAssetAtPath<WorldGrassData>(path);
                if(data==null){data=ScriptableObject.CreateInstance<WorldGrassData>();AssetDatabase.CreateAsset(data,path);}
                data.SetInstances(placements[i],32);EditorUtility.SetDirty(data);
                var field=Node("Instanced meadow",roots[i]).gameObject.AddComponent<WorldGrassField>();field.gameObject.layer=0;
                field.Configure(grassMesh,grassMaterial,data);
                EditorSceneManager.MarkSceneDirty(environment[i]);EditorSceneManager.SaveScene(environment[i]);
            }
            WorldStreamingBuilder.RefreshEnvironmentBounds();
            var wind=world.GetComponent<WorldWind>();
            if(wind==null)
            {
                var windRoot=Node("Continental Wind",world.transform);windRoot.rotation=Quaternion.Euler(0,38,0);
                var zone=windRoot.gameObject.AddComponent<WindZone>();zone.mode=WindZoneMode.Directional;
                zone.windMain=.65f;zone.windTurbulence=.45f;zone.windPulseMagnitude=.16f;zone.windPulseFrequency=.22f;
                wind=world.gameObject.AddComponent<WorldWind>();wind.Configure(zone);
            }
            SceneManager.SetActiveScene(scene);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            foreach(var loaded in environment)EditorSceneManager.CloseScene(loaded,true);
            WriteAudit(counts,total,groundCount,placements);
            Debug.Log("ORBIS_WORLD_STEP2: "+total+" authored trees, "+placements.Sum(x=>x.Count)+" instanced grass placements, blended species, WindZone and moss layers.");
        }
        public static int SelectSpecies(WorldBiomeWeights biome,float random)
        {
            float sum=0;
            for(int s=0;s<7;s++){float probability=0;for(int r=0;r<5;r++)probability+=biome[(M4RegionId)r]*SpeciesWeights[r,s];sum+=probability;if(random<=sum||s==6)return s;}
            return 6;
        }
        static bool Plantable(float x,float z,float height,Sampler sampler,List<Vector4> exclusions,float roadMargin,bool tree)
        {
            if(height<2.8f||height>198||sampler.Slope(x,z)>(tree?32:37))return false;
            if(IslandTerrainBuilder.RoadDistance(x,z,out _)<roadMargin)return false;
            float lake=Mathf.Pow((x-230)/125,2)+Mathf.Pow((z-35)/90,2);if(height<18.8f&&lake<1.35f)return false;
            if(tree&&(IslandTerrainBuilder.SiteDistance(x,z)<84||x>-205&&x<-50&&z>-355&&z<-120||x>320&&x<630&&z>355&&z<615))return false;
            foreach(var p in exclusions)if((x-p.x)*(x-p.x)+(z-p.z)*(z-p.z)<Mathf.Pow(p.w+(tree?5:0),2))return false;
            return true;
        }
        static List<Vector4> Exclusions(M4SceneBootstrap world)
        {
            var result=new List<Vector4>();
            foreach(var region in world.GetComponentsInChildren<M4AuthoredRegion>(true))
            {
                foreach(var actor in region.GetComponentsInChildren<ElementalActor>(true)){var p=actor.transform.position;result.Add(new Vector4(p.x,p.y,p.z,actor.name.Contains("Boss")?12:3));}
                foreach(var t in new[]{region.Spawn,region.Npc,region.Portal,region.Chest,region.ChallengeEntry})if(t!=null){var p=t.position;result.Add(new Vector4(p.x,p.y,p.z,3));}
                var boss=region.BossObject.transform.position;result.Add(new Vector4(boss.x,boss.y,boss.z,13));
                var center=region.Targets.Select(a=>a.transform.position).Aggregate(Vector3.zero,(a,b)=>a+b)/3;result.Add(new Vector4(center.x,center.y,center.z,11));
            }
            return result;
        }
        sealed class Sampler
        {
            readonly Terrain[] tiles;public Sampler(Terrain[] values)=>tiles=values;
            Terrain Tile(float x,float z)=>tiles.First(t=>x>=t.transform.position.x&&x<=t.transform.position.x+1000&&z>=t.transform.position.z&&z<=t.transform.position.z+1000);
            public float Height(float x,float z){var t=Tile(x,z);return t.SampleHeight(new Vector3(x,0,z))+t.transform.position.y;}
            public float Slope(float x,float z){var t=Tile(x,z);return t.terrainData.GetSteepness((x-t.transform.position.x)/1000,(z-t.transform.position.z)/1000);}
        }
        static void PrepareImports(Manifest m)
        {
            foreach(var item in m.species.Concat(m.groundCover))foreach(string path in new[]{item.lod0Path,item.lod1Path})
            {
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.importAnimation=false;
                importer.animationType=ModelImporterAnimationType.None;importer.isReadable=true;importer.generateSecondaryUV=false;
                importer.bakeAxisConversion=true;importer.meshCompression=ModelImporterMeshCompression.Off;importer.SaveAndReimport();
            }
            foreach(string path in m.textures.Select(x=>x.path).Concat(m.species.Select(x=>x.billboardPath)))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;
                importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.isReadable=true;
                importer.maxTextureSize=path.Contains("Billboard")?1024:512;importer.npotScale=TextureImporterNPOTScale.None;
                importer.wrapMode=path.Contains("Billboard")?TextureWrapMode.Clamp:TextureWrapMode.Repeat;
                importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            }
        }
        static Texture2DArray BuildTextureArray(Manifest manifest)
        {
            var array=new Texture2DArray(512,512,manifest.textures.Length,TextureFormat.BC7,true,false){name="Shared Nature Albedo",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=4};
            foreach(var layer in manifest.textures)
            {
                var source=AssetDatabase.LoadAssetAtPath<Texture2D>(layer.path);var temp=new Texture2D(512,512,TextureFormat.RGBA32,true,false);
                temp.SetPixels32(source.GetPixels32());temp.Apply();EditorUtility.CompressTexture(temp,TextureFormat.BC7,TextureCompressionQuality.Best);
                byte[] bytes=temp.GetRawTextureData();int offset=0;
                for(int mip=0;mip<temp.mipmapCount;mip++){array.SetPixelData(bytes,mip,layer.slice,offset);int n=Mathf.Max(1,512>>mip);offset+=Mathf.Max(1,(n+3)/4)*Mathf.Max(1,(n+3)/4)*16;}
                Object.DestroyImmediate(temp);
            }
            array.Apply(false,false);string path=Root+"/Textures/NatureAlbedoArray.asset";var existing=AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
            if(existing!=null){EditorUtility.CopySerialized(array,existing);Object.DestroyImmediate(array);array=existing;EditorUtility.SetDirty(array);}else AssetDatabase.CreateAsset(array,path);
            return array;
        }
        static Material FoliageMaterial(string name,Texture2DArray array,Texture2D ramp)
        {
            string path=Root+"/Materials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader=Shader.Find("Orbis/World/FoliageToon");if(shader==null)throw new InvalidOperationException("Import FoliageToon graph first.");
            if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}else material.shader=shader;
            material.SetTexture("_BaseArray",array);material.SetTexture("_Ramp",ramp);material.SetColor("_BaseColor",Color.white);
            material.SetColor("_ShadowColor",new Color(.61f,.69f,.77f));material.SetFloat("_Cutoff",.25f);
            material.SetFloat("_WindStrength",.22f);material.SetFloat("_FlutterStrength",1);material.SetFloat("_BillboardMode",0);material.SetFloat("_GrassBend",0);
            material.SetFloat("_Surface",0);material.SetFloat("_ZWrite",1);material.SetFloat("_Cull",0);material.SetFloat("_AlphaClip",1);material.SetFloat("_AlphaToMask",1);
            material.EnableKeyword("_ALPHATEST_ON");material.renderQueue=2450;material.enableInstancing=true;EditorUtility.SetDirty(material);return material;
        }
        static GameObject TreePrefab(Species s,Material canopy,Texture2DArray array,Texture2D ramp)
        {
            var root=new GameObject(s.id);
            try
            {
                var near=MeshNode("LOD0",root.transform,BakedMesh(s.lod0Path,s.id+"_LOD0"),canopy);
                var mid=MeshNode("LOD1",root.transform,BakedMesh(s.lod1Path,s.id+"_LOD1"),canopy);
                var material=FoliageMaterial(s.id+"_Billboard",array,ramp);material.SetFloat("_BillboardMode",1);material.SetFloat("_WindStrength",.12f);
                material.SetTexture("_BillboardAtlas",AssetDatabase.LoadAssetAtPath<Texture2D>(s.billboardPath));
                var far=MeshNode("LOD2 / 8-view Billboard",root.transform,BillboardMesh(s),material);far.shadowCastingMode=ShadowCastingMode.Off;
                var lod=root.AddComponent<LODGroup>();lod.fadeMode=LODFadeMode.CrossFade;lod.animateCrossFading=true;
                lod.SetLODs(new[]{new LOD(.16f,new Renderer[]{near}),new LOD(.045f,new Renderer[]{mid}),new LOD(.014f,new Renderer[]{far})});lod.RecalculateBounds();
                return PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/"+s.id+".prefab");
            }
            finally{Object.DestroyImmediate(root);}
        }
        static GameObject GroundPrefab(Species s,Material material)
        {
            var root=new GameObject(s.id);
            try
            {
                var near=MeshNode("LOD0",root.transform,BakedMesh(s.lod0Path,s.id+"_LOD0"),material);
                var far=MeshNode("LOD1",root.transform,BakedMesh(s.lod1Path,s.id+"_LOD1"),material);
                var lod=root.AddComponent<LODGroup>();lod.fadeMode=LODFadeMode.CrossFade;
                lod.SetLODs(new[]{new LOD(.08f,new Renderer[]{near}),new LOD(.012f,new Renderer[]{far})});lod.RecalculateBounds();
                return PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/"+s.id+".prefab");
            }
            finally{Object.DestroyImmediate(root);}
        }
        static Mesh BakedMesh(string path,string name)
        {
            var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);var filter=go.GetComponentsInChildren<MeshFilter>(true).Single();
            var source=filter.sharedMesh;var mesh=Object.Instantiate(source);mesh.name=name;
            var matrix=go.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
            var vertices=mesh.vertices;for(int i=0;i<vertices.Length;i++)vertices[i]=matrix.MultiplyPoint3x4(vertices[i]);mesh.vertices=vertices;
            var normals=mesh.normals;for(int i=0;i<normals.Length;i++)normals[i]=matrix.inverse.transpose.MultiplyVector(normals[i]).normalized;mesh.normals=normals;
            mesh.RecalculateBounds();var b=mesh.bounds;b.Expand(2.4f);mesh.bounds=b;return SaveMesh(mesh,Root+"/Meshes/"+name+".asset");
        }
        static Mesh BillboardMesh(Species s)
        {
            float w=s.billboardWidth/2,h=s.billboardHeight;
            var mesh=new Mesh{name=s.id+"_Billboard"};mesh.vertices=new[]{new Vector3(-w,0,0),new Vector3(w,0,0),new Vector3(-w,h,0),new Vector3(w,h,0)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.uv2=new[]{Vector2.zero,Vector2.zero,Vector2.up,Vector2.up};
            mesh.uv3=new[]{Vector2.right,Vector2.right,Vector2.one,Vector2.one};mesh.normals=Enumerable.Repeat(Vector3.forward,4).ToArray();mesh.triangles=new[]{0,1,2,2,1,3};
            mesh.bounds=new Bounds(Vector3.up*h/2,new Vector3(w*2+2.4f,h+2.4f,w*2+2.4f));return SaveMesh(mesh,Root+"/Meshes/"+s.id+"_Billboard.asset");
        }
        static Mesh SaveMesh(Mesh mesh,string path){var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;}
        static MeshRenderer MeshNode(string name,Transform parent,Mesh mesh,Material material){var t=Node(name,parent);t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.lightProbeUsage=LightProbeUsage.Off;return r;}
        static Transform Node(string name,Transform parent){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        static GameObject Place(GameObject prefab,Transform parent,Vector3 position,Vector3 scale,float yaw){var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));go.transform.localScale=scale;PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);return go;}
        static float Jitter(System.Random random,float extent)=>((float)random.NextDouble()*2-1)*extent;
        static void WriteAudit(int[,] counts,int trees,int ground,List<WorldGrassPlacement>[] grass)
        {
            var lines=new List<string>{"# World vegetation placement",$"Trees {trees}; understory/rocks {ground}; grass {grass.Sum(x=>x.Count)}.","WindZone-driven Shader Graph; native tree LODGroups + 8-view billboard; grass 32m cells / 100m range / <=256 instances per draw.",""};
            for(int r=0;r<5;r++){lines.Add(((M4RegionId)r)+": "+string.Join(", ",Names.Select((s,i)=>s+"="+counts[r,i]))+"; grass="+grass[r].Count);if(Enumerable.Range(0,7).Count(i=>counts[r,i]>0)<3)throw new InvalidOperationException("Biome lacks three tree silhouettes.");}
            Directory.CreateDirectory("TestResults/WorldDev");File.WriteAllLines("TestResults/WorldDev/World02_Placement.txt",lines);
        }
    }
}
