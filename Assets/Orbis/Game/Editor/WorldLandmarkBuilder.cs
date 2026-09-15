using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Art;
using Orbis.Game.World;
using Orbis.M2;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Original regional compositions. Visuals stream; physical traversal and authored encounters remain resident.</summary>
    public static class WorldLandmarkBuilder
    {
        public const string Root="Assets/Orbis/Game/World/Architecture";
        const string Generated="Landmarks / Continental Routes";
        static readonly string[] ModelNames={"House_Cottage","House_Merchant","House_Workshop","Windmill","StormSpire"};
        static Terrain[] terrain;
        static Transform[] environments,collisions;
        static Material stone,wood,brass;
        static List<Vector4> clear;
        static List<string> audit;
        static ArtAssetCatalog catalog;
        static int meshNumber;
        static bool terracesAuthored;
        static List<Vector4> contentKeepout;
        static List<Vector3> buildings;

        [MenuItem("Orbis/Development/Legacy/World Art/03 Landmarks and Discovery Routes")]
        public static void Step3()
        {
            FieldSceneAuthoring.RequireGeneratedEditingAllowed();
            var scene=EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath,OpenSceneMode.Single);
            var world=Object.FindAnyObjectByType<M4SceneBootstrap>();
            if(world==null||world.GetComponent<WorldWind>()==null)throw new InvalidOperationException("Complete world vegetation first.");
            // Check the separately authored model contract before touching saved scenery.
            foreach(string name in ModelNames)for(int lod=0;lod<2;lod++)
                if(!File.Exists(Root+"/Models/"+name+"_LOD"+lod+".fbx"))throw new FileNotFoundException(name+" LOD "+lod);
            PrepareArchitecture();
            terrain=world.GetComponentsInChildren<Terrain>();
            terracesAuthored=world.transform.Cast<Transform>().Any(t=>t.name.StartsWith("Persistent landmarks / ",StringComparison.Ordinal));
            catalog=Resources.Load<ArtAssetCatalog>("Art/ArtAssetCatalog");
            if(catalog==null)catalog=AssetDatabase.LoadAssetAtPath<ArtAssetCatalog>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:ArtAssetCatalog")[0]));
            stone=WorldGroundBuilder.RockMaterial;
            wood=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Architecture_Timber.mat");
            brass=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Architecture_Brass.mat");
            clear=new List<Vector4>();audit=new List<string>();meshNumber=0;
            environments=new Transform[5];collisions=new Transform[5];var scenes=new Scene[5];
            for(int i=0;i<5;i++)
            {
                scenes[i]=EditorSceneManager.OpenScene(WorldStreamingBuilder.ScenePath((M4RegionId)i),OpenSceneMode.Additive);
                var root=scenes[i].GetRootGameObjects().Single().transform;
                foreach(Transform child in root.Cast<Transform>().ToArray())
                    if(child.name!=WorldNatureBuilder.GeneratedName)Object.DestroyImmediate(child.gameObject);
                environments[i]=Node(Generated,root,Vector3.zero);
                foreach(Transform child in world.transform.Cast<Transform>().ToArray())
                    if(child.name=="Persistent traversal / "+(M4RegionId)i||child.name=="Persistent landmarks / "+(M4RegionId)i)Object.DestroyImmediate(child.gameObject);
                collisions[i]=Node("Persistent landmarks / "+(M4RegionId)i,world.transform,Vector3.zero);
            }
            clear.AddRange(WorldContentRedistribution.Apply(world));
            contentKeepout=new List<Vector4>();buildings=new List<Vector3>();
            foreach(var binding in world.GetComponentsInChildren<M4AuthoredRegion>())
            {
                Vector3 shrine=binding.Statues.Select(a=>a.transform.position).Aggregate(Vector3.zero,(a,b)=>a+b)/3;
                Vector3 trial=binding.Targets.Select(a=>a.transform.position).Aggregate(Vector3.zero,(a,b)=>a+b)/3;
                contentKeepout.Add(new Vector4(shrine.x,shrine.y,shrine.z,22));contentKeepout.Add(new Vector4(trial.x,trial.y,trial.z,26));
                Vector3 boss=binding.BossObject.transform.position;contentKeepout.Add(new Vector4(boss.x,boss.y,boss.z,24));
            }
            BuildVillage();PaintVillagePaths();BuildRegionalStructures();BuildTrailLandmarks();
            clear.AddRange(WorldWaterfallBuilder.Build(environments[(int)M4RegionId.Zephyr],collisions[(int)M4RegionId.Zephyr],terrain));
            ClearVegetation(world,clear);
            foreach(var binding in world.GetComponentsInChildren<M4AuthoredRegion>())binding.Validate();
            foreach(var environmentScene in scenes){EditorSceneManager.MarkSceneDirty(environmentScene);EditorSceneManager.SaveScene(environmentScene);}
            WorldStreamingBuilder.RefreshEnvironmentBounds();
            SceneManager.SetActiveScene(scene);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            foreach(var environmentScene in scenes)EditorSceneManager.CloseScene(environmentScene,true);
            Directory.CreateDirectory("TestResults/WorldDev");
            File.WriteAllLines("TestResults/WorldDev/World03_Landmarks.txt",audit);
            Debug.Log("ORBIS_WORLD_STEP3: "+audit.Count+" landmark/placement records, retained five authored content sets and persistent traversal.");
        }

        static void PrepareArchitecture()
        {
            Directory.CreateDirectory(Root+"/Materials");Directory.CreateDirectory(Root+"/Prefabs");Directory.CreateDirectory(Root+"/Meshes");AssetDatabase.Refresh();
            string[] names={"Ivory","Timber","Slate","Stone","Brass","Glass"};
            // The existing character's sampled ivory/navy/gold family; timber and stone are quieter environment defaults.
            string[] colors={"#E2D9D8","#665344","#556484","#ABA89A","#D6B799","#7DD0D7"};
            var materials=new Dictionary<string,Material>();
            for(int i=0;i<names.Length;i++)
            {
                string name="Architecture_"+names[i],path=Root+"/Materials/"+name+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(material==null){material=new Material(Shader.Find("Orbis/Art/UnifiedToon")){name=name};AssetDatabase.CreateAsset(material,path);}
                ColorUtility.TryParseHtmlString(colors[i],out Color color);material.SetColor("_BaseColor",color);
                material.SetColor("_ShadowColor",new Color(.68f,.72f,.78f));material.SetFloat("_AmbientFill",.22f);
                material.enableInstancing=true;EditorUtility.SetDirty(material);materials.Add(name,material);
            }
            foreach(string name in ModelNames)
            {
                var prefab=new GameObject(name);var levels=new LOD[2];
                try
                {
                    for(int lod=0;lod<2;lod++)
                    {
                        string path=Root+"/Models/"+name+"_LOD"+lod+".fbx";
                        var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                        importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
                        importer.importAnimation=false;importer.animationType=ModelImporterAnimationType.None;
                        importer.isReadable=true;importer.bakeAxisConversion=true;importer.SaveAndReimport();
                        var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),prefab.transform);
                        instance.name="LOD"+lod;
                        // The native FBX front imports as -Z. Normalize the usable prefab to +Z.
                        instance.transform.localRotation=Quaternion.Euler(0,180,0);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                        foreach(var renderer in instance.GetComponentsInChildren<MeshRenderer>())
                        {
                            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>
                            {
                                string key=materials.Keys.FirstOrDefault(k=>m!=null&&m.name.StartsWith(k,StringComparison.Ordinal));
                                if(key==null)throw new InvalidOperationException("Unknown architecture material "+m?.name+" in "+path);
                                return materials[key];
                            }).ToArray();
                            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                            if(renderer.name.StartsWith("Rotor",StringComparison.Ordinal))renderer.gameObject.AddComponent<WorldWindmill>();
                        }
                        levels[lod]=new LOD(lod==0?.085f:.007f,instance.GetComponentsInChildren<Renderer>());
                    }
                    // Step 3's inherited building shader has no dither variant. Use an honest single LOD
                    // until the world surface shader in Step 4 supplies its own cross-fade implementation.
                    var group=prefab.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.None;
                    group.SetLODs(levels);group.RecalculateBounds();PrefabUtility.SaveAsPrefabAsset(prefab,Root+"/Prefabs/"+name+".prefab");
                }
                finally{Object.DestroyImmediate(prefab);}
            }
        }

        static void BuildVillage()
        {
            Vector2[] houses={new Vector2(-100,-245),new Vector2(-120,-260),new Vector2(-145,-235),new Vector2(-110,-285),
                new Vector2(-155,-285),new Vector2(-175,-250),new Vector2(-133,-315),new Vector2(-180,-305),new Vector2(-195,-215)};
            for(int i=0;i<houses.Length;i++)
            {
                var p=Ground(houses[i]);var owner=M4RegionId.Zephyr;
                var house=Architecture(ModelNames[i%3],"Windward home "+(i+1),owner,p,i%2==0?80:35);
                // Footings follow the actual slope; doors remain near terrain height rather than hovering with a stretched tower.
                AddFoundation(house,owner);
                SmallProp("barrel","Rain barrel",owner,house.transform.TransformPoint(new Vector3(3.4f,0,-2.6f)),1.0f);
                SmallProp("crate","Market provisions",owner,house.transform.TransformPoint(new Vector3(-3.8f,0,-2.4f)),.7f);
            }
            foreach(var entry in new[]{new Vector3(-95,0,-325),new Vector3(-165,0,-165),new Vector3(95,0,-280)})
                AddFoundation(Architecture("Windmill","Zephyr compass windmill",M4RegionId.Zephyr,Ground(new Vector2(entry.x,entry.z)),30),M4RegionId.Zephyr);
            // A path-side stone bridge spans a shallow decorative dry runnel rather than an inaccessible raised cube.
            ArchLandmark(M4RegionId.Zephyr,Ground(new Vector2(-79,-252)),7,3.3f,5,90,"Windward stone crossing");
        }

        static void BuildRegionalStructures()
        {
            // Lore anchors retain the original five factions; natural foliage may mix around them.
            foreach(var p in new[]{new Vector2(550,470),new Vector2(570,520),new Vector2(490,570),new Vector2(380,550),new Vector2(590,390),new Vector2(350,525)})
                AddFoundation(Architecture("StormSpire","Voltheim conductor spire",M4RegionId.Voltheim,Ground(p),25),M4RegionId.Voltheim);
            AddFoundation(Architecture("House_Workshop","Agnia blacksmith lodge",M4RegionId.Agnia,Ground(new Vector2(-657,310)),70),M4RegionId.Agnia);
            AddFoundation(Architecture("House_Workshop","Granite survey workshop",M4RegionId.Granite,Ground(new Vector2(-585,-355)),110),M4RegionId.Granite);
            AddFoundation(Architecture("House_Merchant","Teluna navigator lodge",M4RegionId.Teluna,Ground(new Vector2(640,-460)),180),M4RegionId.Teluna);
            ArchLandmark(M4RegionId.Granite,Ground(new Vector2(-625,-390)),12,10,8,90,"Granite mine mouth");
            for(int i=0;i<4;i++)Rock(M4RegionId.Granite,Ground(new Vector2(-625+(i-1.5f)*8,-380)),new Vector3(12,12+i%2*5,9),i*39,"Mine cliff");
            foreach(var p in new[]{new Vector2(620,-245),new Vector2(695,-310),new Vector2(710,-535)})
                ArchLandmark(M4RegionId.Teluna,Ground(p),14,11,7,65,"Teluna sea-worn arch");
            // Crater rim uses irregular textured boulders; each bottom is embedded into its own terrain sample.
            for(int i=0;i<14;i++)
            {
                float a=i*Mathf.PI*2/14;var p=Ground(new Vector2(-610+Mathf.Cos(a)*87,535+Mathf.Sin(a)*87));
                Rock(M4RegionId.Agnia,p,new Vector3(18,9+i%3*3,12),i*37,"Volcanic rim");
            }
            // Eastern shore decks follow a single flat water height where present, then step back to land.
            for(int i=0;i<5;i++)
            {
                Vector3 p=Ground(new Vector2(710+i*7,-425));p.y=Mathf.Max(.9f,p.y);
                var deck=Node("Navigator timber landing "+i,environments[(int)M4RegionId.Teluna],p);
                BoxMesh("Landing",deck,new Vector3(0,.1f,0),new Vector3(7,.3f,4),wood);
                for(int k=0;k<4;k++)BoxMesh("Dock pile",deck,new Vector3(k<2?-2.6f:2.6f,-1,k%2==0?-1.5f:1.5f),new Vector3(.3f,3,.3f),wood);
                MergeStaticMeshes(deck);CopyMeshCollision(deck,collisions[(int)M4RegionId.Teluna]);Clear(p,6);
            }
        }

        static void BuildTrailLandmarks()
        {
            var route=IslandTerrainBuilder.RoutePoints();int number=0;
            for(int i=1;i<route.Length-1;i+=2)
            {
                Vector3 p=route[i],forward=(route[i+1]-route[i-1]).normalized;
                Vector3 side=new Vector3(forward.z,0,-forward.x)*(number%2==0?18:-18);
                p=Ground(new Vector2(p.x+side.x,p.z+side.z));
                if(IslandTerrainBuilder.SiteDistance(p.x,p.z)<86)continue;
                var region=IslandTerrainBuilder.BiomeAt(p.x,p.z);
                if(number%3==0)ArchLandmark(region,p,5.5f,7.5f,2.6f,Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg,"Trail compass arch "+number);
                else
                {
                    Rock(region,p,new Vector3(11,8+number%3*2,8),number*37,"Climbable overlook "+number);
                    SmallProp("obelisk","Survey cairn",region,p+new Vector3(6,0,0),4.3f);
                }
                SmallProp("board","Trail wayfinding",region,p-side*.6f,1.8f);
                number++;
            }
        }

        static GameObject Architecture(string model,string label,M4RegionId owner,Vector3 p,float yaw)
        {
            p=BuildingSite(p);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/"+model+".prefab");
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,environments[(int)owner]);
            instance.name=label;instance.transform.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));
            FitBuildingGround(p);
            var marker=instance.AddComponent<WorldLandmark>();marker.Label=label;marker.Region=owner;
            marker.LocalLookPoint=Vector3.up*(model=="StormSpire"?23:model=="Windmill"?15:4);
            foreach(var component in instance.GetComponentsInChildren<Transform>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            CopyMeshCollision(instance.transform.Find("LOD0"),collisions[(int)owner]);
            Clear(p,13);audit.Add(label+" / "+owner+" / "+p.ToString("F2"));return instance;
        }
        static void AddFoundation(GameObject building,M4RegionId owner)
        {
            Bounds bounds=BoundsOf(building.transform.Find("LOD0"));Vector3 p=building.transform.position;
            float lowest=p.y,highest=p.y;
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
            {float y=Ground(new Vector2(p.x+x*3.7f,p.z+z*3.2f)).y;lowest=Mathf.Min(lowest,y);highest=Mathf.Max(highest,y);}
            float depth=Mathf.Max(.35f,p.y-lowest+.6f);
            var footing=Node("Fitted stone footing",environments[(int)owner],p);
            footing.rotation=building.transform.rotation;
            BoxMesh("Foundation",footing,new Vector3(0,-depth*.5f,0),new Vector3(Mathf.Min(8.7f,bounds.size.x),depth,Mathf.Min(8.7f,bounds.size.z)),stone);
            // The architectural body already contains fitted porch steps and their real mesh collision.
            MergeStaticMeshes(footing);CopyMeshCollision(footing,collisions[(int)owner]);
        }
        static void Rock(M4RegionId owner,Vector3 p,Vector3 size,float yaw,string label)
        {
            if(ContentOverlap(p,Mathf.Max(size.x,size.z)*.6f))return;
            string name="Rock_Medium_"+(meshNumber%3+1);var source=AssetDatabase.LoadAssetAtPath<GameObject>(WorldNatureBuilder.Root+"/Prefabs/"+name+".prefab");
            if(source==null)throw new InvalidOperationException(name+" prefab missing.");
            var go=(GameObject)PrefabUtility.InstantiatePrefab(source,environments[(int)owner]);go.name=label;
            Bounds bounds=GeometryBounds(go.transform);go.transform.localScale=new Vector3(size.x/bounds.size.x,size.y/bounds.size.y,size.z/bounds.size.z);
            go.transform.rotation=Quaternion.Euler(0,yaw,0);
            // Broad outcrops must emerge from the slope, not balance on one irregular lowest vertex.
            go.transform.position=p-Vector3.up*(size.y*.38f)-go.transform.rotation*Vector3.Scale(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z),go.transform.localScale);
            foreach(var t in go.GetComponentsInChildren<Transform>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            var marker=go.AddComponent<WorldLandmark>();marker.Label=label;marker.Region=owner;marker.LocalLookPoint=Vector3.up*(bounds.max.y*.85f);
            var low=go.transform.Find("LOD0")??go.transform;CopyMeshCollision(low,collisions[(int)owner]);
            Clear(p,Mathf.Max(size.x,size.z)*.6f);audit.Add(label+" / "+owner+" / "+p.ToString("F2"));meshNumber++;
        }
        static void SmallProp(string key,string label,M4RegionId owner,Vector3 p,float height)
        {
            p.y=Ground(new Vector2(p.x,p.z)).y;
            var go=(GameObject)PrefabUtility.InstantiatePrefab(catalog.Model(key),environments[(int)owner]);go.name=label;
            Bounds bounds=BoundsOf(go.transform);float scale=height/Mathf.Max(.01f,bounds.size.y);
            go.transform.localScale=Vector3.one*scale;go.transform.position=p-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale;
            foreach(var t in go.GetComponentsInChildren<Transform>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            Clear(p,1.2f);
        }
        static void ArchLandmark(M4RegionId owner,Vector3 p,float width,float height,float depth,float yaw,string label)
        {
            if(ContentOverlap(p,width*.6f+2))return;
            var go=Node(label,environments[(int)owner],p);go.rotation=Quaternion.Euler(0,yaw,0);
            float radius=width*.5f,spring=Mathf.Max(.8f,height-radius),thickness=Mathf.Max(.7f,width*.13f);
            BoxMesh("Left pier",go,new Vector3(-radius-thickness*.5f,spring*.5f,0),new Vector3(thickness,spring,depth),stone);
            BoxMesh("Right pier",go,new Vector3(radius+thickness*.5f,spring*.5f,0),new Vector3(thickness,spring,depth),stone);
            const int voussoirs=15;
            for(int i=0;i<voussoirs;i++)
            {
                float a=(i+.5f)*Mathf.PI/voussoirs;float r=radius+thickness*.5f;
                var brick=BoxMesh("Arch voussoir",go,new Vector3(Mathf.Cos(a)*r,spring+Mathf.Sin(a)*r,0),new Vector3(thickness,r*Mathf.PI/voussoirs*1.015f,depth),stone);
                brick.localRotation=Quaternion.Euler(0,0,a*Mathf.Rad2Deg);
            }
            // A small brass inset uses the shared project compass motif, never an elemental-color wall.
            BoxMesh("Compass keystone",go,new Vector3(0,height+thickness*.4f,-depth*.505f),new Vector3(.35f,.55f,.08f),brass);
            MergeStaticMeshes(go);CopyMeshCollision(go,collisions[(int)owner]);
            var marker=go.gameObject.AddComponent<WorldLandmark>();marker.Label=label;marker.Region=owner;marker.LocalLookPoint=Vector3.up*height;
            Clear(p,width*.6f+2);audit.Add(label+" / "+owner+" / "+p.ToString("F2"));
        }

        static Transform BoxMesh(string name,Transform parent,Vector3 local,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=local;go.transform.localScale=size;Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial=material;return go.transform;
        }
        static void MergeStaticMeshes(Transform parent)
        {
            var original=parent.GetComponentsInChildren<MeshFilter>().ToArray();
            foreach(var group in original.GroupBy(f=>f.GetComponent<Renderer>().sharedMaterial))
            {
                var mesh=new Mesh{name=parent.name+" combined",indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(group.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=parent.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);
                string path=Root+"/Meshes/Structure_"+(meshNumber++)+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(old==null)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);mesh=old;}
                var node=Node("Combined "+group.Key.name,parent,parent.position);node.localPosition=Vector3.zero;node.localRotation=Quaternion.identity;
                node.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;node.gameObject.AddComponent<MeshRenderer>().sharedMaterial=group.Key;
            }
            foreach(var filter in original)Object.DestroyImmediate(filter.gameObject);
        }
        static void CopyMeshCollision(Transform source,Transform parent)
        {
            foreach(var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.name.StartsWith("Rotor",StringComparison.Ordinal))continue;
                var collider=Node(source.name+" / "+filter.name,parent,filter.transform.position);collider.gameObject.layer=8;
                collider.rotation=filter.transform.rotation;collider.localScale=filter.transform.lossyScale;
                collider.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
                collider.gameObject.AddComponent<ClimbableSurface>();
            }
        }
        static void ClearVegetation(M4SceneBootstrap world,IReadOnlyList<Vector4> footprints)
        {
            bool Cleared(Vector3 point,float extra=0)
            {
                foreach(var p in footprints)if((point.x-p.x)*(point.x-p.x)+(point.z-p.z)*(point.z-p.z)<(p.w+extra)*(p.w+extra))return true;
                return false;
            }
            int removed=0,grassRemoved=0;
            foreach(var parent in environments)
            {
                // '/' is part of this authored label, not a Transform.Find hierarchy path.
                var root=parent.parent.Cast<Transform>().Single(t=>t.name==WorldNatureBuilder.GeneratedName);
                foreach(Transform child in root.Cast<Transform>().ToArray())
                {
                    if(child.TryGetComponent<WorldGrassField>(out var field))
                    {
                        var data=field.Data;int before=data.InstanceCount;
                        var placements=data.Cells.SelectMany(c=>c.Instances).Where(p=>!Cleared(p.Position)).ToArray();
                        data.SetInstances(placements,32);EditorUtility.SetDirty(data);grassRemoved+=before-data.InstanceCount;
                        field.Configure(field.InstanceMesh,field.InstanceMaterial,data);
                    }
                    else if(Cleared(child.position,1)){Object.DestroyImmediate(child.gameObject);removed++;}
                }
            }
            var trunks=world.transform.Find("Persistent nature collision");
            foreach(Transform trunk in trunks.Cast<Transform>().ToArray())if(Cleared(trunk.position,1))Object.DestroyImmediate(trunk.gameObject);
            audit.Add("Removed overlapping nature props="+removed+"; grass="+grassRemoved+"; clearing circles="+footprints.Count);
        }
        static void FitBuildingGround(Vector3 center)
        {
            // Art default: a 7m level terrace with 6m feather. Never alter the original encounter pads.
            if(terracesAuthored||IslandTerrainBuilder.SiteDistance(center.x,center.z)<79)return;
            foreach(var tile in terrain)
            {
                var data=tile.terrainData;var origin=tile.transform.position;int n=data.heightmapResolution;
                float metre=data.size.x/(n-1);
                int x0=Mathf.Clamp(Mathf.FloorToInt((center.x-origin.x-13)/metre),0,n-1),x1=Mathf.Clamp(Mathf.CeilToInt((center.x-origin.x+13)/metre),0,n-1);
                int z0=Mathf.Clamp(Mathf.FloorToInt((center.z-origin.z-13)/metre),0,n-1),z1=Mathf.Clamp(Mathf.CeilToInt((center.z-origin.z+13)/metre),0,n-1);
                if(x1<=x0||z1<=z0)continue;
                var heights=data.GetHeights(x0,z0,x1-x0+1,z1-z0+1);
                for(int z=0;z<=z1-z0;z++)for(int x=0;x<=x1-x0;x++)
                {
                    float wx=origin.x+(x+x0)*metre,wz=origin.z+(z+z0)*metre;
                    float distance=Vector2.Distance(new Vector2(wx,wz),new Vector2(center.x,center.z));
                    if(distance>=13||IslandTerrainBuilder.SiteDistance(wx,wz)<65)continue;
                    float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(7,13,distance));
                    heights[z,x]=Mathf.Lerp(heights[z,x],(center.y-origin.y)/data.size.y,weight);
                }
                data.SetHeights(x0,z0,heights);EditorUtility.SetDirty(data);tile.Flush();
            }
        }
        static void PaintVillagePaths()
        {
            // A small branching lane connects the existing main road, homes, and three windmills.
            Vector2[][] paths={new[]{new Vector2(0,-222),new Vector2(-72,-235),new Vector2(-125,-266),new Vector2(-182,-290)},
                new[]{new Vector2(-90,-243),new Vector2(-101,-289),new Vector2(-95,-325)},
                new[]{new Vector2(-129,-266),new Vector2(-152,-230),new Vector2(-165,-165)},
                new[]{new Vector2(-152,-230),new Vector2(-191,-215)}};
            float Distance(Vector2 p)
            {
                float best=float.MaxValue;
                foreach(var path in paths)for(int i=0;i<path.Length-1;i++)
                {Vector2 edge=path[i+1]-path[i];float t=Mathf.Clamp01(Vector2.Dot(p-path[i],edge)/edge.sqrMagnitude);best=Mathf.Min(best,Vector2.Distance(p,path[i]+edge*t));}
                return best;
            }
            foreach(var tile in terrain)
            {
                var data=tile.terrainData;int n=data.alphamapResolution;var map=data.GetAlphamaps(0,0,n,n);bool changed=false;
                for(int z=0;z<n;z++)for(int x=0;x<n;x++)
                {
                    float wx=tile.transform.position.x+x/(n-1f)*1000,wz=tile.transform.position.z+z/(n-1f)*1000;
                    if(wx< -210||wx>10||wz< -340||wz> -150)continue;
                    float d=Distance(new Vector2(wx,wz));if(d>5)continue;
                    float target=.84f*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.8f,5,d)));
                    float old=map[z,x,5];if(target<=old)continue;
                    float scale=(1-target)/Mathf.Max(.00001f,1-old);
                    for(int c=0;c<map.GetLength(2);c++)map[z,x,c]*=scale;map[z,x,5]=target;changed=true;
                }
                if(changed){data.SetAlphamaps(0,0,map);EditorUtility.SetDirty(data);tile.Flush();}
            }
            foreach(var path in paths)for(int i=0;i<path.Length-1;i++)
            {
                int count=Mathf.CeilToInt(Vector2.Distance(path[i],path[i+1])/5);
                for(int k=0;k<=count;k++){Vector2 p=Vector2.Lerp(path[i],path[i+1],k/(float)count);Clear(Ground(p),3);}
            }
        }
        static Bounds BoundsOf(Transform root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)throw new InvalidOperationException("No renderer in "+root.name);
            Bounds bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);return bounds;
        }
        static Bounds GeometryBounds(Transform root)
        {
            // Nature render bounds include a wind displacement margin. Siting/scaling must use actual vertices.
            Transform lod=root.Find("LOD0")??root;Bounds bounds=default;bool first=true;
            foreach(var filter in lod.GetComponentsInChildren<MeshFilter>(true))
            {
                Matrix4x4 matrix=root.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                foreach(Vector3 vertex in filter.sharedMesh.vertices)
                {Vector3 p=matrix.MultiplyPoint3x4(vertex);if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}
            }
            if(first)throw new InvalidOperationException("No geometry in "+root.name);return bounds;
        }
        static Transform Node(string name,Transform parent,Vector3 position)
        {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.position=position;return go.transform;}
        static Vector3 Ground(Vector2 p)
        {var tile=terrain.First(t=>p.x>=t.transform.position.x&&p.x<=t.transform.position.x+1000&&p.y>=t.transform.position.z&&p.y<=t.transform.position.z+1000);return new Vector3(p.x,tile.SampleHeight(new Vector3(p.x,0,p.y))+tile.transform.position.y,p.y);}
        static void Clear(Vector3 p,float radius)=>clear.Add(new Vector4(p.x,p.y,p.z,radius));
        static bool ContentOverlap(Vector3 p,float radius)
            =>contentKeepout.Any(c=>(new Vector2(p.x-c.x,p.z-c.z)).sqrMagnitude<(c.w+radius)*(c.w+radius));
        static Vector3 BuildingSite(Vector3 requested)
        {
            // Resolve only scenery around existing gameplay. Never move a puzzle to accommodate a house.
            foreach(float radius in new[]{0f,24f,40f,60f})for(int direction=0;direction<(radius==0?1:12);direction++)
            {
                float a=direction*Mathf.PI/6;
                var p=Ground(new Vector2(requested.x+Mathf.Cos(a)*radius,requested.z+Mathf.Sin(a)*radius));
                if(p.y<3||ContentOverlap(p,13)||IslandTerrainBuilder.SiteDistance(p.x,p.z)<79||
                    buildings.Any(b=>new Vector2(b.x-p.x,b.z-p.z).sqrMagnitude<18*18))continue;
                buildings.Add(p);return p;
            }
            throw new InvalidOperationException("No scenery footing clear of encounters near "+requested);
        }
    }
}
