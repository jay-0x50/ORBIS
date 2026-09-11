using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Game.World;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Orbis.M4;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orbis.Game.Editor
{
    public sealed class WorldEnvironmentRegionSource
    {
        public M4RegionId Id;
        public GameObject Root;
        public Bounds WorldBounds;
    }

    /// <summary>Extracts only authored scenery. Existing orbis.region.* prototype bundles remain separate.</summary>
    public static class WorldStreamingBuilder
    {
        public const string SceneDirectory="Assets/Orbis/Game/World/Scenes";
        public const string CatalogPath="Assets/Orbis/Game/World/Resources/World/StreamCatalog.asset";
        public const string RegionGroup="ORBIS World Environment";
        public static string ScenePath(M4RegionId id)=>SceneDirectory+"/Environment_"+id+".unity";
        public static string Address(M4RegionId id)=>"orbis.world.environment."+id.ToString().ToLowerInvariant();

        /// <summary>
        /// Moves the five supplied plain roots into saved environment scenes, then closes those scenes.
        /// Positions are preserved. The caller owns saving the persistent host scene after this method.
        /// </summary>
        public static WorldRegionStreamer Configure(GameObject host,IReadOnlyList<WorldEnvironmentRegionSource> regions)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Author environment scenes outside Play Mode.");
            if(host==null||!host.scene.IsValid()||!host.scene.isLoaded)throw new ArgumentException("A loaded host scene is required.");
            if(regions==null||regions.Count!=5)throw new ArgumentException("Provide all five scenery roots.");
            var ids=new HashSet<M4RegionId>();var roots=new HashSet<GameObject>();
            foreach(var source in regions)
            {
                if(source==null||source.Root==null||source.Root==host||host.transform.IsChildOf(source.Root.transform)||
                    !Enum.IsDefined(typeof(M4RegionId),source.Id)||!ids.Add(source.Id)||!roots.Add(source.Root)||
                    source.Root.scene!=host.scene)
                    throw new ArgumentException("Each environment needs a unique regional root in the host scene.");
                ValidateEnvironmentRoot(source.Root);
                var existing=SceneManager.GetSceneByPath(ScenePath(source.Id));
                if(existing.IsValid()&&existing.isLoaded)
                    throw new InvalidOperationException("Close the existing streamed scene before rebuilding it: "+ScenePath(source.Id));
            }
            foreach(var source in regions)
                foreach(var other in regions)
                    if(source!=other&&source.Root.transform.IsChildOf(other.Root.transform))
                        throw new ArgumentException("Environment roots cannot contain one another.");
            // Validate bounds before moving any hierarchy or replacing any region scene.
            var definitions=regions.Select(source=>new WorldStreamRegion
                {Id=source.Id,Address=Address(source.Id),WorldBounds=source.WorldBounds}).ToArray();
            var validation=ScriptableObject.CreateInstance<WorldStreamCatalog>();
            try {validation.Regions=definitions;validation.Validate();}
            finally {UnityEngine.Object.DestroyImmediate(validation);}
            Directory.CreateDirectory(SceneDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
            AssetDatabase.Refresh();
            Scene previous=SceneManager.GetActiveScene();
            foreach(var source in regions)
            {
                string path=ScenePath(source.Id);
                Scene regionScene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                bool saved=false;
                try
                {
                    source.Root.transform.SetParent(null,true);
                    SceneManager.MoveGameObjectToScene(source.Root,regionScene);
                    if(!EditorSceneManager.SaveScene(regionScene,path))throw new IOException("Could not save "+path);
                    saved=true;
                }
                finally
                {
                    // If saving failed, restore the unsaved scenery to the still-open host instead of deleting it.
                    if(!saved&&source.Root!=null)SceneManager.MoveGameObjectToScene(source.Root,host.scene);
                    EditorSceneManager.CloseScene(regionScene,true);
                    if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
                }
            }
            var catalog=AssetDatabase.LoadAssetAtPath<WorldStreamCatalog>(CatalogPath);
            if(catalog==null){catalog=ScriptableObject.CreateInstance<WorldStreamCatalog>();AssetDatabase.CreateAsset(catalog,CatalogPath);}
            catalog.Regions=definitions;EditorUtility.SetDirty(catalog);
            RegisterScenes();
            var streamer=host.GetComponent<WorldRegionStreamer>()??host.AddComponent<WorldRegionStreamer>();
            streamer.Configure(catalog);EditorUtility.SetDirty(streamer);
            EditorSceneManager.MarkSceneDirty(host.scene);AssetDatabase.SaveAssets();
            Validate();
            return streamer;
        }

        static void ValidateEnvironmentRoot(GameObject root)
        {
            if(root.GetComponentInChildren<Terrain>(true)!=null||root.GetComponentInChildren<TerrainCollider>(true)!=null||
                root.GetComponentInChildren<CharacterController>(true)!=null||root.GetComponentInChildren<Camera>(true)!=null)
                throw new InvalidOperationException("Terrain, player and cameras must stay in the persistent scene: "+root.name);
            foreach(var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                if(component==null||component is M4SceneBootstrap||component is M4AuthoredRegion||
                    component is M4RegionContent||component is M4FieldBoss||component is PlayerMotor||
                    component is PartyManager||component is ElementalActor||component is ElementalReactionManager||
                    component is FieldElementPuzzle||component is ChallengeRoom||component is WaterVolume||
                    component is ClimbableSurface||component is GameSceneEntry||component is WorldRegionStreamer)
                    throw new InvalidOperationException("Environment extraction cannot move gameplay or traversal components: "+root.name);
        }

        static void RegisterScenes()
        {
            var settings=AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group=settings.FindGroup(RegionGroup);
            if(group==null)group=settings.CreateGroup(RegionGroup,false,false,true,null,
                typeof(BundledAssetGroupSchema),typeof(ContentUpdateGroupSchema));
            var schema=group.GetSchema<BundledAssetGroupSchema>()??group.AddSchema<BundledAssetGroupSchema>();
            schema.IncludeInBuild=true;schema.BundleMode=BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            schema.BuildPath.SetVariableByName(settings,AddressableAssetSettings.kLocalBuildPath);
            schema.LoadPath.SetVariableByName(settings,AddressableAssetSettings.kLocalLoadPath);
            foreach(M4RegionId id in Enum.GetValues(typeof(M4RegionId)))
            {
                var entry=settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(ScenePath(id)),group);
                entry.address=Address(id);
            }
            // Same provider policy as M4ProjectSetup: real Addressables APIs with editor assets in Play Mode,
            // packed local bundles in a player. Regeneration must not reuse a stale editor packed catalog.
            int fast=settings.DataBuilders.FindIndex(x=>x is BuildScriptFastMode);
            int packed=settings.DataBuilders.FindIndex(x=>x is BuildScriptPackedMode);
            if(fast<0||packed<0)throw new BuildFailedException("Addressables Fast/Packed data builders are unavailable.");
            settings.ActivePlayModeDataBuilderIndex=fast;
            settings.ActivePlayerDataBuilderIndex=packed;
            // Streamed scene dependencies must not also be pulled into the built-in scene list.
            var paths=new HashSet<string>(Enum.GetValues(typeof(M4RegionId)).Cast<M4RegionId>().Select(ScenePath));
            EditorBuildSettings.scenes=EditorBuildSettings.scenes.Where(scene=>!paths.Contains(scene.path)).ToArray();
            EditorUtility.SetDirty(group);EditorUtility.SetDirty(schema);EditorUtility.SetDirty(settings);
        }

        [MenuItem("Orbis/World/Build Environment Addressables")]
        public static void BuildContent()
        {
            Validate();
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if(result==null||!string.IsNullOrEmpty(result.Error))
                throw new BuildFailedException(result?.Error??"No Addressables build result.");
            Debug.Log("ORBIS_WORLD_ENVIRONMENT_BUNDLES_BUILT "+result.OutputPath);
        }

        [MenuItem("Orbis/World/Refresh Environment Stream Bounds")]
        public static void RefreshEnvironmentBounds()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Refresh authored bounds outside Play Mode.");
            Validate();
            var catalog=AssetDatabase.LoadAssetAtPath<WorldStreamCatalog>(CatalogPath);
            var revised=new Bounds[catalog.Regions.Length];var previous=SceneManager.GetActiveScene();
            for(int i=0;i<catalog.Regions.Length;i++)
            {
                var region=catalog.Regions[i];string path=ScenePath(region.Id);
                var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
                try
                {
                    if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                    // Retain the site's arrival anchor, then fit its actual scenery. The common terrain is
                    // deliberately excluded: including an entire resident tile would keep unrelated regions loaded.
                    var bounds=new Bounds(WorldBiome.SiteCenter(region.Id),Vector3.one*2);
                    foreach(var root in scene.GetRootGameObjects())
                    {
                        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))bounds.Encapsulate(renderer.bounds);
                        foreach(var grass in root.GetComponentsInChildren<WorldGrassField>(true))
                        {
                            if(grass.Data==null||grass.InstanceMesh==null)
                                throw new BuildFailedException("Grass data/mesh is missing in "+path);
                            var meshBounds=grass.InstanceMesh.bounds;
                            // Rotation-independent radius includes the authored mesh extent and shader displacement.
                            float radius=meshBounds.center.magnitude+meshBounds.extents.magnitude;
                            foreach(var cell in grass.Data.Cells)
                            {
                                float scale=Mathf.Max(cell.MaximumScale.x,Mathf.Max(cell.MaximumScale.y,cell.MaximumScale.z));
                                var cellBounds=cell.PositionBounds;
                                cellBounds.Expand(2*(radius*scale+Mathf.Max(0,grass.DeformationPadding)));
                                bounds.Encapsulate(cellBounds);
                            }
                        }
                    }
                    if(!Finite(bounds.min)||!Finite(bounds.max))throw new BuildFailedException("Non-finite environment bounds: "+path);
                    revised[i]=bounds;
                }
                finally
                {
                    if(opened&&scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
                    if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
                }
            }
            // Commit only after every scene was measured; preserve addresses, margins, hub and all runtime settings.
            for(int i=0;i<revised.Length;i++)catalog.Regions[i].WorldBounds=revised[i];
            catalog.Validate();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssetIfDirty(catalog);
            Debug.Log("ORBIS_WORLD_STREAM_BOUNDS: refreshed five environment regions from all LOD renderers and grass cells; existing load/exit margins retained.");
        }
        static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)&&
            !float.IsInfinity(v.x)&&!float.IsInfinity(v.y)&&!float.IsInfinity(v.z);

        public static void Validate()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<WorldStreamCatalog>(CatalogPath);
            if(catalog==null)throw new BuildFailedException("Environment stream catalog is missing.");
            catalog.Validate();
            var settings=AddressableAssetSettingsDefaultObject.Settings;
            var group=settings!=null?settings.FindGroup(RegionGroup):null;
            var schema=group!=null?group.GetSchema<BundledAssetGroupSchema>():null;
            if(schema==null||!schema.IncludeInBuild||schema.BundleMode!=BundledAssetGroupSchema.BundlePackingMode.PackSeparately)
                throw new BuildFailedException("Environment scenes must use separate local Addressables bundles.");
            foreach(var region in catalog.Regions)
            {
                string path=ScenePath(region.Id),guid=AssetDatabase.AssetPathToGUID(path);
                var entry=settings.FindAssetEntry(guid);
                if(string.IsNullOrEmpty(guid)||entry==null||entry.address!=region.Address||entry.parentGroup!=group||
                    EditorBuildSettings.scenes.Any(scene=>scene.path==path&&scene.enabled))
                    throw new BuildFailedException("Environment scene address/boundary is incorrect: "+path);
            }
        }
    }
}
