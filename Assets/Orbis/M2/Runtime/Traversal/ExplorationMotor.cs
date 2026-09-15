using System;
using Orbis.M0;
using Orbis.M0.Animation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbis.M2
{
    public enum ExplorationMode { Locomotion, Climbing, Gliding, Swimming }

    /// <summary>Optional M2 driver for the same M0 CharacterController; party switching never recreates movement.</summary>
    [DisallowMultipleComponent]
    public sealed class ExplorationMotor : MonoBehaviour, IPlayerTraversal, IPlayerActionTraversal
    {
        // Unspecified prototype values: 2 m/s climbing, 5 m/s gliding with 2.2 m/s descent,
        // 3 m/s swimming and 2 m/s vertical swimming. Stamina costs live in StaminaPool.
        [SerializeField] private float climbSpeed = 2f;
        [SerializeField] private float glideSpeed = 5f;
        [SerializeField] private float glideDescent = 2.2f;
        [SerializeField] private float swimSpeed = 3f;
        private CharacterController controller;
        private PlayerMotor motor;
        private M0Input input;
        private BasicAttackCombo combat;
        private Transform cameraPivot;
        private Animator animator;
        private ICharacterAnimationDriver animationDriver;
        private InputActionMap actions;
        private InputAction climb, ascend, dive;
        private Collider wall;
        private Vector3 wallNormal;
        private WaterVolume water;
        private Vector3 initialPosition;
        private float regrabAfter;
        private float fallOriginY;
        private bool trackingFall;
        private bool exhaustedClimbFall;
        private bool sprintExhausted;
        private bool configured;

        public ExplorationMode Mode { get; private set; }
        public StaminaPool Stamina { get; private set; }
        public bool IsUnderwater => Mode == ExplorationMode.Swimming && water != null &&
            transform.position.y + 1.65f < water.SurfaceY - 0.05f;
        public Vector3 LastSafePosition { get; private set; }
        public float AccumulatedFallDamage { get; private set; }
        public float AccumulatedRescueDamage { get; private set; }
        public int RescueCount { get; private set; }
        public event Action<float> DamageOccurred;

        private void Awake()
        {
            actions = new InputActionMap("M2 Exploration");
            climb = actions.AddAction("Climb", InputActionType.Button, "<Keyboard>/e");
            climb.AddBinding("<Gamepad>/rightShoulder");
            ascend = actions.AddAction("Ascend", InputActionType.Button, "<Keyboard>/space");
            ascend.AddBinding("<Gamepad>/buttonSouth");
            dive = actions.AddAction("Dive", InputActionType.Button, "<Keyboard>/leftCtrl");
            dive.AddBinding("<Gamepad>/leftShoulder");
        }

        private void OnEnable()
        {
            actions?.Enable();
            if (configured && motor != null) motor.Traversal = this;
        }
        private void OnDisable()
        {
            actions?.Disable();
            if (motor != null)
            {
                motor.SprintAllowed = true;
                if (motor.Traversal == this) motor.Traversal = null;
            }
            Mode = ExplorationMode.Locomotion;
            SynchronizeMotionMode();
        }
        private void OnDestroy() => actions?.Dispose();

        public void Configure(M0Input playerInput, PlayerMotor playerMotor, BasicAttackCombo attackCombo,
            Transform viewPivot, StaminaPool stamina)
        {
            input = playerInput;
            motor = playerMotor;
            combat = attackCombo;
            cameraPivot = viewPivot;
            controller = motor.GetComponent<CharacterController>();
            animator = motor.GetComponentInChildren<Animator>();
            animationDriver = CharacterAnimationBinding.Resolve(animator);
            Stamina = stamina ?? throw new ArgumentNullException(nameof(stamina));
            initialPosition = transform.position;
            LastSafePosition = initialPosition;
            motor.Traversal = this;
            configured = true;
            ResetTraversal();
        }

        /// <summary>Keep traversal resources and velocity when the party visual changes.</summary>
        public void SetVisualAnimator(Animator visualAnimator)
        {
            animator = visualAnimator;
            animationDriver = CharacterAnimationBinding.Resolve(animator);
            SynchronizeMotionMode();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                if (animationDriver == null && Mode != ExplorationMode.Locomotion) animator.Play("Jump", 0, .5f);
            }
        }

        private void SynchronizeMotionMode()
        {
            animationDriver?.SetTraversalMode(Mode == ExplorationMode.Climbing ? CharacterMotionMode.Climbing :
                Mode == ExplorationMode.Gliding ? CharacterMotionMode.Gliding :
                Mode == ExplorationMode.Swimming ? CharacterMotionMode.Swimming : CharacterMotionMode.Ground);
        }

        public void SetStaminaPool(StaminaPool pool) => Stamina = pool ?? throw new ArgumentNullException(nameof(pool));

        public bool TickTraversal(float dt)
        {
            if (!configured || !isActiveAndEnabled) return false;
            WaterVolume containingWater = FindWater();
            if (containingWater != null)
            {
                water = containingWater;
                if (Mode != ExplorationMode.Swimming) EnterMode(ExplorationMode.Swimming);
                TickSwimming(dt);
                return true;
            }
            if (Mode == ExplorationMode.Swimming)
            {
                water = null;
                LeaveMode(0f);
            }

            bool staminaAdvanced = false;
            bool wantsClimb = input.GameplayEnabled && climb.WasPressedThisFrame();
            if (Mode == ExplorationMode.Climbing)
            {
                if (wantsClimb)
                {
                    LeaveMode(0f);
                    regrabAfter = Time.time + 0.35f;
                }
                else
                {
                    staminaAdvanced = true;
                    if (TickClimbing(dt)) return true;
                }
            }
            else if (wantsClimb && Time.time >= regrabAfter && Stamina.Current >= 10f &&
                !combat.IsAttacking && FindClimbWall(out RaycastHit hit))
            {
                wall = hit.collider;
                wallNormal = hit.normal;
                EnterMode(ExplorationMode.Climbing);
                return TickClimbing(dt);
            }

            if (input.GameplayEnabled && input.JumpPressed && !motor.IsGrounded)
            {
                if (Mode == ExplorationMode.Gliding) LeaveMode(-glideDescent);
                else if (Stamina.Current >= 5f && CanOpenGlider()) EnterMode(ExplorationMode.Gliding);
            }
            if (Mode == ExplorationMode.Gliding)
            {
                staminaAdvanced = true;
                Stamina.Tick(StaminaActivity.Gliding, dt);
                if (Stamina.Current <= 0f) LeaveMode(-glideDescent);
                else
                {
                    Vector3 direction = PlayerMotor.CameraRelative(input.Move, cameraPivot);
                    if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
                    direction.Normalize();
                    Face(direction, dt);
                    CollisionFlags flags = Move(direction * glideSpeed + Vector3.down * glideDescent, dt);
                    fallOriginY = transform.position.y;
                    trackingFall = true;
                    if ((flags & CollisionFlags.Below) != 0)
                    {
                        trackingFall = false;
                        exhaustedClimbFall = false;
                        LeaveMode(-4f);
                        motor.SetTraversalMotion(-4f, glideSpeed, true);
                        LastSafePosition = transform.position + Vector3.up * 0.05f;
                    }
                    return true;
                }
            }

            if (!input.RunHeld) sprintExhausted = false;
            if (Stamina.Current <= 0f) sprintExhausted = true;
            // A depleted held sprint resumes only after 15 stamina, avoiding rapid walk/run oscillation.
            if (Stamina.Current >= 15f) sprintExhausted = false;
            motor.SprintAllowed = !sprintExhausted && Stamina.Current > 0f;
            bool running = motor.IsGrounded && !combat.IsAttacking && input.GameplayEnabled &&
                input.RunHeld && input.Move.sqrMagnitude > 0.001f && motor.SprintAllowed;
            if (!staminaAdvanced) Stamina.Tick(!motor.IsGrounded ? StaminaActivity.Airborne
                : combat.IsAttacking || input.AttackPressed ? StaminaActivity.Attacking : running
                ? StaminaActivity.Running : StaminaActivity.Rest, dt);
            if (Stamina.Current <= 0f) motor.SprintAllowed = false;
            return false;
        }

        public bool TickActionTraversal(float dt)
        {
            if (!configured || !isActiveAndEnabled) return false;
            WaterVolume containingWater = FindWater();
            if (containingWater != null)
            {
                water = containingWater;
                if (Mode != ExplorationMode.Swimming) EnterMode(ExplorationMode.Swimming);
                TickSwimming(dt, false);
                return true;
            }
            if (Mode == ExplorationMode.Swimming) { water = null; LeaveMode(0f); }
            if (Mode == ExplorationMode.Climbing) return TickClimbing(dt, false);
            if (Mode == ExplorationMode.Gliding)
            {
                Stamina.Tick(StaminaActivity.Gliding, dt);
                if (Stamina.Current <= 0f) { LeaveMode(-glideDescent); return false; }
                CollisionFlags flags = Move(Vector3.down * glideDescent, dt);
                fallOriginY = transform.position.y; trackingFall = true;
                if ((flags & CollisionFlags.Below) != 0)
                {
                    trackingFall = exhaustedClimbFall = false;
                    LeaveMode(-4f); motor.SetTraversalMotion(-4f, 0f, true);
                    LastSafePosition = transform.position + Vector3.up * .05f;
                }
                return true;
            }
            // Locked actions do not grant idle regeneration or accept sprint/climb/glider input.
            Stamina.Tick(motor.IsGrounded ? StaminaActivity.Attacking : StaminaActivity.Airborne, dt);
            return false;
        }

        private bool FindClimbWall(out RaycastHit hit)
        {
            Vector3 direction = PlayerMotor.CameraRelative(input.Move, cameraPivot);
            if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
            return Physics.Raycast(transform.position + Vector3.up, direction.normalized, out hit, 0.95f,
                1 << 8, QueryTriggerInteraction.Ignore) && IsClimbable(hit);
        }

        private static bool IsClimbable(RaycastHit hit) =>
            hit.collider.GetComponentInParent<ClimbableSurface>() != null && Mathf.Abs(hit.normal.y) < 0.35f;

        private bool TickClimbing(float dt, bool controlsAllowed = true)
        {
            Vector2 movementInput = controlsAllowed ? input.Move : Vector2.zero;
            Stamina.Tick(StaminaActivity.Climbing, dt); // Holding still on a wall also costs 8/s.
            if (Stamina.Current <= 0f)
            {
                exhaustedClimbFall = true;
                trackingFall = true;
                fallOriginY = transform.position.y;
                regrabAfter = Time.time + 0.5f;
                LeaveMode(0f);
                return false;
            }
            Vector3 origin = transform.position + Vector3.up;
            if (wall == null || !Physics.Raycast(origin, -wallNormal, out RaycastHit hit, 0.95f,
                    1 << 8, QueryTriggerInteraction.Ignore) || !IsClimbable(hit))
            {
                if (movementInput.y > 0f && TryMantle()) return true;
                regrabAfter = Time.time + 0.35f;
                LeaveMode(0f);
                return false;
            }
            wall = hit.collider;
            wallNormal = hit.normal;
            transform.rotation = Quaternion.LookRotation(-wallNormal);
            Vector3 alongWall = Vector3.Cross(Vector3.up, -wallNormal);
            Vector3 move = (alongWall * movementInput.x + Vector3.up * movementInput.y) * climbSpeed;
            // Maintain a 3cm skin gap without teleporting through the wall.
            float gap = Vector3.Dot(origin - hit.point, wallNormal);
            Vector3 correction = wallNormal * Mathf.Clamp(controller.radius + 0.03f - gap, -0.2f, 0.2f);
            controller.stepOffset = 0f;
            CollisionFlags flags = controller.Move(move * dt + correction);
            motor.SetTraversalMotion(move.y, Mathf.Abs(move.x) + Mathf.Abs(move.z), false);
            fallOriginY = transform.position.y;
            trackingFall = true;
            if (movementInput.y < 0f && (flags & CollisionFlags.Below) != 0)
            {
                trackingFall = false;
                LeaveMode(-4f);
                motor.SetTraversalMotion(-4f, 0f, true);
                LastSafePosition = transform.position + Vector3.up * 0.05f;
            }
            return true;
        }

        private bool TryMantle()
        {
            Vector3 aboveLedge = transform.position - wallNormal * 0.8f + Vector3.up * 1.6f;
            if (!Physics.Raycast(aboveLedge, Vector3.down, out RaycastHit top, 1.7f, 1 << 8,
                    QueryTriggerInteraction.Ignore) || top.normal.y < 0.85f) return false;
            Vector3 destination = top.point + Vector3.up * 0.05f;
            float rise = destination.y - transform.position.y;
            if (rise < -0.05f || rise > 1.5f || !CapsuleClear(destination)) return false;
            CollisionFlags up = controller.Move(Vector3.up * Mathf.Max(0f, rise));
            if ((up & CollisionFlags.Above) != 0) return false;
            controller.Move(Vector3.ProjectOnPlane(destination - transform.position, Vector3.up));
            LeaveMode(-4f);
            motor.SetTraversalMotion(-4f, 0f, true);
            trackingFall = false;
            exhaustedClimbFall = false;
            LastSafePosition = transform.position + Vector3.up * 0.05f;
            return true;
        }

        private bool CapsuleClear(Vector3 feet)
        {
            float radius = controller.radius;
            return !Physics.CheckCapsule(feet + Vector3.up * radius,
                feet + Vector3.up * (controller.height - radius), radius,
                (1 << 8) | (1 << 9), QueryTriggerInteraction.Ignore);
        }

        private bool CanOpenGlider() => !Physics.Raycast(transform.position + Vector3.up * 0.1f,
            Vector3.down, 1.2f, (1 << 8) | (1 << 9), QueryTriggerInteraction.Ignore);

        private WaterVolume FindWater()
        {
            foreach (WaterVolume volume in WaterVolume.ActiveVolumes)
                if (volume != null && volume.Contains(transform.position + Vector3.up * 0.6f) &&
                    transform.position.y < volume.SurfaceY - 0.6f) return volume;
            return null;
        }

        private void TickSwimming(float dt, bool controlsAllowed = true)
        {
            Stamina.Tick(StaminaActivity.Swimming, dt);
            if (Stamina.Current <= 0f)
            {
                // Prototype rescue: five environmental damage and 25 stamina at the last safe land position.
                AccumulatedRescueDamage += 5f;
                RescueCount++;
                DamageOccurred?.Invoke(5f);
                Teleport(LastSafePosition);
                Stamina.SetCurrent(25f);
                return;
            }
            Vector3 direction = PlayerMotor.CameraRelative(controlsAllowed ? input.Move : Vector2.zero, cameraPivot);
            Face(direction, dt);
            float targetFeet = water.SurfaceY - 0.95f;
            float vertical = controlsAllowed && input.GameplayEnabled && dive.IsPressed() ? -2f
                : controlsAllowed && input.GameplayEnabled && ascend.IsPressed() ? 2f
                : Mathf.Clamp((targetFeet - transform.position.y) * 2f, -1.5f, 1.5f);
            // The head stays above the surface when floating; Ctrl allows reaching the shallow test lake floor.
            vertical = Mathf.Min(vertical, Mathf.Max(0f, targetFeet - transform.position.y) / Mathf.Max(dt, 0.0001f));
            Move(direction * swimSpeed + Vector3.up * vertical, dt);
            fallOriginY = transform.position.y;
            trackingFall = false;
            exhaustedClimbFall = false;
        }

        private CollisionFlags Move(Vector3 velocity, float dt)
        {
            controller.stepOffset = 0f;
            CollisionFlags flags = controller.Move(velocity * dt);
            bool grounded = (flags & CollisionFlags.Below) != 0;
            float vertical = (flags & CollisionFlags.Above) != 0 && velocity.y > 0f ? 0f : velocity.y;
            motor.SetTraversalMotion(vertical, Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude, grounded);
            return flags;
        }

        private void Face(Vector3 direction, float dt)
        {
            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 360f * dt);
        }

        private void EnterMode(ExplorationMode mode)
        {
            combat.CancelAttack();
            Mode = mode;
            motor.SprintAllowed = false;
            trackingFall = mode != ExplorationMode.Swimming;
            fallOriginY = transform.position.y;
            // Reuse the M0 airborne pose as an explicit placeholder, with no animation-clock ownership change.
            SynchronizeMotionMode();
            if (animationDriver == null && animator != null) animator.Play("Jump", 0, 0.5f);
        }

        private void LeaveMode(float vertical)
        {
            Mode = ExplorationMode.Locomotion;
            SynchronizeMotionMode();
            motor.SetTraversalMotion(vertical, 0f, false);
            motor.RefreshPresentation();
        }

        public void AfterLocomotion(float dt)
        {
            if (!configured) return;
            if (motor.IsGrounded)
            {
                if (trackingFall)
                {
                    float distance = fallOriginY - transform.position.y;
                    // M2 default: falls over 3m deal 5 + 0.5 per extra metre, capped at 20.
                    // A stamina-exhausted climbing fall always records at least five damage on landing.
                    if (distance > 3f || exhaustedClimbFall)
                    {
                        float damage = Mathf.Clamp(5f + Mathf.Max(0f, distance - 3f) * 0.5f, 5f, 20f);
                        AccumulatedFallDamage += damage;
                        DamageOccurred?.Invoke(damage);
                    }
                }
                trackingFall = exhaustedClimbFall = false;
                LastSafePosition = transform.position + Vector3.up * 0.05f;
            }
            else
            {
                if (!trackingFall) { trackingFall = true; fallOriginY = transform.position.y; }
                fallOriginY = Mathf.Max(fallOriginY, transform.position.y);
            }
        }

        /// <summary>Prototype checkpoints/test travel; cancels attacks and velocity without refilling resources.</summary>
        public void Teleport(Vector3 position)
        {
            combat.CancelAttack();
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            Physics.SyncTransforms();
            motor.ResetMotionObservation();
            LeaveMode(0f);
            water = null;
            wall = null;
            trackingFall = exhaustedClimbFall = false;
            regrabAfter = Time.time + 0.2f;
        }

        public void ResetTraversal()
        {
            if (!configured) return;
            Mode = ExplorationMode.Locomotion;
            SynchronizeMotionMode();
            Stamina.Restore();
            LastSafePosition = initialPosition;
            AccumulatedFallDamage = AccumulatedRescueDamage = 0f;
            RescueCount = 0;
            trackingFall = exhaustedClimbFall = sprintExhausted = false;
            wall = null;
            water = null;
            regrabAfter = 0f;
            motor.SprintAllowed = true;
            controller.stepOffset = 0.3f;
        }
    }
}

