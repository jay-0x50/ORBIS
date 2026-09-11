using UnityEngine;

namespace Orbis.Game.World
{
    /// <summary>Visual rotor only; shares the continent's wind without changing traversal or weather rules.</summary>
    public sealed class WorldWindmill : MonoBehaviour
    {
        // Art default: 12 degrees/second in ordinary wind. The FBX rotor faces local +Z.
        public float DegreesPerSecond=12;
        void Update()
        {
            float wind=Shader.GetGlobalVector("_OrbisWindParams").x;
            transform.Rotate(Vector3.forward,DegreesPerSecond*Mathf.Clamp(wind,.15f,3)*Time.deltaTime,Space.Self);
        }
    }
}
