using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Game.World;
using Orbis.M2;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orbis.Game.Editor
{
    public static partial class WorldArtBuilder
    {
        public const string Root="Assets/Orbis/Game/World";
        [MenuItem("Orbis/Development/Legacy/World Art/01 Continuous Biomes and Streaming")]
        public static void Step1()
        {
            FieldSceneAuthoring.RequireGeneratedEditingAllowed();
            var scene=EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath,OpenSceneMode.Single);
            var world=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<M4SceneBootstrap>(true)).Single();
            if(world.GetComponent<WorldRegionStreamer>()!=null)
            {WorldStreamingBuilder.Validate();Debug.Log("World Step 1 already authored; preserving edited environment scenes.");return;}
            var terrain=world.GetComponentsInChildren<Terrain>();
            float OldHeight(Vector3 p)
            {
                var tile=terrain.FirstOrDefault(t=>p.x>=t.transform.position.x&&p.x<=t.transform.position.x+1000&&p.z>=t.transform.position.z&&p.z<=t.transform.position.z+1000);
                return tile!=null?tile.SampleHeight(p)+tile.transform.position.y:0;
            }
            var all=world.GetComponentsInChildren<Transform>(true);
            var landmarks=all.FirstOrDefault(x=>x.name=="03 Regional Landmarks and Trail Stops");
            var clearings=all.FirstOrDefault(x=>x.name=="04 Clearing Flowers and Trail Grass");
            var anchors=new List<Transform>();
            if(landmarks!=null)
                foreach(Transform child in landmarks)
                {
                    if(child.name.StartsWith("Trail Rest"))anchors.Add(child);
                    else foreach(Transform unit in child)anchors.Add(unit);
                }
            if(clearings!=null)foreach(Transform child in clearings)anchors.Add(child);
            var old=anchors.ToDictionary(x=>x,x=>OldHeight(x.position));
            WorldTerrainUpgrade.Apply(world.gameObject);
            foreach(var anchor in anchors)
                anchor.position+=Vector3.up*(IslandTerrainBuilder.HeightAt(anchor.position.x,anchor.position.z)-old[anchor]);

            var sources=new List<WorldEnvironmentRegionSource>();
            foreach(M4RegionId id in Enum.GetValues(typeof(M4RegionId)))
            {
                var container=new GameObject("Environment / "+id);
                sources.Add(new WorldEnvironmentRegionSource{Id=id,Root=container,WorldBounds=new Bounds(WorldBiome.SiteCenter(id),new Vector3(660,500,660))});
            }
            if(landmarks!=null)
            {
                foreach(var child in landmarks.Cast<Transform>().ToArray())
                {
                    var id=Enum.GetValues(typeof(M4RegionId)).Cast<M4RegionId>().FirstOrDefault(x=>child.name.StartsWith(x.ToString(),StringComparison.Ordinal));
                    if(child.name.StartsWith("Trail Rest"))id=IslandTerrainBuilder.BiomeAt(child.position.x,child.position.z);
                    child.SetParent(sources.Single(x=>x.Id==id).Root.transform,true);
                }
                UnityEngine.Object.DestroyImmediate(landmarks.gameObject);
            }
            if(clearings!=null)
            {
                foreach(var child in clearings.Cast<Transform>().ToArray())
                {
                    var id=IslandTerrainBuilder.BiomeAt(child.position.x,child.position.z);
                    child.SetParent(sources.Single(x=>x.Id==id).Root.transform,true);
                }
                UnityEngine.Object.DestroyImmediate(clearings.gameObject);
            }
            foreach(var source in sources)
            {
                // Collision/Climbable markers remain resident while their high-cost visible scenery streams.
                var collisionRoot=new GameObject("Persistent traversal / "+source.Id).transform;collisionRoot.SetParent(world.transform,false);
                CopyCollision(source.Root.transform,collisionRoot);
                foreach(var collider in source.Root.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
                foreach(var marker in source.Root.GetComponentsInChildren<ClimbableSurface>(true))UnityEngine.Object.DestroyImmediate(marker);
                var renderers=source.Root.GetComponentsInChildren<Renderer>(true);
                foreach(var renderer in renderers)source.WorldBounds.Encapsulate(renderer.bounds);
                foreach(var t in source.Root.GetComponentsInChildren<Transform>(true))
                    if(PrefabUtility.IsPartOfPrefabInstance(t))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                        foreach(var c in t.GetComponents<Component>())if(c!=null)PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                    }
            }
            WorldStreamingBuilder.Configure(world.gameObject,sources);
            EditorSceneManager.SetActiveScene(scene);EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
            // Existing shoreline data and map derive from the newly saved heights. Do not regenerate old vegetation.
            IslandPresentationPolish.Apply();
            IslandScenePolish.RefreshMapAndMetrics(world);
            WriteTerrainAudit(world);
            Debug.Log("ORBIS_WORLD_STEP1: continuous 2km terrain, smooth biome field, independent Addressable environment scenes; shared gameplay core retained.");
        }
        static void CopyCollision(Transform source,Transform parent)
        {
            if(source.GetComponentInChildren<Collider>(true)==null&&source.GetComponentInChildren<ClimbableSurface>(true)==null)return;
            var copy=new GameObject(source.name).transform;copy.gameObject.layer=source.gameObject.layer;copy.SetParent(parent,false);
            copy.localPosition=source.localPosition;copy.localRotation=source.localRotation;copy.localScale=source.localScale;
            copy.gameObject.SetActive(source.gameObject.activeSelf);
            foreach(var collider in source.GetComponents<Collider>())
            {var component=copy.gameObject.AddComponent(collider.GetType());EditorUtility.CopySerialized(collider,component);}
            if(source.GetComponent<ClimbableSurface>()!=null)copy.gameObject.AddComponent<ClimbableSurface>();
            foreach(Transform child in source)CopyCollision(child,copy);
        }
        static void WriteTerrainAudit(M4SceneBootstrap world)
        {
            var terrains=world.GetComponentsInChildren<Terrain>();
            double area=0;float peak=-100,largestSeam=0;
            foreach(var terrain in terrains)
            {
                var data=terrain.terrainData;int n=data.heightmapResolution;var heights=data.GetHeights(0,0,n,n);
                for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++)
                {float h=terrain.transform.position.y+heights[z,x]*data.size.y;peak=Mathf.Max(peak,h);if(h>0)area+=data.size.x*data.size.z/((n-1d)*(n-1d));}
                foreach(var next in terrains)
                {
                    if(next==terrain)continue;
                    if(Mathf.Abs(next.transform.position.x-terrain.transform.position.x-1000)<.01f&&Mathf.Abs(next.transform.position.z-terrain.transform.position.z)<.01f)
                    {var edge=next.terrainData.GetHeights(0,0,1,n);for(int i=0;i<n;i++)largestSeam=Mathf.Max(largestSeam,Mathf.Abs(heights[i,n-1]-edge[i,0])*data.size.y);}
                    if(Mathf.Abs(next.transform.position.z-terrain.transform.position.z-1000)<.01f&&Mathf.Abs(next.transform.position.x-terrain.transform.position.x)<.01f)
                    {var edge=next.terrainData.GetHeights(0,0,n,1);for(int i=0;i<n;i++)largestSeam=Mathf.Max(largestSeam,Mathf.Abs(heights[n-1,i]-edge[0,i])*data.size.y);}
                }
            }
            Directory.CreateDirectory("TestResults/WorldDev");
            File.WriteAllText("TestResults/WorldDev/World01_TerrainMetrics.txt",$"Land area {area/1000000:F4} km2\nPeak {peak:F2} m\nLargest tile seam {largestSeam:F6} m\nFive protected content sites; continuous common terrain plus five Addressable environment scenes.\n");
            if(largestSeam>.02f)throw new InvalidOperationException("Terrain tile seam exceeds 2cm.");
        }
    }
}
