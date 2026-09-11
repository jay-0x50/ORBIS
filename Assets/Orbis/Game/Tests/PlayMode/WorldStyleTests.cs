using System.Collections;
using System.Linq;
using NUnit.Framework;
using Orbis.Game.World;
using Orbis.M3;
using Orbis.M4;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Orbis.Game.Tests
{
    public sealed class WorldStyleTests
    {
        WorldVisualTests fixture;
        [UnitySetUp] public IEnumerator Setup(){fixture=new WorldVisualTests();yield return fixture.Setup();}
        [UnityTearDown] public IEnumerator Cleanup()
        {
            var world=Object.FindAnyObjectByType<M4SceneBootstrap>();if(world!=null)world.Presentation.Ultimate.Cancel();
            yield return fixture.Cleanup();
        }
        [UnityTest] public IEnumerator WorldGradeRetainsReadableMaterialsContinuousAmbientAndBurstPriority()
        {
            var world=Object.FindAnyObjectByType<M4SceneBootstrap>();
            yield return world.GetComponent<WorldRegionStreamer>().LoadAll();
            Assert.That(world.GetComponent<WorldAtmosphere>(),Is.Not.Null);
            foreach(var terrain in world.GetComponentsInChildren<Terrain>())
                foreach(var layer in terrain.terrainData.terrainLayers)Assert.That(layer.smoothness,Is.Zero);
            var renderer=UniversalRenderPipeline.asset.rendererDataList[0] as UniversalRendererData;
            Assert.That(renderer.postProcessData,Is.Not.Null,"Volumes require the real URP post-pass resources.");
            var volume=Object.FindObjectsByType<Volume>().Single(v=>v.name=="World Style Volume");
            Assert.That(volume.priority,Is.EqualTo(20));
            Assert.That(volume.sharedProfile.TryGet<ColorAdjustments>(out var grade),Is.True);
            Assert.That(grade.contrast.value,Is.EqualTo(7));
            foreach(var lod in Object.FindObjectsByType<LODGroup>())
                if(lod.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterials.Any(m=>m!=null&&m.shader.name=="Orbis/World/Architecture")))
                    Assert.That(lod.fadeMode,Is.EqualTo(LODFadeMode.CrossFade));
            for(int z=-850;z<=850;z+=50)for(int x=-850;x<=850;x+=50)
            {
                Color a=WorldAtmosphere.AmbientAt(new Vector3(x,0,z)),b=WorldAtmosphere.AmbientAt(new Vector3(x+1,0,z));
                Assert.That(((Vector4)a-(Vector4)b).magnitude,Is.LessThan(.002f),"No elemental hard cut over a one-metre boundary crossing.");
                Assert.That(Mathf.Min(a.r,Mathf.Min(a.g,a.b)),Is.GreaterThan(.93f));
            }
            string step=WorldVisualTests.Argument("-worldStep");
            if(step=="World04_After")
            {
                var p=new Vector3(-72,0,-225);
                foreach(var terrain in world.GetComponentsInChildren<Terrain>())
                    if(p.x>=terrain.transform.position.x&&p.x<terrain.transform.position.x+1000&&p.z>=terrain.transform.position.z&&p.z<terrain.transform.position.z+1000)
                        p.y=terrain.SampleHeight(p)+terrain.transform.position.y+.15f;
                world.Warp(p);world.Traversal.transform.rotation=Quaternion.identity;
                for(int i=0;i<12;i++)yield return null;
                WorldVisualTests.Capture(step,"StellaWorld",p+new Vector3(2.8f,1.3f,3.7f),p+Vector3.up*1.02f,38);
            }
            bool burstOccurred=false,overridesBaseGrade=false;
            world.Presentation.Ultimate.StageChanged+=(stage,element,position)=>
            {
                if(stage!=M3UltimateStage.ElementBurst)return;
                burstOccurred=true;
                var cinematic=world.Presentation.Ultimate.ColorVolume;
                overridesBaseGrade=cinematic!=null&&cinematic.priority>volume.priority&&cinematic.weight>0;
            };
            Assert.That(world.Presentation.PlayUltimatePresentation(),Is.True);
            float deadline=Time.realtimeSinceStartup+5;
            // Shader warm-up may take longer than a Timeline stage. Inspect the actual signal callback,
            // rather than expecting an unscaled presentation to remain on the same stage after a stalled frame.
            while(!burstOccurred&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(burstOccurred,Is.True);
            Assert.That(overridesBaseGrade,Is.True,"Combat presentation must still override the base world grade.");
            world.Presentation.Ultimate.Cancel();yield return null;
            Assert.That(Time.timeScale,Is.EqualTo(1));
        }
    }
}
