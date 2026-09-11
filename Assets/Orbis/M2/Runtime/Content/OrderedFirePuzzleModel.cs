using System;
using Orbis.M1;

namespace Orbis.M2
{
    /// <summary>순서대로 화 원소로 점화하는 필드 퍼즐의 독립 상태 모델.</summary>
    public sealed class OrderedFirePuzzleModel
    {
        private readonly RewardInventory inventory;
        private readonly string rewardId;
        private readonly int rewardAmount;
        private readonly bool allowReplayAfterClaim;
        public ElementType RequiredElement { get; }
        public int StatueCount { get; }
        public int LitCount { get; private set; }
        public bool IsUnlocked => LitCount == StatueCount;
        public bool RewardClaimed => inventory.HasClaimed(rewardId);
        public event Action Changed;

        // 기획서 미정 M2 기본값: 석상 3개, 보상 강화 재료 5개.
        public OrderedFirePuzzleModel(RewardInventory rewards, string id, int statueCount = 3, int amount = 5,
            ElementType requiredElement = ElementType.Fire, bool allowReplayAfterClaim = false)
        {
            inventory = rewards ?? throw new ArgumentNullException(nameof(rewards));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A stable reward ID is required.", nameof(id));
            if (statueCount <= 0) throw new ArgumentOutOfRangeException(nameof(statueCount));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (requiredElement == ElementType.None) throw new ArgumentException("Puzzle needs an element.", nameof(requiredElement));
            RequiredElement = requiredElement;
            this.allowReplayAfterClaim = allowReplayAfterClaim;
            rewardId = id;
            rewardAmount = amount;
            StatueCount = statueCount;
            LitCount = RewardClaimed && !allowReplayAfterClaim ? StatueCount : 0;
        }

        public bool IsLit(int statueIndex) => statueIndex >= 0 && statueIndex < LitCount;

        public bool RegisterHit(int statueIndex, ElementType incoming, bool isPropagation = false)
        {
            if (isPropagation || statueIndex < 0 || statueIndex >= StatueCount || IsUnlocked) return false;
            // 기획서 미정 규칙: 오답 순서/잘못된 원소는 진행을 초기화한다.
            // 같은 화 공격 콤보가 이미 켜진 석상에 다시 맞으면 무시하여 순서를 깨지 않는다.
            if (incoming == RequiredElement && statueIndex < LitCount) return false;
            if (incoming != RequiredElement || statueIndex != LitCount)
            {
                bool changed = LitCount != 0;
                LitCount = 0;
                if (changed) Changed?.Invoke();
                return false;
            }
            LitCount++;
            Changed?.Invoke();
            return true;
        }

        public bool TryClaimReward()
        {
            if (!IsUnlocked || !inventory.TryGrant(rewardId, rewardAmount)) return false;
            Changed?.Invoke();
            return true;
        }

        public void ResetProgress()
        {
            // 이미 수령한 상자는 같은 세션에서 열린 상태로 남는다.
            LitCount = RewardClaimed && !allowReplayAfterClaim ? StatueCount : 0;
            Changed?.Invoke();
        }
    }
}
