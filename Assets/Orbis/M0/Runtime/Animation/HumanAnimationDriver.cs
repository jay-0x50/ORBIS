using System;
using UnityEngine;

namespace Orbis.M0.Animation
{
    [DisallowMultipleComponent, RequireComponent(typeof(Animator))]
    public sealed class HumanAnimationDriver : MonoBehaviour, ICharacterAnimationDriver
    {
        public const string SpeedParameter = "MotionSpeed", XParameter = "MotionX", ZParameter = "MotionZ";
        public const string RateParameter = "StrideRate", TurnParameter = "Steering", LeftPlantParameter = "LeftPlant", RightPlantParameter = "RightPlant";
        public const string UpperLayer = "UpperBody", SteeringLayer = "Steering";
        [SerializeField] CharacterMotionProfile profile;
        [SerializeField] Transform presentationPivot;
        readonly HumanVisualTurn visualTurn = new HumanVisualTurn();
        Animator animator;
        RuntimeAnimatorController inspectedController;
        CharacterMotionProfile inspectedProfile;
        Avatar inspectedAvatar;
        bool compatible;
        string validationError;
        int upperLayer = -1, steeringLayer = -1, attackStep;
        string baseState, upperState;
        PlayerActionState action = PlayerActionState.Idle;
        bool externalActionClock;
        float elapsedAction, speed, upperWeight, steeringWeight;
        Vector2 localDirection = Vector2.up;
        CharacterMotionObservation observed;
        CharacterMotionMode mode;
        float previousActualSpeed, stopElapsed;
        float walkCycleDuration, runCycleDuration, walkCycleDistance, runCycleDistance;
        public float CadenceCycleDuration { get; private set; }
        public float CadenceEndpointBound { get; private set; }
        public bool CadenceWasLimited { get; private set; }
        string stopState;
        public bool IsStopping => stopState != null;
        public bool StopSupportsLeft => stopState == "StopLeft";
        public float StopNormalizedTime => stopState == null ? 0f : Mathf.Clamp01(stopElapsed/profile.Get(StopSupportsLeft?HumanMotionSlot.StopLeft:HumanMotionSlot.StopRight).Clip.length);

        public CharacterMotionProfile Profile => profile;
        public Transform PresentationPivot => presentationPivot;
        public float VisualYawOffset => visualTurn.YawOffset;
        public string ValidationError => validationError;
        public Animator Animator => animator != null ? animator : animator = GetComponent<Animator>();
        public bool IsReady
        {
            get
            {
                if (profile == null || Animator == null || !Animator.isHuman || Animator.avatar == null || !Animator.avatar.isValid || Animator.runtimeAnimatorController == null)
                { InvalidateContract(); validationError = "Missing valid Humanoid Avatar, profile or controller."; return false; }
                if (presentationPivot == null || presentationPivot == Animator.transform || !Animator.transform.IsChildOf(presentationPivot))
                { InvalidateContract(); validationError = "An explicit visual child wrapper above Animator is required for presentation yaw."; return false; }
                if (inspectedController != Animator.runtimeAnimatorController || inspectedProfile != profile || inspectedAvatar != Animator.avatar)
                {
                    inspectedController = Animator.runtimeAnimatorController;
                    inspectedProfile = profile; inspectedAvatar = Animator.avatar; compatible = false; validationError = null;
                    try { profile.ValidateForBuild(); ValidateCadenceContract(); }
                    catch (Exception exception) { validationError = exception.Message; return false; }
                    upperLayer = Animator.GetLayerIndex(UpperLayer); steeringLayer = Animator.GetLayerIndex(SteeringLayer);
                    compatible = upperLayer >= 0 && steeringLayer >= 0;
                    foreach (string parameter in RequiredParameters) compatible &= HasFloat(parameter);
                    foreach (string state in new[] { "Locomotion", "Air", "Attack1", "Attack2", "Attack3", "Burst", "Dead",
                        "TurnLeft90", "TurnRight90", "TurnLeft180", "TurnRight180", "StopLeft", "StopRight" }) compatible &= HasState(0,state);
                    foreach (string state in new[] { "Empty", "Skill", "Hurt" }) compatible &= HasState(upperLayer,state);
                    compatible &= HasState(steeringLayer,"SteeringPose");
                    if (!compatible) validationError = "Required controller parameter, layer or state is missing.";
                }
                return compatible;
            }
        }
        public bool AllowFootIK => IsReady && isActiveAndEnabled && mode == CharacterMotionMode.Ground && observed.Grounded &&
            attackStep == 0 && action != PlayerActionState.Burst && action != PlayerActionState.Dead;
        public Transform MotionRoot => observed.Root;
        public bool DiscontinuityThisFrame => observed.Discontinuity;

