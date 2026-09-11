using System.Collections.Generic;
using UnityEngine;

namespace Orbis.M2
{
    /// <summary>A bounded water body. Its trigger never participates in solid world collision.</summary>
    [RequireComponent(typeof(BoxCollider))]
    [DisallowMultipleComponent]
    public sealed class WaterVolume : MonoBehaviour
    {
        private static readonly HashSet<WaterVolume> volumes = new HashSet<WaterVolume>();
        [SerializeField] private float localSurfaceY = 0.5f;
        private BoxCollider volume;

        public static IReadOnlyCollection<WaterVolume> ActiveVolumes => volumes;
        public Bounds Bounds => Volume.bounds;
        public float SurfaceY => transform.TransformPoint(new Vector3(0f, localSurfaceY, 0f)).y;
        private BoxCollider Volume => volume != null ? volume : (volume = GetComponent<BoxCollider>());

        private void Awake()
        {
            Volume.isTrigger = true;
        }

        private void OnEnable() => volumes.Add(this);
        private void OnDisable() => volumes.Remove(this);

        public void Configure(Vector3 localCenter, Vector3 localSize, float worldSurfaceY)
        {
            Volume.center = localCenter;
            Volume.size = localSize;
            Volume.isTrigger = true;
            localSurfaceY = transform.InverseTransformPoint(new Vector3(transform.position.x,
                worldSurfaceY, transform.position.z)).y;
        }

        public bool Contains(Vector3 worldPoint)
        {
            return isActiveAndEnabled && Volume.enabled && Volume.bounds.Contains(worldPoint);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.1f, 0.7f, 1f, 0.5f);
            Gizmos.DrawWireCube(Bounds.center, Bounds.size);
        }
    }
}