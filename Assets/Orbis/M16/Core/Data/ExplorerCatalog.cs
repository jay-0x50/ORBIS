using System;
using System.Collections.Generic;
using Orbis.M1;
using UnityEngine;

namespace Orbis.M16
{
    [CreateAssetMenu(menuName = "Orbis/M16/Explorer Catalog")]
    public sealed class ExplorerCatalog : ScriptableObject
    {
        public ExplorerDefinition[] Explorers = Array.Empty<ExplorerDefinition>();
        public ExplorerSkillDefinition[] Skills = Array.Empty<ExplorerSkillDefinition>();
        // 기획 미정 공통 기본값: 원소를 바꾸어도 같은 2초 쿨다운을 공유한다.
        public float SharedSkillCooldown = 2f;
        // 기획 미정 초기 원소: 신규 탐구자는 화 원소로 시작한다.
        public ElementType DefaultElement = ElementType.Fire;

        public void Validate()
        {
            if (Explorers == null || Explorers.Length != 2 || Skills == null || Skills.Length != 5)
                throw new InvalidOperationException("The explorer catalog requires two explorers and five elemental skills.");
            if (float.IsNaN(SharedSkillCooldown) || float.IsInfinity(SharedSkillCooldown) || SharedSkillCooldown < 0f ||
                !Enum.IsDefined(typeof(ElementType), DefaultElement) || DefaultElement == ElementType.None)
                throw new InvalidOperationException("Explorer cooldown/default element is invalid.");
            var choices = new HashSet<ExplorerChoice>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            WayfarerWeaponDefinition sharedWeapon = null;
            foreach (var explorer in Explorers)
            {
                if (explorer == null) throw new InvalidOperationException("The catalog contains a missing explorer.");
                explorer.Validate();
                if (!choices.Add(explorer.Choice) || !ids.Add(explorer.Id))
                    throw new InvalidOperationException("Explorer choices and IDs must be unique.");
                if (sharedWeapon == null) sharedWeapon = explorer.Weapon;
                else if (sharedWeapon != explorer.Weapon)
                    throw new InvalidOperationException("Stella and Polaris must reference the same Wayfarer weapon asset.");
            }
            var elements = new HashSet<ElementType>();
            foreach (var skill in Skills)
            {
                if (skill == null) throw new InvalidOperationException("The catalog contains a missing explorer skill.");
                skill.Validate();
                if (!elements.Add(skill.Element) || !ids.Add(skill.Id))
                    throw new InvalidOperationException("Explorer skill IDs and elements must be unique.");
            }
        }

        public ExplorerDefinition Get(ExplorerChoice choice)
        {
            if (choice != ExplorerChoice.Stella && choice != ExplorerChoice.Polaris)
                throw new ArgumentOutOfRangeException(nameof(choice), "Select an explorer before requesting its definition.");
            foreach (var explorer in Explorers)
                if (explorer != null && explorer.Choice == choice) return explorer;
            throw new InvalidOperationException("The selected explorer is missing from the catalog: " + choice);
        }
        public ExplorerSkillDefinition Skill(ElementType element)
        {
            if (!Enum.IsDefined(typeof(ElementType), element) || element == ElementType.None)
                throw new ArgumentOutOfRangeException(nameof(element));
            foreach (var skill in Skills)
                if (skill != null && skill.Element == element) return skill;
            throw new InvalidOperationException("The explorer skill is missing: " + element);
        }
    }
}