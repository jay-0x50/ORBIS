using System;
using UnityEngine;

namespace Orbis.M0.Animation
{
    public enum HumanMotionSlot
    {
        Idle, Walk, Run, WalkLeft, WalkRight, WalkBack,
        RunLeft, RunRight, RunBack, Air,
        Attack1, Attack2, Attack3, Skill, Burst, Hurt, Dead,
        SteeringNeutral, SteeringLeft, SteeringRight,
        // Appended to preserve the original 20 serialized slot indices.
        TurnLeft90, TurnRight90, TurnLeft180, TurnRight180,
        StopLeft, StopRight // Append; existing24 serialized indices stay unchanged.
    }

    [Serializable]
    public sealed class HumanMotionClip
    {
        public HumanMotionSlot Slot;
        public AnimationClip Clip;
        // Author/retarget calibration: projected travel represented by one full cycle, in player metres.
        // Zero is deliberately INVALID for moving clips; no invented native stride is silently accepted.
        public float CycleDistanceMetres;
        public AnimationCurve LeftPlant = AnimationCurve.Constant(0f, 1f, 0f);
        public AnimationCurve RightPlant = AnimationCurve.Constant(0f, 1f, 0f);
        public string SourceLicense, CalibrationEvidence;
    }

    [Serializable]
    public sealed class HumanSoleCalibration
    {
        public bool Calibrated;
        public Vector3 SolePointInFootLocal;
        public Vector3 NeutralSoleInRoot; // Actual normalized Idle playback measurement, not initial runtime pose.
        public Vector3 SoleNormalInFootLocal = Vector3.up;
        public Vector3 ForwardInFootLocal = Vector3.forward;
        public string SourceModelHash, Evidence; // Exact shoe mesh/foot-rest measurement, not guessed offsets.
    }

    [CreateAssetMenu(menuName = "Orbis/Characters/Motion Profile")]
    public sealed class CharacterMotionProfile : ScriptableObject
    {
        public HumanMotionClip[] Clips = Array.Empty<HumanMotionClip>();
        public HumanSoleCalibration LeftSole = new HumanSoleCalibration(), RightSole = new HumanSoleCalibration();
        // Existing M0 gameplay speeds remain the blend thresholds. These never move the pawn.
        public float WalkThreshold = 2.5f, RunThreshold = 6f;
        // Visual defaults absent from the design document; verify on the same-model comparison.
        public float SpeedSmoothingSeconds = .10f, DirectionSmoothingSeconds = .08f;
        public float LocomotionFadeSeconds = .14f, ActionFadeSeconds = .07f, OverlayFadeSeconds = .10f;
        public float SteeringFullWeightYawSpeed = 360f, SteeringMaximumWeight = .5f;
        // Visual-only yaw defaults: gameplay still turns at its original720deg/s. Moving
        // turns keep their stride; stationary footstep clips are restricted to near-zero speed.
        public float VisualYawDegreesPerSecond = 360f, MaximumVisualYawLag = 175f;
        public float InPlaceTurnMaximumSpeed = .15f, InPlaceTurnMinimumAngle = 30f;
        public float MinimumStrideRate = .05f, MaximumStrideRate = 2.5f;
        public float ContactFadeSeconds = .055f, MaximumPelvisOffset = .14f;
        public float MaximumGroundCorrection = .32f, MaximumFootTiltDegrees = 38f;
        public float MaximumPlantDrift = .26f, LegReachFraction = .985f;
        public LayerMask GroundLayers = 1 << 8; // Player/target bodies and trigger volumes are excluded.

        public HumanMotionClip Get(HumanMotionSlot slot)
        {
            foreach (var entry in Clips) if (entry != null && entry.Slot == slot) return entry;
            return null;
        }