        public static readonly string[] RequiredParameters =
        {
            SpeedParameter, XParameter, ZParameter, RateParameter, TurnParameter, LeftPlantParameter, RightPlantParameter,
            "Attack1Time", "Attack2Time", "Attack3Time", "SkillTime", "BurstTime", "HurtTime", "DeadTime", "SteeringPoseTime",
            "TurnLeft90Time", "TurnRight90Time", "TurnLeft180Time", "TurnRight180Time", "StopLeftTime", "StopRightTime"
        };
        bool HasFloat(string name)
        {
            foreach (var parameter in Animator.parameters)
                if (parameter.name == name && parameter.type == AnimatorControllerParameterType.Float) return true;
            return false;
        }
        bool HasState(int layer,string name) => layer >= 0 && Animator.HasState(layer,Animator.StringToHash(Animator.GetLayerName(layer) + "." + name));
        void InvalidateContract() { inspectedController = null; inspectedProfile = null; inspectedAvatar = null; compatible = false; }
        void OnValidate() => InvalidateContract();
        void OnEnable() => InvalidateContract();
        public void Configure(CharacterMotionProfile value)
        {
            visualTurn.Bind(presentationPivot);
            profile = value; InvalidateContract();
            if (!IsReady) throw new InvalidOperationException("The motion profile and generated controller contract must be installed together: " + validationError);
            Animator.applyRootMotion = false; ResetPresentation();
        }
        public void Configure(CharacterMotionProfile value,Transform pivot) { presentationPivot = pivot; Configure(value); }

