using System;
using System.Collections.Generic;
using Orbis.M0;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbis.M1
{
    /// <summary>
    /// Four independent party states share the M0 movement/camera avatar in this milestone.
    /// Switches preserve world position, facing, camera orbit and vertical velocity.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    [DisallowMultipleComponent]
    public sealed class PartyManager : MonoBehaviour
    {
        public const int Capacity = 4;
        // User-confirmed M1 interpretation of the design's 3–5 seconds: four seconds from switching.
        public const float FieldRetentionSeconds = 4f;
        // Spec 03: no normal cooldown; only an elemental burst creates this one-second lock.
        public const float BurstSwitchDelay = 1f;

        private readonly InputAction[] switchActions = new InputAction[Capacity];
        private InputActionMap actionMap;
        private IReadOnlyList<PartyMember> members = Array.AsReadOnly(Array.Empty<PartyMember>());
        private M0Input input;
        private PlayerMotor motor;
        private BasicAttackCombo combat;
        private ElementalReactionManager reactions;
        private float switchUnlockTime;

        public IReadOnlyList<PartyMember> Members => members;
        public PartyMember PermanentMember { get; private set; }
        public int ActiveIndex { get; private set; } = -1;
        public PartyMember ActiveMember => ActiveIndex >= 0 ? members[ActiveIndex] : null;
        public float SwitchLockRemaining => Mathf.Max(0f, switchUnlockTime - Time.time);
        public event Action<PartyMember> ActiveMemberChanged;
        public event Action<PartyMember> ActiveElementChanged;

        private void Awake()
        {
            actionMap = new InputActionMap("M1 Party");
            for (int i = 0; i < Capacity; i++)
                switchActions[i] = actionMap.AddAction("Slot" + (i + 1), InputActionType.Button,
                    "<Keyboard>/" + (i + 1));
        }

        private void OnEnable() => actionMap?.Enable();
        private void OnDisable() => actionMap?.Disable();
        private void OnDestroy() => actionMap?.Dispose();

        public void Configure(M0Input playerInput, PlayerMotor playerMotor, BasicAttackCombo attackCombo,
            ElementalReactionManager reactionManager, PartyMember[] party)
        {
            if (playerInput == null || playerMotor == null || attackCombo == null || reactionManager == null)
                throw new ArgumentException("The party requires input, movement, combat and reactions.");
            PartyMember permanent = ValidateMembers(party);

            combat?.CancelAttack();
            foreach (PartyMember old in members) old.Actor.IsOnField = false;
            input = playerInput;
            motor = playerMotor;
            combat = attackCombo;
            reactions = reactionManager;
            members = Array.AsReadOnly((PartyMember[])party.Clone());
            PermanentMember = permanent;
            ResetParty();
        }

        private PartyMember ValidateMembers(PartyMember[] party)
        {
            if (party == null || party.Length != Capacity)
                throw new ArgumentException("The party requires exactly four members.", nameof(party));
            var identities = new HashSet<string>();
            var actors = new HashSet<ElementalActor>();
            PartyMember permanent = null;
            for (int i = 0; i < party.Length; i++)
            {
                PartyMember member = party[i];
                if (member == null || member.Actor == null || !actors.Add(member.Actor) ||
                    string.IsNullOrWhiteSpace(member.Actor.SourceId) || !identities.Add(member.Actor.SourceId))
                    throw new ArgumentException("Members must have distinct actors and source identities.", nameof(party));
                if (!member.IsPermanent) continue;
                if (i != 0 || permanent != null)
                    throw new ArgumentException("A single permanent member must occupy slot one.", nameof(party));
                permanent = member;
            }
            if (PermanentMember != null && !ReferenceEquals(permanent, PermanentMember))
                throw new InvalidOperationException("The registered permanent member cannot be removed, replaced or moved.");
            return permanent;
        }

        public void RegisterPermanentMember(PartyMember member)
        {
            if (member == null || !member.IsPermanent)
                throw new ArgumentException("Register a permanent party member.", nameof(member));
            if (PermanentMember != null)
            {
                if (ReferenceEquals(PermanentMember, member)) return;
                throw new InvalidOperationException("A permanent member is already registered.");
            }
            if (members.Count != Capacity) throw new InvalidOperationException("Configure the party before registering its permanent member.");
            var replacement = new PartyMember[Capacity];
            for (int i = 0; i < Capacity; i++) replacement[i] = i == 0 ? member : members[i];
            ValidateMembers(replacement);
            combat.CancelAttack();
            members[0].Actor.IsOnField = false;
            members = Array.AsReadOnly(replacement);
            PermanentMember = member;
            member.Actor.IsOnField = ActiveIndex == 0;
            if (ActiveIndex == 0) { SetActive(0); motor.RefreshPresentation(); }
        }

        /// <summary>Refresh element colors without switching actors, cancelling a cast or replacing its model.</summary>
        public void RefreshActiveElement()
        {
            if (ActiveMember != null) ActiveElementChanged?.Invoke(ActiveMember);
        }

        private void Update()
        {
            if (input == null || !input.GameplayEnabled || ActiveMember == null) return;
            if (input.ResetPressed)
            {
                ResetParty();
                return;
            }
            // Unspecified simultaneous input policy: the lowest pressed slot wins this frame.
            for (int i = 0; i < Capacity; i++)
            {
                if (!switchActions[i].WasPressedThisFrame()) continue;
                TrySwitch(i);
                break;
            }
        }

        public bool TrySwitch(int slot)
        {
            if (!isActiveAndEnabled || slot < 0 || slot >= members.Count ||
                slot == ActiveIndex || SwitchLockRemaining > 0f || ActiveMember == null ||
                motor.State == PlayerActionState.Burst || motor.State == PlayerActionState.Hurt || motor.State == PlayerActionState.Dead)
                return false;

            reactions.RetainSourceAttachments(ActiveMember.Actor.SourceId, FieldRetentionSeconds);
            // Instant switching cancels the outgoing combo and its queued hit. A new attack starts at hit one.
            combat.CancelAttack();
            SetActive(slot);
            motor.RefreshPresentation();
            return true;
        }

        /// <summary>Integration hook for a future burst; this does not implement a burst or its energy system.</summary>
        public void NotifyBurstUsed()
        {
            if (ActiveMember != null) switchUnlockTime = Time.time + BurstSwitchDelay;
        }

        public void ResetParty()
        {
            switchUnlockTime = 0f;
            combat?.CancelAttack();
            if (members.Count == 0) return;
            SetActive(0);
            motor?.RefreshPresentation();
        }

        private void SetActive(int slot)
        {
            ActiveIndex = slot;
            // Keep every actor enabled: inactive members still age their own attachments and shields.
            for (int i = 0; i < members.Count; i++) members[i].Actor.IsOnField = i == slot;
            ActiveMemberChanged?.Invoke(ActiveMember);
        }
    }
}