        public void ValidateForBuild()
        {
            if (!Finite(WalkThreshold) || !Finite(RunThreshold) || !(WalkThreshold > 0f && RunThreshold > WalkThreshold))
                throw new InvalidOperationException("Invalid blend thresholds.");
            foreach (float value in new[] { SpeedSmoothingSeconds, DirectionSmoothingSeconds, LocomotionFadeSeconds,
                ActionFadeSeconds, OverlayFadeSeconds, SteeringFullWeightYawSpeed, ContactFadeSeconds,
                MaximumPelvisOffset, MaximumGroundCorrection, MaximumPlantDrift })
                if (!Finite(value) || value <= 0f) throw new InvalidOperationException("Motion timing/reach values must be finite and positive.");
            if (!Finite(SteeringMaximumWeight) || SteeringMaximumWeight < 0f || SteeringMaximumWeight > 1f ||
                !Finite(MinimumStrideRate) || !Finite(MaximumStrideRate) || MinimumStrideRate <= 0f || MaximumStrideRate < MinimumStrideRate ||
                !Finite(MaximumFootTiltDegrees) || MaximumFootTiltDegrees < 0f || MaximumFootTiltDegrees > 60f ||
                !Finite(LegReachFraction) || LegReachFraction <= 0f || LegReachFraction >= 1f || GroundLayers.value == 0)
                throw new InvalidOperationException("Invalid bounded motion/IK settings.");
            if (!Finite(VisualYawDegreesPerSecond) || VisualYawDegreesPerSecond <= 0f || !Finite(MaximumVisualYawLag) ||
                MaximumVisualYawLag < 30f || MaximumVisualYawLag >= 180f || !Finite(InPlaceTurnMaximumSpeed) ||
                InPlaceTurnMaximumSpeed < 0f || InPlaceTurnMaximumSpeed > .3f || !Finite(InPlaceTurnMinimumAngle) ||
                InPlaceTurnMinimumAngle <= 0f || InPlaceTurnMinimumAngle >= MaximumVisualYawLag)
                throw new InvalidOperationException("Invalid visual yaw/near-stationary step settings.");
            if (Clips == null || Clips.Length != Enum.GetValues(typeof(HumanMotionSlot)).Length)
                throw new InvalidOperationException("Provide exactly one entry for every motion slot.");
            foreach (HumanMotionSlot slot in Enum.GetValues(typeof(HumanMotionSlot)))
            {
                var entry = Get(slot);
                if (entry == null || entry.Clip == null || !entry.Clip.isHumanMotion || entry.Clip.length <= 0f)
                    throw new InvalidOperationException("Supply a valid retargeted Humanoid clip: " + slot);
                if (string.IsNullOrWhiteSpace(entry.SourceLicense)) throw new InvalidOperationException("Missing motion provenance: " + slot);
                if (IsMoving(slot) && (!Finite(entry.CycleDistanceMetres) || !(entry.CycleDistanceMetres > 0f) || string.IsNullOrWhiteSpace(entry.CalibrationEvidence)))
                    throw new InvalidOperationException("Measured/authored stride and evidence required: " + slot);
                if (slot <= HumanMotionSlot.RunBack || IsTurn(slot) || IsStop(slot))
                {
                    ValidateContact(entry.LeftPlant, slot + " left");
                    ValidateContact(entry.RightPlant, slot + " right");
                }
            }
            ValidateSole(LeftSole, "left"); ValidateSole(RightSole, "right");
        }
        static void ValidateContact(AnimationCurve curve, string label)
        {
            if (curve == null || curve.length == 0) throw new InvalidOperationException("Foot contact annotation missing: " + label);
            bool contacts = false;
            for (int i = 0; i <= 100; i++)
            {
                float value = curve.Evaluate(i / 100f);
                if (float.IsNaN(value) || float.IsInfinity(value) || value < -.001f || value > 1.001f)
                    throw new InvalidOperationException("Foot contact curve outside 0..1: " + label);
                contacts |= value > .5f;
            }
            if (!contacts) throw new InvalidOperationException("No planted phase annotated: " + label);
        }
        static void ValidateSole(HumanSoleCalibration sole, string label)
        {
            if (sole == null || !sole.Calibrated || string.IsNullOrWhiteSpace(sole.SourceModelHash) || string.IsNullOrWhiteSpace(sole.Evidence) ||
                !Finite(sole.SolePointInFootLocal) || !Finite(sole.NeutralSoleInRoot) || Mathf.Abs(sole.NeutralSoleInRoot.x)<.02f ||
                !Finite(sole.SoleNormalInFootLocal) || !Finite(sole.ForwardInFootLocal) ||
                Mathf.Abs(sole.SoleNormalInFootLocal.sqrMagnitude - 1f) > .01f || Mathf.Abs(sole.ForwardInFootLocal.sqrMagnitude - 1f) > .01f ||
                Mathf.Abs(Vector3.Dot(sole.SoleNormalInFootLocal, sole.ForwardInFootLocal)) > .05f)
                throw new InvalidOperationException("Calibrated shoe sole reference is required: " + label);
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        public static bool IsMoving(HumanMotionSlot slot) => slot >= HumanMotionSlot.Walk && slot <= HumanMotionSlot.RunBack;
        public static bool IsTurn(HumanMotionSlot slot) => slot >= HumanMotionSlot.TurnLeft90 && slot <= HumanMotionSlot.TurnRight180;
        public static bool IsStop(HumanMotionSlot slot) => slot == HumanMotionSlot.StopLeft || slot == HumanMotionSlot.StopRight;
    }
}
