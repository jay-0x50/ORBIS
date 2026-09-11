using System;
using UnityEngine;

namespace Orbis.M16
{
    [CreateAssetMenu(menuName = "Orbis/M16/Explorer")]
    public sealed class ExplorerDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public ExplorerChoice Choice;
        public WayfarerWeaponDefinition Weapon;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DisplayName))
                throw new InvalidOperationException("An explorer requires a stable ID and display name.");
            if (Choice != ExplorerChoice.Stella && Choice != ExplorerChoice.Polaris)
                throw new InvalidOperationException("An explorer definition must describe Stella or Polaris.");
            if (Weapon == null) throw new InvalidOperationException("An explorer requires the shared Wayfarer weapon.");
            Weapon.Validate();
        }
    }
}