using System;
using Orbis.M0;
using Orbis.M1;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

namespace Orbis.M3
{
    public enum M3UltimateStage { None, CloseUp, SlowMotion, ElementBurst, SoundImpact, RestoreAndResidue, Finished, Cancelled }

    /// <summary>Presentation-only ultimate entry point driven by an authored, unscaled Timeline SignalTrack.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)] // Update the close-up pose before the Cinemachine Brain late update.
    public sealed class M3UltimateDirector : MonoBehaviour
    {
        public const float ResidueDuration = 4f;
        public const string TimelineResource = "M3/Timeline/Ultimate";
        private M0Input input;
        private Transform actor;
        private M3Feedback feedback;
        private TimelineAsset timeline;
        private PlayableDirector playableDirector;
        private SignalReceiver receiver;
        private CinemachineCamera closeUpCamera;
        private CinemachineBrain brain;
        private UniversalAdditionalCameraData outputData;
        private Volume colorVolume;
        private VolumeProfile colorProfile;
        private ColorAdjustments colorAdjustments;
        private ElementType currentElement;
        private Vector3 playOrigin;
        private bool configured;
        private bool presentationAcquired;
        private bool environmentAcquired;
        private bool previousInputLock;
        private bool previousCameraActive;
        private PrioritySettings previousCameraPriority;
        private CinemachineBlendDefinition previousBlend;
        private bool previousBrainUnscaled;
        private bool previousPostProcessing;
        private float previousVolumeWeight;
        private float previousSaturation;
        private bool previousSaturationOverride;

        public bool IsPlaying { get; private set; }
        public M3UltimateStage CurrentStage { get; private set; }
        public PlayableDirector Director => playableDirector;
        public CinemachineCamera CloseUpCamera => closeUpCamera;
        public Volume ColorVolume => colorVolume;
        public event Action<M3UltimateStage, ElementType, Vector3> StageChanged;

        public void Configure(M0Input playerInput, Transform presentedActor, CinemachineCamera gameplayCamera,
            M3Feedback hitFeedback, TimelineAsset sequence = null)
        {
            Cancel();
            if (playerInput == null || presentedActor == null || gameplayCamera == null || hitFeedback == null)
                throw new ArgumentException("The ultimate presentation requires input, actor, gameplay camera, and feedback.");
            input = playerInput;
            actor = presentedActor;
            feedback = hitFeedback;
            timeline = sequence != null ? sequence : Resources.Load<TimelineAsset>(TimelineResource);
            if (playableDirector == null)
            {
                playableDirector = gameObject.AddComponent<PlayableDirector>();
                playableDirector.playOnAwake = false;
                playableDirector.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
                playableDirector.extrapolationMode = DirectorWrapMode.None;
                playableDirector.stopped += OnTimelineStopped;
                receiver = gameObject.AddComponent<SignalReceiver>();
            }
            playableDirector.playableAsset = timeline;
            for (int i = receiver.Count() - 1; i >= 0; i--) receiver.RemoveAtIndex(i);
            if (timeline != null)
            {
                foreach (TrackAsset track in timeline.GetOutputTracks())
                {
                    if (!(track is SignalTrack)) continue;
                    playableDirector.SetGenericBinding(track, receiver);
                    foreach (IMarker marker in track.GetMarkers())
                    {
                        if (!(marker is SignalEmitter emitter) || emitter.asset == null ||
                            !Enum.TryParse(emitter.asset.name, out M3UltimateStage stage) ||
                            stage < M3UltimateStage.CloseUp || stage > M3UltimateStage.RestoreAndResidue ||
                            receiver.GetReaction(emitter.asset) != null) continue;
                        M3UltimateStage capturedStage = stage;
                        var reaction = new UnityEvent();
                        reaction.AddListener(() => ReceiveStage(capturedStage));
                        receiver.AddReaction(emitter.asset, reaction);
                    }
                }
            }
            EnsurePresentationObjects();
            feedback.AttachListener(closeUpCamera);
            UnityEngine.Camera output = UnityEngine.Camera.main;
            brain = output != null ? output.GetComponent<CinemachineBrain>() : null;
            outputData = output != null ? output.GetUniversalAdditionalCameraData() : null;
            configured = true;
        }

        /// <summary>Future skill systems may call this without introducing energy, cooldown, or damage here.</summary>
        public bool Play(ElementType element)
        {
            if (!configured || !isActiveAndEnabled || IsPlaying || timeline == null || receiver.Count() < 5 ||
                actor == null || input == null || feedback == null || !feedback.isActiveAndEnabled || element == ElementType.None)
                return false;
            currentElement = element;
            playOrigin = actor.position;
            CurrentStage = M3UltimateStage.None;
            IsPlaying = true;
            AcquirePresentation();
            playableDirector.time = 0d;
            playableDirector.Play();
            return true;
        }

        public void Cancel()
        {
            bool wasPlaying = IsPlaying;
            IsPlaying = false;
            // Stop also disposes a paused graph and resets emit-once signals for a later replay.
            if (playableDirector != null) playableDirector.Stop();
            RestoreEnvironment();
            RestoreInput();
            if (feedback != null && wasPlaying) feedback.CancelAll();
            if (wasPlaying) Emit(M3UltimateStage.Cancelled);
        }

