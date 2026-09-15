using System;
using System.Collections.Generic;
using Orbis.M0;
using Orbis.M0.Animation;
using UnityEngine;

namespace Orbis.Game.Tests
{
    // Test-only observation after the live Animator/IK. No Animator.Update or pose sampling.
    [DefaultExecutionOrder(32000)]
    public sealed class CharacterMotionLifecycleObserver : MonoBehaviour
    {
        [Serializable] public sealed class Snapshot
        {
            public string label,state;
            public int unityFrame,geometryAttempts;
            public bool lateFrame,visualActive,grounded,allowIK,support,handoff,geometryReady,finite;
            public float remainder,minimumSurfaceDistance,maximumSurfaceDistance;
            public int missingSurfacePoints;
            public Vector3 root,hips,leftFoot,rightFoot;
            public float leftWeight,rightWeight;
            public bool leftReleased,rightReleased,leftRecovering,rightRecovering;
            public CharacterMotionSkinProbe.FootSample[] feet;
        }
        public readonly List<Snapshot> Captures=new List<Snapshot>();
        public Snapshot Latest { get; private set; }
        public Snapshot LastCapture { get; private set; }
        public int CompletedRequests { get; private set; }
        Animator animator;
        PlayerMotor motor;
        HumanAnimationDriver driver;
        HumanFootIK ik;
        MeshCollider surface;
        CharacterMotionSkinProbe skin;
        string pending;

        public void Configure(Animator source,PlayerMotor player,MeshCollider ownedSurface)
        {
            animator=source; motor=player; surface=ownedSurface;
            driver=source.GetComponent<HumanAnimationDriver>(); ik=source.GetComponent<HumanFootIK>();
            // The caller first establishes the same neutral flat pose used by the existing capture.
            skin=new CharacterMotionSkinProbe(source,driver.Profile);
        }
        public void Request(string label)
        {
            if(pending!=null) throw new InvalidOperationException("A lifecycle snapshot is already pending.");
            pending=label;
        }
        public Snapshot Immediate(string label)
        {
            var sample=Read(label,false,false); Captures.Add(sample); return sample;
        }
        void LateUpdate()
        {
            if(skin==null) return;
            Latest=Read(pending??"observation",true,pending!=null);
            if(pending!=null) { LastCapture=Latest; Captures.Add(Latest); pending=null; CompletedRequests++; }
        }
        Snapshot Read(string label,bool late,bool measureSkin)
        {
            var value=new Snapshot
            {
                label=label,lateFrame=late,unityFrame=Time.frameCount,state=motor.State.ToString(),
                visualActive=animator.gameObject.activeInHierarchy,grounded=motor.IsGrounded,
                allowIK=driver.AllowFootIK,support=ik.TerrainSupportPresent,handoff=ik.TerrainHandoffActive,
                geometryReady=ik.TerrainGeometryReady,geometryAttempts=ik.TerrainGeometryAttempts,
                remainder=ik.TerrainPelvisRemainder,root=motor.transform.position,
                hips=animator.GetBoneTransform(HumanBodyBones.Hips).position,
                leftFoot=animator.GetBoneTransform(HumanBodyBones.LeftFoot).position,
                rightFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot).position,
                leftWeight=ik.AppliedSolveWeight(true),rightWeight=ik.AppliedSolveWeight(false),
                leftReleased=ik.ContactReleased(true),rightReleased=ik.ContactReleased(false),
                leftRecovering=ik.ContactRecovering(true),rightRecovering=ik.ContactRecovering(false)
            };
            value.finite=Finite(value.root)&&Finite(value.hips)&&Finite(value.leftFoot)&&Finite(value.rightFoot);
            if(measureSkin)
            {
                value.feet=skin.Measure();
                value.minimumSurfaceDistance=float.PositiveInfinity;
                value.maximumSurfaceDistance=float.NegativeInfinity;
                foreach(var foot in value.feet)
                foreach(var p in foot.pointsWorld)
                {
                    value.finite&=Finite(p);
                    if(surface.Raycast(new Ray(p+Vector3.up*5f,Vector3.down),out var hit,15f))
                    {
                        float distance=Vector3.Dot(p-hit.point,hit.normal);
                        value.minimumSurfaceDistance=Mathf.Min(value.minimumSurfaceDistance,distance);
                        value.maximumSurfaceDistance=Mathf.Max(value.maximumSurfaceDistance,distance);
                    }
                    else value.missingSurfacePoints++;
                }
            }
            return value;
        }
        static bool Finite(Vector3 p) => Finite(p.x)&&Finite(p.y)&&Finite(p.z);
        static bool Finite(float value) => !float.IsNaN(value)&&!float.IsInfinity(value);
        public void Stop() { skin?.Dispose(); skin=null; pending=null; }
        void OnDestroy() => Stop();
    }
}
