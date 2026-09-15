using UnityEngine;

namespace Orbis.M0.Animation
{
    // Owned presentation state. The pivot is an explicit child wrapper above Animator,
    // never the gameplay pawn, camera target, root-motion receiver or rig bone.
    public sealed class HumanVisualTurn
    {
        Transform pivot;
        Quaternion restLocalRotation;
        float offset, elapsed;
        string step;
        public bool IsStepping => step != null;
        public string Step => step;
        public float NormalizedTime { get; private set; }
        public float YawOffset => offset;

        public void Bind(Transform value)
        {
            if (pivot == value) return;
            Reset(); pivot = value;
            if (pivot != null) restLocalRotation = pivot.localRotation;
        }
        public void Reset()
        {
            if (pivot != null) pivot.localRotation = restLocalRotation;
            offset = elapsed = NormalizedTime = 0f; step = null;
        }
        public void Observe(CharacterMotionObservation observation,float speed,bool allowed,CharacterMotionProfile profile)
        {
            if (pivot == null) return;
            if (observation.Root == pivot || (observation.Root != null && observation.Root.IsChildOf(pivot)))
                throw new System.InvalidOperationException("The visual yaw pivot must not contain the gameplay pawn.");
            if (!allowed || observation.Discontinuity) { Reset(); return; }
            float dt = Mathf.Max(0f,observation.DeltaTime);
            float actualYaw = observation.SignedYawDegreesPerSecond*dt;
            // Cancel only the observed actor yaw in this child, then let the visual facing
            // catch up at its own bounded rate. Physics/camera rotation remains untouched.
            // Keep a signed accumulated lag. Wrapping175+24 to-161 would reverse the
            // presentation turn during sustained camera orbit near the180degree boundary.
            offset = Mathf.Clamp(offset-actualYaw,-profile.MaximumVisualYawLag,profile.MaximumVisualYawLag);
            if (speed > profile.InPlaceTurnMaximumSpeed) { step = null; elapsed = 0f; }
            else if (step == null && Mathf.Abs(offset) >= profile.InPlaceTurnMinimumAngle)
            { step = Name(offset); elapsed = 0f; }
            if (step != null)
            {
                // A longer actual actor turn can promote a90deg candidate to180deg without
                // restarting the elapsed clock or changing the pawn's final yaw.
                if (Mathf.Abs(offset) > 110f && step.EndsWith("90")) step = Name(offset,true);
                elapsed += dt;
                HumanMotionSlot slot = (HumanMotionSlot)System.Enum.Parse(typeof(HumanMotionSlot),step);
                float duration = profile.Get(slot).Clip.length;
                NormalizedTime = Mathf.Clamp01(elapsed/Mathf.Max(.001f,duration));
                bool airborneStep = (NormalizedTime > .04f && NormalizedTime < .46f) ||
                    (NormalizedTime > .47f && NormalizedTime < .94f);
                if (airborneStep) offset = Mathf.MoveTowards(offset,0f,profile.VisualYawDegreesPerSecond*dt);
                if (NormalizedTime >= 1f)
                {
                    if (Mathf.Abs(offset) >= profile.InPlaceTurnMinimumAngle) { step = Name(offset); elapsed = NormalizedTime = 0f; }
                    else { step = null; elapsed = 0f; }
                }
            }
            else offset = Mathf.MoveTowards(offset,0f,profile.VisualYawDegreesPerSecond*dt);
            pivot.localRotation = Quaternion.AngleAxis(offset,Vector3.up)*restLocalRotation;
        }
        static string Name(float lag,bool force180=false) => "Turn" + (lag > 0f ? "Left" : "Right") + (force180 || Mathf.Abs(lag) > 135f ? "180" : "90");
    }
}
