using UnityEngine;

namespace Orbis.M0.Animation
{
    // Candidate only: this directory is outside Assets until STEP 2 and the same-model baseline pass.
    public enum CharacterMotionMode { Ground, Air, Climbing, Gliding, Swimming }

    public struct CharacterMotionObservation
    {
        public Transform Root;
        public Vector3 WorldVelocity;
        public Vector2 DesiredMove;
        public float DeltaTime, SignedYawDegreesPerSecond;
        public bool Grounded, Discontinuity;
        public PlayerActionState GameplayState;
    }

    public interface ICharacterAnimationDriver
    {
        bool IsReady { get; }
        Animator Animator { get; }
        void Observe(CharacterMotionObservation observation);
        void SampleAttack(int step, float normalizedTime);
        void ClearAttack();
        void SampleAction(PlayerActionState state, float normalizedTime, bool externallyClocked);
        void ClearAction(PlayerActionState owner);
        void SetTraversalMode(CharacterMotionMode mode);
        void ResetPresentation();
        void Release();
    }

    public static class CharacterAnimationBinding
    {
        public static ICharacterAnimationDriver Resolve(Animator animator)
        {
            if (animator == null) return null;
            ICharacterAnimationDriver selected = null;
            foreach (var component in animator.GetComponents<MonoBehaviour>())
                if (component is ICharacterAnimationDriver driver)
                {
                    if (selected != null) throw new System.InvalidOperationException("Multiple character animation drivers on " + animator.name);
                    selected = driver;
                }
            if (selected == null) return null; // Only an absent driver selects the original seven-clip path.
            if (!selected.IsReady) throw new System.InvalidOperationException("Character animation driver is present but profile/Avatar/controller contract is invalid on " +
                animator.name + " (controller " + (animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "missing") + "). " +
                (selected is HumanAnimationDriver human ? human.ValidationError : ""));
            return selected;
        }
    }

    /// <summary>Post-controller observation; never changes physics, input speed or rotation.</summary>
    public sealed class CharacterMotionTracker
    {
        Vector3 previousPosition, previousForward;
        bool initialized;
        public void Reset() => initialized = false;

        public CharacterMotionObservation Sample(Transform root, Vector2 desiredMove, bool grounded,
            PlayerActionState state, float deltaTime)
        {
            var result = new CharacterMotionObservation
            { Root = root, DesiredMove = desiredMove, Grounded = grounded, GameplayState = state, DeltaTime = deltaTime };
            Vector3 position = root.position, forward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            Vector3 delta = initialized ? position - previousPosition : Vector3.zero;
            // Observation default only: >2m in one frame is a warp, not a 60m/s gait request.
            result.Discontinuity = !initialized || delta.sqrMagnitude > 4f;
            if (!result.Discontinuity && deltaTime > .000001f)
            {
                result.WorldVelocity = delta / deltaTime;
                result.SignedYawDegreesPerSecond = Vector3.SignedAngle(previousForward, forward, Vector3.up) / deltaTime;
            }
            previousPosition = position; previousForward = forward; initialized = true;
            return result;
        }
    }
}
