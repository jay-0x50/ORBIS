using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.M1;
using Orbis.M15;
using Orbis.M4;
using UnityEngine;

namespace Orbis.M16.Tests
{
    /// <summary>Only GUID temporary saves are used; no default session or user profile is opened.</summary>
    public sealed class ExplorerPersistenceTests
    {
        private string directory;
        private string path;
        private DateTime now;

        [Serializable] private sealed class Quest { public string id; public int state; }
        [Serializable] private sealed class VersionOneSave
        {
            public int version;
            public int resetHourKst;
            public long lastSeenUtcTicks;
            public string dailyPeriod;
            public string weeklyPeriod;
            public int coins;
            public Quest[] dailyQuests;
            public int[] defeatedRegions;
            public string[] processedEventIds;
        }
        // No explorer member: fixtures retain valid M4/economy data while testing old versions or missing v3 data.
        [Serializable] private sealed class BeforeExplorerSave
        {
            public int version;
            public int resetHourKst;
            public long lastSeenUtcTicks;
            public string dailyPeriod;
            public string weeklyPeriod;
            public int coins;
            public EconomySaveData economy;
            public Quest[] dailyQuests;
            public int[] defeatedRegions;
            public string[] processedEventIds;
        }
        [Serializable] private sealed class Envelope
        {
            public int version;
            public EconomySaveData economy;
            public ExplorerSaveData explorer;
        }

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "Orbis-M16-Tests-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "m4-progress.json");
            Directory.CreateDirectory(directory);
            now = new DateTime(2026, 9, 11, 3, 0, 0, DateTimeKind.Utc);
        }

