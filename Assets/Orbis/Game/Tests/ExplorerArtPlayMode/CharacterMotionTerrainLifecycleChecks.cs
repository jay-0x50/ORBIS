using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Orbis.M0;
using Orbis.M0.Animation;
using Orbis.M4;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

namespace Orbis.Game.Tests
{
    // Opt-in armed-slope lifecycle assertions. Existing flat/terrain recipes are unchanged.
    public static class CharacterMotionTerrainLifecycleChecks
    {
        [Serializable] sealed class Evidence
        {
            public string scope="Live 24-degree owned surface. Actual ResetContacts, Traversal.Teleport, Dead FSM entry/exit, and visual disable/re-enable. No manually advanced animation or damage/rescue claim.";
            public string character,lastCheck;
            public bool completed;
            public float slopeDegrees=24f;
            public int initialGeometryAttempts;
            public List<string> passedChecks=new List<string>();
            public List<CharacterMotionLifecycleObserver.Snapshot> captures;
        }
        static readonly Vector3 Origin=new Vector3(3400f,100f,3000f);
        const float Degrees=24f;

        public static IEnumerator Run(M4SceneBootstrap world,Animator animator,PlayerMotor motor,
            M0Input input,Keyboard keyboard,string character,string output,Material material,Vector3 returnFlatPosition)
        {
            Assert.That(Directory.Exists(output),Is.False,"Preserve previous lifecycle evidence.");
            var driver=animator.GetComponent<HumanAnimationDriver>();
            var ik=animator.GetComponent<HumanFootIK>();
            Assert.That(driver!=null && driver.IsReady && ik!=null && ik.enabled,Is.True);
            Assert.That(motor.GetComponent<CharacterController>().slopeLimit,Is.GreaterThan(Degrees));
            Assert.That(animator.gameObject.activeSelf,Is.True);
            var proof=new Evidence {character=character};
            var owner=new GameObject("Armed terrain lifecycle / test only");
            owner.layer=8; owner.transform.position=Origin;
            var mesh=SurfaceMesh();
            owner.AddComponent<MeshFilter>().sharedMesh=mesh;
            owner.AddComponent<MeshRenderer>().sharedMaterial=material;
            var surface=owner.AddComponent<MeshCollider>(); surface.sharedMesh=mesh;
            var observer=owner.AddComponent<CharacterMotionLifecycleObserver>();
            Directory.CreateDirectory(output);
            try
            {
                world.Presentation.Ultimate.Cancel();
                input.SetPresentationLocked(false);
                NeutralKeys(keyboard);
                world.Traversal.Teleport(FlatPoint());
                motor.transform.rotation=Quaternion.identity;
                Physics.SyncTransforms();
                for(int i=0;i<35;i++) yield return null;
                Assert.That(motor.IsGrounded,Is.True);
                observer.Configure(animator,motor,surface);
                yield return Capture(observer,"initial-flat");
                Assert.That(observer.LastCapture.handoff || observer.LastCapture.support,Is.False);
                proof.initialGeometryAttempts=ik.TerrainGeometryAttempts;
                Assert.That(proof.initialGeometryAttempts,Is.EqualTo(1),"One successful geometry cache is required before lifecycle reuse.");

                proof.lastCheck="ResetContacts on an actually armed slope";
                yield return Arm(world,motor,keyboard,observer,surface,8f,"reset-armed");
                ik.ResetContacts();
                AssertCleared(observer.Immediate("reset-immediate"));
                yield return SettleAndCapture(observer,35,"reset-rearmed");
                AssertArmed(observer.LastCapture,surface);
                Assert.That(ik.TerrainGeometryAttempts,Is.EqualTo(proof.initialGeometryAttempts));
                proof.passedChecks.Add(proof.lastCheck);

                proof.lastCheck="Actual warp from armed slope to flat";
                world.Traversal.Teleport(FlatPoint());
                // Teleport resets the motion tracker; the next live Observe/IK consumes
                // the discontinuity. Do not manually call ResetPresentation to mask that path.
                yield return SettleAndCapture(observer,3,"warp-flat-first");
                AssertFlat(observer.LastCapture,FlatPoint());
                yield return SettleAndCapture(observer,30,"warp-flat-settled");
                AssertFlat(observer.LastCapture,FlatPoint());
                Assert.That(ik.TerrainGeometryAttempts,Is.EqualTo(proof.initialGeometryAttempts));
                proof.passedChecks.Add(proof.lastCheck);

                proof.lastCheck="Warp onto a different owned slope location";
                yield return Arm(world,motor,keyboard,observer,surface,17f,"warp-slope-rearmed");
                Assert.That(Vector3.Distance(observer.LastCapture.root,SlopePoint(17f)),Is.LessThan(.08f));
                Assert.That(Mathf.Abs(observer.LastCapture.leftFoot.z-(Origin.z+17f)),Is.LessThan(1f),"No old world anchor from z=8 may pull this foot back.");
                Assert.That(Mathf.Abs(observer.LastCapture.rightFoot.z-(Origin.z+17f)),Is.LessThan(1f));
                proof.passedChecks.Add(proof.lastCheck);

                proof.lastCheck="Dead FSM disables armed ground IK and blocks movement";
                Vector3 deathRoot=motor.transform.position;
                Assert.That(motor.TryBeginAction(PlayerActionState.Dead),Is.True);
                Assert.That(driver.AllowFootIK,Is.False);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
                yield return SettleAndCapture(observer,3,"dead-first");
                AssertDead(observer.LastCapture,deathRoot);
                yield return SettleAndCapture(observer,12,"dead-held");
                AssertDead(observer.LastCapture,deathRoot);
                NeutralKeys(keyboard);
                motor.EndAction(PlayerActionState.Dead);
                yield return SettleAndCapture(observer,35,"dead-ended-rearmed");
                AssertArmed(observer.LastCapture,surface);
                proof.passedChecks.Add(proof.lastCheck);

                proof.lastCheck="Armed visual disable/re-enable preserves geometry cache";
                animator.gameObject.SetActive(false);
                AssertCleared(observer.Immediate("visual-disabled-immediate"));
                yield return SettleAndCapture(observer,3,"visual-disabled-held");
                AssertCleared(observer.LastCapture);
                Assert.That(observer.LastCapture.visualActive,Is.False);
                animator.gameObject.SetActive(true);
                yield return SettleAndCapture(observer,35,"visual-reenabled-armed");
                AssertArmed(observer.LastCapture,surface);
                Assert.That(ik.TerrainGeometryAttempts,Is.EqualTo(proof.initialGeometryAttempts),"Disable/re-enable must reuse the once-latched Avatar/profile geometry.");
                proof.passedChecks.Add(proof.lastCheck);
                proof.completed=true;
            }
            finally
            {
                proof.captures=observer.Captures;
                File.WriteAllText(Path.Combine(output,"terrain_lifecycle.json"),JsonUtility.ToJson(proof,true));
                observer.Stop();
                if(animator!=null) animator.gameObject.SetActive(true);
                NeutralKeys(keyboard);
                motor.EndAction(PlayerActionState.Dead);
                world.Presentation.Ultimate.Cancel();
                world.Traversal.Teleport(returnFlatPosition+Vector3.up*.03f);
                Object.Destroy(owner); Object.Destroy(mesh);
            }
            yield return null;
        }

