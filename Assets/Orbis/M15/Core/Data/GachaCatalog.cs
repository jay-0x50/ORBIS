using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis.M15
{
    [CreateAssetMenu(menuName = "Orbis/M15/Gacha Catalog")]
    public sealed class GachaCatalog : ScriptableObject
    {
        public CharacterDefinition[] Characters = Array.Empty<CharacterDefinition>();
        public GachaBanner[] Banners = Array.Empty<GachaBanner>();
        public GachaRules Rules;

        public void Validate()
        {
            if (Rules == null || Characters == null || Characters.Length == 0 || Banners == null || Banners.Length == 0)
                throw new InvalidOperationException("A catalog requires rules, characters and banners.");
            Rules.Validate();
            var characters = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
            foreach (CharacterDefinition character in Characters)
            {
                if (character == null) throw new InvalidOperationException("The catalog contains a missing character.");
                character.Validate();
                if (characters.ContainsKey(character.Id)) throw new InvalidOperationException("Duplicate catalog character ID: " + character.Id);
                characters.Add(character.Id, character);
            }
            var banners = new HashSet<string>(StringComparer.Ordinal);
            foreach (GachaBanner banner in Banners)
            {
                if (banner == null) throw new InvalidOperationException("The catalog contains a missing banner.");
                banner.Validate();
                // M1.5 uses one shared rule asset so displayed odds/exchange costs agree with the transaction service.
                if (banner.Rules != Rules) throw new InvalidOperationException("Every banner must reference the catalog shared rules asset: " + banner.Id);
                if (!banners.Add(banner.Id)) throw new InvalidOperationException("Duplicate catalog banner ID: " + banner.Id);
                foreach (var pool in new[] { banner.ThreeStars, banner.FourStars, banner.FiveStars })
                    foreach (CharacterDefinition character in pool) RequireCanonical(character, characters);
                if (banner.Featured != null) RequireCanonical(banner.Featured, characters);
            }
        }
        private static void RequireCanonical(CharacterDefinition character, Dictionary<string, CharacterDefinition> definitions)
        {
            if (!definitions.TryGetValue(character.Id, out CharacterDefinition canonical) || canonical != character)
                throw new InvalidOperationException("A banner references a character outside the canonical catalog: " + character.Id);
        }
    }
}

