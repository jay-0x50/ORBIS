using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis.Game.World
{
    [Serializable]
    public struct WorldGrassPlacement
    {
        public Vector3 Position;
        public float Yaw;
        public Vector3 Scale;
    }

    [Serializable]
    public sealed class WorldGrassCell
    {
        public Vector2Int Coordinate;
        public Bounds PositionBounds;
        public Vector3 MaximumScale;
        public WorldGrassPlacement[] Instances;
    }

    /// <summary>World-space, editor-authored grass placements. Binary serialization avoids millions of YAML lines.</summary>
    [PreferBinarySerialization]
    [CreateAssetMenu(menuName="Orbis/World/Grass Data")]
    public sealed class WorldGrassData : ScriptableObject
    {
        [SerializeField] float cellSize=32f;
        [SerializeField] WorldGrassCell[] cells=Array.Empty<WorldGrassCell>();
        [SerializeField] int instanceCount;
        public float CellSize=>cellSize;
        public WorldGrassCell[] Cells=>cells;
        public int InstanceCount=>instanceCount;
        public long ApproximatePlacementBytes=>(long)instanceCount*28L+(long)cells.Length*48L;

        /// <summary>Authoring API only: pass sampled terrain heights; no runtime terrain raycasts or GameObjects per blade.</summary>
        public void SetInstances(IReadOnlyList<WorldGrassPlacement> placements,float cellSize=32f)
        {
            if(placements==null)throw new ArgumentNullException(nameof(placements));
            if(!Finite(cellSize)||cellSize<4||cellSize>128)throw new ArgumentOutOfRangeException(nameof(cellSize),"Grass cells must be 4–128 metres.");
            var groups=new Dictionary<Vector2Int,List<WorldGrassPlacement>>();
            for(int i=0;i<placements.Count;i++)
            {
                var p=placements[i];
                if(!Finite(p.Position)||!Finite(p.Scale)||!Finite(p.Yaw)||p.Scale.x<=0||p.Scale.y<=0||p.Scale.z<=0)
                    throw new ArgumentException("Grass placement "+i+" needs finite coordinates and positive scale.");
                var key=new Vector2Int(Mathf.FloorToInt(p.Position.x/cellSize),Mathf.FloorToInt(p.Position.z/cellSize));
                if(!groups.TryGetValue(key,out var list)){list=new List<WorldGrassPlacement>();groups.Add(key,list);}
                list.Add(p);
            }
            // Stable cell order makes regenerated assets reproducible, including negative island coordinates.
            var keys=new List<Vector2Int>(groups.Keys);
            keys.Sort((a,b)=>a.x!=b.x?a.x.CompareTo(b.x):a.y.CompareTo(b.y));
            var result=new WorldGrassCell[keys.Count];
            for(int i=0;i<keys.Count;i++)
            {
                var instances=groups[keys[i]].ToArray();
                var bounds=new Bounds(instances[0].Position,Vector3.zero);var scale=Vector3.zero;
                foreach(var p in instances){bounds.Encapsulate(p.Position);scale=Vector3.Max(scale,p.Scale);}
                result[i]=new WorldGrassCell{Coordinate=keys[i],PositionBounds=bounds,MaximumScale=scale,Instances=instances};
            }
            cells=result;this.cellSize=cellSize;instanceCount=placements.Count;
        }
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
    }
}
