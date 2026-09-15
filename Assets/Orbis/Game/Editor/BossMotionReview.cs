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
    /// <summary>Actual Animator/URP review, on transient instances only. Never rewrites Field.</summary>
    public static class BossMotionReview
    {
        [Serializable] sealed class Pose
        {
            public string slot;
            public float normalizedTime,maxSkinDisplacementFromRest,animatorTranslation,rootBoneTranslation;
            public Vector3 boundsMin,boundsMax;
            public Vector3[] bonePositions;
        }
        [Serializable] sealed class Evidence
        {
            public string manifest,candidateFolder,graphics,unityVersion;
            public string[] boneNames;
            public Vector3 camera,target;
            public float worldScale;
            public Pose[] poses;
            public string status="Actual imported Generic pose samples, matching skinned mesh measurements and same-camera PNGs. Manual review, continuous playback and Field encounter integration remain required.";
        }
        public static void CaptureRequested()
        {
            string folder=Argument("-bossMotionFolder")?.Replace('\\','/').TrimEnd('/');
            string run=Argument("-bossReviewRun");
            Capture(folder,run);
        }
        public static void Capture(string folder,string run)
        {
            if(folder==null || !folder.StartsWith("Assets/Orbis/Game/Characters/BossMotionCandidates/",StringComparison.Ordinal) ||
                folder.Split('/').Any(p=>p==".."||p==".") || string.IsNullOrEmpty(run) || !run.All(c=>char.IsLetterOrDigit(c)||c=='_'))
                throw new ArgumentException("Supply imported candidate folder and fresh review run.");
            string manifestPath=folder+"/SourceManifest.json";
            var manifest=JsonUtility.FromJson<BossMotionImport.Manifest>(File.ReadAllText(manifestPath));
            string name=manifest.label;
            string output="TestResults/CharacterPipeline/UnityBossMotionReview/"+run+"/"+name;
            if(Directory.Exists(output)) throw new IOException("Preserve prior pose review.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var host=new GameObject("Transient original Generic motion");
            GameObject model=null,lightObject=null,cameraObject=null;
            var scratch=new Mesh();
            try
            {
                var binding=JsonUtility.FromJson<BossMotionImport.RestBinding>(File.ReadAllText(folder+"/RestBinding.json"));
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(binding.prefab);
                model=Object.Instantiate(prefab,host.transform);
                var animator=model.GetComponent<Animator>();
                if(animator==null || !animator.avatar.isValid || animator.avatar.isHuman)
                    throw new InvalidOperationException("Reviewed Generic Avatar required.");
                var skin=model.GetComponentsInChildren<SkinnedMeshRenderer>().Single();
                skin.updateWhenOffscreen=true;
                Vector3[] rest=WorldVertices(skin,scratch);
                Bounds restBounds=BoundsOf(rest);
                // Scale around the recorded foot/swim anchor. Never lift a low decorative tail
                // by treating the bounds minimum as the supporting foot plane.
                host.transform.localScale=Vector3.one*(1.8f/restBounds.size.y);
                rest=WorldVertices(skin,scratch);
                animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(folder+"/Review.controller");
                animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
                var bones=skin.bones;
                var root=bones.Single(b=>b.name==manifest.rootBone);
                Vector3 animatorPosition=model.transform.position,rootLocalPosition=root.localPosition;
                ArtStyle.ApplyToHierarchy(host,Resources.Load<ArtAssetCatalog>("Art/Catalog"),true);
                foreach(var item in host.GetComponentsInChildren<SkinnedMeshRenderer>()) item.forceMatrixRecalculationPerRender=true;
                var poses=new List<Pose>();
                Bounds allBounds=BoundsOf(rest);
                foreach(var clip in manifest.clips)
                {
                    // Include the documented attack contact exactly, plus both sides of the
                    // windup and a half-key sample to expose between-key reconstruction errors.
                    var times=new[]{0f,.125f,.25f,.5f,.75f,.999f,clip.slot=="Attack"?clip.contactNormalized:.375f};
                    foreach(float time in times.Distinct().OrderBy(t=>t))
                    {
                        animator.Play(clip.slot,0,time); animator.Update(0f);
                        var points=WorldVertices(skin,scratch); Bounds bounds=BoundsOf(points);
                        allBounds.Encapsulate(bounds);
                        float displacement=0f;
                        for(int i=0;i<points.Length;i++) displacement=Mathf.Max(displacement,Vector3.Distance(rest[i],points[i]));
                        var pose=new Pose { slot=clip.slot,normalizedTime=time,maxSkinDisplacementFromRest=displacement,
                            animatorTranslation=Vector3.Distance(animatorPosition,model.transform.position),
                            rootBoneTranslation=Vector3.Distance(rootLocalPosition,root.localPosition),
                            boundsMin=bounds.min,boundsMax=bounds.max,bonePositions=bones.Select(b=>b.position).ToArray() };
                        if(pose.animatorTranslation>.0001f || pose.rootBoneTranslation>.0001f)
                            throw new InvalidOperationException("Unexpected root motion in "+clip.slot);
                        poses.Add(pose);
                    }
                }
                RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.28f,.30f,.34f); RenderSettings.fog=false;
                lightObject=new GameObject("Motion review key"); var key=lightObject.AddComponent<Light>();
                key.type=LightType.Directional; key.intensity=1.1f; key.color=Color.white; key.shadows=LightShadows.Soft;
                key.transform.rotation=Quaternion.Euler(42,-35,0); RenderSettings.sun=key;
                cameraObject=new GameObject("Motion review camera"); var camera=cameraObject.AddComponent<Camera>();
                camera.enabled=false; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.14f,.16f,.20f);
                camera.nearClipPlane=.03f; camera.farClipPlane=100f; camera.fieldOfView=30f;
                camera.allowHDR=true; camera.allowMSAA=true; camera.useOcclusionCulling=false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                Vector3 direction=new Vector3(.65f,.27f,1f).normalized;
                float distance=allBounds.extents.magnitude/Mathf.Sin(camera.fieldOfView*Mathf.Deg2Rad*.5f)*1.08f;
                camera.transform.SetPositionAndRotation(allBounds.center+direction*distance,Quaternion.LookRotation(-direction));
                if(distance+allBounds.extents.magnitude>=camera.farClipPlane) throw new InvalidOperationException("Invalid capture scale.");
                Directory.CreateDirectory(output);
                for(int i=0;i<poses.Count;i++)
                {
                    Pose pose=poses[i]; animator.Play(pose.slot,0,pose.normalizedTime); animator.Update(0f);
                    // ForceMatrixRecalculationPerRender prevents repeated Editor renders from
                    // displaying the previous pose while CPU measurements have already moved.
                    CharacterPipelinePreview.Render(camera,output+"/"+i.ToString("D3")+"_"+pose.slot+".png");
                }
                File.WriteAllText(output+"/ActualPoseEvidence.json",JsonUtility.ToJson(new Evidence {
                    manifest=manifestPath,candidateFolder=folder,graphics=SystemInfo.graphicsDeviceName,unityVersion=Application.unityVersion,
                    boneNames=bones.Select(b=>b.name).ToArray(),camera=camera.transform.position,target=allBounds.center,
                    worldScale=host.transform.localScale.x,poses=poses.ToArray()
                },true));
                Debug.Log("ORBIS_GENERIC_MOTION_POSE_CAPTURE "+output);
            }
            finally
            {
                Object.DestroyImmediate(scratch); Object.DestroyImmediate(host);
                if(lightObject!=null) Object.DestroyImmediate(lightObject);
                if(cameraObject!=null) Object.DestroyImmediate(cameraObject);
            }
        }
        static Vector3[] WorldVertices(SkinnedMeshRenderer skin,Mesh scratch)
        {
            scratch.Clear(); skin.BakeMesh(scratch,true);
            return scratch.vertices.Select(v=>skin.transform.TransformPoint(v)).ToArray();
        }
        static Bounds BoundsOf(Vector3[] points)
        {
            if(points.Length==0) throw new InvalidOperationException("No actual skinned geometry.");
            var bounds=new Bounds(points[0],Vector3.zero);
            foreach(var point in points) bounds.Encapsulate(point);
            return bounds;
        }
        static string Argument(string key)
        { var args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,key); return i>=0&&i+1<args.Length ? args[i+1] : null; }
    }
}
