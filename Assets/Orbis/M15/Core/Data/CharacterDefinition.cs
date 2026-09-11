using System;
using Orbis.M1;
using UnityEngine;

namespace Orbis.M15
{
    [CreateAssetMenu(menuName = "Orbis/M15/Character")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public ElementType Element;
        public CharacterRarity Rarity = CharacterRarity.Three;
        public string Role;
        public WeaponType Weapon;
        public bool IsStarter;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DisplayName))
                throw new InvalidOperationException("A character requires a stable ID and display name.");
            if (!Enum.IsDefined(typeof(CharacterRarity), Rarity) || !Enum.IsDefined(typeof(WeaponType), Weapon) ||
                !Enum.IsDefined(typeof(ElementType), Element) || Element == ElementType.None)
                throw new InvalidOperationException("Invalid character rarity, weapon or element: " + Id);
            if (IsStarter && Rarity != CharacterRarity.Three)
                throw new InvalidOperationException("Starter characters must have three-star rarity: " + Id);
        }
    }
}
