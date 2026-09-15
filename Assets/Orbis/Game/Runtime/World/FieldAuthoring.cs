using System;
using Orbis.M4;
using UnityEngine;

namespace Orbis.Game.World
{
    [Serializable]
    public sealed class FieldAuthoringRegion
    {
        public M4RegionId Id;
        public GameObject Root;
    }

    /// <summary>The single editable field scene. Environment scenes are export products of this saved source.</summary>
    [DisallowMultipleComponent]
    public sealed class FieldAuthoring : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/Field.unity";
        public GameSceneEntry Entry;
        public FieldAuthoringRegion[] Regions = Array.Empty<FieldAuthoringRegion>();
    }
}
