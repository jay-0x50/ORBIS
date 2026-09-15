#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Orbis.Art;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    // Explicit candidate-only authoring. No initialization hook, live catalog edits or Field build.
    public static class CharacterMotionAuthor
    {
        const string SourceRoot = "Assets/ImportedAssets/KayKit/CharacterAnimations/Rig_Medium/";
        const int SamplesPerSecond = 60;
        [Serializable] public sealed class CurvePoint { public float time, value; }
        [Serializable] public sealed class Slot
        {
            public string slot, clip, source, sourceLicense, calibrationEvidence;
            public float duration, cycleDistanceMetres;
            public float requestedCycleDistanceMetres, maximumAuthoredAnkleTargetError;
            public float maximumAuthoredHipReachDrop;
            public string curveTangents;
            public int samplesPerSecond;
            public int authoredClampedLegSamples;
            public CurvePoint[] leftPlant, rightPlant;
        }
        [Serializable] public sealed class Sole
        {
            public bool calibrated;
            public string side, sourceModelHash, evidence;
            public Vector3 solePointInFootLocal, soleNormalInFootLocal, forwardInFootLocal;
            public Vector3 measuredWorldPoint, measuredHeel, measuredToe, footRestWorld, toesRestWorld;
            public int shoeVertices, soleVertices;
            public float solePlaneY;
        }
        [Serializable] public sealed class Manifest
        {
            public int version = 1;
            public string character, candidatePrefab, output, utc, sourceModelHash;
            public float humanScale, finalModelScale, sourceLegReach;
            public Sole leftSole, rightSole;
            public Slot[] clips;
            public string status = "Authored candidate only. Humanoid playback, actual sole/contact trajectory and visual review must pass before driver installation.";
        }
        sealed class Snapshot
        {
            readonly Transform[] transforms;
            readonly Vector3[] positions, scales;
            readonly Quaternion[] rotations;
            public Snapshot(Transform root)
            {
                transforms = root.GetComponentsInChildren<Transform>(true);
                positions = transforms.Select(t => t.localPosition).ToArray();
                scales = transforms.Select(t => t.localScale).ToArray();
                rotations = transforms.Select(t => t.localRotation).ToArray();
            }
            public void Restore()
            {
                for (int i = 0; i < transforms.Length; ++i)
                { transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i]; }
            }
        }
        sealed class Body : IDisposable
        {
            public readonly GameObject Host, Model;
            public readonly Animator Animator;
            public readonly HumanPoseHandler Handler;
            public readonly Snapshot Rest;
            public readonly Transform Hips, Spine, Chest, Head;
            public readonly Sole Left, Right;
            public readonly Vector3 HipRest;
            public readonly float LegReach, Floor;
            public readonly Quaternion HipRotation, SpineRotation, ChestRotation;
            public readonly Quaternion LeftFootRotation, RightFootRotation;
            public float MaximumLegError;
            public float MaximumHipReachDrop;
            public int ClampedLegSamples;
            public Body(string prefabPath, string modelHash)
            {
                Host = new GameObject("Motion authoring / isolated candidate");
                Model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), Host.transform);
                Animator = Model.GetComponentInChildren<Animator>(true);
                if (Animator == null || !Animator.isHuman || Animator.avatar == null || !Animator.avatar.isValid)
                    throw new InvalidOperationException("A valid imported candidate Humanoid is required.");
                // Record FBX rest transforms before the old CC0 controller evaluates. Fit the final
                // visual scale using the same existing idle/BakeMesh normalization as ArtCharacterRoster.
                var originalRest = new Snapshot(Model.transform);
                Animator.applyRootMotion = false; Animator.Rebind(); Animator.Play("Idle", 0, 0); Animator.Update(0);
                ArtCharacterRoster.NormalizeVisibleModelHeight(Host, 1.8f, Vector3.zero);
                Animator.enabled = false; originalRest.Restore();
                Rest = new Snapshot(Model.transform);
                Handler = new HumanPoseHandler(Animator.avatar, Animator.transform);
                Hips = Bone(HumanBodyBones.Hips); Spine = Bone(HumanBodyBones.Spine);
                Chest = Bone(HumanBodyBones.Chest); Head = Bone(HumanBodyBones.Head);
                HipRest = Hips.position; HipRotation = Hips.rotation; SpineRotation = Spine.rotation; ChestRotation = Chest.rotation;
                LeftFootRotation = Bone(HumanBodyBones.LeftFoot).rotation; RightFootRotation = Bone(HumanBodyBones.RightFoot).rotation;
                Left = MeasureSole(this, true, modelHash); Right = MeasureSole(this, false, modelHash);
                Floor = Mathf.Min(Left.solePlaneY, Right.solePlaneY);
                LegReach = Mathf.Min(ChainLength(true), ChainLength(false));
            }
            public Transform Bone(HumanBodyBones bone) => Animator.GetBoneTransform(bone) ?? throw new InvalidOperationException("Missing bone " + bone);
            public float ChainLength(bool left)
            {
                Transform a = Bone(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                Transform b = Bone(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                Transform c = Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                return Vector3.Distance(a.position, b.position) + Vector3.Distance(b.position, c.position);
            }
            public HumanPose ReadPose()
            {
                HumanPose pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
                Handler.GetHumanPose(ref pose);
                for (int i = 0; i < pose.muscles.Length; i++)
                {
                    string muscle = HumanTrait.MuscleName[i];
                    // Original grip default: a relaxed left hand and closed right sword fingers.
                    // Values are presentation only and must be checked in the actual hand close-up.
                    if (muscle.Contains(" Stretched") && (muscle.StartsWith("Left ") || muscle.StartsWith("Right ")))
                        pose.muscles[i] = muscle.StartsWith("Right ") ? -.45f : -.12f;
                    if (!Finite(pose.muscles[i])) throw new InvalidOperationException("Non-finite muscle " + muscle);
                }
                return pose;
            }
            public void Dispose() { Handler?.Dispose(); if (Host != null) Object.DestroyImmediate(Host); }
        }

        public static void AuthorRequested()
        {
            string name = Argument("-motionAuthorCharacter") ?? "Polaris";
            string version = Argument("-motionAuthorVersion") ?? "Motion01";
            if ((name != "Polaris" && name != "Stella") || !version.All(c => char.IsLetterOrDigit(c) || c == '_'))
                throw new ArgumentException("Select an imported hero and a simple fresh version label.");
            string candidateRoot = (Argument("-motionAuthorRoot") ?? "Assets/Orbis/Game/Characters/Candidates").Replace('\\','/').TrimEnd('/');
            if (!candidateRoot.StartsWith("Assets/Orbis/Game/Characters/Candidates",StringComparison.Ordinal) || candidateRoot.Split('/').Any(p => p == ".." || p == "."))
                throw new ArgumentException("Use an existing reviewed candidate subfolder.");
            string prefab = candidateRoot + "/" + name + "/" + name + ".prefab";
            string output = "Assets/Orbis/Game/Characters/MotionCandidates/" + version + "/" + name;
            if (Directory.Exists(output)) throw new IOException("Preserve previous candidate motion output; choose a new version.");
            string model = candidateRoot + "/" + name + "/" + name + ".fbx";
            string modelHash = Hash(model);
            Directory.CreateDirectory(output); AssetDatabase.Refresh();
            var slots = new List<Slot>();
            using (var body = new Body(prefab, modelHash))
            {
                // Distances are derived from measured leg reach. These define authored native
                // travel per cycle; the animation driver later matches actual gameplay speed.
                float walkDistance = body.LegReach * 1.50f;
                float runDistance = body.LegReach * 3.25f;
                foreach (string slot in new[] { "Idle", "Walk", "Run", "WalkLeft", "WalkRight", "WalkBack", "RunLeft", "RunRight", "RunBack", "Air",
                    "SteeringNeutral", "SteeringLeft", "SteeringRight", "TurnLeft90", "TurnRight90", "TurnLeft180", "TurnRight180" })
                {
                    bool run = slot.StartsWith("Run"), moving = run || slot.StartsWith("Walk"), turn = slot.StartsWith("Turn"), steering = slot.StartsWith("Steering");
                    float duration = slot == "Idle" ? 2.4f : moving ? (run ? .66f : .82f) : turn ? (slot.EndsWith("180") ? .48f : .36f) : .5f;
                    float requestedDistance = moving ? (run ? runDistance : walkDistance) : 0f;
                    float distance = moving ? CalibrateReach(body,slot,requestedDistance) : 0f;
                    var result = Bake(body, slot, duration, distance, output,
                        t => Pose(body, slot, t, distance), moving || slot == "Idle", steering);
                    result.requestedCycleDistanceMetres = requestedDistance;
                    slots.Add(result);
                }
                foreach (var request in new[]
                {
                    new[] {"Attack1", "CombatMelee", "Melee_1H_Attack_Slice_Horizontal"},
                    new[] {"Attack2", "CombatMelee", "Melee_1H_Attack_Slice_Diagonal"},
                    new[] {"Attack3", "CombatMelee", "Melee_1H_Attack_Chop"},
                    new[] {"Skill", "CombatRanged", "Ranged_Magic_Spellcasting"},
                    new[] {"Burst", "CombatRanged", "Ranged_Magic_Summon"},
                    new[] {"Hurt", "General", "Hit_A"},
                    new[] {"Dead", "General", "Death_A"}
                }) slots.Add(CopySource(request[0], request[1], request[2], output));

                var manifest = new Manifest { character = name, candidatePrefab = prefab, output = output,
                    utc = DateTime.UtcNow.ToString("o"), sourceModelHash = modelHash, humanScale = body.Animator.humanScale,
                    finalModelScale = body.Host.transform.lossyScale.y, sourceLegReach = body.LegReach,
                    leftSole = body.Left, rightSole = body.Right, clips = slots.ToArray() };
                File.WriteAllText(output + "/MotionManifest.json", JsonUtility.ToJson(manifest, true));
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("ORBIS_AUTHORED_MOTION_CANDIDATE " + output + " / " + slots.Count + " clips; playback/contact review pending");
        }

        public static void AppendStopsRequested()
        {
            string sourcePath=Argument("-motionAppendStopsFrom") ?? throw new ArgumentException("Existing reviewed24 manifest required.");
            var prior=JsonUtility.FromJson<Manifest>(File.ReadAllText(sourcePath));
            string version=Argument("-motionAuthorVersion") ?? throw new ArgumentException("Fresh output version required.");
            if(!version.All(c=>char.IsLetterOrDigit(c)||c=='_') || prior.clips.Length!=24) throw new ArgumentException("Append only to a reviewed24-slot manifest.");
            string output="Assets/Orbis/Game/Characters/MotionCandidates/"+version+"/"+prior.character;
            if(Directory.Exists(output)) throw new IOException("Preserve earlier candidate: "+output);
            var avatar=AssetDatabase.LoadAssetAtPath<GameObject>(prior.candidatePrefab).GetComponentInChildren<Animator>().avatar;
            if(Hash(AssetDatabase.GetAssetPath(avatar))!=prior.sourceModelHash) throw new InvalidOperationException("Model hash changed.");
            Directory.CreateDirectory(output); AssetDatabase.Refresh();
            using(var body=new Body(prior.candidatePrefab,prior.sourceModelHash))
            {
                var clips=prior.clips.ToList();
                foreach(string slot in new[]{"StopLeft","StopRight"})
                    clips.Add(Bake(body,slot,.64f,0,output,t=>Pose(body,slot,t,0),false,false));
                prior.output=output; prior.clips=clips.ToArray(); prior.utc=DateTime.UtcNow.ToString("O");
                File.WriteAllText(output+"/MotionManifest.json",JsonUtility.ToJson(prior,true));
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("ORBIS_STOP_MOTIONS_AUTHORED "+output+"; original24 references unchanged.");
        }

        static Slot Bake(Body body, string name, float duration, float distance, string output, Action<float> pose, bool loop, bool steering)
        {
            body.MaximumLegError = 0f; body.ClampedLegSamples = 0; body.MaximumHipReachDrop = 0f;
            string tangentChoice = Argument("-motionAuthorTangents") ?? "ClampedAuto";
            if (tangentChoice != "ClampedAuto" && tangentChoice != "Linear") throw new ArgumentException("Supported diagnostic tangents: ClampedAuto / Linear.");
            var tangentMode = tangentChoice == "Linear" ? AnimationUtility.TangentMode.Linear : AnimationUtility.TangentMode.ClampedAuto;
            int sampleRate = int.TryParse(Argument("-motionAuthorRate"),out int requestedRate) ? requestedRate : SamplesPerSecond;
            if (sampleRate < 30 || sampleRate > 240) throw new ArgumentException("Diagnostic author rate must be30..240.");
            AnimationClip template = Source("General", "Idle_A");
            var clip = Object.Instantiate(template); clip.name = name; clip.ClearCurves(); clip.frameRate = sampleRate;
            int count = Mathf.CeilToInt(duration * sampleRate);
            var values = new Dictionary<string, List<Keyframe>>();
            string[] muscleNames = HumanTrait.MuscleName;
            foreach (string property in muscleNames.Concat(new[] { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" }))
                values[property] = new List<Keyframe>(count + 1);
            Quaternion previousRotation = Quaternion.identity;
            var leftContacts = new List<CurvePoint>(); var rightContacts = new List<CurvePoint>();
            for (int i = 0; i <= count; i++)
            {
                float normalized = (float)i / count, time = duration * normalized;
                body.Rest.Restore(); pose(normalized); HumanPose sample = body.ReadPose();
                if (i > 0 && Quaternion.Dot(previousRotation, sample.bodyRotation) < 0f)
                    sample.bodyRotation = new Quaternion(-sample.bodyRotation.x, -sample.bodyRotation.y, -sample.bodyRotation.z, -sample.bodyRotation.w);
                previousRotation = sample.bodyRotation;
                for (int m = 0; m < muscleNames.Length; m++) Add(values, muscleNames[m], time, sample.muscles[m]);
                Add(values, "RootT.x", time, sample.bodyPosition.x); Add(values, "RootT.y", time, sample.bodyPosition.y); Add(values, "RootT.z", time, sample.bodyPosition.z);
                Add(values, "RootQ.x", time, sample.bodyRotation.x); Add(values, "RootQ.y", time, sample.bodyRotation.y);
                Add(values, "RootQ.z", time, sample.bodyRotation.z); Add(values, "RootQ.w", time, sample.bodyRotation.w);
                leftContacts.Add(new CurvePoint { time = normalized, value = Contact(name, normalized, true) });
                rightContacts.Add(new CurvePoint { time = normalized, value = Contact(name, normalized, false) });
            }
            foreach (var pair in values)
            {
                var curve = new AnimationCurve(pair.Value.ToArray());
                for (int i = 0; i < curve.length; i++)
                { AnimationUtility.SetKeyLeftTangentMode(curve,i,tangentMode); AnimationUtility.SetKeyRightTangentMode(curve,i,tangentMode); }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), pair.Key), curve);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop; settings.loopBlend = false; settings.startTime = 0; settings.stopTime = duration;
            settings.hasAdditiveReferencePose = steering; settings.additiveReferencePoseTime = 0f;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            clip.EnsureQuaternionContinuity();
            string path = output + "/" + name + ".anim"; AssetDatabase.CreateAsset(clip, path);
            if (!clip.isHumanMotion) throw new InvalidOperationException("Authored clip did not retain Humanoid muscle binding: " + name);
            return new Slot { slot = name, clip = path, duration = duration, cycleDistanceMetres = distance,
                maximumAuthoredAnkleTargetError = body.MaximumLegError, authoredClampedLegSamples = body.ClampedLegSamples,
                maximumAuthoredHipReachDrop = body.MaximumHipReachDrop,
                curveTangents = tangentChoice, samplesPerSecond = sampleRate,
                source = "Original Orbis measured-rig two-bone trajectory authoring", sourceLicense = "Original project-authored motion; no third-party motion curves copied.",
                calibrationEvidence = "MotionManifest.json: measured Unity BakeMesh soles and limb reach; playback QA pending",
                leftPlant = leftContacts.ToArray(), rightPlant = rightContacts.ToArray() };
        }
        static void Add(Dictionary<string, List<Keyframe>> curves, string property, float time, float value)
        { if (!Finite(value)) throw new InvalidOperationException("Nonfinite " + property); curves[property].Add(new Keyframe(time, value)); }

        static float CalibrateReach(Body body,string slot,float requested)
        {
            // Preserve the authored hip/stance style and measured limb lengths. Shorten the
            // native cycle until both ankle trajectories remain reachable; no hidden leg scale,
            // larger runtime IK correction, or compensation for an invalid Avatar origin.
            float distance = requested;
            for (int attempt = 0; attempt < 60; ++attempt)
            {
                body.MaximumLegError = 0f; body.ClampedLegSamples = 0;
                for (int i = 0; i <= 120; ++i) { body.Rest.Restore(); Pose(body,slot,i/120f,distance); }
                if (body.MaximumLegError <= .001f) return distance;
                distance *= .97f;
            }
            throw new InvalidOperationException("No reachable native stride for " + slot + "; inspect the actual rest rig and author pose.");
        }

        static void Pose(Body body, string slot, float t, float distance)
        {
            bool moving = slot.StartsWith("Walk") || slot.StartsWith("Run"), run = slot.StartsWith("Run"), turn = slot.StartsWith("Turn");
            float cycle = t * Mathf.PI * 2f;
            Vector3 travel = slot.EndsWith("Left") ? Vector3.left : slot.EndsWith("Right") ? Vector3.right : slot.EndsWith("Back") ? Vector3.back : Vector3.forward;
            // Presentation defaults: run COM is lowest at mid-support (phase .14) and rises
            // into the flight window (.39). It must not peak while a flat stance leg trails.
            float bob = moving ? run ? .035f * (1f - Mathf.Cos(4f*Mathf.PI*(t-.14f))) : .008f * (1f-Mathf.Cos(cycle*2f)) : .003f*Mathf.Sin(cycle);
            float flex = moving ? (run ? .11f : .05f) : .025f;
            if(slot.StartsWith("Stop")) { bob=0f; flex=Mathf.Lerp(.08f,.025f,Smooth(t)); }
            body.Hips.position = body.HipRest + new Vector3(moving ? .016f*Mathf.Sin(cycle) : 0f,bob-flex,0f) + (moving && run ? travel*.025f : Vector3.zero);
            float yaw = moving ? 3f * Mathf.Sin(cycle) : 0f, roll = moving ? 1.5f * Mathf.Sin(cycle) : 0f;
            float lean = moving && run ? 6f : moving ? 2f : 0f;
            if (slot == "SteeringLeft" || slot == "SteeringRight")
            { float sign = slot == "SteeringLeft" ? -1f : 1f; yaw = sign * 12f * t; roll = -sign * 6f * t; }
            if (turn) { float sign = slot.Contains("Left") ? -1f : 1f; yaw = -sign * 22f * Mathf.Sin(Mathf.PI * t); roll = sign * 3f * Mathf.Sin(Mathf.PI * t); }
            body.Hips.rotation = Quaternion.Euler(0f, yaw, roll) * body.HipRotation;
            body.Spine.rotation = Quaternion.Euler(lean*travel.z,-yaw*.65f,-roll*.5f-lean*travel.x) * body.SpineRotation;
            body.Chest.rotation = Quaternion.Euler(lean*travel.z,-yaw,-roll*.3f-lean*travel.x) * body.ChestRotation;
            if (moving) FitHipsToLegReach(body,slot,t,distance,travel);
            PoseLeg(body, slot, t, true, distance, travel); PoseLeg(body, slot, t, false, distance, travel);
            PoseArm(body, true, moving ? cycle : 0f, moving, run, yaw);
            PoseArm(body, false, moving ? cycle : 0f, moving, run, yaw);
        }

        static void PoseLeg(Body body, string slot, float t, bool left, float distance, Vector3 travel)
        {
            Transform foot = body.Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            Transform upper = body.Bone(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            Transform lower = body.Bone(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            LegTarget(body,slot,t,left,distance,travel,out Vector3 ankleTarget,out Quaternion footRotation);
            SolveChain(upper,lower,foot,ankleTarget,Vector3.forward); foot.rotation = footRotation;
            float error = Vector3.Distance(foot.position,ankleTarget);
            body.MaximumLegError = Mathf.Max(body.MaximumLegError,error);
            if (error > .001f) body.ClampedLegSamples++;
        }
        static void FitHipsToLegReach(Body body,string slot,float t,float distance,Vector3 travel)
        {
            float requiredDrop = 0f;
            foreach (bool left in new[] { true,false })
            {
                LegTarget(body,slot,t,left,distance,travel,out Vector3 target,out Quaternion rotation);
                Transform upper = body.Bone(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                Vector3 planar = Vector3.ProjectOnPlane(target-upper.position,Vector3.up);
                float reach = body.ChainLength(left)*.985f;
                if (planar.sqrMagnitude >= reach*reach) continue; // Native-stride calibration resolves impossible horizontal reach.
                float highestReachableHip = target.y + Mathf.Sqrt(reach*reach-planar.sqrMagnitude);
                requiredDrop = Mathf.Max(requiredDrop,upper.position.y-highestReachableHip);
            }
            // Additional COM adjustment is bounded to10cm. It is authored into the clip;
            // no runtime leg extension, actor movement or Avatar scale adjustment is used.
            float drop = Mathf.Clamp(requiredDrop,0f,.10f);
            body.Hips.position -= Vector3.up*drop; body.MaximumHipReachDrop = Mathf.Max(body.MaximumHipReachDrop,drop);
        }
        static void LegTarget(Body body,string slot,float t,bool left,float distance,Vector3 travel,out Vector3 ankleTarget,out Quaternion footRotation)
        {
            Sole sole = left ? body.Left : body.Right;
            Transform foot = body.Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            Vector3 target = sole.measuredWorldPoint;
            bool moving = slot.StartsWith("Walk") || slot.StartsWith("Run"), run = slot.StartsWith("Run");
            float pitch = 0f;
            if (moving)
            {
                float phase = Mathf.Repeat(t + (left ? 0f : .5f), 1f), duty = run ? .28f : .60f;
                float lead = distance * duty * .5f;
                float displacement;
                if (phase < duty) displacement = lead - distance * phase;
                else
                {
                    float swing = (phase - duty) / (1f - duty);
                    displacement = Mathf.Lerp(-lead, lead, Smooth(swing));
                    target.y += (run ? .135f : .065f) * Mathf.Pow(Mathf.Sin(Mathf.PI * swing), 1.3f);
                    pitch = -8f * Mathf.Sin(Mathf.PI * swing);
                }
                target += travel * displacement;
            }
            else if (slot.StartsWith("Turn"))
            {
                // Two distinct planted/support and lift/replant phases. A torso lean alone is not a turn step.
                bool leftTurn = slot.Contains("Left"), leading = leftTurn == left;
                float start = leading ? .04f : .47f, end = leading ? .46f : .94f;
                float phase = Mathf.InverseLerp(start, end, t);
                if (t > start && t < end)
                {
                    float sign = leftTurn ? -1f : 1f, magnitude = slot.EndsWith("180") ? .13f : .085f;
                    target += new Vector3(sign * magnitude * Mathf.Sin(Mathf.PI * phase), .075f * Mathf.Sin(Mathf.PI * phase),
                        (leading ? .075f : -.055f) * Mathf.Sin(Mathf.PI * phase));
                }
            }
            else if(slot.StartsWith("Stop"))
            {
                // Original authored braking step: the opposite foot settles first, then
                // the initial support foot lifts/replants.64s;8cm clearance are visual defaults.
                bool support=(slot=="StopLeft")==left;
                float start=support ? .48f : .02f,end=support ? .92f : .43f;
                float step=Mathf.InverseLerp(start,end,t);
                target.z+=(support?-.15f:.15f)*(1f-Smooth(step));
                if(t>start && t<end) target.y+=.08f*Mathf.Sin(Mathf.PI*step);
            }
            else if (slot == "Air") { target.y += .10f; target.z -= left ? .08f : .02f; }
            // A stance foot keeps its original world orientation; hip counter-yaw/roll must
            // not be inherited by the planted shoe. Swing alone adds the authored toe pitch.
            footRotation = Quaternion.AngleAxis(pitch, Vector3.right) * (left ? body.LeftFootRotation : body.RightFootRotation);
            Vector3 localOffsetWorld = foot.TransformPoint(sole.solePointInFootLocal) - foot.position;
            ankleTarget = target - footRotation * Quaternion.Inverse(foot.rotation) * localOffsetWorld;
        }
        static void PoseArm(Body body, bool left, float phase, bool moving, bool run, float bodyYaw)
        {
            Transform upper = body.Bone(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lower = body.Bone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform hand = body.Bone(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            float sign = left ? -1f : 1f, opposite = left ? -1f : 1f;
            float reach = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
            float swing = moving ? opposite * (run ? .16f : .105f) * Mathf.Cos(phase) : 0f;
            Vector3 target = new Vector3(upper.position.x + sign * .055f, upper.position.y - reach * (run ? .70f : .88f), upper.position.z + swing + (run ? .10f : .015f));
            Quaternion wristInForearm = Quaternion.Inverse(lower.rotation) * hand.rotation;
            SolveChain(upper, lower, hand, target, new Vector3(sign * .20f, .1f, -.5f));
            hand.rotation = lower.rotation * wristInForearm;
        }
        static void SolveChain(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
        {
            Vector3 origin = upper.position;
            float first = Vector3.Distance(origin, lower.position), second = Vector3.Distance(lower.position, end.position);
            Vector3 vector = target - origin;
            float distance = Mathf.Clamp(vector.magnitude, Mathf.Abs(first - second) + .001f, (first + second) * .995f);
            Vector3 direction = vector.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
            if (bend.sqrMagnitude < .1f) bend = Vector3.ProjectOnPlane(Vector3.right, direction).normalized;
            float along = (first * first - second * second + distance * distance) / (2f * distance);
            Vector3 knee = origin + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, first * first - along * along));
            upper.rotation = Quaternion.FromToRotation(lower.position - origin, knee - origin) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, origin + direction * distance - lower.position) * lower.rotation;
        }

        static Sole MeasureSole(Body body, bool left, string modelHash)
        {
            Transform foot = body.Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            Transform toe = body.Bone(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
            var vertices = new List<Vector3>(); var scratch = new Mesh();
            try
            {
                foreach (var skin in body.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    int footIndex = Array.IndexOf(skin.bones, foot), toeIndex = Array.IndexOf(skin.bones, toe);
                    if (footIndex < 0) continue;
                    scratch.Clear(); skin.BakeMesh(scratch, true);
                    Vector3[] baked = scratch.vertices; BoneWeight[] weights = skin.sharedMesh.boneWeights;
                    for (int i = 0; i < baked.Length; i++)
                    {
                        var w = weights[i]; float sum = 0;
                        if (w.boneIndex0 == footIndex || w.boneIndex0 == toeIndex) sum += w.weight0;
                        if (w.boneIndex1 == footIndex || w.boneIndex1 == toeIndex) sum += w.weight1;
                        if (w.boneIndex2 == footIndex || w.boneIndex2 == toeIndex) sum += w.weight2;
                        if (w.boneIndex3 == footIndex || w.boneIndex3 == toeIndex) sum += w.weight3;
                        if (sum > .25f) vertices.Add(skin.transform.TransformPoint(baked[i]));
                    }
                }
            }
            finally { Object.DestroyImmediate(scratch); }
            if (vertices.Count < 20) throw new InvalidOperationException("Insufficient actual shoe vertices for " + (left ? "left" : "right"));
            float floor = vertices.Min(v => v.y);
            Vector3[] plane = vertices.Where(v => v.y < floor + .004f).ToArray();
            Vector3 point = new Vector3(foot.position.x, floor, foot.position.z);
            Vector3 forward = Vector3.ProjectOnPlane(toe.position - foot.position, Vector3.up).normalized;
            if (Vector3.Dot(forward, Vector3.forward) < .5f) throw new InvalidOperationException("Imported toe direction is not forward; verify FBX axis conversion before authoring.");
            return new Sole { calibrated = true, side = left ? "Left" : "Right", sourceModelHash = modelHash,
                evidence = "Unity imported rest BakeMesh vertices with >.25 foot/toe skin weight; lowest 4mm sole plane. Final visible scale uses existing ArtCharacterRoster normalization.",
                solePointInFootLocal = foot.InverseTransformPoint(point), soleNormalInFootLocal = foot.InverseTransformDirection(Vector3.up).normalized,
                forwardInFootLocal = foot.InverseTransformDirection(forward).normalized, measuredWorldPoint = point,
                measuredHeel = plane.OrderBy(v => Vector3.Dot(v, forward)).First(), measuredToe = plane.OrderBy(v => Vector3.Dot(v, forward)).Last(),
                footRestWorld = foot.position, toesRestWorld = toe.position, shoeVertices = vertices.Count, soleVertices = plane.Length, solePlaneY = floor };
        }
        static float Contact(string slot, float t, bool left)
        {
            if (slot == "Idle" || slot.StartsWith("Steering")) return 1f;
            if (slot.StartsWith("Walk") || slot.StartsWith("Run"))
            {
                float phase = Mathf.Repeat(t + (left ? 0f : .5f), 1f), duty = slot.StartsWith("Run") ? .28f : .60f;
                return phase >= duty ? 0f : Mathf.Min(Smooth(phase / .055f), Smooth((duty - phase) / .055f));
            }
            if (slot.StartsWith("Turn"))
            {
                bool leading = slot.Contains("Left") == left;
                float start = leading ? .04f : .47f, end = leading ? .46f : .94f;
                return t > start && t < end ? 0f : 1f;
            }
            if(slot.StartsWith("Stop"))
            {
                bool support=(slot=="StopLeft")==left;
                float start=support ? .48f : .02f,end=support ? .92f : .43f;
                return t>start && t<end ? 0f:1f;
            }
            return 0f;
        }
        static Slot CopySource(string slot, string group, string name, string output)
        {
            var source = Source(group, name); var clip = Object.Instantiate(source); clip.name = slot;
            string path = output + "/" + slot + ".anim"; AssetDatabase.CreateAsset(clip, path);
            return new Slot { slot = slot, clip = path, duration = clip.length, source = SourceRoot + "Rig_Medium_" + group + ".fbx / " + name,
                sourceLicense = "KayKit Character Animations 1.1 / CC0; selected-files.sha256.json", calibrationEvidence = "Existing approved source action; target-rig pose review pending",
                leftPlant = new[] { new CurvePoint { time = 0, value = 0 }, new CurvePoint { time = 1, value = 0 } },
                rightPlant = new[] { new CurvePoint { time = 0, value = 0 }, new CurvePoint { time = 1, value = 0 } } };
        }
        static AnimationClip Source(string group, string name) => AssetDatabase.LoadAllAssetsAtPath(SourceRoot + "Rig_Medium_" + group + ".fbx")
            .OfType<AnimationClip>().SingleOrDefault(c => c.name == name && c.isHumanMotion) ?? throw new InvalidOperationException("Missing licensed Humanoid source clip " + name);
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static string Hash(string path)
        { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static string Argument(string name)
        { string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    }
}
#endif
