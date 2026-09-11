using System;
using Orbis.M1;
using UnityEngine;

namespace Orbis.M16
{
    [CreateAssetMenu(menuName = "Orbis/M16/Explorer Skill")]
    public sealed class ExplorerSkillDefinition : ScriptableObject
    {
        public string Id;
        public ElementType Element;
        // 기획 미정 공통 기본값. M1.6의 다섯 스킬은 원소만 다르고 동일한 근접 판정/동작을 사용한다.
        public float Damage = 25f;
        public float Range = 2.5f;
        public float Radius = .85f;
        public float Duration = .5f;
        public float HitTime = .25f;
        public float AttachmentDuration = 4f;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) || !Enum.IsDefined(typeof(ElementType), Element) || Element == ElementType.None)
                throw new InvalidOperationException("An explorer skill requires an ID and one of the five elements.");
            if (!Finite(Damage) || Damage < 0f || !Finite(Range) || Range <= 0f || !Finite(Radius) || Radius <= 0f ||
                !Finite(Duration) || Duration <= 0f || !Finite(HitTime) || HitTime < 0f || HitTime > Duration ||
                !Finite(AttachmentDuration) || AttachmentDuration <= 0f)
                throw new InvalidOperationException("Explorer skill values must be finite, with positive ranges/durations and a hit time within the action.");
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}