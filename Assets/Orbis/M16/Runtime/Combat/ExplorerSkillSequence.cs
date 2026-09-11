using System;
using Orbis.M1;

namespace Orbis.M16
{
    /// <summary>Immutable cast data: changing the selected element or an authoring asset cannot change an in-flight hit.</summary>
    public sealed class ExplorerCast
    {
        public string SkillId { get; }
        public ElementType Element { get; }
        public float Damage { get; }
        public float Range { get; }
        public float Radius { get; }
        public float Duration { get; }
        public float HitTime { get; }
        public float AttachmentDuration { get; }
        internal ExplorerCast(ExplorerSkillDefinition skill)
        {
            SkillId = skill.Id; Element = skill.Element; Damage = skill.Damage;
            Range = skill.Range; Radius = skill.Radius; Duration = skill.Duration;
            HitTime = skill.HitTime; AttachmentDuration = skill.AttachmentDuration;
        }
    }

    /// <summary>A shared cooldown and one scheduled hit, independent of animator frames and element selection.</summary>
    public sealed class ExplorerSkillSequence
    {
        public ExplorerCast CurrentCast { get; private set; }
        public bool IsCasting => CurrentCast != null;
        public float Elapsed { get; private set; }
        public float NormalizedTime => CurrentCast == null ? 0f : Math.Min(1f, Elapsed / CurrentCast.Duration);
        public float CooldownRemaining { get; private set; }
        private bool hitDispatched;
        public event Action<ExplorerCast> HitReady;
        public event Action Ended;

        public bool TryStart(ExplorerSkillDefinition skill, float cooldown)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));
            ValidateSeconds(cooldown);
            skill.Validate();
            if (IsCasting || CooldownRemaining > 0f) return false;
            CurrentCast = new ExplorerCast(skill);
            CooldownRemaining = cooldown;
            Elapsed = 0f; hitDispatched = false;
            return true;
        }

        public void Tick(float deltaTime)
        {
            ValidateSeconds(deltaTime);
            CooldownRemaining = Math.Max(0f, CooldownRemaining - deltaTime);
            ExplorerCast cast = CurrentCast;
            if (cast == null) return;
            Elapsed = Math.Min(cast.Duration, Elapsed + deltaTime);
            if (!hitDispatched && Elapsed >= cast.HitTime)
            {
                hitDispatched = true; // Claim before publishing; reentrant cancellation cannot emit another hit.
                HitReady?.Invoke(cast);
            }
            if (ReferenceEquals(CurrentCast, cast) && Elapsed >= cast.Duration) Cancel();
        }

        public void Cancel()
        {
            if (!IsCasting) return;
            CurrentCast = null;
            Elapsed = 0f; hitDispatched = false;
            Ended?.Invoke();
        }

        public void RestoreCooldown(float seconds)
        {
            ValidateSeconds(seconds);
            // Import/extend an existing session lock; this API cannot shorten cooldown or cancel a cast.
            CooldownRemaining = Math.Max(CooldownRemaining, seconds);
        }

        private static void ValidateSeconds(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
