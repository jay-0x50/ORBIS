using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Actual common-toon stills and calibrated Blender pose transfer, without saved pose changes.</summary>
    public static class StellaLegCandidatePreview
    {
        [Serializable] sealed class Bone {public string name,parent;public float[] matrix;}
        [Serializable] sealed class Pose {public string name;public Bone[] bones;}
        [Serializable] sealed class Source {public string source,sourceSha256Unchanged;public Bone[] rest;public Pose[] poses;}
        [Serializable] sealed class Evidence
        {
            public string view,pose,prefab,modelSha256,sourcePoseSha256,graphics,shader,note;
            public float normalStrength,humanScale,restFitRms,restFitMaximum,poseFitMaximum,fitScale,parity;
            public Vector3 camera,target;public float fieldOfView;public bool outline,keyShadows;
        }
        sealed class Rest
        {
            readonly Transform[] transforms;readonly Vector3[] positions,scales;readonly Quaternion[] rotations;
            public Rest(Transform root){transforms=root.GetComponentsInChildren<Transform>(true);positions=transforms.Select(t=>t.localPosition).ToArray();scales=transforms.Select(t=>t.localScale).ToArray();rotations=transforms.Select(t=>t.localRotation).ToArray();}
            public void Restore(){for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=positions[i];transforms[i].localRotation=rotations[i];transforms[i].localScale=scales[i];}}
        }
        public static void CaptureRequested()
        {
            string run=Argument("-stellaLegRun")??"Integrated06Toon01";
            if(!run.All(c=>char.IsLetterOrDigit(c)||c=='_'))throw new ArgumentException("Simple fresh review label required.");
            string output="TestResults/CharacterPipeline/UnityPreview/Stella/"+run;
            if(Directory.Exists(output))throw new IOException("Preserve previous evidence: choose a fresh run label.");
            string poseFile="Tools/CharacterPipeline/PendingImport/StellaLegIntegrated06/ReviewPoses.json";
            var source=JsonUtility.FromJson<Source>(File.ReadAllText(poseFile));
            var guard=StellaLegCandidateImport.ProtectedSnapshot();string modelHash=StellaLegCandidateImport.Hash(StellaLegCandidateImport.Folder+"/Stella.fbx");
            Directory.CreateDirectory(output);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var host=new GameObject("Transient Stella local seam review");
            var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(StellaLegCandidateImport.Prefab),host.transform);
            var animator=model.GetComponent<Animator>();var importedRest=new Rest(model.transform);
            animator.Rebind();animator.Play("Idle",0,0);animator.Update(0);
            ArtCharacterRoster.NormalizeVisibleModelHeight(host,1.8f,Vector3.zero);
            var idle=new Rest(model.transform);animator.enabled=false;
            var materials=new List<Material>();
            foreach(var skin in host.GetComponentsInChildren<SkinnedMeshRenderer>())skin.sharedMaterials=skin.sharedMaterials.Select(m=>{var clone=new Material(m);materials.Add(clone);return clone;}).ToArray();
            ArtStyle.ApplyToHierarchy(host,Resources.Load<ArtAssetCatalog>("Art/Catalog"),true);
            foreach(var skin in host.GetComponentsInChildren<SkinnedMeshRenderer>(true))skin.forceMatrixRecalculationPerRender=true;
            var outlines=host.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("Art Outline",StringComparison.Ordinal)).ToArray();
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.28f,.30f,.34f);RenderSettings.fog=false;
            var light=new GameObject("Preview key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.color=Color.white;
            light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(42,-35,0);RenderSettings.sun=light;
            var camera=new GameObject("Preview camera").AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.14f,.16f,.20f);camera.nearClipPlane=.03f;camera.farClipPlane=30;camera.allowHDR=true;camera.allowMSAA=true;camera.useOcclusionCulling=false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var table=source.rest.ToDictionary(b=>b.name);var transforms=model.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.Single());
            if(source.rest.Any(b=>!transforms.ContainsKey(b.name)))throw new InvalidOperationException("Source/imported skeleton name mismatch");
            importedRest.Restore();
            Vector3 Src(string n)=>Position(Matrix(table[n].matrix));Vector3 Dst(string n)=>transforms[n].position;
            var a=Basis(Src("RightHand")-Src("LeftHand"),Src("Head")-Src("Hips"));var b=Basis(Dst("RightHand")-Dst("LeftHand"),Dst("Head")-Dst("Hips"));
            Matrix4x4 conversion=Matrix4x4.identity;float scale=1,rms=float.PositiveInfinity,max=0;Vector3 translation=Vector3.zero;
            foreach(float parity in new[]{1f,-1f})
            {
                var c=b*Matrix4x4.Scale(new Vector3(1,1,parity))*a.inverse;float numerator=0,denominator=0;
                foreach(var bone in source.rest){var x=Position(Matrix(bone.matrix))-Src("Hips");var y=Dst(bone.name)-Dst("Hips");numerator+=Vector3.Dot(c.MultiplyVector(x),y);denominator+=x.sqrMagnitude;}
                float k=numerator/denominator;var offset=Dst("Hips")-c.MultiplyVector(Src("Hips"))*k;float sum=0,worst=0;
                foreach(var bone in source.rest){float error=Vector3.Distance(c.MultiplyVector(Position(Matrix(bone.matrix)))*k+offset,Dst(bone.name));sum+=error*error;worst=Mathf.Max(worst,error);}
                float candidate=Mathf.Sqrt(sum/source.rest.Length);if(candidate<rms){conversion=c;scale=k;translation=offset;rms=candidate;max=worst;}
            }
            if(max>.0005f||scale<=0)throw new InvalidOperationException("Source/imported rest basis does not fit within .5mm: "+max);
            var restWorld=source.rest.ToDictionary(x=>x.name,x=>transforms[x.name].rotation);
            float ApplyPose(string name)
            {
                importedRest.Restore();var pose=source.poses.Single(p=>p.name==name);
                // Exact source world deltas, ordered parent before child, avoid guessed FBX local axes.
                foreach(var bone in pose.bones)
                {
                    var delta=RotationMatrix(Matrix(bone.matrix))*RotationMatrix(Matrix(table[bone.name].matrix)).inverse;
                    var rotation=(conversion*delta*conversion.inverse).rotation*restWorld[bone.name];
                    var position=conversion.MultiplyVector(Position(Matrix(bone.matrix)))*scale+translation;
                    transforms[bone.name].SetPositionAndRotation(position,rotation);
                }
                float error=pose.bones.Max(p=>Vector3.Distance(transforms[p.name].position,conversion.MultiplyVector(Position(Matrix(p.matrix)))*scale+translation));
                if(error>.0005f)throw new InvalidOperationException("Source pose transfer exceeds .5mm: "+error);
                return error;
            }
            try
            {
                var views=new[]{
                    ("Body","Idle",new Vector3(3.2f,2.1f,4.5f),new Vector3(0,.9f,0),28f),
                    ("Face","Idle",new Vector3(0,1.64f,2.1f),new Vector3(0,1.61f,0),16f),
                    ("Torso","Idle",new Vector3(1.2f,1.3f,2.8f),new Vector3(0,1.15f,0),23f),
                    ("Legs","Idle",new Vector3(0,.55f,2.1f),new Vector3(0,.55f,0),18f),
                    ("BentLegs","WalkContact",new Vector3(1.1f,.65f,2f),new Vector3(0,.6f,0),25f),
                    ("KneeFlex","JointStress",new Vector3(1.1f,.65f,2f),new Vector3(0,.6f,0),31f)};
                foreach(var view in views)
                {
                    float poseError=0;if(view.Item2=="Idle")idle.Restore();else poseError=ApplyPose(view.Item2);
                    bool diagnostic=view.Item1=="Legs"||view.Item1=="BentLegs"||view.Item1=="KneeFlex";
                    var settings=diagnostic?new[]{(0f,true),(1f,true),(0f,false),(1f,false)}:new[]{(0f,true),(1f,true)};
                    foreach(var setting in settings)
                    {
                        foreach(var material in materials)material.SetFloat("_NormalStrength",setting.Item1);
                        foreach(var outline in outlines)outline.enabled=setting.Item2;
                        camera.transform.SetPositionAndRotation(view.Item3,Quaternion.LookRotation(view.Item4-view.Item3));camera.fieldOfView=view.Item5;
                        string path=output+"/"+view.Item1+(setting.Item1>0?"_NormalOn":"_NormalOff")+(setting.Item2?"_OutlineOn":"_OutlineOff");
                        CharacterPipelinePreview.Render(camera,path+".png");
                        File.WriteAllText(path+".json",JsonUtility.ToJson(new Evidence{view=view.Item1,pose=view.Item2,prefab=StellaLegCandidateImport.Prefab,modelSha256=modelHash,
                            sourcePoseSha256=source.sourceSha256Unchanged,graphics=SystemInfo.graphicsDeviceName,shader=AssetDatabase.GetAssetPath(materials[0].shader),
                            normalStrength=setting.Item1,humanScale=animator.humanScale,restFitRms=rms,restFitMaximum=max,poseFitMaximum=poseError,fitScale=scale,parity=conversion.determinant,
                            camera=view.Item3,target=view.Item4,fieldOfView=view.Item5,outline=setting.Item2,keyShadows=true,
                            note="Actual common Unity URP toon+outline. Idle cameras match existing source review. BentLegs/KneeFlex use measured Blender rest-basis +/-parity fit and exact world pose deltas. Material/pose overrides transient. Mask retained but common toon has no Mask input. Visible steps/folds remain acceptance issues; no final approval."},true));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(host);Object.DestroyImmediate(light.gameObject);Object.DestroyImmediate(camera.gameObject);
                foreach(var material in materials)Object.DestroyImmediate(material);StellaLegCandidateImport.AssertProtected(guard);
            }
            if(StellaLegCandidateImport.Hash(StellaLegCandidateImport.Folder+"/Stella.fbx")!=modelHash)throw new InvalidOperationException("Preview changed source FBX");
            Debug.Log("ORBIS_STELLA_LOCAL_SEAM_PREVIEW "+output);
        }
        static Matrix4x4 Basis(Vector3 x,Vector3 up){x.Normalize();up=(up-x*Vector3.Dot(up,x)).normalized;var m=Matrix4x4.identity;m.SetColumn(0,new Vector4(x.x,x.y,x.z,0));m.SetColumn(1,new Vector4(up.x,up.y,up.z,0));var z=Vector3.Cross(x,up);m.SetColumn(2,new Vector4(z.x,z.y,z.z,0));return m;}
        static Matrix4x4 RotationMatrix(Matrix4x4 m){for(int i=0;i<3;i++){var v=((Vector3)m.GetColumn(i)).normalized;m.SetColumn(i,new Vector4(v.x,v.y,v.z,0));}m.SetColumn(3,new Vector4(0,0,0,1));return m;}
        static Matrix4x4 Matrix(float[] values){if(values==null||values.Length!=16)throw new ArgumentException("Matrix16 required");var m=Matrix4x4.zero;for(int r=0;r<4;r++)for(int c=0;c<4;c++)m[r,c]=values[r*4+c];return m;}
        static Vector3 Position(Matrix4x4 m)=>m.GetColumn(3);
        static string Argument(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:null;}
    }
}
