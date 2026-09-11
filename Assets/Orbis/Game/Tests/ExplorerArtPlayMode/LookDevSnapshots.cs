using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Game.Tests
{
    /// <summary>Opt-in evidence inside the existing two tests; no extra test or gameplay frame.</summary>
    internal static class LookDevSnapshots
    {
        [Serializable] sealed class RendererCost
        {
            public string name,kind;
            public int submeshes,materialSlots;
            public long triangles;
            public string[] materials,shaders;
        }
        [Serializable] sealed class Snapshot
        {
            public int recipeVersion=1;
            public string step,character,view,scene,pose="Idle layer 0 normalizedTime 0";
            public Vector3 cameraPosition,lookAt,pawnPosition;
            public float fieldOfView,yawDegrees=35;
            public int width,height,msaa;
            public string structuralScope="Active character mesh renderers admitted by this camera mask/frustum; includes weapon and outline clones. Conservative bounds, not occlusion/LOD/GPU draw-call measurements.";
            public int rendererCount,submeshCount,materialSlotCount,uniqueMaterials;
            public long triangleCount;
            public int outlineRendererCount,outlineMaterialSlots,silhouetteRendererCount;
            public long outlineTriangles;
            public RendererCost[] renderers;
            public CounterDelta drawCalls,setPassCalls;
            public string counterScope="Raw global ProfilerRecorder.CurrentValue change across one dedicated SingleCameraRequest through synchronous ReadPixels. Unity exposes no camera-specific counter; these deltas are contextual, not authoritative character draw calls.";
            public bool countersAreCameraAuthoritative=false;
            public string comparedWith;
            public string[] structuralWarnings=Array.Empty<string>();
        }
        [Serializable] sealed class CounterDelta
        {
            public bool available;
            public string marker;
            public long before,after,delta;
        }
        sealed class CounterProbe : IDisposable
        {
            ProfilerRecorder draw,setPass;
            public readonly CounterDelta Draw=new CounterDelta(),SetPass=new CounterDelta();
            public CounterProbe()
            {
                // Discover supported names instead of assuming editor/platform counter availability.
                var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);
                foreach(var handle in handles)
                {
                    var description=ProfilerRecorderHandle.GetDescription(handle);
                    string key=new string(description.Name.Where(char.IsLetterOrDigit).ToArray());
                    if(key=="DrawCallsCount"&&!Draw.available)Start(handle,description.Name,ref draw,Draw);
                    else if(key=="SetPassCallsCount"&&!SetPass.available)Start(handle,description.Name,ref setPass,SetPass);
                }
            }
            static void Start(ProfilerRecorderHandle handle,string name,ref ProfilerRecorder recorder,CounterDelta result)
            {
                recorder=new ProfilerRecorder(handle,1,ProfilerRecorderOptions.StartImmediately|ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
                result.available=recorder.Valid;result.marker=name;
            }
            public void Before(){if(Draw.available)Draw.before=draw.CurrentValue;if(SetPass.available)SetPass.before=setPass.CurrentValue;}
            public void After()
            {
                if(Draw.available){Draw.after=draw.CurrentValue;Draw.delta=Draw.after-Draw.before;}
                if(SetPass.available){SetPass.after=setPass.CurrentValue;SetPass.delta=SetPass.after-SetPass.before;}
            }
            public void Dispose(){if(draw.Valid)draw.Dispose();if(setPass.Valid)setPass.Dispose();}
        }

        internal static void CaptureIfRequested(string character,Animator animator,Transform pawn)
        {
            string step=ReadStep();if(step==null)return;
            Assert.That(SystemInfo.graphicsDeviceType,Is.Not.EqualTo(GraphicsDeviceType.Null),"-lookStep needs a graphics-enabled Unity test run.");
            Assert.That(step.Length,Is.InRange(1,64));
            Assert.That(step.All(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'),Is.True,"-lookStep is a filename label, not a path.");
            string safeName=new string(character.Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'?c:'_').ToArray());
            const string directory="TestResults/LookDev";Directory.CreateDirectory(directory);
            float speed=animator.speed;
            try
            {
                animator.Play("Idle",0,0f);animator.Update(0f);animator.speed=0;
                var left=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var right=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Assert.That(left,Is.Not.Null);Assert.That(right,Is.Not.Null);
                Vector3 horizontalRight=Vector3.ProjectOnPlane(right.position-left.position,Vector3.up).normalized;
                Assert.That(horizontalRight.sqrMagnitude,Is.GreaterThan(.99f));
                Vector3 forward=Vector3.Cross(horizontalRight,Vector3.up).normalized;
                Vector3 direction=Quaternion.AngleAxis(35f,Vector3.up)*forward;
                // Fixed pawn-local framing for the normalized 1.8 m avatars; no automatic bounds fit.
                Capture(step,safeName,"Full",animator,pawn,direction,pawn.position+Vector3.up*.92f,24f,2.30f,1200,1600,directory);
                Capture(step,safeName,"Close",animator,pawn,direction,pawn.position+Vector3.up*1.69f+forward*.02f,28f,.50f,1024,1024,directory);
                var weapon=animator.GetComponentsInChildren<Transform>().FirstOrDefault(x=>x.name==character+" Weapon");
                if(weapon!=null&&weapon.GetComponentsInChildren<MeshRenderer>().Any(x=>x.sharedMaterial!=null&&x.sharedMaterial.name=="WayfarerBlade"))
                {
                    var center=weapon.TransformPoint(new Vector3(0,.38f,0));
                    Capture(step,safeName,"Weapon",animator,pawn,weapon.TransformDirection(new Vector3(.25f,0,1)).normalized,
                        center,22f,1.25f,1024,1536,directory);
                }
            }
            finally{animator.speed=speed;}
        }

        static string ReadStep()
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length;i++)
            {
                if(args[i].StartsWith("-lookStep=",StringComparison.OrdinalIgnoreCase))return args[i].Substring(10);
                if(!string.Equals(args[i],"-lookStep",StringComparison.OrdinalIgnoreCase))continue;
                Assert.That(i+1,Is.LessThan(args.Length),"Supply a label after -lookStep.");return args[i+1];
            }
            return null;
        }

        static void Capture(string step,string character,string view,Animator animator,Transform pawn,Vector3 direction,Vector3 center,
            float fov,float verticalSpan,int width,int height,string directory)
        {
            var snapshot=new Snapshot{step=step,character=character,view=view,scene=pawn.gameObject.scene.name,
                pawnPosition=pawn.position,lookAt=center,fieldOfView=fov,width=width,height=height};
            float distance=verticalSpan/(2f*Mathf.Tan(fov*.5f*Mathf.Deg2Rad));
            snapshot.cameraPosition=center+direction*distance;
            string filename=step+"_"+character+"_"+view;
            using(var probe=new CounterProbe())
            {
                ExplorerArtTests.CaptureView("LookDev/"+filename,snapshot.cameraPosition,center,fov,width,height,
                    (camera,msaa)=>{snapshot.msaa=msaa;MeasureCharacter(snapshot,animator,camera);probe.Before();},probe.After);
                snapshot.drawCalls=probe.Draw;snapshot.setPassCalls=probe.SetPass;
            }
            CompareBaseline(snapshot,directory);
            File.WriteAllText(Path.Combine(directory,filename+".json"),JsonUtility.ToJson(snapshot,true));
            Debug.Log("ORBIS_LOOKDEV "+filename+" renderers="+snapshot.rendererCount+" slots="+snapshot.materialSlotCount+
                " triangles="+snapshot.triangleCount+" outlineSlots="+snapshot.outlineMaterialSlots+" MSAA="+snapshot.msaa);
            foreach(string warning in snapshot.structuralWarnings)Debug.LogWarning("ORBIS_LOOKDEV_COST "+filename+" "+warning);
        }

        static void MeasureCharacter(Snapshot result,Animator animator,Camera camera)
        {
            var planes=GeometryUtility.CalculateFrustumPlanes(camera);var rows=new List<RendererCost>();var unique=new HashSet<Material>();
            foreach(var renderer in animator.GetComponentsInChildren<Renderer>(true))
            {
                if(!renderer.enabled||!renderer.gameObject.activeInHierarchy||renderer.forceRenderingOff||
                    (camera.cullingMask&(1<<renderer.gameObject.layer))==0||!GeometryUtility.TestPlanesAABB(planes,renderer.bounds))continue;
                Mesh mesh=renderer is SkinnedMeshRenderer skin?skin.sharedMesh:
                    renderer.TryGetComponent<MeshFilter>(out var filter)?filter.sharedMesh:null;
                if(mesh==null)continue;
                var materials=renderer.sharedMaterials;
                long triangles=0;for(int i=0;i<mesh.subMeshCount;i++)if(mesh.GetTopology(i)==MeshTopology.Triangles)triangles+=mesh.GetIndexCount(i)/3;
                bool outline=renderer.name.StartsWith("Art Outline",StringComparison.Ordinal);
                bool silhouette=renderer.name.StartsWith("M3 Silhouette",StringComparison.Ordinal);
                rows.Add(new RendererCost{name=renderer.name,kind=outline?"outline":silhouette?"appearance silhouette":"source or weapon",
                    submeshes=mesh.subMeshCount,materialSlots=materials.Length,triangles=triangles,
                    materials=materials.Select(m=>m!=null?m.name:"<missing>").ToArray(),shaders=materials.Select(m=>m!=null&&m.shader!=null?m.shader.name:"<missing>").ToArray()});
                foreach(var material in materials)if(material!=null)unique.Add(material);
                result.rendererCount++;result.submeshCount+=mesh.subMeshCount;result.materialSlotCount+=materials.Length;result.triangleCount+=triangles;
                if(outline){result.outlineRendererCount++;result.outlineMaterialSlots+=materials.Length;result.outlineTriangles+=triangles;}
                if(silhouette)result.silhouetteRendererCount++;
            }
            result.uniqueMaterials=unique.Count;result.renderers=rows.OrderBy(row=>row.name,StringComparer.Ordinal).ToArray();
        }

        static void CompareBaseline(Snapshot result,string directory)
        {
            if(result.step=="Step01_Before")return;
            string file=Path.Combine(directory,"Step01_Before_"+result.character+"_"+result.view+".json");
            if(!File.Exists(file))return;
            var baseline=JsonUtility.FromJson<Snapshot>(File.ReadAllText(file));if(baseline==null||baseline.recipeVersion!=result.recipeVersion)return;
            result.comparedWith=Path.GetFileName(file);var warnings=new List<string>();
            // Conservative structural regression signals, not inferred GPU draw-call assertions.
            if(result.materialSlotCount>baseline.materialSlotCount*1.25f&&result.materialSlotCount-baseline.materialSlotCount>=4)
                warnings.Add("Camera-admitted material slots grew from "+baseline.materialSlotCount+" to "+result.materialSlotCount+" (>25%).");
            if(result.triangleCount>baseline.triangleCount*1.25&&result.triangleCount-baseline.triangleCount>=10000)
                warnings.Add("Camera-admitted triangles including outline copies grew from "+baseline.triangleCount+" to "+result.triangleCount+" (>25%).");
            result.structuralWarnings=warnings.ToArray();
        }
    }
}
