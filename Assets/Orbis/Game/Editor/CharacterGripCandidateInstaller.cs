using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Orbis.Art;
using Orbis.M0.Animation;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Copies an existing complete motion candidate and changes only20 finger curves.</summary>
    public static class CharacterGripCandidateInstaller
    {
        [Serializable] public sealed class Muscle { public int index;public string name;public float value; }
        [Serializable] public sealed class Grip
        {
            public string source,sourceSha256,model;
            public float weaponWorldLength;
            public Vector3 socketLocalPosition,socketLocalEuler,socketLocalScale;
            public Muscle[] rightFingerMuscles;
        }
        [Serializable] public sealed class Report
        {
            public string sourcePrefab,sourceProfile,gripEvidence,gripEvidenceSha256,prefab,profile,controller;
            public Vector3 socketLocalPosition,socketLocalEuler,socketLocalScale;
            public float measuredHandScale,weaponWorldLength;
            public int changedChannelsPerClip;
            public string note="Candidate presentation only.20 finger channels changed; all other muscle/root/contact/action curves, Avatar, geometry and gameplay unchanged. Actual held-weapon playback still requires review.";
        }
        public static void InstallRequested()
        {
            string prefabPath=Required("-gripMotionPrefab"),evidencePath=Required("-gripEvidence"),run=Required("-gripInstallRun");
            if(!run.All(c=>char.IsLetterOrDigit(c)||c=='_'))throw new ArgumentException("Fresh simple candidate label required.");
            var grip=JsonUtility.FromJson<Grip>(File.ReadAllText(evidencePath));
            if(grip.rightFingerMuscles==null||grip.rightFingerMuscles.Length!=20)throw new InvalidOperationException("Complete measured grip required.");
            foreach(var m in grip.rightFingerMuscles)
                if(m.index<0||m.index>=HumanTrait.MuscleCount||HumanTrait.MuscleName[m.index]!=m.name||!m.name.StartsWith("Right ")||
                    !new[]{"Thumb","Index","Middle","Ring","Little"}.Any(f=>m.name.Contains(f))||float.IsNaN(m.value)||float.IsInfinity(m.value)||Mathf.Abs(m.value)>1.5f)
                    throw new InvalidOperationException("Invalid right-finger channel.");
            if(grip.rightFingerMuscles.Select(m=>m.index).Distinct().Count()!=20)throw new InvalidOperationException("Duplicate finger channel.");
            var original=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var originalDriver=original!=null?original.GetComponentInChildren<HumanAnimationDriver>(true):null;
            if(originalDriver==null)throw new InvalidOperationException("Complete runtime motion candidate required.");
            var originalAnimator=originalDriver.GetComponent<Animator>();
            var measuredPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(grip.model);
            if(measuredPrefab==null||AssetDatabase.GetAssetPath(originalAnimator.avatar)!=AssetDatabase.GetAssetPath(measuredPrefab.GetComponent<Animator>().avatar))
                throw new InvalidOperationException("Measured grip belongs to a different Avatar.");
            string label=Path.GetFileNameWithoutExtension(prefabPath);
            string folder="Assets/Orbis/Game/Characters/Candidates/"+run+"/"+label;
            string motionFolder="Assets/Orbis/Characters/Animation/"+run+"/"+label;
            if(Directory.Exists(folder)||Directory.Exists(motionFolder))throw new IOException("Preserve previous candidate.");
            Directory.CreateDirectory(folder);Directory.CreateDirectory(motionFolder+"/GripSources");AssetDatabase.Refresh();
            var profile=Object.Instantiate(originalDriver.Profile);profile.name=label+" Held Sword Motion";
            foreach(var entry in profile.Clips)
            {
                var clip=Object.Instantiate(entry.Clip);clip.name=entry.Slot+" Measured Grip";
                foreach(var muscle in grip.rightFingerMuscles)
                    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),muscle.name),AnimationCurve.Constant(0,clip.length,muscle.value));
                foreach(var binding in AnimationUtility.GetCurveBindings(entry.Clip))
                {
                    if(binding.path==""&&binding.type==typeof(Animator)&&grip.rightFingerMuscles.Any(m=>m.name==binding.propertyName))continue;
                    var before=AnimationUtility.GetEditorCurve(entry.Clip,binding);var after=AnimationUtility.GetEditorCurve(clip,binding);
                    if(after==null||before.preWrapMode!=after.preWrapMode||before.postWrapMode!=after.postWrapMode||!before.keys.SequenceEqual(after.keys))
                        throw new InvalidOperationException("Non-finger curve changed: "+entry.Slot+" / "+binding.propertyName);
                }
                AssetDatabase.CreateAsset(clip,motionFolder+"/GripSources/"+entry.Slot+".anim");entry.Clip=clip;
                entry.CalibrationEvidence+=" | right-finger-only grip "+evidencePath;
            }
            AssetDatabase.CreateAsset(profile,motionFolder+"/MotionProfile.asset");
            var controller=CharacterMotionControllerBuilder.Build(profile,motionFolder);
            var host=new GameObject("Transient held-weapon normalization");
            try
            {
                var facing=new GameObject("Visual Facing").transform;facing.SetParent(host.transform,false);
                var model=Object.Instantiate(original,facing,false);var animator=model.GetComponentInChildren<Animator>();
                var transforms=model.GetComponentsInChildren<Transform>(true);
                var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();var scales=transforms.Select(t=>t.localScale).ToArray();
                animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.Rebind();
                var driver=animator.GetComponent<HumanAnimationDriver>();driver.Configure(profile,facing);animator.Update(0);
                ArtCharacterRoster.NormalizeVisibleModelHeight(host,1.8f,Vector3.zero);
                float handScale=animator.GetBoneTransform(HumanBodyBones.RightHand).lossyScale.y;
                if(handScale<=0||float.IsNaN(handScale))throw new InvalidOperationException("Invalid measured hand scale.");
                var record=new Report {sourcePrefab=prefabPath,sourceProfile=AssetDatabase.GetAssetPath(originalDriver.Profile),
                    gripEvidence=evidencePath,gripEvidenceSha256=Hash(evidencePath),prefab=folder+"/"+label+".prefab",
                    profile=AssetDatabase.GetAssetPath(profile),controller=AssetDatabase.GetAssetPath(controller),
                    socketLocalPosition=grip.socketLocalPosition,socketLocalEuler=grip.socketLocalEuler,
                    //The existing sword prefab has1m units. Normalize its world length independently of body fit.
                    socketLocalScale=Vector3.one/handScale,measuredHandScale=handScale,weaponWorldLength=grip.weaponWorldLength,changedChannelsPerClip=20};
                var serialized=new SerializedObject(driver);serialized.FindProperty("presentationPivot").objectReferenceValue=null;serialized.ApplyModifiedPropertiesWithoutUndo();
                for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=positions[i];transforms[i].localRotation=rotations[i];transforms[i].localScale=scales[i];}
                PrefabUtility.SaveAsPrefabAsset(model,record.prefab);
                File.WriteAllText(folder+"/GripBinding.json",JsonUtility.ToJson(record,true));AssetDatabase.SaveAssets();
                Debug.Log("ORBIS_HELD_SWORD_CANDIDATE "+record.prefab);
            }
            finally{Object.DestroyImmediate(host);}
        }
        static string Required(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);if(i<0||i+1>=a.Length)throw new ArgumentException(key);return a[i+1];}
        static string Hash(string path){using(var input=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
    }
}
