using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Orbis.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Measured Blender pose to Humanoid review; never mutates production catalog, rig or clips.</summary>
    public static class HeroGripTransfer
    {
        [Serializable] sealed class BoneRecord { public string name,parent; public bool applyFinger; public float[] restFlat,poseFlat; }
        [Serializable] sealed class Source { public string source,sourceSha256Unchanged; public BoneRecord[] bones; public float[] weaponFlat; public Vector3[] weaponPoints; }
        [Serializable] sealed class Muscle { public int index; public string name; public float value; }
        [Serializable] sealed class Evidence
        {
            public string character,source,sourceSha256,model,run,status;
            public float restFitRms,restFitMax,bodySourceToWorldScale,basisDeterminant,weaponLocalGeometryFitMax,weaponUnitScale;
            public float directPoseBoneMax,humanoidRoundtripBoneMax,handRelativeBoneMax,handRelativeSkinMax,weaponWorldLength;
            public float fittedHandRelativeBoneMax,fittedHandRelativeSkinMax,fitInitialCost,fitFinalCost;
            public bool fitted;
            public Vector3 socketLocalPosition,socketLocalEuler,socketLocalScale;
            public Muscle[] rightFingerMuscles;
            public float[] bodyBasis,weaponBasis;
        }
        sealed class Rest
        {
            public Transform[] transforms; public Vector3[] positions,scales; public Quaternion[] rotations;
            public Rest(Transform root)
            { transforms=root.GetComponentsInChildren<Transform>(true); positions=transforms.Select(t=>t.localPosition).ToArray(); scales=transforms.Select(t=>t.localScale).ToArray(); rotations=transforms.Select(t=>t.localRotation).ToArray(); }
            public void Restore() { for(int i=0;i<transforms.Length;i++) { transforms[i].localPosition=positions[i]; transforms[i].localScale=scales[i]; transforms[i].localRotation=rotations[i]; } }
        }
        public static void RunRequested()
        {
            string character=Arg("-gripCharacter")??"Polaris";
            if(character!="Polaris"&&character!="Stella")throw new ArgumentException("-gripCharacter selects Polaris or Stella.");
            string sourcePath=Arg("-gripSource")??(character=="Polaris"?"TestResults/CharacterPipeline/RigReview/PolarisGrip03/PoseTransferSource.json":"TestResults/CharacterPipeline/RigReview/StellaGrip02/PoseTransferSource.json");
            string prefabPath=Arg("-gripPrefab")??(character=="Polaris"?"Assets/Orbis/Game/Characters/Candidates/OriginTrial01/Polaris/Polaris.prefab":"Assets/Orbis/Game/Characters/Candidates/StellaIntegrated12/Stella/Stella.prefab");
            string run=Arg("-gripRun")??"Transfer01";
            if(!run.All(c=>char.IsLetterOrDigit(c)||c=='_')) throw new ArgumentException("Simple fresh review name required.");
            string output="TestResults/CharacterPipeline/UnityGripReview/"+character+"/"+run;
            if(Directory.Exists(output)) throw new IOException("Preserve prior evidence.");
            var source=JsonUtility.FromJson<Source>(File.ReadAllText(sourcePath));
            using(var input=File.OpenRead(source.source)) using(var hash=System.Security.Cryptography.SHA256.Create())
                if(BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant()!=source.sourceSha256Unchanged)
                    throw new InvalidOperationException("The reviewed grip source changed after pose export.");
            if(source.bones==null || source.weaponPoints==null || source.weaponPoints.Length<100) throw new InvalidOperationException("Complete observed source required.");
            var table=source.bones.ToDictionary(b=>b.name);
            Directory.CreateDirectory(output); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var host=new GameObject("Transient measured grip review");
            var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath),host.transform);
            var animator=model.GetComponent<Animator>(); var importedRest=new Rest(model.transform);
            animator.applyRootMotion=false; animator.Rebind(); animator.Play("Idle",0,0); animator.Update(0);
            ArtCharacterRoster.NormalizeVisibleModelHeight(host,1.8f,Vector3.zero);
            animator.enabled=false; importedRest.Restore();
            var rest=new Rest(model.transform);
            var transforms=model.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.Single());
            var matched=source.bones.Where(b=>transforms.ContainsKey(b.name)).ToArray();
            if(matched.Length!=source.bones.Length) throw new InvalidOperationException("Source/imported bone names differ.");
            Vector3 Src(string n)=>Position(Matrix(table[n].restFlat));
            Vector3 Dst(string n)=>transforms[n].position;
            var a=Basis(Src("RightHand")-Src("LeftHand"),Src("Head")-Src("Hips"));
            var b=Basis(Dst("RightHand")-Dst("LeftHand"),Dst("Head")-Dst("Hips"));
            Matrix4x4 conversion=Matrix4x4.identity; float scale=1,rms=float.PositiveInfinity,max=0; Vector3 translation=Vector3.zero;
            foreach(float parity in new[]{1f,-1f})
            {
                var reflection=Matrix4x4.Scale(new Vector3(1,1,parity)); var c=b*reflection*a.inverse;
                float numerator=0,denominator=0;
                foreach(var bone in matched)
                {
                    var x=Position(Matrix(bone.restFlat))-Src("Hips"); var y=Dst(bone.name)-Dst("Hips");
                    numerator+=Vector3.Dot(c.MultiplyVector(x),y); denominator+=x.sqrMagnitude;
                }
                float k=numerator/denominator; var offset=Dst("Hips")-c.MultiplyVector(Src("Hips"))*k; float sum=0,worst=0;
                foreach(var bone in matched)
                {
                    float error=Vector3.Distance(c.MultiplyVector(Position(Matrix(bone.restFlat)))*k+offset,Dst(bone.name));
                    sum+=error*error; worst=Mathf.Max(worst,error);
                }
                float candidate=Mathf.Sqrt(sum/matched.Length);
                if(candidate<rms) { conversion=c;scale=k;translation=offset;rms=candidate;max=worst; }
            }
            if(scale<=0 || max>.0005f) throw new InvalidOperationException("Source/import rest fit exceeds .5mm: "+max);
            var restWorld=matched.ToDictionary(x=>x.name,x=>transforms[x.name].rotation);
            foreach(var bone in matched.Where(x=>x.applyFinger))
            {
                var delta=RotationMatrix(Matrix(bone.poseFlat))*RotationMatrix(Matrix(bone.restFlat)).inverse;
                var converted=conversion*delta*conversion.inverse;
                transforms[bone.name].rotation=converted.rotation*restWorld[bone.name];
            }
            float PoseError()
            {
                float e=0;
                foreach(var bone in matched.Where(x=>x.applyFinger))
                    e=Mathf.Max(e,Vector3.Distance(transforms[bone.name].position,conversion.MultiplyVector(Position(Matrix(bone.poseFlat)))*scale+translation));
                return e;
            }
            float directError=PoseError();
            if(directError>.001f) throw new InvalidOperationException("Direct finger world rotation transfer failed: "+directError);
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            var weapon=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Orbis/Game/LookDev/Resources/LookDev/WayfarerBlade.prefab"));
            var meshPoints=weapon.GetComponentsInChildren<MeshFilter>(true).SelectMany(f=>f.sharedMesh.vertices.Select(v=>weapon.transform.InverseTransformPoint(f.transform.TransformPoint(v)))).ToArray();
            var weaponAxis=FitWeaponBasis(source.weaponPoints,meshPoints,Mathf.Sign(conversion.determinant),out float weaponUnits,out float geometryError);
            if(geometryError>.0005f) throw new InvalidOperationException("Weapon source/import geometry fit exceeds .5mm: "+geometryError);
            var bladeSource=Matrix(source.weaponFlat);
            var weaponRotation=(conversion*RotationMatrix(bladeSource)*weaponAxis.inverse).rotation;
            var weaponCenter=conversion.MultiplyVector(Position(bladeSource))*scale+translation;
            weapon.transform.SetPositionAndRotation(weaponCenter,weaponRotation);
            weapon.transform.localScale=Vector3.one/weaponUnits;
            weapon.transform.SetParent(hand,true);
            var catalog=Resources.Load<ArtAssetCatalog>("Art/Catalog");
            ArtStyle.ApplyToHierarchy(host,catalog,true);
            foreach(var skin in host.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.forceMatrixRecalculationPerRender=true;
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.28f,.30f,.34f); RenderSettings.fog=false;
            var key=new GameObject("Grip key").AddComponent<Light>(); key.type=LightType.Directional;key.intensity=1.1f;key.color=Color.white;key.shadows=LightShadows.Soft;key.transform.rotation=Quaternion.Euler(42,-35,0);
            RenderSettings.sun=key;
            var camera=new GameObject("Grip camera").AddComponent<Camera>(); camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.14f,.16f,.20f);
            camera.orthographic=true;camera.orthographicSize=.16f;camera.nearClipPlane=.001f;camera.farClipPlane=20;camera.allowHDR=true;camera.allowMSAA=true;camera.useOcclusionCulling=false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            void Capture(string variant)
            {
                foreach(var view in new[]{("Front",new Vector3(0,-1,.13f)),("Top",new Vector3(-.1f,-.15f,1)),("Palm",new Vector3(-.12f,-.3f,-1))})
                {
                    var direction=conversion.MultiplyVector(view.Item2).normalized;
                    var up=conversion.MultiplyVector(Vector3.forward);
                    if(Mathf.Abs(Vector3.Dot(up,direction))>.9f) up=conversion.MultiplyVector(Vector3.up);
                    camera.transform.SetPositionAndRotation(weaponCenter+direction,Quaternion.LookRotation(-direction,up));
                    CharacterPipelinePreview.Render(camera,output+"/"+variant+"_"+view.Item1+".png");
                }
            }
            Capture("DirectSourcePose");
            Vector3 RelativePoint(Vector3 world)=>Quaternion.Inverse(hand.rotation)*(world-hand.position);
            var fingerBones=matched.Where(x=>x.applyFinger).Select(x=>transforms[x.name]).ToArray();
            var directRelativeBones=fingerBones.Select(t=>RelativePoint(t.position)).ToArray();
            var directRelativeRotations=fingerBones.Select(t=>Quaternion.Inverse(hand.rotation)*t.rotation).ToArray();
            var bodySkin=model.GetComponentsInChildren<SkinnedMeshRenderer>().First(s=>!s.name.Contains("Outline"));
            var handBoneIndices=Enumerable.Range(0,bodySkin.bones.Length).Where(i=>bodySkin.bones[i]==hand||bodySkin.bones[i].IsChildOf(hand)).ToHashSet();
            var skinWeights=bodySkin.sharedMesh.boneWeights;
            var handVertices=Enumerable.Range(0,bodySkin.sharedMesh.vertexCount).Where(i=>
            {
                var w=skinWeights[i];
                return (handBoneIndices.Contains(w.boneIndex0)?w.weight0:0)+(handBoneIndices.Contains(w.boneIndex1)?w.weight1:0)+
                    (handBoneIndices.Contains(w.boneIndex2)?w.weight2:0)+(handBoneIndices.Contains(w.boneIndex3)?w.weight3:0)>.95f;
            }).ToArray();
            Vector3[] HandSurface()
            {
                var baked=new Mesh();bodySkin.BakeMesh(baked,true);var v=baked.vertices;
                var result=handVertices.Select(i=>RelativePoint(bodySkin.transform.TransformPoint(v[i]))).ToArray();
                Object.DestroyImmediate(baked);return result;
            }
            var directRelativeSkin=HandSurface();
            Muscle[] muscles; float roundtripError,relativeBoneError,relativeSkinError;
            float fittedBoneError=0,fittedSkinError=0,initialCost=0,finalCost=0;
            bool fit=Arg("-gripFit")=="true";
            using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))
            {
                var pose=new HumanPose { muscles=new float[HumanTrait.MuscleCount] };handler.GetHumanPose(ref pose);
                muscles=Enumerable.Range(0,pose.muscles.Length).Where(i=>IsRightFinger(HumanTrait.MuscleName[i])).Select(i=>new Muscle { index=i,name=HumanTrait.MuscleName[i],value=pose.muscles[i] }).ToArray();
                if(muscles.Length!=20 || muscles.Any(m=>float.IsNaN(m.value)||float.IsInfinity(m.value))) throw new InvalidOperationException("Expected20 finite right finger muscles.");
                rest.Restore();handler.SetHumanPose(ref pose);roundtripError=PoseError();
                relativeBoneError=fingerBones.Select((t,i)=>Vector3.Distance(RelativePoint(t.position),directRelativeBones[i])).Max();
                relativeSkinError=HandSurface().Select((v,i)=>Vector3.Distance(v,directRelativeSkin[i])).Max();
                Capture("HumanoidRoundtrip");
                if(fit)
                {
                    // Fit only the20 right-finger Humanoid channels. Rest rig, skin weights,
                    // body pose and measured sword socket remain fixed. This is a candidate
                    // optimization against the visually inspected source, not art acceptance.
                    float Cost()
                    {
                        handler.SetHumanPose(ref pose);float cost=0;
                        for(int i=0;i<fingerBones.Length;i++)
                        {
                            cost+=(RelativePoint(fingerBones[i].position)-directRelativeBones[i]).sqrMagnitude;
                            float angle=Quaternion.Angle(Quaternion.Inverse(hand.rotation)*fingerBones[i].rotation,directRelativeRotations[i])*Mathf.Deg2Rad;
                            //25mm reference lever preserves distal finger orientation as well as joint positions.
                            cost+=angle*angle*.025f*.025f;
                        }
                        return cost;
                    }
                    initialCost=finalCost=Cost();
                    foreach(float step in new[]{.16f,.08f,.04f,.02f,.01f})for(int sweep=0;sweep<4;sweep++)
                        foreach(var muscle in muscles)
                        {
                            float original=pose.muscles[muscle.index],bestValue=original;
                            foreach(float sign in new[]{-1f,1f})
                            {
                                //Unity permits normalized channels beyond1. The bounded1.5 envelope
                                //includes measured source bends while excluding unconstrained fitting.
                                float value=Mathf.Clamp(original+sign*step,-1.5f,1.5f);
                                pose.muscles[muscle.index]=value;float cost=Cost();
                                if(cost<finalCost){finalCost=cost;bestValue=value;}
                            }
                            pose.muscles[muscle.index]=bestValue;
                        }
                    handler.SetHumanPose(ref pose);
                    muscles=muscles.Select(m=>new Muscle{index=m.index,name=m.name,value=pose.muscles[m.index]}).ToArray();
                    fittedBoneError=fingerBones.Select((t,i)=>Vector3.Distance(RelativePoint(t.position),directRelativeBones[i])).Max();
                    fittedSkinError=HandSurface().Select((v,i)=>Vector3.Distance(v,directRelativeSkin[i])).Max();
                    Capture("FittedHumanoid");
                }
            }
            var positions=weapon.GetComponentsInChildren<MeshFilter>().SelectMany(f=>f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v))).ToArray();
            Vector3 axis=weapon.transform.up;
            float length=positions.Max(v=>Vector3.Dot(v,axis))-positions.Min(v=>Vector3.Dot(v,axis));
            File.WriteAllText(output+"/MeasuredTransfer.json",JsonUtility.ToJson(new Evidence {
                character=character,source=sourcePath,sourceSha256=source.sourceSha256Unchanged,model=prefabPath,run=run,
                restFitRms=rms,restFitMax=max,bodySourceToWorldScale=scale,basisDeterminant=conversion.determinant,
                weaponLocalGeometryFitMax=geometryError,weaponUnitScale=weaponUnits,directPoseBoneMax=directError,humanoidRoundtripBoneMax=roundtripError,
                handRelativeBoneMax=relativeBoneError,handRelativeSkinMax=relativeSkinError,
                fitted=fit,fittedHandRelativeBoneMax=fittedBoneError,fittedHandRelativeSkinMax=fittedSkinError,fitInitialCost=initialCost,fitFinalCost=finalCost,
                socketLocalPosition=weapon.transform.localPosition,socketLocalEuler=weapon.transform.localEulerAngles,socketLocalScale=weapon.transform.localScale,
                rightFingerMuscles=muscles,bodyBasis=Flat(conversion),weaponBasis=Flat(weaponAxis),weaponWorldLength=length,
                status="Measured transfer candidate; source grip contact remains under visual review. No catalog, motion clips, rig or source mesh changed."
            },true));
            Debug.Log("ORBIS_GRIP_TRANSFER "+output+" direct="+directError+" roundtrip="+roundtripError+" weapon="+length);
            Object.DestroyImmediate(host);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(key.gameObject);
        }
        static bool IsRightFinger(string name)=>name.StartsWith("Right ") && new[]{"Thumb","Index","Middle","Ring","Little"}.Any(f=>name.Contains(f));
        static Matrix4x4 FitWeaponBasis(Vector3[] source,Vector3[] target,float parity,out float scale,out float max)
        {
            float sr=source.Max(v=>v.magnitude),tr=target.Max(v=>v.magnitude);scale=tr/sr;
            var best=Matrix4x4.identity;float error=float.PositiveInfinity;
            int[][] permutations={new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}};
            foreach(var p in permutations) for(int bits=0;bits<8;bits++)
            {
                var c=Matrix4x4.zero;c[3,3]=1;
                for(int i=0;i<3;i++)c[p[i],i]=(bits&(1<<i))==0?1:-1;
                if(Mathf.Sign(c.determinant)!=parity)continue;
                float total=0;
                for(int i=0;i<source.Length;i+=19)
                {
                    var q=c.MultiplyVector(source[i])*scale;float near=float.PositiveInfinity;
                    foreach(var v in target) near=Mathf.Min(near,(q-v).sqrMagnitude);
                    total+=near;
                }
                if(total<error){error=total;best=c;}
            }
            max=0;
            foreach(var v in source)
            {
                var q=best.MultiplyVector(v)*scale;float near=float.PositiveInfinity;
                foreach(var t in target)near=Mathf.Min(near,(q-t).sqrMagnitude);
                max=Mathf.Max(max,Mathf.Sqrt(near));
            }
            return best;
        }
        static Matrix4x4 Basis(Vector3 x,Vector3 up)
        {
            x.Normalize();up=(up-x*Vector3.Dot(up,x)).normalized;
            var m=Matrix4x4.identity;m.SetColumn(0,new Vector4(x.x,x.y,x.z,0));m.SetColumn(1,new Vector4(up.x,up.y,up.z,0));
            Vector3 z=Vector3.Cross(x,up);m.SetColumn(2,new Vector4(z.x,z.y,z.z,0));return m;
        }
        static Matrix4x4 RotationMatrix(Matrix4x4 m)
        {
            for(int i=0;i<3;i++){var v=((Vector3)m.GetColumn(i)).normalized;m.SetColumn(i,new Vector4(v.x,v.y,v.z,0));}
            m.SetColumn(3,new Vector4(0,0,0,1));return m;
        }
        static Matrix4x4 Matrix(float[] values) { if(values==null||values.Length!=16)throw new ArgumentException("Matrix16required");var m=Matrix4x4.zero;for(int r=0;r<4;r++)for(int c=0;c<4;c++)m[r,c]=values[r*4+c];return m; }
        static Vector3 Position(Matrix4x4 m)=>m.GetColumn(3);
        static float[] Flat(Matrix4x4 m)=>Enumerable.Range(0,16).Select(i=>m[i/4,i%4]).ToArray();
        static string Arg(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:null;}
    }
}
