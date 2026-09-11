using UnityEngine;

namespace Orbis.M0
{
    public enum PlayerActionState { Idle, Move, Attack, Skill, Burst, Hurt, Dead }

    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerMotor : MonoBehaviour
    {
        // 기획서 미정 수치: 걷기 2.5m/s, 달리기 6m/s, 점프 높이 1.4m.
        [SerializeField, Min(0.1f)] private float walkSpeed = 2.5f;
        [SerializeField, Min(0.1f)] private float runSpeed = 6f;
        [SerializeField, Min(0.1f)] private float jumpHeight = 1.4f;
        // 기획서 미정 수치: 반응성 있는 프로토타입용 중력 -24m/s², 회전 720도/s.
        [SerializeField] private float gravity = -24f;
        [SerializeField, Min(1f)] private float turnSpeed = 720f;
        [SerializeField] private LayerMask groundLayers = (1 << 8) | (1 << 9);
        private CharacterController controller;
        private M0Input input;
        private Transform cameraPivot;
        private Animator animator;
        private BasicAttackCombo combat;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private float verticalSpeed;
        private int locomotionHash;
        private bool configured;
        private PlayerActionState actionState;
        private float actionNormalizedTime;
        private bool ownsActionAnimatorClock;
        private float previousActionAnimatorSpeed;

        // Optional traversal driver. A null driver preserves the completed M0/M1 movement path.
        public IPlayerTraversal Traversal { get; set; }
        public bool SprintAllowed { get; set; } = true;
        public float VerticalSpeed => verticalSpeed;
        public bool IsGrounded { get; private set; }
        public float HorizontalSpeed { get; private set; }
        public PlayerActionState State => ActionLocked ? actionState
            : combat != null && combat.IsAttacking ? PlayerActionState.Attack
            : HorizontalSpeed > 0.05f ? PlayerActionState.Move : PlayerActionState.Idle;
        public bool ActionLocked => actionState >= PlayerActionState.Skill;
        public string StateName => State.ToString();
        public event System.Action<PlayerActionState> ActionStateChanged;

        public void Configure(M0Input playerInput, Transform viewPivot, Animator visualAnimator,
            BasicAttackCombo attackCombo)
        {
            if (combat != null) combat.StepStarted -= FaceAttack;
            controller = GetComponent<CharacterController>();
            input = playerInput;
            cameraPivot = viewPivot;
            animator = visualAnimator;
            combat = attackCombo;
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            if (animator != null) animator.applyRootMotion = false;
            combat.StepStarted += FaceAttack;
            configured = true;
        }

        /// <summary>Swap presentation only; movement, spawn and combo state remain owned by this pawn.</summary>
        public void SetVisualAnimator(Animator visualAnimator)
        {
            RestoreActionAnimatorClock();
            animator = visualAnimator;
            if (animator != null) animator.applyRootMotion = false;
            RefreshPresentation();
        }

        private void Update()
        {
            if (!configured) return;
            if (input.ResetPressed || transform.position.y < -15f)
            {
                // 테스트 편의용 낙하 복귀선 -15m. 세이브/사망 시스템이 아니다.
                ResetToSpawn();
                return;
            }

            float dt = Time.deltaTime;
            if (ActionLocked)
            {
                TickActionMotion(dt);
                return;
            }
            if (Traversal != null && Traversal.TickTraversal(dt)) return;
            bool wasAttacking = combat.IsAttacking;
            IsGrounded = verticalSpeed <= 0f && (controller.isGrounded || ProbeGround());
            if (IsGrounded && verticalSpeed < 0f) verticalSpeed = -4f; // 경사면 접지용 기본값.
            if (!input.GameplayEnabled) combat.CancelAttack();
            if (input.AttackPressed) combat.RequestAttack(IsGrounded);
            wasAttacking |= combat.IsAttacking;
            combat.Tick(dt);

            Vector3 movement = CameraRelative(input.Move, cameraPivot);
            if (combat.IsAttacking)
            {
                movement = Vector3.zero; // 사용자 확정: 공격 중 이동·점프 제한.
                HorizontalSpeed = 0f;
            }
            else
            {
                float speed = input.RunHeld && SprintAllowed ? runSpeed : walkSpeed;
                HorizontalSpeed = speed * movement.magnitude;
                if (movement.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(movement), turnSpeed * dt);
                if (input.JumpPressed && IsGrounded)
                {
                    verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    IsGrounded = false;
                }
                movement *= speed;
            }

            // 공격 중에도 중력·캐릭터 충돌은 계속 적용한다.
            verticalSpeed = Mathf.Max(verticalSpeed + gravity * dt, -40f);
            controller.stepOffset = IsGrounded ? 0.3f : 0f; // 공중에서 계단을 자동으로 오르지 않는다.
            var flags = controller.Move((movement + Vector3.up * verticalSpeed) * dt);
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
            IsGrounded = verticalSpeed <= 0f && ((flags & CollisionFlags.Below) != 0 || ProbeGround());
            if (!IsGrounded && combat.IsAttacking) combat.CancelAttack();

            if (!combat.IsAttacking) AnimateLocomotion(wasAttacking);
            Traversal?.AfterLocomotion(dt);
        }

        /// <summary>Use the shared controller and seven existing clips for the four action-lock states.</summary>
        public bool CanBeginAction(PlayerActionState next)
        {
            if (next < PlayerActionState.Skill || next > PlayerActionState.Dead)
                throw new System.ArgumentOutOfRangeException(nameof(next));
            if (!isActiveAndEnabled || actionState == PlayerActionState.Dead && next != PlayerActionState.Dead) return false;
            if (next == PlayerActionState.Skill) return State == PlayerActionState.Idle || State == PlayerActionState.Move;
            if (next == PlayerActionState.Burst) return !ActionLocked;
            return true;
        }

        public bool TryBeginAction(PlayerActionState next)
        {
            if (!CanBeginAction(next)) return false;
            combat?.CancelAttack();
            actionState = next;
            actionNormalizedTime = 0f;
            HorizontalSpeed = 0f;
            RefreshPresentation();
            ActionStateChanged?.Invoke(State);
            return true;
        }

        public void EndAction(PlayerActionState owner)
        {
            if (!ActionLocked || actionState != owner) return;
            actionState = PlayerActionState.Idle;
            RestoreActionAnimatorClock();
            RefreshPresentation();
            ActionStateChanged?.Invoke(State);
        }

        public void SetActionNormalizedTime(PlayerActionState owner, float normalizedTime)
        {
            if (!ActionLocked || actionState != owner) return;
            actionNormalizedTime = Mathf.Clamp01(normalizedTime);
            SampleActionPose();
        }

        private void TickActionMotion(float dt)
        {
            combat?.CancelAttack();
            HorizontalSpeed = 0f;
            // Preserve environmental clocks and passive descent while suppressing traversal controls.
            bool moved = Traversal is IPlayerActionTraversal blocked && blocked.TickActionTraversal(dt);
            if (!moved)
            {
                IsGrounded = verticalSpeed <= 0f && (controller.isGrounded || ProbeGround());
                if (IsGrounded && verticalSpeed < 0f) verticalSpeed = -4f;
                verticalSpeed = Mathf.Max(verticalSpeed + gravity * dt, -40f);
                controller.stepOffset = IsGrounded ? .3f : 0f;
                CollisionFlags flags = controller.Move(Vector3.up * verticalSpeed * dt);
                if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
                IsGrounded = verticalSpeed <= 0f && ((flags & CollisionFlags.Below) != 0 || ProbeGround());
                Traversal?.AfterLocomotion(dt);
            }
            SampleActionPose();
        }

        private void SampleActionPose()
        {
            if (!ActionLocked || animator == null || animator.runtimeAnimatorController == null) return;
            if (!ownsActionAnimatorClock)
            {
                previousActionAnimatorSpeed = animator.speed;
                animator.speed = 0f;
                ownsActionAnimatorClock = true;
            }
            // M1.6 adds gameplay states, not new authored clips. Skill reuses the same one-hand slash for all elements.
            animator.Play(actionState == PlayerActionState.Skill ? "Attack1" : "Idle", 0, actionNormalizedTime);
        }

        private void RestoreActionAnimatorClock()
        {
            if (!ownsActionAnimatorClock) return;
            if (animator != null) animator.speed = previousActionAnimatorSpeed;
            ownsActionAnimatorClock = false;
        }

        public static Vector3 CameraRelative(Vector2 move, Transform view)
        {
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return (forward * move.y + right * move.x) * (move.sqrMagnitude > 1f ? 1f / move.magnitude : 1f);
        }

        private bool ProbeGround()
        {
            // 0.1m 접지 허용 오차. 자기 자신은 Player 레이어라 검사에서 제외된다.
            Vector3 origin = transform.position + Vector3.up * 0.33f;
            return Physics.SphereCast(origin, 0.27f, Vector3.down, out var hit, 0.16f,
                       groundLayers, QueryTriggerInteraction.Ignore)
                && Vector3.Angle(hit.normal, Vector3.up) <= controller.slopeLimit;
        }

        private void FaceAttack(int step)
        {
            Vector3 direction = CameraRelative(input.Move, cameraPivot);
            // 각 타 시작에서만 방향 변경. 입력이 없으면 현재 몸 방향을 유지한다.
            if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
        }

        private void AnimateLocomotion(bool force)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            string state = !IsGrounded ? "Jump" : HorizontalSpeed < 0.05f ? "Idle"
                : input.RunHeld && SprintAllowed ? "Run" : "Walk";
            int hash = Animator.StringToHash(state);
            if (hash == locomotionHash && !force) return;
            // 기획서 미정 수치: 이동 애니메이션 전환 0.08초.
            animator.CrossFadeInFixedTime(hash, 0.08f);
            locomotionHash = hash;
        }

