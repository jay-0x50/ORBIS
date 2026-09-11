using System;

namespace Orbis.M0
{
    /// <summary>Unity에 의존하지 않는 지상 기본 공격 3단 콤보의 시간 상태머신.</summary>
    public sealed class ComboSequence
    {
        public readonly struct StepTiming
        {
            public readonly float Duration;
            public readonly float ActiveFrom;
            public readonly float ActiveTo;
            public readonly float BufferFrom;

            public StepTiming(float duration, float activeFrom, float activeTo, float bufferFrom)
            {
                if (!IsFinite(duration) || !IsFinite(activeFrom) || !IsFinite(activeTo) ||
                    !IsFinite(bufferFrom) || duration <= 0f || activeFrom < 0f ||
                    activeFrom >= activeTo || activeTo > duration || bufferFrom < 0f ||
                    bufferFrom >= duration)
                    throw new ArgumentOutOfRangeException(nameof(duration), "Invalid combo step timing.");

                Duration = duration;
                ActiveFrom = activeFrom;
                ActiveTo = activeTo;
                BufferFrom = bufferFrom;
            }
        }

        // 기획서에 없는 M0 기본값(초): 각 타 0.50/0.55/0.65초, 후반 입력 1개만 예약.
        // 피격 판정은 각 타의 중간 구간에만 존재하며, 종료 후 추가 쿨다운은 없다.
        private static readonly StepTiming[] Timings =
        {
            new StepTiming(0.50f, 0.16f, 0.27f, 0.29f),
            new StepTiming(0.55f, 0.18f, 0.30f, 0.31f),
            new StepTiming(0.65f, 0.23f, 0.40f, 0.37f)
        };

        private float elapsed;
        private bool nextStepBuffered;

        public const int StepCount = 3;
        public bool IsAttacking => CurrentStep != 0;
        public int CurrentStep { get; private set; }
        public float NormalizedTime => IsAttacking ? elapsed / Timings[CurrentStep - 1].Duration : 0f;
        public bool HasBufferedAttack => nextStepBuffered;
        public bool IsInActiveWindow => IsAttacking &&
            elapsed >= Timings[CurrentStep - 1].ActiveFrom &&
            elapsed < Timings[CurrentStep - 1].ActiveTo;

        /// <summary>1부터 시작하는 타 번호. 방향/애니메이션을 이 시점에 변경한다.</summary>
        public event Action<int> StepStarted;

        /// <summary>
        /// 해당 Tick 구간이 유효 타격 구간과 겹치면 호출된다. 긴 프레임에서도 타격 구간을 건너뛰지 않는다.
        /// 한 타가 여러 프레임에 걸쳐 호출될 수 있으므로 수신자가 대상별 중복을 제거한다.
        /// </summary>
        public event Action<int> ActiveWindowVisited;

        public static StepTiming GetTiming(int step)
        {
            if (step < 1 || step > StepCount)
                throw new ArgumentOutOfRangeException(nameof(step));
            return Timings[step - 1];
        }

        /// <returns>새 공격을 시작하거나 다음 한 타 입력을 새로 예약했을 때만 true.</returns>
        public bool RequestAttack(bool isGrounded)
        {
            if (!IsAttacking)
            {
                if (!isGrounded)
                    return false;

                StartStep(1);
                return true;
            }

            // 이미 시작한 지상 콤보는 유지한다. 공중에서 새로운 콤보를 시작할 수는 없다.
            if (CurrentStep >= StepCount || nextStepBuffered ||
                elapsed < Timings[CurrentStep - 1].BufferFrom)
                return false;

            nextStepBuffered = true;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (!IsFinite(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));

            float remaining = deltaTime;
            while (IsAttacking && remaining > 0f)
            {
                int step = CurrentStep;
                StepTiming timing = Timings[step - 1];
                float previous = elapsed;
                float advance = Math.Min(remaining, timing.Duration - elapsed);
                elapsed = Math.Min(timing.Duration, elapsed + advance);
                remaining = Math.Max(0f, remaining - advance);

                if (previous < timing.ActiveTo && elapsed >= timing.ActiveFrom)
                    ActiveWindowVisited?.Invoke(step);

                // 이벤트 수신자가 취소하는 경우 종료된 타의 완료 로직을 실행하지 않는다.
                if (CurrentStep != step)
                    return;

                if (elapsed < timing.Duration)
                    break;

                if (nextStepBuffered && step < StepCount)
                    StartStep(step + 1);
                else
                    CancelAttack();
            }
        }

        public void CancelAttack()
        {
            CurrentStep = 0;
            elapsed = 0f;
            nextStepBuffered = false;
        }

        private void StartStep(int step)
        {
            CurrentStep = step;
            elapsed = 0f;
            nextStepBuffered = false;
            StepStarted?.Invoke(step);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
