using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis.M15
{
    [CreateAssetMenu(menuName = "Orbis/M15/Gacha Banner")]
    public sealed class GachaBanner : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public BannerType Type;
        public GachaRules Rules;
        public CharacterDefinition Featured;
        public CharacterDefinition[] ThreeStars = Array.Empty<CharacterDefinition>();
        public CharacterDefinition[] FourStars = Array.Empty<CharacterDefinition>();
        public CharacterDefinition[] FiveStars = Array.Empty<CharacterDefinition>();

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DisplayName) ||
                !Enum.IsDefined(typeof(BannerType), Type) || Rules == null)
                throw new InvalidOperationException("A banner requires an ID, name, valid type and rules.");
            Rules.Validate();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            ValidatePool(ThreeStars, CharacterRarity.Three, ids);
            ValidatePool(FourStars, CharacterRarity.Four, ids);
            ValidatePool(FiveStars, CharacterRarity.Five, ids);
            if (Type == BannerType.Standard)
            {
                if (Featured != null) throw new InvalidOperationException("A standard banner cannot have a featured character.");
                return;
            }
            if (Featured == null) throw new InvalidOperationException("A limited banner requires a featured five-star character.");
            Featured.Validate();
            if (Featured.Rarity != CharacterRarity.Five)
                throw new InvalidOperationException("The featured character must be five-star.");
            bool hasNonFeatured = false;
            foreach (CharacterDefinition character in FiveStars)
            {
                if (character.Id != Featured.Id) hasNonFeatured = true;
                else if (character != Featured) throw new InvalidOperationException("The same featured ID refers to different character definitions.");
            }
            if (!hasNonFeatured) throw new InvalidOperationException("The limited banner needs a non-featured five-star loss pool.");
        }
        private static void ValidatePool(CharacterDefinition[] pool, CharacterRarity rarity, HashSet<string> ids)
        {
            if (pool == null || pool.Length == 0) throw new InvalidOperationException("Every rarity needs a nonempty character pool.");
            foreach (CharacterDefinition character in pool)
            {
                if (character == null) throw new InvalidOperationException("A character pool contains a missing definition.");
                character.Validate();
                if (character.Rarity != rarity || !ids.Add(character.Id))
                    throw new InvalidOperationException("Wrong rarity or duplicate character ID in banner pool: " + character.Id);
            }
        }
    }
}
