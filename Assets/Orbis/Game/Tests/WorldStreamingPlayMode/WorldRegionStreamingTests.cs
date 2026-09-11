using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Orbis.Game.World;
using Orbis.M1;
using Orbis.M4;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Orbis.Game.Tests
{
    /// <summary>Real registered environment scenes through the configured Addressables Play Mode provider.</summary>
    public sealed class WorldRegionStreamingTests
    {
        Scene fixture,previousScene;
        WorldStreamCatalog catalog;
        GameObject host,probe;
        BoxCollider persistentGround;
        WorldRegionStreamer streamer;
        bool background;

        [UnitySetUp] public IEnumerator Setup()
        {
            background=Application.runInBackground;Application.runInBackground=true;
            Debug.Log("WORLD_STREAM_TEST_SETUP "+TestContext.CurrentContext.Test.Name);
            previousScene=SceneManager.GetActiveScene();
            fixture=SceneManager.CreateScene("World streaming test "+Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(fixture);
            // The initial scene owns Unity's coroutine test runner. Keep it alive (as M0IntegrationTests does).
            // This fixture owns only its newly created scene and its additive environment handles.
            // Other island tests may have just destroyed a host while an additive load was pending.
            yield return Await(()=>WorldRegionStreamer.OwnedSceneHandleCount==0,"Previous scene handles did not drain.");
            yield return null;
            var asset=Resources.Load<WorldStreamCatalog>("World/StreamCatalog");
            Assert.That(asset,Is.Not.Null,"Run WorldArtBuilder first to author/register the five real scenery scenes.");
            catalog=Object.Instantiate(asset);catalog.Validate();
            Assert.That(LoadedEnvironmentScenes(),Is.EqualTo(0));
            // The fixture has no bootstrap, save service, or player input. This collider stands in for persistent ground.
            persistentGround=new GameObject("Persistent ground sentinel").AddComponent<BoxCollider>();
            persistentGround.size=new Vector3(20,1,20);
            probe=new GameObject("Stream follow position");probe.transform.position=FarOutside();
            host=new GameObject("Environment streaming host");host.SetActive(false);
            streamer=host.AddComponent<WorldRegionStreamer>();streamer.Configure(catalog);
            streamer.FollowTarget=probe.transform;streamer.ShowLoadingStatus=false;
            Debug.Log("WORLD_STREAM_TEST_READY "+TestContext.CurrentContext.Test.Name);
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(host!=null)Object.Destroy(host);
            yield return null;
            yield return Await(()=>WorldRegionStreamer.OwnedSceneHandleCount==0&&LoadedEnvironmentScenes()==0,
                "Environment scene ownership leaked after destroying the host.");
            if(catalog!=null)Object.Destroy(catalog);
            if(previousScene.IsValid()&&previousScene.isLoaded)SceneManager.SetActiveScene(previousScene);
            if(fixture.IsValid()&&fixture.isLoaded)yield return SceneManager.UnloadSceneAsync(fixture);
            Application.runInBackground=background;
            Debug.Log("WORLD_STREAM_TEST_CLEAN "+TestContext.CurrentContext.Test.Name);
        }

        [UnityTest] public IEnumerator SceneryLoadsAdditivelyRetainsAnExitMarginAndSurvivesRapidTravel()
        {
            streamer.HoldAllRegions=true;host.SetActive(true);yield return streamer.WaitReady();
            AssertAllLoaded();AssertPersistentCore();
            var first=catalog.Regions[0];
            // A position outside the preload boundary but inside its exit margin must retain the loaded district.
            float between=(catalog.LoadDistance+catalog.UnloadDistance)*.5f;
            probe.transform.position=new Vector3(first.WorldBounds.max.x+between,0,first.WorldBounds.center.z);
            streamer.HoldAllRegions=false;streamer.RefreshNow();yield return streamer.WaitReady();
            Assert.That(streamer.IsRegionLoaded(first.Id),Is.True,"Crossing the preload edge must not churn a loaded scene.");
            AssertPersistentCore();
            probe.transform.position=FarOutside();streamer.RefreshNow();yield return streamer.WaitReady();
            Assert.That(streamer.LoadedRegionCount,Is.Zero);
            Assert.That(LoadedEnvironmentScenes(),Is.Zero);
            Assert.That(WorldRegionStreamer.OwnedSceneHandleCount,Is.Zero);
            AssertPersistentCore();

            probe.transform.position=first.WorldBounds.center;streamer.RefreshNow();yield return streamer.WaitReady();
            Assert.That(streamer.IsRegionLoaded(first.Id),Is.True,"Teleport arrival should acquire the requested environment.");
            streamer.HoldAllRegions=true;yield return streamer.WaitReady();AssertAllLoaded();
            probe.transform.position=FarOutside();streamer.HoldAllRegions=false;
            Assert.That(streamer.PendingRegionCount,Is.GreaterThan(0));
            // Turn back during the first unload. This exercises cached Addressables handle ownership, not a mock provider.
            streamer.HoldAllRegions=true;yield return streamer.WaitReady();
            AssertAllLoaded();AssertPersistentCore();
        }

        [UnityTest] public IEnumerator DestroyingHostDuringARealLoadDrainsTheSceneAndQueuedRequests()
        {
            streamer.HoldAllRegions=true;host.SetActive(true);
            Assert.That(streamer.PendingRegionCount,Is.GreaterThan(0));
            Assert.That(WorldRegionStreamer.OwnedSceneHandleCount,Is.GreaterThan(0));
            Object.Destroy(host);yield return null;
            yield return Await(()=>WorldRegionStreamer.OwnedSceneHandleCount==0&&LoadedEnvironmentScenes()==0,
                "A callback-owned queue must release a load even after its MonoBehaviour is gone.");
            AssertPersistentCore();
        }

        void AssertAllLoaded()
        {
            Assert.That(streamer.LoadedRegionCount,Is.EqualTo(5));
            Assert.That(WorldRegionStreamer.OwnedSceneHandleCount,Is.EqualTo(5));
            Assert.That(LoadedEnvironmentScenes(),Is.EqualTo(5));
            foreach(var region in catalog.Regions)
            {
                Assert.That(streamer.IsRegionLoaded(region.Id),Is.True,region.Id.ToString());
                string path=PathFor(region.Id);
                var scene=SceneManager.GetSceneByPath(path);
                Assert.That(scene.IsValid()&&scene.isLoaded,Is.True,path);
                int copies=0;
                for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).path==path)copies++;
                Assert.That(copies,Is.EqualTo(1),"Rapid direction changes cannot create duplicate additive scenes.");
                Assert.That(scene.GetRootGameObjects().Length,Is.GreaterThan(0));
                foreach(var root in scene.GetRootGameObjects())
                {
                    Assert.That(root.GetComponentsInChildren<M4SceneBootstrap>(true),Is.Empty);
                    Assert.That(root.GetComponentsInChildren<ElementalActor>(true),Is.Empty);
                    Assert.That(root.GetComponentsInChildren<Terrain>(true),Is.Empty);
                    Assert.That(root.GetComponentsInChildren<CharacterController>(true),Is.Empty);
                }
            }
        }
        void AssertPersistentCore()
        {
            Assert.That(SceneManager.GetActiveScene(),Is.EqualTo(fixture));
            Assert.That(persistentGround!=null&&persistentGround.enabled,Is.True);
            Assert.That(persistentGround.gameObject.scene,Is.EqualTo(fixture));
        }
        Vector3 FarOutside()
        {
            float x=catalog.Regions.Max(r=>r.WorldBounds.max.x)+catalog.UnloadDistance+1000;
            float z=catalog.Regions.Max(r=>r.WorldBounds.max.z)+catalog.UnloadDistance+1000;
            return new Vector3(x,0,z);
        }
        static string PathFor(M4RegionId id)=>"Assets/Orbis/Game/World/Scenes/Environment_"+id+".unity";
        static int LoadedEnvironmentScenes()
        {
            int n=0;
            for(int i=0;i<SceneManager.sceneCount;i++)
            {
                var scene=SceneManager.GetSceneAt(i);
                if(scene.isLoaded&&scene.path.StartsWith("Assets/Orbis/Game/World/Scenes/Environment_",StringComparison.Ordinal))n++;
            }
            return n;
        }
        static IEnumerator Await(Func<bool> ready,string message)
        {
            float deadline=Time.realtimeSinceStartup+45;
            while(!ready())
            {
                Assert.That(Time.realtimeSinceStartup,Is.LessThan(deadline),message);
                yield return null;
            }
        }
    }
}
