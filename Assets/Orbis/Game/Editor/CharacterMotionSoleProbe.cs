#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Orbis.Game.Editor
{
    // Captures actual shoe vertices once in imported rest, then re-bakes their animated
    // positions. These are skin observations, separate from the IK's foot-local proxy.
    public sealed class CharacterMotionSoleProbe : IDisposable
    {
        [Serializable] public struct Measurement
        {
            public int vertices;
            public float minimumY, maximumY;
            public Vector3 centroid;
            public int[] pointIds;
            public Vector3[] pointsWorld;
        }
        sealed class Binding
        {
            public SkinnedMeshRenderer skin;
            public int[] left, right;
        }
        readonly List<Binding> bindings = new List<Binding>();
        readonly Mesh scratch = new Mesh();
        public CharacterMotionSoleProbe(Animator animator)
        {
            Transform lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot), lt = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform rf = animator.GetBoneTransform(HumanBodyBones.RightFoot), rt = animator.GetBoneTransform(HumanBodyBones.RightToes);
            foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                scratch.Clear(); skin.BakeMesh(scratch,true);
                var world = scratch.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
                var weights = skin.sharedMesh.boneWeights;
                var left = Select(skin,weights,world,lf,lt); var right = Select(skin,weights,world,rf,rt);
                if (left.Length > 0 || right.Length > 0) bindings.Add(new Binding { skin = skin,left = left,right = right });
            }
            if (bindings.Sum(b => b.left.Length) < 12 || bindings.Sum(b => b.right.Length) < 12)
                throw new InvalidOperationException("Insufficient actual rest shoe sole vertices for playback probe.");
        }
        static int[] Select(SkinnedMeshRenderer skin,BoneWeight[] weights,Vector3[] world,Transform foot,Transform toes)
        {
            int f = Array.IndexOf(skin.bones,foot), t = Array.IndexOf(skin.bones,toes);
            if (f < 0) return Array.Empty<int>();
            var indices = new List<int>();
            for (int i = 0; i < weights.Length; ++i)
            {
                var w = weights[i]; float weight = 0f;
                if (w.boneIndex0 == f || w.boneIndex0 == t) weight += w.weight0;
                if (w.boneIndex1 == f || w.boneIndex1 == t) weight += w.weight1;
                if (w.boneIndex2 == f || w.boneIndex2 == t) weight += w.weight2;
                if (w.boneIndex3 == f || w.boneIndex3 == t) weight += w.weight3;
                if (weight > .25f) indices.Add(i);
            }
            if (indices.Count == 0) return Array.Empty<int>();
            float min = indices.Min(i => world[i].y);
            // Same 4mm imported-rest sole plane used by author calibration; no pose-dependent
            // re-selection that could silently switch from sole to another shoe surface.
            return indices.Where(i => world[i].y <= min + .004f).ToArray();
        }
        public void Measure(out Measurement left,out Measurement right)
        {
            left = new Measurement { minimumY = float.PositiveInfinity,maximumY = float.NegativeInfinity };
            right = left;
            var leftPoints = new List<Vector3>(); var rightPoints = new List<Vector3>();
            var leftIds = new List<int>(); var rightIds = new List<int>();
            for (int i = 0; i < bindings.Count; ++i)
            {
                var binding = bindings[i];
                scratch.Clear(); binding.skin.BakeMesh(scratch,true); Vector3[] vertices = scratch.vertices;
                Accumulate(binding.skin,vertices,binding.left,ref left); Accumulate(binding.skin,vertices,binding.right,ref right);
                Append(binding.skin,vertices,binding.left,i,leftIds,leftPoints); Append(binding.skin,vertices,binding.right,i,rightIds,rightPoints);
            }
            left.centroid /= left.vertices; right.centroid /= right.vertices;
            left.pointIds = leftIds.ToArray(); left.pointsWorld = leftPoints.ToArray();
            right.pointIds = rightIds.ToArray(); right.pointsWorld = rightPoints.ToArray();
        }
        static void Append(SkinnedMeshRenderer skin,Vector3[] vertices,int[] indices,int binding,List<int> ids,List<Vector3> points)
        {
            foreach (int index in indices) { ids.Add((binding<<24)|index); points.Add(skin.transform.TransformPoint(vertices[index])); }
        }
        static void Accumulate(SkinnedMeshRenderer skin,Vector3[] vertices,int[] indices,ref Measurement measurement)
        {
            foreach (int index in indices)
            {
                Vector3 v = skin.transform.TransformPoint(vertices[index]);
                measurement.minimumY = Mathf.Min(measurement.minimumY,v.y); measurement.maximumY = Mathf.Max(measurement.maximumY,v.y);
                measurement.centroid += v; measurement.vertices++;
            }
        }
        public void Dispose() => UnityEngine.Object.DestroyImmediate(scratch);
    }
}
#endif
