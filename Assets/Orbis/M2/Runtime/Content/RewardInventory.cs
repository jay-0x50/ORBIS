using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis.M2
{
    /// <summary>플레이 세션의 강화 재료와 수령 기록. 저장 파일/일반 인벤토리 시스템은 포함하지 않는다.</summary>
    public sealed class RewardInventory
    {
        private readonly HashSet<string> claimedRewards = new HashSet<string>(StringComparer.Ordinal);
        public static RewardInventory Session { get; private set; } = new RewardInventory();
        public int EnhancementMaterials { get; private set; }
        public event Action Changed;

        public bool HasClaimed(string rewardId) => !string.IsNullOrEmpty(rewardId) && claimedRewards.Contains(rewardId);

        public bool TryGrant(string rewardId, int amount)
        {
            if (string.IsNullOrWhiteSpace(rewardId)) throw new ArgumentException("A stable reward ID is required.", nameof(rewardId));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (claimedRewards.Contains(rewardId)) return false;
            int nextAmount = checked(EnhancementMaterials + amount);
            claimedRewards.Add(rewardId);
            EnhancementMaterials = nextAmount;
            Changed?.Invoke();
            return true;
        }

        // 씬 재로드/R 초기화는 수령 기록을 지우지 않는다. 새 Play 세션에서만 초기화한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void BeginPlaySession() => Session = new RewardInventory();
    }
}
