using System.Collections;
using NUnit.Framework;
using Orbis.M0;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Orbis.M1.Tests
{
    /// <summary>Loads the shipped M1 scenes and drives party/combat through real input events.</summary>
    public sealed class M1IntegrationTests
    {
        private const string PartyScene = "Assets/Orbis/M1/Scenes/M1_PartyPrototype.unity";
        private const string RockScene = "Assets/Orbis/M1/Scenes/M1_CrystallizePrototype.unity";
        private Keyboard keyboard;
        private Gamepad gamepad;
        private Scene loadedScene;
        private M1SceneBootstrap bootstrap;
        private PartyManager party;
        private ElementalReactionManager manager;
        private ElementalActor target;
        private M0Input input;
        private PlayerMotor motor;
        private BasicAttackCombo combat;
        private M0CameraRig rig;
        private Animator animator;
        private float previousCaptureDeltaTime;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool previousRunInBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDeltaTime = Time.captureDeltaTime;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousRunInBackground = Application.runInBackground;
            previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            // Keep the settings instance: InputManager destroys a temporary instance when replaced.
            // Batch mode has no focused Game view, so route our synthetic keyboard events to gameplay.
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true;
            Time.captureDeltaTime = 1f / 60f;
            keyboard = InputSystem.AddDevice<Keyboard>();
            gamepad = InputSystem.AddDevice<Gamepad>();
            SetKeys();
            SetPad();
            yield return LoadPrototype(PartyScene);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
            // A replacement empty scene permits unloading the last loaded prototype scene.
            Scene emptyScene = SceneManager.CreateScene("M1 Test Cleanup");
            SceneManager.SetActiveScene(emptyScene);
            if (loadedScene.IsValid() && loadedScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(loadedScene);
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
            Application.runInBackground = previousRunInBackground;
            Time.captureDeltaTime = previousCaptureDeltaTime;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        [UnityTest]
        public IEnumerator NumberKeysSwitchAllFourMembersWithoutReplacingThePawnOrCamera()
        {
            Assert.That(party.Members.Count, Is.EqualTo(4));
            Assert.That(party.ActiveIndex, Is.Zero);
            Assert.That(party.Members[0].Actor.Element, Is.EqualTo(ElementType.Fire));
            Assert.That(party.Members[1].Actor.Element, Is.EqualTo(ElementType.Water));
            Assert.That(party.Members[2].Actor.Element, Is.EqualTo(ElementType.Lightning));
            Assert.That(party.Members[3].Actor.Element, Is.EqualTo(ElementType.Wind));
            SetPad(new Vector2(1f, 0f));
            SetKeys(Key.D);
            yield return Frames(12);
            SetPad();
            SetKeys();
            yield return Frames(2);
            Vector3 position = motor.transform.position;
            Quaternion facing = motor.transform.rotation;
            Quaternion cameraRotation = rig.Pivot.rotation;
            Transform cameraFollow = FindInScene<CinemachineCamera>().Follow;
            PlayerMotor originalMotor = motor;
            M0CameraRig originalRig = rig;

            Key[] keys = { Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit1 };
            int[] slots = { 1, 2, 3, 0 };
            for (int i = 0; i < keys.Length; i++)
            {
                SetKeys(keys[i]);
                yield return Frames(1);
                Assert.That(party.ActiveIndex, Is.EqualTo(slots[i]));
                Assert.That(party.SwitchLockRemaining, Is.Zero);
                AssertOnlyOneMemberOnField();
                Assert.That(FindInScene<PlayerMotor>(), Is.SameAs(originalMotor));
                Assert.That(FindInScene<M0CameraRig>(), Is.SameAs(originalRig));
                Assert.That(FindInScene<CinemachineCamera>().Follow, Is.SameAs(cameraFollow));
                Assert.That(Vector3.Distance(position, motor.transform.position), Is.LessThan(0.02f));
                Assert.That(Quaternion.Angle(facing, motor.transform.rotation), Is.LessThan(0.01f));
                Assert.That(Quaternion.Angle(cameraRotation, rig.Pivot.rotation), Is.LessThan(0.01f));
                SetKeys();
                yield return Frames(1);
            }

            manager.Apply(target, ElementType.Fire, party.ActiveMember.Actor, 0f, 0.3f);
            Assert.That(party.TrySwitch(party.ActiveIndex), Is.False, "The current slot must be a no-op.");
            Assert.That(target.AuraRemainingTime, Is.LessThan(0.31f),
                "Selecting the same member must not extend its attachments as though it left the field.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                CaptureSceneForVisualReview(FindInScene<CinemachineBrain>().GetComponent<UnityEngine.Camera>());
        }

        [UnityTest]
        public IEnumerator ReleasedInputIgnoresPartyKeysAndRecaptureDoesNotAttack()
        {
            SetPad(Vector2.zero, GamepadButton.Start);
            yield return Frames(1);
            SetPad();
            SetKeys(Key.Digit2);
            yield return Frames(3);
            Assert.That(input.GameplayEnabled, Is.False);
            Assert.That(party.ActiveIndex, Is.Zero);
            SetKeys();
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(4);
            Assert.That(input.GameplayEnabled, Is.True);
            Assert.That(combat.IsAttacking, Is.False);
            SetPad();
            SetKeys(Key.Digit2);
            yield return Frames(1);
            Assert.That(party.ActiveIndex, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SwitchingCancelsTheOldComboAndTheNextHitUsesTheNewMemberElement()
        {
            Vector3 position = motor.transform.position;
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(1);
            SetPad();
            yield return Frames(19);
            Assert.That(combat.CurrentStep, Is.EqualTo(1));
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.Fire));
            Assert.That(target.AuraSourceId, Is.EqualTo(party.Members[0].Actor.SourceId));
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(1);
            Assert.That(combat.HasBufferedAttack, Is.True);
            SetPad();
            SetKeys(Key.Digit2);
            yield return Frames(1);
            Assert.That(party.ActiveIndex, Is.EqualTo(1));
            Assert.That(combat.IsAttacking, Is.False);
            Assert.That(combat.HasBufferedAttack, Is.False);
            Assert.That(animator.speed, Is.EqualTo(1f));
            SetKeys();
            yield return Frames(1);
            float damageBefore = target.DamageTaken;
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(1);
            Assert.That(combat.CurrentStep, Is.EqualTo(1));
            SetPad();
            // Keep a backwards movement/jump input during the new attack to preserve M0 restrictions.
            SetKeys(Key.S, Key.Space);
            yield return Frames(12);
            Assert.That(combat.IsAttacking, Is.True);
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(Vector3.Distance(position, motor.transform.position), Is.LessThan(0.03f));
            Assert.That(manager.LastReaction, Is.EqualTo(ReactionType.Vaporize));
            Assert.That(target.DamageTaken - damageBefore, Is.GreaterThan(10f));
        }

        [UnityTest]
        public IEnumerator OutgoingAuraIsRetainedForFollowUpButAnExpiredAuraCannotReact()
        {
            ElementalActor fireMember = party.Members[0].Actor;
            manager.Apply(target, ElementType.Fire, fireMember, 0f, 0.3f);
            yield return Frames(6);
            SetKeys(Key.Digit2);
            yield return Frames(1);
            SetKeys();
            Assert.That(target.AuraRemainingTime, Is.InRange(3.9f, 4.01f));
            Assert.That(target.AuraSourceId, Is.EqualTo(fireMember.SourceId));
            yield return Frames(30);
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.Fire),
                "The aura's original 0.3 s duration has elapsed, but switching must retain it.");
            yield return AttackOnceToHit();
            Assert.That(manager.LastReaction, Is.EqualTo(ReactionType.Vaporize));

            combat.CancelAttack();
            manager.ResetState();
            Assert.That(party.TrySwitch(0), Is.True);
            manager.Apply(target, ElementType.Fire, fireMember, 0f, 0.1f);
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.Fire));
            yield return Frames(12);
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.None));
            Assert.That(party.TrySwitch(1), Is.True);
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.None),
                "Switching must not resurrect an attachment which already expired.");
            yield return AttackOnceToHit();
            Assert.That(manager.LastReaction, Is.EqualTo(ReactionType.None));
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.Water));
            Assert.That(target.DamageTaken, Is.EqualTo(10f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator OffFieldMembersKeepTheirAuraAndShieldWhileTimersContinue()
        {
            ElementalActor outgoing = party.Members[0].Actor;
            manager.Apply(outgoing, ElementType.Water, target, 0f, 0.6f);
            outgoing.GrantShield(ElementType.Fire, 25f, 1.25f);
            SetKeys(Key.Digit2);
            yield return Frames(1);
            SetKeys();
            yield return Frames(18);
            Assert.That(outgoing.IsOnField, Is.False);
            Assert.That(outgoing.gameObject.activeInHierarchy, Is.True);
            Assert.That(outgoing.AuraElement, Is.EqualTo(ElementType.Water));
            Assert.That(outgoing.AuraRemainingTime, Is.InRange(0.2f, 0.4f));
            Assert.That(outgoing.ShieldAmount, Is.EqualTo(25f));
            Assert.That(outgoing.ShieldElement, Is.EqualTo(ElementType.Fire));
            Assert.That(outgoing.ShieldRemainingTime, Is.InRange(0.8f, 1.05f));
            yield return Frames(25);
            Assert.That(outgoing.AuraElement, Is.EqualTo(ElementType.None));
            Assert.That(outgoing.ShieldAmount, Is.EqualTo(25f));
            SetKeys(Key.Digit1);
            yield return Frames(1);
            SetKeys();
            Assert.That(party.ActiveMember.Actor, Is.SameAs(outgoing));
            Assert.That(outgoing.ShieldAmount, Is.EqualTo(25f));
            yield return Frames(40);
            Assert.That(outgoing.ShieldAmount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator BurstHookBlocksSwitchingForOneSecondWithoutAddingNormalCooldown()
        {
            party.NotifyBurstUsed();
            Assert.That(party.SwitchLockRemaining, Is.GreaterThan(0.95f));
            Assert.That(party.TrySwitch(1), Is.False);
            SetKeys(Key.Digit2);
            yield return Frames(1);
            SetKeys();
            Assert.That(party.ActiveIndex, Is.Zero);
            yield return Frames(63);
            Assert.That(party.SwitchLockRemaining, Is.Zero);
            SetKeys(Key.Digit2);
            yield return Frames(1);
            Assert.That(party.ActiveIndex, Is.EqualTo(1));
            Assert.That(party.TrySwitch(2), Is.True);
            Assert.That(party.TrySwitch(3), Is.True);
            Assert.That(party.SwitchLockRemaining, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SwitchingInTheAirPreservesTheSameJumpAndLandsNormally()
        {
            float floor = motor.transform.position.y;
            PlayerMotor originalMotor = motor;
            SetKeys(Key.Space);
            yield return Frames(1);
            SetKeys();
            yield return Frames(9);
            Assert.That(motor.IsGrounded, Is.False);
            float beforeSwitch = motor.transform.position.y;
            SetKeys(Key.Digit2);
            yield return Frames(1);
            SetKeys();
            Assert.That(party.ActiveIndex, Is.EqualTo(1));
            Assert.That(FindInScene<PlayerMotor>(), Is.SameAs(originalMotor));
            Assert.That(motor.transform.position.y, Is.InRange(beforeSwitch, beforeSwitch + 0.12f));
            float peak = motor.transform.position.y;
            for (int frame = 0; frame < 48; frame++)
            {
                yield return null;
                peak = Mathf.Max(peak, motor.transform.position.y);
            }
            Assert.That(peak - floor, Is.InRange(1.1f, 1.5f));
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(motor.transform.position.y, Is.EqualTo(floor).Within(0.06f));
        }

        [UnityTest]
        public IEnumerator CrystallizeSceneLoadsRockInSlotFourAndItsAttackGrantsAShield()
        {
            yield return LoadPrototype(RockScene);
            Assert.That(party.Members[3].Actor.Element, Is.EqualTo(ElementType.Rock));
            manager.Apply(target, ElementType.Fire, party.Members[0].Actor, 0f, 4f);
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.Fire));
            SetKeys(Key.Digit4);
            yield return Frames(1);
            SetKeys();
            Assert.That(party.ActiveIndex, Is.EqualTo(3));
            AssertOnlyOneMemberOnField();
            yield return AttackOnceToHit();
            Assert.That(manager.LastReaction, Is.EqualTo(ReactionType.Crystallize));
            Assert.That(party.ActiveMember.Actor.ShieldElement, Is.EqualTo(ElementType.Fire));
            Assert.That(party.ActiveMember.Actor.ShieldAmount, Is.GreaterThan(0f));
        }

        private IEnumerator LoadPrototype(string path)
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(path), Is.True,
                "Run Orbis > M1 > Setup and Validate to add the prototype scenes to Build Settings.");
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
            loadedScene = SceneManager.GetSceneByPath(path);
            Assert.That(loadedScene.IsValid() && loadedScene.isLoaded, Is.True);
            SceneManager.SetActiveScene(loadedScene);
            bootstrap = FindInScene<M1SceneBootstrap>();
            yield return Frames(12);
            party = bootstrap.Party;
            manager = bootstrap.Manager;
            target = bootstrap.PrimaryTarget;
            input = FindInScene<M0Input>();
            motor = FindInScene<PlayerMotor>();
            combat = FindInScene<BasicAttackCombo>();
            rig = FindInScene<M0CameraRig>();
            animator = motor.GetComponentInChildren<Animator>();
            Assert.That(input.GameplayEnabled, Is.True);
            Assert.That(motor.IsGrounded, Is.True);
        }

        private IEnumerator AttackOnceToHit()
        {
            SetPad();
            yield return Frames(1);
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(1);
            SetPad();
            yield return Frames(12);
        }

        private void AssertOnlyOneMemberOnField()
        {
            int activeCount = 0;
            foreach (PartyMember member in party.Members)
            {
                Assert.That(member.Actor.gameObject.activeInHierarchy, Is.True);
                if (member.Actor.IsOnField) activeCount++;
            }
            Assert.That(activeCount, Is.EqualTo(1));
            Assert.That(party.ActiveMember.Actor.IsOnField, Is.True);
        }

        private T FindInScene<T>() where T : Component
        {
            foreach (GameObject root in loadedScene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>();
                if (component != null) return component;
            }
            Assert.Fail(typeof(T).Name + " was not found in " + loadedScene.path);
            return null;
        }

        private static void CaptureSceneForVisualReview(UnityEngine.Camera camera)
        {
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;
#if UNITY_EDITOR
            // Batch rendering can submit before asynchronous shader variants are ready.
            bool previousAsyncCompilation = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
#endif
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            Texture2D pixels = null;
            try
            {
                target.Create();
                camera.aspect = 1280f / 720f;
                RenderPipeline.SubmitRenderRequest(camera,
                    new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
                pixels.Apply();
                string outputDirectory = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "..", "TestResults"));
                System.IO.Directory.CreateDirectory(outputDirectory);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(outputDirectory, "M1_Prototype.png"),
                    pixels.EncodeToPNG());
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Optional M1 scene capture was unavailable: " + exception.Message);
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                camera.aspect = previousAspect;
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
#endif
                if (pixels != null) Object.Destroy(pixels);
                target.Release();
                Object.Destroy(target);
            }
        }
        private void SetKeys(params Key[] keys) =>
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));

        private void SetPad(Vector2 look = default, params GamepadButton[] buttons)
        {
            var state = new GamepadState { rightStick = look };
            foreach (GamepadButton button in buttons) state = state.WithButton(button);
            InputSystem.QueueStateEvent(gamepad, state);
        }

        private static IEnumerator Frames(int count)
        {
            for (int frame = 0; frame < count; frame++) yield return null;
        }
    }
}