        public void Observe(CharacterMotionObservation observation)
        {
            if (!IsReady || !isActiveAndEnabled) return;
            observed = observation;
            float dt = Mathf.Max(0f, observation.DeltaTime);
            if (observation.Discontinuity)
            {
                speed = 0f; localDirection = Vector2.up;
                ClearStop(); previousActualSpeed=0f;
                GetComponent<HumanFootIK>()?.ResetContacts();
            }
            Vector3 planar = Vector3.ProjectOnPlane(observation.WorldVelocity, Vector3.up);
            float actualSpeed = planar.magnitude;
            bool stopAllowed=attackStep==0 && observation.GameplayState<PlayerActionState.Attack &&
                mode==CharacterMotionMode.Ground && observation.Grounded;
            if(IsStopping && (!stopAllowed || actualSpeed>.15f)) ClearStop();
            if(!IsStopping && stopAllowed && actualSpeed<.05f && previousActualSpeed>.5f)
            {
                stopState=Animator.GetFloat(LeftPlantParameter)>=Animator.GetFloat(RightPlantParameter)?"StopLeft":"StopRight";
                stopElapsed=0f;
                GetComponent<HumanFootIK>()?.BeginStop();
            }
            previousActualSpeed=actualSpeed;
            if(IsStopping)
            {
                stopElapsed+=dt;
                if(StopNormalizedTime>=1f) ClearStop(true);
                else Animator.SetFloat(stopState+"Time",StopNormalizedTime);
            }
            visualTurn.Bind(presentationPivot);
            bool turnAllowed = attackStep == 0 && observation.GameplayState < PlayerActionState.Attack &&
                mode == CharacterMotionMode.Ground && observation.Grounded;
            visualTurn.Observe(observation,actualSpeed,turnAllowed,profile);
            speed = Damp(speed, actualSpeed, profile.SpeedSmoothingSeconds, dt);
            if (actualSpeed > .025f && observation.Root != null)
            {
                // Velocity must be expressed in the delayed VISUAL facing frame, otherwise
                // the feet keep moving forward while the visible body is still turning.
                Vector3 local = presentationPivot.InverseTransformDirection(planar.normalized);
                localDirection = Vector2.Lerp(localDirection, new Vector2(local.x, local.z), Alpha(profile.DirectionSmoothingSeconds, dt));
                if (localDirection.sqrMagnitude > .001f) localDirection.Normalize();
            }
            if (attackStep != 0 && observation.GameplayState != PlayerActionState.Attack) ClearAttack();
            if (action >= PlayerActionState.Skill && observation.GameplayState != action) ClearAction(action);

            Animator.SetFloat(SpeedParameter, speed);
            Animator.SetFloat(XParameter, localDirection.x); Animator.SetFloat(ZParameter, localDirection.y);
            float rate = StrideRate(actualSpeed, localDirection);
            Animator.SetFloat(RateParameter, rate);
            float steering = Mathf.Clamp(observation.SignedYawDegreesPerSecond / Mathf.Max(1f, profile.SteeringFullWeightYawSpeed), -1f, 1f);
            Animator.SetFloat(TurnParameter, Damp(Animator.GetFloat(TurnParameter), steering, .075f, dt));

            bool overlay = action == PlayerActionState.Skill || action == PlayerActionState.Hurt;
            upperWeight = Damp(upperWeight, overlay ? 1f : 0f, profile.OverlayFadeSeconds, dt);
            Animator.SetLayerWeight(upperLayer, upperWeight);
            float steeringTarget = attackStep == 0 && action < PlayerActionState.Skill && mode == CharacterMotionMode.Ground && observation.Grounded
                ? profile.SteeringMaximumWeight : 0f;
            steeringWeight = Damp(steeringWeight, steeringTarget, .10f, dt);
            Animator.SetLayerWeight(steeringLayer, steeringWeight);
            if (!overlay && upperWeight < .001f && upperState != "Empty")
            { Animator.Play("Empty", upperLayer, 0f); upperState = "Empty"; }

            if (action >= PlayerActionState.Skill && !externalActionClock)
            {
                // Only visual unclocked fallbacks (principally the death settling pose) use elapsed time.
                // Skill/Hurt receive their FSM clocks; Burst receives the existing unscaled Timeline time.
                elapsedAction += dt;
                var clip = profile.Get(ActionSlot(action));
                SetActionTime(action, clip != null ? elapsedAction / Mathf.Max(.001f, clip.Clip.length) : 0f);
            }
            if (attackStep == 0 && action != PlayerActionState.Burst && action != PlayerActionState.Dead)
            {
                string state = mode == CharacterMotionMode.Ground && observation.Grounded ? IsStopping ? stopState : visualTurn.IsStepping ? visualTurn.Step : "Locomotion" : "Air";
                if (!IsStopping && visualTurn.IsStepping) Animator.SetFloat(state+"Time",visualTurn.NormalizedTime);
                SetBaseState(state,IsStopping || visualTurn.IsStepping ? profile.ActionFadeSeconds : profile.LocomotionFadeSeconds);
            }
        }

