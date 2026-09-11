using System;
using System.Linq;
using Orbis.M4;
using UnityEngine;

namespace Orbis.M15
{
    /// <summary>
    /// Main-thread singleton. A pull saves tickets, ownership and pity as one profile transaction.
    /// Awake never touches disk: tests can inject a temporary profile before any economy API call.
    /// </summary>
    public sealed class CurrencyManager : MonoBehaviour
    {
        static CurrencyManager instance;
        M4ProgressService profile;
        GachaCatalog catalog;
        Func<double> random;
        bool busy;

        public static CurrencyManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<CurrencyManager>();
                    if (instance == null) instance = new GameObject("CurrencyManager").AddComponent<CurrencyManager>();
                }
                return instance;
            }
        }

        public GachaCatalog Catalog { get { EnsureInitialized(); return catalog; } }
        public EconomySaveData Snapshot { get { EnsureInitialized(); return profile.GetEconomySnapshot(); } }
        public string LastError { get; private set; }
        public string SavePath { get { EnsureInitialized(); return profile.SavePath; } }
        public bool IsReadOnly { get { EnsureInitialized(); return profile.IsReadOnly; } }
        public bool IsBusy => busy;
        public event Action Changed;
        public event Action<PullResult> PullCommitted;
        public event Action<CharacterRarity> PresentationRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() => instance = null;

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (profile != null) profile.Changed -= OnProfileChanged;
            if (instance == this) instance = null;
        }

        public void Initialize(M4ProgressService progress, GachaCatalog definitions, Func<double> rng = null)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (profile != null)
            {
                if (profile != progress || catalog != definitions)
                    throw new InvalidOperationException("An initialized economy cannot switch profiles or catalogs.");
                return;
            }
            definitions.Validate();
            profile = progress;
            catalog = definitions;
            // Local prototype RNG; no monetization or server authority is implemented in M1.5.
            var source = new System.Random();
            random = rng ?? source.NextDouble;
            profile.Changed += OnProfileChanged;
            LastError = profile.IsReadOnly ? profile.LoadMessage : profile.LastSaveError;
        }

        void EnsureInitialized()
        {
            if (profile == null) Initialize(M4Session.Progress, Resources.Load<GachaCatalog>("M15/Catalog"));
        }

        public int Balance(CurrencyType type) => Snapshot.GetBalance(type);

        public bool Add(CurrencyType type, int amount) => Mutate(draft =>
        {
            if (!ValidType(type) || amount <= 0) return Fail("획득량은 양수여야 합니다.");
            int index = (int)type;
            if (draft.balances[index] > int.MaxValue - amount) return Fail("재화 보유 한도를 초과합니다.");
            draft.balances[index] += amount;
            return true;
        });

        public bool Spend(CurrencyType type, int amount) => Mutate(draft =>
        {
            if (!ValidType(type) || amount <= 0) return Fail("소비량은 양수여야 합니다.");
            int index = (int)type;
            if (draft.balances[index] < amount) return Fail("재화가 부족합니다.");
            draft.balances[index] -= amount;
            return true;
        });

        public bool Exchange(CurrencyType ticketType, int tickets = 1) => Mutate(draft =>
        {
            if ((ticketType != CurrencyType.LimitedPledge && ticketType != CurrencyType.StandardPledge) || tickets <= 0)
                return Fail("교환할 서약서 종류와 양수를 지정하세요.");
            catalog.Rules.Validate();
            long cost = (long)tickets * catalog.Rules.ExchangeCost;
            int destination = (int)ticketType;
            if (cost > draft.balances[(int)CurrencyType.OrbitShard]) return Fail("오르빗 결정이 부족합니다.");
            if (draft.balances[destination] > int.MaxValue - tickets) return Fail("서약서 보유 한도를 초과합니다.");
            draft.balances[(int)CurrencyType.OrbitShard] -= (int)cost;
            draft.balances[destination] += tickets;
            return true;
        });

        /// <summary>Explicit tutorial/debug hook. Free starters are granted once, even if already drawn.</summary>
        public bool GrantStarters() => Mutate(draft =>
        {
            catalog.Validate();
            if (draft.startersGranted) return Fail("스타터 무료 지급을 이미 받았습니다.");
            foreach (CharacterDefinition starter in catalog.Characters.Where(value => value.IsStarter))
            {
                OwnedCharacterData entry = Array.Find(draft.owned, value => value.id == starter.Id);
                if (entry == null)
                    draft.owned = draft.owned.Concat(new[] { new OwnedCharacterData { id = starter.Id, copies = 1 } }).ToArray();
                else
                {
                    if (entry.copies == int.MaxValue) return Fail("동료 보유 수량 한도를 초과합니다.");
                    entry.copies++;
                }
            }
            draft.startersGranted = true;
            return true;
        });

        public bool PullSingle(GachaBanner banner, out PullResult result, bool skipPresentation = false)
        {
            bool success = Pull(banner, 1, out PullResult[] results, skipPresentation);
            result = success ? results[0] : default;
            return success;
        }

        public bool PullTen(GachaBanner banner, out PullResult[] results, bool skipPresentation = false)
            => Pull(banner, 10, out results, skipPresentation);

        bool Pull(GachaBanner banner, int count, out PullResult[] results, bool skipPresentation)
        {
            results = Array.Empty<PullResult>();
            if (!Begin()) return false;
            try
            {
                catalog.Validate();
                if (banner == null || !catalog.Banners.Contains(banner)) return Fail("등록되지 않은 배너입니다.");
                EconomySaveData draft = profile.GetEconomySnapshot();
                CurrencyType payment = banner.Type == BannerType.Limited ? CurrencyType.LimitedPledge : CurrencyType.StandardPledge;
                if (draft.balances[(int)payment] < count) return Fail("서약서가 부족합니다.");
                draft.balances[(int)payment] -= count;
                var pending = new PullResult[count];
                for (int i = 0; i < count; i++) pending[i] = GachaEngine.Draw(banner, draft, random);
                if (!profile.TryCommitEconomy(draft.revision, draft)) return Fail(profile.LastSaveError ?? profile.LoadMessage);
                results = pending;
                foreach (PullResult result in pending)
                {
                    Publish(PullCommitted, result);
                    if (!skipPresentation) Publish(PresentationRequested, result.Rarity);
                }
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException || exception is OverflowException)
            { return Fail(exception.Message); }
            finally { busy = false; }
        }

        bool Mutate(Func<EconomySaveData, bool> mutation)
        {
            if (!Begin()) return false;
            try
            {
                EconomySaveData draft = profile.GetEconomySnapshot();
                if (!mutation(draft)) return false;
                return profile.TryCommitEconomy(draft.revision, draft) || Fail(profile.LastSaveError ?? profile.LoadMessage);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException || exception is OverflowException)
            { return Fail(exception.Message); }
            finally { busy = false; }
        }

        bool Begin()
        {
            if (busy) return false; // Reject reentrant mutations from result/changed listeners.
            EnsureInitialized();
            LastError = null;
            if (profile.IsReadOnly) return Fail(profile.LoadMessage);
            busy = true;
            return true;
        }

        public bool Save() => ProfileOperation(false);
        public bool Reload() => ProfileOperation(true);

        bool ProfileOperation(bool reload)
        {
            if (busy) return false;
            EnsureInitialized();
            busy = true;
            LastError = null;
            try
            {
                bool success = reload ? profile.Reload() : profile.Save();
                return success || Fail(profile.LastSaveError ?? profile.LoadMessage);
            }
            finally { busy = false; }
        }

        bool Fail(string reason) { LastError = reason; return false; }
        static bool ValidType(CurrencyType type) => (int)type >= 0 && (int)type < 6;
        void OnProfileChanged()
        {
            if (Changed == null) return;
            foreach (Action listener in Changed.GetInvocationList())
            {
                try { listener(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        static void Publish<T>(Action<T> listeners, T value)
        {
            if (listeners == null) return;
            foreach (Action<T> listener in listeners.GetInvocationList())
            {
                try { listener(value); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        void OnApplicationPause(bool paused) { if (paused && profile != null && !busy) Save(); }
        void OnApplicationQuit() { if (profile != null && !busy) Save(); }
    }
}

