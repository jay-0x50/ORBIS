using System;
using Orbis.M4;
using UnityEngine;

namespace Orbis.Game.Animation
{
    public enum BossVisualState { Idle, Attack, Exposed, Dead }

    public readonly struct BossPoseSample
    {
        public readonly BossVisualState State;
        public readonly float NormalizedTime;
        public readonly bool Immediate;
        public BossPoseSample(BossVisualState state, float time, bool immediate=false)
        { State=state; NormalizedTime=time; Immediate=immediate; }
    }

    /// <summary>
    /// Reads the existing M4 encounter clock. It cannot move a boss or execute an attack.
    /// One authored Attack clip is split at its documented contact pose: the M4 telegraph
    /// plays its windup; M4 recovery plays its follow-through. An interrupted windup never
    /// plays the contact pose when core exposure ends.
    /// </summary>
    public sealed class BossPoseClock
    {
        bool initialized, attackRecovery;
        M4BossState previous;
        float idleElapsed, deathElapsed;

        public void Reset()
        { initialized=false; attackRecovery=false; idleElapsed=deathElapsed=0f; }

        public BossPoseSample Observe(M4BossState state, float remaining, float telegraphProgress,
            M4BossProfile encounter, float deltaTime, float attackContact,
            float idleDuration, float exposedDuration, float deathDuration)
        {
            if(encounter==null) throw new ArgumentNullException(nameof(encounter));
            Validate(deltaTime, true); Validate(remaining, true);
            Validate(idleDuration, false); Validate(exposedDuration, false); Validate(deathDuration, false);
            if(float.IsNaN(telegraphProgress) || float.IsInfinity(telegraphProgress) ||
                float.IsNaN(attackContact) || attackContact<=0f || attackContact>=1f)
                throw new ArgumentOutOfRangeException(nameof(attackContact));

            bool first=!initialized;
            bool changed=first || previous!=state;
            if(changed)
            {
                // Recover after Exposed is a vulnerable recovery, not a delayed attack.
                attackRecovery=!first && state==M4BossState.Recover && previous==M4BossState.Telegraph;
                if(state==M4BossState.Dormant) idleElapsed=0f;
                if(state==M4BossState.Defeated) deathElapsed=first ? deathDuration : 0f;
            }
            previous=state; initialized=true;
            switch(state)
            {
                case M4BossState.Telegraph:
                    return new BossPoseSample(BossVisualState.Attack,
                        Mathf.Clamp01(telegraphProgress)*attackContact,first);
                case M4BossState.Recover:
                    if(attackRecovery)
                        return new BossPoseSample(BossVisualState.Attack,
                            Mathf.Lerp(attackContact,1f,Mathf.Clamp01(1f-remaining/encounter.RecoveryDuration)),first);
                    idleElapsed+=deltaTime;
                    return new BossPoseSample(BossVisualState.Idle,idleElapsed/idleDuration,first);
                case M4BossState.Exposed:
                    return new BossPoseSample(BossVisualState.Exposed,
                        Mathf.Max(0f,encounter.ExposureDuration-remaining)/exposedDuration,first);
                case M4BossState.Defeated:
                    if(!changed) deathElapsed+=deltaTime;
                    return new BossPoseSample(BossVisualState.Dead,Mathf.Clamp01(deathElapsed/deathDuration),first);
                case M4BossState.Dormant:
                    idleElapsed+=deltaTime;
                    return new BossPoseSample(BossVisualState.Idle,idleElapsed/idleDuration,first);
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        static void Validate(float value,bool zeroAllowed)
        {
            if(float.IsNaN(value) || float.IsInfinity(value) || (zeroAllowed ? value<0f : value<=0f))
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
