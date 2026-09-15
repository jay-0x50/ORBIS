using System;
using UnityEngine;

namespace Orbis.Game.Animation
{
    /// <summary>Six original Generic clips for one reviewed anatomy. No combat balance data.</summary>
    [CreateAssetMenu(menuName="Orbis/Art/Boss Motion Profile")]
    public sealed class BossMotionProfile : ScriptableObject
    {
        public AnimationClip Idle, Move, Attack, Hurt, Exposed, Dead;
        public RuntimeAnimatorController Controller;
        public AvatarMask HurtMask;
        [Range(.01f,.99f)] public float AttackContact=.5f;
        // Presentation defaults only, tuned after actual rig pose review. Never alter M4 attack timing.
        [Range(0f,.3f)] public float TransitionSeconds=.12f;
        [Range(0f,1f)] public float HurtWeight=.55f;
        [Min(.001f)] public float HurtFadeSeconds=.08f;
        public string SourceRigSha256;
        [TextArea] public string Provenance;

        public void Validate()
        {
            foreach(var clip in new[]{Idle,Move,Attack,Hurt,Exposed,Dead})
                if(clip==null || clip.humanMotion || clip.legacy || clip.length<=0f || clip.events.Length!=0)
                    throw new InvalidOperationException(name+": six Generic clips without damage events are required.");
            if(Controller==null || HurtMask==null || string.IsNullOrEmpty(SourceRigSha256) || string.IsNullOrEmpty(Provenance))
                throw new InvalidOperationException(name+": controller, mask and verified source provenance required.");
            if(float.IsNaN(AttackContact) || AttackContact<=0f || AttackContact>=1f ||
                float.IsNaN(TransitionSeconds) || TransitionSeconds<0f || TransitionSeconds>.3f ||
                float.IsNaN(HurtWeight) || HurtWeight<0f || HurtWeight>1f ||
                float.IsNaN(HurtFadeSeconds) || float.IsInfinity(HurtFadeSeconds) || HurtFadeSeconds<=0f)
                throw new InvalidOperationException(name+": invalid presentation settings.");
        }
    }
}