        /// <summary>Refresh a switched character's pose without changing position or jump velocity.</summary>
        public void RefreshPresentation()
        {
            locomotionHash = 0;
            if (ActionLocked) { SampleActionPose(); return; }
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.Play(IsGrounded ? "Idle" : "Jump", 0, 0f);
        }

        /// <summary>Synchronize movement observations after an optional traversal driver moves the same controller.</summary>
        public void SetTraversalMotion(float verticalVelocity, float horizontalSpeed, bool grounded)
        {
            verticalSpeed = verticalVelocity;
            HorizontalSpeed = horizontalSpeed;
            IsGrounded = grounded;
            locomotionHash = 0;
        }

        public void ResetToSpawn()
        {
            if (ActionLocked) EndAction(actionState);
            combat.CancelAttack();
            controller.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            controller.enabled = true;
            verticalSpeed = 0f;
            HorizontalSpeed = 0f;
            IsGrounded = false;
            locomotionHash = 0;
            Traversal?.ResetTraversal();
        }

        private void OnDisable()
        {
            if (ActionLocked) EndAction(actionState);
            RestoreActionAnimatorClock();
            if (combat != null) combat.CancelAttack();
            verticalSpeed = HorizontalSpeed = 0f;
            locomotionHash = 0;
        }

        private void OnDestroy()
        {
            if (combat != null) combat.StepStarted -= FaceAttack;
        }
    }
}

