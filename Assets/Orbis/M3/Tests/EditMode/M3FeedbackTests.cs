using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Timeline;

namespace Orbis.M3.Tests
{
    public sealed class M3FeedbackTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private M3Feedback feedback;
        private float originalScale;
        private float originalFixed;
        private bool originalImpulseClock;

        [SetUp]
        public void SetUp()
        {
            originalScale = Time.timeScale;
            originalFixed = Time.fixedDeltaTime;
            originalImpulseClock = CinemachineImpulseManager.Instance.IgnoreTimeScale;
            Time.timeScale = 0.65f;
            Time.fixedDeltaTime = 0.02f;
            feedback = CreateFeedback("Feedback Under Test");
        }

        [TearDown]
        public void TearDown()
        {
            // EditMode explicitly closes public leases. Runtime OnDisable/scene-unload cleanup is covered in PlayMode.
            foreach (GameObject instance in objects)
                if (instance != null) instance.GetComponent<M3Feedback>()?.CancelAll();
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
            Time.timeScale = originalScale;
            Time.fixedDeltaTime = originalFixed;
            CinemachineImpulseManager.Instance.IgnoreTimeScale = originalImpulseClock;
        }

        [Test]
        public void HitstopTemporarilyOverridesSlowMotionThenRestoresTheOriginalClock()
        {
            Assert.That(feedback.SetSlowMotion(0.2f), Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.13f).Within(0.00001f));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(0.004f).Within(0.00001f));
            feedback.RequestHitStop(0.08f);
            Assert.That(Time.timeScale, Is.Zero);
            feedback.Tick(0.04f);
            Assert.That(feedback.IsFrozen, Is.True);
            feedback.Tick(0.05f);
            Assert.That(feedback.IsFrozen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(0.13f).Within(0.00001f));
            feedback.ClearSlowMotion();
            Assert.That(Time.timeScale, Is.EqualTo(0.65f));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(0.02f).Within(0.000001f));
        }

        [Test]
        public void SimultaneousHitstopsExtendToTheLongestRemainingTimeWithoutAdding()
        {
            feedback.RequestHitStop(0.08f);
            feedback.Tick(0.02f);
            feedback.RequestHitStop(0.05f);
            Assert.That(feedback.FreezeRemaining, Is.EqualTo(0.06f).Within(0.00001f));
            for (int i = 0; i < 10; i++) feedback.RequestHitStop(0.08f);
            Assert.That(feedback.FreezeRemaining, Is.EqualTo(0.08f).Within(0.00001f));
            feedback.Tick(0.081f);
            Assert.That(Time.timeScale, Is.EqualTo(0.65f));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(0.02f).Within(0.000001f));
        }

        [TestCase(M3HitStrength.Weak, 0.1f, 0f)]
        [TestCase(M3HitStrength.Strong, 0.3f, 0.08f)]
        [TestCase(M3HitStrength.Burst, 0.6f, 0.25f)]
        public void ActualImpulseAndHitstopUseTheSpecifiedStrengths(M3HitStrength strength, float amplitude, float stop)
        {
            Assert.That(feedback.PlayHit(strength, Vector3.zero), Is.True);
            Assert.That(feedback.LastImpulseAmplitude, Is.EqualTo(amplitude));
            Assert.That(feedback.ImpulseCount, Is.EqualTo(1));
            Assert.That(feedback.FreezeRemaining, Is.EqualTo(stop));
            Assert.That(feedback.ImpulseSource, Is.Not.Null);
            Assert.That(feedback.ImpulseSource.ImpulseDefinition.ImpulseType,
                Is.EqualTo(CinemachineImpulseDefinition.ImpulseTypes.Uniform));
            Assert.That(Time.timeScale, Is.EqualTo(stop > 0f ? 0f : 0.65f));
        }

        [Test]
        public void CancelRestoresNonDefaultBaselineDuringNestedEffects()
        {
            feedback.SetSlowMotion(0.2f);
            feedback.RequestHitStop(0.25f);
            feedback.CancelAll();
            Assert.That(feedback.FreezeRemaining, Is.Zero);
            Assert.That(feedback.SlowMotionScale, Is.EqualTo(1f));
            Assert.That(Time.timeScale, Is.EqualTo(0.65f));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(0.02f).Within(0.000001f));
            feedback.Tick(10f);
            Assert.That(Time.timeScale, Is.EqualTo(0.65f));
        }

        [Test]
        public void ASecondServiceCannotOverwriteOrRestoreAnotherOwnersClock()
        {
            M3Feedback second = CreateFeedback("Second Feedback");
            feedback.RequestHitStop(0.08f);
            Assert.That(second.RequestHitStop(1f), Is.False);
            Assert.That(second.SetSlowMotion(0.5f), Is.False);
            second.CancelAll();
            Assert.That(Time.timeScale, Is.Zero);
            feedback.Tick(0.09f);
            Assert.That(Time.timeScale, Is.EqualTo(0.65f));
            Assert.That(second.RequestHitStop(0.03f), Is.True);
            second.CancelAll();
            Assert.That(Time.timeScale, Is.EqualTo(0.65f));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(0.02f).Within(0.000001f));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidDurationsCannotAcquireOrCorruptTheClock(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => feedback.RequestHitStop(value));
            Assert.Throws<ArgumentOutOfRangeException>(() => feedback.Tick(value));
            Assert.That(Time.timeScale, Is.EqualTo(0.65f));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(0.02f).Within(0.000001f));
        }

        [TestCase(0f)]
        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        public void InvalidSlowMotionScalesCannotAcquireTheClock(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => feedback.SetSlowMotion(value));
            Assert.That(Time.timeScale, Is.EqualTo(0.65f));
        }

        [Test]
        public void APreviouslyPausedClockRemainsPausedAfterFeedback()
        {
            Time.timeScale = 0f;
            Time.fixedDeltaTime = 0.015f;
            feedback.RequestHitStop(0.08f);
            feedback.Tick(0.1f);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(0.015f).Within(0.000001f));
        }

        [Test]
        public void AuthoredTimelineHasFiveOrderedSignalsAndUnscaledPhaseDurations()
        {
            TimelineAsset timeline = Resources.Load<TimelineAsset>(M3UltimateDirector.TimelineResource);
            Assert.That(timeline, Is.Not.Null, "Run Orbis > M3 > Setup and Validate to create the authored Timeline.");
            Assert.That(timeline.durationMode, Is.EqualTo(TimelineAsset.DurationMode.FixedLength));
            Assert.That(timeline.fixedDuration, Is.EqualTo(1.05d).Within(0.001d));
            var signals = new List<SignalEmitter>();
            foreach (TrackAsset track in timeline.GetOutputTracks())
                if (track is SignalTrack)
                    foreach (IMarker marker in track.GetMarkers())
                        if (marker is SignalEmitter emitter) signals.Add(emitter);
            signals.Sort((a, b) => a.time.CompareTo(b.time));
            Assert.That(signals.Count, Is.EqualTo(5));
            Assert.That(signals[0].asset.name, Is.EqualTo("CloseUp"));
            Assert.That(signals[1].asset.name, Is.EqualTo("SlowMotion"));
            Assert.That(signals[2].asset.name, Is.EqualTo("ElementBurst"));
            Assert.That(signals[3].asset.name, Is.EqualTo("SoundImpact"));
            Assert.That(signals[4].asset.name, Is.EqualTo("RestoreAndResidue"));
            Assert.That(signals[1].time - signals[0].time, Is.EqualTo(0.3d).Within(0.001d));
            Assert.That(signals[2].time - signals[1].time, Is.EqualTo(0.4d).Within(0.001d));
            foreach (SignalEmitter signal in signals)
            {
                Assert.That(signal.emitOnce, Is.True);
                Assert.That(signal.retroactive, Is.True, "A slow frame must not skip a Timeline phase.");
            }
        }

        private M3Feedback CreateFeedback(string name)
        {
            var instance = new GameObject(name);
            objects.Add(instance);
            var service = instance.AddComponent<M3Feedback>();
            service.AutoTick = false;
            return service;
        }
    }
}

