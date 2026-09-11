using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Orbis.Game.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Orbis.Game.Tests
{
    public sealed class WorldGrassRenderingTests
    {
        Scene previous,fixture;
        [UnityTearDown] public IEnumerator CleanupScene()
        {
            if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
            if(fixture.IsValid()&&fixture.isLoaded)yield return SceneManager.UnloadSceneAsync(fixture);
        }
        [UnityTest] public IEnumerator ActualRenderRequestsCullBehindAndFarGrassAndReuseBoundedBatches()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||!SystemInfo.supportsInstancing)
                Assert.Ignore("This render integration check requires an instancing-capable graphics device.");
            previous=SceneManager.GetActiveScene();fixture=SceneManager.CreateScene("Grass render test");
            SceneManager.SetActiveScene(fixture);
            Mesh mesh=null;Material material=null;WorldGrassData data=null;RenderTexture target=null;
            try
            {
                mesh=new Mesh{name="Test grass blade"};
                mesh.vertices=new[]{new Vector3(-.3f,0,0),new Vector3(.3f,0,0),new Vector3(.3f,1,0),new Vector3(-.3f,1,0)};
                mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateNormals();mesh.RecalculateBounds();
                var shader=Shader.Find("Universal Render Pipeline/Unlit");Assert.That(shader,Is.Not.Null);
                material=new Material(shader){enableInstancing=true};
                data=ScriptableObject.CreateInstance<WorldGrassData>();var placements=new List<WorldGrassPlacement>();
                for(int i=0;i<600;i++)Add(placements,-8+(i%30)*.5f,20+(i/30)*.4f);
                for(int i=0;i<120;i++)Add(placements,-4+(i%20)*.4f,-80-(i/20)*.4f);
                for(int i=0;i<80;i++)Add(placements,-4+(i%20)*.4f,150+(i/20)*.4f);
                data.SetInstances(placements);
                var go=new GameObject("Grass draw fixture");go.layer=30;go.SetActive(false);
                var field=go.AddComponent<WorldGrassField>();field.Configure(mesh,material,data);go.SetActive(true);
                var camera=new GameObject("Grass inspection camera").AddComponent<Camera>();camera.enabled=false;
                camera.cullingMask=1<<30;camera.transform.position=new Vector3(0,1.5f,0);camera.farClipPlane=300;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                target=new RenderTexture(128,128,24);target.Create();
                yield return null;
                long buffers=field.ApproximateBufferBytes;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                Assert.That(field.VisibleInstanceCount,Is.EqualTo(600));
                Assert.That(field.DrawBatchCount,Is.EqualTo(3),"600 instances need only three draws with a 256-instance limit.");
                Assert.That(field.LastCameraEntityId,Is.EqualTo(camera.GetEntityId()));
                camera.transform.rotation=Quaternion.Euler(0,180,0);
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                Assert.That(field.VisibleInstanceCount,Is.EqualTo(120),"Changing the request camera must recull the authored population.");
                Assert.That(field.DrawBatchCount,Is.EqualTo(1));
                Assert.That(field.ApproximateBufferBytes,Is.EqualTo(buffers),"Camera changes reuse the prepared instance arrays.");
                camera.cullingMask=0;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                Assert.That(field.DrawBatchCount,Is.Zero);Assert.That(field.VisibleInstanceCount,Is.Zero);
                field.enabled=false;Assert.That(field.ApproximateBufferBytes,Is.Zero);
            }
            finally
            {
                if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
                // Destroy scene objects before releasing their transient mesh/material; the test runner scene is preserved.
                foreach(var go in fixture.GetRootGameObjects())Object.DestroyImmediate(go);
                if(target!=null){target.Release();Object.DestroyImmediate(target);}
                if(mesh!=null)Object.DestroyImmediate(mesh);
                if(material!=null)Object.DestroyImmediate(material);
                if(data!=null)Object.DestroyImmediate(data);
            }
            yield return null;
        }
        static void Add(List<WorldGrassPlacement> placements,float x,float z)
        {
            placements.Add(new WorldGrassPlacement{Position=new Vector3(x,0,z),Yaw=0,Scale=Vector3.one});
        }
    }
}
