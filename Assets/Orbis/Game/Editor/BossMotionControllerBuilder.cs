using System;
using System.IO;
using System.Linq;
using Orbis.Game.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Orbis.Game.Editor
{
    /// <summary>Explicitly invoked after six original clips and their contact pose have been reviewed.</summary>
    public static class BossMotionControllerBuilder
    {
        public static AnimatorController Build(BossMotionProfile profile,GameObject rigPrefab,
            string[] hurtBoneNames,string folder)
        {
            if(profile==null || rigPrefab==null || hurtBoneNames==null || hurtBoneNames.Length==0)
                throw new ArgumentException("Reviewed rig, profile and explicit hurt-bone mask are required.");
            folder=folder.Replace('\\','/').TrimEnd('/');
            if(!folder.StartsWith("Assets/Orbis/Game/Characters/Bosses/",StringComparison.Ordinal) ||
                folder.Split('/').Any(p=>p==".." || p=="." || p.Length==0))
                throw new ArgumentException("Use a new boss motion asset subfolder.");
            if(Directory.Exists(folder)) throw new IOException("Preserve previously reviewed motion assets; use a fresh output folder.");
            var animator=rigPrefab.GetComponent<Animator>();
            if(animator==null || animator.avatar==null || !animator.avatar.isValid || animator.avatar.isHuman)
                throw new InvalidOperationException("A reviewed Generic rig prefab is required.");
            var names=new[]{"Idle","Move","Attack","Hurt","Exposed","Dead"};
            var clips=new[]{profile.Idle,profile.Move,profile.Attack,profile.Hurt,profile.Exposed,profile.Dead};
            for(int i=0;i<clips.Length;i++)
            {
                var clip=clips[i];
                if(clip==null || clip.humanMotion || clip.legacy || clip.events.Length!=0 || clip.length<=0f)
                    throw new InvalidOperationException("Invalid original Generic clip: "+names[i]);
                foreach(var curve in AnimationUtility.GetCurveBindings(clip))
                    if(curve.path=="" && curve.type==typeof(Transform))
                        throw new InvalidOperationException("A visual clip may not animate the Animator/gameplay root: "+clip.name);
            }
            var transforms=rigPrefab.GetComponentsInChildren<Transform>(true);
            var selected=hurtBoneNames.Select(name=>
            {
                var matches=transforms.Where(t=>t.name==name).ToArray();
                if(matches.Length!=1 || matches[0]==rigPrefab.transform)
                    throw new InvalidOperationException("Ambiguous/missing/root hurt bone: "+name);
                return matches[0];
            }).ToArray();
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var mask=new AvatarMask { name=profile.name+" Hurt bones" };
            mask.transformCount=transforms.Length;
            for(int i=0;i<transforms.Length;i++)
            {
                mask.SetTransformPath(i,AnimationUtility.CalculateTransformPath(transforms[i],rigPrefab.transform));
                mask.SetTransformActive(i,selected.Contains(transforms[i]));
            }
            for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);
            AssetDatabase.CreateAsset(mask,folder+"/Hurt.mask");
            var controller=AnimatorController.CreateAnimatorControllerAtPath(folder+"/Boss.controller");
            var machine=controller.layers[0].stateMachine;
            for(int i=0;i<names.Length;i++)
            {
                if(names[i]=="Move" || names[i]=="Hurt") continue;
                controller.AddParameter(names[i]+"Time",AnimatorControllerParameterType.Float);
                var state=Timed(machine,names[i],clips[i]);
                if(names[i]=="Idle") machine.defaultState=state;
            }
            var hurtMachine=new AnimatorStateMachine { name="Hurt" };
            AssetDatabase.AddObjectToAsset(hurtMachine,controller);
            controller.AddParameter("HurtTime",AnimatorControllerParameterType.Float);
            hurtMachine.defaultState=Timed(hurtMachine,"Hurt",profile.Hurt);
            controller.layers=new[]{controller.layers[0],new AnimatorControllerLayer
            {
                name="Hurt",stateMachine=hurtMachine,avatarMask=mask,defaultWeight=0f,
                blendingMode=AnimatorLayerBlendingMode.Override,iKPass=false
            }};
            // A separate review controller exposes Move without connecting it to the fixed-anchor encounter.
            var review=AnimatorController.CreateAnimatorControllerAtPath(folder+"/BossReview.controller");
            for(int i=0;i<names.Length;i++)
            {
                var state=review.layers[0].stateMachine.AddState(names[i]);
                state.motion=clips[i]; state.writeDefaultValues=false;
                if(names[i]=="Idle") review.layers[0].stateMachine.defaultState=state;
            }
            profile.Controller=controller; profile.HurtMask=mask; profile.Validate();
            EditorUtility.SetDirty(profile); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
            return controller;
        }

        static AnimatorState Timed(AnimatorStateMachine machine,string name,AnimationClip clip)
        {
            var state=machine.AddState(name); state.motion=clip; state.writeDefaultValues=false;
            state.timeParameterActive=true; state.timeParameter=name+"Time"; state.iKOnFeet=false;
            return state;
        }
    }
}
