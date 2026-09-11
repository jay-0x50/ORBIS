using System;
using System.Collections.Generic;
using Orbis.M1;
using UnityEngine;

namespace Orbis.M2
{
    /// <summary>실제 증발 피해 이벤트와 제한 시간 도전 모델을 연결한다. F/거리 확인은 씬이 소유한다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-60)] // 플레이어 공격 처리 전에 제한 시간 종료를 반영한다.
    public sealed class ChallengeRoom : MonoBehaviour
    {
        private ElementalReactionManager manager;
        private ElementalActor[] targets = Array.Empty<ElementalActor>();
        private Vector3[] anchors = Array.Empty<Vector3>();
        private Collider[][] colliders = Array.Empty<Collider[]>();
        private bool[][] originalColliderEnabled = Array.Empty<bool[]>();
        private bool subscribed;
        public ChallengeRoomModel Model { get; private set; }
        public bool AutoTick { get; set; } = true;
        public ChallengeState State => Model != null ? Model.State : ChallengeState.Ready;
        public float RemainingTime => Model != null ? Model.RemainingTime : 60f;
        public int DefeatedCount => Model != null ? Model.DefeatedCount : 0;
        public int RequiredCount => Model != null ? Model.RequiredCount : 3;
        public bool RewardClaimed => Model != null && Model.RewardClaimed;
        public event Action Changed;

        public void Configure(ElementalReactionManager reactions, ElementalActor[] challengeTargets,
            RewardInventory inventory, string rewardId = "m2.challenge.vaporize",
            ReactionType requiredReaction = ReactionType.Vaporize, bool allowReplayAfterClaim = false)
        {
            if (reactions == null) throw new ArgumentNullException(nameof(reactions));
            if (challengeTargets == null || challengeTargets.Length != 3)
                throw new ArgumentException("The M2 challenge requires three targets.", nameof(challengeTargets));
            var unique = new HashSet<ElementalActor>();
            foreach (ElementalActor target in challengeTargets)
                if (target == null || !unique.Add(target))
                    throw new ArgumentException("Targets must be non-null and unique.", nameof(challengeTargets));
            Unsubscribe();
            if (Model != null) Model.Changed -= OnModelChanged;
            manager = reactions;
            targets = (ElementalActor[])challengeTargets.Clone();
            anchors = new Vector3[targets.Length];
            colliders = new Collider[targets.Length][];
            originalColliderEnabled = new bool[targets.Length][];
            for (int i = 0; i < targets.Length; i++)
            {
                anchors[i] = targets[i].transform.position;
                colliders[i] = targets[i].GetComponentsInChildren<Collider>(true);
                originalColliderEnabled[i] = new bool[colliders[i].Length];
                for (int j = 0; j < colliders[i].Length; j++) originalColliderEnabled[i][j] = colliders[i][j].enabled;
            }
            Model = new ChallengeRoomModel(inventory, rewardId, targets.Length, 60f, 3, requiredReaction, allowReplayAfterClaim);
            Model.Changed += OnModelChanged;
            Subscribe();
            OnModelChanged();
        }

        public bool TryStart()
        {
            if (Model == null || (State != ChallengeState.Ready && State != ChallengeState.Failed)) return false;
            ClearTargets();
            return Model.TryStart();
        }

        public bool TryClaimReward() => Model != null && Model.TryClaimReward();
        public bool IsDefeated(int index) => Model != null && Model.IsDefeated(index);

        public void ResetChallenge()
        {
            if (Model == null) return;
            ClearTargets();
            Model.ResetProgress();
        }

        public void Tick(float deltaTime) => Model?.Tick(deltaTime);
        private void Update() { if (AutoTick) Tick(Time.deltaTime); }
        private void LateUpdate() => RestoreAnchors();

        private void ClearTargets()
        {
            foreach (ElementalActor target in targets)
                if (target != null) manager.ResetTarget(target);
            RestoreAnchors();
        }

        private void OnDamage(ElementDamageEvent damage)
        {
            if (Model == null || damage.Reaction != Model.RequiredReaction || damage.Source == null ||
                damage.Source.Team != ActorTeam.Player) return;
            int index = Array.IndexOf(targets, damage.Target);
            // 화→수와 수→화 모두 허용한다. 실제 피해가 기록된 뒤 완료 처리하여 마지막 증발 피해를 취소하지 않는다.
            if (index >= 0) Model.RegisterReaction(index, damage.Reaction);
        }

        private void OnModelChanged()
        {
            UpdateTargetAvailability();
            Changed?.Invoke();
        }

        private void UpdateTargetAvailability()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                bool available = isActiveAndEnabled && State == ChallengeState.Running && !Model.IsDefeated(i);
                if (targets[i] != null) targets[i].IsOnField = available;
                for (int j = 0; j < colliders[i].Length; j++)
                    if (colliders[i][j] != null) colliders[i][j].enabled = available && originalColliderEnabled[i][j];
            }
            // 시간 종료/목표 제압 즉시 콜라이더와 원소 입력 대상을 닫는다.
            Physics.SyncTransforms();
        }

        private void RestoreAnchors()
        {
            bool moved = false;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null || targets[i].transform.position == anchors[i]) continue;
                targets[i].transform.position = anchors[i];
                moved = true;
            }
            if (moved) Physics.SyncTransforms();
        }

        private void OnEnable()
        {
            Subscribe();
            if (Model != null) UpdateTargetAvailability();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (Model != null) UpdateTargetAvailability();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (Model != null) Model.Changed -= OnModelChanged;
        }

        private void Subscribe()
        {
            if (subscribed || manager == null || !isActiveAndEnabled) return;
            manager.DamageApplied += OnDamage;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (subscribed && manager != null) manager.DamageApplied -= OnDamage;
            subscribed = false;
        }
    }
}


