using Orbis.M4;
using UnityEngine;

namespace Orbis.Game.World
{
    /// <summary>Authored visual destination for inspection/profiling; owns no quest or exploration rewards.</summary>
    public sealed class WorldLandmark : MonoBehaviour
    {
        public string Label;
        public M4RegionId Region;
        public Vector3 LocalLookPoint=Vector3.up*4;
        public Vector3 LookPoint=>transform.TransformPoint(LocalLookPoint);
    }
}
