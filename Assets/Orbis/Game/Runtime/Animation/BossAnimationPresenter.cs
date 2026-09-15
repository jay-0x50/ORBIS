using System;
using System.Linq;
using Orbis.M4;
using UnityEngine;

namespace Orbis.Game.Animation
{
    /// <summary>
    /// Visual-only adapter for the existing fixed-anchor M4 boss. It reads state/health;
    /// it never calls Tick, Apply, RegisterDirectHit or changes the encounter transform.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)] // M4's -55 Update runs first; Animator evaluates after ordinary Updates.
    public sealed class BossAnimationPresenter : MonoBehaviour
    {
        [SerializeField] M4FieldBoss encounter;
        [SerializeField] Animator animator;
        [SerializeField] BossMotionProfile profile;
        readonly BossPoseClock clock=new BossPoseClock();
        bool bound, observed;
        float previousHealth, hurtElapsed=float.PositiveInfinity;
        BossVisualState visualState;
        static readonly string[] Names={"Idle","Attack","Exposed","Dead"};
        static readonly int[] States=Names.Select(n=>Animator.StringToHash("Base Layer."+n)).ToArray();
        static readonly int[] Times=Names.Select(n=>Animator.StringToHash(n+"Time")).ToArray();
        static readonly int HurtTime=Animator.StringToHash("HurtTime");

        public void Configure(M4FieldBoss owner,Animator target,BossMotionProfile motions)
        {
            encounter=owner; animator=target; profile=motions; bound=false;
            Bind(); ResetPresentation();
        }

        void OnEnable() => ResetPresentation();
        void OnDisable()
        {
            if(bound && animator!=null) animator.SetLayerWeight(1,0f);
            clock.Reset(); observed=false;
        }

        public void ResetPresentation()
        {
            clock.Reset(); observed=false; hurtElapsed=float.PositiveInfinity;
            if(bound && animator!=null) animator.SetLayerWeight(1,0f);
        }

        void Bind()
        {
            if(encounter==null || animator==null || profile==null)
                throw new InvalidOperationException("Boss presentation needs saved encounter, Animator and motion profile references.");
            profile.Validate();
            if(animator.avatar==null || !animator.avatar.isValid || animator.avatar.isHuman)
                throw new InvalidOperationException("Boss presentation requires the reviewed Generic Avatar.");
            if(animator.runtimeAnimatorController!=profile.Controller)
                throw new InvalidOperationException("Boss Animator is not using its reviewed motion controller.");
            if(animator.layerCount!=2 || animator.GetLayerName(1)!="Hurt")
                throw new InvalidOperationException("Boss controller requires Base Layer and masked Hurt layer.");
            for(int i=0;i<States.Length;i++)
                if(!animator.HasState(0,States[i]) || !animator.parameters.Any(p=>p.nameHash==Times[i] && p.type==AnimatorControllerParameterType.Float))
                    throw new InvalidOperationException("Boss controller is missing "+Names[i]+" state/time.");
            if(!animator.HasState(1,Animator.StringToHash("Hurt.Hurt")) ||
                !animator.parameters.Any(p=>p.nameHash==HurtTime && p.type==AnimatorControllerParameterType.Float))
                throw new InvalidOperationException("Boss controller lacks a clocked Hurt clip.");
            animator.applyRootMotion=false;
            bound=true;
        }

        void Update()
        {
            // Configure runs when the existing scene binder initializes the M4 encounter.
            // Saved art is visible in Edit Mode without executing any gameplay initialization.
            if(encounter==null || encounter.Profile==null) return;
            if(!bound) Bind();
            if(observed && encounter.HitPoints>previousHealth)
                ResetPresentation(); // Leash/reset restores the encounter without replaying an old windup.
            if(observed && encounter.HitPoints<previousHealth && encounter.State!=M4BossState.Defeated &&
                encounter.State!=M4BossState.Exposed)
                hurtElapsed=0f;
            previousHealth=encounter.HitPoints;
            var sample=clock.Observe(encounter.State,encounter.StateRemaining,encounter.TelegraphProgress,
                encounter.Profile,Time.deltaTime,profile.AttackContact,profile.Idle.length,
                profile.Exposed.length,profile.Dead.length);
            int index=(int)sample.State;
            animator.SetFloat(Times[index],sample.NormalizedTime);
            if(!observed || sample.State!=visualState)
            {
                if(sample.Immediate || !observed) animator.Play(States[index],0,sample.NormalizedTime);
                else animator.CrossFadeInFixedTime(States[index],profile.TransitionSeconds,0);
                visualState=sample.State;
            }
            if(sample.State==BossVisualState.Dead || sample.State==BossVisualState.Exposed)
                hurtElapsed=float.PositiveInfinity;
            float desiredWeight=0f;
            if(!float.IsPositiveInfinity(hurtElapsed))
            {
                hurtElapsed+=Time.deltaTime;
                animator.SetFloat(HurtTime,Mathf.Clamp01(hurtElapsed/profile.Hurt.length));
                desiredWeight=hurtElapsed<profile.Hurt.length ? profile.HurtWeight : 0f;
            }
            // The masked pose bends upper-body bones only; leg contact and the M4 attack clock continue.
            animator.SetLayerWeight(1,Mathf.MoveTowards(animator.GetLayerWeight(1),desiredWeight,
                Time.deltaTime/profile.HurtFadeSeconds));
            observed=true;
        }
    }
}
