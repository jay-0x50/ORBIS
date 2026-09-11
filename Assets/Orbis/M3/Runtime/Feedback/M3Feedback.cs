using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace Orbis.M3
{
    public enum M3HitStrength { Weak, Strong, Burst }

    /// <summary>Unscaled hitstop and Cinemachine Impulse, with explicit ownership of every changed global clock.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed class M3Feedback : MonoBehaviour
    {
        private struct ImpulseLease
        {
            public CinemachineImpulseManager.ImpulseEvent Event;
            public float Started;
        }

        private const int Channel = 1 << 6;
        private static M3Feedback clockOwner;
        private CinemachineImpulseSource impulseSource;
        private readonly List<ImpulseLease> impulses = new List<ImpulseLease>();
        private readonly List<CinemachineImpulseListener> ownedListeners = new List<CinemachineImpulseListener>();
        private bool hasClock;
        private float savedTimeScale;
        private float savedFixedDeltaTime;
        private float slowScale = 1f;
        private bool impulseClockOwned;
        private bool savedImpulseIgnoreTimeScale;
        private double lastRealtime;

        public bool AutoTick { get; set; } = true;
        public float FreezeRemaining { get; private set; }
        public bool IsFrozen => FreezeRemaining > 0f;
        public float SlowMotionScale => slowScale;
        public float LastImpulseAmplitude { get; private set; }
        public int ImpulseCount { get; private set; }
        public CinemachineImpulseSource ImpulseSource => impulseSource;

        public void Configure(CinemachineCamera gameplayCamera)
        {
            EnsureImpulseSource();
            AttachListener(gameplayCamera);
        }

        public CinemachineImpulseListener AttachListener(CinemachineCamera camera)
        {
            if (camera == null) return null;
            // M3 owns a separate listener/channel, preserving any existing camera feedback configuration.
            foreach (CinemachineImpulseListener listener in ownedListeners)
                if (listener != null && listener.gameObject == camera.gameObject) return listener;
            var added = camera.gameObject.AddComponent<CinemachineImpulseListener>();
            added.ChannelMask = Channel;
            added.Gain = 1f;
            added.UseCameraSpace = true;
            added.ApplyAfter = CinemachineCore.Stage.Noise;
            added.ReactionSettings = default; // No extra unrequested secondary shake tail.
            added.enabled = isActiveAndEnabled;
            ownedListeners.Add(added);
            return added;
        }

        public bool PlayHit(M3HitStrength strength, Vector3 position)
        {
            if (!isActiveAndEnabled) return false;
            EnsureImpulseSource();
            float amplitude;
            switch (strength)
            {
                case M3HitStrength.Weak: amplitude = 0.1f; break;
                case M3HitStrength.Strong: amplitude = 0.3f; RequestHitStop(0.08f); break;
                case M3HitStrength.Burst: amplitude = 0.6f; RequestHitStop(0.25f); break;
                default: throw new ArgumentOutOfRangeException(nameof(strength));
            }
            // 기획서 03: 약 .1 / 강 .3 / 궁극기 .6. 미정 정지 기본값은 강 .08초 / 궁극기 .25초.
            LastImpulseAmplitude = amplitude;
            ImpulseCount++;
            var created = impulseSource.ImpulseDefinition.CreateAndReturnEvent(position, Vector3.down * amplitude);
            if (created != null) impulses.Add(new ImpulseLease { Event = created, Started = created.StartTime });
            return true;
        }

        public bool RequestHitStop(float unscaledSeconds)
        {
            ValidateDuration(unscaledSeconds);
            if (!isActiveAndEnabled || unscaledSeconds == 0f || !AcquireClock()) return false;
            // Simultaneous target hits extend to the largest remaining stop, never add their durations.
            FreezeRemaining = Mathf.Max(FreezeRemaining, unscaledSeconds);
            ApplyClock();
            return true;
        }

        public bool SetSlowMotion(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f || scale > 1f)
                throw new ArgumentOutOfRangeException(nameof(scale));
            if (scale == 1f) { ClearSlowMotion(); return true; }
            if (!isActiveAndEnabled || !AcquireClock()) return false;
            slowScale = scale;
            ApplyClock();
            return true;
        }

        public void ClearSlowMotion()
        {
            slowScale = 1f;
            ApplyClock();
        }

        public void Tick(float unscaledDeltaTime)
        {
            ValidateDuration(unscaledDeltaTime);
            FreezeRemaining = Mathf.Max(0f, FreezeRemaining - unscaledDeltaTime);
            ApplyClock();
            impulses.RemoveAll(lease => lease.Event == null || lease.Event.StartTime != lease.Started || lease.Event.Expired);
        }

        public void CancelAll()
        {
            FreezeRemaining = 0f;
            slowScale = 1f;
            RestoreClock();
            float now = CinemachineImpulseManager.Instance.CurrentTime;
            foreach (ImpulseLease lease in impulses)
            {
                // Cinemachine reuses expired events; never cancel a later event that recycled the same object.
                if (lease.Event != null && lease.Event.StartTime == lease.Started && lease.Event.Channel == Channel)
                    lease.Event.Cancel(now, true);
            }
            impulses.Clear();
        }

        private bool AcquireClock()
        {
            if (hasClock) return true;
            if (clockOwner != null && clockOwner != this) return false;
            clockOwner = this;
            hasClock = true;
            savedTimeScale = Time.timeScale;
            savedFixedDeltaTime = Time.fixedDeltaTime;
            lastRealtime = Time.realtimeSinceStartupAsDouble;
            return true;
        }

        private void ApplyClock()
        {
            if (!hasClock) return;
            if (!IsFrozen && slowScale >= 1f) { RestoreClock(); return; }
            float multiplier = IsFrozen ? 0f : slowScale;
            Time.timeScale = savedTimeScale * multiplier;
            // A fixed step must stay positive even when hitstop suspends the scaled clock completely.
            Time.fixedDeltaTime = IsFrozen ? savedFixedDeltaTime : Mathf.Max(0.0001f, savedFixedDeltaTime * slowScale);
        }

        private void RestoreClock()
        {
            if (!hasClock) return;
            Time.timeScale = savedTimeScale;
            Time.fixedDeltaTime = savedFixedDeltaTime;
            hasClock = false;
            if (clockOwner == this) clockOwner = null;
        }

        private void EnsureImpulseSource()
        {
            if (impulseSource == null)
            {
                impulseSource = gameObject.AddComponent<CinemachineImpulseSource>();
                impulseSource.ImpulseDefinition = new CinemachineImpulseDefinition
                {
                    ImpulseChannel = Channel,
                    ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump,
                    ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform,
                    ImpulseDuration = 0.2f, // 미정 M3 기본 진동 길이: 0.2초.
                    DissipationDistance = 100f,
                    DissipationRate = 0.25f,
                    PropagationSpeed = 343f
                };
            }
            if (!impulseClockOwned)
            {
                savedImpulseIgnoreTimeScale = CinemachineImpulseManager.Instance.IgnoreTimeScale;
                CinemachineImpulseManager.Instance.IgnoreTimeScale = true;
                impulseClockOwned = true;
            }
        }

        private void OnEnable()
        {
            lastRealtime = Time.realtimeSinceStartupAsDouble;
            foreach (CinemachineImpulseListener listener in ownedListeners)
                if (listener != null) listener.enabled = true;
            if (impulseSource != null) EnsureImpulseSource();
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (AutoTick) Tick((float)Math.Max(0d, now - lastRealtime));
            lastRealtime = now;
        }

        private void OnDisable()
        {
            CancelAll();
            foreach (CinemachineImpulseListener listener in ownedListeners)
                if (listener != null) listener.enabled = false;
            if (impulseClockOwned)
            {
                CinemachineImpulseManager.Instance.IgnoreTimeScale = savedImpulseIgnoreTimeScale;
                impulseClockOwned = false;
            }
        }

        private void OnDestroy()
        {
            RestoreClock();
            foreach (CinemachineImpulseListener listener in ownedListeners)
                if (listener != null) Destroy(listener);
            ownedListeners.Clear();
        }

        private static void ValidateDuration(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
