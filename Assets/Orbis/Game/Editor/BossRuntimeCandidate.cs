using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Orbis.Game.Animation;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Builds an isolated visual runtime candidate from reviewed original takes.</summary>
    public static class BossRuntimeCandidate
    {
        [Serializable] public sealed class Binding
        {
            public string label,sourceFolder,restPrefab,restFbxSha256,motionFbxSha256,profile,prefab,reviewController;
            public string note="Candidate only; actual M4 playback must pass before saving references in Field.";
        }
        public static void BuildRequested()
        {
            string source=Arg("-bossMotionFolder")?.Replace('\\','/').TrimEnd('/');
            string run=Arg("-bossRuntimeRun");
            Build(source,run);
        }
        public static void Build(string source,string run)
        {
            if(source==null || !source.StartsWith("Assets/Orbis/Game/Characters/BossMotionCandidates/",StringComparison.Ordinal) ||
                source.Split('/').Any(p=>p=="."||p=="..") || string.IsNullOrEmpty(run) || !run.All(c=>char.IsLetterOrDigit(c)||c=='_'))
                throw new ArgumentException("Existing motion candidate and fresh runtime label required.");
            var manifest=JsonUtility.FromJson<BossMotionImport.Manifest>(File.ReadAllText(source+"/SourceManifest.json"));
            var rest=JsonUtility.FromJson<BossMotionImport.RestBinding>(File.ReadAllText(source+"/RestBinding.json"));
            if(Hash(rest.fbx)!=rest.sha256 || rest.sha256!=manifest.restFbxSha256 || Hash(source+"/OriginalMotion.fbx")!=manifest.motionFbxSha256)
                throw new InvalidOperationException("Source/rest hashes changed after motion import.");
            string folder="Assets/Orbis/Game/Characters/Bosses/"+run+"/"+manifest.label;
            var profile=ScriptableObject.CreateInstance<BossMotionProfile>();profile.name=manifest.label+" Original Motions";
            AnimationClip Clip(string slot)=>AssetDatabase.LoadAssetAtPath<AnimationClip>(source+"/"+slot+".anim");
            profile.Idle=Clip("Idle");profile.Move=Clip("Move");profile.Attack=Clip("Attack");
            profile.Hurt=Clip("Hurt");profile.Exposed=Clip("Exposed");profile.Dead=Clip("Dead");
            profile.AttackContact=manifest.clips.Single(c=>c.slot=="Attack").contactNormalized;
            profile.SourceRigSha256=rest.sha256;
            profile.Provenance="Original anatomy-specific authored animation; "+source+"/SourceManifest.json; rest SHA256="+rest.sha256+"; motion SHA256="+manifest.motionFbxSha256+
                ". Move remains a review-library clip because the existing M4 encounter has a fixed gameplay anchor.";
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(rest.prefab);
            BossMotionControllerBuilder.Build(profile,prefab,manifest.hurtBoneNames,folder);
            AssetDatabase.CreateAsset(profile,folder+"/MotionProfile.asset");
            var instance=Object.Instantiate(prefab);
            try
            {
                instance.name=manifest.label+" Animated Visual";
                var animator=instance.GetComponent<Animator>();animator.runtimeAnimatorController=profile.Controller;
                animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                // Rest transforms are saved. The existing Field binder supplies the encounter reference later.
                PrefabUtility.SaveAsPrefabAsset(instance,folder+"/"+manifest.label+".prefab");
            }
            finally { Object.DestroyImmediate(instance); }
            File.WriteAllText(folder+"/RuntimeBinding.json",JsonUtility.ToJson(new Binding {
                label=manifest.label,sourceFolder=source,restPrefab=rest.prefab,restFbxSha256=rest.sha256,motionFbxSha256=manifest.motionFbxSha256,
                profile=folder+"/MotionProfile.asset",prefab=folder+"/"+manifest.label+".prefab",reviewController=folder+"/BossReview.controller"
            },true));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);AssetDatabase.SaveAssets();
            Debug.Log("ORBIS_BOSS_RUNTIME_CANDIDATE "+folder);
        }
        static string Arg(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        static string Hash(string file){using(var input=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
    }
}
