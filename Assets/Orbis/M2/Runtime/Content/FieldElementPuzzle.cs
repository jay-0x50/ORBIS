using System;
using System.Collections.Generic;
using Orbis.M1;
using UnityEngine;

namespace Orbis.M2
{
    /// <summary>실제 M1 직접 적중 이벤트를 순서 퍼즐에 연결한다. 거리/F 입력과 표현은 씬이 소유한다.</summary>
    [DisallowMultipleComponent]
    public sealed class FieldElementPuzzle : MonoBehaviour
    {
        private ElementalReactionManager manager;
        private ElementalActor[] statues = Array.Empty<ElementalActor>();
        private Vector3[] anchors = Array.Empty<Vector3>();
        private bool subscribed;
        public OrderedFirePuzzleModel Model { get; private set; }
        public event Action Changed;

        public void Configure(ElementalReactionManager reactions, ElementalActor[] orderedStatues,
            RewardInventory inventory, string rewardId = "m2.field.fire-statues",
            ElementType requiredElement = ElementType.Fire, bool allowReplayAfterClaim = false)
        {
            if (reactions == null) throw new ArgumentNullException(nameof(reactions));
            if (orderedStatues == null || orderedStatues.Length != 3)
                throw new ArgumentException("The M2 field puzzle requires three ordered statues.", nameof(orderedStatues));
            var unique = new HashSet<ElementalActor>();
            foreach (ElementalActor statue in orderedStatues)
                if (statue == null || !unique.Add(statue))
                    throw new ArgumentException("Statues must be non-null and unique.", nameof(orderedStatues));
            Unsubscribe();
            if (Model != null) Model.Changed -= NotifyChanged;
            manager = reactions;
            statues = (ElementalActor[])orderedStatues.Clone();
            anchors = new Vector3[statues.Length];
            for (int i = 0; i < statues.Length; i++) anchors[i] = statues[i].transform.position;
            Model = new OrderedFirePuzzleModel(inventory, rewardId, statues.Length, 5, requiredElement, allowReplayAfterClaim);
            Model.Changed += NotifyChanged;
            Subscribe();
            NotifyChanged();
        }

        public bool TryClaimReward() => Model != null && Model.TryClaimReward();

        public void ResetPuzzle()
        {
            if (Model == null) return;
            foreach (ElementalActor statue in statues)
                if (statue != null) manager.ResetTarget(statue);
            RestoreAnchors();
            Model.ResetProgress();
        }

        private void LateUpdate() => RestoreAnchors();

        private void RestoreAnchors()
        {
            // 고정 석상은 잘못된 원소의 과부하 넉백으로 퍼즐 배치가 바뀌지 않게 한다.
            bool moved = false;
            for (int i = 0; i < statues.Length; i++)
            {
                if (statues[i] == null || statues[i].transform.position == anchors[i]) continue;
                statues[i].transform.position = anchors[i];
                moved = true;
            }
            if (moved) Physics.SyncTransforms();
        }

        private void OnApplied(ElementApplicationEvent application)
        {
            if (Model == null || application.IsPropagation || application.Source == null ||
                application.Source.Team != ActorTeam.Player) return;
            int index = Array.IndexOf(statues, application.Target);
            if (index >= 0) Model.RegisterHit(index, application.IncomingElement);
        }

        private void NotifyChanged() => Changed?.Invoke();
        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy()
        {
            Unsubscribe();
            if (Model != null) Model.Changed -= NotifyChanged;
        }

        private void Subscribe()
        {
            if (subscribed || manager == null || !isActiveAndEnabled) return;
            manager.Applied += OnApplied;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (subscribed && manager != null) manager.Applied -= OnApplied;
            subscribed = false;
        }
    }
}

