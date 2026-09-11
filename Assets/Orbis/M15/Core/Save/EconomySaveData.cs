using System;
using System.Collections.Generic;

namespace Orbis.M15
{
    [Serializable]
    public sealed class OwnedCharacterData
    {
        public string id;
        public int copies;
        public OwnedCharacterData Clone() => new OwnedCharacterData { id = id, copies = copies };
    }

    [Serializable]
    public sealed class BannerPityData
    {
        public string bannerId;
        public int fiveStarMisses;
        public int fourStarMisses;
        public bool guaranteedFeatured;
        public BannerPityData Clone() => new BannerPityData
        {
            bannerId = bannerId, fiveStarMisses = fiveStarMisses,
            fourStarMisses = fourStarMisses, guaranteedFeatured = guaranteedFeatured
        };
    }

    [Serializable]
    public sealed class EconomySaveData
    {
        public int[] balances = new int[6];
        public OwnedCharacterData[] owned = Array.Empty<OwnedCharacterData>();
        public BannerPityData[] pities = Array.Empty<BannerPityData>();
        public bool startersGranted;
        public long revision;

        public EconomySaveData Clone()
        {
            var copy = new EconomySaveData
            {
                balances = balances == null ? null : (int[])balances.Clone(),
                owned = owned == null ? null : new OwnedCharacterData[owned.Length],
                pities = pities == null ? null : new BannerPityData[pities.Length],
                startersGranted = startersGranted, revision = revision
            };
            if (owned != null) for (int i = 0; i < owned.Length; i++) copy.owned[i] = owned[i]?.Clone();
            if (pities != null) for (int i = 0; i < pities.Length; i++) copy.pities[i] = pities[i]?.Clone();
            return copy;
        }

        public bool Validate(out string error)
        {
            if (balances == null || balances.Length != 6) return Invalid("Exactly six currency balances are required.", out error);
            foreach (int balance in balances) if (balance < 0) return Invalid("Currency cannot be negative.", out error);
            if (owned == null || pities == null) return Invalid("Owned characters and pity arrays must not be null.", out error);
            if (revision < 0) return Invalid("Save revision cannot be negative.", out error);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (OwnedCharacterData entry in owned)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id) || entry.copies < 1)
                    return Invalid("Owned character IDs must be unique and copies positive.", out error);
            }
            ids.Clear();
            foreach (BannerPityData entry in pities)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.bannerId) || !ids.Add(entry.bannerId) ||
                    entry.fiveStarMisses < 0 || entry.fourStarMisses < 0 ||
                    entry.fiveStarMisses == int.MaxValue || entry.fourStarMisses == int.MaxValue)
                    return Invalid("Pity IDs must be unique and counters nonnegative with increment capacity.", out error);
            }
            // Unknown IDs are intentionally retained: catalog changes must not erase owned characters or historical pity.
            error = null;
            return true;
        }
        private static bool Invalid(string message, out string error) { error = message; return false; }

        public int GetBalance(CurrencyType currency)
        {
            if (!Enum.IsDefined(typeof(CurrencyType), currency)) throw new ArgumentOutOfRangeException(nameof(currency));
            if (balances == null || balances.Length != 6) throw new InvalidOperationException("Invalid currency balance array.");
            return balances[(int)currency];
        }
        public int CopiesOf(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A character ID is required.", nameof(id));
            if (owned == null) throw new InvalidOperationException("Missing owned-character array.");
            foreach (OwnedCharacterData entry in owned)
            {
                if (entry == null) throw new InvalidOperationException("Missing owned-character entry.");
                if (entry.id == id) return entry.copies;
            }
            return 0;
        }
        /// <summary>Returns or inserts a mutable pity entry. Call this on a draft/clone, not a live read-only view.</summary>
        public BannerPityData GetPity(string bannerId)
        {
            if (string.IsNullOrWhiteSpace(bannerId)) throw new ArgumentException("A banner ID is required.", nameof(bannerId));
            if (pities == null) throw new InvalidOperationException("Missing pity array.");
            foreach (BannerPityData entry in pities)
            {
                if (entry == null) throw new InvalidOperationException("Missing pity entry.");
                if (entry.bannerId == bannerId) return entry;
            }
            var pity = new BannerPityData { bannerId = bannerId };
            Array.Resize(ref pities, checked(pities.Length + 1));
            pities[pities.Length - 1] = pity;
            return pity;
        }
    }
}

