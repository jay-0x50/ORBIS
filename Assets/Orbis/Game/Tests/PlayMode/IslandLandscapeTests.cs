using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.M0;
using Orbis.M2;
using Orbis.M4;
using Orbis.M16;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Orbis.Game.Tests
{
    public sealed class IslandLandscapeTests
    {
        string directory;
        M4SceneBootstrap world;
        GameSceneEntry entry;
        Keyboard keyboard;
        float scale,capture,fixedDelta;
        bool background,cursorVisible;
        CursorLockMode cursor;
        InputSettings.BackgroundBehavior inputBackground;
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scale=Time.timeScale;capture=Time.captureDeltaTime;fixedDelta=Time.fixedDeltaTime;
            background=Application.runInBackground;cursor=Cursor.lockState;cursorVisible=Cursor.visible;
            inputBackground=InputSystem.settings.backgroundBehavior;editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Time.timeScale=1;Time.captureDeltaTime=1f/60;Application.runInBackground=true;
            keyboard=InputSystem.AddDevice<Keyboard>();Keys();
            directory=Path.Combine(Path.GetTempPath(),"Orbis-Island-Landscape-"+Guid.NewGuid().ToString("N"));
            ExplorerJourney.Stop();M4Session.UseProgressForTests(new M4ProgressService(Path.Combine(directory,"profile.json")));
            yield return SceneManager.LoadSceneAsync(ExplorerJourney.IslandScene,LoadSceneMode.Single);
            yield return Frames(4);
            entry=Object.FindAnyObjectByType<GameSceneEntry>();world=entry.World;
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Keys();world?.Presentation?.Ultimate.Cancel();ExplorerJourney.Stop();
            var previous=SceneManager.GetActiveScene();var empty=SceneManager.CreateScene("Island landscape cleanup");SceneManager.SetActiveScene(empty);
            if(previous.IsValid()&&previous.isLoaded)yield return SceneManager.UnloadSceneAsync(previous);
            var router=Object.FindAnyObjectByType<M4RegionRouter>();if(router!=null)Object.Destroy(router.gameObject);
            yield return null;M4Session.UseProgressForTests(null);
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            Time.timeScale=scale;Time.captureDeltaTime=capture;Time.fixedDeltaTime=fixedDelta;
            Application.runInBackground=background;Cursor.lockState=cursor;Cursor.visible=cursorVisible;
            InputSystem.settings.backgroundBehavior=inputBackground;InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
            Assert.That(Path.GetFileName(directory),Does.StartWith("Orbis-Island-Landscape-"));
            Assert.That(Path.GetDirectoryName(directory),Is.EqualTo(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
        [UnityTest]
        public IEnumerator SavedIslandHasContinuousCollisionAtAllFiveSitesAndSelectionStartsOnTheSameTerrain()
        {
            Assert.That(entry.HasStarted,Is.False);
            var terrains=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            Assert.That(terrains.Length,Is.EqualTo(4));
            var bounds=new Bounds(terrains[0].transform.position+terrains[0].terrainData.size*.5f,terrains[0].terrainData.size);
            foreach(var terrain in terrains)
            {
                bounds.Encapsulate(new Bounds(terrain.transform.position+terrain.terrainData.size*.5f,terrain.terrainData.size));
                Assert.That(terrain.GetComponent<TerrainCollider>().terrainData,Is.SameAs(terrain.terrainData));
                Assert.That(terrain.terrainData.heightmapResolution,Is.GreaterThanOrEqualTo(513));
            }
            Assert.That(bounds.size.x,Is.EqualTo(2000).Within(.01));Assert.That(bounds.size.z,Is.EqualTo(2000).Within(.01));
            foreach(var region in world.IslandRegions)
            {
                foreach(var point in new[]{region.Authored.Spawn.position,region.Authored.Npc.position,region.Authored.BossObject.transform.position})
                {
                    Assert.That(Physics.Raycast(point+Vector3.up*25,Vector3.down,out var hit,50,1<<8),Is.True,"No ground under "+region.Id);
                    Assert.That(hit.point.y,Is.EqualTo(point.y).Within(.3f),"Content floats or is buried in "+region.Id);
                }
            }
            Assert.That(Object.FindObjectsByType<WaterVolume>().Length,Is.GreaterThanOrEqualTo(2));
            Assert.That(Object.FindObjectsByType<ClimbableSurface>().Length,Is.GreaterThanOrEqualTo(1));
            Capture(entry.OverviewCamera,"Island_Overview.png");
            var handle=SceneManager.GetActiveScene().handle;
            var selection=Object.FindAnyObjectByType<ExplorerSelectionScreen>();
            Assert.That(selection.Select(ExplorerChoice.Stella),Is.True);Assert.That(selection.BeginJourney(),Is.True,selection.Notice);
            yield return Frames(100);
            Assert.That(SceneManager.GetActiveScene().handle,Is.EqualTo(handle));
            CollectionAssert.AreEquivalent(terrains,Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None));
            Assert.That(world.Region,Is.EqualTo(M4RegionId.Zephyr));
            Vector3 start=world.Traversal.transform.position;
            Keys(Key.W);yield return Frames(60);Keys();yield return Frames(5);
            Assert.That(Vector3.Distance(start,world.Traversal.transform.position),Is.GreaterThan(2));
            Assert.That(world.Traversal.transform.position.y,Is.EqualTo(start.y).Within(.6f));
            Capture(Camera.main,"Island_Player.png");
            foreach(var id in new[]{M4RegionId.Agnia,M4RegionId.Teluna,M4RegionId.Granite,M4RegionId.Voltheim})
            {
                var region=world.GetRegion(id);
                var cameraObject=new GameObject("Scenic review camera");var camera=cameraObject.AddComponent<Camera>();
                camera.CopyFrom(Camera.main);camera.enabled=false;camera.fieldOfView=60;
                Vector3 focus=region.Center;
                camera.transform.position=focus+new Vector3(-65,55,-85);camera.transform.LookAt(focus+Vector3.up*5);
                Capture(camera,"Island_"+id+".png");Object.Destroy(cameraObject);
            }
        }
        static void Capture(Camera camera,string name)
        {
            bool previous=ShaderUtilAsync;
            ShaderUtilAsync=false;
            var texture=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);texture.Create();
            var image=new Texture2D(1600,900,TextureFormat.RGB24,false);var active=RenderTexture.active;
            try
            {
                camera.GetUniversalAdditionalCameraData();
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=texture});
                RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
                Directory.CreateDirectory("TestResults");File.WriteAllBytes(Path.Combine("TestResults",name),image.EncodeToPNG());
            }
            finally{RenderTexture.active=active;Object.Destroy(image);texture.Release();Object.Destroy(texture);ShaderUtilAsync=previous;}
        }
        static bool ShaderUtilAsync
        {
            get
            {
                #if UNITY_EDITOR
                return UnityEditor.ShaderUtil.allowAsyncCompilation;
                #else
                return false;
                #endif
            }
            set
            {
                #if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation=value;
                #endif
            }
        }
        void Keys(params Key[] keys){if(keyboard!=null)InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));}
        static IEnumerator Frames(int count){for(int i=0;i<count;i++)yield return null;}
    }
}
