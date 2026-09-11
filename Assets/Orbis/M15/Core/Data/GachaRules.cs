using System;
using UnityEngine;

namespace Orbis.M15
{
    [CreateAssetMenu(menuName = "Orbis/M15/Gacha Rules")]
    public sealed class GachaRules : ScriptableObject
    {
        // 기획서의 약 94%를 나머지 5.1%/0.6%와 합계 100%가 되도록 94.3%로 정한다.
        public double ThreeStarProbability = .943;
        public double FourStarProbability = .051;
        public double FiveStarProbability = .006;
        // 미정 기본값: 74번째부터 매회 6%p 증가. 증가분은 3성 확률에서 먼저 차감한다.
        public double SoftPityStep = .06;
        public double FeaturedChance = .5;
        public int SoftPityStart = 74;
        public int HardPity = 90;
        public int FourStarHardPity = 10;
        public int ExchangeCost = 160;

        public void Validate()
        {
            CheckProbability(ThreeStarProbability, nameof(ThreeStarProbability));
            CheckProbability(FourStarProbability, nameof(FourStarProbability));
            CheckProbability(FiveStarProbability, nameof(FiveStarProbability));
            CheckProbability(SoftPityStep, nameof(SoftPityStep));
            CheckProbability(FeaturedChance, nameof(FeaturedChance));
            if (Math.Abs(ThreeStarProbability + FourStarProbability + FiveStarProbability - 1d) > 1e-9)
                throw new InvalidOperationException("Base rarity probabilities must sum to one.");
            if (SoftPityStart < 1 || HardPity < SoftPityStart || FourStarHardPity < 1 || ExchangeCost < 1)
                throw new InvalidOperationException("Gacha thresholds and exchange cost must be positive, with soft pity no later than hard pity.");
        }
        private static void CheckProbability(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d || value > 1d)
                throw new InvalidOperationException(name + " must be finite and within [0, 1].");
        }
    }
}
