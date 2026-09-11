using System;

namespace Orbis.M2
{
    public enum StaminaActivity { Rest, Running, Climbing, Gliding, Swimming, Airborne, Attacking }

    /// <summary>
    /// Frame-independent M2 stamina bookkeeping with no Unity dependency.
    /// Only grounded walking/idle (Rest) recovers stamina; airborne, attacking and running grace time do not.
    /// </summary>
    public sealed class StaminaPool
    {
        // Design 02: running costs 2/s only after three continuous seconds;
        // climbing 8/s, gliding 5/s, swimming 6/s.
        private const double RunningGraceSeconds = 3d;
        private const double RunningCost = 2d;
        private const double ClimbingCost = 8d;
        private const double GlidingCost = 5d;
        private const double SwimmingCost = 6d;
        // Unspecified M2 defaults: 100 capacity, recovery 15/s after one second since the last spend.
        private const double RecoveryDelaySeconds = 1d;
        private const double RecoveryPerSecond = 15d;
        private readonly double maximum;
        private double current;
        private double runSeconds;
        private double recoveryDelayRemaining;

        public float Current => (float)current;
        public float Maximum => (float)maximum;
        public float RunSeconds => (float)Math.Min(runSeconds, float.MaxValue);

        public StaminaPool(float maximum = 100f)
        {
            if (!IsFinite(maximum) || maximum <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maximum), "Capacity must be finite and positive.");
            this.maximum = maximum;
            Restore();
        }

        public void Tick(StaminaActivity activity, float deltaTime)
        {
            if (!IsFinite(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime), "Time must be finite and nonnegative.");
            if (activity < StaminaActivity.Rest || activity > StaminaActivity.Attacking)
                throw new ArgumentOutOfRangeException(nameof(activity));
            if (deltaTime == 0f) return;

            double duration = deltaTime;
            double graceWithinTick = 0d;
            if (activity == StaminaActivity.Running)
            {
                graceWithinTick = Math.Min(duration, Math.Max(0d, RunningGraceSeconds - runSeconds));
                runSeconds += duration;
            }
            else runSeconds = 0d;

            if (activity == StaminaActivity.Rest)
            {
                double recoveringSeconds = Math.Max(0d, duration - recoveryDelayRemaining);
                recoveryDelayRemaining = Math.Max(0d, recoveryDelayRemaining - duration);
                current = Math.Min(maximum, current + recoveringSeconds * RecoveryPerSecond);
                return;
            }

            double costPerSecond;
            switch (activity)
            {
                case StaminaActivity.Running: costPerSecond = RunningCost; break;
                case StaminaActivity.Climbing: costPerSecond = ClimbingCost; break;
                case StaminaActivity.Gliding: costPerSecond = GlidingCost; break;
                case StaminaActivity.Swimming: costPerSecond = SwimmingCost; break;
                default: costPerSecond = 0d; break;
            }

            recoveryDelayRemaining = Math.Max(0d, recoveryDelayRemaining - duration);
            double spendingSeconds = duration - graceWithinTick;
            if (costPerSecond <= 0d || spendingSeconds <= 0d || current <= 0d) return;

            // If exhaustion happens inside a long frame, count the delay from that exact instant.
            // A zero pool cannot spend more, but it still cannot recover outside Rest.
            double actualSpendingSeconds = Math.Min(spendingSeconds, current / costPerSecond);
            current = Math.Max(0d, current - spendingSeconds * costPerSecond);
            double timeAfterLastSpend = spendingSeconds - actualSpendingSeconds;
            recoveryDelayRemaining = Math.Max(0d, RecoveryDelaySeconds - timeAfterLastSpend);
        }

        /// <summary>Reset to a full pool with a fresh running grace period and no recovery delay.</summary>
        public void Restore()
        {
            current = maximum;
            runSeconds = 0d;
            recoveryDelayRemaining = 0d;
        }

        /// <summary>Set a clamped amount for setup/reset tools without changing the activity timers.</summary>
        public void SetCurrent(float value)
        {
            if (!IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            current = Math.Max(0d, Math.Min(maximum, value));
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}