using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Orbis.M15;
using Orbis.M16;
using Orbis.M1;

namespace Orbis.M4
{
    public enum M4ObjectiveKind { Survey, FieldPuzzle, ChallengeRoom, FieldBoss }
    public enum M4QuestState { Offered, Accepted, Completed, Claimed }
    public enum M4LoadStatus { NewSave, Loaded, RecoveredBackup, CorruptReadOnly, UnsupportedVersionReadOnly, IoErrorReadOnly }

    public sealed class M4QuestDefinition
    {
        public string Id { get; }
        public M4RegionId Region { get; }
        public M4ObjectiveKind Kind { get; }
        public string NpcRequest { get; }
        public int RewardCoins => 25; // Unspecified M4 default, separate from M2 enhancement materials.
        internal M4QuestDefinition(string id, M4RegionId region, M4ObjectiveKind kind, string request)
        { Id = id; Region = region; Kind = kind; NpcRequest = request; }
    }

    public sealed class M4DailyQuest
    {
        public M4QuestDefinition Definition { get; }
        public M4QuestState State { get; internal set; }
        internal M4DailyQuest(M4QuestDefinition definition, M4QuestState state) { Definition = definition; State = state; }
    }

    /// <summary>
    /// JSON-backed local progress. Calendar periods use KST with an injected UTC clock.
    /// A saved high-water UTC timestamp prevents clock rollback from reopening previous rewards.
    /// Mutations are committed only after a successful write; corrupt originals are never silently replaced.
    /// </summary>
    public sealed class M4ProgressService
    {
        public const int BossRewardCoins = 50; // Unspecified default: once per region per weekly period.
        public const int SaveVersion = 3;
        const string DateFormat = "yyyy-MM-dd";
        static readonly IReadOnlyList<M4QuestDefinition> definitions = BuildDefinitions();
        readonly Func<DateTime> utcClock;
        readonly int resetHourKst;
        readonly string path;
        SaveData data;
        ReadOnlyCollection<M4DailyQuest> dailyQuests;
        long observedUtcTicks;
        bool preserveMainBeforeWrite;

        [Serializable] sealed class QuestData { public string id; public int state; }
        [Serializable] sealed class SaveData
        {
            public int version;
            public int resetHourKst;
            public long lastSeenUtcTicks;
            public string dailyPeriod;
            public string weeklyPeriod;
            public int coins; // v1 migration only; Lumen lives exclusively in economy.balances.
            public EconomySaveData economy;
            public ExplorerSaveData explorer;
            [NonSerialized] public bool upgradedFromPreviousVersion;
            public QuestData[] dailyQuests;
            public int[] defeatedRegions;
            public string[] processedEventIds;
        }

        public static IReadOnlyList<M4QuestDefinition> AllDefinitions => definitions;
        public IReadOnlyList<M4DailyQuest> DailyQuests => dailyQuests;
        public int Coins => data.economy.balances[(int)CurrencyType.Lumen];
        public string DailyPeriodId => data.dailyPeriod;
        public string WeeklyPeriodId => data.weeklyPeriod;
        public DateTime LastSeenUtc => new DateTime(observedUtcTicks, DateTimeKind.Utc);
        public string SavePath => path;
        public M4LoadStatus LoadStatus { get; private set; }
        public string LoadMessage { get; private set; }
        public string LastSaveError { get; private set; }
        public bool IsReadOnly { get; private set; }
        public event Action Changed;

        // Unspecified reset default: 04:00 KST daily and Monday 04:00 KST weekly.
        // Caller supplies the prototype setting; tests may use a different hour.
        public M4ProgressService(string savePath, Func<DateTime> utcClock = null, int resetHourKst = 4)
        {
            if (string.IsNullOrWhiteSpace(savePath)) throw new ArgumentException("A save file path is required.", nameof(savePath));
            if (resetHourKst < 0 || resetHourKst > 23) throw new ArgumentOutOfRangeException(nameof(resetHourKst));
            path = Path.GetFullPath(savePath);
            this.utcClock = utcClock ?? (() => DateTime.UtcNow);
            this.resetHourKst = resetHourKst;
            observedUtcTicks = ReadClockTicks();
            Load();
            observedUtcTicks = Math.Max(observedUtcTicks, data.lastSeenUtcTicks);
            RebuildEntries();
            if (!IsReadOnly)
            {
                bool rolled = RefreshPeriods();
                if (!rolled && (LoadStatus == M4LoadStatus.NewSave || data.upgradedFromPreviousVersion)) Save();
            }
        }

