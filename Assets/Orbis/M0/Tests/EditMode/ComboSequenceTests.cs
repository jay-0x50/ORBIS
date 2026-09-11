using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Orbis.M0.Tests
{
    public sealed class ComboSequenceTests
    {
        [Test]
        public void AirborneInputCannotStartButLandingInputCan()
        {
            var combo = new ComboSequence();
            Assert.That(combo.RequestAttack(false), Is.False);
            Assert.That(combo.IsAttacking, Is.False);
            Assert.That(combo.RequestAttack(true), Is.True);
            Assert.That(combo.CurrentStep, Is.EqualTo(1));
        }

        [Test]
        public void EarlyInputIsIgnoredAndDoesNotSilentlyQueue()
        {
            var combo = new ComboSequence();
            combo.RequestAttack(true);
            combo.Tick(0.1f);
            Assert.That(combo.RequestAttack(true), Is.False);
            combo.Tick(1f);
            Assert.That(combo.IsAttacking, Is.False);
        }

        [Test]
        public void RepeatedBufferedInputsCannotSkipOrPrequeueThirdStrike()
        {
            var combo = new ComboSequence();
            var starts = new List<int>();
            combo.StepStarted += starts.Add;
            combo.RequestAttack(true);
            combo.Tick(0.35f);
            Assert.That(combo.RequestAttack(true), Is.True);
            for (int i = 0; i < 10; i++)
                Assert.That(combo.RequestAttack(true), Is.False);
            combo.Tick(2f);
            Assert.That(starts, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(combo.IsAttacking, Is.False);
        }

        [Test]
        public void ThreeStrikesNeedThreeTimedInputsAndFinishWithoutFourthStrike()
        {
            var combo = new ComboSequence();
            var starts = new List<int>();
            combo.StepStarted += starts.Add;
            combo.RequestAttack(true);
            combo.Tick(0.35f);
            combo.RequestAttack(true);
            combo.Tick(0.5f); // 1타 잔여 0.15초 + 2타 0.35초.
            Assert.That(combo.CurrentStep, Is.EqualTo(2));
            Assert.That(combo.RequestAttack(true), Is.True);
            combo.Tick(0.65f); // 2타 잔여 0.20초 + 3타 0.45초.
            Assert.That(combo.CurrentStep, Is.EqualTo(3));
            Assert.That(combo.RequestAttack(true), Is.False);
            combo.Tick(1f);
            Assert.That(starts, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(combo.CurrentStep, Is.Zero);
            Assert.That(combo.NormalizedTime, Is.Zero);
            Assert.That(combo.RequestAttack(true), Is.True, "No extra cooldown after the third strike.");
        }

        [Test]
        public void LongFrameStillVisitsEveryStartedStrikeActiveWindow()
        {
            var combo = new ComboSequence();
            var activeSteps = new List<int>();
            combo.ActiveWindowVisited += activeSteps.Add;
            combo.RequestAttack(true);
            combo.Tick(0.35f); // 이 한 프레임에서 1타 유효 구간 전체를 통과.
            combo.RequestAttack(true);
            combo.Tick(2f); // 이 한 프레임에서 2타 전체를 통과.
            Assert.That(activeSteps, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(combo.IsAttacking, Is.False);
        }

        [Test]
        public void ActiveWindowRunsAcrossFramesButNeverOutsideItsInterval()
        {
            var combo = new ComboSequence();
            int visits = 0;
            combo.ActiveWindowVisited += _ => visits++;
            combo.RequestAttack(true);
            combo.Tick(0.1f);
            Assert.That(visits, Is.Zero);
            combo.Tick(0.08f);
            combo.Tick(0.03f);
            Assert.That(visits, Is.EqualTo(2));
            combo.Tick(0.1f);
            int finishedVisits = visits;
            combo.Tick(0.1f);
            Assert.That(visits, Is.EqualTo(finishedVisits));
        }

        [Test]
        public void CancelClearsBufferedInputAndRestartsFromFirstStrike()
        {
            var combo = new ComboSequence();
            combo.RequestAttack(true);
            combo.Tick(0.35f);
            combo.RequestAttack(true);
            combo.CancelAttack();
            combo.Tick(5f);
            Assert.That(combo.HasBufferedAttack, Is.False);
            Assert.That(combo.CurrentStep, Is.Zero);
            combo.RequestAttack(true);
            Assert.That(combo.CurrentStep, Is.EqualTo(1));
            Assert.That(combo.NormalizedTime, Is.Zero);
        }

        [TestCase(-0.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidDeltaCannotCorruptSequence(float delta)
        {
            var combo = new ComboSequence();
            combo.RequestAttack(true);
            Assert.Throws<ArgumentOutOfRangeException>(() => combo.Tick(delta));
            Assert.That(combo.CurrentStep, Is.EqualTo(1));
            Assert.That(combo.NormalizedTime, Is.Zero);
        }

        [Test]
        public void ZeroDeltaDoesNotAdvanceOrApplyHit()
        {
            var combo = new ComboSequence();
            int visits = 0;
            combo.ActiveWindowVisited += _ => visits++;
            combo.RequestAttack(true);
            combo.Tick(0f);
            Assert.That(combo.NormalizedTime, Is.Zero);
            Assert.That(visits, Is.Zero);
        }

        [Test]
        public void CancellationFromHitCallbackCannotAdvanceOldSequence()
        {
            var combo = new ComboSequence();
            combo.ActiveWindowVisited += _ => combo.CancelAttack();
            combo.RequestAttack(true);
            combo.Tick(2f);
            Assert.That(combo.CurrentStep, Is.Zero);
            Assert.That(combo.HasBufferedAttack, Is.False);
        }
    }
}
