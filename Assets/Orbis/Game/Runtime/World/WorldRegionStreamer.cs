using System;
using System.Collections;
using Orbis.M4;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Orbis.Game.World
{
    /// <summary>Loads scenery additively; never teleports the pawn or unloads terrain/M4 gameplay state.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldRegionStreamer : MonoBehaviour
    {
        [SerializeField] WorldStreamCatalog catalog;
        [SerializeField] bool holdAllRegions;
        [SerializeField] bool showLoadingStatus=true;
        Transform player;
        M4SceneBootstrap world;
        SceneQueue queue;
        float nextRefresh;

        public WorldStreamCatalog Catalog=>catalog;
        public Transform FollowTarget { get; set; }
        public bool ShowLoadingStatus { get=>showLoadingStatus; set=>showLoadingStatus=value; }
        public bool HoldAllRegions
        {
            get=>holdAllRegions;
            set {holdAllRegions=value;if(Application.isPlaying&&isActiveAndEnabled)RefreshNow();}
        }
        public int LoadedRegionCount=>queue?.LoadedCount??0;
        public int PendingRegionCount=>queue?.PendingCount??0;
        public bool IsReady=>queue!=null&&queue.Ready;
        public string LastError=>queue?.Error;
        // Diagnostic ownership count includes pending loads. No successful scene handle is discarded.
        public static int OwnedSceneHandleCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDiagnostics()=>OwnedSceneHandleCount=0;

        public void Configure(WorldStreamCatalog value)
        {
            if(value==null)throw new ArgumentNullException(nameof(value));
            value.Validate();
            if(queue!=null)throw new InvalidOperationException("Configure the streamer before its first load.");
            catalog=value;
            if(Application.isPlaying&&isActiveAndEnabled)RefreshNow();
        }
        void OnEnable()
        {
            M4SceneBootstrap.PlayerReady+=OnPlayerReady;
            if(catalog!=null)RefreshNow();
        }
        void OnPlayerReady(M4SceneBootstrap source)
        {
            if(source.gameObject.scene!=gameObject.scene)return;
            world=source;
            var controller=source.GetComponentInChildren<CharacterController>();
            if(controller!=null)player=controller.transform;
            RefreshNow();
        }
        void Update()
        {
            if(Time.unscaledTime>=nextRefresh)RefreshNow();
        }
        public void RefreshNow()
        {
            if(!Application.isPlaying||!isActiveAndEnabled||catalog==null)return;
            if(queue==null){catalog.Validate();queue=new SceneQueue(catalog);}
            nextRefresh=Time.unscaledTime+catalog.RefreshSeconds;
            Vector3 focus;
            if(FollowTarget!=null)focus=FollowTarget.position;
            else
            {
                if(world==null)world=GetComponent<M4SceneBootstrap>()??FindFirstObjectByType<M4SceneBootstrap>();
                if(player==null&&world!=null)
                {
                    var controller=world.GetComponentInChildren<CharacterController>();
                    if(controller!=null)player=controller.transform;
                }
                // Before selection, load around the authored arrival point, not an overview/capture camera.
                focus=player!=null?player.position:
                    world!=null&&world.AuthoredRegion!=null&&world.AuthoredRegion.Spawn!=null?
                    world.AuthoredRegion.Spawn.position:catalog.HubCenter;
            }
            queue.Request(focus,holdAllRegions);
        }
        public bool IsRegionLoaded(M4RegionId id)=>queue!=null&&queue.IsLoaded(id);
        public IEnumerator LoadAll()
        {
            HoldAllRegions=true;
            yield return WaitReady();
        }
        public IEnumerator WaitReady(float timeoutSeconds=45f)
        {
            RefreshNow();
            float deadline=Time.realtimeSinceStartup+timeoutSeconds;
            while(!IsReady)
            {
                if(!string.IsNullOrEmpty(LastError))throw new InvalidOperationException(LastError);
                if(Time.realtimeSinceStartup>=deadline)throw new TimeoutException("Environment streaming did not settle.");
                yield return null;
            }
        }
        public void RetryFailedLoads()
        {
            queue?.ClearFailures();
            RefreshNow();
        }
        void OnGUI()
        {
            if(!showLoadingStatus||queue==null||queue.Ready)return;
            // F10/menu warps remain immediate on persistent ground; explain the brief asynchronous scenery fill.
            string label=string.IsNullOrEmpty(LastError)?"주변 환경 불러오는 중 · "+LoadedRegionCount+"/5":"주변 환경 로드 실패";
            GUI.Label(new Rect(Screen.width-285,Screen.height-38,275,28),label);
        }
        void OnDisable()
        {
            M4SceneBootstrap.PlayerReady-=OnPlayerReady;
            queue?.RequestNone();
        }
        void OnDestroy()=>queue?.Dispose();

        /// <summary>
        /// Callback-owned queue deliberately outlives a destroyed MonoBehaviour until its pending load/unload ends.
        /// No coroutine or async continuation requires the destroyed host. Requests coalesce while one operation runs.
        /// </summary>
        sealed class SceneQueue : IUpdateReceiver
        {
            sealed class Entry
            {
                public WorldStreamRegion Definition;
                public bool Wanted,Owned,Loading,Unloading;
                public string Failure;
                public float Distance;
                public AsyncOperationHandle<SceneInstance> Handle;
                public bool Loaded=>Owned&&!Loading&&!Unloading&&Handle.IsValid()&&
                    Handle.Status==AsyncOperationStatus.Succeeded&&Handle.Result.Scene.isLoaded;
            }
            readonly WorldStreamCatalog data;
            readonly Entry[] entries;
            bool busy,disposed,requested;
            public SceneQueue(WorldStreamCatalog data)
            {
                this.data=data;entries=new Entry[data.Regions.Length];
                for(int i=0;i<entries.Length;i++)entries[i]=new Entry{Definition=data.Regions[i]};
                Addressables.ResourceManager.AddUpdateReceiver(this);
            }
            public void Update(float unscaledDeltaTime)
            {
                // SceneProvider releases the old scene handle AFTER notifying unload completion.
                // Pump on the following manager update, so a rapid return cannot reacquire that stale handle.
                // This also drains an in-flight load after the host MonoBehaviour has been destroyed.
                Pump();
                if(disposed&&!busy)
                {
                    foreach(var e in entries)if(e.Owned)return;
                    Addressables.ResourceManager.RemoveUpdateReciever(this);
                }
            }
            public int LoadedCount {get{int n=0;foreach(var e in entries)if(e.Loaded)n++;return n;}}
            public int PendingCount {get{int n=0;foreach(var e in entries)if(e.Wanted!=e.Loaded||e.Loading||e.Unloading)n++;return n;}}
            public bool Ready=>requested&&!busy&&PendingCount==0&&Error==null;
            public string Error {get{foreach(var e in entries)if(e.Wanted&&e.Failure!=null)return e.Failure;return null;}}
            public bool IsLoaded(M4RegionId id){foreach(var e in entries)if(e.Definition.Id==id)return e.Loaded;return false;}
            public void Request(Vector3 focus,bool holdAll)
            {
                if(disposed)return;
                requested=true;
                float hx=focus.x-data.HubCenter.x,hz=focus.z-data.HubCenter.z;
                bool atHub=data.HubPreloadRadius>0&&hx*hx+hz*hz<=data.HubPreloadRadius*data.HubPreloadRadius;
                foreach(var e in entries)
                {
                    e.Distance=WorldStreamCatalog.HorizontalDistanceSquared(focus,e.Definition.WorldBounds);
                    float range=e.Owned||e.Wanted?data.UnloadDistance:data.LoadDistance;
                    e.Wanted=holdAll||atHub||e.Distance<=range*range;
                    if(!e.Wanted)e.Failure=null;
                }
                Pump();
            }
            public void ClearFailures(){foreach(var e in entries)e.Failure=null;}
            public void RequestNone()
            {
                requested=true;foreach(var e in entries){e.Wanted=false;e.Failure=null;}Pump();
            }
            public void Dispose(){disposed=true;RequestNone();}
            void Pump()
            {
                if(busy)return;
                // A Single scene load can unload an additive scene externally. Release our explicit ownership too.
                foreach(var e in entries)if(e.Owned&&!e.Loading&&!e.Unloading&&!e.Loaded)Release(e,true);
                Entry nearest=null;
                foreach(var e in entries)
                    if(!disposed&&e.Wanted&&!e.Owned&&e.Failure==null&&(nearest==null||e.Distance<nearest.Distance))nearest=e;
                if(nearest!=null){Load(nearest);return;}
                // Preload the incoming overlap before releasing distant scenery.
                foreach(var e in entries)if(e.Owned&&!e.Wanted){Unload(e);return;}
            }
            void Load(Entry e)
            {
                busy=true;e.Loading=true;
                try
                {
                    e.Handle=Addressables.LoadSceneAsync(e.Definition.Address,LoadSceneMode.Additive,
                        SceneReleaseMode.OnlyReleaseSceneOnHandleRelease,true);
                    if(!e.Handle.IsValid())throw new InvalidOperationException("Addressables returned no scene handle.");
                    e.Owned=true;OwnedSceneHandleCount++;
                    e.Handle.Completed+=operation=>
                    {
                        e.Loading=false;busy=false;
                        if(operation.Status!=AsyncOperationStatus.Succeeded)
                        {
                            e.Failure="Environment "+e.Definition.Id+": "+(operation.OperationException?.Message??"load failed");
                            Debug.LogWarning(e.Failure);Release(e,true);
                        }
                    };
                }
                catch(Exception exception)
                {
                    e.Loading=false;busy=false;e.Failure="Environment "+e.Definition.Id+": "+exception.Message;
                    Debug.LogWarning(e.Failure);Release(e,true);
                }
            }
            void Unload(Entry e)
            {
                busy=true;e.Unloading=true;
                try
                {
                    // The unload operation releases its scene-load handle; its own handle auto-releases.
                    var operation=Addressables.UnloadSceneAsync(e.Handle,true);
                    operation.Completed+=result=>
                    {
                        bool failed=result.Status!=AsyncOperationStatus.Succeeded;
                        e.Unloading=false;busy=false;
                        if(failed)Debug.LogWarning("Environment unload failed: "+e.Definition.Id);
                        Release(e,failed);
                    };
                }
                catch(Exception exception)
                {
                    e.Unloading=false;busy=false;
                    Debug.LogWarning("Environment unload: "+exception.Message);
                    Release(e,true);
                }
            }
            static void Release(Entry e,bool releaseHandle)
            {
                if(!e.Owned)return;
                e.Owned=false;
                if(releaseHandle&&e.Handle.IsValid())Addressables.Release(e.Handle);
                e.Handle=default;OwnedSceneHandleCount--;
            }
        }
    }
}
