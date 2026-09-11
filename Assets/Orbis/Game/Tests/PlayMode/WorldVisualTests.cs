using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.M4;
using Orbis.M16;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Orbis.Game.Tests
{
    public sealed class WorldVisualTests
    {
        [Serializable] sealed class Shot
        {public string stage,view;public Vector3 camera,focus;public float fov;public bool monochrome;public string graphics;public int vram;public string note="Actual Unity URP render; fixed world camera recipe. Not a performance benchmark.";}
        string directory;float timeScale,captureDelta;bool background;Scene loaded;
        [UnitySetUp] public IEnumerator Setup()
        {
            timeScale=Time.timeScale;captureDelta=Time.captureDeltaTime;background=Application.runInBackground;
            Time.timeScale=1;Time.captureDeltaTime=1f/60;Application.runInBackground=true;
            directory=Path.Combine(Path.GetTempPath(),"Orbis-WorldArt-"+Guid.NewGuid().ToString("N"));
            var progress=new M4ProgressService(Path.Combine(directory,"profile.json"));progress.TrySelectExplorer(ExplorerChoice.Stella);
            ExplorerJourney.Stop();M4Session.UseProgressForTests(progress);
            yield return SceneManager.LoadSceneAsync(ExplorerJourney.IslandScene,LoadSceneMode.Single);
            loaded=SceneManager.GetActiveScene();yield return new WaitForSecondsRealtime(1.3f);
            for(int i=0;i<60;i++)yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            ExplorerJourney.Stop();var empty=SceneManager.CreateScene("World art cleanup");SceneManager.SetActiveScene(empty);
            if(loaded.IsValid()&&loaded.isLoaded)yield return SceneManager.UnloadSceneAsync(loaded);
            var router=Object.FindAnyObjectByType<M4RegionRouter>();if(router!=null)Object.Destroy(router.gameObject);
            yield return null;M4Session.UseProgressForTests(null);
            Time.timeScale=timeScale;Time.captureDeltaTime=captureDelta;Application.runInBackground=background;
            Assert.That(Path.GetFileName(directory),Does.StartWith("Orbis-WorldArt-"));
            Assert.That(Path.GetDirectoryName(directory),Is.EqualTo(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
        [UnityTest] public IEnumerator ContinuousWorldPreservesCoreAndCanBeInspectedAtFixedViews()
        {
            var world=Object.FindAnyObjectByType<M4SceneBootstrap>();Assert.That(world,Is.Not.Null);
            Assert.That(Object.FindObjectsByType<M4SceneBootstrap>(FindObjectsSortMode.None).Length,Is.EqualTo(1));
            Assert.That(world.IslandRegions.Length,Is.EqualTo(5));
            Assert.That(Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Length,Is.EqualTo(4));
            string stage=Argument("-worldStep");if(string.IsNullOrEmpty(stage))yield break;
            Assert.That(stage.All(c=>char.IsLetterOrDigit(c)||c=='_'),Is.True);
            // Optional streamer is absent in the baseline. This inspection switch pins environment scenes only.
            var streamer=world.GetComponents<MonoBehaviour>().FirstOrDefault(x=>x.GetType().Name=="WorldRegionStreamer");
            if(streamer!=null)
            {
                var property=streamer.GetType().GetProperty("HoldAllRegions");property?.SetValue(streamer,true);
                var field=streamer.GetType().GetField("HoldAllRegions");field?.SetValue(streamer,true);
                float end=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<end)
                {
                    var count=streamer.GetType().GetProperty("LoadedRegionCount");
                    if(count!=null&&(int)count.GetValue(streamer)==5)break;
                    yield return null;
                }
                if(streamer.GetType().GetProperty("LoadedRegionCount") is System.Reflection.PropertyInfo ready)
                    Assert.That((int)ready.GetValue(streamer),Is.EqualTo(5),"Addressable environment scenes must finish before a comparison image.");
            }
            Capture(stage,"Overview",new Vector3(-1150,1250,-1360),new Vector3(0,45,0),59);
            Capture(stage,"Silhouette",new Vector3(0,2500,0),Vector3.zero,48,true);
            Capture(stage,"MeadowHighland",new Vector3(-265,55,-350),new Vector3(-415,95,-500),58);
            Capture(stage,"ThermalFoothills",new Vector3(-230,65,115),new Vector3(-470,100,350),58);
            Capture(stage,"CoastalWetland",new Vector3(320,40,-195),new Vector3(585,16,-380),58);
            Capture(stage,"StormWoodland",new Vector3(265,60,165),new Vector3(465,105,420),58);
            Capture(stage,"Village",new Vector3(2,28,-227),new Vector3(-120,34,-225),58);
            Capture(stage,"Lakeside",new Vector3(75,26,-30),new Vector3(285,23,73),58);
        }
        internal static string Argument(string key)
        {var args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]==key)return args[i+1];return null;}
        internal static void Capture(string stage,string name,Vector3 position,Vector3 focus,float fov,bool mono=false)
        {
            if(name!="Overview"&&name!="Silhouette")
            {
                if(stage=="World01_Before")
                {
                    float ground=-60;
                    foreach(var terrain in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
                        if(position.x>=terrain.transform.position.x&&position.x<=terrain.transform.position.x+1000&&position.z>=terrain.transform.position.z&&position.z<=terrain.transform.position.z+1000)
                            ground=terrain.SampleHeight(position)+terrain.transform.position.y;
                    var upgrade=Type.GetType("Orbis.Game.Editor.WorldTerrainUpgrade, Orbis.Game.Editor");
                    var method=upgrade?.GetMethod("HeightAt");
                    float planned=method!=null?(float)method.Invoke(null,new object[]{position.x,position.z}):ground;
                    position.y=Mathf.Max(position.y,Mathf.Max(ground,planned)+2);
                }
                else
                {
                    string baseline="TestResults/WorldDev/World01_Before_"+name+".json";
                    if(File.Exists(baseline)){var recipe=JsonUtility.FromJson<Shot>(File.ReadAllText(baseline));position=recipe.camera;focus=recipe.focus;fov=recipe.fov;}
                }
            }
            var go=new GameObject("World fixed review camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;
            camera.fieldOfView=fov;camera.farClipPlane=6000;camera.transform.position=position;
            camera.transform.rotation=Quaternion.LookRotation((focus-position).normalized,mono?Vector3.forward:Vector3.up);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            Volume volume=null;VolumeProfile profile=null;
            if(mono)
            {
                profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.Add<ColorAdjustments>(true).saturation.Override(-100);
                var volumeObject=new GameObject("Temporary monochrome inspection");volumeObject.SetActive(false);
                volume=volumeObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=2000;volume.sharedProfile=profile;
                volumeObject.SetActive(true); // Register with the final priority before this same-frame capture.
            }
            var target=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32){antiAliasing=4};target.Create();
            var resolved=new RenderTexture(1920,1080,0,RenderTextureFormat.ARGB32);resolved.Create();
            var pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);var active=RenderTexture.active;
            VolumeStack previousStack=null;
            #if UNITY_EDITOR
            bool asynchronous=UnityEditor.ShaderUtil.allowAsyncCompilation;UnityEditor.ShaderUtil.allowAsyncCompilation=false;
            var renderer=UniversalRenderPipeline.asset.rendererDataList[0] as UniversalRendererData;
            var originalPost=renderer!=null?renderer.postProcessData:null;
            // Baseline renderer has no post-process resource. Temporarily enable it only for the
            // diagnostic monochrome map; normal Before views retain the actual original rendering.
            if(mono&&renderer!=null&&renderer.postProcessData==null)
            {
                renderer.postProcessData=UnityEditor.AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                Assert.That(renderer.postProcessData,Is.Not.Null);renderer.SetDirty();
            }
            #endif
            try
            {
                // A headless Play Mode runner has no Game view to initialize URP before the first request.
                if(!VolumeManager.instance.isInitialized)
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);camera.UpdateVolumeStack();
                if(mono)Assert.That(camera.GetUniversalAdditionalCameraData().volumeStack.GetComponent<ColorAdjustments>().saturation.value,Is.EqualTo(-100));
                // SingleCameraRequest skips URP.UpdateVolumeFramework; LUT generation reads the global stack.
                previousStack=VolumeManager.instance.stack;VolumeManager.instance.stack=camera.GetUniversalAdditionalCameraData().volumeStack;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=active;target.ResolveAntiAliasedSurface(resolved);RenderTexture.active=resolved;
                pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
                Directory.CreateDirectory("TestResults/WorldDev");string file="TestResults/WorldDev/"+stage+"_"+name;
                File.WriteAllBytes(file+".png",pixels.EncodeToPNG());
                File.WriteAllText(file+".json",JsonUtility.ToJson(new Shot{stage=stage,view=name,camera=position,focus=focus,fov=fov,monochrome=mono,graphics=SystemInfo.graphicsDeviceName,vram=SystemInfo.graphicsMemorySize},true));
            }
            finally
            {
                RenderTexture.active=active;
                if(previousStack!=null)VolumeManager.instance.stack=previousStack;
                #if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation=asynchronous;
                if(renderer!=null&&renderer.postProcessData!=originalPost){renderer.postProcessData=originalPost;renderer.SetDirty();}
                #endif
                Object.DestroyImmediate(pixels);target.Release();resolved.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(resolved);
                Object.DestroyImmediate(go);if(volume!=null)Object.DestroyImmediate(volume.gameObject);if(profile!=null)Object.DestroyImmediate(profile);
            }
        }
    }
}
