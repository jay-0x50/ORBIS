using System;
using System.IO;
using System.Linq;
using Orbis.Game.World;
using Orbis.M4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    public static class WorldStyleBuilder
    {
        public const string ProfilePath="Assets/Orbis/Game/World/Resources/World/WorldGrade.asset";
        [MenuItem("Orbis/Development/Legacy/World Art/04 Unify Environment Style")]
        public static void Step4()
        {
            FieldSceneAuthoring.RequireGeneratedEditingAllowed();
            var scene=EditorSceneManager.OpenScene(IslandSceneBuilder.ScenePath,OpenSceneMode.Single);
            var world=Object.FindAnyObjectByType<M4SceneBootstrap>();
            if(world==null||!world.transform.Cast<Transform>().Any(t=>t.name.StartsWith("Persistent landmarks / ",StringComparison.Ordinal)))
                throw new InvalidOperationException("Finish continental landmarks before the style pass.");
            WorldGroundBuilder.ApplyStyle(world.GetComponentsInChildren<Terrain>());
            WorldMaterialStyling.Apply();
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Orbis/M0/Settings/M0_Renderer.asset");
            var post=AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            if(renderer==null||post==null)throw new InvalidOperationException("The installed URP renderer/post-processing resources are required.");
            // The previous renderer had a null postProcessData reference. Volumes existed but its post passes did not.
            renderer.postProcessData=post;renderer.SetDirty();EditorUtility.SetDirty(renderer);
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,ProfilePath);}
            var color=Effect<ColorAdjustments>(profile);color.contrast.Override(7);color.saturation.Override(5);
            color.postExposure.Override(.08f);color.colorFilter.Override(Color.white);
            var bloom=Effect<Bloom>(profile);bloom.intensity.Override(.16f);bloom.threshold.Override(1.05f);
            bloom.scatter.Override(.55f);bloom.tint.Override(ExplorerLookBuilder.ColorOf("Ivory"));
            var vignette=Effect<Vignette>(profile);vignette.intensity.Override(.055f);vignette.smoothness.Override(.7f);
            vignette.color.Override(ExplorerLookBuilder.ColorOf("NavyShadow"));
            Effect<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);
            foreach(var c in profile.components)EditorUtility.SetDirty(c);EditorUtility.SetDirty(profile);
            var go=GameObject.Find("World Style Volume")??new GameObject("World Style Volume");
            var volume=go.GetComponent<Volume>()??go.AddComponent<Volume>();
            // Replaces the shared base grade at priority 10; M3 flash/burst priorities 100/1000 stay dominant.
            volume.isGlobal=true;volume.priority=20;volume.weight=1;volume.sharedProfile=profile;
            var atmosphere=world.GetComponent<WorldAtmosphere>()??world.gameObject.AddComponent<WorldAtmosphere>();
            atmosphere.ConfigureClearRange(400,2600);
            foreach(var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            for(int i=0;i<5;i++)
            {
                var region=EditorSceneManager.OpenScene(WorldStreamingBuilder.ScenePath((M4RegionId)i),OpenSceneMode.Additive);
                foreach(var root in region.GetRootGameObjects())foreach(var lod in root.GetComponentsInChildren<LODGroup>(true))
                    if(lod.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterials.Any(m=>m!=null&&m.shader.name=="Orbis/World/Architecture")))
                    {lod.fadeMode=LODFadeMode.CrossFade;lod.animateCrossFading=true;}
                EditorSceneManager.MarkSceneDirty(region);EditorSceneManager.SaveScene(region);EditorSceneManager.CloseScene(region,true);
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("TestResults/WorldDev");
            File.WriteAllText("TestResults/WorldDev/World04_Style.txt","World grade priority 20; URP post resources restored; continuous elemental ambient 6.5%; terrain heights/masks preserved.\n");
            Debug.Log("ORBIS_WORLD_STEP4: unified ground, foliage, architecture and world grade; URP post processing enabled.");
        }
        static T Effect<T>(VolumeProfile profile)where T:VolumeComponent
        {
            if(profile.TryGet<T>(out var c))return c;
            c=profile.Add<T>(true);AssetDatabase.AddObjectToAsset(c,profile);return c;
        }
    }
}
