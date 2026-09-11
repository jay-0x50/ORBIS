using UnityEngine;

namespace Orbis.M0
{
    /// <summary>M0 피격 판정 검증용 카운터. 체력/사망/AI 시스템은 포함하지 않는다.</summary>
    [DisallowMultipleComponent]
    public sealed class TrainingDummy : MonoBehaviour
    {
        public int HitCount { get; private set; }
        public int LastComboStep { get; private set; }

        public void RegisterHit(int comboStep)
        {
            HitCount++;
            LastComboStep = comboStep;
        }

        public void ResetHits()
        {
            HitCount = 0;
            LastComboStep = 0;
        }
    }
}
