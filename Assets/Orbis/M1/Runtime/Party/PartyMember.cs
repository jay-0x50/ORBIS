using System;

namespace Orbis.M1
{
    /// <summary>Persistent member state is held by Actor, independently of the shared prototype avatar.</summary>
    public sealed class PartyMember
    {
        private float? basicAttackDamage;
        public string Name { get; }
        public ElementalActor Actor { get; }
        public bool IsPermanent { get; }
        public float? BasicAttackDamage
        {
            get => basicAttackDamage;
            set
            {
                if (value.HasValue && (float.IsNaN(value.Value) || float.IsInfinity(value.Value) || value.Value < 0f))
                    throw new ArgumentOutOfRangeException(nameof(value));
                basicAttackDamage = value;
            }
        }

        public PartyMember(string name, ElementalActor actor, bool isPermanent = false)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A party member needs a name.", nameof(name));
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            Name = name;
            Actor = actor;
            IsPermanent = isPermanent;
        }
    }
}