        private void ReceiveStage(M3UltimateStage stage)
        {
            if (!IsPlaying || stage <= CurrentStage) return;
            switch (stage)
            {
                case M3UltimateStage.CloseUp:
                    // Camera was acquired before Timeline evaluation to avoid a one-frame view of an unlocked actor.
                    break;
                case M3UltimateStage.SlowMotion:
                    // 기획서 범위 내 기본값: 0.4초 동안 20% 속도, 채도 +35.
                    feedback.SetSlowMotion(0.2f);
                    colorAdjustments.saturation.Override(35f);
                    colorVolume.weight = 1f;
                    break;
                case M3UltimateStage.ElementBurst:
                    feedback.ClearSlowMotion();
                    feedback.PlayHit(M3HitStrength.Burst, ActorPosition());
                    break;
                case M3UltimateStage.RestoreAndResidue:
                    RestoreEnvironment();
                    // Scene presentation owns a pooled residue lasting ResidueDuration (4 seconds).
                    break;
            }
            Emit(stage);
        }

        private void Emit(M3UltimateStage stage)
        {
            CurrentStage = stage;
            StageChanged?.Invoke(stage, currentElement, ActorPosition());
        }

        private Vector3 ActorPosition() => actor != null ? actor.position : playOrigin;

        private void AcquirePresentation()
        {
            previousInputLock = input.PresentationLocked;
            input.SetPresentationLocked(true);
            presentationAcquired = true;
            previousCameraPriority = closeUpCamera.Priority;
            previousCameraActive = closeUpCamera.gameObject.activeSelf;
            if (brain != null)
            {
                previousBlend = brain.DefaultBlend;
                previousBrainUnscaled = brain.IgnoreTimeScale;
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
                brain.IgnoreTimeScale = true;
            }
            if (outputData != null)
            {
                previousPostProcessing = outputData.renderPostProcessing;
                outputData.renderPostProcessing = true;
            }
            previousVolumeWeight = colorVolume.weight;
            previousSaturation = colorAdjustments.saturation.value;
            previousSaturationOverride = colorAdjustments.saturation.overrideState;
            environmentAcquired = true;
            PositionCloseUpCamera();
            closeUpCamera.Priority = 1000;
            closeUpCamera.gameObject.SetActive(true);
        }

        private void RestoreEnvironment()
        {
            if (!environmentAcquired) return;
            environmentAcquired = false;
            if (feedback != null) feedback.CancelAll();
            if (closeUpCamera != null)
            {
                closeUpCamera.Priority = previousCameraPriority;
                closeUpCamera.gameObject.SetActive(previousCameraActive);
            }
            if (brain != null)
            {
                brain.DefaultBlend = previousBlend;
                brain.IgnoreTimeScale = previousBrainUnscaled;
                brain.ResetState(); // Return to the gameplay camera immediately, preserving its normal future blend settings.
            }
            if (outputData != null) outputData.renderPostProcessing = previousPostProcessing;
            if (colorVolume != null) colorVolume.weight = previousVolumeWeight;
            if (colorAdjustments != null)
            {
                colorAdjustments.saturation.value = previousSaturation;
                colorAdjustments.saturation.overrideState = previousSaturationOverride;
            }
        }

        private void RestoreInput()
        {
            if (!presentationAcquired) return;
            presentationAcquired = false;
            if (input != null) input.SetPresentationLocked(previousInputLock);
        }

        private void OnTimelineStopped(PlayableDirector stopped)
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            RestoreEnvironment();
            RestoreInput();
            Emit(M3UltimateStage.Finished);
        }

        private void EnsurePresentationObjects()
        {
            if (closeUpCamera == null)
            {
                var cameraObject = new GameObject("M3 Ultimate Close-up Camera");
                cameraObject.transform.SetParent(transform, false);
                closeUpCamera = cameraObject.AddComponent<CinemachineCamera>();
                closeUpCamera.Priority = -100;
                closeUpCamera.Lens.FieldOfView = 45f; // 미정 기본값: 얼굴/상체를 보여주는 45도 시야각.
                cameraObject.SetActive(false);
            }
            if (colorVolume == null)
            {
                var volumeObject = new GameObject("M3 Ultimate Saturation");
                volumeObject.transform.SetParent(transform, false);
                colorVolume = volumeObject.AddComponent<Volume>();
                colorVolume.isGlobal = true;
                colorVolume.priority = 1000f;
                colorVolume.weight = 0f;
                colorProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                colorProfile.name = "M3 Runtime Ultimate Color";
                colorProfile.hideFlags = HideFlags.DontSave;
                colorAdjustments = colorProfile.Add<ColorAdjustments>(false);
                colorAdjustments.saturation.Override(0f);
                colorVolume.sharedProfile = colorProfile;
            }
        }

        private void PositionCloseUpCamera()
        {
            if (actor == null || closeUpCamera == null) return;
            Vector3 target = actor.position + Vector3.up * 1.25f;
            Vector3 position = actor.position + actor.forward * 1.8f + actor.right * 0.65f + Vector3.up * 1.5f;
            closeUpCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
        }

        private void Update()
        {
            if (IsPlaying && (input == null || actor == null || feedback == null ||
                !feedback.isActiveAndEnabled || !input.HasGameplayFocus)) Cancel();
        }

        private void LateUpdate()
        {
            if (IsPlaying && environmentAcquired) PositionCloseUpCamera();
        }

        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            Cancel();
            if (playableDirector != null) playableDirector.stopped -= OnTimelineStopped;
            if (colorProfile != null)
            {
                foreach (VolumeComponent component in colorProfile.components)
                    if (component != null) Destroy(component);
                Destroy(colorProfile);
            }
        }
    }
}


