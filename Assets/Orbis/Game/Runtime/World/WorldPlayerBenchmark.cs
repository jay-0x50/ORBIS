using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.M4;
using Orbis.M16;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Orbis.Game.World
{
    /// <summary>Explicit command-line inspection only. Never created during a normal game session.</summary>
    public sealed class WorldPlayerBenchmark : MonoBehaviour
    {
        [Serializable] sealed class Stats
        {
            public string name,unit; public bool available; public int samples;
            public double mean,p50,p95,p99,maximum;
        }
        sealed class Series
        {
            readonly double[] values=new double[120000]; int count;
            public readonly string Name,Unit;
            public Series(string name,string unit){Name=name;Unit=unit;}
            public void Add(double value){if(count<values.Length&&double.IsFinite(value))values[count++]=value;}
            public Stats Finish()
            {
                var s=new Stats{name=Name,unit=Unit,available=count>0,samples=count};
                if(count==0)return s;
                double total=0;for(int i=0;i<count;i++)total+=values[i];Array.Sort(values,0,count);
                s.mean=total/count;s.p50=values[(count-1)/2];s.p95=values[(int)((count-1)*.95)];
                s.p99=values[(int)((count-1)*.99)];s.maximum=values[count-1];return s;
            }
        }
        sealed class Counter : IDisposable
        {
            public ProfilerRecorder Recorder; public Series Values; public string Name,Unit;
            public void Dispose(){if(Recorder.Valid)Recorder.Dispose();}
        }
        [Serializable] sealed class StationResult
        {
            public string name; public Vector3 start,end,focus;
            public float warmupSeconds,measurementSeconds;
            public int renderedFrames,minimumLoadedRegions,maximumLoadedRegions;
            public bool occlusion,renderingConfirmed;
            public Stats[] metrics;
        }
        [Serializable] sealed class Report
        {
            public string utc,unity,graphics,driver,cpu,operatingSystem,quality,scene,savePath,error,renderingMode;
            public string note="Real Windows Player wall time; captureDeltaTime=0, vSync=0, uncapped. PNG readback and warm-up excluded. Unsupported counters are absent; GPU zero means unavailable. No camera-specific draw counter exists; only this benchmark camera renders.";
            public bool development,instancing,frameTimingEnabled,complete;
            public int width,height,graphicsMemoryMB,systemMemoryMB,processorCount,msaa;
            public float renderScale,lodBias,shadowDistance,qualityShadowDistance;
            public int loadedLodGroups,grassPlacements,landmarks; public long grassCpuBuffers;
            public List<StationResult> stations=new List<StationResult>();
            public int[] packedSceneCycle; public double packedInitialLoadSeconds;
            public string[] availableCounters;
        }
        struct Station
        {
            public string Name; public Vector3 Start,End,Focus; public WorldWeatherKind Weather;
            public Station(string name,Vector3 position,Vector3 focus,WorldWeatherKind weather=WorldWeatherKind.Clear)
            {Name=name;Start=End=position;Focus=focus;Weather=weather;}
        }
        string output; Camera camera; M4SceneBootstrap world; WorldRegionStreamer streamer;
        WorldGrassField[] grass; Terrain[] terrains; WorldWeather weather; readonly Report report=new Report();
        readonly List<Counter> counters=new List<Counter>();
        readonly FrameTiming[] timings=new FrameTiming[1];
        int cameraFrames; bool finished; double started; bool occlusion;
        RenderTexture offscreen; bool manualRendering;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if(!Has("-orbisWorldBenchmark")||Application.isEditor)return;
            // The caller deliberately supplies an evidence directory. Never use the real persistent profile.
            string path=Argument("-benchmarkOutput");
            if(string.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path))
            {Debug.LogError("Benchmark requires an absolute -benchmarkOutput directory.");Application.Quit(2);return;}
            Directory.CreateDirectory(path);
            var profile=new M4ProgressService(Path.Combine(path,"BenchmarkProfile",Guid.NewGuid().ToString("N")+".json"));
            profile.TrySelectExplorer(ExplorerChoice.Stella);M4Session.UseProgressForTests(profile);
            var go=new GameObject("Opt-in world player benchmark");DontDestroyOnLoad(go);
            var benchmark=go.AddComponent<WorldPlayerBenchmark>();benchmark.output=path;
            benchmark.report.savePath=profile.SavePath;
        }
        IEnumerator Start()
        {
            Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
            Time.captureDeltaTime=0;Time.timeScale=1;Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
            occlusion=!Has("-benchmarkNoOcclusion");started=Time.realtimeSinceStartupAsDouble;
            Application.logMessageReceived+=OnLog;
            if(SceneManager.GetActiveScene().path!=ExplorerJourney.IslandScene)
                yield return SceneManager.LoadSceneAsync(ExplorerJourney.IslandScene,LoadSceneMode.Single);
            double deadline=Time.realtimeSinceStartupAsDouble+120;
            while((world=FindAnyObjectByType<M4SceneBootstrap>())==null||!world.IsInitialized)
            {
                if(Time.realtimeSinceStartupAsDouble>deadline){Fail("World initialization timeout.");yield break;}
                yield return null;
            }
            streamer=world.GetComponent<WorldRegionStreamer>();
            if(streamer==null){Fail("Missing packed environment streamer.");yield break;}
            streamer.HoldAllRegions=true;
            while(!streamer.IsReady||streamer.LoadedRegionCount!=5)
            {
                if(!string.IsNullOrEmpty(streamer.LastError)){Fail(streamer.LastError);yield break;}
                if(Time.realtimeSinceStartupAsDouble>deadline){Fail("Packed environment initial load timeout.");yield break;}
                yield return null;
            }
            report.packedInitialLoadSeconds=Time.realtimeSinceStartupAsDouble-started;
            foreach(var other in Camera.allCameras)other.enabled=false;
            camera=new GameObject("World benchmark camera").AddComponent<Camera>();
            camera.tag="MainCamera";camera.fieldOfView=58;camera.nearClipPlane=.15f;camera.farClipPlane=3500;
            camera.allowHDR=true;camera.allowMSAA=true;camera.useOcclusionCulling=occlusion;
            var additional=camera.GetUniversalAdditionalCameraData();additional.renderPostProcessing=true;
            additional.requiresDepthTexture=true;additional.volumeLayerMask=~0;
            manualRendering=Has("-benchmarkOffscreen");
            if(manualRendering)
            {
                camera.enabled=false;
                offscreen=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32){antiAliasing=4};offscreen.Create();
                report.renderingMode="Offscreen SingleCameraRequest: actual URP draws, no window presentation; not a delivered game FPS measurement.";
            }
            else report.renderingMode="Normal visible player camera and window presentation";
            streamer.ShowLoadingStatus=false;world.ShowHud=false;
            weather=world.GetComponent<WorldWeather>();weather.ShowHud=false;
            var hud=FindAnyObjectByType<ExplorerStatusHud>();if(hud!=null)hud.enabled=false;
            grass=FindObjectsByType<WorldGrassField>(FindObjectsSortMode.None);
            terrains=Terrain.activeTerrains;
            if(Has("-benchmarkGrassOff"))foreach(var field in grass)field.enabled=false;
            if(Has("-benchmarkShadowsOff"))QualitySettings.shadows=UnityEngine.ShadowQuality.Disable;
            RenderPipelineManager.endCameraRendering+=OnCameraRendered;
            CaptureConfiguration();StartCounters();
            var stations=new[]
            {
                new Station("Village",new Vector3(2,28,-227),new Vector3(-120,34,-225)),
                new Station("Forest",new Vector3(265,60,165),new Vector3(465,105,420)),
                new Station("Lakeside",new Vector3(75,26,-30),new Vector3(285,23,73)),
                new Station("HighlandStorm",new Vector3(-265,55,-350),new Vector3(-415,95,-500),WorldWeatherKind.Thunderstorm),
                new Station("IslandTravel",new Vector3(-35,30,-200),new Vector3(-450,120,-450)){End=new Vector3(-380,125,-420)}
            };
            string only=Argument("-benchmarkStation");
            float seconds=Number("-benchmarkSeconds",60,5,180),warmup=Number("-benchmarkWarmup",15,2,120);
            // First-use shader/texture upload is allowed a longer warm-up and never mixed into steady-state FPS.
            SetCamera(stations[0],0);yield return new WaitForSecondsRealtime(Number("-benchmarkInitialWarmup",30,2,180));
            foreach(var station in stations)
            {
                if(!string.IsNullOrEmpty(only)&&only!=station.Name)continue;
                weather.SetWeather(station.Weather,0);SetCamera(station,0);
                streamer.HoldAllRegions=false;streamer.FollowTarget=camera.transform;streamer.RefreshNow();
                yield return new WaitForSecondsRealtime(warmup);
                if(Has("-benchmarkScreenshots"))
                {
                    if(manualRendering)CaptureOffscreen(Path.Combine(output,station.Name+".png"));
                    else ScreenCapture.CaptureScreenshot(Path.Combine(output,station.Name+".png"));
                    yield return new WaitForSecondsRealtime(2);
                }
                var wall=new Series("Wall frame time","ms");var cpu=new Series("FrameTiming CPU","ms");
                var gpu=new Series("FrameTiming GPU","ms");var main=new Series("FrameTiming main thread","ms");
                var render=new Series("FrameTiming render thread","ms");
                var grassInstances=new Series("Submitted grass instances","count");var grassBatches=new Series("Grass instance batches","count");
                foreach(var counter in counters)counter.Values=new Series(counter.Name,counter.Unit);
                var result=new StationResult{name=station.Name,start=camera.transform.position,end=station.End,focus=station.Focus,
                    warmupSeconds=warmup,measurementSeconds=seconds,occlusion=occlusion,minimumLoadedRegions=5};
                cameraFrames=0;ulong lastTimestamp=0;
                double begin=Time.realtimeSinceStartupAsDouble,previous=begin;
                while(Time.realtimeSinceStartupAsDouble-begin<seconds)
                {
                    FrameTimingManager.CaptureFrameTimings();yield return null;
                    double now=Time.realtimeSinceStartupAsDouble;wall.Add((now-previous)*1000);previous=now;
                    if(FrameTimingManager.GetLatestTimings(1,timings)>0&&timings[0].frameStartTimestamp!=lastTimestamp)
                    {
                        var timing=timings[0];lastTimestamp=timing.frameStartTimestamp;
                        if(timing.cpuFrameTime>0)cpu.Add(timing.cpuFrameTime);if(timing.gpuFrameTime>0)gpu.Add(timing.gpuFrameTime);
                        if(timing.cpuMainThreadFrameTime>0)main.Add(timing.cpuMainThreadFrameTime);
                        if(timing.cpuRenderThreadFrameTime>0)render.Add(timing.cpuRenderThreadFrameTime);
                    }
                    foreach(var counter in counters)if(counter.Recorder.Valid&&counter.Recorder.Count>0)counter.Values.Add(counter.Recorder.LastValue);
                    int instances=0,batches=0;foreach(var field in grass)if(field!=null){instances+=field.VisibleInstanceCount;batches+=field.DrawBatchCount;}
                    grassInstances.Add(instances);grassBatches.Add(batches);
                    result.minimumLoadedRegions=Mathf.Min(result.minimumLoadedRegions,streamer.LoadedRegionCount);
                    result.maximumLoadedRegions=Mathf.Max(result.maximumLoadedRegions,streamer.LoadedRegionCount);
                    if(station.Start!=station.End)SetCamera(station,Mathf.Clamp01((float)((now-begin)/seconds)));
                }
                result.renderedFrames=cameraFrames;result.renderingConfirmed=cameraFrames>0;
                var metrics=new List<Stats>{wall.Finish(),cpu.Finish(),gpu.Finish(),main.Finish(),render.Finish(),grassInstances.Finish(),grassBatches.Finish()};
                metrics.AddRange(counters.Select(c=>c.Values.Finish()));result.metrics=metrics.ToArray();
                report.stations.Add(result);Write();
                Debug.Log("ORBIS_BENCHMARK_STATION "+station.Name+" p95="+metrics[0].p95+"ms rendered="+cameraFrames);
                if(!result.renderingConfirmed){Fail("No actual camera frames were rendered.");yield break;}
                // User-requested stop policy: do not continue a long stress route below 20 FPS mean.
                if(metrics[0].mean>50){Fail("Severe frame cost (>50ms mean). Remaining stations stopped for optimization.");yield break;}
            }
            // Exercise actual packed IO and handle ownership after timings; resident terrain/player stay in place.
            streamer.HoldAllRegions=true;yield return streamer.WaitReady(120);
            int first=streamer.LoadedRegionCount;
            camera.transform.position=new Vector3(8000,100,8000);streamer.HoldAllRegions=false;
            streamer.RefreshNow();yield return streamer.WaitReady(120);int second=streamer.LoadedRegionCount;
            streamer.HoldAllRegions=true;yield return streamer.WaitReady(120);int third=streamer.LoadedRegionCount;
            report.packedSceneCycle=new[]{first,second,third};
            if(first!=5||second!=0||third!=5){Fail("Packed scenes failed the 5→0→5 cycle.");yield break;}
            report.complete=true;Write();finished=true;Application.Quit(0);
        }
        void SetCamera(Station station,float progress)
        {
            Vector3 p=Vector3.Lerp(station.Start,station.End,progress);float ground=-100;
            foreach(var terrain in terrains)
            {
                Vector3 local=p-terrain.transform.position;Vector3 size=terrain.terrainData.size;
                if(local.x>=0&&local.z>=0&&local.x<=size.x&&local.z<=size.z)ground=Mathf.Max(ground,terrain.SampleHeight(p)+terrain.transform.position.y);
            }
            p.y=Mathf.Max(p.y,ground+2);camera.transform.position=p;camera.transform.LookAt(station.Focus);
        }
        void StartCounters()
        {
            var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);
            var names=new List<string>();var wanted=new HashSet<string>{"Main Thread","Render Thread","Draw Calls Count","SetPass Calls Count","Triangles Count","GC Allocated In Frame","Total Used Memory","Gfx Used Memory"};
            foreach(var handle in handles)
            {
                var d=ProfilerRecorderHandle.GetDescription(handle);names.Add(d.Name+" ["+d.UnitType+"]");
                if(!wanted.Remove(d.Name))continue;
                var recorder=new ProfilerRecorder(handle,1,ProfilerRecorderOptions.StartImmediately|ProfilerRecorderOptions.WrapAroundWhenCapacityReached|ProfilerRecorderOptions.SumAllSamplesInFrame);
                if(recorder.Valid)counters.Add(new Counter{Recorder=recorder,Name=d.Name,Unit=d.UnitType.ToString()});
            }
            report.availableCounters=names.ToArray();
        }
        void CaptureConfiguration()
        {
            report.utc=DateTime.UtcNow.ToString("O");report.unity=Application.unityVersion;report.development=Debug.isDebugBuild;
            report.graphics=SystemInfo.graphicsDeviceName;report.driver=SystemInfo.graphicsDeviceVersion;report.cpu=SystemInfo.processorType;
            report.operatingSystem=SystemInfo.operatingSystem;report.graphicsMemoryMB=SystemInfo.graphicsMemorySize;
            report.systemMemoryMB=SystemInfo.systemMemorySize;report.processorCount=SystemInfo.processorCount;
            report.instancing=SystemInfo.supportsInstancing;report.frameTimingEnabled=FrameTimingManager.IsFeatureEnabled();
            report.quality=QualitySettings.names[QualitySettings.GetQualityLevel()];report.width=Screen.width;report.height=Screen.height;
            report.scene=SceneManager.GetActiveScene().path;report.lodBias=QualitySettings.lodBias;report.qualityShadowDistance=QualitySettings.shadowDistance;
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(pipeline!=null){report.renderScale=pipeline.renderScale;report.msaa=pipeline.msaaSampleCount;report.shadowDistance=pipeline.shadowDistance;}
            // Includes trees, rocks and architecture; do not label this aggregate as the number of trees.
            report.loadedLodGroups=FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Length;
            report.landmarks=FindObjectsByType<WorldLandmark>(FindObjectsSortMode.None).Length;
            foreach(var field in grass){report.grassPlacements+=field.Data.InstanceCount;report.grassCpuBuffers+=field.ApproximateBufferBytes;}
        }
        void OnCameraRendered(ScriptableRenderContext context,Camera value){if(value==camera)cameraFrames++;}
        void LateUpdate()
        {
            if(!manualRendering||camera==null||offscreen==null||finished)return;
            if(!VolumeManager.instance.isInitialized)
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=offscreen});
            camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);camera.UpdateVolumeStack();
            var previous=VolumeManager.instance.stack;
            try
            {
                VolumeManager.instance.stack=camera.GetUniversalAdditionalCameraData().volumeStack;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=offscreen});
            }
            finally{VolumeManager.instance.stack=previous;}
        }
        void CaptureOffscreen(string path)
        {
            // Readback is explicitly outside every timed window.
            var previous=RenderTexture.active;var resolved=new RenderTexture(1920,1080,0,RenderTextureFormat.ARGB32);resolved.Create();
            var pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            try{offscreen.ResolveAntiAliasedSurface(resolved);RenderTexture.active=resolved;pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;resolved.Release();Destroy(resolved);Destroy(pixels);}
        }
        void OnLog(string condition,string stack,LogType type)
        {if(!finished&&(type==LogType.Exception||type==LogType.Error))Fail(condition+"\n"+stack);}
        void Update()
        {if(!finished&&started>0&&Time.realtimeSinceStartupAsDouble-started>1800)Fail("Benchmark watchdog timeout.");}
        void Fail(string error){report.error=error;Write();finished=true;Debug.LogError(error);Application.Quit(2);}
        void Write(){if(!string.IsNullOrEmpty(output))File.WriteAllText(Path.Combine(output,"WorldPerformance.json"),JsonUtility.ToJson(report,true));}
        void OnDestroy()
        {
            Application.logMessageReceived-=OnLog;RenderPipelineManager.endCameraRendering-=OnCameraRendered;
            foreach(var counter in counters)counter.Dispose();
            if(offscreen!=null){offscreen.Release();Destroy(offscreen);}
        }
        static bool Has(string value)=>Array.IndexOf(Environment.GetCommandLineArgs(),value)>=0;
        static string Argument(string key){var args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]==key)return args[i+1];return null;}
        static float Number(string key,float fallback,float min,float max)=>float.TryParse(Argument(key),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float value)?Mathf.Clamp(value,min,max):fallback;
    }
}