        /// <summary>Detached draft; commit with its revision before publishing rewards.</summary>
        public EconomySaveData GetEconomySnapshot() => data.economy.Clone();

        /// <summary>Detached protagonist data; all changes go through the atomic selection/element APIs.</summary>
        public ExplorerSaveData GetExplorerSnapshot() => data.explorer.Clone();

        // 기획서 미정 기본값: 첫 선택 직후 화 원소로 시작한다. 모든 5원소는 이후 비용 없이 선택 가능하다.
        public bool TrySelectExplorer(ExplorerChoice choice, ElementType initialElement = ElementType.Fire)
        {
            if (IsReadOnly) { LastSaveError = LoadMessage; return false; }
            if (data.explorer.choice != ExplorerChoice.Unselected)
            { LastSaveError = "이미 주인공 선택을 완료했습니다."; return false; }
            var selected = new ExplorerSaveData { choice = choice, element = initialElement };
            if (choice == ExplorerChoice.Unselected || !selected.Validate(out _))
            { LastSaveError = "유효한 주인공과 원소를 선택하세요."; return false; }
            return Commit(() => data.explorer = selected);
        }

        public bool TrySetExplorerElement(ElementType element)
        {
            if (IsReadOnly) { LastSaveError = LoadMessage; return false; }
            if (data.explorer.choice == ExplorerChoice.Unselected)
            { LastSaveError = "먼저 주인공을 선택하세요."; return false; }
            if (data.explorer.element == element)
            { LastSaveError = "이미 선택한 원소입니다."; return false; }
            var selected = new ExplorerSaveData { choice = data.explorer.choice, element = element };
            if (!selected.Validate(out _))
            { LastSaveError = "5원소 중 하나를 선택하세요."; return false; }
            return Commit(() => data.explorer = selected);
        }

        public bool TryCommitEconomy(long expectedRevision, EconomySaveData next)
        {
            if (IsReadOnly) { LastSaveError = LoadMessage; return false; }
            if (next == null || !next.Validate(out _))
            { LastSaveError = "유효하지 않은 재화 저장 데이터입니다."; return false; }
            if (expectedRevision != data.economy.revision || next.revision != expectedRevision)
            { LastSaveError = "진행 데이터가 변경되었습니다. 다시 시도하세요."; return false; }
            EconomySaveData draft = next.Clone();
            return Commit(() => data.economy = draft);
        }

        /// <summary>Reload the shared profile instance so world and economy stay synchronized.</summary>
        public bool Reload()
        {
            IsReadOnly = false;
            LastSaveError = null;
            preserveMainBeforeWrite = false;
            Load();
            observedUtcTicks = Math.Max(observedUtcTicks, data.lastSeenUtcTicks);
            RebuildEntries();
            if (!IsReadOnly)
            {
                bool rolled = RefreshPeriods();
                if (!rolled && (LoadStatus == M4LoadStatus.NewSave || data.upgradedFromPreviousVersion)) Save();
            }
            NotifyChanged();
            return !IsReadOnly && string.IsNullOrEmpty(LastSaveError);
        }

