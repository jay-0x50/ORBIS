using System;
using Orbis.M0;
using Orbis.M1;
using Orbis.M3;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.M4
{
    /// <summary>고정 위치 필드 보스. 기존 M0 적중과 M1 반응/실드 피해를 그대로 연결한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ElementalActor), typeof(TrainingDummy))]
    [DefaultExecutionOrder(-55)]
    public sealed class M4FieldBoss : MonoBehaviour
    {
        private ElementalReactionManager manager;
        private PartyManager party;
        private Transform player;
        private M4PlayerVitals vitals;
        private M4BossModel model;
        private Collider[] hitColliders;
        private bool[] colliderDefaults;
        private Vector3 anchor;
        private Quaternion anchorRotation;
        private bool subscribed;
        private Func<ElementType, float> previousScale;
        private Func<ElementType, float> ownScale;
        private GameObject ring;
        private Mesh ringMesh;
        private Renderer ringRenderer;
        private MaterialPropertyBlock ringProperties;

        public bool AutoTick { get; set; } = true;
        public ElementalActor Actor { get; private set; }
        public M4RegionId Region { get; private set; }
        public ElementType Weakness => model?.Weakness ?? ElementType.None;
        public M4BossProfile Profile => model?.Profile;
        public M4BossState State => model?.State ?? M4BossState.Dormant;
        public float HitPoints => model?.HitPoints ?? 0f;
        public float MaximumHitPoints => Profile?.MaximumHealth ?? 0f;
        public int WeakHitCount => model?.WeakHitCount ?? 0;
        public float TelegraphProgress => model?.TelegraphProgress ?? 0f;
        public float AttackRadius => Profile?.AttackRadius ?? 0f;
        public float StateRemaining => model?.StateRemaining ?? 0f;
        public bool UsesCoreExposure => model != null && model.UseCoreExposure;
        public bool TelegraphVisible => ringRenderer != null && ringRenderer.enabled;
        public event Action Changed;
        public event Action<M4RegionId> Defeated;
        public event Action AttackExecuted;

        public void Configure(M4RegionId region, ElementType element, ElementType weakness,
            ElementalReactionManager reactionManager, PartyManager owner, Transform playerTransform,
            bool useCoreExposure)
        {
            if (reactionManager == null || owner == null || playerTransform == null)
                throw new ArgumentNullException("Field bosses need reactions, a party and the shared player.");
            Unsubscribe();
            if (model != null)
            {
                model.Changed -= OnModelChanged; model.AttackReady -= ExecuteAttack; model.Defeated -= OnDefeated;
            }
            manager = reactionManager; party = owner; player = playerTransform; Region = region;
            Actor = GetComponent<ElementalActor>();
            // Identity와 상성 핵은 부착 오라가 아니다. 매 프레임 고유 원소 오라를 재부착하지 않는다.
            Actor.Configure("M4.Boss." + region, element, ActorTeam.Enemy);
            anchor = transform.position; anchorRotation = transform.rotation;
            if (hitColliders != null)
                for (int i = 0; i < hitColliders.Length; i++)
                    if (hitColliders[i] != null) hitColliders[i].enabled = colliderDefaults[i];
            hitColliders = GetComponentsInChildren<Collider>(true);
            colliderDefaults = new bool[hitColliders.Length];
            for (int i = 0; i < hitColliders.Length; i++) colliderDefaults[i] = hitColliders[i].enabled;
            model = new M4BossModel(M4BossProfile.ForElement(element), weakness, useCoreExposure);
            model.Changed += OnModelChanged; model.AttackReady += ExecuteAttack; model.Defeated += OnDefeated;
            CreateTelegraph();
            Subscribe();
            ResetEncounter(false);
        }

        public void SetPlayerVitals(M4PlayerVitals playerVitals) => vitals = playerVitals;

        public void ResetEncounter(bool defeatedThisWeek)
        {
            if (model == null) return;
            if (manager != null) manager.ResetTarget(Actor);
            else if (Actor != null) Actor.ResetState();
            GetComponent<TrainingDummy>().ResetHits();
            transform.SetPositionAndRotation(anchor, anchorRotation);
            model.Reset(defeatedThisWeek);
            Physics.SyncTransforms();
        }

        private void OnEnable()
        {
            Subscribe();
            if (model != null) OnModelChanged();
        }
        private void OnDisable()
        {
            Unsubscribe();
            if (model != null) ResetEncounter(State == M4BossState.Defeated);
            if (ringRenderer != null) ringRenderer.enabled = false;
        }
        private void OnDestroy()
        {
            Unsubscribe();
            if (ringMesh != null)
            {
                if (Application.isPlaying) Destroy(ringMesh);
                else DestroyImmediate(ringMesh);
            }
        }
        private void Update() { if (AutoTick) Tick(Time.deltaTime); }
        private void LateUpdate()
        {
            // 대형 보스 핵은 고정 앵커다. M1 과부하의 작은 타깃 이동을 다음 판정까지 누적시키지 않는다.
            if (model != null && (transform.position != anchor || transform.rotation != anchorRotation))
            {
                transform.SetPositionAndRotation(anchor, anchorRotation);
                Physics.SyncTransforms();
            }
        }

        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (model == null || !isActiveAndEnabled || State == M4BossState.Defeated) return;
            ElementalActor active = party != null ? party.ActiveMember?.Actor : null;
            bool available = player != null && active != null && active.isActiveAndEnabled && active.IsOnField &&
                (vitals == null || !vitals.IsKnockedOut);
            Vector3 offset = available ? player.position - anchor : Vector3.one * 1000f;
            float horizontal = new Vector2(offset.x, offset.z).magnitude;
            // 기획서 미정: 높이 차 4m 이상은 별도 층으로 보고 교전을 해제한다.
            bool withinLeash = available && horizontal <= Profile.LeashRadius && Mathf.Abs(offset.y) <= 4f;
            if (!withinLeash && (State != M4BossState.Dormant || HitPoints != MaximumHitPoints || WeakHitCount != 0))
            {
                ResetEncounter(false); // DoT/오라까지 지워 경기장 밖에서 처치가 누적되지 않도록 한다.
                return;
            }
            model.Tick(deltaTime, horizontal <= Profile.EngagementRadius, withinLeash, available);
        }

        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled || manager == null || model == null || Actor == null) return;
            previousScale = Actor.IncomingDamageScale;
            ownScale = ScaleDamage;
            Actor.IncomingDamageScale = ownScale;
            manager.Applied += OnApplied;
            manager.DamageApplied += OnDamage;
            subscribed = true;
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (manager != null) { manager.Applied -= OnApplied; manager.DamageApplied -= OnDamage; }
            if (Actor != null && Actor.IncomingDamageScale == ownScale) Actor.IncomingDamageScale = previousScale;
            previousScale = null; ownScale = null; subscribed = false;
        }
        private float ScaleDamage(ElementType element)
            => (previousScale?.Invoke(element) ?? 1f) * model.DamageMultiplier(element);

        private void OnApplied(ElementApplicationEvent application)
        {
            if (application.Target != Actor || application.Source == null || application.Source.Team != ActorTeam.Player ||
                application.BaseDamage <= 0f) return;
            model.RegisterDirectHit(application.IncomingElement, application.IsPropagation);
        }
        private void OnDamage(ElementDamageEvent damage)
        {
            if (damage.Target != Actor) return;
            // 반응 기본 피해/추가 피해는 각 실제 이벤트당 한 번만 소비한다. Applied에서 HP를 빼지 않는다.
            model.ReceiveDamage(damage.AppliedDamage);
        }
        private void OnDefeated() => Defeated?.Invoke(Region);
        private void OnModelChanged()
        {
            bool targetable = isActiveAndEnabled && State != M4BossState.Defeated;
            if (Actor != null) Actor.IsOnField = targetable;
            if (hitColliders != null)
                for (int i = 0; i < hitColliders.Length; i++)
                    if (hitColliders[i] != null) hitColliders[i].enabled = targetable && colliderDefaults[i];
            if (ringRenderer != null)
            {
                ringRenderer.enabled = targetable && State == M4BossState.Telegraph;
                ringProperties.Clear();
                M3Palette.Set(ringProperties, Actor.Element, 0f);
                Color color = M3Palette.Primary(Actor.Element);
                Color warning = Color.Lerp(color, Color.white, TelegraphProgress * .6f);
                ringProperties.SetColor("_PrimaryColor", warning);
                ringRenderer.SetPropertyBlock(ringProperties);
            }
            Changed?.Invoke();
        }

        private void ExecuteAttack()
        {
            if (!isActiveAndEnabled || State == M4BossState.Defeated || (vitals != null && vitals.IsKnockedOut)) return;
            ElementalActor target = party?.ActiveMember?.Actor;
            if (target == null || !target.IsOnField || !target.isActiveAndEnabled) return;
            AttackExecuted?.Invoke();
            Vector3 delta = target.EffectCenter - Actor.EffectCenter;
            // 바닥 원형 예고와 일치하는 수평 반경. 2m 이상 높이에서는 범위 공격을 피할 수 있다.
            if (new Vector2(delta.x, delta.z).sqrMagnitude > AttackRadius * AttackRadius || Mathf.Abs(delta.y) > 2f) return;
            if (Physics.Linecast(Actor.EffectCenter, target.EffectCenter, 1 << 8, QueryTriggerInteraction.Ignore)) return;
            manager.Apply(target, Actor.Element, Actor, Profile.AttackDamage);
        }

        private void CreateTelegraph()
        {
            if (ring == null)
            {
                ring = new GameObject("M4 Boss Telegraph");
                ring.transform.SetParent(transform, false);
                ringMesh = new Mesh { name = "M4 Telegraph Quad" };
                ringMesh.vertices = new[] { new Vector3(-.5f, 0f, -.5f), new Vector3(-.5f, 0f, .5f),
                    new Vector3(.5f, 0f, .5f), new Vector3(.5f, 0f, -.5f) };
                ringMesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                ringMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                ringMesh.RecalculateNormals(); ringMesh.RecalculateBounds();
                ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;
                ringRenderer = ring.AddComponent<MeshRenderer>();
                ringRenderer.sharedMaterial = Resources.Load<Material>("M3/Materials/SwirlRing");
                ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
                ringRenderer.receiveShadows = false;
                ringProperties = new MaterialPropertyBlock();
            }
            ring.transform.localPosition = Vector3.up * .04f;
            ring.transform.localScale = Vector3.one * (Profile.AttackRadius * 2f);
            ringRenderer.enabled = false;
        }
    }
}



