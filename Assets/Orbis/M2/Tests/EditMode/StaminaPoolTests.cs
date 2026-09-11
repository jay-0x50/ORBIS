using System;
using NUnit.Framework;

namespace Orbis.M2.Tests
{
    public sealed class StaminaPoolTests
    {
        [Test]
        public void ContinuousRunningChargesOnlyTimeBeyondThreeSeconds()
        {
            var pool = new StaminaPool();
            pool.Tick(StaminaActivity.Running, 2.75f);
            Assert.That(pool.Current, Is.EqualTo(100f));
            pool.Tick(StaminaActivity.Running, 0.25f);
            Assert.That(pool.Current, Is.EqualTo(100f));
            pool.Tick(StaminaActivity.Running, 0.5f);
            Assert.That(pool.Current, Is.EqualTo(99f).Within(0.0001f));
            Assert.That(pool.RunSeconds, Is.EqualTo(3.5f).Within(0.0001f));
        }

        [Test]
        public void CrossingRunningGraceInsideOneFrameDoesNotChargeTheWholeFrame()
        {
            var pool = new StaminaPool();
            pool.Tick(StaminaActivity.Running, 2.9f);
            pool.Tick(StaminaActivity.Running, 0.3f);
            Assert.That(pool.Current, Is.EqualTo(99.6f).Within(0.0001f));
        }

        [TestCase(StaminaActivity.Rest)]
        [TestCase(StaminaActivity.Airborne)]
        [TestCase(StaminaActivity.Attacking)]
        [TestCase(StaminaActivity.Climbing)]
        [TestCase(StaminaActivity.Gliding)]
        [TestCase(StaminaActivity.Swimming)]
        public void LeavingRunningStartsANewContinuousGracePeriod(StaminaActivity interruption)
        {
            var pool = new StaminaPool();
            pool.Tick(StaminaActivity.Running, 4f);
            pool.Tick(interruption, 0.1f);
            Assert.That(pool.RunSeconds, Is.Zero);
            float beforeGrace = pool.Current;
            pool.Tick(StaminaActivity.Running, 3f);
            Assert.That(pool.Current, Is.EqualTo(beforeGrace).Within(0.0001f));
            pool.Tick(StaminaActivity.Running, 0.5f);
            Assert.That(pool.Current, Is.EqualTo(beforeGrace - 1f).Within(0.0001f));
        }

        [TestCase(StaminaActivity.Climbing, 8f)]
        [TestCase(StaminaActivity.Gliding, 5f)]
        [TestCase(StaminaActivity.Swimming, 6f)]
        public void TraversalUsesTheSpecifiedPerSecondCosts(StaminaActivity activity, float cost)
        {
            var pool = new StaminaPool();
            pool.Tick(activity, 2.5f);
            Assert.That(pool.Current, Is.EqualTo(100f - cost * 2.5f).Within(0.0001f));
        }

        [Test]
        public void GroundedRestRecoversOnlyThePartAfterTheOneSecondDelay()
        {
            var pool = new StaminaPool();
            pool.Tick(StaminaActivity.Climbing, 1f);
            pool.Tick(StaminaActivity.Rest, 0.75f);
            Assert.That(pool.Current, Is.EqualTo(92f));
            pool.Tick(StaminaActivity.Rest, 0.5f);
            Assert.That(pool.Current, Is.EqualTo(95.75f).Within(0.0001f));
            pool.Tick(StaminaActivity.Rest, 1f);
            Assert.That(pool.Current, Is.EqualTo(100f));
        }

        [Test]
        public void RunningGraceAirborneAndAttackTimeDoNotRegenerateStamina()
        {
            var pool = new StaminaPool();
            pool.SetCurrent(50f);
            pool.Tick(StaminaActivity.Running, 3f);
            Assert.That(pool.Current, Is.EqualTo(50f));
            pool.Tick(StaminaActivity.Airborne, 10f);
            Assert.That(pool.Current, Is.EqualTo(50f));
            pool.Tick(StaminaActivity.Attacking, 10f);
            Assert.That(pool.Current, Is.EqualTo(50f));
            pool.Tick(StaminaActivity.Rest, 1f);
            Assert.That(pool.Current, Is.EqualTo(65f));
        }

