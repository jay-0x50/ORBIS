using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Imports original anatomy-specific takes for review. Does not edit Field/catalog.</summary>
    public static class BossMotionImport
    {
        [Serializable] public sealed class ClipRecord
        {
            public string slot,action,fbxTake;
            public float startFrame,endFrame,fps,duration,contactFrame,contactNormalized;
            public bool loop,rootLocalMotion;
        }
        [Serializable] public sealed class Manifest
        {
            public string label,restRigSha256,restFbxSha256,rigObjectName,rootBone;
            public string motionFbxPath,motionFbxSha256;
            public bool motionFbxRoundtripPass;
            public float fps;
            public string[] hurtBoneNames;
            public ClipRecord[] clips;
        }
        [Serializable] sealed class ClipEvidence
        {
            public string slot,take,path;
            public float duration,frameRate;
            public int bindingCount;
        }
        [Serializable] sealed class ImportEvidence
        {
            public string manifest,manifestSha256,sourceFbxSha256,restFbxSha256,avatar,reviewController;
            public ClipEvidence[] clips;
            public string status="Imported for actual playback review. No Field/controller/profile adoption or performance acceptance.";
        }
        [Serializable] public sealed class RestBinding
        {
            public string prefab,fbx,sha256;
        }
        static readonly string[] Slots={"Idle","Move","Attack","Hurt","Exposed","Dead"};

        public static void ImportRequested()
        {
            Import(Argument("-bossMotionManifest"),Argument("-bossMotionRun"),Argument("-bossRestFolder"));
        }
        public static void ImportAndCaptureRequested()
        {
            ImportRequested(); BossMotionReview.CaptureRequested();
        }
        public static void Import(string manifestPath,string run,string restFolder=null)
        {
            if(string.IsNullOrEmpty(run) || !run.All(c=>char.IsLetterOrDigit(c)||c=='_'))
                throw new ArgumentException("A fresh alphanumeric review run is required.");
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if(manifest==null || !new[]{"FireBoss","WaterBoss","RockBoss","WindBoss","LightningBoss"}.Contains(manifest.label) ||
                manifest.clips==null || manifest.clips.Length!=6 ||
                !manifest.clips.Select(c=>c.slot).OrderBy(s=>s).SequenceEqual(Slots.OrderBy(s=>s)) ||
                manifest.hurtBoneNames==null || manifest.hurtBoneNames.Length==0)
                throw new InvalidOperationException("A complete six-slot anatomy manifest is required.");
            string name=manifest.label;
            string rigFolder=(restFolder??"Assets/Orbis/Game/Characters/BossCandidates/Import01/"+name).Replace('\\','/').TrimEnd('/');
            if(!rigFolder.StartsWith("Assets/Orbis/Game/Characters/BossCandidates/",StringComparison.Ordinal) ||
                rigFolder.Split('/').Any(p=>p==".."||p==".")) throw new ArgumentException("Reviewed candidate rig folder required.");
            string rigPath=rigFolder+"/"+name+".fbx";
            var rig=AssetDatabase.LoadAssetAtPath<GameObject>(rigFolder+"/"+name+".prefab");
            var animator=rig!=null ? rig.GetComponent<Animator>() : null;
            if(animator==null || animator.avatar==null || !animator.avatar.isValid || animator.avatar.isHuman ||
                Hash(rigPath)!=manifest.restFbxSha256)
                throw new InvalidOperationException("The reviewed rest rig/Generic Avatar does not match the motion manifest.");
            string input=Path.GetFullPath(manifest.motionFbxPath);
            string reviewRoot=Path.GetFullPath("TestResults/CharacterPipeline")+Path.DirectorySeparatorChar;
            if(!input.StartsWith(reviewRoot,StringComparison.OrdinalIgnoreCase) ||
                !manifest.motionFbxRoundtripPass || Hash(input)!=manifest.motionFbxSha256)
                throw new InvalidOperationException("An immutable, verified motion FBX in CharacterPipeline evidence is required.");
            foreach(var clip in manifest.clips)
                if(clip.rootLocalMotion || string.IsNullOrEmpty(clip.fbxTake) || clip.fps<=0f ||
                    clip.endFrame<=clip.startFrame || Mathf.Abs((clip.endFrame-clip.startFrame)/clip.fps-clip.duration)>.001f)
                    throw new InvalidOperationException("Invalid actual take/frame/root-motion contract: "+clip.slot);

            string folder="Assets/Orbis/Game/Characters/BossMotionCandidates/"+run+"/"+name;
            if(Directory.Exists(folder)) throw new IOException("Keep prior motion evidence; use a fresh run.");
            Directory.CreateDirectory(folder);
            string fbx=folder+"/OriginalMotion.fbx";
            File.Copy(input,fbx); File.Copy(manifestPath,folder+"/SourceManifest.json");
            File.WriteAllText(folder+"/RestBinding.json",JsonUtility.ToJson(new RestBinding {
                prefab=rigFolder+"/"+name+".prefab",fbx=rigPath,sha256=manifest.restFbxSha256
            },true));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer=(ModelImporter)AssetImporter.GetAtPath(fbx);
            importer.animationType=ModelImporterAnimationType.Generic;
            importer.avatarSetup=ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar=animator.avatar;
            importer.motionNodeName=manifest.rigObjectName+"/"+manifest.rootBone;
            importer.importAnimation=true; importer.optimizeGameObjects=false; importer.optimizeBones=false;
            importer.animationCompression=ModelImporterAnimationCompression.Off;
            importer.resampleCurves=true; importer.importCameras=false; importer.importLights=false;
            importer.SaveAndReimport();
            // Resolve exact exported takes, rather than relying on the FBX's arbitrary default clip.
            // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-defaultClipAnimations.html
            var available=importer.defaultClipAnimations;
            var clips=manifest.clips.Select(record=>
            {
                var matches=available.Where(c=>c.takeName==record.fbxTake).ToArray();
                if(matches.Length!=1) throw new InvalidOperationException("Missing/ambiguous FBX take: "+record.fbxTake);
                var clip=matches[0];
                if(record.startFrame<clip.firstFrame-.01f || record.endFrame>clip.lastFrame+.01f)
                    throw new InvalidOperationException("Manifest frames exceed actual take: "+record.slot);
                clip.name=record.slot; clip.firstFrame=record.startFrame; clip.lastFrame=record.endFrame;
                clip.loopTime=record.loop; clip.loopPose=false; clip.cycleOffset=0f;
                clip.keepOriginalOrientation=true; clip.keepOriginalPositionY=true; clip.keepOriginalPositionXZ=true;
                clip.lockRootRotation=true; clip.lockRootHeightY=true; clip.lockRootPositionXZ=true;
                clip.events=Array.Empty<AnimationEvent>();
                return clip;
            }).ToArray();
            importer.clipAnimations=clips; importer.SaveAndReimport();
            var imported=AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            var paths=rig.GetComponentsInChildren<Transform>(true).Select(t=>AnimationUtility.CalculateTransformPath(t,rig.transform)).ToHashSet();
            var controller=AnimatorController.CreateAnimatorControllerAtPath(folder+"/Review.controller");
            var evidence=new System.Collections.Generic.List<ClipEvidence>();
            foreach(var record in manifest.clips)
            {
                var source=imported.Single(c=>c.name==record.slot);
                if(source.humanMotion || source.legacy || source.events.Length!=0 || Mathf.Abs(source.length-record.duration)>.002f)
                    throw new InvalidOperationException("Imported duration/type mismatch: "+record.slot);
                var bindings=AnimationUtility.GetCurveBindings(source);
                foreach(var binding in bindings.Where(b=>b.type==typeof(Transform)))
                    if(!paths.Contains(binding.path)) throw new InvalidOperationException("Animation bone absent from reviewed rest rig: "+binding.path);
                var copy=Object.Instantiate(source); copy.name=record.slot;
                string clipPath=folder+"/"+record.slot+".anim"; AssetDatabase.CreateAsset(copy,clipPath);
                var state=controller.layers[0].stateMachine.AddState(record.slot);
                state.motion=copy; state.writeDefaultValues=false;
                if(record.slot=="Idle") controller.layers[0].stateMachine.defaultState=state;
                evidence.Add(new ClipEvidence { slot=record.slot,take=record.fbxTake,path=clipPath,
                    duration=copy.length,frameRate=copy.frameRate,bindingCount=bindings.Length });
            }
            AssetDatabase.SaveAssets();
            string output="TestResults/CharacterPipeline/UnityBossMotionImport/"+run;
            Directory.CreateDirectory(output);
            File.WriteAllText(output+"/"+name+".json",JsonUtility.ToJson(new ImportEvidence {
                manifest=manifestPath,manifestSha256=Hash(manifestPath),sourceFbxSha256=Hash(fbx),restFbxSha256=Hash(rigPath),
                avatar=AssetDatabase.GetAssetPath(animator.avatar),reviewController=AssetDatabase.GetAssetPath(controller),clips=evidence.ToArray()
            },true));
            Debug.Log("ORBIS_GENERIC_MOTION_REVIEW_IMPORTED "+name);
        }
        static string Argument(string key)
        { var args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,key); return i>=0&&i+1<args.Length ? args[i+1] : null; }
        static string Hash(string path)
        { using(var input=File.OpenRead(path)) using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant(); }
    }
}
