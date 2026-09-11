namespace Orbis.M0
{
    /// <summary>Optional passive traversal while Skill/Burst/Hurt/Dead suppress player controls.</summary>
    public interface IPlayerActionTraversal
    {
        /// <returns>True if passive traversal handled movement; otherwise the motor applies gravity.</returns>
        bool TickActionTraversal(float deltaTime);
    }

    /// <summary>Optional movement modes, independent of any later milestone assembly.</summary>
    public interface IPlayerTraversal
    {
        /// <returns>True when traversal has moved the controller and ordinary locomotion should skip this frame.</returns>
        bool TickTraversal(float deltaTime);
        void AfterLocomotion(float deltaTime);
        void ResetTraversal();
    }
}
