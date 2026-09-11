using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis.M1
{
    /// <summary>씬 싱글턴과 인스턴스 이벤트 버스로 원소 부착→반응→효과를 전달한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ElementalReactionManager : MonoBehaviour
    {
        private sealed class ElectroChargedStatus
        {
            public ElementalActor Target;
            public ElementalActor Source;
            public float Damage;
            public float UntilNextTick = 1f;
            public int TicksRemaining = 3;
        }

        // 기획서 미정 M1 기본값: 감전 1초 간격 3회, 원피해 40%, 3m 안의 적에게 1홉 20% 연쇄.
        private const float ChainRadius = 3f;
        // 과부하: 반경 2.5m, 원피해 80% 추가, 최대 1.5m 수평 넉백. 확산: 3m/50% 피해/4초 부착.
        private const float OverloadRadius = 2.5f;
        private const float KnockbackDistance = 1.5f;
        private const float SwirlRadius = 3f;
        private const int WorldMask = 1 << 8;
        private readonly List<ElectroChargedStatus> electroCharged = new List<ElectroChargedStatus>();

        public static ElementalReactionManager Instance { get; private set; }
        public bool AutoTick { get; set; } = true;
        public ReactionType LastReaction { get; private set; }
        public int PendingElectroChargedCount => electroCharged.Count;
        public event Action<ElementApplicationEvent> Applied;
        public event Action<ElementReactionEvent> Reacted;
        public event Action<ElementDamageEvent> DamageApplied;
        public event Action<ElementChainEvent> ChainLinked;

        private void Awake() => ClaimInstance();
        private void OnEnable() => ClaimInstance();

        private void ClaimInstance()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("An elemental reaction manager already exists; disabling the duplicate component.", this);
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            if (AutoTick) Tick(Time.deltaTime);
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public ReactionType Apply(ElementalActor target, ElementType incoming, ElementalActor source,
            float baseDamage = 10f, float duration = 4f)
        {
            ElementalActor.ValidateNonnegative(baseDamage, nameof(baseDamage));
            ElementalActor.ValidateNonnegative(duration, nameof(duration));
            if (!CanApply(target, source) || incoming == ElementType.None || duration <= 0f)
                return ReactionType.None;

            Applied?.Invoke(new ElementApplicationEvent(target, source, incoming, baseDamage));
            ElementType existing = target.AuraElement;
            ReactionType reaction = ElementReactionResolver.Resolve(existing, incoming);
            LastReaction = reaction;
            float multiplier = reaction == ReactionType.Vaporize
                ? ElementReactionResolver.VaporizeMultiplier(incoming) : 1f;
            ElementType affected = reaction == ReactionType.Crystallize
                ? (existing == ElementType.Rock ? incoming : existing)
                : reaction == ReactionType.Swirl
                    ? (existing == ElementType.Wind ? incoming : existing) : incoming;

            if (reaction == ReactionType.None)
            {
                // 동일 원소는 시간/소유자를 새 부착으로 갱신한다.
                // 미정 조합이 향후 생기면 서로 다른 비반응 원소는 incoming으로 교체한다.
                target.SetAura(incoming, source.SourceId, duration);
            }
            else
            {
                // 효과 이벤트 전에 두 원소를 모두 소비하여 기존 부착으로 재반응하지 않는다.
                target.ClearAura();
                Reacted?.Invoke(new ElementReactionEvent(target, source, reaction, existing,
                    incoming, affected, multiplier));
            }

            DealDamage(target, source, incoming, baseDamage * multiplier, reaction);
            switch (reaction)
            {
                case ReactionType.ElectroCharged:
                    StartElectroCharged(target, source, baseDamage * 0.4f);
                    break;
                case ReactionType.Overload:
                    ApplyOverload(target, source, baseDamage);
                    break;
                case ReactionType.Swirl:
                    ApplySwirl(target, source, affected, baseDamage, duration);
                    break;
                case ReactionType.Crystallize:
                    // 사용자 확정: 암+풍의 실드는 풍 속성. 획득물 없이 시전자 actor에 즉시 장착한다.
                    source.GrantShield(affected);
                    break;
            }
            return reaction;
        }

        public void Tick(float deltaTime)
        {
            ElementalActor.ValidateNonnegative(deltaTime, nameof(deltaTime));
            float remaining = deltaTime;
            while (remaining > 0f)
            {
                electroCharged.RemoveAll(status => status.Target == null || status.Source == null ||
                    !status.Target.isActiveAndEnabled || !status.Source.isActiveAndEnabled);
                float step = remaining;
                foreach (ElectroChargedStatus status in electroCharged)
                    step = Mathf.Min(step, Mathf.Max(0f, status.UntilNextTick));

                // 큰 deltaTime도 DoT 예정 시점으로 나눠 실드 만료 전/후의 흡수를 정확히 계산한다.
                foreach (ElementalActor actor in ElementalActor.Snapshot()) actor.TickTimers(step);
                foreach (ElectroChargedStatus status in electroCharged) status.UntilNextTick -= step;
                remaining = Mathf.Max(0f, remaining - step);

                var due = new List<ElectroChargedStatus>();
                foreach (ElectroChargedStatus status in electroCharged)
                    if (status.UntilNextTick <= 0.00001f) due.Add(status);
                foreach (ElectroChargedStatus status in due)
                {
                    if (!electroCharged.Contains(status)) continue;
                    status.TicksRemaining--;
                    status.UntilNextTick += 1f;
                    if (status.TicksRemaining == 0) electroCharged.Remove(status);
                    ApplyElectroChargedTick(status);
                }
            }
        }

        /// <summary>전환 시점에 해당 소스가 이미 부착한 살아 있는 원소만 정확히 seconds초로 유지한다.</summary>
        public int RetainSourceAttachments(string ownerId, float seconds = 4f)
        {
            ElementalActor.ValidateNonnegative(seconds, nameof(seconds));
            if (string.IsNullOrEmpty(ownerId)) return 0;
            int retained = 0;
            foreach (ElementalActor actor in ElementalActor.Snapshot())
            {
                if (actor.AuraElement == ElementType.None || actor.AuraRemainingTime <= 0f ||
                    !string.Equals(actor.AuraSourceId, ownerId, StringComparison.Ordinal)) continue;
                actor.SetAura(actor.AuraElement, actor.AuraSourceId, seconds);
                retained++;
            }
            return retained;
        }

        /// <summary>Traversal damage without elemental attachment; uses the existing damage/shield accounting.</summary>
        public void ApplyEnvironmentalDamage(ElementalActor target, float damage)
        {
            ElementalActor.ValidateNonnegative(damage, nameof(damage));
            DealDamage(target, null, ElementType.None, damage, ReactionType.None);
        }

        /// <summary>Validated direct physical hit: shields and health apply, without elemental attachment or reaction.</summary>
        public bool DealPhysicalDamage(ElementalActor target, ElementalActor source, float damage)
        {
            ElementalActor.ValidateNonnegative(damage, nameof(damage));
            if (!CanApply(target, source)) return false;
            LastReaction = ReactionType.None;
            DealDamage(target, source, ElementType.None, damage, ReactionType.None);
            return true;
        }

        public void ResetTarget(ElementalActor target)
        {
            if (target == null) return;
            electroCharged.RemoveAll(status => status.Target == target);
            target.ResetState();
        }

        public void ResetState()
        {
            electroCharged.Clear();
            LastReaction = ReactionType.None;
            foreach (ElementalActor actor in ElementalActor.Snapshot()) actor.ResetState();
        }

        private static bool CanApply(ElementalActor target, ElementalActor source)
            => target != null && source != null && target != source && target.isActiveAndEnabled &&
               source.isActiveAndEnabled && target.IsOnField && source.IsOnField &&
               target.Team != source.Team && !string.IsNullOrEmpty(source.SourceId);

        private void DealDamage(ElementalActor target, ElementalActor source, ElementType element,
            float damage, ReactionType reaction)
        {
            if (target == null || !target.isActiveAndEnabled || !target.IsOnField) return;
            float scale = target.IncomingDamageScale != null ? target.IncomingDamageScale(element) : 1f;
            ElementalActor.ValidateNonnegative(scale, nameof(scale));
            damage *= scale;
            float absorbed = target.AbsorbAndRecord(damage);
            DamageApplied?.Invoke(new ElementDamageEvent(target, source, element, damage,
                damage - absorbed, absorbed, reaction));
        }

        private void StartElectroCharged(ElementalActor target, ElementalActor source, float damage)
        {
            // 동일 타깃에 감전을 재발동하면 새 3회로 갱신한다. 중첩 DoT 폭증은 M1에 추가하지 않는다.
            electroCharged.RemoveAll(status => status.Target == target);
            electroCharged.Add(new ElectroChargedStatus { Target = target, Source = source, Damage = damage });
        }

        private void ApplyElectroChargedTick(ElectroChargedStatus status)
        {
            if (status.Target == null || status.Source == null || !status.Target.IsOnField) return;
            // 오프필드로 전환한 시전자의 기존 감전은 계속 흐른다. 대상이 오프필드면 횟수만 소모한다.
            DealDamage(status.Target, status.Source, ElementType.Lightning, status.Damage, ReactionType.ElectroCharged);
            foreach (ElementalActor neighbour in Neighbours(status.Target, status.Source, ChainRadius, false))
            {
                // 1홉만 피해를 전달한다. Apply를 호출하지 않아 추가 부착/재귀 감전을 만들지 않는다.
                DealDamage(neighbour, status.Source, ElementType.Lightning, status.Damage * 0.5f, ReactionType.ElectroCharged);
                ChainLinked?.Invoke(new ElementChainEvent(status.Target, neighbour, status.Source));
            }
        }

        private void ApplyOverload(ElementalActor target, ElementalActor source, float baseDamage)
        {
            Vector3 center = target.EffectCenter;
            Vector3 fallback = Vector3.ProjectOnPlane(target.transform.position - source.transform.position, Vector3.up).normalized;
            if (fallback.sqrMagnitude < 0.001f) fallback = Vector3.forward;
            // 원 타깃도 목록에 한 번만 포함한다. 기본 피해와 별개인 과부하 추가 피해를 중복 적용하지 않는다.
            foreach (ElementalActor actor in Neighbours(target, source, OverloadRadius, true))
            {
                DealDamage(actor, source, ElementType.Fire, baseDamage * 0.8f, ReactionType.Overload);
                Vector3 direction = Vector3.ProjectOnPlane(actor.EffectCenter - center, Vector3.up).normalized;
                if (direction.sqrMagnitude < 0.001f) direction = fallback;
                ApplyKnockback(actor, direction * KnockbackDistance);
            }
            // Transform으로 이동한 정적 더미를 같은 프레임의 다음 물리 쿼리에서도 새 위치로 검사한다.
            Physics.SyncTransforms();
        }

        private void ApplySwirl(ElementalActor target, ElementalActor source, ElementType spread,
            float baseDamage, float duration)
        {
            foreach (ElementalActor neighbour in Neighbours(target, source, SwirlRadius, false))
            {
                // 원소 전파의 소유자는 확산 시전자다. 기존 원소는 전파 원소로 교체하고 반응을 재귀 호출하지 않는다.
                neighbour.SetAura(spread, source.SourceId, duration);
                Applied?.Invoke(new ElementApplicationEvent(neighbour, source, spread, baseDamage * 0.5f, true));
                DealDamage(neighbour, source, spread, baseDamage * 0.5f, ReactionType.Swirl);
            }
        }

        private static List<ElementalActor> Neighbours(ElementalActor center, ElementalActor source,
            float radius, bool includeCenter)
        {
            var result = new List<ElementalActor>();
            Vector3 position = center.EffectCenter;
            foreach (ElementalActor actor in ElementalActor.Snapshot())
            {
                if ((!includeCenter && actor == center) || actor == source || !actor.IsOnField || actor.Team != center.Team ||
                    actor.Team == source.Team || (actor.EffectCenter - position).sqrMagnitude > radius * radius) continue;
                // 지형 너머로 전파하지 않는다. 플레이어/더미는 제외하고 World 레이어만 차폐물로 검사한다.
                if (Physics.Linecast(position, actor.EffectCenter, WorldMask, QueryTriggerInteraction.Ignore)) continue;
                result.Add(actor);
            }
            return result;
        }

        private static void ApplyKnockback(ElementalActor actor, Vector3 displacement)
        {
            var controller = actor.GetComponentInParent<CharacterController>();
            if (controller != null && controller.enabled)
            {
                Vector3 previous = controller.transform.position;
                controller.Move(displacement);
                actor.LastKnockback = controller.transform.position - previous;
                return;
            }

            Collider body = actor.GetComponentInChildren<Collider>();
            Vector3 center = actor.EffectCenter;
            float radius = body != null ? Mathf.Max(0.05f, Mathf.Min(body.bounds.extents.x, body.bounds.extents.z)) : 0.3f;
            float halfHeight = body != null ? Mathf.Max(radius, body.bounds.extents.y) : 0.9f;
            Vector3 upper = center + Vector3.up * (halfHeight - radius);
            Vector3 lower = center - Vector3.up * (halfHeight - radius);
            float distance = displacement.magnitude;
            Vector3 direction = displacement.normalized;
            if (Physics.CapsuleCast(upper, lower, radius, direction, out RaycastHit hit, distance,
                    WorldMask, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0f, hit.distance - 0.03f); // M0와 같은 3cm 충돌 여유.
            Vector3 actual = direction * distance;
            actor.transform.position += actual;
            actor.LastKnockback = actual;
        }
    }
}

