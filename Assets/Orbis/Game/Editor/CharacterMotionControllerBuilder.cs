#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Orbis.M0.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.Game.Editor
{
    /// <summary>
    /// Candidate editor builder; no menu or InitializeOnLoad. Invoked explicitly only after the
    /// new rig, authored/contact-calibrated clips and same-model baseline have passed review.
    /// Never edits the original M0 controller or imported CC0 FBXs.
    /// </summary>
    public static class CharacterMotionControllerBuilder
    {
        public static AnimatorController Build(CharacterMotionProfile profile, string folder)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.ValidateForBuild();
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (!folder.StartsWith("Assets/Orbis/Characters/Animation/", StringComparison.Ordinal))
                throw new InvalidOperationException("Use a new versioned Assets/Orbis/Characters/Animation/ output folder.");
            string path = folder + "/Character.controller";
            if (File.Exists(path) || AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
                throw new InvalidOperationException("Do not overwrite a reviewed motion controller. Use a new candidate output version.");
            Directory.CreateDirectory(folder + "/Clips"); AssetDatabase.Refresh();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            foreach (string parameter in HumanAnimationDriver.RequiredParameters)
                controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            var parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].name == HumanAnimationDriver.RateParameter || parameters[i].name == HumanAnimationDriver.ZParameter)
                    parameters[i].defaultFloat = 1f;
            controller.parameters = parameters;
            var clips = new Dictionary<HumanMotionSlot, AnimationClip>();
            foreach (HumanMotionSlot slot in Enum.GetValues(typeof(HumanMotionSlot)))
            {
                var source = profile.Get(slot);
                var clip = Object.Instantiate(source.Clip); clip.name = slot.ToString();
                // Derived Humanoid assets preserve their retargeted muscle curves; external source bytes remain unchanged.
                if (slot <= HumanMotionSlot.RunBack || CharacterMotionProfile.IsTurn(slot) || CharacterMotionProfile.IsStop(slot))
                {
                    SetContact(clip, HumanAnimationDriver.LeftPlantParameter, source.LeftPlant);
                    SetContact(clip, HumanAnimationDriver.RightPlantParameter, source.RightPlant);
                }
                if (slot >= HumanMotionSlot.SteeringNeutral && slot <= HumanMotionSlot.SteeringRight)
                {
                    // The supplied steering poses must share the same retargeted neutral reference.
                    // Configure a consistent additive reference instead of interpreting an absolute pose as a delta.
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.hasAdditiveReferencePose = true;
                    settings.additiveReferencePoseTime = 0f;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    // Caller supplies pose clips whose first frame is the same neutral reference;
                    // the held authored offset must be sampled at normalizedTime 1 in the steering layer.
                }
                AssetDatabase.CreateAsset(clip, folder + "/Clips/" + slot + ".anim"); clips.Add(slot, clip);
            }
            var machine = controller.layers[0].stateMachine;
            var locomotion = State(machine, "Locomotion", BuildLocomotion(profile, controller, clips));
            locomotion.speedParameter = HumanAnimationDriver.RateParameter; locomotion.speedParameterActive = true;
            machine.defaultState = locomotion;
            State(machine, "Air", clips[HumanMotionSlot.Air]);
            foreach (var slot in new[] { HumanMotionSlot.Attack1, HumanMotionSlot.Attack2, HumanMotionSlot.Attack3, HumanMotionSlot.Burst, HumanMotionSlot.Dead })
                TimedState(machine, slot.ToString(), clips[slot]);
            // Candidate-only turn states expose real authored left/right foot lift/replant clips.
            // Their gameplay clock selection is deliberately pending actual yaw/contact review;
            // a torso steering pose is not a substitute for enabling this separate step motion.
            foreach (var slot in new[] { HumanMotionSlot.TurnLeft90, HumanMotionSlot.TurnRight90, HumanMotionSlot.TurnLeft180, HumanMotionSlot.TurnRight180 })
                TimedState(machine, slot.ToString(), clips[slot]);
            foreach(var slot in new[]{HumanMotionSlot.StopLeft,HumanMotionSlot.StopRight})
                TimedState(machine,slot.ToString(),clips[slot]);
            var upperMask = Mask(folder + "/UpperBody.mask", false);
            var steeringMask = Mask(folder + "/Steering.mask", true);
            var upperMachine = new AnimatorStateMachine { name = HumanAnimationDriver.UpperLayer };
            AssetDatabase.AddObjectToAsset(upperMachine, controller);
            upperMachine.defaultState = State(upperMachine, "Empty", null);
            TimedState(upperMachine, "Skill", clips[HumanMotionSlot.Skill]);
            TimedState(upperMachine, "Hurt", clips[HumanMotionSlot.Hurt]);
            var steeringMachine = new AnimatorStateMachine { name = HumanAnimationDriver.SteeringLayer };
            AssetDatabase.AddObjectToAsset(steeringMachine, controller);
            var steeringTree = Tree(controller, "Steering poses", BlendTreeType.Simple1D, HumanAnimationDriver.TurnParameter);
            steeringTree.useAutomaticThresholds = false;
            steeringTree.AddChild(clips[HumanMotionSlot.SteeringLeft], -1f);
            steeringTree.AddChild(clips[HumanMotionSlot.SteeringNeutral], 0f);
            steeringTree.AddChild(clips[HumanMotionSlot.SteeringRight], 1f);
            var steer = State(steeringMachine, "SteeringPose", steeringTree);
            // Pose assets use their first key as common additive reference and end key as held offset.
            // speed 0 + cycleOffset 1 would wrap a looping source, so sample explicitly with a constant parameter.
            var poseParameters = controller.parameters;
            foreach (var parameter in poseParameters) if (parameter.name == "SteeringPoseTime") parameter.defaultFloat = 1f;
            controller.parameters = poseParameters;
            steer.timeParameterActive = true; steer.timeParameter = "SteeringPoseTime";
            steeringMachine.defaultState = steer;
            var baseLayer = controller.layers[0]; baseLayer.iKPass = true;
            controller.layers = new[]
            {
                baseLayer,
                new AnimatorControllerLayer { name = HumanAnimationDriver.UpperLayer, defaultWeight = 0f,
                    avatarMask = upperMask, blendingMode = AnimatorLayerBlendingMode.Override, stateMachine = upperMachine, iKPass = false },
                new AnimatorControllerLayer { name = HumanAnimationDriver.SteeringLayer, defaultWeight = 0f,
                    avatarMask = steeringMask, blendingMode = AnimatorLayerBlendingMode.Additive, stateMachine = steeringMachine, iKPass = false }
            };
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets(); return controller;
        }

        static BlendTree BuildLocomotion(CharacterMotionProfile profile, AnimatorController owner, Dictionary<HumanMotionSlot, AnimationClip> clips)
        {
            var root = Tree(owner, "Speed locomotion", BlendTreeType.Simple1D, HumanAnimationDriver.SpeedParameter);
            root.useAutomaticThresholds = false;
            root.AddChild(clips[HumanMotionSlot.Idle], 0f);
            root.AddChild(Directional(owner, clips, false), profile.WalkThreshold);
            root.AddChild(Directional(owner, clips, true), profile.RunThreshold);
            return root;
        }
        static BlendTree Directional(AnimatorController owner, Dictionary<HumanMotionSlot, AnimationClip> clips, bool run)
        {
            var tree = Tree(owner, run ? "Run directions" : "Walk directions", BlendTreeType.FreeformDirectional2D, HumanAnimationDriver.XParameter);
            tree.blendParameterY = HumanAnimationDriver.ZParameter;
            tree.AddChild(clips[run ? HumanMotionSlot.Run : HumanMotionSlot.Walk], Vector2.up);
            tree.AddChild(clips[run ? HumanMotionSlot.RunBack : HumanMotionSlot.WalkBack], Vector2.down);
            tree.AddChild(clips[run ? HumanMotionSlot.RunLeft : HumanMotionSlot.WalkLeft], Vector2.left);
            tree.AddChild(clips[run ? HumanMotionSlot.RunRight : HumanMotionSlot.WalkRight], Vector2.right);
            return tree;
        }
        static BlendTree Tree(AnimatorController owner, string name, BlendTreeType type, string parameter)
        {
            var tree = new BlendTree { name = name, blendType = type, blendParameter = parameter };
            AssetDatabase.AddObjectToAsset(tree, owner); return tree;
        }
        static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion)
        { var state = machine.AddState(name); state.motion = motion; state.writeDefaultValues = false; state.iKOnFeet = false; return state; }
        static AnimatorState TimedState(AnimatorStateMachine machine, string name, AnimationClip clip)
        { var state = State(machine, name, clip); state.timeParameterActive = true; state.timeParameter = name + "Time"; return state; }
        static void SetContact(AnimationClip clip, string property, AnimationCurve normalized)
        {
            var keys = normalized.keys;
            for (int i = 0; i < keys.Length; i++)
            { keys[i].time *= clip.length; keys[i].inTangent /= clip.length; keys[i].outTangent /= clip.length; }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), new AnimationCurve(keys));
        }
        static AvatarMask Mask(string path, bool bodyOnly)
        {
            var mask = new AvatarMask { name = bodyOnly ? "Steering torso" : "Skill and hurt upper body" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            if (!bodyOnly)
            {
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true); mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true); mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            }
            // Root, legs and IK goals always remain in the locomotion/base layer.
            AssetDatabase.CreateAsset(mask, path); return mask;
        }
    }
}
#endif
