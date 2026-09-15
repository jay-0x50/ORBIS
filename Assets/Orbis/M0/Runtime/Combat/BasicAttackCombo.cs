using System;
using System.Collections.Generic;
using UnityEngine;
using Orbis.M0.Animation;

namespace Orbis.M0
{
    /// <summary>지상 3단 공격. 입력과 Tick 호출은 플레이어 모터가 소유한다.</summary>
    [DisallowMultipleComponent]
    public sealed class BasicAttackCombo : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        // 기획서에 없는 M0 기본값: 전방 1.05m, 높이 0.9m, 반경 0.8m의 단순 근접 판정.
        [SerializeField, Min(0f)] private float hitForwardOffset = 1.05f;
        [SerializeField, Min(0f)] private float hitHeight = 0.9f;
        [SerializeField, Min(0.01f)] private float hitRadius = 0.8f;
        [SerializeField] private LayerMask targetLayers = 1 << 9;
        [SerializeField] private LayerMask obstructionLayers = 1 << 8;

        private readonly HashSet<TrainingDummy> hitThisStep = new HashSet<TrainingDummy>();
        private ComboSequence sequence;
        private ICharacterAnimationDriver animationDriver;
        private bool ownsAnimatorClock;
        private float previousAnimatorSpeed;

        public bool IsAttacking => sequence != null && sequence.IsAttacking;
        public int CurrentStep => sequence == null ? 0 : sequence.CurrentStep;
        public float NormalizedTime => sequence == null ? 0f : sequence.NormalizedTime;
        public bool HasBufferedAttack => sequence != null && sequence.HasBufferedAttack;
        public int TotalHitCount { get; private set; }
        public event Action<int> StepStarted;
        // Successful, deduplicated hit hook. Later milestones can attach effects without changing M0 hit rules.
        public event Action<TrainingDummy, int> HitLanded;

        private void Awake() => EnsureSequence();

        public void Configure(Animator targetAnimator)
        {
            RestoreAnimatorClock();
            animationDriver?.ClearAttack();
            animator = targetAnimator;
            animationDriver = CharacterAnimationBinding.Resolve(animator);
            EnsureSequence();
            SynchronizeAttackPose();
        }

        /// <summary>Reconnect only the animation clock; hit timing and damage queries do not change.</summary>
        public void SetVisualAnimator(Animator visualAnimator) => Configure(visualAnimator);

        public void RequestAttack(bool isGrounded)
        {
            EnsureSequence();
            sequence.RequestAttack(isGrounded);
        }

        public void Tick(float deltaTime)
        {
            EnsureSequence();
            sequence.Tick(deltaTime);
            SynchronizeAttackPose();
        }

        public void CancelAttack()
        {
            sequence?.CancelAttack();
            animationDriver?.ClearAttack();
            hitThisStep.Clear();
            RestoreAnimatorClock();
        }

        private void OnDisable() => CancelAttack();

        private void EnsureSequence()
        {
            if (sequence != null)
                return;

            sequence = new ComboSequence();
            sequence.StepStarted += OnStepStarted;
            sequence.ActiveWindowVisited += CheckStrike;
        }

        private void OnStepStarted(int step)
        {
            hitThisStep.Clear();
            // 이동 방향으로 회전하는 모터의 콜백을 피격 판정 전에 실행한다.
            StepStarted?.Invoke(step);
            SynchronizeAttackPose();
        }

        private void SynchronizeAttackPose()
        {
            if (animationDriver != null)
            {
                if (IsAttacking) animationDriver.SampleAttack(CurrentStep, NormalizedTime);
                else animationDriver.ClearAttack();
                return;
            }
            if (!IsAttacking || animator == null || animator.runtimeAnimatorController == null)
            {
                RestoreAnimatorClock();
                return;
            }

            // 공격 동안 FSM 하나가 포즈와 타격 구간을 함께 구동한다. 루트 모션이 없는
            // M0 공격 클립을 직접 샘플링하여 긴 프레임에서 다음 타로 넘어간 잔여 시간도 일치시킨다.
            // Animator의 자동 시간 진행을 멈추지 않으면 지정한 시간에 deltaTime이 다시 더해진다.
            if (!ownsAnimatorClock)
            {
                previousAnimatorSpeed = animator.speed;
                animator.speed = 0f;
                ownsAnimatorClock = true;
            }
            animator.Play("Attack" + CurrentStep, 0, NormalizedTime);
        }

        private void RestoreAnimatorClock()
        {
            if (!ownsAnimatorClock)
                return;
            if (animator != null)
                animator.speed = previousAnimatorSpeed;
            ownsAnimatorClock = false;
        }

        private void CheckStrike(int step)
        {
            Vector3 origin = transform.position + Vector3.up * hitHeight;
            Vector3 center = origin + transform.forward * hitForwardOffset;
            // M0 소규모 테스트 씬: 고정 크기 버퍼 잘림 없이 범위 내 모든 더미를 검사한다.
            Collider[] overlaps = Physics.OverlapSphere(center, hitRadius, targetLayers, QueryTriggerInteraction.Ignore);
            foreach (Collider candidate in overlaps)
            {
                TrainingDummy dummy = candidate.GetComponentInParent<TrainingDummy>();
                if (dummy == null || !dummy.isActiveAndEnabled || hitThisStep.Contains(dummy))
                    continue;

                Vector3 targetPoint = candidate.ClosestPoint(origin);
                if (Physics.Linecast(origin, targetPoint, obstructionLayers, QueryTriggerInteraction.Ignore))
                    continue;

                hitThisStep.Add(dummy);
                dummy.RegisterHit(step);
                TotalHitCount++;
                HitLanded?.Invoke(dummy, step);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = sequence != null && sequence.IsInActiveWindow ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * hitHeight +
                transform.forward * hitForwardOffset, hitRadius);
        }
    }
}