        [TearDown]
        public void TearDown()
        {
            if (directory == null || !Directory.Exists(directory)) return;
            string resolved = Path.GetFullPath(directory);
            Assert.That(Path.GetDirectoryName(resolved),
                Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            Assert.That(Path.GetFileName(resolved), Does.StartWith("Orbis-M16-Tests-"));
            Directory.Delete(resolved, true);
        }

        private M4ProgressService Open() => new M4ProgressService(path, () => now);
        private static string Json(object value) => JsonUtility.ToJson(value);
        private Envelope ReadEnvelope() => JsonUtility.FromJson<Envelope>(File.ReadAllText(path));
        private void WriteWithoutBackup(string json)
        {
            File.WriteAllText(path, json);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }

        private static void SeedEconomy(M4ProgressService profile)
        {
            EconomySaveData draft = profile.GetEconomySnapshot();
            draft.balances = new[] { 145, 320, 2, 3, 4, 5 };
            draft.owned = new[] { new OwnedCharacterData { id = "preserved-companion", copies = 2 } };
            draft.pities = new[]
            {
                new BannerPityData { bannerId = "preserved-banner", fiveStarMisses = 73, fourStarMisses = 9, guaranteedFeatured = true }
            };
            draft.startersGranted = true;
            Assert.That(profile.TryCommitEconomy(draft.revision, draft), Is.True);
        }

        private static string CompleteWorldProgress(M4ProgressService profile)
        {
            M4DailyQuest quest = profile.DailyQuests.First(q => q.Definition.Kind != M4ObjectiveKind.FieldBoss);
            Assert.That(profile.Accept(quest.Definition.Id), Is.True);
            Assert.That(profile.RecordObjective(quest.Definition.Kind, quest.Definition.Region, "preserved-event"), Is.True);
            Assert.That(profile.Claim(quest.Definition.Id), Is.True);
            Assert.That(profile.RecordBossDefeated(M4RegionId.Agnia), Is.True);
            return quest.Definition.Id;
        }

        [Test]
        public void NewProfileHasNoSelectionAndRejectsInvalidOrPrematureChanges()
        {
            var profile = Open();
            ExplorerSaveData empty = profile.GetExplorerSnapshot();
            Assert.That(empty.choice, Is.EqualTo(ExplorerChoice.Unselected));
            Assert.That(empty.element, Is.EqualTo(ElementType.None));
            Assert.That(empty.Validate(out _), Is.True);
            string original = File.ReadAllText(path);
            long revision = profile.GetEconomySnapshot().revision;
            int changes = 0;
            profile.Changed += () => changes++;
            Assert.That(profile.TrySetExplorerElement(ElementType.Fire), Is.False);
            Assert.That(profile.TrySelectExplorer(ExplorerChoice.Unselected), Is.False);
            Assert.That(profile.TrySelectExplorer((ExplorerChoice)99), Is.False);
            Assert.That(profile.TrySelectExplorer(ExplorerChoice.Stella, ElementType.None), Is.False);
            Assert.That(profile.TrySelectExplorer(ExplorerChoice.Polaris, (ElementType)99), Is.False);
            Assert.That(profile.GetEconomySnapshot().revision, Is.EqualTo(revision));
            Assert.That(changes, Is.Zero);
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            Assert.That(new ExplorerSaveData { choice = ExplorerChoice.Unselected, element = ElementType.Fire }.Validate(out _), Is.False);
            Assert.That(new ExplorerSaveData { choice = ExplorerChoice.Stella, element = ElementType.None }.Validate(out _), Is.False);
        }

        [TestCase(ExplorerChoice.Stella)]
        [TestCase(ExplorerChoice.Polaris)]
        public void SelectionIsSavedOnceAndSnapshotsCannotChangeTheStoredChoice(ExplorerChoice choice)
        {
            var profile = Open();
            Assert.That(profile.TrySelectExplorer(choice), Is.True);
            Assert.That(ReadEnvelope().version, Is.EqualTo(M4ProgressService.SaveVersion));
            Assert.That(ReadEnvelope().explorer.choice, Is.EqualTo(choice));
            Assert.That(ReadEnvelope().explorer.element, Is.EqualTo(ElementType.Fire));
            ExplorerSaveData detached = profile.GetExplorerSnapshot();
            detached.choice = ExplorerChoice.Unselected;
            detached.element = ElementType.None;
            Assert.That(profile.GetExplorerSnapshot().choice, Is.EqualTo(choice));
            ExplorerSaveData clone = profile.GetExplorerSnapshot().Clone();
            clone.element = ElementType.Water;
            Assert.That(profile.GetExplorerSnapshot().element, Is.EqualTo(ElementType.Fire));

            var loaded = Open();
            Assert.That(loaded.GetExplorerSnapshot().choice, Is.EqualTo(choice));
            string before = File.ReadAllText(path);
            Assert.That(loaded.TrySelectExplorer(choice), Is.False);
            Assert.That(loaded.TrySelectExplorer(choice == ExplorerChoice.Stella ? ExplorerChoice.Polaris : ExplorerChoice.Stella), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        }

        [Test]
        public void AllFiveElementsPersistWithoutChangingChoiceAndSameElementIsANoOp()
        {
            var profile = Open();
            Assert.That(profile.TrySelectExplorer(ExplorerChoice.Polaris), Is.True);
            foreach (ElementType element in new[] { ElementType.Water, ElementType.Wind, ElementType.Rock, ElementType.Lightning, ElementType.Fire })
            {
                Assert.That(profile.TrySetExplorerElement(element), Is.True, element.ToString());
                var loaded = Open();
                Assert.That(loaded.GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Polaris));
                Assert.That(loaded.GetExplorerSnapshot().element, Is.EqualTo(element));
                long revision = loaded.GetEconomySnapshot().revision;
                string before = File.ReadAllText(path);
                Assert.That(loaded.TrySetExplorerElement(element), Is.False);
                Assert.That(loaded.GetEconomySnapshot().revision, Is.EqualTo(revision));
                Assert.That(File.ReadAllText(path), Is.EqualTo(before));
            }
            Assert.That(profile.TrySetExplorerElement(ElementType.None), Is.False);
            Assert.That(profile.TrySetExplorerElement((ElementType)999), Is.False);
        }

        [Test]
        public void SelectionAndElementChangesPreserveEconomyAndInvalidateStaleCurrencyDrafts()
        {
            var profile = Open();
            string questId = CompleteWorldProgress(profile);
            SeedEconomy(profile);
            EconomySaveData old = profile.GetEconomySnapshot();
            Assert.That(profile.TrySelectExplorer(ExplorerChoice.Stella, ElementType.Wind), Is.True);
            Assert.That(profile.TryCommitEconomy(old.revision, old), Is.False);
            EconomySaveData selected = profile.GetEconomySnapshot();
            Assert.That(selected.revision, Is.EqualTo(old.revision + 1));
            old.revision = selected.revision;
            Assert.That(Json(selected), Is.EqualTo(Json(old)));
            Assert.That(profile.TrySetExplorerElement(ElementType.Water), Is.True);
            Assert.That(profile.TryCommitEconomy(selected.revision, selected), Is.False);
            EconomySaveData switched = profile.GetEconomySnapshot();
            selected.revision = switched.revision;
            Assert.That(Json(switched), Is.EqualTo(Json(selected)));
            Assert.That(switched.revision, Is.EqualTo(old.revision + 1));
            Assert.That(profile.DailyQuests.Single(q => q.Definition.Id == questId).State, Is.EqualTo(M4QuestState.Claimed));
            Assert.That(profile.IsBossDefeated(M4RegionId.Agnia), Is.True);
            Assert.That(Open().GetEconomySnapshot().CopiesOf("preserved-companion"), Is.EqualTo(2));
            Assert.That(Open().GetEconomySnapshot().GetPity("preserved-banner").guaranteedFeatured, Is.True);
        }

        [Test]
        public void FailedSelectionWriteLeavesIdentityRevisionAndDiskUnchanged()
        {
            var profile = Open();
            SeedEconomy(profile);
            string before = File.ReadAllText(path);
            string economy = Json(profile.GetEconomySnapshot());
            int changes = 0;
            profile.Changed += () =>
            {
                changes++;
                Assert.That(ReadEnvelope().explorer.choice, Is.EqualTo(profile.GetExplorerSnapshot().choice));
                Assert.That(ReadEnvelope().economy.revision, Is.EqualTo(profile.GetEconomySnapshot().revision));
            };
            using (var locked = new FileStream(path + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(profile.TrySelectExplorer(ExplorerChoice.Stella), Is.False);
                Assert.That(profile.GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Unselected));
                Assert.That(Json(profile.GetEconomySnapshot()), Is.EqualTo(economy));
                Assert.That(File.ReadAllText(path), Is.EqualTo(before));
                Assert.That(profile.LastSaveError, Is.Not.Empty);
                Assert.That(changes, Is.Zero);
            }
            Assert.That(profile.TrySelectExplorer(ExplorerChoice.Polaris), Is.True);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(Open().GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Polaris));
        }