        float StrideRate(float actualSpeed, Vector2 direction)
        {
            CadenceCycleDuration=CadenceEndpointBound=0f; CadenceWasLimited=false;
            if (speed < .025f) return 1f; // Idle breathing must continue when the pawn has stopped.
            float run = Mathf.InverseLerp(profile.WalkThreshold, profile.RunThreshold, speed);
            float walkNative = DirectionalNativeSpeed(false, direction), runNative = DirectionalNativeSpeed(true, direction);
            // Below the walk threshold the tree also contains stationary Idle. Include that
            // blend contribution, otherwise accelerating feet travel less than the pawn.
            float native = Mathf.Lerp(walkNative, runNative, run) * Mathf.Clamp01(speed / profile.WalkThreshold);
            // Stops slow the gait before Idle blends in. Do not multiply the actor's movement speed.
            float requestedRate=Mathf.Clamp(actualSpeed / Mathf.Max(.05f, native), profile.MinimumStrideRate, profile.MaximumStrideRate);
            // After06 A/B only: limit the transient Walk/Run cycle cadence, not pawn
            // velocity or authored stride. Idle/start, steady gait, stop, actions and
            // traversal retain their existing rate path. The active driver is unchanged.
            if(run>0f && run<1f && actualSpeed>=profile.WalkThreshold)
            {
                // The generated tree has unit child time scales. All four directions
                // in each gait have the same duration (validated below), so arbitrary
                // 2D directional weights cancel: this is the exact current 1D tree
                // cycle duration, not an average of the clips' native velocities.
                // After04 frame90 verifies L=.716668*.82+.283332*.66=.774667s;
                // observed normalized advance=dt*2.5/L=.10757, not settled .07310.
                CadenceCycleDuration=Mathf.Lerp(walkCycleDuration,runCycleDuration,run);
                CadenceEndpointBound=Mathf.Max(profile.WalkThreshold/walkCycleDistance,
                    profile.RunThreshold/runCycleDistance);
                float maximumRate=CadenceEndpointBound*CadenceCycleDuration;
                CadenceWasLimited=requestedRate>maximumRate;
                return Mathf.Min(requestedRate,maximumRate);
            }
            return requestedRate;
        }
        void ValidateCadenceContract()
        {
            // This bounded experiment uses the currently reviewed equal-duration,
            // equal-distance direction sets. Fail explicitly for an incompatible new
            // profile instead of pretending our 1D duration is exact for another tree.
            var walk=profile.Get(HumanMotionSlot.Walk); var run=profile.Get(HumanMotionSlot.Run);
            walkCycleDuration=walk.Clip.length; runCycleDuration=run.Clip.length;
            walkCycleDistance=walk.CycleDistanceMetres; runCycleDistance=run.CycleDistanceMetres;
            foreach(var slot in new[]{HumanMotionSlot.WalkLeft,HumanMotionSlot.WalkRight,HumanMotionSlot.WalkBack,
                HumanMotionSlot.RunLeft,HumanMotionSlot.RunRight,HumanMotionSlot.RunBack})
            {
                bool isRun=slot>=HumanMotionSlot.RunLeft;
                var clip=profile.Get(slot);
                if(Mathf.Abs(clip.Clip.length-(isRun?runCycleDuration:walkCycleDuration))>1e-5f ||
                    Mathf.Abs(clip.CycleDistanceMetres-(isRun?runCycleDistance:walkCycleDistance))>1e-5f)
                    throw new InvalidOperationException("After06 cadence experiment requires the reviewed equal-duration/equal-distance directional clips: "+slot);
            }
        }
        float DirectionalNativeSpeed(bool run, Vector2 direction)
        {
            HumanMotionSlot forward = run ? HumanMotionSlot.Run : HumanMotionSlot.Walk;
            HumanMotionSlot back = run ? HumanMotionSlot.RunBack : HumanMotionSlot.WalkBack;
            HumanMotionSlot left = run ? HumanMotionSlot.RunLeft : HumanMotionSlot.WalkLeft;
            HumanMotionSlot right = run ? HumanMotionSlot.RunRight : HumanMotionSlot.WalkRight;
            float x = Mathf.Abs(direction.x), z = Mathf.Abs(direction.y), total = Mathf.Max(.0001f, x + z);
            return (Native(direction.y >= 0f ? forward : back) * z + Native(direction.x >= 0f ? right : left) * x) / total;
        }
        float Native(HumanMotionSlot slot)
        {
            var entry = profile.Get(slot);
            return entry != null && entry.Clip != null ? entry.CycleDistanceMetres / Mathf.Max(.001f, entry.Clip.length) : 0f;
        }

