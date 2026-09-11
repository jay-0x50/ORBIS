using System.Collections;
using NUnit.Framework;
using Orbis.M0;
using Orbis.M1;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Orbis.M2.Tests
{
    /// <summary>Loads the real M2 region and verifies traversal and content through Input System events.</summary>
    public sealed class M2IntegrationTests
    {
        private const string ScenePath = "Assets/Orbis/M2/Scenes/M2_ExplorationPrototype.unity";
        private Scene loadedScene;
        private Keyboard keyboard;
        private Gamepad gamepad;
        private M2SceneBootstrap bootstrap;
        private ExplorationMotor traversal;
        private PlayerMotor motor;
        private M0Input input;
        private BasicAttackCombo combat;
        private PartyManager party;
        private float previousCaptureDeltaTime;
        private bool previousRunInBackground;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDeltaTime = Time.captureDeltaTime;
            previousRunInBackground = Application.runInBackground;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            // Preserve the settings instance. Replacing a temporary InputSettings destroys the original.
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true;
            Time.captureDeltaTime = 1f / 60f;
            keyboard = InputSystem.AddDevice<Keyboard>();
            gamepad = InputSystem.AddDevice<Gamepad>();
            SetKeys();
            SetPad();
            yield return LoadRegion();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
            Scene empty = SceneManager.CreateScene("M2 Test Cleanup");
            SceneManager.SetActiveScene(empty);
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
        public IEnumerator RunningHonorsGraceThenExhaustionWalksAndGroundRestRecovers()
        {
            yield return TravelTo(new Vector3(-3f, 0.1f, -4f));
            traversal.Stamina.Restore();
            // Orbit while running on the flat southern ground; this keeps a continuous sprint in bounds.
            SetPad(new Vector2(1f, 0f));
            SetKeys(Key.W, Key.LeftShift);
            yield return Frames(180);
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(motor.HorizontalSpeed, Is.EqualTo(6f).Within(0.01f));
            Assert.That(traversal.Stamina.Current, Is.EqualTo(100f).Within(0.06f));
            yield return Frames(30);
            Assert.That(traversal.Stamina.Current, Is.EqualTo(99f).Within(0.07f));
            traversal.Stamina.SetCurrent(0.1f);
            yield return Frames(4);
            Assert.That(traversal.Stamina.Current, Is.Zero);
            Assert.That(motor.HorizontalSpeed, Is.EqualTo(2.5f).Within(0.01f));
            SetKeys();
            SetPad();
            yield return Frames(44);
            Assert.That(traversal.Stamina.Current, Is.Zero);
            yield return Frames(30);
            Assert.That(traversal.Stamina.Current, Is.InRange(1f, 6f));
        }

        [UnityTest]
        public IEnumerator ClimbingMantlesOntoTheCliffAndPartySwitchKeepsTheSharedPool()
        {
            yield return TravelTo(bootstrap.CliffBasePosition);
            SetKeys(Key.E);
            yield return Frames(1);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Climbing));
            StaminaPool originalPool = traversal.Stamina;
            PlayerMotor originalMotor = motor;
            Transform originalFollow = FindInScene<CinemachineCamera>().Follow;
            SetKeys(Key.W);
            yield return Frames(20);
            Assert.That(motor.transform.position.y, Is.GreaterThan(0.5f));
            Assert.That(originalPool.Current, Is.LessThan(98f));
            SetKeys(Key.W, Key.Digit2);
            yield return Frames(1);
            Assert.That(party.ActiveIndex, Is.EqualTo(1));
            Assert.That(traversal.Stamina, Is.SameAs(originalPool));
            Assert.That(FindInScene<PlayerMotor>(), Is.SameAs(originalMotor));
            Assert.That(FindInScene<CinemachineCamera>().Follow, Is.SameAs(originalFollow));
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Climbing));
            SetKeys(Key.W);
            for (int frame = 0; traversal.Mode == ExplorationMode.Climbing && frame < 260; frame++)
                yield return null;
            SetKeys();
            yield return Frames(2);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Locomotion));
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(motor.transform.position.y, Is.GreaterThan(7.9f));
            Assert.That(motor.transform.position.z, Is.GreaterThan(12.2f));
            Assert.That(traversal.LastSafePosition.y, Is.GreaterThan(7.9f));
            Assert.That(traversal.AccumulatedFallDamage, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ExhaustedClimbingFallsAndRecordsLandingDamage()
        {
            yield return TravelTo(bootstrap.CliffBasePosition + Vector3.up * 5f);
            SetKeys(Key.E);
            yield return Frames(1);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Climbing));
            SetKeys();
            traversal.Stamina.SetCurrent(0.1f);
            yield return Frames(2);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Locomotion));
            Assert.That(motor.IsGrounded, Is.False);
            Assert.That(traversal.Stamina.Current, Is.Zero);
            for (int frame = 0; !motor.IsGrounded && frame < 100; frame++) yield return null;
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(traversal.AccumulatedFallDamage, Is.GreaterThanOrEqualTo(5f));
            Assert.That(party.ActiveMember.Actor.DamageTaken, Is.GreaterThanOrEqualTo(5f));
        }

        [UnityTest]
        public IEnumerator GlidingTogglesWithSpaceAndExhaustionReturnsToGravity()
        {
            traversal.Teleport(new Vector3(5f, 8f, 5f));
            motor.transform.rotation = Quaternion.identity;
            yield return Frames(2);
            SetKeys(Key.Space);
            yield return Frames(1);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Gliding));
            float startHeight = motor.transform.position.y;
            float startStamina = traversal.Stamina.Current;
            SetKeys(Key.W);
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(20);
            Assert.That(combat.IsAttacking, Is.False);
            Assert.That(startHeight - motor.transform.position.y, Is.InRange(0.65f, 0.82f));
            Assert.That(startStamina - traversal.Stamina.Current, Is.InRange(1.5f, 1.85f));
            SetPad();
            SetKeys(Key.Space);
            yield return Frames(1);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Locomotion));
            SetKeys();
            yield return Frames(1);
            SetKeys(Key.Space);
            yield return Frames(1);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Gliding));
            SetKeys();
            traversal.Stamina.SetCurrent(0.05f);
            yield return Frames(2);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Locomotion));
            Assert.That(traversal.Stamina.Current, Is.Zero);
            float fallingFrom = motor.transform.position.y;
            yield return Frames(12);
            Assert.That(fallingFrom - motor.transform.position.y, Is.GreaterThan(0.7f));
        }

        [UnityTest]
        public IEnumerator SwimmingSupportsDiveAscendAndWalkingOutOnTheShore()
        {
            yield return TravelTo(bootstrap.SwimPosition, 24);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Swimming));
            Assert.That(traversal.IsUnderwater, Is.False);
            SetKeys(Key.LeftCtrl);
            yield return Frames(45);
            Assert.That(traversal.IsUnderwater, Is.True);
            Assert.That(motor.transform.position.y, Is.LessThan(-1.7f));
            SetKeys(Key.Space);
            yield return Frames(45);
            Assert.That(traversal.IsUnderwater, Is.False);
            Assert.That(motor.transform.position.y, Is.GreaterThan(-1.15f));
            SetKeys(Key.D);
            for (int frame = 0; frame < 300; frame++)
            {
                yield return null;
                if (traversal.Mode == ExplorationMode.Locomotion && motor.IsGrounded) break;
            }
            SetKeys();
            yield return Frames(2);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Locomotion));
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(motor.transform.position.x, Is.GreaterThan(-10f));
            Assert.That(motor.transform.position.y, Is.GreaterThan(-0.7f));
            Assert.That(traversal.RescueCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SwimmingExhaustionReturnsToLastLandWithRescueDamageAndStamina()
        {
            Vector3 safe = traversal.LastSafePosition;
            yield return TravelTo(bootstrap.SwimPosition, 24);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Swimming));
            traversal.Stamina.SetCurrent(0.01f);
            yield return Frames(1);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Locomotion));
            Assert.That(traversal.RescueCount, Is.EqualTo(1));
            Assert.That(traversal.AccumulatedRescueDamage, Is.EqualTo(5f));
            Assert.That(party.ActiveMember.Actor.DamageTaken, Is.EqualTo(5f));
            Assert.That(traversal.Stamina.Current, Is.EqualTo(25f).Within(0.01f));
            Assert.That(Vector3.Distance(motor.transform.position, safe), Is.LessThan(0.1f));
            yield return Frames(4);
            Assert.That(traversal.RescueCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FieldPuzzleUsesRealFireHitsAndClaimsItsChestOnlyOnceAcrossReload()
        {
            int beforeReward = RewardInventory.Session.EnhancementMaterials;
            Assert.That(bootstrap.FieldPuzzle.Model.RewardClaimed, Is.False);
            yield return StrikeAt(new Vector3(14f, 0.1f, 6.5f));
            Assert.That(bootstrap.FieldPuzzle.Model.LitCount, Is.Zero, "Statue II cannot begin the sequence.");
            for (int index = 0; index < 3; index++)
            {
                yield return StrikeAt(new Vector3(11f + index * 3f, 0.1f, 6.5f));
                Assert.That(bootstrap.FieldPuzzle.Model.LitCount, Is.EqualTo(index + 1));
            }
            Assert.That(bootstrap.FieldPuzzle.Model.IsUnlocked, Is.True);
            yield return Frames(25);
            Assert.That(combat.IsAttacking, Is.False);
            yield return TapKey(Key.F);
            Assert.That(bootstrap.FieldPuzzle.Model.RewardClaimed, Is.False, "F outside the 2.5 m reach must do nothing.");
            yield return TravelTo(bootstrap.ChestPosition + new Vector3(0f, 0.1f, -1.3f));
            yield return TapKey(Key.F);
            Assert.That(bootstrap.FieldPuzzle.Model.RewardClaimed, Is.True);
            Assert.That(RewardInventory.Session.EnhancementMaterials, Is.EqualTo(beforeReward + 5));
            yield return TapKey(Key.F);
            yield return TapKey(Key.R);
            Assert.That(RewardInventory.Session.EnhancementMaterials, Is.EqualTo(beforeReward + 5));
            Assert.That(bootstrap.FieldPuzzle.Model.RewardClaimed, Is.True);
            yield return LoadRegion();
            Assert.That(bootstrap.FieldPuzzle.Model.RewardClaimed, Is.True);
            Assert.That(bootstrap.FieldPuzzle.Model.IsUnlocked, Is.True);
            Assert.That(bootstrap.FieldPuzzle.TryClaimReward(), Is.False);
            Assert.That(RewardInventory.Session.EnhancementMaterials, Is.EqualTo(beforeReward + 5));
        }

        [UnityTest]
        public IEnumerator ChallengeCanTimeOutRetryAndCompleteWithThreeActualVaporizeHits()
        {
            int beforeReward = RewardInventory.Session.EnhancementMaterials;
            yield return TravelTo(bootstrap.ChallengePosition);
            yield return TapKey(Key.F);
            Assert.That(bootstrap.Challenge.State, Is.EqualTo(ChallengeState.Running));
            bootstrap.Challenge.AutoTick = false;
            bootstrap.Challenge.Tick(60.1f);
            Assert.That(bootstrap.Challenge.State, Is.EqualTo(ChallengeState.Failed));
            Assert.That(FindActor("Challenge Target 1").IsOnField, Is.False);
            yield return TapKey(Key.F);
            bootstrap.Challenge.AutoTick = true;
            Assert.That(bootstrap.Challenge.State, Is.EqualTo(ChallengeState.Running));
            Assert.That(bootstrap.Challenge.DefeatedCount, Is.Zero);
            Vector3[] firingPositions =
            {
                new Vector3(11f, 0.1f, 24.5f), new Vector3(14f, 0.1f, 26.5f),
                new Vector3(17f, 0.1f, 24.5f)
            };
            for (int index = 0; index < firingPositions.Length; index++)
            {
                yield return TravelTo(firingPositions[index]);
                yield return TapKey(Key.Digit1);
                yield return AttackOnceToHit();
                Assert.That(FindActor("Challenge Target " + (index + 1)).AuraElement, Is.EqualTo(ElementType.Fire));
                yield return TapKey(Key.Digit2);
                yield return AttackOnceToHit();
                Assert.That(bootstrap.Challenge.DefeatedCount, Is.EqualTo(index + 1));
                Assert.That(FindActor("Challenge Target " + (index + 1)).IsOnField, Is.False);
            }
            Assert.That(bootstrap.Challenge.State, Is.EqualTo(ChallengeState.Completed));
            Assert.That(bootstrap.Challenge.RemainingTime, Is.GreaterThan(0f));
            Assert.That(RewardInventory.Session.EnhancementMaterials, Is.EqualTo(beforeReward));
            yield return TravelTo(bootstrap.ChallengePosition);
            yield return TapKey(Key.F);
            Assert.That(bootstrap.Challenge.RewardClaimed, Is.True);
            Assert.That(RewardInventory.Session.EnhancementMaterials, Is.EqualTo(beforeReward + 3));
            yield return TapKey(Key.F);
            yield return TapKey(Key.R);
            Assert.That(RewardInventory.Session.EnhancementMaterials, Is.EqualTo(beforeReward + 3));
            Assert.That(bootstrap.Challenge.RewardClaimed, Is.True);
        }

        [UnityTest]
        public IEnumerator ReleasedInputCannotGrabAndResetRestoresTheRegionWithoutDeletingRewards()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                CaptureSceneForVisualReview(FindInScene<CinemachineBrain>().GetComponent<UnityEngine.Camera>());
            yield return TravelTo(bootstrap.CliffBasePosition);
            SetPad(Vector2.zero, GamepadButton.Start);
            yield return Frames(1);
            SetPad();
            SetKeys(Key.E);
            yield return Frames(2);
            Assert.That(input.GameplayEnabled, Is.False);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Locomotion));
            SetKeys();
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(4);
            SetPad();
            Assert.That(input.GameplayEnabled, Is.True);
            Assert.That(combat.IsAttacking, Is.False);
            traversal.Stamina.SetCurrent(40f);
            party.TrySwitch(2);
            bool rewardWasClaimed = bootstrap.FieldPuzzle.Model.RewardClaimed;
            yield return TapKey(Key.R);
            Assert.That(traversal.Mode, Is.EqualTo(ExplorationMode.Locomotion));
            Assert.That(traversal.Stamina.Current, Is.EqualTo(100f));
            Assert.That(party.ActiveIndex, Is.Zero);
            Assert.That(Vector3.Distance(motor.transform.position, bootstrap.SpawnPosition), Is.LessThan(0.15f));
            Assert.That(bootstrap.Challenge.State, Is.EqualTo(ChallengeState.Ready));
            Assert.That(bootstrap.FieldPuzzle.Model.RewardClaimed, Is.EqualTo(rewardWasClaimed));
        }

        private IEnumerator LoadRegion()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(ScenePath), Is.True,
                "Run Orbis > M2 > Setup and Validate before the integration suite.");
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            loadedScene = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(loadedScene);
            bootstrap = FindInScene<M2SceneBootstrap>();
            yield return Frames(15);
            traversal = bootstrap.Traversal;
            party = bootstrap.Party;
            motor = FindInScene<PlayerMotor>();
            input = FindInScene<M0Input>();
            combat = FindInScene<BasicAttackCombo>();
            Assert.That(input.GameplayEnabled, Is.True);
            Assert.That(motor.IsGrounded, Is.True);
        }

        private IEnumerator TravelTo(Vector3 position, int frames = 15)
        {
            SetKeys();
            SetPad();
            traversal.Teleport(position);
            motor.transform.rotation = Quaternion.identity;
            yield return Frames(frames);
        }

        private IEnumerator StrikeAt(Vector3 position)
        {
            yield return TravelTo(position);
            yield return AttackOnceToHit();
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

        private IEnumerator TapKey(Key key)
        {
            SetKeys(key);
            // F and R are observed by the scene's LateUpdate after the first coroutine resume.
            yield return Frames(2);
            SetKeys();
            yield return Frames(1);
        }

        private ElementalActor FindActor(string actorName)
        {
            foreach (GameObject root in loadedScene.GetRootGameObjects())
                foreach (ElementalActor actor in root.GetComponentsInChildren<ElementalActor>(true))
                    if (actor.name == actorName) return actor;
            Assert.Fail("Missing actor: " + actorName);
            return null;
        }

        private T FindInScene<T>() where T : Component
        {
            foreach (GameObject root in loadedScene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>();
                if (component != null) return component;
            }
            Assert.Fail(typeof(T).Name + " missing from " + loadedScene.path);
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
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(outputDirectory, "M2_Prototype.png"),
                    pixels.EncodeToPNG());
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Optional M2 scene capture was unavailable: " + exception.Message);
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
        private void SetKeys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
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