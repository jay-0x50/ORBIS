using System;

namespace Orbis.M15
{
    public readonly struct GachaOdds
    {
        public double Three { get; }
        public double Four { get; }
        public double Five { get; }
        public GachaOdds(double three, double four, double five) { Three = three; Four = four; Five = five; }
    }

    public readonly struct PullResult
    {
        public string BannerId { get; }
        public CharacterRarity Rarity { get; }
        public CharacterDefinition Character { get; }
        public bool IsNew { get; }
        public int OwnedCopies { get; }
        public PullResult(string bannerId, CharacterRarity rarity, CharacterDefinition character, bool isNew, int ownedCopies)
        {
            BannerId = bannerId; Rarity = rarity; Character = character; IsNew = isNew; OwnedCopies = ownedCopies;
        }
    }

    /// <summary>Pure draw decisions over authored Unity data. The caller owns ticket debit, revision and atomic persistence.</summary>
    public static class GachaEngine
    {
        public static GachaOdds NextOdds(GachaBanner banner, BannerPityData pity)
        {
            if (banner == null) throw new ArgumentNullException(nameof(banner));
            banner.Validate();
            ValidatePity(banner, pity);
            return CalculateOdds(banner.Rules, pity);
        }

        public static PullResult Draw(GachaBanner banner, EconomySaveData draft, Func<double> rng)
        {
            if (banner == null) throw new ArgumentNullException(nameof(banner));
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            banner.Validate();
            if (!draft.Validate(out string error)) throw new InvalidOperationException(error);
            BannerPityData pity = null;
            foreach (BannerPityData entry in draft.pities) if (entry.bannerId == banner.Id) { pity = entry; break; }
            GachaOdds odds = CalculateOdds(banner.Rules, pity);
            // Always consume the rarity draw, including hard pity. Separate draws are used for featured selection and pool index.
            double rarityRoll = Sample(rng);
            CharacterRarity rarity = rarityRoll < odds.Five ? CharacterRarity.Five
                : rarityRoll < odds.Five + odds.Four ? CharacterRarity.Four : CharacterRarity.Three;
            bool guaranteed = pity != null && pity.guaranteedFeatured;
            CharacterDefinition character;
            if (rarity == CharacterRarity.Five && banner.Type == BannerType.Limited)
            {
                bool featured = guaranteed || Sample(rng) < banner.Rules.FeaturedChance;
                character = featured ? banner.Featured : Choose(banner.FiveStars, rng, banner.Featured.Id);
                guaranteed = !featured;
            }
            else
            {
                var pool = rarity == CharacterRarity.Five ? banner.FiveStars
                    : rarity == CharacterRarity.Four ? banner.FourStars : banner.ThreeStars;
                character = Choose(pool, rng, null);
                if (banner.Type == BannerType.Standard) guaranteed = false;
            }
            OwnedCharacterData owned = null;
            foreach (OwnedCharacterData entry in draft.owned) if (entry.id == character.Id) { owned = entry; break; }
            int copies = checked((owned == null ? 0 : owned.copies) + 1);
            int fiveMisses = rarity == CharacterRarity.Five ? 0 : checked((pity == null ? 0 : pity.fiveStarMisses) + 1);
            int fourMisses = rarity >= CharacterRarity.Four ? 0 : checked((pity == null ? 0 : pity.fourStarMisses) + 1);
            // All validation, RNG and allocations finish before committing any draft field. An invalid RNG cannot create an empty pity entry.
            OwnedCharacterData[] ownedEntries = draft.owned;
            BannerPityData[] pityEntries = draft.pities;
            if (owned == null)
            {
                owned = new OwnedCharacterData { id = character.Id };
                Array.Resize(ref ownedEntries, checked(ownedEntries.Length + 1));
                ownedEntries[ownedEntries.Length - 1] = owned;
            }
            if (pity == null)
            {
                pity = new BannerPityData { bannerId = banner.Id };
                Array.Resize(ref pityEntries, checked(pityEntries.Length + 1));
                pityEntries[pityEntries.Length - 1] = pity;
            }
            var result = new PullResult(banner.Id, rarity, character, copies == 1, copies);
            owned.copies = copies;
            pity.fiveStarMisses = fiveMisses; pity.fourStarMisses = fourMisses; pity.guaranteedFeatured = guaranteed;
            draft.owned = ownedEntries; draft.pities = pityEntries;
            return result;
        }

        private static void ValidatePity(GachaBanner banner, BannerPityData pity)
        {
            if (pity != null && (pity.bannerId != banner.Id || pity.fiveStarMisses < 0 || pity.fourStarMisses < 0 ||
                pity.fiveStarMisses == int.MaxValue || pity.fourStarMisses == int.MaxValue))
                throw new InvalidOperationException("Pity must belong to this banner and have valid counters.");
        }
        private static GachaOdds CalculateOdds(GachaRules rules, BannerPityData pity)
        {
            long fiveAttempt = (long)(pity == null ? 0 : pity.fiveStarMisses) + 1L;
            long fourAttempt = (long)(pity == null ? 0 : pity.fourStarMisses) + 1L;
            if (fiveAttempt >= rules.HardPity) return new GachaOdds(0d, 0d, 1d);
            double five = rules.FiveStarProbability;
            if (fiveAttempt >= rules.SoftPityStart)
                five = Math.Min(1d, five + (fiveAttempt - rules.SoftPityStart + 1L) * rules.SoftPityStep);
            // 기본값: 4성 확률을 유지하고 먼저 3성에서 차감한다. 4성 천장은 5성이 아닌 모든 결과를 4성으로 만든다.
            double four = fourAttempt >= rules.FourStarHardPity ? 1d - five : Math.Min(rules.FourStarProbability, 1d - five);
            return new GachaOdds(Math.Max(0d, 1d - five - four), four, five);
        }
        private static double Sample(Func<double> rng)
        {
            double value = rng();
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d || value >= 1d)
                throw new InvalidOperationException("The RNG must return a finite value in [0, 1).");
            return value;
        }
        private static CharacterDefinition Choose(CharacterDefinition[] pool, Func<double> rng, string excludedId)
        {
            int count = 0;
            foreach (CharacterDefinition character in pool) if (character.Id != excludedId) count++;
            if (count == 0) throw new InvalidOperationException("There is no eligible character in the pool.");
            int index = (int)(Sample(rng) * count);
            foreach (CharacterDefinition character in pool)
            {
                if (character.Id == excludedId) continue;
                if (index-- == 0) return character;
            }
            throw new InvalidOperationException("Character selection was out of range.");
        }
    }
}


