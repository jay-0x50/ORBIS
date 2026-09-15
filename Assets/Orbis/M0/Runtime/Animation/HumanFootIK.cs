using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis.M0.Animation
{
    /// <summary>
    /// Calibrated sole-to-surface IK for ground locomotion. No foot offset is guessed from an ankle bone.
    /// The profile must carry the actual shoe mesh/rest-pose measurement before this can be enabled.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Animator), typeof(HumanAnimationDriver))]
    public sealed class HumanFootIK : MonoBehaviour
    {
        sealed class Foot
        {
            public AvatarIKGoal Goal;
            public HumanBodyBones Bone, UpperLeg, LowerLeg;
            public string ContactParameter;
            public Transform Transform, Upper, Lower;
            public bool Planted, HasTarget;
            public Vector3 PlantPoint, TargetGoal, GroundNormal;
            public Quaternion TargetGoalRotation;
            public Quaternion PlantGoalRotation;
            public Vector3 NeutralInRoot, StopStart, StopEnd;
            public Quaternion NeutralRotationInRoot, StopStartRotation, StopEndRotation;
            public float StopStartHeight;
            public float Weight, VerticalCorrection, ReachPelvisDrop;
            public bool ReleasedUntilSwing, Recovering, HasRendered, AnimationHandoff;
            public Vector3 SourceUpper;
            public float SourceLegLength, RecoveryProjection;
            public bool TerrainAdapted;
            public bool TerrainHandoff;
            public float TerrainHandoffClearance;
            public int TerrainGateRays;
            public Vector3 TerrainSourceSole, SourceAnkle, SourceSole, SurfacePoint;
            public Quaternion TerrainSourceFrame, SourceSoleFrame;
            public Matrix4x4 SourceFootMatrix;
            public Vector3[] SolePoints, SoleHull;
            public float TerrainSourceOffset, TerrainClearanceLift, TerrainReachProjection, AppliedWeight;
            public float TerrainUnresolvedClearance;
            public int TerrainSurfaceRays;
            public float TerrainFinalVerticalCorrection;
            public bool TerrainConstraintFeasible=true;
            public float RecoveryTime;
            public float RecoveryArcHeight;
            public Vector3 RenderedSole, RecoveryStart;
            public Quaternion RenderedRotation, RecoveryRotation;
            public string GuardReason;
            public float GuardDrift, GuardTwist, GuardReachExcess;
        }
        readonly Foot left = new Foot { Goal = AvatarIKGoal.LeftFoot, Bone = HumanBodyBones.LeftFoot,
            UpperLeg = HumanBodyBones.LeftUpperLeg, LowerLeg = HumanBodyBones.LeftLowerLeg, ContactParameter = HumanAnimationDriver.LeftPlantParameter };
        readonly Foot right = new Foot { Goal = AvatarIKGoal.RightFoot, Bone = HumanBodyBones.RightFoot,
            UpperLeg = HumanBodyBones.RightUpperLeg, LowerLeg = HumanBodyBones.RightLowerLeg, ContactParameter = HumanAnimationDriver.RightPlantParameter };
        readonly RaycastHit[] hits = new RaycastHit[12];
        Animator animator;
        HumanAnimationDriver driver;
        float pelvisOffset;
        bool bound;
        bool stopping;
        bool rootSurfaceFound;
        bool terrainSupportThisFrame,terrainHandoffActive;
        float terrainPelvisRemainder;
        bool geometryAttempted,terrainGeometryReady;
        int geometryAttempts;
        string terrainGeometryError;
        Avatar geometryAvatar;
        CharacterMotionProfile geometryProfile;
        RaycastHit rootSurface;
        // Presentation defaults absent from the design: a guard-triggered release gets
        // a short visible re-step, never a new contact anchor on the very next frame.
        const float RecoverySeconds=.12f, RecoveryLift=.06f;
        public float EffectiveContact(bool isLeft) => (isLeft?left:right).ReleasedUntilSwing ||
            (isLeft?left:right).Recovering ? 0f : animator==null ? 0f : animator.GetFloat((isLeft?left:right).ContactParameter);
        public bool ContactReleased(bool isLeft) => (isLeft?left:right).ReleasedUntilSwing;
        public bool ContactRecovering(bool isLeft) => (isLeft?left:right).Recovering;
        public bool TerrainGeometryReady => terrainGeometryReady;
        public bool TerrainSupportPresent => terrainSupportThisFrame;
        public bool TerrainHandoffActive => terrainHandoffActive;
        public float TerrainPelvisRemainder => terrainPelvisRemainder;
        public bool TerrainHandoff(bool isLeft) => (isLeft?left:right).TerrainHandoff;
        public float TerrainHandoffClearance(bool isLeft) => (isLeft?left:right).TerrainHandoffClearance;
        public int TerrainGateRayCount(bool isLeft) => (isLeft?left:right).TerrainGateRays;
        public int TerrainGeometryAttempts => geometryAttempts;
        public string TerrainGeometryError => terrainGeometryError;
        public float TerrainFinalVerticalCorrection(bool isLeft) => (isLeft?left:right).TerrainFinalVerticalCorrection;
        public bool TerrainConstraintFeasible(bool isLeft) => (isLeft?left:right).TerrainConstraintFeasible;
        public bool TerrainAdapted(bool isLeft) => (isLeft?left:right).TerrainAdapted;
        public float TerrainSourceOffset(bool isLeft) => (isLeft?left:right).TerrainSourceOffset;
        public float TerrainClearanceLift(bool isLeft) => (isLeft?left:right).TerrainClearanceLift;
        public float TerrainReachProjection(bool isLeft) => (isLeft?left:right).TerrainReachProjection;
        public int TerrainSurfaceRayCount(bool isLeft) => (isLeft?left:right).TerrainSurfaceRays;
        public float TerrainUnresolvedClearance(bool isLeft) => (isLeft?left:right).TerrainUnresolvedClearance;
        public float AppliedSolveWeight(bool isLeft) => (isLeft?left:right).AppliedWeight;
        public int CalibratedSolePointCount(bool isLeft) => (isLeft?left:right).SolePoints?.Length ?? 0;
        public float ContactRecoveryProgress(bool isLeft) => Mathf.Clamp01((isLeft?left:right).RecoveryTime/RecoverySeconds);
        public float ContactRecoveryProjection(bool isLeft) => (isLeft?left:right).RecoveryProjection;
        public bool ContactAnimationHandoff(bool isLeft) => (isLeft?left:right).AnimationHandoff;
        public float ContactRecoveryArcHeight(bool isLeft) => (isLeft?left:right).RecoveryArcHeight;
        public string ContactGuardReason(bool isLeft) => (isLeft?left:right).GuardReason;
        public float ContactSolveWeight(bool isLeft) => (isLeft?left:right).Weight;
        public Vector3 ContactGuardMeasurements(bool isLeft)
        {
            var foot=isLeft?left:right;
            // x/y/z are the pre-recovery ground/plant target drift metres, twist degrees, and reach excess metres.
            // RecoveryProjection separately records the final recovery reach correction.
            return new Vector3(foot.GuardDrift,foot.GuardTwist,foot.GuardReachExcess);
        }

        void LateUpdate()
        {
            if(!bound) return;
            foreach(var foot in new[]{left,right})
            {
                var c=foot==left?driver.Profile.LeftSole:driver.Profile.RightSole;
                foot.RenderedSole=foot.Transform.TransformPoint(c.SolePointInFootLocal);
                foot.RenderedRotation=SoleFrame(foot,c); foot.HasRendered=true;
            }
        }

        void Bind()
        {
            animator = GetComponent<Animator>(); driver = GetComponent<HumanAnimationDriver>();
            if (animator == null || !animator.isHuman || driver == null || !driver.IsReady) return;
            foreach (var foot in new[] { left, right })
            {
                foot.Transform = animator.GetBoneTransform(foot.Bone);
                foot.Upper = animator.GetBoneTransform(foot.UpperLeg); foot.Lower = animator.GetBoneTransform(foot.LowerLeg);
                if (foot.Transform == null || foot.Upper == null || foot.Lower == null) return;
                if(driver.MotionRoot==null) return;
                var c=foot==left?driver.Profile.LeftSole:driver.Profile.RightSole;
                foot.NeutralInRoot=c.NeutralSoleInRoot;
                // The reviewed authored Idle sole normal/forward are root +Y/+Z. This is
                // independent of a first Bind occurring airborne or partway through an action.
                foot.NeutralRotationInRoot=Quaternion.identity;
            }
            bound = true;
        }
        public void ResetContacts()
        {
            left.Planted = right.Planted = false; left.HasTarget = right.HasTarget = false;
            left.Weight = right.Weight = pelvisOffset = 0f;
            left.ReleasedUntilSwing=right.ReleasedUntilSwing=false;
            left.Recovering=right.Recovering=false; left.HasRendered=right.HasRendered=false;
            left.RecoveryArcHeight=right.RecoveryArcHeight=0f;
            left.RecoveryProjection=right.RecoveryProjection=0f;
            left.AnimationHandoff=right.AnimationHandoff=false;
            left.TerrainAdapted=right.TerrainAdapted=false;
            left.TerrainHandoff=right.TerrainHandoff=false;
            left.TerrainHandoffClearance=right.TerrainHandoffClearance=0f;
            terrainSupportThisFrame=terrainHandoffActive=false; terrainPelvisRemainder=0f;
            left.AppliedWeight=right.AppliedWeight=0f;
            left.GuardReason=right.GuardReason=null;
        }
        public void BeginStop()
        {
            if(!bound) Bind();
            if(!bound || driver.MotionRoot==null) return;
            stopping=true;
            foreach(var foot in new[]{left,right})
            {
                var c=foot==left?driver.Profile.LeftSole:driver.Profile.RightSole;
                foot.StopStart=foot.Transform.TransformPoint(c.SolePointInFootLocal);
                foot.StopEnd=driver.MotionRoot.TransformPoint(foot.NeutralInRoot);
                foot.StopStartRotation=SoleFrame(foot,c);
                foot.StopEndRotation=driver.MotionRoot.rotation*foot.NeutralRotationInRoot;
                foot.StopStartHeight=RaycastGround(foot.StopStart,driver.Profile.MaximumGroundCorrection,driver.Profile.GroundLayers,out var hit)
                    ? Mathf.Max(0,foot.StopStart.y-hit.point.y):0f;
                foot.Planted=false; foot.AnimationHandoff=false;
                // The explicit stop path begins at the rendered foot and supersedes
                // any release recovery; its targets must never receive recovery projection.
                foot.Recovering=false; foot.ReleasedUntilSwing=false;
            }
        }
        public void EndStop(bool preserveCompletedPlant=false)
        {
            stopping=false;
            if(!preserveCompletedPlant) { ResetContacts(); return; }
            foreach(var foot in new[]{left,right})
            {
                foot.Planted=true; foot.PlantPoint=foot.StopEnd;
                foot.PlantGoalRotation=foot.StopEndRotation;
                foot.ReleasedUntilSwing=false; foot.Recovering=false; foot.AnimationHandoff=false;
            }
        }
        static Quaternion SoleFrame(Foot foot,HumanSoleCalibration c) => Quaternion.LookRotation(
            foot.Transform.TransformDirection(c.ForwardInFootLocal),foot.Transform.TransformDirection(c.SoleNormalInFootLocal));
        void StopTarget(Foot foot,out Vector3 target,out Quaternion rotation,out float height)
        {
            bool support=driver.StopSupportsLeft==(foot==left);
            float start=support ? .48f : .02f,end=support ? .92f : .43f;
            float t=driver.StopNormalizedTime,phase=Mathf.InverseLerp(start,end,t),smooth=phase*phase*(3f-2f*phase);
            target=Vector3.Lerp(foot.StopStart,foot.StopEnd,smooth);
            rotation=Quaternion.Slerp(foot.StopStartRotation,foot.StopEndRotation,smooth);
            height=foot.StopStartHeight*(1f-smooth)+(t>start && t<end ? .08f*Mathf.Sin(Mathf.PI*phase):0f);
        }

        void OnAnimatorIK(int layerIndex)
        {
            // Only the base layer has IK Pass. Never solve the same body again on an overlay callback.
            if (layerIndex != 0) return;
            if (!bound) Bind();
            if (!bound) return;
            var profile = driver.Profile;
            bool enabled = driver.AllowFootIK && profile.LeftSole.Calibrated && profile.RightSole.Calibrated;
            if (driver.DiscontinuityThisFrame) ResetContacts();
            if (!enabled)
            {
                EndStop(); SetWeights(left, 0f); SetWeights(right, 0f); return;
            }
            TryMeasureSoleGeometry();
            float dt = Time.deltaTime;
            rootSurfaceFound=RaycastGround(driver.MotionRoot.position,profile.MaximumGroundCorrection,
                profile.GroundLayers,out rootSurface);
            left.TerrainGateRays=right.TerrainGateRays=0;
            // One pelvis affects both legs. A plateau foot cannot drop out of full
            // terrain solving while its partner still supports the body on an incline.
            // Hull probes detect a toe reaching a ramp before either central ray does.
            terrainSupportThisFrame=terrainGeometryReady && rootSurfaceFound &&
                (Inclined(rootSurface.normal) || FindFootTerrainSupport(left,profile) || FindFootTerrainSupport(right,profile));
            Solve(left, profile.LeftSole, profile, dt);
            Solve(right, profile.RightSole, profile, dt);

            float desiredPelvis = 0f;
            float leftPelvisWeight=left.TerrainAdapted?1f:left.Weight;
            float rightPelvisWeight=right.TerrainAdapted?1f:right.Weight;
            if (left.HasTarget && leftPelvisWeight > .1f) desiredPelvis = Mathf.Min(desiredPelvis, left.VerticalCorrection * leftPelvisWeight);
            if (right.HasTarget && rightPelvisWeight > .1f) desiredPelvis = Mathf.Min(desiredPelvis, right.VerticalCorrection * rightPelvisWeight);
            if (left.HasTarget && leftPelvisWeight > .1f) desiredPelvis = Mathf.Min(desiredPelvis, left.ReachPelvisDrop * leftPelvisWeight);
            if (right.HasTarget && rightPelvisWeight > .1f) desiredPelvis = Mathf.Min(desiredPelvis, right.ReachPelvisDrop * rightPelvisWeight);
            desiredPelvis = Mathf.Clamp(desiredPelvis, -profile.MaximumPelvisOffset, profile.MaximumPelvisOffset);
            float pelvisAlpha=Alpha(.075f,dt);
            pelvisOffset = Mathf.Lerp(pelvisOffset, desiredPelvis, pelvisAlpha);
            if(terrainSupportThisFrame)
            {
                terrainHandoffActive=true; terrainPelvisRemainder=Mathf.Abs(pelvisOffset);
            }
            else if(terrainHandoffActive)
            {
                // Linear smoothing retains this fraction of the last terrain pelvis
                // correction. Track that contribution, without adding a release timer.
                terrainPelvisRemainder*=1f-pelvisAlpha;
            }
            animator.bodyPosition += Vector3.up * pelvisOffset;
            // Recovery goals use the actually applied, smoothed pelvis offset. Testing
            // reach against a future full pelvis drop would still overextend this frame.
            ConstrainRecoveryReach(left, pelvisOffset);
            ConstrainRecoveryReach(right, pelvisOffset);
            bool handoffLeft=false,handoffRight=false;
            if(terrainHandoffActive && !terrainSupportThisFrame)
            {
                handoffLeft=PrepareTerrainHandoff(left,profile,pelvisOffset);
                handoffRight=PrepareTerrainHandoff(right,profile,pelvisOffset);
            }
            FinalizeTerrainGoal(left,profile,pelvisOffset);
            FinalizeTerrainGoal(right,profile,pelvisOffset);
            if(terrainHandoffActive && !terrainSupportThisFrame && !handoffLeft && !handoffRight &&
                left.SourceAnkle.y+terrainPelvisRemainder==left.SourceAnkle.y &&
                right.SourceAnkle.y+terrainPelvisRemainder==right.SourceAnkle.y)
            {
                // The residual no longer changes a representable world-space ankle,
                // and both legacy goals are clear. Resume the exact ordinary flat path.
                terrainHandoffActive=false; terrainPelvisRemainder=0f;
            }
            Apply(left); Apply(right);
        }

        void Solve(Foot foot, HumanSoleCalibration sole, CharacterMotionProfile profile, float dt)
        {
            float contact = Mathf.Clamp01(animator.GetFloat(foot.ContactParameter));
            foot.RecoveryProjection=0f;
            foot.TerrainHandoff=false; foot.TerrainHandoffClearance=0f;
            foot.TerrainSurfaceRays=0; foot.TerrainFinalVerticalCorrection=0; foot.TerrainConstraintFeasible=true;
            foot.TerrainAdapted=false; foot.TerrainSourceOffset=foot.TerrainClearanceLift=foot.TerrainReachProjection=foot.TerrainUnresolvedClearance=0f;
            foot.SourceAnkle=foot.Transform.position; foot.SourceFootMatrix=foot.Transform.localToWorldMatrix;
            foot.SourceUpper=foot.Upper.position;
            foot.SourceLegLength=Vector3.Distance(foot.Upper.position,foot.Lower.position)+
                Vector3.Distance(foot.Lower.position,foot.Transform.position);
            Vector3 animatedSole = foot.Transform.TransformPoint(sole.SolePointInFootLocal);
            bool stop=stopping && driver.IsStopping;
            Vector3 stopPoint=default; Quaternion stopRotation=default; float stopHeight=0;
            if(stop) StopTarget(foot,out stopPoint,out stopRotation,out stopHeight);
            if (contact < .25f)
            {
                foot.Planted = false; foot.ReleasedUntilSwing=false;
                if(!foot.Recovering) foot.GuardReason=null;
            }
            Vector3 rayPoint = stop ? stopPoint : foot.Planted ? foot.PlantPoint : animatedSole;
            bool grounded = RaycastGround(rayPoint, profile.MaximumGroundCorrection, profile.GroundLayers, out var hit);
            foot.HasTarget = grounded;
            float desiredWeight = grounded ? stop ? 1f : contact : 0f;
            if (!grounded) foot.Planted = false;
            if (grounded)
            {
                float correction = hit.point.y - animatedSole.y;
                // A distant ledge or a different floor must not pull the foot through space.
                if (Mathf.Abs(correction) > profile.MaximumGroundCorrection)
                { desiredWeight = 0f; foot.Planted = false; foot.HasTarget = false; }
                else
                {
                    Vector3 currentNormal = foot.Transform.TransformDirection(sole.SoleNormalInFootLocal).normalized;
                    Vector3 currentForward = foot.Transform.TransformDirection(sole.ForwardInFootLocal).normalized;
                    Quaternion sourceSoleFrame = Quaternion.LookRotation(currentForward,currentNormal);
                    foot.SourceSole=animatedSole; foot.SourceSoleFrame=sourceSoleFrame;
                    // Only non-horizontal owned surfaces enable terrain adaptation. This
                    // 1e-8 normal tolerance rejects floating-point noise, not a slope limit.
                    foot.TerrainAdapted=terrainSupportThisFrame;
                    if(foot.TerrainAdapted)
                    {
                        // Preserve authored swing X/Z and height over its measured Idle sole.
                        // Terrain elevation is independent of the authored contact weight.
                        float nativeHeight=Mathf.Max(0,animatedSole.y-driver.MotionRoot.position.y-foot.NeutralInRoot.y);
                        foot.TerrainSourceOffset=Mathf.Clamp(hit.point.y+nativeHeight-animatedSole.y,
                            -profile.MaximumGroundCorrection,profile.MaximumGroundCorrection);
                        foot.TerrainSourceSole=animatedSole+Vector3.up*foot.TerrainSourceOffset;
                        Quaternion terrainTilt=Quaternion.RotateTowards(Quaternion.identity,
                            Quaternion.FromToRotation(Vector3.up,hit.normal),profile.MaximumFootTiltDegrees);
                        foot.TerrainSourceFrame=terrainTilt*sourceSoleFrame;
                    }
                    Quaternion groundAlign = Quaternion.FromToRotation(currentNormal,hit.normal);
                    groundAlign = Quaternion.RotateTowards(Quaternion.identity,groundAlign,profile.MaximumFootTiltDegrees);
                    Quaternion alignedSoleFrame = groundAlign * sourceSoleFrame;
                    if (!stop && !foot.Planted && !foot.ReleasedUntilSwing && !foot.Recovering && contact > .65f)
                    {
                        foot.Planted = true; foot.PlantPoint = hit.point; foot.PlantGoalRotation = alignedSoleFrame;
                        foot.AnimationHandoff=false;
                    }
                    Vector3 targetSole = stop ? stopPoint : foot.Planted ? foot.PlantPoint : hit.point;
                    targetSole.y = hit.point.y+(stop?stopHeight:0f);
                    foot.GroundNormal = hit.normal; foot.SurfacePoint=hit.point;
                    Quaternion desiredSoleFrame = stop ? stopRotation : foot.Planted ?
                        Quaternion.FromToRotation(foot.PlantGoalRotation * Vector3.up,hit.normal)*foot.PlantGoalRotation : alignedSoleFrame;
                    if(stop && foot.TerrainAdapted)
                    {
                        Quaternion stopAlign=Quaternion.FromToRotation(desiredSoleFrame*Vector3.up,hit.normal);
                        desiredSoleFrame=Quaternion.RotateTowards(Quaternion.identity,stopAlign,
                            profile.MaximumFootTiltDegrees)*desiredSoleFrame;
                    }
                    float drift=Vector3.ProjectOnPlane(animatedSole-targetSole,Vector3.up).magnitude;
                    foot.GuardDrift=drift;
                    foot.GuardTwist=foot.Planted?Quaternion.Angle(alignedSoleFrame,desiredSoleFrame):0f;
                    Vector3 plantBone=BoneGoal(foot,animatedSole,sourceSoleFrame,targetSole,desiredSoleFrame);
                    float plantDrop=ReachDrop(foot,plantBone,profile);
                    foot.GuardReachExcess=Vector3.Distance(foot.SourceUpper+Vector3.up*plantDrop,plantBone)-
                        foot.SourceLegLength*.9995f;
                    // All plant guards run before the one recovery-target evaluation.
                    // After07's late Reach call missed this frame and then alternated
                    // fading/full IK weights as the recovery crossed the reach limit.
                    if(!stop && foot.Planted)
                    {
                        string reason=drift>profile.MaximumPlantDrift?"Drift":
                            foot.GuardTwist>65f?"Twist":foot.GuardReachExcess>0f?"Reach":null;
                        if(reason!=null) Release(foot,animatedSole,sourceSoleFrame,contact,reason);
                    }
                    else if(!stop && !foot.Recovering && !foot.AnimationHandoff && foot.GuardReachExcess>0f)
                        desiredWeight=0f; // Preserve the existing unplanted swing/ground-correction guard.

                    if(!stop && foot.AnimationHandoff)
                    {
                        // A release ends at the live animation, not at a new ground ray.
                        // Even a fading residual weight must refer to that same source
                        // goal until the next genuine plant, including after latch reset.
                        targetSole=foot.TerrainAdapted?foot.TerrainSourceSole:animatedSole;
                        desiredSoleFrame=foot.TerrainAdapted?foot.TerrainSourceFrame:sourceSoleFrame; desiredWeight=0f;
                    }
                    if(!stop && foot.Recovering)
                    {
                        foot.RecoveryTime+=dt;
                        float t=Mathf.Clamp01(foot.RecoveryTime/RecoverySeconds),s=t*t*(3f-2f*t);
                        targetSole=Vector3.Lerp(foot.RecoveryStart,foot.TerrainAdapted?foot.TerrainSourceSole:animatedSole,s)+Vector3.up*(foot.RecoveryArcHeight*Mathf.Sin(Mathf.PI*t));
                        desiredSoleFrame=Quaternion.Slerp(foot.RecoveryRotation,foot.TerrainAdapted?foot.TerrainSourceFrame:sourceSoleFrame,s);
                        desiredWeight=1f;
                        if(t>=1f) { foot.Recovering=false; desiredWeight=0f; }
                    }
                    Vector3 targetBone=BoneGoal(foot,animatedSole,sourceSoleFrame,targetSole,desiredSoleFrame);
                    foot.ReachPelvisDrop=ReachDrop(foot,targetBone,profile);
                    // Preserve the previous stop reach handling. Recoveries instead
                    // receive a bounded target after both feet choose the actual pelvis;
                    // they never toggle solve weight off and back to one for reach.
                    if(stop && Vector3.Distance(foot.SourceUpper+Vector3.up*foot.ReachPelvisDrop,targetBone)>
                        foot.SourceLegLength*.9995f) desiredWeight=0f;
                    // Authored muscle-only Humanoid clips can carry default/stale internal IK
                    // goals. Actual After01 measured GetIKPosition almost .93m above the shoe
                    // bone, so carrying that offset into the target folds the leg upward.
                    // Supply the measured ankle target directly. IK rotations use a semantic
                    // foot frame (+Z along the sole, +Y normal), not the source bone axes.
                    foot.TargetGoal = targetBone;
                    foot.TargetGoalRotation = desiredSoleFrame;
                    foot.VerticalCorrection = targetBone.y - foot.Transform.position.y;
                }
            }
            foot.Weight = Mathf.Lerp(foot.Weight, desiredWeight, Alpha(profile.ContactFadeSeconds, dt));
            // Stop targets start at the actually rendered sole position, then lift only in
            // the authored stepping intervals. Full solve here preserves that continuous path
            // while adapting arbitrary incoming gait phase; it does not move the player root.
            if(stop && desiredWeight>0f) foot.Weight=desiredWeight;
            if(foot.Recovering && desiredWeight>0f) foot.Weight=1f;
            if(foot.TerrainAdapted && foot.HasTarget)
            {
                Vector3 terrainBone=BoneGoal(foot,animatedSole,foot.SourceSoleFrame,foot.TerrainSourceSole,foot.TerrainSourceFrame);
                // Blend contact locking into an already terrain-relative source pose.
                // Apply uses full IK for this final goal; fading contact cannot lower
                // an uphill swing back through the ground.
                foot.TargetGoal=Vector3.Lerp(terrainBone,foot.TargetGoal,foot.Weight);
                foot.TargetGoalRotation=Quaternion.Slerp(foot.TerrainSourceFrame,foot.TargetGoalRotation,foot.Weight);
                foot.VerticalCorrection=foot.TargetGoal.y-foot.SourceAnkle.y;
                foot.ReachPelvisDrop=ReachDrop(foot,foot.TargetGoal,profile);
            }
            if (!foot.HasTarget)
            {
                foot.Weight=0f;
                // Never resume a stale world-space recovery after leaving the raycast
                // surface. The airborne animation owns the foot until a genuine new plant.
                if(foot.Recovering) { foot.Recovering=false; foot.AnimationHandoff=true; }
            }
        }

        static void Release(Foot foot,Vector3 animatedSole,Quaternion sourceFrame,float contact,string reason)
        {
            bool wasPlanted=foot.Planted;
            foot.Planted=false;
            if(foot.ReleasedUntilSwing || foot.Recovering) return;
            // An unplanted foot keeps its original swing. A previously planted weak
            // contact still needs positional continuity: After05 skipped recovery,
            // so the next frame replaced the old target with the current ray hit
            // while IK weight was still .259. The actual sole jumped .473m below
            // the floor. Preserve the rendered-start→animated-end connection;
            // suppress only its additional lift when contact is already fading.
            if(!wasPlanted) return;
            foot.GuardReason=reason; foot.ReleasedUntilSwing=contact>=.25f; foot.AnimationHandoff=true;
            foot.RecoveryArcHeight=contact>.65f && foot.Weight>.25f?RecoveryLift:0f;
            foot.Recovering=true; foot.RecoveryTime=0;
            foot.RecoveryStart=foot.HasRendered?foot.RenderedSole:animatedSole;
            foot.RecoveryRotation=foot.HasRendered?foot.RenderedRotation:sourceFrame;
        }

        static Vector3 BoneGoal(Foot foot,Vector3 animatedSole,Quaternion sourceSoleFrame,
            Vector3 targetSole,Quaternion desiredSoleFrame)
        {
            Quaternion align=desiredSoleFrame*Quaternion.Inverse(sourceSoleFrame);
            // The measured world offset also retains non-uniform parent scale/shear.
            return targetSole-align*(animatedSole-foot.Transform.position);
        }
        static float ReachDrop(Foot foot,Vector3 targetBone,CharacterMotionProfile profile) =>
            -Mathf.Clamp(Vector3.Distance(foot.SourceUpper,targetBone)-foot.SourceLegLength*profile.LegReachFraction,
                0f,profile.MaximumPelvisOffset);
        static void ConstrainRecoveryReach(Foot foot,float actualPelvisOffset)
        {
            if(foot.TerrainAdapted || !foot.Recovering || !foot.HasTarget || foot.Weight<=0f) return;
            Vector3 hip=foot.SourceUpper+Vector3.up*actualPelvisOffset;
            Vector3 delta=foot.TargetGoal-hip;
            float reach=foot.SourceLegLength*.9995f; // Same existing geometric guard; never enlarge it.
            if(delta.sqrMagnitude<=reach*reach) return;
            Vector3 bounded=hip+delta.normalized*reach;
            foot.RecoveryProjection=Vector3.Distance(foot.TargetGoal,bounded);
            foot.TargetGoal=bounded;
            // This minimum radial displacement is diagnostic, not hidden by a larger
            // allowance. A rapidly moving root can make the previous world contact
            // unreachable; actual shoe/sole QA must judge the resulting release.
        }

        static bool Inclined(Vector3 normal) => Vector3.Cross(normal,Vector3.up).sqrMagnitude>1e-8f;
        bool FindFootTerrainSupport(Foot foot,CharacterMotionProfile profile)
        {
            var sole=foot==left?profile.LeftSole:profile.RightSole;
            Vector3 centre=foot.Planted?foot.PlantPoint:foot.Transform.TransformPoint(sole.SolePointInFootLocal);
            if(stopping && driver.IsStopping) StopTarget(foot,out centre,out _,out _);
            foot.TerrainGateRays++;
            if(RaycastGround(centre,profile.MaximumGroundCorrection,profile.GroundLayers,out var central) && Inclined(central.normal)) return true;
            // Use the actual measured convex hull, including its toe/heel extremities.
            // These checks do not alter a freshly spawned flat-only character's goals.
            foreach(var local in foot.SoleHull)
            {
                foot.TerrainGateRays++;
                if(RaycastGround(foot.Transform.TransformPoint(local),profile.MaximumGroundCorrection,
                    profile.GroundLayers,out var hit) && Inclined(hit.normal)) return true;
            }
            return false;
        }
        bool PrepareTerrainHandoff(Foot foot,CharacterMotionProfile profile,float actualPelvisOffset)
        {
            if(!foot.HasTarget) return false;
            Vector3 oldGoal=foot.TargetGoal;
            Quaternion oldRotation=foot.TargetGoalRotation;
            // On horizontal ground, let the unchanged flat solver choose pelvis and
            // contact weight. Include that actually applied pelvis in its source pose.
            // Only lift a predicted legacy goal when retained terrain pelvis would
            // otherwise put the measured shoe through the surface.
            foot.TargetGoal=Vector3.Lerp(foot.SourceAnkle+Vector3.up*actualPelvisOffset,oldGoal,foot.Weight);
            foot.TargetGoalRotation=Quaternion.Slerp(foot.SourceSoleFrame,oldRotation,foot.Weight);
            Quaternion align=foot.TargetGoalRotation*Quaternion.Inverse(foot.SourceSoleFrame);
            float required=RequiredTerrainLift(foot,profile,align);
            foot.TerrainHandoffClearance=required;
            if(required<=0f)
            {
                foot.TargetGoal=oldGoal; foot.TargetGoalRotation=oldRotation;
                return false;
            }
            // This source/contact interpolation already contains the legacy weight;
            // apply the constrained result at full IK weight, as on the incline.
            // FinalizeTerrainGoal retains the same reach sphere and +/- .32m slab.
            foot.TerrainAdapted=true; foot.TerrainHandoff=true;
            return true;
        }

        void TryMeasureSoleGeometry()
        {
            if(geometryProfile!=driver.Profile || geometryAvatar!=animator.avatar)
            {
                geometryAttempted=false; terrainGeometryReady=false; terrainGeometryError=null;
                geometryProfile=driver.Profile; geometryAvatar=animator.avatar;
            }
            if(geometryAttempted) return;
            // Latch before any read/validation. A malformed/non-readable mesh must not
            // allocate or throw again each IK frame, including after disable/re-enable.
            geometryAttempted=true; geometryAttempts++;
            try
            {
                MeasureSoleGeometry(); terrainGeometryReady=true;
            }
            catch(Exception error) when(!(error is OutOfMemoryException))
            {
                terrainGeometryReady=false; terrainGeometryError=error.Message;
                left.SolePoints=right.SolePoints=Array.Empty<Vector3>();
                left.SoleHull=right.SoleHull=Array.Empty<Vector3>();
                Debug.LogWarning("Terrain sole calibration disabled for this avatar/profile: "+error.Message+
                    ". Existing flat contact solver remains available.",this);
            }
        }
        void MeasureSoleGeometry()
        {
            var l=new List<Vector3>(); var r=new List<Vector3>();
            foreach(var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh=skin.sharedMesh;
                if(mesh==null || skin.name.StartsWith("Art Outline",StringComparison.Ordinal) ||
                    skin.name.StartsWith("M3 Silhouette",StringComparison.Ordinal)) continue;
                if(!mesh.isReadable) throw new InvalidOperationException("Skin mesh is not readable: "+mesh.name);
                var vertices=mesh.vertices; var weights=mesh.boneWeights; var bindposes=mesh.bindposes;
                CollectSole(skin,vertices,weights,bindposes,left,driver.Profile.LeftSole,
                    animator.GetBoneTransform(HumanBodyBones.LeftToes),l);
                CollectSole(skin,vertices,weights,bindposes,right,driver.Profile.RightSole,
                    animator.GetBoneTransform(HumanBodyBones.RightToes),r);
            }
            if(l.Count<12 || r.Count<12)
                throw new InvalidOperationException("Insufficient measured bind-pose sole points: "+l.Count+" / "+r.Count);
            left.SolePoints=l.ToArray(); right.SolePoints=r.ToArray();
            left.SoleHull=SoleHull(l,driver.Profile.LeftSole);
            right.SoleHull=SoleHull(r,driver.Profile.RightSole);
        }
        static void CollectSole(SkinnedMeshRenderer skin,Vector3[] vertices,BoneWeight[] weights,Matrix4x4[] bindposes,
            Foot foot,HumanSoleCalibration sole,Transform toe,List<Vector3> result)
        {
            int fi=Array.IndexOf(skin.bones,foot.Transform),ti=Array.IndexOf(skin.bones,toe);
            if(fi<0) return;
            if(weights.Length!=vertices.Length || fi>=bindposes.Length)
                throw new InvalidOperationException("Missing skin weights/bind pose: "+skin.name);
            // Mesh.bindposes[i] maps unposed mesh-local vertices into bone i's rest
            // local coordinates. No current bone pose, Animator sample or BakeMesh.
            Matrix4x4 toFoot=bindposes[fi];
            Matrix4x4 restFootToWorld=skin.transform.localToWorldMatrix*toFoot.inverse;
            float inverseWorldPlaneScale=restFootToWorld.inverse.transpose.MultiplyVector(sole.SoleNormalInFootLocal).magnitude;
            if(!(inverseWorldPlaneScale>0f) || float.IsNaN(inverseWorldPlaneScale) || float.IsInfinity(inverseWorldPlaneScale))
                throw new InvalidOperationException("Invalid sole bind transform: "+skin.name);
            for(int i=0;i<vertices.Length;i++)
            {
                var w=weights[i]; float influence=0;
                if(w.boneIndex0==fi || w.boneIndex0==ti) influence+=w.weight0;
                if(w.boneIndex1==fi || w.boneIndex1==ti) influence+=w.weight1;
                if(w.boneIndex2==fi || w.boneIndex2==ti) influence+=w.weight2;
                if(w.boneIndex3==fi || w.boneIndex3==ti) influence+=w.weight3;
                if(influence<=.25f) continue;
                Vector3 local=toFoot.MultiplyPoint3x4(vertices[i]);
                // The unchanged 4mm calibration band is measured in world metres
                // through the rest transform, including non-uniform renderer scale.
                float distance=Vector3.Dot(local-sole.SolePointInFootLocal,sole.SoleNormalInFootLocal)/inverseWorldPlaneScale;
                if(Mathf.Abs(distance)<=.004f) result.Add(local);
            }
        }
        static Vector3[] SoleHull(List<Vector3> source,HumanSoleCalibration sole)
        {
            Vector3 rightAxis=Vector3.Cross(sole.SoleNormalInFootLocal,sole.ForwardInFootLocal);
            var sorted=new List<Vector3>(source);
            sorted.Sort((a,b)=> { int x=Vector3.Dot(a,rightAxis).CompareTo(Vector3.Dot(b,rightAxis));
                return x!=0?x:Vector3.Dot(a,sole.ForwardInFootLocal).CompareTo(Vector3.Dot(b,sole.ForwardInFootLocal)); });
            var hull=new List<Vector3>();
            foreach(var point in sorted)
            {
                while(hull.Count>=2 && HullCross(hull[hull.Count-2],hull[hull.Count-1],point,rightAxis,sole.ForwardInFootLocal)<=0)
                    hull.RemoveAt(hull.Count-1);
                hull.Add(point);
            }
            int lowerCount=hull.Count;
            for(int i=sorted.Count-2;i>=0;i--)
            {
                var point=sorted[i];
                while(hull.Count>lowerCount && HullCross(hull[hull.Count-2],hull[hull.Count-1],point,rightAxis,sole.ForwardInFootLocal)<=0)
                    hull.RemoveAt(hull.Count-1);
                hull.Add(point);
            }
            if(hull.Count>1) hull.RemoveAt(hull.Count-1);
            return hull.ToArray();
        }
        static float HullCross(Vector3 a,Vector3 b,Vector3 c,Vector3 rightAxis,Vector3 forward) =>
            Vector3.Dot(b-a,rightAxis)*Vector3.Dot(c-a,forward)-Vector3.Dot(b-a,forward)*Vector3.Dot(c-a,rightAxis);

        void FinalizeTerrainGoal(Foot foot,CharacterMotionProfile profile,float actualPelvisOffset)
        {
            if(!foot.TerrainAdapted || !foot.HasTarget) return;
            Quaternion align=foot.TargetGoalRotation*Quaternion.Inverse(foot.SourceSoleFrame);
            Vector3 hip=foot.SourceUpper+Vector3.up*actualPelvisOffset;
            float reach=foot.SourceLegLength*.9995f;
            float minY=foot.SourceAnkle.y-profile.MaximumGroundCorrection;
            float maxY=foot.SourceAnkle.y+profile.MaximumGroundCorrection;
            // Terrain clearance is best-effort inside BOTH the unchanged reach sphere
            // and source-ankle vertical correction slab. Projection cannot evade the cap.
            for(int pass=0;pass<2;pass++)
            {
                float needed=RequiredTerrainLift(foot,profile,align);
                Vector3 requested=foot.TargetGoal+Vector3.up*needed;
                if(!TryBoundTerrainGoal(requested,hip,reach,minY,maxY,out var bounded))
                {
                    // An impossible/malformed constraint set never sends an invalid
                    // full-weight goal to Mecanim. Record failure and use source animation.
                    foot.TerrainConstraintFeasible=false; foot.TerrainUnresolvedClearance=needed;
                    foot.TerrainAdapted=false; foot.HasTarget=false; foot.Weight=0f;
                    return;
                }
                foot.TerrainClearanceLift+=Mathf.Max(0,Mathf.Min(requested.y,maxY)-foot.TargetGoal.y);
                foot.TerrainReachProjection+=Vector3.Distance(requested,bounded);
                bool unchanged=(bounded-foot.TargetGoal).sqrMagnitude<=1e-12f;
                foot.TargetGoal=bounded;
                foot.TerrainFinalVerticalCorrection=bounded.y-foot.SourceAnkle.y;
                if(needed<=0f && unchanged) { foot.TerrainUnresolvedClearance=0f; return; }
            }
            foot.TerrainUnresolvedClearance=RequiredTerrainLift(foot,profile,align);
        }
        internal static bool TryBoundTerrainGoal(Vector3 requested,Vector3 hip,float reach,float minY,float maxY,out Vector3 bounded)
        {
            bounded=requested;
            if(!Finite(requested) || !Finite(hip) || !Finite(reach) || !Finite(minY) || !Finite(maxY) ||
                !(reach>0f) || minY>maxY) return false;
            float lower=Mathf.Max(minY,hip.y-reach),upper=Mathf.Min(maxY,hip.y+reach);
            if(lower>upper) return false;
            Vector3 delta=requested-hip;
            bounded=hip+Vector3.ClampMagnitude(delta,reach);
            if(bounded.y>=minY && bounded.y<=maxY) return true;
            // The closest feasible point lies on the violated slab plane and within
            // the sphere's circular section at that height. Use requested X/Z, not
            // the previously projected X/Z, for the true closest-point construction.
            float y=Mathf.Clamp(bounded.y,lower,upper);
            float radius=Mathf.Sqrt(Mathf.Max(0,reach*reach-(y-hip.y)*(y-hip.y)));
            Vector2 horizontal=new Vector2(requested.x-hip.x,requested.z-hip.z);
            horizontal=Vector2.ClampMagnitude(horizontal,radius);
            bounded=new Vector3(hip.x+horizontal.x,y,hip.z+horizontal.y);
            return true;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        float RequiredTerrainLift(Foot foot,CharacterMotionProfile profile,Quaternion align)
        {
            float needed=0f;
            // All selected sole points are checked against the main support plane.
            foreach(var local in foot.SolePoints)
            {
                Vector3 point=foot.TargetGoal+align*foot.SourceFootMatrix.MultiplyVector(local);
                needed=Mathf.Max(needed,-Vector3.Dot(point-foot.SurfacePoint,foot.GroundNormal)/foot.GroundNormal.y);
            }
            // Actual hull vertices query adjacent collider triangles too; the centre
            // plane alone would miss a toe crossing a flat→slope or slope→crest seam.
            foreach(var local in foot.SoleHull)
            {
                Vector3 point=foot.TargetGoal+align*foot.SourceFootMatrix.MultiplyVector(local);
                foot.TerrainSurfaceRays++;
                if(RaycastGround(point,profile.MaximumGroundCorrection,profile.GroundLayers,out var hit))
                    needed=Mathf.Max(needed,hit.point.y-point.y);
            }
            return needed;
        }

        bool RaycastGround(Vector3 sole, float correction, LayerMask mask, out RaycastHit result)
        {
            Vector3 origin = sole + Vector3.up * (correction + .05f);
            // Physically exclude Player layer even if an edited inspector mask accidentally includes it.
            int layerMask = mask.value & ~(1 << 10); // Existing TagManager/M0 Player layer is 10.
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, correction * 2f + .10f, layerMask, QueryTriggerInteraction.Ignore);
            float distance = float.PositiveInfinity; result = default;
            if (count == hits.Length)
            {
                // Rare crowded contacts: avoid truncation selecting an arbitrary surface; no allocation in the common path.
                foreach (var hit in Physics.RaycastAll(origin, Vector3.down, correction * 2f + .10f, layerMask, QueryTriggerInteraction.Ignore))
                    Select(hit, ref distance, ref result);
            }
            else for (int i = 0; i < count; i++) Select(hits[i], ref distance, ref result);
            return !float.IsPositiveInfinity(distance);
        }
        void Select(RaycastHit hit, ref float nearest, ref RaycastHit selected)
        {
            if (hit.collider == null || hit.normal.y < .45f) return;
            Transform root = driver.MotionRoot;
            if (root != null && hit.collider.transform.IsChildOf(root)) return;
            if (hit.distance < nearest) { nearest = hit.distance; selected = hit; }
        }
        void Apply(Foot foot)
        {
            float weight=foot.HasTarget?(foot.TerrainAdapted?1f:foot.Weight):0f;
            SetWeights(foot,weight);
            if (!foot.HasTarget || weight <= .001f) return;
            animator.SetIKPosition(foot.Goal, foot.TargetGoal); animator.SetIKRotation(foot.Goal, foot.TargetGoalRotation);
        }
        void SetWeights(Foot foot, float weight)
        { foot.AppliedWeight=weight; animator.SetIKPositionWeight(foot.Goal, weight); animator.SetIKRotationWeight(foot.Goal, weight); }
        void OnDisable() { ResetContacts(); bound = false; }
        static float Alpha(float seconds, float dt) => dt <= 0f ? 0f : 1f - Mathf.Exp(-dt / Mathf.Max(.0001f, seconds));
    }
}
