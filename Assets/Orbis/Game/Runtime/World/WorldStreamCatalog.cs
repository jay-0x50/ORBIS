using System;
using System.Collections.Generic;
using Orbis.M4;
using UnityEngine;

namespace Orbis.Game.World
{
    [Serializable]
    public sealed class WorldStreamRegion
    {
        public M4RegionId Id;
        public string Address;
        public Bounds WorldBounds;
    }

    /// <summary>Environment scene ownership only. Biome blending and persistent M4 encounters are independent.</summary>
    [CreateAssetMenu(menuName="Orbis/World/Environment Stream Catalog")]
    public sealed class WorldStreamCatalog : ScriptableObject
    {
        public WorldStreamRegion[] Regions=Array.Empty<WorldStreamRegion>();
        // Unspecified defaults: preload before the 700m environment range, keep a 150m exit margin.
        // The 2km island's terrain/collision and small M4 encounters stay resident.
        [Min(0)] public float LoadDistance=700f;
        [Min(0)] public float UnloadDistance=850f;
        [Min(.05f)] public float RefreshSeconds=.25f;
        public Vector3 HubCenter=Vector3.zero;
        // The central overlook can see adjacent districts, so preload every environment while near it.
        [Min(0)] public float HubPreloadRadius=140f;

        public void Validate()
        {
            if(Regions==null||Regions.Length!=5)throw new InvalidOperationException("Five environment region entries are required.");
            if(!Finite(LoadDistance)||!Finite(UnloadDistance)||LoadDistance<0||UnloadDistance<=LoadDistance||
                !Finite(RefreshSeconds)||RefreshSeconds<.05f||!Finite(HubPreloadRadius)||HubPreloadRadius<0)
                throw new InvalidOperationException("Environment distances/timing must be finite and include an unload margin.");
            var ids=new HashSet<M4RegionId>();var addresses=new HashSet<string>();
            foreach(var entry in Regions)
            {
                if(entry==null||!Enum.IsDefined(typeof(M4RegionId),entry.Id)||!ids.Add(entry.Id)||
                    string.IsNullOrWhiteSpace(entry.Address)||!entry.Address.StartsWith("orbis.world.environment.",StringComparison.Ordinal)||
                    !addresses.Add(entry.Address)||!Finite(entry.WorldBounds.center)||!Finite(entry.WorldBounds.size)||
                    entry.WorldBounds.size.x<=0||entry.WorldBounds.size.z<=0)
                    throw new InvalidOperationException("Environment entries need unique IDs, addresses and finite horizontal bounds.");
            }
            if(!Finite(HubCenter))throw new InvalidOperationException("The stream hub must be finite.");
        }

        internal static float HorizontalDistanceSquared(Vector3 point,Bounds bounds)
        {
            float x=Mathf.Max(0,Mathf.Max(bounds.min.x-point.x,point.x-bounds.max.x));
            float z=Mathf.Max(0,Mathf.Max(bounds.min.z-point.z,point.z-bounds.max.z));
            return x*x+z*z;
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static bool Finite(Vector3 value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z);
    }
}
