using Orbis.M0;
using UnityEngine;

namespace Orbis.M1
{
    /// <summary>Add M1 elements to M0's already validated, wall-checked, once-per-strike hit events.</summary>
    public sealed class PartyCombatBridge : MonoBehaviour
    {
        // Spec does not give base damage: all placeholder weapons use ten damage per combo strike in M1.
        [SerializeField, Min(0f)] private float basicDamage = 10f;
        // Basic attacks attach a single weak tag for four seconds. Gauge-strength mechanics are deferred.
        [SerializeField, Min(0.1f)] private float attachmentDuration = 4f;
        private BasicAttackCombo combat;
        private PartyManager party;
        private ElementalReactionManager reactions;

        public void Configure(BasicAttackCombo attackCombo, PartyManager partyManager,
            ElementalReactionManager reactionManager)
        {
            if (combat != null) combat.HitLanded -= OnHit;
            combat = attackCombo;
            party = partyManager;
            reactions = reactionManager;
            if (combat != null && isActiveAndEnabled) combat.HitLanded += OnHit;
        }

        private void OnEnable()
        {
            if (combat != null) combat.HitLanded += OnHit;
        }

        private void OnDisable()
        {
            if (combat != null) combat.HitLanded -= OnHit;
        }

        private void OnHit(TrainingDummy dummy, int step)
        {
            if (party == null || reactions == null || party.ActiveMember == null || dummy == null) return;
            var target = dummy.GetComponent<ElementalActor>();
            if (target == null) return;
            ElementalActor source = party.ActiveMember.Actor;
            float damage = party.ActiveMember.BasicAttackDamage ?? basicDamage;
            if (party.ActiveMember.IsPermanent) reactions.DealPhysicalDamage(target, source, damage);
            else reactions.Apply(target, source.Element, source, damage, attachmentDuration);
        }
    }
}
