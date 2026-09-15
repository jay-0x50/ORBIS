using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.M0;
using Orbis.M0.Animation;
using Orbis.M3;
using Orbis.M4;
using UnityEngine;
using UnityEngine.InputSystem;
using Object=UnityEngine.Object;

namespace Orbis.Game.Tests
{
    public static class CharacterMotionActionChecks
    {
        [Serializable] sealed class Evidence
        {
            public string scope="Actual live PlayerMotor/ComboSequence/Explorer damage/Timeline/Teleport; only requests are scripted, no clocks or poses are manually advanced.";
            public int[] comboSteps;
            public string[] ultimateStages;
            public bool burstStarted, burstRestarted, hurtInterruptedBurst, teleportResetFacing, releaseResetFacing;
            public int frames;
            public float warpEntryYaw, releaseEntryYaw;
        }
        public static IEnumerator Run(M4SceneBootstrap world,Animator animator,PlayerMotor motor,M0Input input,Keyboard keyboard,
            string stage,string run,string character,string output,Vector3 floor)
        {
            var driver=animator.GetComponent<HumanAnimationDriver>();
            Assert.That(driver!=null && driver.IsReady,Is.True);
            var combo=motor.GetComponent<BasicAttackCombo>();
            var steps=new List<int>(); var stages=new List<string>();
            var proof=new Evidence();
            Vector3 warpTarget=motor.transform.position+new Vector3(5,0,3);
            Action<int> onStep=n=>steps.Add(n);
            Action<M3UltimateStage,Orbis.M1.ElementType,Vector3> onStage=(s,e,p)=>stages.Add(s.ToString());
            combo.StepStarted+=onStep; world.Presentation.Ultimate.StageChanged+=onStage;
            var owner=new GameObject("Motion action evidence / test only");
            var recorder=owner.AddComponent<CharacterMotionRecorder>();
            try
            {
                Action<int> prepare=i=>
                {
                    if(i==75 || i==85 || i==101) combo.RequestAttack(motor.IsGrounded);
                    if(i==150) world.Manager.ApplyEnvironmentalDamage(world.Party.ActiveMember.Actor,1f);
                    if(i==180) proof.burstStarted=world.Presentation.PlayUltimatePresentation();
                    if(i==195)
                    {
                        bool wasPlaying=world.Presentation.Ultimate.IsPlaying;
                        world.Manager.ApplyEnvironmentalDamage(world.Party.ActiveMember.Actor,1f);
                        proof.hurtInterruptedBurst=wasPlaying && motor.State==PlayerActionState.Hurt && !world.Presentation.Ultimate.IsPlaying;
                    }
                    if(i==215) proof.burstRestarted=world.Presentation.PlayUltimatePresentation();
                    if(i==270) world.Presentation.Ultimate.Cancel();
                    if(i==280)
                    {
                        proof.warpEntryYaw=driver.VisualYawOffset;
                        world.Traversal.Teleport(warpTarget);
                    }
                    if(i==285)
                    {
                        proof.releaseEntryYaw=driver.VisualYawOffset;
                        driver.Release();
                        proof.releaseResetFacing=Mathf.Abs(driver.VisualYawOffset)<.001f;
                        driver.ResetPresentation();
                    }
                };
                recorder.Begin(stage,run,character,output,animator,motor,input,keyboard,floor,Keys,Phase,prepare,true);
                float deadline=Time.realtimeSinceStartup+1800;
                while(recorder.Recording && Time.realtimeSinceStartup<deadline) yield return null;
                Assert.That(recorder.Error,Is.Null.Or.Empty,recorder.Error);
                Assert.That(recorder.FrameCount,Is.EqualTo(300));
                proof.teleportResetFacing=Mathf.Abs(recorder.Report.frames[280].visualYawOffset)<.001f &&
                    Vector3.Distance(recorder.Report.frames[280].rootPosition,warpTarget)<.1f;
                proof.comboSteps=steps.ToArray(); proof.ultimateStages=stages.ToArray(); proof.frames=recorder.FrameCount;
                File.WriteAllText(Path.Combine(output,"action_checks.json"),JsonUtility.ToJson(proof,true));
                CollectionAssert.AreEqual(new[]{1,2,3},steps);
                Assert.That(proof.burstStarted && proof.burstRestarted && proof.hurtInterruptedBurst,Is.True);
                Assert.That(proof.teleportResetFacing && proof.releaseResetFacing,Is.True);
                Assert.That(Mathf.Abs(proof.warpEntryYaw),Is.GreaterThan(1f),"Warp must actually interrupt pending visual yaw.");
                Assert.That(Mathf.Abs(proof.releaseEntryYaw),Is.GreaterThan(1f),"Release must actually interrupt pending visual yaw.");
                var frames=recorder.Report.frames;
                Assert.That(frames.All(f=>f.finite && Mathf.Abs(f.animatorSpeed-1f)<.001f),Is.True,"New action clocks must not freeze the entire Animator.");
                Assert.That(frames.Where(f=>f.attackStep>0).All(f=>Mathf.Abs(f.attackClock-f.attackParameter)<.00001f),Is.True,"Animation time must match authoritative combo time.");
                Assert.That(frames.Any(f=>f.gameplayState=="Hurt" && f.actionParameter>0),Is.True);
                Assert.That(frames.Any(f=>f.gameplayState=="Burst" && f.actionParameter>0),Is.True);
                Assert.That(frames.Where(f=>f.attackStep>0 || f.gameplayState=="Hurt" || f.gameplayState=="Burst")
                    .All(f=>f.measuredSpeed<.05f && Mathf.Abs(f.visualYawOffset)<.001f),Is.True,"Attack/action locks and facing reset must preserve gameplay ownership.");
                Assert.That(world.Presentation.Ultimate.IsPlaying,Is.False);
                Assert.That(motor.ActionLocked,Is.False);
                Assert.That(input.GameplayEnabled,Is.True);
                Assert.That(Time.timeScale,Is.EqualTo(1f).Within(.0001f));
                Assert.That(Vector3.Distance(frames[285].rootPosition,frames[299].rootPosition),Is.LessThan(.1f));
            }
            finally
            {
                combo.StepStarted-=onStep; world.Presentation.Ultimate.StageChanged-=onStage;
                world.Presentation.Ultimate.Cancel(); recorder.StopAndRelease(); Object.Destroy(owner);
            }
        }
        static Key[] Keys(int i)
        {
            if(i>=30 && i<60 || i>=75 && i<127) return i==90 ? new[]{Key.W,Key.Space} : new[]{Key.W};
            if(i>=276 && i<280) return new[]{Key.A};
            if(i>=281 && i<285) return new[]{Key.D};
            return Array.Empty<Key>();
        }
        static string Phase(int i) => i<30?"Idle":i<60?"Walk":i<75?"Stop":i<128?"Combo":i<150?"Recover":
            i<180?"Hurt":i<195?"Burst":i<215?"HurtInterrupt":i<270?"BurstReplay":i<280?"Cancel":i<285?"Warp":"ReleaseReset";
    }
}
