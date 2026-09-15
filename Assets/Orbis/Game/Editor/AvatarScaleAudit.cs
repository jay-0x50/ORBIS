#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Orbis.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>Origin/humanScale hypothesis audit. Reads actual imported avatars without altering their importers.</summary>
    public static class AvatarScaleAudit
    {
        [Serializable] public sealed class BoneData
        { public string name, path; public Vector3 world, animatorLocal, localPosition, localScale; public Quaternion localRotation; }
        [Serializable] public sealed class PoseData
        {
            public string phase;
            public float humanScale, visibleHeight, visibleFloorY, hipsHeightOverSole, leftLegLength, rightLegLength;
            public Vector3 animatorPosition, animatorLossyScale, hostLossyScale, humanPoseBodyPosition;
            public Quaternion humanPoseBodyRotation;
            public BoneData[] bones;
        }
        [Serializable] public sealed class ModelData
        {
            public string label, prefab, avatar, avatarModelSha256, controller, error;
            public bool validHuman;
            public PoseData[] poses;
        }
        [Serializable] public sealed class Report
        {
            public string utc, unityVersion;
            public string scope = "Read-only actual imported Avatar/Animator/root/baked-geometry measurements. humanScale origin cause is a hypothesis until distinct fresh-import trial is compared. No animation compensation or gameplay change.";
            public ModelData[] models;
        }
        static readonly HumanBodyBones[] Measured = { HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,HumanBodyBones.RightToes,
            HumanBodyBones.LeftShoulder,HumanBodyBones.RightShoulder,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftHand,HumanBodyBones.RightHand };
        public static void RunRequested()
        {
            string run = Argument("-avatarAuditRun") ?? "Origin01";
            if (!run.All(c => char.IsLetterOrDigit(c) || c == '_')) throw new ArgumentException("Use a fresh simple audit label.");
            string output = "TestResults/CharacterPipeline/AvatarScale/" + run + ".json";
            if (File.Exists(output)) throw new IOException("Preserve previous origin audit evidence.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var catalog = Resources.Load<ArtAssetCatalog>("Art/Catalog");
            var rows = new List<ModelData>();
            foreach (var explorer in catalog.Explorers)
                rows.Add(Audit("Legacy explorer " + explorer.SourceId,AssetDatabase.GetAssetPath(explorer.Character.Prefab),explorer.Character.Controller));
            rows.Add(Audit("Legacy companion " + catalog.Characters[0].DisplayName,AssetDatabase.GetAssetPath(catalog.Characters[0].Prefab),catalog.Characters[0].Controller));
            string roots = Argument("-avatarAuditRoots") ?? "Assets/Orbis/Game/Characters/Candidates;Assets/Orbis/Game/Characters/Candidates/OriginTrial01";
            foreach (string root in roots.Split(';')) foreach (string hero in new[] { "Polaris","Stella" })
            {
                string path = root.TrimEnd('/') + "/" + hero + "/" + hero + ".prefab";
                if (File.Exists(path)) rows.Add(Audit(root + " / " + hero,path,catalog.Explorer(hero.ToLowerInvariant()).Controller));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output,JsonUtility.ToJson(new Report { utc = DateTime.UtcNow.ToString("o"),unityVersion = Application.unityVersion,models = rows.ToArray() },true));
            if (rows.Any(row => !string.IsNullOrEmpty(row.error))) throw new InvalidOperationException("Avatar origin audit has an error; inspect " + output);
            Debug.Log("ORBIS_AVATAR_SCALE_AUDIT " + output);
        }
        static ModelData Audit(string label,string path,RuntimeAnimatorController oldController)
        {
            var row = new ModelData { label = label,prefab = path,controller = AssetDatabase.GetAssetPath(oldController) };
            var host = new GameObject("Origin audit / " + label);
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new FileNotFoundException("Missing prefab",path);
                var model = Object.Instantiate(prefab,host.transform);
                var animator = model.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null) throw new InvalidOperationException("Missing Animator Avatar");
                row.avatar = AssetDatabase.GetAssetPath(animator.avatar); row.avatarModelSha256 = Hash(row.avatar);
                row.validHuman = animator.avatar.isHuman && animator.avatar.isValid;
                if (!row.validHuman) throw new InvalidOperationException("Invalid Humanoid Avatar");
                var phases = new List<PoseData> { Measure("ImportedRest",host,animator) };
                animator.runtimeAnimatorController = oldController; animator.applyRootMotion = false;
                animator.Rebind(); animator.Play("Idle",0,0); animator.Update(0);
                phases.Add(Measure("ExistingCC0Idle",host,animator));
                ArtCharacterRoster.NormalizeVisibleModelHeight(host,1.8f,Vector3.zero);
                phases.Add(Measure("ExistingCC0Idle_NormalizedVisible1_8m",host,animator));
                // GetHumanPose documents world COM while SetHumanPose documents root-local COM.
                // Record a known parent translation instead of assuming these spaces coincide.
                host.transform.position += new Vector3(3f,2f,5f);
                phases.Add(Measure("NormalizedIdle_KnownHostTranslation_3_2_5",host,animator));
                row.poses = phases.ToArray();
            }
            catch (Exception exception) { row.error = exception.ToString(); }
            finally { Object.DestroyImmediate(host); }
            return row;
        }
        static PoseData Measure(string phase,GameObject host,Animator animator)
        {
            var data = new PoseData { phase = phase,humanScale = animator.humanScale,animatorPosition = animator.transform.position,
                animatorLossyScale = animator.transform.lossyScale,hostLossyScale = host.transform.lossyScale };
            var bones = new List<BoneData>();
            foreach (HumanBodyBones bone in Measured)
            {
                Transform t = animator.GetBoneTransform(bone); if (t == null) continue;
                bones.Add(new BoneData { name = bone.ToString(),path = AnimationUtility.CalculateTransformPath(t,animator.transform),world = t.position,
                    animatorLocal = animator.transform.InverseTransformPoint(t.position),localPosition = t.localPosition,localScale = t.localScale,localRotation = t.localRotation });
            }
            data.bones = bones.ToArray();
            var bounds = new Bounds(); bool found = false; var baked = new Mesh();
            try
            {
                foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null) continue; baked.Clear(); skin.BakeMesh(baked,true);
                    foreach (Vector3 vertex in baked.vertices)
                    { Vector3 world = skin.transform.TransformPoint(vertex); if (!found) { bounds = new Bounds(world,Vector3.zero); found = true; } else bounds.Encapsulate(world); }
                }
            }
            finally { Object.DestroyImmediate(baked); }
            if (!found) throw new InvalidOperationException("No baked geometry");
            data.visibleFloorY = bounds.min.y; data.visibleHeight = bounds.size.y;
            data.hipsHeightOverSole = animator.GetBoneTransform(HumanBodyBones.Hips).position.y - bounds.min.y;
            data.leftLegLength = Chain(animator,true); data.rightLegLength = Chain(animator,false);
            using (var handler = new HumanPoseHandler(animator.avatar,animator.transform))
            { var pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] }; handler.GetHumanPose(ref pose); data.humanPoseBodyPosition = pose.bodyPosition; data.humanPoseBodyRotation = pose.bodyRotation; }
            return data;
        }
        static float Chain(Animator a,bool left)
        {
            Vector3 upper = a.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg).position;
            Vector3 lower = a.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg).position;
            Vector3 foot = a.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot).position;
            return Vector3.Distance(upper,lower) + Vector3.Distance(lower,foot);
        }
        static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant(); }
        static string Argument(string name) { string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args,name); return index >= 0 && index+1 < args.Length ? args[index+1] : null; }
    }
}
#endif
