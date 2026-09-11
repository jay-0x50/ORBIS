using System;
using System.Collections.Generic;
using Orbis.M1;

namespace Orbis.M4
{
    public sealed class M4RegionProfile
    {
        public M4RegionId Id { get; }
        public string DisplayName { get; }
        public ElementType Element { get; }
        public ElementType Weakness { get; }

        internal M4RegionProfile(M4RegionId id, string displayName, ElementType element, ElementType weakness)
        { Id = id; DisplayName = displayName; Element = element; Weakness = weakness; }
    }

    public static class M4RegionCatalog
    {
        // Spec01 supplies region identities and elements. Boss weaknesses are unspecified:
        // these provisional pairings give every regional encounter a clear party-switch target.
        private static readonly IReadOnlyList<M4RegionProfile> profiles = Array.AsReadOnly(new[]
        {
            new M4RegionProfile(M4RegionId.Agnia, "아그니아 고원", ElementType.Fire, ElementType.Water),
            new M4RegionProfile(M4RegionId.Teluna, "텔루나 군도", ElementType.Water, ElementType.Lightning),
            new M4RegionProfile(M4RegionId.Zephyr, "자피르 초원", ElementType.Wind, ElementType.Rock),
            new M4RegionProfile(M4RegionId.Granite, "그라니테 산맥", ElementType.Rock, ElementType.Fire),
            new M4RegionProfile(M4RegionId.Voltheim, "볼트하임", ElementType.Lightning, ElementType.Water)
        });

        public static IReadOnlyList<M4RegionProfile> All => profiles;
        public static M4RegionProfile Get(M4RegionId id)
        {
            foreach (var profile in profiles) if (profile.Id == id) return profile;
            throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown ORBIS region.");
        }
    }
}
