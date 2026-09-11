using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis.M1
{
    /// <summary>캐릭터/타깃의 M1 원소 상태. 누적 피해만 기록하며 체력·사망·AI는 구현하지 않는다.</summary>
    [DisallowMultipleComponent]
    public sealed class ElementalActor : MonoBehaviour
    {
        private static readonly HashSet<ElementalActor> Registered = new HashSet<ElementalActor>();
        [SerializeField] private string sourceId = "";
        [SerializeField] private ElementType element;
        [SerializeField] private ActorTeam team = ActorTeam.Enemy;

        public string SourceId => sourceId;
        public ElementType Element => element;
        public ActorTeam Team => team;
        // 비활성 파티원도 GameObject는 켜 둔다. IsOnField만 바꿔 오프필드 타이머를 유지한다.
        public bool IsOnField { get; set; } = true;
        public ElementType AuraElement { get; private set; }
        public float AuraRemainingTime { get; private set; }
        public string AuraSourceId { get; private set; } = "";
        public ElementalAura Aura => new ElementalAura(AuraElement, AuraRemainingTime, AuraSourceId);
        public ElementType ShieldElement { get; private set; }
        public float ShieldAmount { get; private set; }
        public float ShieldRemainingTime { get; private set; }
        public ElementalShield Shield => new ElementalShield(ShieldElement, ShieldAmount, ShieldRemainingTime);
        public float DamageTaken { get; private set; }
        public float TotalShieldAbsorbed { get; private set; }
        public Vector3 LastKnockback { get; internal set; }
        // Optional M4 boss exposure adapter; null preserves the M1/M2/M3 damage rules.
        public Func<ElementType, float> IncomingDamageScale { get; set; }

        public void Configure(string id, ElementType identity, ActorTeam actorTeam)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An actor needs a stable source ID.", nameof(id));
            sourceId = id;
            element = identity;
            team = actorTeam;
            Registered.Add(this);
        }

        /// <summary>Change the selected attack element without replacing identity, aura, shield or damage state.</summary>
        public void SetElement(ElementType selected)
        {
            if (selected == ElementType.None || !Enum.IsDefined(typeof(ElementType), selected))
                throw new ArgumentOutOfRangeException(nameof(selected));
            element = selected;
        }

        public void GrantShield(ElementType shieldElement, float amount = 25f, float duration = 6f)
        {
            ValidateNonnegative(amount, nameof(amount));
            ValidateNonnegative(duration, nameof(duration));
            if (shieldElement == ElementType.None || amount == 0f || duration == 0f)
            { ClearShield(); return; }
            // 기획서 미정 M1 기본값: 흡수량 25, 지속 6초. 재획득은 교체하며 중첩하지 않는다.
            // 속성은 관찰/식별용이며 이번 범위에서는 피해 속성별 흡수 효율 차이를 추가하지 않는다.
            ShieldElement = shieldElement;
            ShieldAmount = amount;
            ShieldRemainingTime = duration;
        }

        public void ResetState()
        {
            ClearAura();
            ClearShield();
            DamageTaken = TotalShieldAbsorbed = 0f;
            LastKnockback = Vector3.zero;
        }

        internal static List<ElementalActor> Snapshot()
        {
            Registered.RemoveWhere(actor => actor == null);
            var result = new List<ElementalActor>();
            foreach (ElementalActor actor in Registered)
                if (actor.isActiveAndEnabled) result.Add(actor);
            return result;
        }

        internal void SetAura(ElementType aura, string ownerId, float duration)
        {
            if (aura == ElementType.None || duration <= 0f) { ClearAura(); return; }
            AuraElement = aura;
            AuraSourceId = ownerId ?? "";
            AuraRemainingTime = duration;
        }

        internal void ClearAura()
        {
            AuraElement = ElementType.None;
            AuraRemainingTime = 0f;
            AuraSourceId = "";
        }

        internal void TickTimers(float deltaTime)
        {
            if (AuraElement != ElementType.None)
            {
                AuraRemainingTime = Mathf.Max(0f, AuraRemainingTime - deltaTime);
                if (AuraRemainingTime <= 0f) ClearAura();
            }
            if (ShieldAmount > 0f)
            {
                ShieldRemainingTime = Mathf.Max(0f, ShieldRemainingTime - deltaTime);
                if (ShieldRemainingTime <= 0f) ClearShield();
            }
        }

        internal float AbsorbAndRecord(float damage)
        {
            float absorbed = Mathf.Min(ShieldAmount, damage);
            ShieldAmount -= absorbed;
            TotalShieldAbsorbed += absorbed;
            DamageTaken += damage - absorbed;
            if (ShieldAmount <= 0f) ClearShield();
            return absorbed;
        }

        public Vector3 EffectCenter
        {
            get
            {
                var controller = GetComponentInParent<CharacterController>();
                if (controller != null) return controller.bounds.center;
                var collider = GetComponentInChildren<Collider>();
                return collider != null ? collider.bounds.center : transform.position + Vector3.up * 0.9f;
            }
        }

        internal static void ValidateNonnegative(float value, string parameter)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameter);
        }

        private void ClearShield()
        {
            ShieldAmount = ShieldRemainingTime = 0f;
            ShieldElement = ElementType.None;
        }

        private void OnEnable() => Registered.Add(this);
        private void OnDisable() => Registered.Remove(this);
        private void OnDestroy() => Registered.Remove(this);
    }
}