        [Test]
        public void RecoveryDelayElapsesInTheAirButRecoveryWaitsForGroundedRest()
        {
            var pool = new StaminaPool();
            pool.Tick(StaminaActivity.Gliding, 2f);
            pool.Tick(StaminaActivity.Airborne, 0.75f);
            Assert.That(pool.Current, Is.EqualTo(90f));
            pool.Tick(StaminaActivity.Rest, 0.5f);
            Assert.That(pool.Current, Is.EqualTo(93.75f).Within(0.0001f));
        }

        [Test]
        public void ExhaustionInsideALongFrameStartsDelayAtTheActualLastSpend()
        {
            var pool = new StaminaPool();
            pool.SetCurrent(4f);
            pool.Tick(StaminaActivity.Climbing, 1.25f);
            Assert.That(pool.Current, Is.Zero);
            // Four points lasted 0.5 s. The remaining 0.75 s already counted toward the delay.
            pool.Tick(StaminaActivity.Rest, 0.5f);
            Assert.That(pool.Current, Is.EqualTo(3.75f).Within(0.0001f));
        }

        [Test]
        public void LargeAndPartitionedTicksProduceTheSameRunningAndRecoveryState()
        {
            var whole = new StaminaPool();
            var split = new StaminaPool();
            whole.Tick(StaminaActivity.Running, 5.25f);
            for (int i = 0; i < 105; i++) split.Tick(StaminaActivity.Running, 0.05f);
            whole.Tick(StaminaActivity.Airborne, 0.5f);
            for (int i = 0; i < 10; i++) split.Tick(StaminaActivity.Airborne, 0.05f);
            whole.Tick(StaminaActivity.Rest, 0.7f);
            for (int i = 0; i < 14; i++) split.Tick(StaminaActivity.Rest, 0.05f);
            Assert.That(whole.Current, Is.EqualTo(98.5f).Within(0.0001f));
            Assert.That(split.Current, Is.EqualTo(whole.Current).Within(0.0001f));
            Assert.That(split.RunSeconds, Is.EqualTo(whole.RunSeconds));
        }

        [Test]
        public void PartitioningAcrossExhaustionKeepsTheSameRecoveryStart()
        {
            var whole = new StaminaPool();
            var split = new StaminaPool();
            whole.SetCurrent(4f);
            split.SetCurrent(4f);
            whole.Tick(StaminaActivity.Climbing, 1.25f);
            for (int i = 0; i < 25; i++) split.Tick(StaminaActivity.Climbing, 0.05f);
            whole.Tick(StaminaActivity.Rest, 0.5f);
            for (int i = 0; i < 10; i++) split.Tick(StaminaActivity.Rest, 0.05f);
            Assert.That(split.Current, Is.EqualTo(whole.Current).Within(0.0001f));
        }

        [Test]
        public void DepletionAndRecoveryNeverPassThePoolBounds()
        {
            var pool = new StaminaPool(12f);
            pool.Tick(StaminaActivity.Swimming, 100f);
            Assert.That(pool.Current, Is.Zero);
            pool.Tick(StaminaActivity.Rest, 100f);
            Assert.That(pool.Current, Is.EqualTo(12f));
            pool.SetCurrent(-50f);
            Assert.That(pool.Current, Is.Zero);
            pool.SetCurrent(1000f);
            Assert.That(pool.Current, Is.EqualTo(12f));
        }

        [Test]
        public void RestoreClearsRunningAndRecoveryTimers()
        {
            var pool = new StaminaPool(50f);
            pool.Tick(StaminaActivity.Running, 5f);
            pool.Restore();
            Assert.That(pool.Current, Is.EqualTo(50f));
            Assert.That(pool.Maximum, Is.EqualTo(50f));
            Assert.That(pool.RunSeconds, Is.Zero);
            pool.SetCurrent(10f);
            pool.Tick(StaminaActivity.Rest, 0.5f);
            Assert.That(pool.Current, Is.EqualTo(17.5f));
        }

        [Test]
        public void ZeroTimeIsANoOpAndInvalidValuesAreRejected()
        {
            var pool = new StaminaPool();
            pool.Tick(StaminaActivity.Running, 4f);
            pool.Tick(StaminaActivity.Rest, 0f);
            Assert.That(pool.Current, Is.EqualTo(98f));
            Assert.That(pool.RunSeconds, Is.EqualTo(4f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StaminaPool(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StaminaPool(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StaminaPool(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.SetCurrent(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.SetCurrent(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Tick(StaminaActivity.Rest, -1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Tick(StaminaActivity.Rest, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Tick((StaminaActivity)100, 1f));
        }
    }
}