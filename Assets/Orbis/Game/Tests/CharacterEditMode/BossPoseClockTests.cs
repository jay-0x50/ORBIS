using NUnit.Framework;
using Orbis.M1;
using Orbis.M4;
using Orbis.Game.Animation;

namespace Orbis.Game.Tests
{
    public sealed class BossPoseClockTests
    {
        static BossPoseSample Observe(BossPoseClock clock,M4BossModel boss,float dt=0f)
            => clock.Observe(boss.State,boss.StateRemaining,boss.TelegraphProgress,boss.Profile,dt,.6f,2f,2f,1.5f);

        [Test]
        public void AttackPoseReachesContactOnlyWhenExistingModelExecutesAttack()
        {
            var boss=new M4BossModel(M4BossProfile.ForElement(ElementType.Fire),ElementType.Water,true);
            var clock=new BossPoseClock(); int attacks=0;
            boss.AttackReady+=()=>attacks++;
            Observe(clock,boss);
            boss.Tick(0,true,true); var start=Observe(clock,boss);
            Assert.That(start.State,Is.EqualTo(BossVisualState.Attack)); Assert.That(start.NormalizedTime,Is.Zero);
            boss.Tick(.5f,true,true);
            Assert.That(Observe(clock,boss).NormalizedTime,Is.EqualTo(.3f).Within(.0001f));
            Assert.That(attacks,Is.Zero);
            boss.Tick(.5f,true,true); var contact=Observe(clock,boss);
            Assert.That(attacks,Is.EqualTo(1)); Assert.That(contact.NormalizedTime,Is.EqualTo(.6f).Within(.0001f));
            boss.Tick(1.25f,true,true);
            Assert.That(Observe(clock,boss).NormalizedTime,Is.EqualTo(.8f).Within(.0001f));
            Assert.That(attacks,Is.EqualTo(1));
        }

        [Test]
        public void CoreExposureCancelsWindupWithoutReplayingContactDuringRecovery()
        {
            var boss=new M4BossModel(M4BossProfile.ForElement(ElementType.Fire),ElementType.Water,true);
            var clock=new BossPoseClock(); int attacks=0;
            boss.AttackReady+=()=>attacks++;
            boss.Tick(0,true,true); Observe(clock,boss);
            for(int i=0;i<3;i++) boss.RegisterDirectHit(ElementType.Water);
            Assert.That(Observe(clock,boss).State,Is.EqualTo(BossVisualState.Exposed));
            boss.Tick(boss.Profile.ExposureDuration,true,true);
            Assert.That(Observe(clock,boss).State,Is.EqualTo(BossVisualState.Idle));
            Assert.That(attacks,Is.Zero);
        }

        [Test]
        public void SavedDefeatIsStillWhileNewDefeatPlaysAndHoldsFinalPose()
        {
            var boss=new M4BossModel(M4BossProfile.ForElement(ElementType.Fire),ElementType.Water,true);
            boss.Reset(true);
            Assert.That(Observe(new BossPoseClock(),boss).NormalizedTime,Is.EqualTo(1f));
            var clock=new BossPoseClock(); boss.Reset(false); Observe(clock,boss);
            boss.ReceiveDamage(1000f);
            Assert.That(Observe(clock,boss).NormalizedTime,Is.Zero);
            Assert.That(Observe(clock,boss,1.5f).NormalizedTime,Is.EqualTo(1f));
            Assert.That(Observe(clock,boss,100f).NormalizedTime,Is.EqualTo(1f));
        }

        [Test]
        public void HitstopCannotAdvanceAuthoredPoseOrAttackClock()
        {
            var boss=new M4BossModel(M4BossProfile.ForElement(ElementType.Wind),ElementType.Rock,false);
            var clock=new BossPoseClock();
            boss.Tick(0,true,true); boss.Tick(.2f,true,true);
            var pose=Observe(clock,boss);
            for(int i=0;i<30;i++)
            {
                boss.Tick(0,true,true);
                Assert.That(Observe(clock,boss).NormalizedTime,Is.EqualTo(pose.NormalizedTime));
            }
        }

        [Test]
        public void SkippingFramesDoesNotInventAdditionalAttackEvents()
        {
            var boss=new M4BossModel(M4BossProfile.ForElement(ElementType.Lightning),ElementType.Water,true);
            var clock=new BossPoseClock(); int attacks=0; boss.AttackReady+=()=>attacks++;
            boss.Tick(0,true,true); Observe(clock,boss);
            boss.Tick(30f,true,true); var pose=Observe(clock,boss,30f);
            Assert.That(attacks,Is.EqualTo(1)); Assert.That(pose.NormalizedTime,Is.EqualTo(.6f));
            Assert.That(boss.State,Is.EqualTo(M4BossState.Recover));
        }
    }
}
