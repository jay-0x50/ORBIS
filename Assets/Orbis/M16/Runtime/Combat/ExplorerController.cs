using System;
using System.Collections.Generic;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Orbis.M3;
using Orbis.M4;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbis.M16
{
    /// <summary>Story protagonist adapter on the existing shared pawn; no extra controller, health pool or animator FSM.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-70)] // Input -100, party switching -80, skill state -70, shared motor -50.
    public sealed class ExplorerController : MonoBehaviour
    {
        private static readonly ElementType[] Cycle = { ElementType.Fire, ElementType.Water, ElementType.Wind, ElementType.Rock, ElementType.Lightning };
        private readonly ExplorerSkillSequence sequence = new ExplorerSkillSequence();
        private readonly HashSet<TrainingDummy> hitTargets = new HashSet<TrainingDummy>();
        private M4SceneBootstrap scene;
        private PartyManager party;
        private PartyMember member;
        private PlayerMotor motor;
        private M0Input input;
        private ExplorationMotor traversal;
        private BasicAttackCombo combat;
        private ElementalReactionManager manager;
        private ExplorerCatalog catalog;
        private bool subscribed, changingElement;
        private float hurtRemaining;
        // 기획 미정 피격 경직 기본값 .25초. 피해는 기존 실드 흡수 후 실제 HP 피해만 사용한다.
        private const float HurtSeconds = .25f;
        public ExplorerDefinition Definition { get; private set; }
        public ElementalActor Actor => member?.Actor;
        public ElementType SelectedElement => Actor != null ? Actor.Element : ElementType.None;
        public ElementType CurrentCastElement => sequence.CurrentCast?.Element ?? ElementType.None;
        public bool IsCasting => sequence.IsCasting;
        public float CooldownRemaining => sequence.CooldownRemaining;
        public bool AutoTick { get; set; } = true;
        public event Action<ElementType> ElementChanged;
        public event Action<ExplorerCast> SkillStarted;
        public event Action<ExplorerCast> SkillHit;

        public void Configure(M4SceneBootstrap owner, ExplorerDefinition definition, ExplorerCatalog definitions)
        {
            if (owner == null || definition == null || definitions == null)
                throw new ArgumentNullException("An explorer requires its scene, definition and catalog.");
            if (scene != null) throw new InvalidOperationException("An explorer controller can only be configured once.");
            definitions.Validate(); definition.Validate();
            if (definitions.Get(definition.Choice) != definition || owner.Party?.PermanentMember == null ||
                owner.Party.PermanentMember.Actor.SourceId != definition.Id)
                throw new InvalidOperationException("Register the selected explorer in the permanent party slot before configuring combat.");
            scene = owner; Definition = definition; catalog = definitions;
            party = scene.Party; member = party.PermanentMember; manager = scene.Manager;
            traversal = scene.Traversal; motor = traversal.GetComponent<PlayerMotor>();
            combat = motor.GetComponent<BasicAttackCombo>(); input = motor.GetComponent<M0Input>();
            member.BasicAttackDamage = definition.Weapon.BaseAttack;
            sequence.HitReady += ApplySkillHit;
            sequence.Ended += OnCastEnded;
            Subscribe();
        }

        private void Update()
        {
            if (scene == null) return;
            // First age the existing cast. A new G press starts at t=0, never one frame into its wind-up.
            if (AutoTick) Tick(Time.deltaTime);
            if (input == null || !input.GameplayEnabled) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                int current = Array.IndexOf(Cycle, SelectedElement);
                TryChangeElement(Cycle[(current + 1) % Cycle.Length]);
            }
            if (keyboard.gKey.wasPressedThisFrame) TryCastSkill();
        }

        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (scene == null) return;
            if (sequence.IsCasting && (party.ActiveMember != member || !Actor.IsOnField ||
                input == null || !input.GameplayEnabled || motor.State != PlayerActionState.Skill ||
                !motor.IsGrounded || traversal.Mode != ExplorationMode.Locomotion)) CancelCast();
            sequence.Tick(deltaTime); // Also runs while a companion is active; switching never resets cooldown.
            if (sequence.IsCasting) motor.SetActionNormalizedTime(PlayerActionState.Skill, sequence.NormalizedTime);
            if (hurtRemaining > 0f)
            {
                hurtRemaining = Mathf.Max(0f, hurtRemaining - deltaTime);
                if (hurtRemaining <= 0f) motor.EndAction(PlayerActionState.Hurt);
            }
        }

        public bool TryChangeElement(ElementType element)
        {
            if (!CanControlExplorer() || changingElement || !Enum.IsDefined(typeof(ElementType), element) ||
                element == ElementType.None || element == SelectedElement ||
                motor.State == PlayerActionState.Burst || motor.State == PlayerActionState.Hurt || motor.State == PlayerActionState.Dead)
                return false;
            changingElement = true;
            try
            {
                // Persist before changing gameplay. Existing skill snapshot/cooldown/aura/shield remain intact.
                if (!scene.Progress.TrySetExplorerElement(element)) return false;
                Actor.SetElement(element);
                party.RefreshActiveElement();
                ElementChanged?.Invoke(element);
                return true;
            }
            finally { changingElement = false; }
        }

        public bool TryCastSkill()
        {
            if (!CanControlExplorer() || !motor.IsGrounded || traversal.Mode != ExplorationMode.Locomotion ||
                !motor.CanBeginAction(PlayerActionState.Skill) || sequence.IsCasting || sequence.CooldownRemaining > 0f)
                return false;
            ExplorerSkillDefinition skill = catalog.Skill(SelectedElement);
            catalog.Validate(); skill.Validate();
            if (!motor.TryBeginAction(PlayerActionState.Skill)) return false;
            if (!sequence.TryStart(skill, catalog.SharedSkillCooldown)) { motor.EndAction(PlayerActionState.Skill); return false; }
            hitTargets.Clear();
            SkillStarted?.Invoke(sequence.CurrentCast);
            return true;
        }

        private bool CanControlExplorer() => isActiveAndEnabled && scene != null && member != null &&
            Actor != null && Actor.isActiveAndEnabled && Actor.IsOnField && party.ActiveMember == member &&
            input != null && input.GameplayEnabled && !scene.Vitals.IsKnockedOut;

        public void CancelCast() => sequence.Cancel();
        public void RestoreCooldown(float remaining) => sequence.RestoreCooldown(remaining);

        private void ApplySkillHit(ExplorerCast cast)
        {
            if (!isActiveAndEnabled || party.ActiveMember != member || !Actor.IsOnField || motor.State != PlayerActionState.Skill) return;
            Vector3 origin = motor.transform.position + Vector3.up * .9f;
            Vector3 end = origin + motor.transform.forward * cast.Range;
            // 공통 시험 스킬: 전방 Range 길이, Radius 두께의 근접 구간. 가까운 표적도 빈틈 없이 검사한다.
            Collider[] overlaps = Physics.OverlapCapsule(origin, end, cast.Radius, 1 << 9, QueryTriggerInteraction.Ignore);
            foreach (Collider candidate in overlaps)
            {
                if (!sequence.IsCasting || !ReferenceEquals(sequence.CurrentCast, cast) || party.ActiveMember != member) break;
                TrainingDummy dummy = candidate.GetComponentInParent<TrainingDummy>();
                if (dummy == null || !dummy.isActiveAndEnabled || hitTargets.Contains(dummy)) continue;
                ElementalActor target = dummy.GetComponent<ElementalActor>();
                if (target == null || target.Team == Actor.Team || !target.IsOnField || !target.isActiveAndEnabled) continue;
                if (Physics.Linecast(origin, candidate.ClosestPoint(origin), 1 << 8, QueryTriggerInteraction.Ignore)) continue;
                hitTargets.Add(dummy);
                manager.Apply(target, cast.Element, Actor, cast.Damage, cast.AttachmentDuration);
            }
            SkillHit?.Invoke(cast);
        }

        private void OnCastEnded()
        {
            hitTargets.Clear();
            if (motor != null) motor.EndAction(PlayerActionState.Skill);
        }
        private void OnMemberChanged(PartyMember active) { CancelCast(); }
        private void OnActionStateChanged(PlayerActionState state)
        {
            if (state != PlayerActionState.Skill && sequence.IsCasting) CancelCast();
        }

        private void OnDamage(ElementDamageEvent damage)
        {
            if (party.ActiveMember == null || damage.Target != party.ActiveMember.Actor || damage.AppliedDamage <= 0f) return;
            CancelCast();
            scene.Presentation.Ultimate.Cancel();
            if (scene.Vitals.IsKnockedOut)
            {
                hurtRemaining = 0f;
                motor.TryBeginAction(PlayerActionState.Dead);
            }
            else
            {
                hurtRemaining = HurtSeconds;
                motor.TryBeginAction(PlayerActionState.Hurt);
            }
        }
        private void OnVitalsChanged()
        {
            // Region travel restores an HP snapshot too; it is not a damage event.
            if (!scene.Vitals.IsKnockedOut) motor.EndAction(PlayerActionState.Dead);
        }
        private void OnUltimateStage(M3UltimateStage stage, ElementType element, Vector3 position)
        {
            if (stage == M3UltimateStage.CloseUp)
            {
                if (motor.State != PlayerActionState.Burst && !motor.TryBeginAction(PlayerActionState.Burst))
                { scene.Presentation.Ultimate.Cancel(); return; }
                CancelCast();
                party.NotifyBurstUsed();
            }
            else if (stage == M3UltimateStage.Finished || stage == M3UltimateStage.Cancelled)
                motor.EndAction(PlayerActionState.Burst);
        }

        private void Subscribe()
        {
            if (subscribed || scene == null || !isActiveAndEnabled) return;
            party.ActiveMemberChanged += OnMemberChanged;
            motor.ActionStateChanged += OnActionStateChanged;
            manager.DamageApplied += OnDamage;
            scene.Vitals.Changed += OnVitalsChanged;
            scene.Presentation.Ultimate.StageChanged += OnUltimateStage;
            subscribed = true;
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (party != null) party.ActiveMemberChanged -= OnMemberChanged;
            if (motor != null) motor.ActionStateChanged -= OnActionStateChanged;
            if (manager != null) manager.DamageApplied -= OnDamage;
            if (scene != null)
            {
                if (scene.Vitals != null) scene.Vitals.Changed -= OnVitalsChanged;
                if (scene.Presentation != null && scene.Presentation.Ultimate != null)
                    scene.Presentation.Ultimate.StageChanged -= OnUltimateStage;
            }
            subscribed = false;
        }
        private void OnEnable() => Subscribe();
        private void OnDisable()
        {
            CancelCast();
            if (scene != null && scene.Presentation != null) scene.Presentation.Ultimate.Cancel();
            hurtRemaining = 0f;
            if (motor != null)
            {
                motor.EndAction(PlayerActionState.Hurt);
                motor.EndAction(PlayerActionState.Burst);
                motor.EndAction(PlayerActionState.Dead);
            }
            Unsubscribe();
        }
        private void OnDestroy()
        {
            Unsubscribe();
            sequence.HitReady -= ApplySkillHit;
            sequence.Ended -= OnCastEnded;
        }
    }
}