        public void SampleAttack(int step, float normalizedTime)
        {
            if (!IsReady) return;
            if (step < 1 || step > ComboSequence.StepCount || !Finite(normalizedTime)) throw new ArgumentOutOfRangeException(nameof(step));
            // The caller's ComboSequence remains authoritative. No animation events perform hits.
            attackStep = step;
            ClearStop();
            ResetVisualTurn();
            string state = "Attack" + step;
            Animator.SetFloat(state + "Time", Mathf.Clamp01(normalizedTime));
            SetBaseState(state, profile.ActionFadeSeconds);
        }
        public void ClearAttack()
        {
            if (attackStep == 0) return;
            attackStep = 0;
            if (IsReady && action != PlayerActionState.Burst && action != PlayerActionState.Dead)
                SetBaseState(mode == CharacterMotionMode.Ground && observed.Grounded ? "Locomotion" : "Air", profile.LocomotionFadeSeconds);
        }
        public void SampleAction(PlayerActionState state, float normalizedTime, bool externallyClocked)
        {
            if (!IsReady) return;
            if (state < PlayerActionState.Skill || state > PlayerActionState.Dead || !Finite(normalizedTime))
                throw new ArgumentOutOfRangeException(nameof(state));
            if (action != state) { action = state; elapsedAction = 0f; }
            externalActionClock = externallyClocked;
            ClearStop();
            ResetVisualTurn();
            attackStep = 0;
            if (externallyClocked) SetActionTime(state, normalizedTime);
            string name = state.ToString();
            if (state == PlayerActionState.Skill || state == PlayerActionState.Hurt)
            {
                if (upperState != name)
                {
                    Animator.CrossFadeInFixedTime(name, profile.ActionFadeSeconds, upperLayer, 0f);
                    upperState = name;
                }
            }
            else SetBaseState(name, profile.ActionFadeSeconds);
        }
        void SetActionTime(PlayerActionState state, float value) => Animator.SetFloat(state + "Time", Mathf.Clamp01(value));
        public void ClearAction(PlayerActionState owner)
        {
            if (owner != action) return; // A cancelled old Timeline cannot release a newer Hurt/Dead owner.
            action = PlayerActionState.Idle; externalActionClock = false; elapsedAction = 0f;
            if (IsReady && attackStep == 0) SetBaseState(mode == CharacterMotionMode.Ground && observed.Grounded ? "Locomotion" : "Air", profile.LocomotionFadeSeconds);
        }
        public void SetTraversalMode(CharacterMotionMode value)
        {
            if (mode == value) return;
            mode = value; GetComponent<HumanFootIK>()?.ResetContacts();
            ClearStop();
            ResetVisualTurn();
        }
        void SetBaseState(string state, float fade)
        {
            if (baseState == state) return;
            // Each action has a separate Time parameter. Changing Attack2Time must not reset the
            // outgoing Attack1 pose while it is still contributing to the crossfade.
            Animator.CrossFadeInFixedTime(state, fade, 0, 0f); baseState = state;
        }
        public void ResetPresentation()
        {
            if (!IsReady) return;
            visualTurn.Bind(presentationPivot); ResetVisualTurn();
            ClearStop(); previousActualSpeed=0f;
            attackStep = 0; action = PlayerActionState.Idle; externalActionClock = false; elapsedAction = 0f;
            speed = upperWeight = steeringWeight = 0f; localDirection = Vector2.up;
            Animator.SetLayerWeight(upperLayer, 0f); Animator.SetLayerWeight(steeringLayer, 0f);
            Animator.SetFloat(SpeedParameter, 0f); Animator.SetFloat(RateParameter, 1f);
            Animator.SetFloat(XParameter, 0f); Animator.SetFloat(ZParameter, 1f); Animator.SetFloat(TurnParameter, 0f);
            foreach (string parameter in RequiredParameters)
                if (parameter.EndsWith("Time", StringComparison.Ordinal)) Animator.SetFloat(parameter, parameter == "SteeringPoseTime" ? 1f : 0f);
            Animator.Play("Locomotion", 0, 0f); Animator.Play("Empty", upperLayer, 0f);
            Animator.Play("SteeringPose", steeringLayer, 0f);
            baseState = "Locomotion"; upperState = "Empty"; GetComponent<HumanFootIK>()?.ResetContacts();
        }
        public void Release()
        {
            ResetVisualTurn();
            ClearStop(); previousActualSpeed=0f;
            if (Animator != null && compatible)
            { Animator.SetLayerWeight(upperLayer, 0f); Animator.SetLayerWeight(steeringLayer, 0f); }
            GetComponent<HumanFootIK>()?.ResetContacts();
            attackStep = 0; action = PlayerActionState.Idle; baseState = upperState = null;
            // This driver never borrowed Animator.speed. Legacy motor/combat clock owners restore their own loans.
        }
        void OnDisable() => Release();
        void ClearStop(bool completed=false)
        {
            if(stopState==null) return;
            stopState=null; stopElapsed=0f; GetComponent<HumanFootIK>()?.EndStop(completed);
        }
        void ResetVisualTurn()
        {
            if (Mathf.Abs(visualTurn.YawOffset) > .001f || visualTurn.IsStepping) GetComponent<HumanFootIK>()?.ResetContacts();
            visualTurn.Reset();
        }
        static HumanMotionSlot ActionSlot(PlayerActionState value) => value == PlayerActionState.Skill ? HumanMotionSlot.Skill :
            value == PlayerActionState.Burst ? HumanMotionSlot.Burst : value == PlayerActionState.Hurt ? HumanMotionSlot.Hurt : HumanMotionSlot.Dead;
        static float Alpha(float seconds, float dt) => dt <= 0f ? 0f : 1f - Mathf.Exp(-dt / Mathf.Max(.0001f, seconds));
        static float Damp(float from, float to, float seconds, float dt) => Mathf.Lerp(from, to, Alpha(seconds, dt));
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
