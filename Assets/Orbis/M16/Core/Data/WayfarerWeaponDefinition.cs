using System;
using UnityEngine;

namespace Orbis.M16
{
    [CreateAssetMenu(menuName = "Orbis/M16/Wayfarer Weapon")]
    public sealed class WayfarerWeaponDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string EnglishName;
        // 기획 미정 기본값: 동일 장검의 기본 공격력은 두 탐구자 모두 10.
        public float BaseAttack = 10f;
        public string WeaponType = "Longsword";

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(EnglishName))
                throw new InvalidOperationException("The Wayfarer weapon requires an ID and both names.");
            if (WeaponType != "Longsword" || float.IsNaN(BaseAttack) || float.IsInfinity(BaseAttack) || BaseAttack <= 0f)
                throw new InvalidOperationException("The Wayfarer weapon must be a longsword with finite positive base attack.");
        }
    }
}