        static IEnumerator Arm(M4SceneBootstrap world,PlayerMotor motor,Keyboard keyboard,
            CharacterMotionLifecycleObserver observer,MeshCollider surface,float z,string label)
        {
            NeutralKeys(keyboard);
            world.Traversal.Teleport(SlopePoint(z)); motor.transform.rotation=Quaternion.identity;
            Physics.SyncTransforms();
            yield return SettleAndCapture(observer,35,label);
            AssertArmed(observer.LastCapture,surface);
        }
        static IEnumerator SettleAndCapture(CharacterMotionLifecycleObserver observer,int frames,string label)
        {
            for(int i=0;i<frames;i++) yield return null;
            yield return Capture(observer,label);
        }
        static IEnumerator Capture(CharacterMotionLifecycleObserver observer,string label)
        {
            int expected=observer.CompletedRequests+1;
            observer.Request(label);
            for(int i=0;i<30 && observer.CompletedRequests<expected;i++) yield return null;
            Assert.That(observer.CompletedRequests,Is.EqualTo(expected),"Live LateUpdate must produce the requested checkpoint.");
            Assert.That(observer.LastCapture.lateFrame && observer.LastCapture.finite,Is.True);
        }
        static void AssertArmed(CharacterMotionLifecycleObserver.Snapshot s,MeshCollider surface)
        {
            Assert.That(s.grounded && s.allowIK && s.geometryReady && s.support && s.handoff,Is.True,s.label);
            Assert.That(s.leftWeight,Is.EqualTo(1f).Within(.0001f));
            Assert.That(s.rightWeight,Is.EqualTo(1f).Within(.0001f));
            Assert.That(surface.Raycast(new Ray(s.root+Vector3.up*2f,Vector3.down),out var hit,5f),Is.True);
            Assert.That(Vector3.Angle(hit.normal,Vector3.up),Is.EqualTo(Degrees).Within(.01f),"Arming must occur on the actual owned incline.");
            Assert.That(s.missingSurfacePoints,Is.Zero);
            // Lifecycle correctness gate in stable Idle, not general art acceptance.
            Assert.That(s.minimumSurfaceDistance,Is.InRange(-.005f,.03f),"Rearming must support the actual shoe on its current surface.");
            foreach(var foot in s.feet)
            {
                Assert.That(foot.terrainConstraintFeasible,Is.True);
                Assert.That(Mathf.Abs(foot.terrainFinalVerticalCorrection),Is.LessThanOrEqualTo(.3201f));
            }
        }
        static void AssertFlat(CharacterMotionLifecycleObserver.Snapshot s,Vector3 expected)
        {
            Assert.That(s.support || s.handoff,Is.False,"A warp must not carry armed terrain state onto fresh flat ground.");
            Assert.That(s.remainder,Is.Zero);
            Assert.That(s.leftReleased || s.rightReleased || s.leftRecovering || s.rightRecovering,Is.False);
            Assert.That(Vector3.Distance(s.root,expected),Is.LessThan(.08f));
            Assert.That(s.missingSurfacePoints,Is.Zero);
            Assert.That(s.minimumSurfaceDistance,Is.InRange(-.005f,.03f));
        }
        static void AssertDead(CharacterMotionLifecycleObserver.Snapshot s,Vector3 origin)
        {
            Assert.That(s.state,Is.EqualTo(PlayerActionState.Dead.ToString()));
            Assert.That(s.allowIK,Is.False); AssertCleared(s);
            Assert.That(Vector2.Distance(new Vector2(s.root.x,s.root.z),new Vector2(origin.x,origin.z)),Is.LessThan(.001f),"Held W cannot move the Dead pawn.");
        }
        static void AssertCleared(CharacterMotionLifecycleObserver.Snapshot s)
        {
            Assert.That(s.support || s.handoff || s.leftReleased || s.rightReleased || s.leftRecovering || s.rightRecovering,Is.False,s.label);
            Assert.That(s.remainder,Is.Zero);
            Assert.That(s.leftWeight,Is.Zero); Assert.That(s.rightWeight,Is.Zero);
        }
        static void NeutralKeys(Keyboard keyboard) => InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        static Vector3 FlatPoint() => Origin+new Vector3(0,.03f,-10f);
        static Vector3 SlopePoint(float z) => Origin+new Vector3(0,Mathf.Tan(Degrees*Mathf.Deg2Rad)*z+.03f,z);
        static Mesh SurfaceMesh()
        {
            var vertices=new List<Vector3>(); var triangles=new List<int>();
            foreach(float z in new[]{-20f,0f,24f,40f})
            {
                float y=Mathf.Tan(Degrees*Mathf.Deg2Rad)*Mathf.Clamp(z,0f,24f);
                vertices.Add(new Vector3(-20f,y,z)); vertices.Add(new Vector3(20f,y,z));
            }
            for(int i=0;i<3;i++) { int a=i*2; triangles.AddRange(new[]{a,a+2,a+1,a+1,a+2,a+3}); }
            var mesh=new Mesh {name="Armed lifecycle continuous ramp24"};
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
