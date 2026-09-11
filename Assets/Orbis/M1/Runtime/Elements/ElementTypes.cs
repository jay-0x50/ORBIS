using System;
using UnityEngine;

namespace Orbis.M1
{
    public enum ElementType { None, Fire, Water, Wind, Rock, Lightning }
    public enum ReactionType { None, Vaporize, ElectroCharged, Overload, Swirl, Crystallize }
    public enum ActorTeam { Player, Enemy }

    public readonly struct ElementalAura
    {
        public ElementType Element { get; }
        public float RemainingTime { get; }
        public string SourceId { get; }
        public ElementalAura(ElementType element, float remainingTime, string sourceId)
        { Element = element; RemainingTime = remainingTime; SourceId = sourceId; }
    }

    public readonly struct ElementalShield
    {
        public ElementType Element { get; }
        public float Amount { get; }
        public float RemainingTime { get; }
        public ElementalShield(ElementType element, float amount, float remainingTime)
        { Element = element; Amount = amount; RemainingTime = remainingTime; }
    }

    public readonly struct ElementApplicationEvent
    {
        public ElementalActor Target { get; }
        public ElementalActor Source { get; }
        public ElementType IncomingElement { get; }
        public float BaseDamage { get; }
        public bool IsPropagation { get; }
        public ElementApplicationEvent(ElementalActor target, ElementalActor source,
            ElementType incomingElement, float baseDamage, bool isPropagation = false)
        { Target = target; Source = source; IncomingElement = incomingElement; BaseDamage = baseDamage; IsPropagation = isPropagation; }
    }

    public readonly struct ElementReactionEvent
    {
        public ElementalActor Target { get; }
        public ElementalActor Source { get; }
        public ReactionType Reaction { get; }
        public ElementType ExistingElement { get; }
        public ElementType IncomingElement { get; }
        public ElementType AffectedElement { get; }
        public float DamageMultiplier { get; }
        public ElementReactionEvent(ElementalActor target, ElementalActor source, ReactionType reaction,
            ElementType existingElement, ElementType incomingElement, ElementType affectedElement, float damageMultiplier)
        {
            Target = target; Source = source; Reaction = reaction; ExistingElement = existingElement;
            IncomingElement = incomingElement; AffectedElement = affectedElement; DamageMultiplier = damageMultiplier;
        }
    }

    /// <summary>Presentation-only notification of a validated one-hop electrical damage link.</summary>
    public readonly struct ElementChainEvent
    {
        public ElementalActor Origin { get; }
        public ElementalActor Target { get; }
        public ElementalActor Source { get; }
        public ElementChainEvent(ElementalActor origin, ElementalActor target, ElementalActor source)
        { Origin = origin; Target = target; Source = source; }
    }
    public readonly struct ElementDamageEvent
    {
        public ElementalActor Target { get; }
        public ElementalActor Source { get; }
        public ElementType Element { get; }
        public float RequestedDamage { get; }
        public float AppliedDamage { get; }
        public float AbsorbedDamage { get; }
        public ReactionType Reaction { get; }
        public ElementDamageEvent(ElementalActor target, ElementalActor source, ElementType element,
            float requestedDamage, float appliedDamage, float absorbedDamage, ReactionType reaction)
        {
            Target = target; Source = source; Element = element; RequestedDamage = requestedDamage;
            AppliedDamage = appliedDamage; AbsorbedDamage = absorbedDamage; Reaction = reaction;
        }
    }

    /// <summary>순서에 독립적인 5원소 반응표. 증발의 배율만 incoming 원소에 따라 달라진다.</summary>
    public static class ElementReactionResolver
    {
        public static ReactionType Resolve(ElementType existing, ElementType incoming)
        {
            if (existing == ElementType.None || incoming == ElementType.None || existing == incoming)
                return ReactionType.None;
            // 사용자 확정: 암+풍은 결정화 우선. 암과 다른 네 원소 모두 결정화한다.
            if (existing == ElementType.Rock || incoming == ElementType.Rock)
                return ReactionType.Crystallize;
            if (existing == ElementType.Wind || incoming == ElementType.Wind)
                return ReactionType.Swirl;
            if (IsPair(existing, incoming, ElementType.Fire, ElementType.Water))
                return ReactionType.Vaporize;
            if (IsPair(existing, incoming, ElementType.Water, ElementType.Lightning))
                return ReactionType.ElectroCharged;
            if (IsPair(existing, incoming, ElementType.Fire, ElementType.Lightning))
                return ReactionType.Overload;
            return ReactionType.None;
        }

        public static float VaporizeMultiplier(ElementType incoming) => incoming == ElementType.Water ? 2f : 1.5f;

        private static bool IsPair(ElementType a, ElementType b, ElementType first, ElementType second)
            => (a == first && b == second) || (a == second && b == first);
    }
}
