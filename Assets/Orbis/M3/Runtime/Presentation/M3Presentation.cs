using Orbis.M0;
using Orbis.M1;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orbis.M3
{
    /// <summary>Presentation adapter. Existing M1 events remain the only source of combat truth.</summary>
    public sealed class M3Presentation : MonoBehaviour
    {
        private ElementalReactionManager manager;
        private PartyManager party;
        private BasicAttackCombo combat;
        private PlayerMotor motor;
        private AudioSource[] voices;
        private AudioClip impactAudio;
        private int nextVoice;
        private ElementType previousElement;
        private bool subscribed;
        private UniversalAdditionalCameraData cameraData;
        private bool previousPostProcessing;
        private Volume flashVolume;
        private VolumeProfile flashProfile;
        private ColorAdjustments flashAdjustments;
        private float flashAge, flashDuration, flashStrength;
        private Color flashColor;
        private float popupAge = 10f;
        private int comboCount;
        private GUIStyle popupStyle;
        private int lastFeedbackFrame = -1;
        public M3VfxPool Pool { get; private set; }
        public M3MeshEffects MeshEffects { get; private set; }
        public M3Feedback Feedback { get; private set; }
        public M3UltimateDirector Ultimate { get; private set; }
        public ReactionType LastReaction { get; private set; }
        public int ReactionCount { get; private set; }
        public int ComboCount => comboCount;
        public int AudioPlayCount { get; private set; }
        /// <summary>Visual adapters swap the avatar here, after its outgoing pose is captured and before appearance starts.</summary>
        public event System.Action<PartyMember> AvatarSwitchRequested;

        public void Configure(ElementalReactionManager reactions, PartyManager owner, PlayerMotor player)
        {
            manager = reactions; party = owner; motor = player;
            combat = player.GetComponent<BasicAttackCombo>();
            var camera = player.transform.root.GetComponentInChildren<CinemachineCamera>();
            if (camera == null) camera = FindFirstObjectByType<CinemachineCamera>();
            Pool = gameObject.AddComponent<M3VfxPool>(); Pool.Prewarm();
            MeshEffects = gameObject.AddComponent<M3MeshEffects>();
            MeshEffects.Configure(party, combat, player.GetComponentInChildren<Animator>());
            Feedback = gameObject.AddComponent<M3Feedback>(); Feedback.Configure(camera);
            Ultimate = gameObject.AddComponent<M3UltimateDirector>();
            Ultimate.Configure(player.GetComponent<M0Input>(), player.transform, camera, Feedback);
            previousElement = party.ActiveMember.Actor.Element;
            impactAudio = Resources.Load<AudioClip>("M3/Audio/ElementImpact");
            if (impactAudio == null) throw new System.InvalidOperationException("M3 original impact audio missing.");
            // Unspecified audio pool defaults: 8 voices; oldest voice is reused under simultaneous impacts.
            voices = new AudioSource[8];
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = new GameObject("Pooled Impact Audio " + i);
                voice.transform.SetParent(transform, false);
                voices[i] = voice.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 0.35f; voices[i].minDistance = 2f; voices[i].maxDistance = 24f;
                voices[i].clip = impactAudio;
            }
            var flash = new GameObject("Transient Elemental Exposure");
            flash.transform.SetParent(transform, false);
            flashVolume = flash.AddComponent<Volume>();
            flashVolume.isGlobal = true; flashVolume.priority = 100f; flashVolume.weight = 0f;
            flashProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            flashAdjustments = flashProfile.Add<ColorAdjustments>(true);
            flashVolume.sharedProfile = flashProfile;
            cameraData = Camera.main.GetUniversalAdditionalCameraData();
            previousPostProcessing = cameraData.renderPostProcessing;
            cameraData.renderPostProcessing = true;
            Subscribe();
            MeshEffects.BeginAppearance();
        }

        private void Subscribe()
        {
            if (subscribed || manager == null) return;
            manager.Applied += OnApplied;
            manager.Reacted += OnReaction;
            manager.DamageApplied += OnDamage;
            manager.ChainLinked += OnChain;
            combat.HitLanded += OnHit;
            party.ActiveMemberChanged += OnMemberChanged;
            party.ActiveElementChanged += OnElementChanged;
            Ultimate.StageChanged += OnUltimateStage;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (manager != null)
            {
                manager.Applied -= OnApplied; manager.Reacted -= OnReaction;
                manager.DamageApplied -= OnDamage; manager.ChainLinked -= OnChain;
            }
            if (combat != null) combat.HitLanded -= OnHit;
            if (party != null) { party.ActiveMemberChanged -= OnMemberChanged; party.ActiveElementChanged -= OnElementChanged; }
            if (Ultimate != null) Ultimate.StageChanged -= OnUltimateStage;
            subscribed = false;
        }

        private void OnApplied(ElementApplicationEvent entry)
        {
            if (entry.Target == null) return;
            Pool.Play(M3EffectKind.Impact, entry.Target.EffectCenter, entry.IncomingElement,
                entry.IsPropagation ? 0.65f : 1f);
        }

        private void OnHit(TrainingDummy target, int step)
        {
            comboCount = popupAge > 1.2f ? 1 : comboCount + 1;
            popupAge = 0f;
            if (lastFeedbackFrame != Time.frameCount)
            {
                Feedback.PlayHit(step >= 3 ? M3HitStrength.Strong : M3HitStrength.Weak, target.transform.position);
                lastFeedbackFrame = Time.frameCount;
            }
            PlaySound(target.transform.position, party.ActiveMember.Actor.Element, false);
        }

        private void OnReaction(ElementReactionEvent entry)
        {
            if (entry.Target == null) return;
            LastReaction = entry.Reaction; ReactionCount++;
            Vector3 center = entry.Target.EffectCenter;
            ElementType element = entry.AffectedElement;
            switch (entry.Reaction)
            {
                case ReactionType.Vaporize:
                    Pool.Play(M3EffectKind.Vaporize, center, ElementType.Water);
                    Flash(Color.white, 0.22f, 0.7f); // Unspecified mild whiteout: .22 s, +.7 exposure.
                    break;
                case ReactionType.ElectroCharged:
                    Pool.Play(M3EffectKind.ElectroCharged, center, ElementType.Lightning);
                    break;
                case ReactionType.Overload:
                    Pool.Play(M3EffectKind.Overload, center, ElementType.Fire);
                    MeshEffects.EmitRing(entry.Target.transform.position, ElementType.Fire);
                    Feedback.PlayHit(M3HitStrength.Strong, center);
                    lastFeedbackFrame = Time.frameCount;
                    break;
                case ReactionType.Swirl:
                    Pool.Play(M3EffectKind.Swirl, center, element);
                    MeshEffects.EmitRing(entry.Target.transform.position, element);
                    MeshEffects.EmitAfterimage(element);
                    break;
                case ReactionType.Crystallize:
                    Pool.Play(M3EffectKind.Crystallize, center, element);
                    break;
            }
            PlaySound(center, element, false);
        }

        private void OnDamage(ElementDamageEvent entry)
        {
            if (entry.Target == null) return;
            if (entry.Reaction == ReactionType.ElectroCharged && entry.Element == ElementType.Lightning)
                Pool.Play(M3EffectKind.Impact, entry.Target.EffectCenter, ElementType.Lightning, 0.65f);
        }

        private void OnChain(ElementChainEvent entry)
        {
            MeshEffects.EmitChain(entry.Origin, entry.Target);
            Pool.Play(M3EffectKind.ElectroCharged, entry.Target.EffectCenter, ElementType.Lightning, 0.65f);
        }

        private void OnElementChanged(PartyMember member)
        {
            // Element choice is not a character switch: keep the avatar, animation clock and current burst snapshot.
            previousElement = member.Actor.Element;
            MeshEffects.SetElement(previousElement);
        }

        private void OnMemberChanged(PartyMember member)
        {
            // Snapshot outgoing pose/color, then dissolve the shared incoming avatar into view.
            Ultimate.Cancel();
            MeshEffects.EmitAfterimage(previousElement);
            previousElement = member.Actor.Element;
            AvatarSwitchRequested?.Invoke(member);
            MeshEffects.SetElement(previousElement);
            MeshEffects.BeginAppearance();
            comboCount = 0; popupAge = 10f;
        }

        /// <summary>Presentation-only skill hook: no energy deduction, damage, or new attack rules.</summary>
        public bool PlayUltimatePresentation()
        {
            if (Ultimate == null || party == null) return false;
            if (motor != null && !motor.CanBeginAction(PlayerActionState.Burst)) return false;
            bool started = Ultimate.Play(party.ActiveMember.Actor.Element);
            if (started) { combat.CancelAttack(); motor.TryBeginAction(PlayerActionState.Burst); }
            return started;
        }

        private void OnUltimateStage(M3UltimateStage stage, ElementType element, Vector3 position)
        {
            switch (stage)
            {
                case M3UltimateStage.ElementBurst:
                    MeshEffects.SetOutline(true, element);
                    Pool.Play(M3EffectKind.Burst, position + Vector3.up, element, 1.5f);
                    MeshEffects.EmitRing(position, element);
                    Flash(M3Palette.Secondary(element), 0.32f, 1.1f);
                    break;
                case M3UltimateStage.SoundImpact:
                    PlaySound(position, element, true);
                    break;
                case M3UltimateStage.RestoreAndResidue:
                    MeshEffects.SetOutline(false, element);
                    Pool.Play(M3EffectKind.Residue, position + Vector3.up * 0.08f, element);
                    break;
                case M3UltimateStage.Cancelled:
                    motor.EndAction(PlayerActionState.Burst);
                    ClearTransient();
                    break;
                case M3UltimateStage.Finished:
                    motor.EndAction(PlayerActionState.Burst);
                    MeshEffects.SetOutline(false, element);
                    break;
            }
        }

        private void Flash(Color color, float duration, float strength)
        {
            flashAge = 0f; flashDuration = duration; flashStrength = strength; flashColor = color;
        }

        private void Update()
        {
            popupAge += Time.unscaledDeltaTime;
            if (motor != null && motor.State == PlayerActionState.Burst && Ultimate != null &&
                Ultimate.Director != null && Ultimate.Director.state == UnityEngine.Playables.PlayState.Playing &&
                Ultimate.Director.duration > 0 && !double.IsInfinity(Ultimate.Director.duration))
                motor.SetActionNormalizedTime(PlayerActionState.Burst, (float)(Ultimate.Director.time / Ultimate.Director.duration));
            if (flashVolume == null) return;
            flashAge += Time.unscaledDeltaTime;
            float envelope = flashDuration > 0f ? Mathf.Clamp01(1f - flashAge / flashDuration) : 0f;
            flashVolume.weight = envelope;
            flashAdjustments.postExposure.Override(flashStrength);
            flashAdjustments.colorFilter.Override(Color.Lerp(Color.white, flashColor, 0.18f));
        }

        private void PlaySound(Vector3 position, ElementType element, bool burst)
        {
            if (voices == null) return;
            AudioSource voice = voices[nextVoice++ % voices.Length];
            voice.Stop(); voice.transform.position = position;
            // Original synthesized impact, modest elemental pitch variation; no third-party/voice sample.
            voice.pitch = burst ? 0.72f : 0.95f + (int)element * 0.045f;
            voice.volume = burst ? 0.65f : 0.22f;
            voice.Play(); AudioPlayCount++;
        }

        private void OnGUI()
        {
            if (popupAge >= 1.2f || comboCount <= 0) return;
            if (popupStyle == null) popupStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            // Unspecified popup defaults: .12 s punch scale, 1.2 s hold/fade, 20 px upward float.
            float punch = 1f + 0.4f * Mathf.Clamp01(1f - popupAge / 0.12f);
            popupStyle.fontSize = Mathf.RoundToInt(28f * punch);
            Color previous = GUI.color;
            Color color = M3Palette.Secondary(party.ActiveMember.Actor.Element);
            color.a = Mathf.Clamp01((1.2f - popupAge) / 0.35f); GUI.color = color;
            GUI.Label(new Rect(Screen.width * 0.5f - 120f, Screen.height * 0.32f - popupAge * 20f, 240f, 65f),
                comboCount + " HIT", popupStyle);
            GUI.color = previous;
        }

        public void ClearTransient()
        {
            Pool?.Clear(); MeshEffects?.Clear(); Feedback?.CancelAll();
            if (flashVolume != null) flashVolume.weight = 0f;
            flashDuration = 0f; popupAge = 10f; comboCount = 0;
            if (voices != null) foreach (AudioSource voice in voices) if (voice != null) voice.Stop();
        }

        private void OnEnable()
        {
            if (Pool != null) Pool.enabled = true;
            if (MeshEffects != null) MeshEffects.enabled = true;
            if (Feedback != null) Feedback.enabled = true;
            if (Ultimate != null) Ultimate.enabled = true;
            if (cameraData != null) cameraData.renderPostProcessing = true;
            if (party != null && party.ActiveMember != null)
            {
                previousElement = party.ActiveMember.Actor.Element;
                MeshEffects?.SetElement(previousElement);
            }
            Subscribe();
        }
        private void OnDisable()
        {
            Ultimate?.Cancel();
            ClearTransient(); Unsubscribe();
            if (Ultimate != null) Ultimate.enabled = false;
            if (Feedback != null) Feedback.enabled = false;
            if (MeshEffects != null) MeshEffects.enabled = false;
            if (Pool != null) Pool.enabled = false;
            if (cameraData != null) cameraData.renderPostProcessing = previousPostProcessing;
        }
        private void OnDestroy()
        {
            Unsubscribe();
            if (flashProfile != null)
            {
                foreach (VolumeComponent component in flashProfile.components)
                    if (component != null) Destroy(component);
                Destroy(flashProfile);
            }
        }
    }
}


