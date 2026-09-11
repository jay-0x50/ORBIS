using System;
using Orbis.M1;

namespace Orbis.M2
{
    public enum ChallengeState { Ready, Running, Completed, Failed }

    /// <summary>증발 성공 여부로만 목표를 제압하는 제한 시간 도전. 적 HP/사망 시스템은 포함하지 않는다.</summary>
    public sealed class ChallengeRoomModel
    {
        private readonly RewardInventory inventory;
        private readonly string rewardId;
        private readonly int rewardAmount;
        private readonly bool[] defeated;
        public ReactionType RequiredReaction { get; }
        public ChallengeState State { get; private set; }
        public float TimeLimit { get; }
        public float RemainingTime { get; private set; }
        public int RequiredCount => defeated.Length;
        public int DefeatedCount { get; private set; }
        public bool RewardClaimed => inventory.HasClaimed(rewardId);
        public event Action Changed;

        // 미정 M2 기본 진행안: 60초 안에 표적 3개에 증발 1회씩, 강화 재료 3개를 완료 후 F로 수령.
        public ChallengeRoomModel(RewardInventory rewards, string id, int targetCount = 3,
            float timeLimit = 60f, int amount = 3,
            ReactionType requiredReaction = ReactionType.Vaporize, bool allowReplayAfterClaim = false)
        {
            inventory = rewards ?? throw new ArgumentNullException(nameof(rewards));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A stable reward ID is required.", nameof(id));
            if (targetCount <= 0) throw new ArgumentOutOfRangeException(nameof(targetCount));
            if (float.IsNaN(timeLimit) || float.IsInfinity(timeLimit) || timeLimit <= 0f)
                throw new ArgumentOutOfRangeException(nameof(timeLimit));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (requiredReaction == ReactionType.None) throw new ArgumentException("Challenge needs a reaction.", nameof(requiredReaction));
            RequiredReaction = requiredReaction;
            rewardId = id;
            rewardAmount = amount;
            TimeLimit = timeLimit;
            RemainingTime = timeLimit;
            defeated = new bool[targetCount];
            if (RewardClaimed && !allowReplayAfterClaim)
            {
                for (int i = 0; i < defeated.Length; i++) defeated[i] = true;
                DefeatedCount = defeated.Length;
                State = ChallengeState.Completed;
            }
        }

        public bool IsDefeated(int index) => index >= 0 && index < defeated.Length && defeated[index];

        public bool TryStart()
        {
            if (State != ChallengeState.Ready && State != ChallengeState.Failed) return false;
            Array.Clear(defeated, 0, defeated.Length);
            DefeatedCount = 0;
            RemainingTime = TimeLimit;
            State = ChallengeState.Running;
            Changed?.Invoke();
            return true;
        }

        public bool RegisterReaction(int targetIndex, ReactionType reaction)
        {
            if (State != ChallengeState.Running || reaction != RequiredReaction ||
                targetIndex < 0 || targetIndex >= defeated.Length || defeated[targetIndex]) return false;
            defeated[targetIndex] = true;
            DefeatedCount++;
            if (DefeatedCount == RequiredCount) State = ChallengeState.Completed;
            Changed?.Invoke();
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (State != ChallengeState.Running || deltaTime == 0f) return;
            RemainingTime = Math.Max(0f, RemainingTime - deltaTime);
            if (RemainingTime <= 0f)
            {
                State = ChallengeState.Failed;
                Changed?.Invoke();
            }
        }

        public bool TryClaimReward()
        {
            if (State != ChallengeState.Completed || !inventory.TryGrant(rewardId, rewardAmount)) return false;
            Changed?.Invoke();
            return true;
        }

        public void ResetProgress()
        {
            Array.Clear(defeated, 0, defeated.Length);
            DefeatedCount = 0;
            RemainingTime = TimeLimit;
            State = ChallengeState.Ready;
            // 연습 재도전은 허용하되 세션 수령 기록은 초기화하지 않는다.
            Changed?.Invoke();
        }
    }
}
