using System;
using Orbis.M1;

namespace Orbis.M4
{
    public enum M4BossState { Dormant, Telegraph, Recover, Exposed, Defeated }

    /// <summary>기획서 미정 수치: 느린 범위 공격을 원소별로 비교하는 필드 보스 프로토타입.</summary>
    public sealed class M4BossProfile
    {
        public float MaximumHealth { get; }
        public float TelegraphDuration { get; }
        public float RecoveryDuration { get; }
        public float AttackRadius { get; }
        public float AttackDamage { get; }
        public float EngagementRadius => 6f;
        public float LeashRadius => 8f;
        public int WeakHitsRequired => 3;
        public float ExposureDuration => 4f;
        public float ExposureMultiplier => 2f;

        private M4BossProfile(float health, float telegraph, float recovery, float radius, float damage)
        { MaximumHealth = health; TelegraphDuration = telegraph; RecoveryDuration = recovery; AttackRadius = radius; AttackDamage = damage; }

        public static M4BossProfile ForElement(ElementType element)
        {
            // 기획서에는 개별 보스 전투 수치가 없다. 체력 300–400, 예고 .8–1.3초로 구분한다.
            switch (element)
            {
                case ElementType.Fire: return new M4BossProfile(320f, 1f, 2.5f, 3f, 12f);
                case ElementType.Water: return new M4BossProfile(300f, 1.2f, 2.6f, 3.4f, 11f);
                case ElementType.Wind: return new M4BossProfile(300f, .8f, 2.1f, 2.8f, 10f);
                case ElementType.Rock: return new M4BossProfile(400f, 1.3f, 3f, 3.2f, 16f);
                case ElementType.Lightning: return new M4BossProfile(340f, .9f, 2.2f, 3f, 13f);
                default: throw new ArgumentOutOfRangeException(nameof(element));
            }
        }

        public static ElementType DefaultWeakness(ElementType element)
        {
            // 기획서 미정 상성 기본값. M1 반응표와 별개인 보스 핵 공략 속성이다.
            switch (element)
            {
                case ElementType.Fire: return ElementType.Water;
                case ElementType.Water: return ElementType.Lightning;
                case ElementType.Wind: return ElementType.Rock;
                case ElementType.Rock: return ElementType.Fire;
                case ElementType.Lightning: return ElementType.Water;
                default: throw new ArgumentOutOfRangeException(nameof(element));
            }
        }
    }

    /// <summary>Unity 시간/물리와 독립된 유한 보스 상태. 실제 피해는 M1의 실드 적용 후 값을 한 번만 받는다.</summary>
    public sealed class M4BossModel
    {
        public M4BossProfile Profile { get; }
        public ElementType Weakness { get; }
        public bool UseCoreExposure { get; }
        public M4BossState State { get; private set; } = M4BossState.Dormant;
        public float HitPoints { get; private set; }
        public float StateRemaining { get; private set; }
        public int WeakHitCount { get; private set; }
        public float TelegraphProgress => State == M4BossState.Telegraph ? 1f - StateRemaining / Profile.TelegraphDuration : 0f;
        public event Action Changed;
        public event Action AttackReady;
        public event Action Defeated;

        public M4BossModel(M4BossProfile profile, ElementType weakness, bool useCoreExposure)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            if (weakness == ElementType.None) throw new ArgumentOutOfRangeException(nameof(weakness));
            Weakness = weakness;
            UseCoreExposure = useCoreExposure;
            HitPoints = Profile.MaximumHealth;
        }

        public void Tick(float deltaTime, bool withinEngagement, bool withinLeash, bool playerAvailable = true)
        {
            Validate(deltaTime, nameof(deltaTime));
            if (State == M4BossState.Defeated) return;
            if (!withinLeash || !playerAvailable)
            {
                if (State != M4BossState.Dormant || HitPoints != Profile.MaximumHealth || WeakHitCount != 0) Reset(false);
                return;
            }
            if (State == M4BossState.Dormant)
            {
                if (withinEngagement) Enter(M4BossState.Telegraph, Profile.TelegraphDuration);
                return;
            }
            if (deltaTime == 0f) return;
            StateRemaining = Math.Max(0f, StateRemaining - deltaTime);
            if (StateRemaining > 0f) { Changed?.Invoke(); return; }
            switch (State)
            {
                case M4BossState.Telegraph:
                    Enter(M4BossState.Recover, Profile.RecoveryDuration);
                    AttackReady?.Invoke();
                    break;
                case M4BossState.Exposed:
                    Enter(M4BossState.Recover, Profile.RecoveryDuration);
                    break;
                case M4BossState.Recover:
                    Enter(M4BossState.Telegraph, Profile.TelegraphDuration);
                    break;
            }
            // 큰 dt라도 한 프레임에 미래의 예고/공격을 반복하지 않는다. 새 예고는 항상 전체 시간을 보여 준다.
        }

        public bool RegisterDirectHit(ElementType element, bool isPropagation = false)
        {
            if (State == M4BossState.Defeated || State == M4BossState.Exposed || !UseCoreExposure ||
                isPropagation || element != Weakness) return false;
            WeakHitCount++;
            if (WeakHitCount >= Profile.WeakHitsRequired)
            {
                WeakHitCount = 0;
                // 세 번째 직접 적중 시 먼저 노출되므로 해당 적중의 실피해부터 2배가 된다.
                Enter(M4BossState.Exposed, Profile.ExposureDuration);
            }
            else Changed?.Invoke();
            return true;
        }

        public float DamageMultiplier(ElementType element)
            => UseCoreExposure ? (State == M4BossState.Exposed ? Profile.ExposureMultiplier : 1f)
                : (element == Weakness ? Profile.ExposureMultiplier : 1f);

        public void ReceiveDamage(float appliedDamage)
        {
            Validate(appliedDamage, nameof(appliedDamage));
            if (State == M4BossState.Defeated || appliedDamage == 0f) return;
            HitPoints = Math.Max(0f, HitPoints - appliedDamage);
            if (HitPoints <= 0f)
            {
                WeakHitCount = 0;
                Enter(M4BossState.Defeated, 0f);
                Defeated?.Invoke();
            }
            else Changed?.Invoke();
        }

        public void Reset(bool defeatedThisWeek)
        {
            HitPoints = defeatedThisWeek ? 0f : Profile.MaximumHealth;
            WeakHitCount = 0;
            // 저장된 주간 처치를 복원할 때 새 처치 이벤트/보상을 발생시키지 않는다.
            Enter(defeatedThisWeek ? M4BossState.Defeated : M4BossState.Dormant, 0f);
        }

        private void Enter(M4BossState state, float duration)
        { State = state; StateRemaining = duration; Changed?.Invoke(); }

        private static void Validate(float value, string name)
        { if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) throw new ArgumentOutOfRangeException(name); }
    }
}
