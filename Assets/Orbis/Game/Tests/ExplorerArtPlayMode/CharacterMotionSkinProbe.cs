using System;
using System.Collections.Generic;
using System.Linq;
using Orbis.M0.Animation;
using UnityEngine;

namespace Orbis.Game.Tests
{
    // Test-only observation; no posing/IK correction. Pick actual skin vertices near the
    // measured source sole plane once, then keep these exact vertex IDs throughout capture.
    public sealed class CharacterMotionSkinProbe : IDisposable
    {
        [Serializable] public sealed class FootSample
        {
            public string side;
            public float contact, effectiveContact, minimumY, maximumY, recoveryArcHeight, recoveryProjection, recoveryProgress;
            public bool released, recovering, animationHandoff, terrainAdapted;
            public float terrainSourceOffset, terrainClearanceLift, terrainReachProjection, terrainUnresolvedClearance, appliedSolveWeight;
            public int calibratedSolePointCount, terrainSurfaceRayCount;
            public bool terrainGeometryReady, terrainConstraintFeasible;
            public int terrainGeometryAttempts;
            public string terrainGeometryError;
            public float terrainFinalVerticalCorrection;
            public bool terrainSupportPresent,terrainHandoffActive,terrainHandoff;
            public float terrainPelvisRemainder,terrainHandoffClearance;
            public int terrainGateRayCount;
            public string guardReason;
            public float solveWeight;
            // x=pre-recovery target drift(m), y=sole twist(deg), z=pre-recovery reach excess(m).
            // These are solver diagnostics, not replacements for measured skin contact.
            public Vector3 guardMeasurements;
            public Vector3 calibratedSoleWorld;
            public int[] pointIds;
            public Vector3[] pointsWorld;
        }
        sealed class Binding { public SkinnedMeshRenderer skin; public int[] left, right; }
        readonly List<Binding> bindings = new List<Binding>();
        readonly Mesh scratch = new Mesh();
        readonly Animator animator;
        readonly CharacterMotionProfile profile;
        readonly HumanFootIK ik;
        readonly Transform left, right;
        public CharacterMotionSkinProbe(Animator animator, CharacterMotionProfile profile)
        {
            this.animator = animator; this.profile = profile;
            ik=animator.GetComponent<HumanFootIK>();
            left = animator.GetBoneTransform(HumanBodyBones.LeftFoot); right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            var lt = animator.GetBoneTransform(HumanBodyBones.LeftToes); var rt = animator.GetBoneTransform(HumanBodyBones.RightToes);
            foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null || skin.name.StartsWith("Art Outline") || skin.name.StartsWith("M3 Silhouette")) continue;
                scratch.Clear(); skin.BakeMesh(scratch,true);
                var world = scratch.vertices.Select(skin.transform.TransformPoint).ToArray();
                var l = Select(skin,world,left,lt,profile.LeftSole); var r = Select(skin,world,right,rt,profile.RightSole);
                if (l.Length > 0 || r.Length > 0) bindings.Add(new Binding { skin=skin,left=l,right=r });
            }
            if (bindings.Sum(b=>b.left.Length)<12 || bindings.Sum(b=>b.right.Length)<12)
                throw new InvalidOperationException("Missing measured skin sole points for actual motion capture.");
        }
        static int[] Select(SkinnedMeshRenderer skin,Vector3[] world,Transform foot,Transform toe,HumanSoleCalibration calibration)
        {
            int fi=Array.IndexOf(skin.bones,foot), ti=Array.IndexOf(skin.bones,toe);
            if (fi<0) return Array.Empty<int>();
            var weights=skin.sharedMesh.boneWeights;
            var ids=new List<int>();
            for (int i=0;i<weights.Length;i++)
            {
                var w=weights[i]; float total=0;
                if(w.boneIndex0==fi || w.boneIndex0==ti) total+=w.weight0;
                if(w.boneIndex1==fi || w.boneIndex1==ti) total+=w.weight1;
                if(w.boneIndex2==fi || w.boneIndex2==ti) total+=w.weight2;
                if(w.boneIndex3==fi || w.boneIndex3==ti) total+=w.weight3;
                // 4mm source-rest plane selection, converted by the actual foot scale;
                // fixed for the sequence, never re-select a convenient contact point each frame.
                float distance=Vector3.Dot(foot.InverseTransformPoint(world[i])-calibration.SolePointInFootLocal,calibration.SoleNormalInFootLocal)*foot.lossyScale.y;
                if(total>.25f && Mathf.Abs(distance)<=.004f) ids.Add(i);
            }
            return ids.ToArray();
        }
        public FootSample[] Measure()
        {
            var lp=new List<Vector3>(); var rp=new List<Vector3>(); var li=new List<int>(); var ri=new List<int>();
            for(int i=0;i<bindings.Count;i++)
            {
                var b=bindings[i]; scratch.Clear(); b.skin.BakeMesh(scratch,true); var v=scratch.vertices;
                foreach(int n in b.left) { li.Add((i<<24)|n); lp.Add(b.skin.transform.TransformPoint(v[n])); }
                foreach(int n in b.right) { ri.Add((i<<24)|n); rp.Add(b.skin.transform.TransformPoint(v[n])); }
            }
            return new[]{Sample("Left",lp,li,left,profile.LeftSole,HumanAnimationDriver.LeftPlantParameter),Sample("Right",rp,ri,right,profile.RightSole,HumanAnimationDriver.RightPlantParameter)};
        }
        FootSample Sample(string side,List<Vector3> p,List<int> ids,Transform foot,HumanSoleCalibration c,string parameter) => new FootSample
        { side=side,contact=animator.GetFloat(parameter),pointIds=ids.ToArray(),pointsWorld=p.ToArray(),
            effectiveContact=ik==null?animator.GetFloat(parameter):ik.EffectiveContact(side=="Left"),
            released=ik!=null && ik.ContactReleased(side=="Left"),recovering=ik!=null && ik.ContactRecovering(side=="Left"),
            recoveryArcHeight=ik==null?0f:ik.ContactRecoveryArcHeight(side=="Left"),
            recoveryProjection=ik==null?0f:ik.ContactRecoveryProjection(side=="Left"),
            recoveryProgress=ik==null?0f:ik.ContactRecoveryProgress(side=="Left"),
            animationHandoff=ik!=null && ik.ContactAnimationHandoff(side=="Left"),
            terrainAdapted=ik!=null && ik.TerrainAdapted(side=="Left"),
            terrainSourceOffset=ik==null?0f:ik.TerrainSourceOffset(side=="Left"),
            terrainClearanceLift=ik==null?0f:ik.TerrainClearanceLift(side=="Left"),
            terrainReachProjection=ik==null?0f:ik.TerrainReachProjection(side=="Left"),
            terrainUnresolvedClearance=ik==null?0f:ik.TerrainUnresolvedClearance(side=="Left"),
            appliedSolveWeight=ik==null?0f:ik.AppliedSolveWeight(side=="Left"),
            calibratedSolePointCount=ik==null?0:ik.CalibratedSolePointCount(side=="Left"),
            terrainSurfaceRayCount=ik==null?0:ik.TerrainSurfaceRayCount(side=="Left"),
            terrainGeometryReady=ik!=null && ik.TerrainGeometryReady,
            terrainGeometryAttempts=ik==null?0:ik.TerrainGeometryAttempts,
            terrainGeometryError=ik==null?null:ik.TerrainGeometryError,
            terrainFinalVerticalCorrection=ik==null?0f:ik.TerrainFinalVerticalCorrection(side=="Left"),
            terrainConstraintFeasible=ik==null || ik.TerrainConstraintFeasible(side=="Left"),
            terrainSupportPresent=ik!=null && ik.TerrainSupportPresent,
            terrainHandoffActive=ik!=null && ik.TerrainHandoffActive,
            terrainHandoff=ik!=null && ik.TerrainHandoff(side=="Left"),
            terrainPelvisRemainder=ik==null?0f:ik.TerrainPelvisRemainder,
            terrainHandoffClearance=ik==null?0f:ik.TerrainHandoffClearance(side=="Left"),
            terrainGateRayCount=ik==null?0:ik.TerrainGateRayCount(side=="Left"),
            guardReason=ik==null?null:ik.ContactGuardReason(side=="Left"),
            solveWeight=ik==null?0f:ik.ContactSolveWeight(side=="Left"),
            guardMeasurements=ik==null?Vector3.zero:ik.ContactGuardMeasurements(side=="Left"),
            minimumY=p.Min(v=>v.y),maximumY=p.Max(v=>v.y),calibratedSoleWorld=foot.TransformPoint(c.SolePointInFootLocal) };
        public void Dispose() { UnityEngine.Object.Destroy(scratch); }
    }
}
