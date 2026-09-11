using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Orbis.M4.Tests
{
    public sealed class M4ProgressTests
    {
        string directory;
        string path;
        DateTime now;
        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "Orbis-M4-Tests-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "progress.json");
            now = Utc(2026, 9, 10, 3);
        }
        [TearDown] public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        M4ProgressService Open(int resetHour = 4) => new M4ProgressService(path, () => now, resetHour);
        static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
            new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
        static string Ids(M4ProgressService progress) => string.Join("|", progress.DailyQuests.Select(q => q.Definition.Id));
        static void CompleteAndClaim(M4ProgressService progress, M4DailyQuest quest, string eventId)
        {
            Assert.That(progress.Accept(quest.Definition.Id), Is.True);
            Assert.That(progress.RecordObjective(quest.Definition.Kind, quest.Definition.Region, eventId), Is.True);
            Assert.That(progress.Claim(quest.Definition.Id), Is.True);
        }

        [Test] public void FourDistinctKindsRotateThroughAllTwentyDefinitionsInFiveDays()
        {
            var progress = Open();
            var ids = new System.Collections.Generic.HashSet<string>();
            for (int day = 0; day < 5; day++)
            {
                Assert.That(progress.DailyQuests, Has.Count.EqualTo(4));
                Assert.That(progress.DailyQuests.Select(q => q.Definition.Kind).Distinct().Count(), Is.EqualTo(4));
                Assert.That(progress.DailyQuests.Select(q => q.Definition.Region).Distinct().Count(), Is.EqualTo(4));
                Assert.That(progress.DailyQuests.All(q => q.State == M4QuestState.Offered), Is.True);
                foreach (var quest in progress.DailyQuests)
                {
                    ids.Add(quest.Definition.Id);
                    Assert.That(quest.Definition.NpcRequest, Is.Not.Empty);
                    Assert.That(quest.Definition.RewardCoins, Is.EqualTo(25));
                }
                now = now.AddDays(1);
                Assert.That(progress.RefreshPeriods(), Is.True);
            }
            Assert.That(ids.Count, Is.EqualTo(20));
            Assert.That(M4ProgressService.AllDefinitions.Select(q => q.Id), Is.EquivalentTo(ids));
        }

        [Test] public void OnlyAcceptedMatchingObjectivesCompleteAndRewardsAreIdempotent()
        {
            var progress = Open();
            var quest = progress.DailyQuests[0];
            Assert.That(progress.RecordObjective(quest.Definition.Kind, quest.Definition.Region, "before-accept"), Is.False);
            Assert.That(progress.Accept("missing"), Is.False);
            Assert.That(progress.Claim(quest.Definition.Id), Is.False);
            Assert.That(progress.Accept(quest.Definition.Id), Is.True);
            Assert.That(progress.Accept(quest.Definition.Id), Is.False);
            var otherRegion = (M4RegionId)(((int)quest.Definition.Region + 1) % 5);
            Assert.That(progress.RecordObjective(quest.Definition.Kind, otherRegion, "wrong-region"), Is.False);
            Assert.That(progress.RecordObjective((M4ObjectiveKind)(((int)quest.Definition.Kind + 1) % 4), quest.Definition.Region, "wrong-kind"), Is.False);
            Assert.That(progress.RecordObjective(quest.Definition.Kind, quest.Definition.Region, "objective-1"), Is.True);
            Assert.That(progress.DailyQuests[0].State, Is.EqualTo(M4QuestState.Completed));
            Assert.That(progress.RecordObjective(quest.Definition.Kind, quest.Definition.Region, "objective-1"), Is.False);
            Assert.That(progress.Coins, Is.Zero);
            Assert.That(progress.Claim(quest.Definition.Id), Is.True);
            Assert.That(progress.Claim(quest.Definition.Id), Is.False);
            Assert.That(progress.Coins, Is.EqualTo(25));
        }

        [Test] public void DailyBoundaryResetsAcceptedAndClaimedStatesButKeepsCoins()
        {
            now = Utc(2026, 9, 13, 18, 59, 59); // Monday 03:59:59 KST, still Sunday's commission day.
            var progress = Open();
            string previous = Ids(progress);
            CompleteAndClaim(progress, progress.DailyQuests[0], "survey");
            Assert.That(progress.Accept(progress.DailyQuests[1].Definition.Id), Is.True);
            Assert.That(progress.DailyPeriodId, Is.EqualTo("2026-09-13"));
            Assert.That(progress.RefreshPeriods(), Is.False);
            now = now.AddSeconds(1);
            Assert.That(progress.RefreshPeriods(), Is.True);
            Assert.That(progress.DailyPeriodId, Is.EqualTo("2026-09-14"));
            Assert.That(Ids(progress), Is.Not.EqualTo(previous));
            Assert.That(progress.DailyQuests.All(q => q.State == M4QuestState.Offered), Is.True);
            Assert.That(progress.Coins, Is.EqualTo(25));
            Assert.That(progress.RefreshPeriods(), Is.False);
        }

        [Test] public void ResetHourIsConfigurableWithoutDependingOnMachineTimezone()
        {
            now = Utc(2026, 9, 13, 14, 59, 59);
            var progress = Open(0);
            Assert.That(progress.DailyPeriodId, Is.EqualTo("2026-09-13"));
            now = now.AddSeconds(1);
            Assert.That(progress.RefreshPeriods(), Is.True);
            Assert.That(progress.DailyPeriodId, Is.EqualTo("2026-09-14"));
            Assert.That(progress.WeeklyPeriodId, Is.EqualTo("2026-09-14"));
        }

        [Test] public void WeeklyBossRespawnsAtMondayBoundaryOnlyAndEachRegionPaysOnce()
        {
            now = Utc(2026, 9, 12, 19);
            var progress = Open();
            foreach (M4RegionId region in Enum.GetValues(typeof(M4RegionId)))
            {
                Assert.That(progress.RecordBossDefeated(region), Is.True);
                Assert.That(progress.RecordBossDefeated(region), Is.False);
            }
            Assert.That(progress.Coins, Is.EqualTo(250));
            now = Utc(2026, 9, 13, 18, 59, 59);
            Assert.That(progress.IsBossDefeated(M4RegionId.Agnia), Is.True);
            Assert.That(progress.WeeklyPeriodId, Is.EqualTo("2026-09-07"));
            now = now.AddSeconds(1);
            Assert.That(progress.IsBossDefeated(M4RegionId.Agnia), Is.False);
            Assert.That(progress.WeeklyPeriodId, Is.EqualTo("2026-09-14"));
            Assert.That(progress.RecordBossDefeated(M4RegionId.Agnia), Is.True);
            now = now.AddDays(1);
            Assert.That(progress.IsBossDefeated(M4RegionId.Agnia), Is.True);
            Assert.That(progress.Coins, Is.EqualTo(300));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WeeklyBossProofCompletesCommissionBeforeOrAfterAccepting(bool acceptFirst)
        {
            var progress = Open();
            var quest = progress.DailyQuests.Single(q => q.Definition.Kind == M4ObjectiveKind.FieldBoss);
            if (acceptFirst) Assert.That(progress.Accept(quest.Definition.Id), Is.True);
            Assert.That(progress.RecordBossDefeated(quest.Definition.Region), Is.True);
            if (!acceptFirst) Assert.That(progress.Accept(quest.Definition.Id), Is.True);
            var loaded = Open();
            Assert.That(loaded.Coins, Is.EqualTo(50));
            Assert.That(loaded.DailyQuests.Single(q => q.Definition.Id == quest.Definition.Id).State, Is.EqualTo(M4QuestState.Completed));
            Assert.That(loaded.Claim(quest.Definition.Id), Is.True);
            Assert.That(loaded.Claim(quest.Definition.Id), Is.False);
            Assert.That(loaded.RecordBossDefeated(quest.Definition.Region), Is.False);
            Assert.That(Open().Coins, Is.EqualTo(75));
        }

        [Test] public void LaterDailyBossProofUsesWeeklyRecordButNextWeekNeedsANewVictory()
        {
            now = Utc(2026, 9, 6, 19); // Monday 04:00 KST.
            var progress = Open();
            var first = progress.DailyQuests.Single(q => q.Definition.Kind == M4ObjectiveKind.FieldBoss);
            progress.RecordBossDefeated(first.Definition.Region);
            progress.Accept(first.Definition.Id);
            progress.Claim(first.Definition.Id);
            now = now.AddDays(5); // Same rotating definition returns on Saturday.
            progress.RefreshPeriods();
            Assert.That(progress.Accept(first.Definition.Id), Is.True);
            Assert.That(progress.DailyQuests.Single(q => q.Definition.Id == first.Definition.Id).State, Is.EqualTo(M4QuestState.Completed));
            Assert.That(progress.Claim(first.Definition.Id), Is.True);
            Assert.That(progress.Coins, Is.EqualTo(100));
            now = now.AddDays(2);
            progress.RefreshPeriods();
            var next = progress.DailyQuests.Single(q => q.Definition.Kind == M4ObjectiveKind.FieldBoss);
            Assert.That(progress.Accept(next.Definition.Id), Is.True);
            Assert.That(progress.DailyQuests.Single(q => q.Definition.Id == next.Definition.Id).State, Is.EqualTo(M4QuestState.Accepted));
            Assert.That(progress.IsBossDefeated(first.Definition.Region), Is.False);
        }

        [Test] public void ReloadPreservesCommissionStatesCoinsAndWeeklyBosses()
        {
            var progress = Open();
            CompleteAndClaim(progress, progress.DailyQuests[0], "persisted-event");
            progress.Accept(progress.DailyQuests[1].Definition.Id);
            progress.RecordObjective(progress.DailyQuests[1].Definition.Kind, progress.DailyQuests[1].Definition.Region, "unclaimed");
            progress.RecordBossDefeated(M4RegionId.Teluna);
            string ids = Ids(progress);
            var loaded = Open();
            Assert.That(loaded.LoadStatus, Is.EqualTo(M4LoadStatus.Loaded));
            Assert.That(Ids(loaded), Is.EqualTo(ids));
            Assert.That(loaded.DailyQuests[0].State, Is.EqualTo(M4QuestState.Claimed));
            Assert.That(loaded.DailyQuests[1].State, Is.EqualTo(M4QuestState.Completed));
            Assert.That(loaded.Coins, Is.EqualTo(75));
            Assert.That(loaded.IsBossDefeated(M4RegionId.Teluna), Is.True);
            Assert.That(loaded.Claim(loaded.DailyQuests[0].Definition.Id), Is.False);
            Assert.That(loaded.RecordBossDefeated(M4RegionId.Teluna), Is.False);
            Assert.That(loaded.Claim(loaded.DailyQuests[1].Definition.Id), Is.True);
            Assert.That(Open().Coins, Is.EqualTo(100));
        }

        [Test] public void ClockRollbackCannotRerollOrReplayWeeklyRewardsEvenAfterReload()
        {
            now = Utc(2026, 9, 13, 19);
            var progress = Open();
            progress.RecordBossDefeated(M4RegionId.Agnia);
            now = now.AddDays(7);
            progress.RefreshPeriods();
            progress.RecordBossDefeated(M4RegionId.Agnia);
            CompleteAndClaim(progress, progress.DailyQuests[0], "future-objective");
            string ids = Ids(progress), day = progress.DailyPeriodId;
            DateTime future = now;
            now = now.AddDays(-9);
            Assert.That(progress.RefreshPeriods(), Is.False);
            Assert.That(progress.DailyPeriodId, Is.EqualTo(day));
            Assert.That(progress.RecordBossDefeated(M4RegionId.Agnia), Is.False);
            var loaded = Open();
            Assert.That(loaded.LastSeenUtc, Is.EqualTo(future));
            Assert.That(Ids(loaded), Is.EqualTo(ids));
            Assert.That(loaded.Coins, Is.EqualTo(125));
            Assert.That(loaded.DailyQuests[0].State, Is.EqualTo(M4QuestState.Claimed));
            Assert.That(loaded.RecordBossDefeated(M4RegionId.Agnia), Is.False);
        }

        [Test] public void CalendarRefreshWithinPeriodRaisesNoEventAndDoesNotWriteEveryFrame()
        {
            var progress = Open();
            string saved = File.ReadAllText(path);
            int changes = 0; progress.Changed += () => changes++;
            now = now.AddMinutes(5);
            for (int i = 0; i < 120; i++) Assert.That(progress.RefreshPeriods(), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(saved));
            Assert.That(changes, Is.Zero);
            Assert.That(progress.Save(), Is.True);
            Assert.That(Open().LastSeenUtc, Is.EqualTo(now));
            Assert.That(changes, Is.Zero);
            Assert.That(progress.Accept(progress.DailyQuests[0].Definition.Id), Is.True);
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test] public void CorruptMainRecoversBackupAndQuarantinesOriginalBeforeNextWrite()
        {
            var progress = Open();
            CompleteAndClaim(progress, progress.DailyQuests[0], "saved-objective");
            Assert.That(progress.Save(), Is.True);
            const string broken = "{ definitely invalid JSON";
            File.WriteAllText(path, broken);
            var recovered = Open();
            Assert.That(recovered.LoadStatus, Is.EqualTo(M4LoadStatus.RecoveredBackup));
            Assert.That(recovered.IsReadOnly, Is.False);
            Assert.That(recovered.Coins, Is.EqualTo(25));
            Assert.That(File.ReadAllText(path), Is.EqualTo(broken), "Loading alone must preserve the invalid original.");
            Assert.That(recovered.Accept(recovered.DailyQuests[1].Definition.Id), Is.True);
            string[] quarantines = Directory.GetFiles(directory, "progress.json.corrupt-*");
            Assert.That(quarantines, Has.Length.EqualTo(1));
            Assert.That(File.ReadAllText(quarantines[0]), Is.EqualTo(broken));
            Assert.That(Open().Coins, Is.EqualTo(25));
            Assert.That(Open().DailyQuests[1].State, Is.EqualTo(M4QuestState.Accepted));
        }

        [Test] public void InvalidMainAndBackupArePreservedAndBlockProgressWrites()
        {
            Directory.CreateDirectory(directory);
            const string invalidMain = "{}", invalidBackup = "null";
            File.WriteAllText(path, invalidMain); File.WriteAllText(path + ".bak", invalidBackup);
            var progress = Open();
            Assert.That(progress.LoadStatus, Is.EqualTo(M4LoadStatus.CorruptReadOnly));
            Assert.That(progress.IsReadOnly, Is.True);
            Assert.That(progress.LoadMessage, Is.Not.Empty);
            Assert.That(progress.Accept(progress.DailyQuests[0].Definition.Id), Is.False);
            Assert.That(progress.RecordBossDefeated(M4RegionId.Agnia), Is.False);
            Assert.That(progress.Save(), Is.False);
            now = now.AddDays(8);
            Assert.That(progress.RefreshPeriods(), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(invalidMain));
            Assert.That(File.ReadAllText(path + ".bak"), Is.EqualTo(invalidBackup));
        }

        [Test] public void MissingMainUsesLastVerifiedBackupWithoutLosingItsRewards()
        {
            var progress = Open();
            progress.RecordBossDefeated(M4RegionId.Voltheim);
            progress.Save();
            File.Delete(path);
            var recovered = Open();
            Assert.That(recovered.LoadStatus, Is.EqualTo(M4LoadStatus.RecoveredBackup));
            Assert.That(recovered.Coins, Is.EqualTo(50));
            Assert.That(recovered.IsBossDefeated(M4RegionId.Voltheim), Is.True);
            Assert.That(recovered.Save(), Is.True);
            Assert.That(Open().LoadStatus, Is.EqualTo(M4LoadStatus.Loaded));
        }

        [Test] public void FutureSaveVersionIsNotDowngradedThroughAnOldBackup()
        {
            Open().Save();
            string original = File.ReadAllText(path).Replace("\"version\": " + M4ProgressService.SaveVersion, "\"version\": 999");
            File.WriteAllText(path, original);
            var progress = Open();
            Assert.That(progress.LoadStatus, Is.EqualTo(M4LoadStatus.UnsupportedVersionReadOnly));
            Assert.That(progress.IsReadOnly, Is.True);
            Assert.That(progress.Save(), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        }

        [Test] public void FailedPeriodWriteCannotAcceptYesterdayOrRespawnUncommittedBosses()
        {
            now = Utc(2026, 9, 13, 18, 59, 59);
            var progress = Open();
            string oldId = progress.DailyQuests[0].Definition.Id;
            progress.RecordBossDefeated(M4RegionId.Agnia);
            now = now.AddSeconds(1);
            using (var locked = new FileStream(path + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(progress.RefreshPeriods(), Is.False);
                Assert.That(progress.Accept(oldId), Is.False);
                Assert.That(progress.IsBossDefeated(M4RegionId.Agnia), Is.True);
                Assert.That(progress.RecordBossDefeated(M4RegionId.Agnia), Is.False);
                Assert.That(progress.Coins, Is.EqualTo(50));
            }
            Assert.That(progress.RefreshPeriods(), Is.True);
            Assert.That(progress.IsBossDefeated(M4RegionId.Agnia), Is.False);
            Assert.That(progress.DailyQuests.All(q => q.State == M4QuestState.Offered), Is.True);
        }

        [Test] public void FailedAtomicWriteRollsBackMemoryAndReportsError()
        {
            var progress = Open();
            var quest = progress.DailyQuests[0];
            using (var locked = new FileStream(path + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(progress.Accept(quest.Definition.Id), Is.False);
                Assert.That(progress.DailyQuests[0].State, Is.EqualTo(M4QuestState.Offered));
                Assert.That(progress.RecordBossDefeated(M4RegionId.Agnia), Is.False);
                Assert.That(progress.Coins, Is.Zero);
                Assert.That(progress.LastSaveError, Is.Not.Empty);
            }
            Assert.That(progress.Accept(quest.Definition.Id), Is.True);
            Assert.That(progress.LastSaveError, Is.Null);
            Assert.That(Open().DailyQuests[0].State, Is.EqualTo(M4QuestState.Accepted));
        }
    }
}