        void NotifyChanged()
        {
            if (Changed == null) return;
            // Listener failure must not report a persisted transaction as a failed purchase.
            foreach (Action listener in Changed.GetInvocationList())
            {
                try { listener(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        public bool Accept(string id)
        {
            RefreshPeriods();
            int index = FindQuest(id);
            if (!PeriodsAreCurrent() || index < 0 || data.dailyQuests[index].state != (int)M4QuestState.Offered) return false;
            var definition = Definition(id);
            // A weekly boss cannot be killed again for a later daily commission.
            // This one objective accepts the current week's existing victory record as proof.
            bool provenBoss = definition.Kind == M4ObjectiveKind.FieldBoss && data.defeatedRegions.Contains((int)definition.Region);
            return Commit(() => data.dailyQuests[index].state = (int)(provenBoss ? M4QuestState.Completed : M4QuestState.Accepted));
        }

        public bool RecordObjective(M4ObjectiveKind kind, M4RegionId region, string eventId)
        {
            RefreshPeriods();
            if (!PeriodsAreCurrent() || !ValidKind(kind) || !ValidRegion(region) || string.IsNullOrWhiteSpace(eventId) ||
                eventId.Length > 256 || data.processedEventIds.Contains(eventId)) return false;
            var matches = new List<int>();
            for (int i = 0; i < data.dailyQuests.Length; i++)
            {
                var definition = Definition(data.dailyQuests[i].id);
                if (definition.Kind == kind && definition.Region == region && data.dailyQuests[i].state == (int)M4QuestState.Accepted)
                    matches.Add(i);
            }
            if (matches.Count == 0) return false; // Work done before accepting a commission does not complete it.
            return Commit(() =>
            {
                foreach (int index in matches) data.dailyQuests[index].state = (int)M4QuestState.Completed;
                data.processedEventIds = data.processedEventIds.Concat(new[] { eventId }).ToArray();
            });
        }

        public bool Claim(string id)
        {
            RefreshPeriods();
            int index = FindQuest(id);
            if (!PeriodsAreCurrent() || index < 0 || data.dailyQuests[index].state != (int)M4QuestState.Completed) return false;
            int reward = Definition(id).RewardCoins;
            if (data.economy.balances[(int)CurrencyType.Lumen] > int.MaxValue - reward) return false;
            return Commit(() => { data.dailyQuests[index].state = (int)M4QuestState.Claimed; data.economy.balances[(int)CurrencyType.Lumen] += reward; });
        }

        public bool IsBossDefeated(M4RegionId region)
        {
            RefreshPeriods();
            return ValidRegion(region) && data.defeatedRegions.Contains((int)region);
        }

        public bool RecordBossDefeated(M4RegionId region)
        {
            RefreshPeriods();
            if (!PeriodsAreCurrent() || !ValidRegion(region) || data.defeatedRegions.Contains((int)region) ||
                data.economy.balances[(int)CurrencyType.Lumen] > int.MaxValue - BossRewardCoins) return false;
            return Commit(() =>
            {
                data.defeatedRegions = data.defeatedRegions.Concat(new[] { (int)region }).ToArray();
                data.economy.balances[(int)CurrencyType.Lumen] += BossRewardCoins;
                // Commit weekly reward and accepted daily proof together, avoiding a crash between two saves.
                foreach (QuestData quest in data.dailyQuests)
                {
                    var definition = Definition(quest.id);
                    if (quest.state == (int)M4QuestState.Accepted && definition.Kind == M4ObjectiveKind.FieldBoss && definition.Region == region)
                        quest.state = (int)M4QuestState.Completed;
                }
            });
        }

        /// <summary>Call regularly; writes only when a calendar boundary changes, never once per frame.</summary>
        public bool RefreshPeriods()
        {
            ObserveClock();
            if (IsReadOnly) return false;
            string day = CurrentDay(), week = CurrentWeek();
            if (day == data.dailyPeriod && week == data.weeklyPeriod) return false;
            return Commit(() =>
            {
                if (day != data.dailyPeriod)
                {
                    data.dailyPeriod = day;
                    data.dailyQuests = NewDaily(day);
                    data.processedEventIds = Array.Empty<string>();
                }
                if (week != data.weeklyPeriod)
                {
                    data.weeklyPeriod = week;
                    data.defeatedRegions = Array.Empty<int>();
                }
            });
        }

        /// <summary>Flush the UTC high-water mark on application pause/quit as well as progress mutations.</summary>
        public bool Save()
        {
            if (IsReadOnly) return false;
            ObserveClock();
            data.lastSeenUtcTicks = observedUtcTicks;
            string temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(data, true));
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                bool existing = File.Exists(path);
                bool existingValid = false;
                if (existing)
                {
                    existingValid = TryRead(path, out _, out _, out _);
                    if (preserveMainBeforeWrite || !existingValid)
                    {
                        // Keep the exact invalid bytes for manual recovery; never promote corruption into .bak.
                        string quarantine = path + ".corrupt-" + Guid.NewGuid().ToString("N");
                        File.Copy(path, quarantine, false);
                    }
                    try
                    {
                        File.Replace(temporary, path, existingValid && !preserveMainBeforeWrite ? path + ".bak" : null);
                    }
                    catch (PlatformNotSupportedException) { ReplaceFallback(temporary, existingValid); }
                    catch (NotSupportedException) { ReplaceFallback(temporary, existingValid); }
                }
                else File.Move(temporary, path);
                preserveMainBeforeWrite = false;
                data.upgradedFromPreviousVersion = false;
                LastSaveError = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
            {
                LastSaveError = "저장하지 못했습니다: " + exception.Message;
                return false;
            }
        }

        void ReplaceFallback(string temporary, bool existingValid)
        {
            // Filesystems without atomic replacement retain a verified .bak across the remove/move window.
            if (existingValid && !preserveMainBeforeWrite) File.Copy(path, path + ".bak", true);
            File.Delete(path);
            File.Move(temporary, path);
        }

        bool PeriodsAreCurrent() => !IsReadOnly && data.dailyPeriod == CurrentDay() && data.weeklyPeriod == CurrentWeek();

        bool Commit(Action change)
        {
            if (IsReadOnly) return false;
            if (data.economy.revision == long.MaxValue)
            { LastSaveError = "저장 리비전 한도를 초과했습니다."; return false; }
            SaveData previous = Clone(data);
            change();
            // All profile changes advance revision so stale drafts cannot erase a world reward.
            data.economy.revision = previous.economy.revision + 1;
            if (!Save()) { data = previous; RebuildEntries(); return false; }
            RebuildEntries(); NotifyChanged(); return true;
        }

        void Load()
        {
            bool mainExists = File.Exists(path), backupExists = File.Exists(path + ".bak");
            if (!mainExists && !backupExists)
            {
                data = NewData(); LoadStatus = M4LoadStatus.NewSave; LoadMessage = "새 진행 파일을 생성했습니다."; return;
            }
            if (mainExists)
            {
                if (TryRead(path, out SaveData main, out string mainError, out bool newer, out bool mainIoError))
                {
                    data = main; LoadStatus = M4LoadStatus.Loaded; LoadMessage = "진행 파일을 불러왔습니다."; return;
                }
                if (mainIoError)
                {
                    // 일시적인 파일 잠금은 손상이 아니다. 오래된 백업으로 최신 진행을 덮어쓰지 않도록 재로드까지 잠근다.
                    data = data ?? NewData(); IsReadOnly = true; LoadStatus = M4LoadStatus.IoErrorReadOnly;
                    LoadMessage = "본문을 읽지 못했습니다: " + mainError + " 원본을 보존하고 저장을 잠갔습니다. 접근 문제를 해결한 뒤 다시 읽으세요."; return;
                }
                // Do not downgrade a future-version main save through an older backup.
                if (newer)
                {
                    data = NewData(); IsReadOnly = true; LoadStatus = M4LoadStatus.UnsupportedVersionReadOnly;
                    LoadMessage = mainError + " 원본을 보존하고 저장을 잠갔습니다."; return;
                }
            }
            if (backupExists)
            {
                if (TryRead(path + ".bak", out SaveData backup, out string backupError, out _, out bool backupIoError))
                {
                    data = backup; preserveMainBeforeWrite = mainExists;
                    LoadStatus = M4LoadStatus.RecoveredBackup;
                    LoadMessage = "정상 백업을 불러왔습니다. 손상된 본문은 다음 저장 시 별도 보존합니다."; return;
                }
                if (backupIoError)
                {
                    data = data ?? NewData(); IsReadOnly = true; LoadStatus = M4LoadStatus.IoErrorReadOnly;
                    LoadMessage = "백업을 읽지 못했습니다: " + backupError + " 원본을 보존하고 저장을 잠갔습니다. 접근 문제를 해결한 뒤 다시 읽으세요."; return;
                }
            }
            data = NewData(); IsReadOnly = true; LoadStatus = M4LoadStatus.CorruptReadOnly;
            LoadMessage = "본문과 백업에서 유효한 진행 데이터를 읽지 못했습니다. 원본을 보존하고 저장을 잠갔습니다.";
        }

        bool TryRead(string candidate, out SaveData result, out string error, out bool newer)
            => TryRead(candidate, out result, out error, out newer, out _);

        bool TryRead(string candidate, out SaveData result, out string error, out bool newer, out bool ioError)
        {
            result = null; error = null; newer = false; ioError = false;
            try
            {
                string json = File.ReadAllText(candidate);
                if (json.Length > 1024 * 1024) throw new FormatException("Save file is too large.");
                // JsonUtility.FromJson may construct missing nested fields with valid-looking defaults.
                // Invalid sentinels reject absent economy fields and absent/partial v3 protagonist data.
                result = new SaveData
                {
                    economy = new EconomySaveData { balances = null, owned = null, pities = null, revision = -1 },
                    explorer = new ExplorerSaveData { choice = (ExplorerChoice)(-1), element = (ElementType)(-1) }
                };
                JsonUtility.FromJsonOverwrite(json, result);
                if (result != null && result.version > SaveVersion)
                { newer = true; error = "더 새로운 저장 버전입니다."; return false; }
                if (result != null && result.version == 1)
                {
                    if (result.coins < 0)
                        throw new FormatException("Invalid legacy currency data.");
                    // JsonUtility can materialize missing nested objects; v1 coins are authoritative.
                    result.economy = new EconomySaveData();
                    result.economy.balances[(int)CurrencyType.Lumen] = result.coins;
                    result.coins = 0;
                }
                if (result != null && (result.version == 1 || result.version == 2))
                {
                    // Earlier saves have no protagonist selection. Preserve their complete economy and world progress.
                    result.explorer = new ExplorerSaveData();
                    result.version = SaveVersion;
                    result.upgradedFromPreviousVersion = true;
                }
                if (!ValidData(result)) throw new FormatException("Save fields or period identifiers are invalid.");
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { error = exception.Message; result = null; ioError = true; return false; }
            catch (Exception exception) when (exception is ArgumentException || exception is FormatException)
            { error = exception.Message; result = null; return false; }
        }

        bool ValidData(SaveData value)
        {
            if (value == null || value.version != SaveVersion || value.resetHourKst != resetHourKst || value.coins != 0 || value.economy == null || !value.economy.Validate(out _) ||
                value.explorer == null || !value.explorer.Validate(out _) ||
                value.lastSeenUtcTicks <= 0 || value.lastSeenUtcTicks > DateTime.MaxValue.Ticks ||
                !ParseDate(value.dailyPeriod, out DateTime daily) || !ParseDate(value.weeklyPeriod, out DateTime week) ||
                week.DayOfWeek != DayOfWeek.Monday || value.dailyQuests == null || value.dailyQuests.Length != 4 ||
                value.defeatedRegions == null || value.processedEventIds == null || value.processedEventIds.Length > 4) return false;
            // Reject internally inconsistent timestamps instead of silently advancing or rerolling a damaged save.
            DateTime dateAtSave = ResetDate(value.lastSeenUtcTicks);
            if (daily > dateAtSave || week > dateAtSave || daily < week || daily >= week.AddDays(7)) return false;
            QuestData[] expected = NewDaily(value.dailyPeriod);
            var seen = new HashSet<string>();
            for (int i = 0; i < 4; i++)
            {
                QuestData quest = value.dailyQuests[i];
                if (quest == null || quest.id != expected[i].id || quest.state < 0 || quest.state > (int)M4QuestState.Claimed || !seen.Add(quest.id)) return false;
            }
            if (value.defeatedRegions.Length > 5 || value.defeatedRegions.Distinct().Count() != value.defeatedRegions.Length ||
                value.defeatedRegions.Any(region => !ValidRegion((M4RegionId)region))) return false;
            return value.processedEventIds.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 256) &&
                value.processedEventIds.Distinct().Count() == value.processedEventIds.Length;
        }

        SaveData NewData() => new SaveData
        {
            version = SaveVersion, resetHourKst = resetHourKst, lastSeenUtcTicks = observedUtcTicks,
            dailyPeriod = CurrentDay(), weeklyPeriod = CurrentWeek(), coins = 0, economy = new EconomySaveData(), explorer = new ExplorerSaveData(),
            dailyQuests = NewDaily(CurrentDay()), defeatedRegions = Array.Empty<int>(), processedEventIds = Array.Empty<string>()
        };
        static SaveData Clone(SaveData source) => JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(source));
        void RebuildEntries() => dailyQuests = Array.AsReadOnly(data.dailyQuests.Select(q => new M4DailyQuest(Definition(q.id), (M4QuestState)q.state)).ToArray());
        int FindQuest(string id) => Array.FindIndex(data.dailyQuests, quest => quest.id == id);
        static M4QuestDefinition Definition(string id) => definitions.First(definition => definition.Id == id);
        static bool ValidRegion(M4RegionId region) => (int)region >= 0 && (int)region < 5;
        static bool ValidKind(M4ObjectiveKind kind) => kind >= M4ObjectiveKind.Survey && kind <= M4ObjectiveKind.FieldBoss;
        long ReadClockTicks()
        {
            DateTime value = utcClock();
            return value.Kind == DateTimeKind.Local ? value.ToUniversalTime().Ticks : value.Ticks;
        }
        void ObserveClock() => observedUtcTicks = Math.Max(observedUtcTicks, ReadClockTicks());
        DateTime ResetDate(long utcTicks)
        {
            long offset = (9L - resetHourKst) * TimeSpan.TicksPerHour;
            long ticks = Math.Max(0L, Math.Min(DateTime.MaxValue.Ticks, utcTicks + offset));
            return new DateTime(ticks, DateTimeKind.Unspecified).Date;
        }
        string CurrentDay() => ResetDate(observedUtcTicks).ToString(DateFormat, CultureInfo.InvariantCulture);
        string CurrentWeek()
        {
            DateTime date = ResetDate(observedUtcTicks);
            int sinceMonday = ((int)date.DayOfWeek + 6) % 7;
            return date.AddDays(-sinceMonday).ToString(DateFormat, CultureInfo.InvariantCulture);
        }
        static bool ParseDate(string value, out DateTime result) => DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        static QuestData[] NewDaily(string day)
        {
            ParseDate(day, out DateTime date);
            int dayIndex = (int)(date.Ticks / TimeSpan.TicksPerDay % 5);
            var result = new QuestData[4];
            // Four kinds in four regions daily; all 20 definitions appear over a deterministic five-day cycle.
            for (int kind = 0; kind < 4; kind++)
            {
                int region = (dayIndex + kind * 2) % 5;
                result[kind] = new QuestData { id = definitions[region * 4 + kind].Id, state = (int)M4QuestState.Offered };
            }
            return result;
        }
        static IReadOnlyList<M4QuestDefinition> BuildDefinitions()
        {
            string[] regions = { "아그니아", "텔루나", "자피르", "그라니테", "볼트하임" };
            string[] requests = { "의 탐사 표식을 확인해 주세요.", "의 원소 필드 퍼즐을 해결해 주세요.", "의 도전방을 완료해 주세요.", "의 이번 주 지역 수호자 격퇴 기록을 확인해 주세요." };
            var values = new List<M4QuestDefinition>();
            for (int region = 0; region < 5; region++)
                for (int kind = 0; kind < 4; kind++)
                    values.Add(new M4QuestDefinition("m4." + ((M4RegionId)region).ToString().ToLowerInvariant() + "." + ((M4ObjectiveKind)kind).ToString().ToLowerInvariant(),
                        (M4RegionId)region, (M4ObjectiveKind)kind, regions[region] + requests[kind]));
            return values.AsReadOnly();
        }
    }
}
