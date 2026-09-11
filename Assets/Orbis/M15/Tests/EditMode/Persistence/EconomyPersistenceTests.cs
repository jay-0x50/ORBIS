using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.M1;
using Orbis.M4;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.M15.Tests
{
    /// <summary>Integration through the public profile and currency APIs, using only per-test temporary saves.</summary>
    public sealed class EconomyPersistenceTests
    {
        private string directory;
        private string path;
        private DateTime now;
        private GachaCatalog catalog;
        private readonly List<GameObject> objects = new List<GameObject>();

        [Serializable] private sealed class LegacyQuest { public string id; public int state; }
        // The legacy shape intentionally has no economy field; it also tests a v2 file missing that required section.
        [Serializable] private sealed class LegacySave
        {
            public int version;
            public int resetHourKst;
            public long lastSeenUtcTicks;
            public string dailyPeriod;
            public string weeklyPeriod;
            public int coins;
            public LegacyQuest[] dailyQuests;
            public int[] defeatedRegions;
            public string[] processedEventIds;
        }
        [Serializable] private sealed class SaveEnvelope { public int version; public EconomySaveData economy; }

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "Orbis-M15-Tests-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "m4-progress.json");
            Directory.CreateDirectory(directory);
            now = new DateTime(2026, 9, 11, 3, 0, 0, DateTimeKind.Utc);
            catalog = Resources.Load<GachaCatalog>("M15/Catalog");
            Assert.That(catalog, Is.Not.Null, "Run Orbis/M15 setup before testing the generated catalog.");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in objects)
                if (gameObject != null) Object.DestroyImmediate(gameObject);
            objects.Clear();
            if (directory == null || !Directory.Exists(directory)) return;
            string resolved = Path.GetFullPath(directory);
            Assert.That(Path.GetDirectoryName(resolved),
                Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            Assert.That(Path.GetFileName(resolved), Does.StartWith("Orbis-M15-Tests-"));
            Directory.Delete(resolved, true);
        }

        private M4ProgressService Open() => new M4ProgressService(path, () => now);
        private CurrencyManager CreateManager(M4ProgressService profile, Func<double> random = null)
        {
            var gameObject = new GameObject("M15 temporary economy test");
            objects.Add(gameObject);
            var manager = gameObject.AddComponent<CurrencyManager>();
            manager.Initialize(profile, catalog, random);
            Assert.That(manager.SavePath, Is.EqualTo(path));
            return manager;
        }
        private GachaBanner Limited => catalog.Banners.Single(b => b.Type == BannerType.Limited);
        private GachaBanner Standard => catalog.Banners.Single(b => b.Type == BannerType.Standard);
        private static string State(EconomySaveData value) => JsonUtility.ToJson(value);
        private SaveEnvelope ReadFile() => JsonUtility.FromJson<SaveEnvelope>(File.ReadAllText(path));
        private static void CompleteAndClaim(M4ProgressService profile, M4DailyQuest quest, string eventId)
        {
            Assert.That(profile.Accept(quest.Definition.Id), Is.True);
            Assert.That(profile.RecordObjective(quest.Definition.Kind, quest.Definition.Region, eventId), Is.True);
            Assert.That(profile.Claim(quest.Definition.Id), Is.True);
        }
        private static void Commit(M4ProgressService profile, Action<EconomySaveData> change)
        {
            EconomySaveData next = profile.GetEconomySnapshot();
            long revision = next.revision;
            change(next);
            Assert.That(profile.TryCommitEconomy(revision, next), Is.True, profile.LastSaveError);
        }

        [Test]
        public void VersionOneMigratesCoinsOnceAndPreservesM4QuestAndBossProgress()
        {
            var profile = Open();
            M4DailyQuest quest = profile.DailyQuests.First(q => q.Definition.Kind != M4ObjectiveKind.FieldBoss);
            CompleteAndClaim(profile, quest, "legacy-objective");
            Assert.That(profile.RecordBossDefeated(M4RegionId.Teluna), Is.True);
            var legacy = JsonUtility.FromJson<LegacySave>(File.ReadAllText(path));
            legacy.version = 1;
            legacy.coins = 1234;
            string legacyJson = JsonUtility.ToJson(legacy, true);
            Assert.That(legacyJson, Does.Not.Contain("\"economy\""));
            File.WriteAllText(path, legacyJson);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");

            var migrated = Open();
            Assert.That(migrated.IsReadOnly, Is.False);
            Assert.That(migrated.Coins, Is.EqualTo(1234));
            Assert.That(migrated.GetEconomySnapshot().GetBalance(CurrencyType.Lumen), Is.EqualTo(1234));
            Assert.That(migrated.GetEconomySnapshot().GetBalance(CurrencyType.OrbitShard), Is.Zero);
            Assert.That(migrated.DailyQuests.Single(q => q.Definition.Id == quest.Definition.Id).State, Is.EqualTo(M4QuestState.Claimed));
            Assert.That(migrated.IsBossDefeated(M4RegionId.Teluna), Is.True);
            Assert.That(migrated.Claim(quest.Definition.Id), Is.False);
            Assert.That(migrated.RecordBossDefeated(M4RegionId.Teluna), Is.False);
            Assert.That(migrated.Save(), Is.True);
            Assert.That(ReadFile().version, Is.EqualTo(M4ProgressService.SaveVersion));
            Assert.That(ReadFile().economy, Is.Not.Null);
            Assert.That(Open().GetEconomySnapshot().GetBalance(CurrencyType.Lumen), Is.EqualTo(1234));
        }

        [Test]
        public void EconomyCommitUsesDeepCopiesAndRejectsStaleOrInvalidDraftsWithoutWriting()
        {
            var profile = Open();
            EconomySaveData original = profile.GetEconomySnapshot();
            EconomySaveData draft = profile.GetEconomySnapshot();
            draft.balances[(int)CurrencyType.OrbitShard] = 160;
            draft.owned = new[] { new OwnedCharacterData { id = catalog.Characters[0].Id, copies = 2 } };
            draft.pities = new[] { new BannerPityData { bannerId = Limited.Id, fiveStarMisses = 12, fourStarMisses = 2, guaranteedFeatured = true } };
            Assert.That(profile.GetEconomySnapshot().GetBalance(CurrencyType.OrbitShard), Is.Zero);
            Assert.That(profile.TryCommitEconomy(original.revision, draft), Is.True);
            long committedRevision = profile.GetEconomySnapshot().revision;
            Assert.That(committedRevision, Is.EqualTo(original.revision + 1));

            draft.balances[(int)CurrencyType.OrbitShard] = 1;
            draft.owned[0].copies = 999;
            draft.pities[0].guaranteedFeatured = false;
            EconomySaveData saved = profile.GetEconomySnapshot();
            Assert.That(saved.GetBalance(CurrencyType.OrbitShard), Is.EqualTo(160));
            Assert.That(saved.CopiesOf(catalog.Characters[0].Id), Is.EqualTo(2));
            Assert.That(saved.GetPity(Limited.Id).guaranteedFeatured, Is.True);
            string before = State(saved);
            string disk = File.ReadAllText(path);
            Assert.That(profile.TryCommitEconomy(original.revision, original), Is.False);
            EconomySaveData invalid = saved.Clone();
            invalid.balances[(int)CurrencyType.Lumen] = -1;
            Assert.That(profile.TryCommitEconomy(committedRevision, invalid), Is.False);
            Assert.That(State(profile.GetEconomySnapshot()), Is.EqualTo(before));
            Assert.That(File.ReadAllText(path), Is.EqualTo(disk));
        }

        [Test]
        public void FinalRevisionCanBeSavedAndReloadedButCannotWrapOrCommitAgain()
        {
            var profile = Open();
            Assert.That(profile.GetEconomySnapshot().revision, Is.Zero);
            string json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("\"revision\": 0"));
            File.WriteAllText(path, json.Replace("\"revision\": 0", "\"revision\": " + (long.MaxValue - 1)));
            profile = Open();
            Assert.That(profile.IsReadOnly, Is.False);
            EconomySaveData last = profile.GetEconomySnapshot();
            last.balances[(int)CurrencyType.Lumen] = 5;
            Assert.That(profile.TryCommitEconomy(long.MaxValue - 1, last), Is.True);
            var loaded = Open();
            Assert.That(loaded.IsReadOnly, Is.False);
            Assert.That(loaded.GetEconomySnapshot().revision, Is.EqualTo(long.MaxValue));
            Assert.That(loaded.Coins, Is.EqualTo(5));
            string before = File.ReadAllText(path);
            Assert.That(loaded.TryCommitEconomy(long.MaxValue, loaded.GetEconomySnapshot()), Is.False);
            Assert.That(loaded.RecordBossDefeated(M4RegionId.Agnia), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
            Assert.That(loaded.GetEconomySnapshot().revision, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void M4RewardsAndCurrencySpendingShareOnePersistedLumenBalance()
        {
            var profile = Open();
            var manager = CreateManager(profile);
            Assert.That(manager.Add(CurrencyType.Lumen, 100), Is.True);
            Assert.That(profile.Coins, Is.EqualTo(100));
            Assert.That(profile.RecordBossDefeated(M4RegionId.Agnia), Is.True);
            Assert.That(manager.Balance(CurrencyType.Lumen), Is.EqualTo(150));
            Assert.That(manager.Spend(CurrencyType.Lumen, 70), Is.True);
            Assert.That(profile.Coins, Is.EqualTo(80));
            CompleteAndClaim(profile, profile.DailyQuests.First(q => q.Definition.Kind != M4ObjectiveKind.FieldBoss), "shared-lumen");
            Assert.That(manager.Balance(CurrencyType.Lumen), Is.EqualTo(105));
            Assert.That(Open().Coins, Is.EqualTo(105));
            Assert.That(Open().GetEconomySnapshot().GetBalance(CurrencyType.Lumen), Is.EqualTo(105));
        }

        [Test]
        public void CurrencyOperationsRejectInvalidZeroNegativeInsufficientAndOverflowAmounts()
        {
            var profile = Open();
            var manager = CreateManager(profile);
            string before = State(manager.Snapshot);
            string disk = File.ReadAllText(path);
            foreach (int amount in new[] { 0, -1, int.MinValue })
            {
                Assert.That(manager.Add(CurrencyType.Lumen, amount), Is.False);
                Assert.That(manager.Spend(CurrencyType.Lumen, amount), Is.False);
            }
            Assert.That(manager.Add((CurrencyType)999, 1), Is.False);
            Assert.That(manager.Spend((CurrencyType)999, 1), Is.False);
            Assert.That(manager.Spend(CurrencyType.Lumen, 1), Is.False);
            Assert.That(State(manager.Snapshot), Is.EqualTo(before));
            Assert.That(File.ReadAllText(path), Is.EqualTo(disk));
            Assert.That(manager.Add(CurrencyType.Lumen, int.MaxValue), Is.True);
            before = State(manager.Snapshot);
            Assert.That(manager.Add(CurrencyType.Lumen, 1), Is.False);
            Assert.That(State(manager.Snapshot), Is.EqualTo(before));
            Assert.That(manager.Spend(CurrencyType.Lumen, int.MaxValue), Is.True);
            Assert.That(manager.Balance(CurrencyType.Lumen), Is.Zero);
        }

        [Test]
        public void ExchangeUses160ShardsAndNeverPartiallyChangesEitherBalance()
        {
            var profile = Open();
            var manager = CreateManager(profile);
            Assert.That(manager.Add(CurrencyType.OrbitShard, 159), Is.True);
            string before = State(manager.Snapshot);
            Assert.That(manager.Exchange(CurrencyType.StandardPledge), Is.False);
            Assert.That(State(manager.Snapshot), Is.EqualTo(before));
            Assert.That(manager.Add(CurrencyType.OrbitShard, 1), Is.True);
            Assert.That(manager.Exchange(CurrencyType.StandardPledge), Is.True);
            Assert.That(manager.Balance(CurrencyType.OrbitShard), Is.Zero);
            Assert.That(manager.Balance(CurrencyType.StandardPledge), Is.EqualTo(1));
            Assert.That(manager.Add(CurrencyType.OrbitShard, 1600), Is.True);
            Assert.That(manager.Exchange(CurrencyType.LimitedPledge, 10), Is.True);
            Assert.That(manager.Balance(CurrencyType.LimitedPledge), Is.EqualTo(10));
            Assert.That(manager.Balance(CurrencyType.OrbitShard), Is.Zero);
            before = State(manager.Snapshot);
            Assert.That(manager.Exchange(CurrencyType.Lumen), Is.False);
            Assert.That(manager.Exchange((CurrencyType)999), Is.False);
            foreach (int count in new[] { 0, -1, int.MaxValue })
                Assert.That(manager.Exchange(CurrencyType.StandardPledge, count), Is.False);
            Assert.That(State(manager.Snapshot), Is.EqualTo(before));
            Commit(profile, data =>
            {
                data.balances[(int)CurrencyType.OrbitShard] = 160;
                data.balances[(int)CurrencyType.StandardPledge] = int.MaxValue;
            });
            before = State(manager.Snapshot);
            Assert.That(manager.Exchange(CurrencyType.StandardPledge), Is.False);
            Assert.That(State(manager.Snapshot), Is.EqualTo(before));
        }

        [Test]
        public void TenPullChecksTheCorrectTicketBeforeRandomnessThenCommitsAllTenOnce()
        {
            var profile = Open();
            int randomCalls = 0;
            var manager = CreateManager(profile, () => { randomCalls++; return .999; });
            Assert.That(manager.Add(CurrencyType.StandardPledge, 9), Is.True);
            Assert.That(manager.Add(CurrencyType.LimitedPledge, 10), Is.True);
            int changes = 0, results = 0, presentations = 0;
            manager.Changed += () => changes++;
            manager.PullCommitted += _ => results++;
            manager.PresentationRequested += _ => presentations++;
            string before = State(manager.Snapshot);
            Assert.That(manager.PullTen(Standard, out _, true), Is.False);
            Assert.That(State(manager.Snapshot), Is.EqualTo(before));
            Assert.That(randomCalls, Is.Zero);
            Assert.That(changes + results + presentations, Is.Zero);
            Assert.That(manager.Add(CurrencyType.StandardPledge, 1), Is.True);
            changes = 0;
            long revision = manager.Snapshot.revision;
            Assert.That(manager.PullTen(Standard, out var pulls, true), Is.True);
            Assert.That(pulls.Length, Is.EqualTo(10));
            Assert.That(manager.Balance(CurrencyType.StandardPledge), Is.Zero);
            Assert.That(manager.Balance(CurrencyType.LimitedPledge), Is.EqualTo(10));
            Assert.That(manager.Snapshot.owned.Sum(character => character.copies), Is.EqualTo(10));
            Assert.That(manager.Snapshot.revision, Is.EqualTo(revision + 1));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(results, Is.EqualTo(10));
            Assert.That(presentations, Is.Zero);
            Assert.That(State(Open().GetEconomySnapshot()), Is.EqualTo(State(manager.Snapshot)));
        }

        [Test]
        public void FailedAtomicPullWriteLeavesTicketsOwnershipPityAndEventsUnchanged()
        {
            var profile = Open();
            var manager = CreateManager(profile, () => .999);
            Assert.That(manager.Add(CurrencyType.StandardPledge, 10), Is.True);
            Assert.That(manager.Add(CurrencyType.OrbitShard, 160), Is.True);
            int changes = 0, results = 0, presentations = 0;
            manager.Changed += () => changes++;
            manager.PullCommitted += _ => results++;
            manager.PresentationRequested += _ => presentations++;
            string before = State(manager.Snapshot);
            string disk = File.ReadAllText(path);
            using (var locked = new FileStream(path + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(manager.PullTen(Standard, out _), Is.False);
                Assert.That(manager.Exchange(CurrencyType.StandardPledge), Is.False);
                Assert.That(manager.GrantStarters(), Is.False);
                Assert.That(manager.Add(CurrencyType.Lumen, 25), Is.False);
                Assert.That(State(manager.Snapshot), Is.EqualTo(before));
                Assert.That(File.ReadAllText(path), Is.EqualTo(disk));
                Assert.That(changes + results + presentations, Is.Zero);
                Assert.That(manager.LastError, Is.Not.Empty);
            }
            Assert.That(manager.PullTen(Standard, out var recovered, true), Is.True);
            Assert.That(recovered.Length, Is.EqualTo(10));
            Assert.That(manager.Balance(CurrencyType.StandardPledge), Is.Zero);
            Assert.That(State(Open().GetEconomySnapshot()), Is.EqualTo(State(manager.Snapshot)));
        }

        [Test]
        public void InvalidRandomnessAfterOnePendingResultCancelsTheEntireTenPull()
        {
            var profile = Open();
            var random = new Queue<double>(new[] { .99, 0d, double.NaN });
            var manager = CreateManager(profile, () => random.Dequeue());
            Assert.That(manager.Add(CurrencyType.StandardPledge, 10), Is.True);
            int events = 0;
            manager.Changed += () => events++;
            manager.PullCommitted += _ => events++;
            manager.PresentationRequested += _ => events++;
            string before = State(manager.Snapshot);
            string disk = File.ReadAllText(path);
            Assert.That(manager.PullTen(Standard, out var results), Is.False);
            Assert.That(results, Is.Empty);
            Assert.That(random, Is.Empty, "The failure happens after a valid first draw, inside the second draw.");
            Assert.That(State(manager.Snapshot), Is.EqualTo(before));
            Assert.That(File.ReadAllText(path), Is.EqualTo(disk));
            Assert.That(events, Is.Zero);
        }

        [Test]
        public void LimitedLossGuaranteeSurvivesReloadAndIsConsumedByTheNextFiveStar()
        {
            var profile = Open();
            Commit(profile, data =>
            {
                data.balances[(int)CurrencyType.LimitedPledge] = 2;
                data.pities = new[] { new BannerPityData { bannerId = Limited.Id, fiveStarMisses = 89 } };
            });
            var random = new Queue<double>(new[] { .99, .99, 0d, 0d });
            var manager = CreateManager(profile, () => random.Dequeue());
            Assert.That(manager.PullSingle(Limited, out var loss, true), Is.True);
            Assert.That(loss.Rarity, Is.EqualTo(CharacterRarity.Five));
            Assert.That(loss.Character.Id, Is.Not.EqualTo(Limited.Featured.Id));
            Assert.That(manager.Snapshot.GetPity(Limited.Id).guaranteedFeatured, Is.True);
            Assert.That(manager.Reload(), Is.True);
            Assert.That(manager.Snapshot.GetPity(Limited.Id).guaranteedFeatured, Is.True);
            Assert.That(manager.Snapshot.CopiesOf(loss.Character.Id), Is.EqualTo(1));
            Assert.That(manager.PullSingle(Limited, out var guaranteed, true), Is.True);
            Assert.That(guaranteed.Character, Is.SameAs(Limited.Featured));
            Assert.That(manager.Snapshot.GetPity(Limited.Id).guaranteedFeatured, Is.False);
            Assert.That(manager.Snapshot.GetPity(Limited.Id).fiveStarMisses, Is.Zero);
            Assert.That(manager.Balance(CurrencyType.LimitedPledge), Is.Zero);
            Assert.That(random, Is.Empty);
            Assert.That(State(Open().GetEconomySnapshot()), Is.EqualTo(State(manager.Snapshot)));
        }

        [Test]
        public void EventsObservePersistedResultsAndSkipOnlySuppressesPresentation()
        {
            var profile = Open();
            var manager = CreateManager(profile, () => 0d);
            Assert.That(manager.Add(CurrencyType.LimitedPledge, 2), Is.True);
            int changes = 0, results = 0, presentations = 0;
            Action assertPersisted = () =>
            {
                SaveEnvelope disk = ReadFile();
                Assert.That(disk.version, Is.EqualTo(M4ProgressService.SaveVersion));
                Assert.That(State(disk.economy), Is.EqualTo(State(manager.Snapshot)), "Subscribers must see the committed snapshot.");
            };
            manager.Changed += () => { changes++; assertPersisted(); };
            manager.PullCommitted += result => { results++; assertPersisted(); };
            manager.PresentationRequested += rarity => { presentations++; assertPersisted(); };
            Assert.That(manager.PullSingle(Limited, out var first, true), Is.True);
            Assert.That(first.IsNew, Is.True);
            Assert.That(first.OwnedCopies, Is.EqualTo(1));
            Assert.That(results, Is.EqualTo(1));
            Assert.That(presentations, Is.Zero);
            Assert.That(manager.PullSingle(Limited, out var duplicate, false), Is.True);
            Assert.That(duplicate.Character, Is.SameAs(first.Character));
            Assert.That(duplicate.IsNew, Is.False);
            Assert.That(duplicate.OwnedCopies, Is.EqualTo(2));
            Assert.That(changes, Is.EqualTo(2));
            Assert.That(results, Is.EqualTo(2));
            Assert.That(presentations, Is.EqualTo(1));
            Assert.That(manager.Balance(CurrencyType.LimitedPledge), Is.Zero);
        }

        [Test]
        public void StartersAreExplicitAndGrantedOnceAcrossSaveReloadAndRepeatedInitialization()
        {
            var profile = Open();
            var manager = CreateManager(profile);
            Assert.That(manager.Snapshot.startersGranted, Is.False, "Initialize must not grant currency or characters.");
            Assert.That(manager.Snapshot.owned, Is.Empty);
            Assert.That(manager.Snapshot.balances.All(balance => balance == 0), Is.True);
            Assert.That(manager.GrantStarters(), Is.True);
            CharacterDefinition[] starters = catalog.Characters.Where(character => character.IsStarter).ToArray();
            Assert.That(starters.Length, Is.EqualTo(3));
            foreach (CharacterDefinition starter in starters)
                Assert.That(manager.Snapshot.CopiesOf(starter.Id), Is.EqualTo(1), starter.DisplayName);
            Assert.That(manager.Save(), Is.True);
            Assert.That(manager.Reload(), Is.True);
            string before = State(manager.Snapshot);
            manager.Initialize(profile, catalog);
            Assert.That(State(manager.Snapshot), Is.EqualTo(before));
            Assert.That(manager.GrantStarters(), Is.False);
            Assert.That(State(manager.Snapshot), Is.EqualTo(before));
            Assert.That(Open().GetEconomySnapshot().startersGranted, Is.True);
            Assert.Throws<InvalidOperationException>(() => manager.Initialize(Open(), catalog));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CorruptOrFutureSaveIsReadOnlyAndCannotChargeGrantOrEmitEvents(bool future)
        {
            var profile = Open();
            Assert.That(profile.Save(), Is.True); // A valid older backup must not defeat future-version protection.
            string original = future ? "{\"version\":999,\"untouched\":\"future payload\"}" : "{}";
            File.WriteAllText(path, original);
            if (!future && File.Exists(path + ".bak")) File.WriteAllText(path + ".bak", "null");
            var readOnly = Open();
            Assert.That(readOnly.IsReadOnly, Is.True);
            Assert.That(readOnly.LoadStatus, Is.EqualTo(future ? M4LoadStatus.UnsupportedVersionReadOnly : M4LoadStatus.CorruptReadOnly));
            var manager = CreateManager(readOnly, () => throw new InvalidOperationException("Read-only pulls must not use randomness."));
            int changes = 0, results = 0, presentations = 0;
            manager.Changed += () => changes++;
            manager.PullCommitted += _ => results++;
            manager.PresentationRequested += _ => presentations++;
            string before = State(manager.Snapshot);
            Assert.That(manager.Add(CurrencyType.OrbitShard, 1600), Is.False);
            Assert.That(manager.Spend(CurrencyType.Lumen, 1), Is.False);
            Assert.That(manager.Exchange(CurrencyType.StandardPledge), Is.False);
            Assert.That(manager.GrantStarters(), Is.False);
            Assert.That(manager.PullSingle(Standard, out _), Is.False);
            Assert.That(manager.PullTen(Limited, out _), Is.False);
            Assert.That(manager.Save(), Is.False);
            Assert.That(manager.IsReadOnly, Is.True);
            Assert.That(State(manager.Snapshot), Is.EqualTo(before));
            Assert.That(changes + results + presentations, Is.Zero);
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        }

        [TestCase(null)]
        [TestCase("{}")]
        [TestCase("{\"balances\":[0,0,0,0,0,0],\"revision\":0}")]
        public void VersionTwoWithoutEconomyIsReadOnlyInsteadOfSilentlyResettingProgress(string economyJson)
        {
            var profile = Open();
            Assert.That(profile.Accept(profile.DailyQuests[0].Definition.Id), Is.True);
            Commit(profile, data =>
            {
                data.balances[(int)CurrencyType.OrbitShard] = 1600;
                data.owned = new[] { new OwnedCharacterData { id = catalog.Characters[0].Id, copies = 2 } };
                data.startersGranted = true;
            });
            // Keep every valid M4 field, but deliberately serialize through a class with no economy field.
            // Unity JsonUtility can otherwise construct nested defaults for a missing serialized section.
            var missingEconomy = JsonUtility.FromJson<LegacySave>(File.ReadAllText(path));
            missingEconomy.version = 2;
            Assert.That(missingEconomy.coins, Is.Zero);
            string original = JsonUtility.ToJson(missingEconomy, true);
            Assert.That(original, Does.Not.Contain("\"economy\""));
            if (economyJson != null)
            {
                // Add a syntactically valid but incomplete section while preserving every valid M4 field.
                original = original.TrimEnd();
                original = original.Substring(0, original.Length - 1) + ",\"economy\":" + economyJson + "}";
            }
            File.WriteAllText(path, original);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");

            var rejected = Open();
            Assert.That(rejected.IsReadOnly, Is.True, "Missing or incomplete v2 economy must never become a writable empty wallet.");
            Assert.That(rejected.LoadStatus, Is.EqualTo(M4LoadStatus.CorruptReadOnly));
            Assert.That(rejected.LoadMessage, Is.Not.Empty);
            var manager = CreateManager(rejected);
            int changes = 0;
            manager.Changed += () => changes++;
            Assert.That(manager.Add(CurrencyType.OrbitShard, 160), Is.False);
            Assert.That(manager.GrantStarters(), Is.False);
            Assert.That(manager.Save(), Is.False);
            Assert.That(changes, Is.Zero);
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            Assert.That(File.Exists(path + ".bak"), Is.False);
        }

        [Test]
        public void ReloadKeepsTheManagerInstanceAndRejectsAnOutstandingOldDraft()
        {
            var profile = Open();
            var manager = CreateManager(profile);
            Assert.That(manager.Add(CurrencyType.OrbitShard, 160), Is.True);
            EconomySaveData old = manager.Snapshot;
            var writer = Open();
            Commit(writer, data => data.balances[(int)CurrencyType.OrbitShard] = 320);
            Assert.That(manager.Reload(), Is.True);
            Assert.That(manager.Balance(CurrencyType.OrbitShard), Is.EqualTo(320));
            Assert.That(manager.Catalog, Is.SameAs(catalog));
            Assert.That(manager.GetComponent<CurrencyManager>(), Is.SameAs(manager));
            Assert.That(profile.TryCommitEconomy(old.revision, old), Is.False);
            Assert.That(manager.Balance(CurrencyType.OrbitShard), Is.EqualTo(320));
        }

        [Test]
        public void VersionTwoBackupRecoveryRetainsOwnedCopiesPityAndStarterMarker()
        {
            var profile = Open();
            Commit(profile, data =>
            {
                data.balances[(int)CurrencyType.OrbitShard] = 320;
                data.owned = new[] { new OwnedCharacterData { id = catalog.Characters[0].Id, copies = 3 } };
                data.pities = new[] { new BannerPityData { bannerId = Limited.Id, fiveStarMisses = 32, fourStarMisses = 2, guaranteedFeatured = true } };
                data.startersGranted = true;
            });
            Assert.That(profile.Save(), Is.True); // The backup now contains the same committed economy.
            string expected = State(profile.GetEconomySnapshot());
            const string corrupt = "{interrupted";
            File.WriteAllText(path, corrupt);
            var recovered = Open();
            Assert.That(recovered.LoadStatus, Is.EqualTo(M4LoadStatus.RecoveredBackup));
            Assert.That(State(recovered.GetEconomySnapshot()), Is.EqualTo(expected));
            Assert.That(recovered.Save(), Is.True);
            string[] quarantines = Directory.GetFiles(directory, "m4-progress.json.corrupt-*");
            Assert.That(quarantines.Length, Is.EqualTo(1));
            Assert.That(File.ReadAllText(quarantines[0]), Is.EqualTo(corrupt));
            Assert.That(State(Open().GetEconomySnapshot()), Is.EqualTo(expected));
        }

        [Test]
        public void CatalogContainsTheConfirmedEighteenCharactersAndBothCompletePools()
        {
            Assert.DoesNotThrow(catalog.Validate);
            (string name, ElementType element, CharacterRarity rarity)[] expected =
            {
                ("카이런", ElementType.Fire, CharacterRarity.Three),
                ("밀라", ElementType.Water, CharacterRarity.Three), ("토르반", ElementType.Rock, CharacterRarity.Three),
                ("로렌", ElementType.Fire, CharacterRarity.Four), ("카이", ElementType.Fire, CharacterRarity.Four),
                ("셀린", ElementType.Water, CharacterRarity.Four), ("노아", ElementType.Water, CharacterRarity.Four),
                ("핀", ElementType.Wind, CharacterRarity.Four), ("리아", ElementType.Wind, CharacterRarity.Four),
                ("도린", ElementType.Rock, CharacterRarity.Four), ("마르코", ElementType.Rock, CharacterRarity.Four),
                ("조이", ElementType.Lightning, CharacterRarity.Four), ("벨라", ElementType.Lightning, CharacterRarity.Four),
                ("이그니스", ElementType.Fire, CharacterRarity.Five), ("마리스", ElementType.Water, CharacterRarity.Five),
                ("아우라", ElementType.Wind, CharacterRarity.Five), ("그롬", ElementType.Rock, CharacterRarity.Five),
                ("스파클", ElementType.Lightning, CharacterRarity.Five)
            };
            Assert.That(catalog.Characters.Length, Is.EqualTo(18));
            CollectionAssert.AreEquivalent(expected.Select(value => value.name), catalog.Characters.Select(character => character.DisplayName));
            foreach (var value in expected)
            {
                CharacterDefinition character = catalog.Characters.Single(item => item.DisplayName == value.name);
                Assert.That(character.Element, Is.EqualTo(value.element), value.name);
                Assert.That(character.Rarity, Is.EqualTo(value.rarity), value.name);
                Assert.That(character.IsStarter, Is.EqualTo(value.rarity == CharacterRarity.Three), value.name);
                Assert.That(character.Role, Is.Not.Empty, value.name);
            }
            var weapons = new Dictionary<string, WeaponType>
            {
                ["이그니스"] = WeaponType.Longsword, ["마리스"] = WeaponType.Bow,
                ["아우라"] = WeaponType.DualBlades, ["그롬"] = WeaponType.Greatsword, ["스파클"] = WeaponType.Spear
            };
            foreach (var pair in weapons)
                Assert.That(catalog.Characters.Single(character => character.DisplayName == pair.Key).Weapon, Is.EqualTo(pair.Value), pair.Key);
            Assert.That(catalog.Banners.Length, Is.EqualTo(2));
            foreach (GachaBanner banner in catalog.Banners)
            {
                Assert.That(banner.ThreeStars.Length, Is.EqualTo(3));
                Assert.That(banner.FourStars.Length, Is.EqualTo(10));
                Assert.That(banner.FiveStars.Length, Is.EqualTo(5));
                CollectionAssert.AreEquivalent(catalog.Characters.Where(character => character.Rarity == CharacterRarity.Five), banner.FiveStars);
            }
        }
    }
}
