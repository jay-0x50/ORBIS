#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Orbis.Art;
using Orbis.M0.Animation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    // Explicit staging installation. Creates a separate prefab/controller/profile, never edits
    // the live Resources catalog or source FBX. The runtime roster owns its Visual Facing wrapper.
    public static class CharacterMotionCandidateInstaller
    {
        [Serializable] sealed class Report
        {
            public string manifest, evidence, sourcePrefab, sourceModel, sourceSha256, prefab, profile, controller;
            public float humanScale, runtimeFitScale, authorFitScale, runtimeToAuthorRatio;
            public bool driverReady, rootMotion;
            public int clipCount;
            public string status = "Separate playback candidate; actual motor/turn/contact acceptance is still required.";
        }
        public static void InstallRequested()
        {
            string manifestPath = Required("-motionManifest");
            string evidencePath = Required("-motionEvidence");
            string version = Required("-motionInstallVersion");
            if (version.Length > 64 || !version.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                throw new InvalidOperationException("Installation version must be a simple new label.");
            var source = JsonUtility.FromJson<CharacterMotionAuthor.Manifest>(File.ReadAllText(manifestPath));
            string folder = "Assets/Orbis/Game/Characters/Candidates/" + version + "/" + source.character;
            string animationFolder = "Assets/Orbis/Characters/Animation/" + version + "/" + source.character;
            if (Directory.Exists(folder)) throw new InvalidOperationException("Preserve earlier candidate: " + folder);
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(source.candidatePrefab);
            var originalAnimator = original != null ? original.GetComponentInChildren<Animator>(true) : null;
            if (originalAnimator == null) throw new InvalidOperationException("Missing reviewed source prefab.");
            string modelPath = AssetDatabase.GetAssetPath(originalAnimator.avatar);
            if (Hash(modelPath) != source.sourceModelHash) throw new InvalidOperationException("Reviewed model bytes changed.");
            var built = CharacterMotionProfileBuilder.BuildReviewed(manifestPath,evidencePath,animationFolder);
            var host = new GameObject("Motion candidate installation / temporary validation");
            try
            {
                var facing = new GameObject("Visual Facing / validation only").transform;
                facing.SetParent(host.transform,false);
                var model = Object.Instantiate(original,facing,false);
                model.name = source.character;
                var rest = model.GetComponentsInChildren<Transform>(true);
                var positions = rest.Select(t => t.localPosition).ToArray();
                var rotations = rest.Select(t => t.localRotation).ToArray();
                var scales = rest.Select(t => t.localScale).ToArray();
                var animator = model.GetComponentInChildren<Animator>(true);
                animator.runtimeAnimatorController = built.Controller;
                animator.applyRootMotion = false;
                animator.Rebind();
                var driver = animator.gameObject.AddComponent<HumanAnimationDriver>();
                animator.gameObject.AddComponent<HumanFootIK>();
                driver.Configure(built.Profile,facing);
                animator.Update(0f);
                ArtCharacterRoster.NormalizeVisibleModelHeight(host,1.8f,Vector3.zero);
                if (!driver.IsReady) throw new InvalidOperationException(driver.ValidationError);
                float runtimeScale = animator.transform.lossyScale.y;
                // The child asset keeps original model coordinates; normalization and the facing
                // wrapper are runtime view responsibilities, so neither is baked into this prefab.
                var serialized = new SerializedObject(driver);
                serialized.FindProperty("presentationPivot").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                for (int i=0;i<rest.Length;i++)
                { rest[i].localPosition=positions[i]; rest[i].localRotation=rotations[i]; rest[i].localScale=scales[i]; }
                Directory.CreateDirectory(folder); AssetDatabase.Refresh();
                string prefabPath = folder + "/" + source.character + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(model,prefabPath);
                var report = new Report { manifest = manifestPath,evidence = evidencePath,sourcePrefab = source.candidatePrefab,
                    sourceModel = modelPath,sourceSha256 = source.sourceModelHash,prefab = prefabPath,
                    profile = AssetDatabase.GetAssetPath(built.Profile),controller = AssetDatabase.GetAssetPath(built.Controller),
                    humanScale = animator.humanScale,runtimeFitScale = runtimeScale,authorFitScale = source.finalModelScale,
                    runtimeToAuthorRatio = runtimeScale/source.finalModelScale,driverReady = true,rootMotion = animator.applyRootMotion,
                    clipCount = built.Profile.Clips.Length };
                string output = "TestResults/CharacterPipeline/MotionInstallation/" + version + "/" + source.character;
                Directory.CreateDirectory(output);
                File.WriteAllText(output + "/installation.json",JsonUtility.ToJson(report,true));
                AssetDatabase.SaveAssets();
                Debug.Log("ORBIS_MOTION_CANDIDATE_INSTALLED " + prefabPath);
            }
            finally { Object.DestroyImmediate(host); }
        }
        static string Required(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i=0;i<args.Length-1;i++) if (args[i] == name) return args[i+1];
            throw new ArgumentException("Required argument " + name);
        }
        static string Hash(string path)
        {
            using(var stream=File.OpenRead(path)) using(var sha=SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
#endif