        [Test]
        public void FailedElementWriteRetainsTheLastCommittedElementAndCanBeRetried()
        {
            var profile = Open();
            Assert.That(profile.TrySelectExplorer(ExplorerChoice.Stella, ElementType.Rock), Is.True);
            string before = File.ReadAllText(path);
            long revision = profile.GetEconomySnapshot().revision;
            int changes = 0;
            profile.Changed += () => changes++;
            using (var locked = new FileStream(path + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(profile.TrySetExplorerElement(ElementType.Lightning), Is.False);
                Assert.That(profile.GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Stella));
                Assert.That(profile.GetExplorerSnapshot().element, Is.EqualTo(ElementType.Rock));
                Assert.That(profile.GetEconomySnapshot().revision, Is.EqualTo(revision));
                Assert.That(File.ReadAllText(path), Is.EqualTo(before));
                Assert.That(changes, Is.Zero);
            }
            Assert.That(profile.TrySetExplorerElement(ElementType.Lightning), Is.True);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(profile.Reload(), Is.True);
            Assert.That(profile.GetExplorerSnapshot().element, Is.EqualTo(ElementType.Lightning));
        }

        [Test]
        public void VersionTwoMigrationPreservesAllEconomyAndWorldProgressWithoutSelectingAHero()
        {
            var profile = Open();
            string questId = CompleteWorldProgress(profile);
            SeedEconomy(profile);
            string economy = Json(profile.GetEconomySnapshot());
            string day = profile.DailyPeriodId, week = profile.WeeklyPeriodId;
            var old = JsonUtility.FromJson<BeforeExplorerSave>(File.ReadAllText(path));
            old.version = 2;
            string fixture = Json(old);
            Assert.That(fixture, Does.Not.Contain("\"explorer\""));
            WriteWithoutBackup(fixture);

            var migrated = Open();
            Assert.That(migrated.IsReadOnly, Is.False, migrated.LoadMessage);
            Assert.That(Json(migrated.GetEconomySnapshot()), Is.EqualTo(economy));
            Assert.That(migrated.GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Unselected));
            Assert.That(migrated.GetExplorerSnapshot().element, Is.EqualTo(ElementType.None));
            Assert.That(migrated.DailyPeriodId, Is.EqualTo(day));
            Assert.That(migrated.WeeklyPeriodId, Is.EqualTo(week));
            Assert.That(migrated.DailyQuests.Single(q => q.Definition.Id == questId).State, Is.EqualTo(M4QuestState.Claimed));
            Assert.That(migrated.IsBossDefeated(M4RegionId.Agnia), Is.True);
            Assert.That(ReadEnvelope().version, Is.EqualTo(M4ProgressService.SaveVersion));
            Assert.That(Json(Open().GetEconomySnapshot()), Is.EqualTo(economy));
            Assert.That(migrated.TrySelectExplorer(ExplorerChoice.Stella), Is.True);
            Assert.That(Open().GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Stella));
        }

        [Test]
        public void VersionOneCoinsStillMigrateAlongsideAnUnselectedExplorer()
        {
            var profile = Open();
            string questId = CompleteWorldProgress(profile);
            var old = JsonUtility.FromJson<VersionOneSave>(File.ReadAllText(path));
            old.version = 1;
            old.coins = 777;
            string fixture = Json(old);
            Assert.That(fixture, Does.Not.Contain("\"economy\""));
            Assert.That(fixture, Does.Not.Contain("\"explorer\""));
            WriteWithoutBackup(fixture);

            var migrated = Open();
            Assert.That(migrated.IsReadOnly, Is.False);
            Assert.That(migrated.Coins, Is.EqualTo(777));
            Assert.That(migrated.GetEconomySnapshot().GetBalance(CurrencyType.Lumen), Is.EqualTo(777));
            Assert.That(migrated.GetEconomySnapshot().owned, Is.Empty);
            Assert.That(migrated.GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Unselected));
            Assert.That(migrated.DailyQuests.Single(q => q.Definition.Id == questId).State, Is.EqualTo(M4QuestState.Claimed));
            Assert.That(migrated.IsBossDefeated(M4RegionId.Agnia), Is.True);
            Assert.That(migrated.Save(), Is.True);
            Assert.That(Open().Coins, Is.EqualTo(777));
        }

        [TestCase(null)]
        [TestCase("{}")]
        [TestCase("{\"choice\":1}")]
        [TestCase("{\"element\":1}")]
        [TestCase("{\"choice\":0,\"element\":1}")]
        [TestCase("{\"choice\":1,\"element\":0}")]
        public void MissingPartialOrInconsistentVersionThreeExplorerIsReadOnly(string explorerJson)
        {
            var profile = Open();
            SeedEconomy(profile);
            Assert.That(profile.TrySelectExplorer(ExplorerChoice.Stella), Is.True);
            var malformed = JsonUtility.FromJson<BeforeExplorerSave>(File.ReadAllText(path));
            malformed.version = 3;
            string original = Json(malformed);
            Assert.That(original, Does.Not.Contain("\"explorer\""));
            if (explorerJson != null)
                original = original.Substring(0, original.Length - 1) + ",\"explorer\":" + explorerJson + "}";
            WriteWithoutBackup(original);

            var rejected = Open();
            Assert.That(rejected.IsReadOnly, Is.True, "Incomplete v3 protagonist data must not reopen character selection.");
            Assert.That(rejected.LoadStatus, Is.EqualTo(M4LoadStatus.CorruptReadOnly));
            Assert.That(rejected.TrySelectExplorer(ExplorerChoice.Polaris), Is.False);
            Assert.That(rejected.TrySetExplorerElement(ElementType.Water), Is.False);
            Assert.That(rejected.Save(), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        }

        [Test]
        public void ValidVersionThreeBackupRecoversTheChosenExplorerAndPreservesBadMain()
        {
            var profile = Open();
            SeedEconomy(profile);
            Assert.That(profile.TrySelectExplorer(ExplorerChoice.Polaris, ElementType.Water), Is.True);
            Assert.That(profile.Save(), Is.True);
            string economy = Json(profile.GetEconomySnapshot());
            var missing = JsonUtility.FromJson<BeforeExplorerSave>(File.ReadAllText(path));
            missing.version = 3;
            string original = Json(missing);
            File.WriteAllText(path, original);

            var recovered = Open();
            Assert.That(recovered.LoadStatus, Is.EqualTo(M4LoadStatus.RecoveredBackup));
            Assert.That(recovered.GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Polaris));
            Assert.That(recovered.GetExplorerSnapshot().element, Is.EqualTo(ElementType.Water));
            Assert.That(Json(recovered.GetEconomySnapshot()), Is.EqualTo(economy));
            Assert.That(recovered.Save(), Is.True);
            string quarantine = Directory.GetFiles(directory, "m4-progress.json.corrupt-*").Single();
            Assert.That(File.ReadAllText(quarantine), Is.EqualTo(original));
            Assert.That(Open().GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Polaris));
        }
    }
}
