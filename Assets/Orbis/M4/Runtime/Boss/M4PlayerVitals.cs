using System;
using Orbis.M1;
using UnityEngine;

namespace Orbis.M4
{
    /// <summary>M4 공유 파티 체력. 원소 반응/환경 피해는 M1의 실드 흡수 후 값만 한 번 소비한다.</summary>
    [DisallowMultipleComponent]
    public sealed class M4PlayerVitals : MonoBehaviour
    {
        private ElementalReactionManager manager;
        private PartyManager party;
        private bool subscribed;
        public float Health { get; private set; }
        public float MaximumHealth { get; private set; } = 100f;
        public bool IsKnockedOut => Health <= 0f;
        public event Action Changed;
        public event Action KnockedOut;

        public void Configure(ElementalReactionManager reactionManager, PartyManager owner, float maximumHealth = 100f)
        {
            if (reactionManager == null || owner == null) throw new ArgumentNullException("M4 vitals require reactions and a party.");
            if (float.IsNaN(maximumHealth) || float.IsInfinity(maximumHealth) || maximumHealth <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maximumHealth));
            Unsubscribe();
            manager = reactionManager; party = owner; MaximumHealth = maximumHealth;
            // 기획서 미정 기본 체력 100. 캐릭터 전환은 이 공유 값을 회복시키지 않는다.
            Restore();
            Subscribe();
        }

        public void Restore() => Restore(MaximumHealth);
        public void Restore(float health)
        {
            if (float.IsNaN(health) || float.IsInfinity(health)) throw new ArgumentOutOfRangeException(nameof(health));
            Health = Mathf.Clamp(health, 0f, MaximumHealth); Changed?.Invoke();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled || manager == null) return;
            manager.DamageApplied += OnDamage;
            subscribed = true;
        }
        private void Unsubscribe()
        {
            if (subscribed && manager != null) manager.DamageApplied -= OnDamage;
            subscribed = false;
        }
        private void OnDamage(ElementDamageEvent damage)
        {
            if (IsKnockedOut || party == null || party.ActiveMember == null ||
                damage.Target != party.ActiveMember.Actor || damage.Target.Team != ActorTeam.Player ||
                !damage.Target.IsOnField || damage.AppliedDamage <= 0f) return;
            Health = Mathf.Max(0f, Health - damage.AppliedDamage);
            Changed?.Invoke();
            if (IsKnockedOut) KnockedOut?.Invoke();
            // 구조/텔레포트는 호출자 LateUpdate에서 처리해 동일 반응의 후속 피해 이벤트가 끝나도록 한다.
        }
    }
}

