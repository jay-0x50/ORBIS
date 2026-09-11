using System.Collections;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Orbis.M0.Tests
{
    /// <summary>
    /// Exercises the generated M0 arena through real Input System events and frame updates.
    /// Run Orbis > M0 > Setup and Validate before running this suite in the Test Runner.
    /// </summary>
    public sealed class M0IntegrationTests
    {
        private Scene testScene;
        private Scene previousScene;
        private Keyboard keyboard;
        private Gamepad gamepad;
        private M0SceneBootstrap bootstrap;
        private M0Input input;
        private PlayerMotor motor;
        private BasicAttackCombo combat;
        private Animator animator;
        private M0CameraRig rig;
        private float previousCaptureDeltaTime;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;
        private bool previousRunInBackground;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDeltaTime = Time.captureDeltaTime;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousRunInBackground = Application.runInBackground;
            previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            // In batch mode no Game view is focused. Unity's default Editor routing sends
            // keyboard/pointer events to the Editor while gamepad events still reach gameplay.
            // Save and restore just these values: replacing a temporary InputSettings instance
            // destroys the old instance inside InputManager.settings and prevents its restoration.
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true;
            // Fixed simulation delta keeps combo windows deterministic even with headless rendering.
            Time.captureDeltaTime = 1f / 60f;
            previousScene = SceneManager.GetActiveScene();
            testScene = SceneManager.CreateScene("M0 Integration " + System.Guid.NewGuid());
            SceneManager.SetActiveScene(testScene);
            keyboard = InputSystem.AddDevice<Keyboard>();
            gamepad = InputSystem.AddDevice<Gamepad>();
            bootstrap = new GameObject("M0 Test Bootstrap").AddComponent<M0SceneBootstrap>();
            input = bootstrap.GetComponentInChildren<M0Input>();
            motor = bootstrap.GetComponentInChildren<PlayerMotor>();
            combat = bootstrap.GetComponentInChildren<BasicAttackCombo>();
            animator = bootstrap.GetComponentInChildren<Animator>();
            rig = bootstrap.GetComponentInChildren<M0CameraRig>();
            SetKeys();
            SetPad();
            yield return Frames(12);
            Assert.That(input.GameplayEnabled, Is.True,
                "The Game view must have focus/cursor capture when using the interactive Test Runner.");
            Assert.That(motor.IsGrounded, Is.True, "The prototype must settle on its test floor.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
            Application.runInBackground = previousRunInBackground;
            if (previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);
            if (testScene.IsValid() && testScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(testScene);
            Time.captureDeltaTime = previousCaptureDeltaTime;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        [UnityTest]
        public IEnumerator WalkingRunningAndDiagonalInputRespectWorldSpeed()
        {
            Vector3 start = motor.transform.position;
            SetKeys(Key.A);
            yield return Frames(12);
            float walked = HorizontalDistance(start, motor.transform.position);
            Assert.That(motor.HorizontalSpeed, Is.EqualTo(2.5f).Within(0.01f));
            Assert.That(walked, Is.EqualTo(0.5f).Within(0.09f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), Is.True);

            start = motor.transform.position;
            SetKeys(Key.A, Key.LeftShift);
            yield return Frames(12);
            float ran = HorizontalDistance(start, motor.transform.position);
            Assert.That(motor.HorizontalSpeed, Is.EqualTo(6f).Within(0.01f));
            Assert.That(ran, Is.EqualTo(1.2f).Within(0.13f));
            Assert.That(ran, Is.GreaterThan(walked * 2f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Run"), Is.True);

            start = motor.transform.position;
            SetKeys(Key.A, Key.S, Key.LeftShift);
            yield return Frames(12);
            Assert.That(motor.HorizontalSpeed, Is.EqualTo(6f).Within(0.01f));
            Assert.That(HorizontalDistance(start, motor.transform.position),
                Is.EqualTo(ran).Within(0.13f), "Diagonal movement must not gain speed.");

            SetKeys(Key.A);
            yield return Frames(2);
            Assert.That(motor.HorizontalSpeed, Is.EqualTo(2.5f).Within(0.01f));
            SetKeys();
            yield return Frames(8);
            Assert.That(motor.HorizontalSpeed, Is.Zero);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
        }

        [UnityTest]
        public IEnumerator JumpLeavesGroundRejectsAirJumpAndReturnsToIdle()
        {
            float floor = motor.transform.position.y;
            SetKeys(Key.Space);
            yield return Frames(1);
            SetKeys();
            yield return Frames(9);
            Assert.That(motor.IsGrounded, Is.False);
            Assert.That(motor.transform.position.y, Is.GreaterThan(floor + 0.6f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Jump"), Is.True);

            // A second distinct press while airborne must not restart the vertical impulse.
            SetKeys(Key.Space);
            yield return Frames(1);
            SetKeys();
            float peak = motor.transform.position.y;
            for (int frame = 0; frame < 46; frame++)
            {
                yield return null;
                peak = Mathf.Max(peak, motor.transform.position.y);
            }
            Assert.That(peak - floor, Is.InRange(1.1f, 1.5f));
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(motor.transform.position.y, Is.EqualTo(floor).Within(0.06f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
        }

        [UnityTest]
        public IEnumerator CameraStickYawChangesTheActualMovementDirection()
        {
            SetPad(new Vector2(1f, 0f));
            yield return Frames(36);
            SetPad();
            yield return Frames(1);
            Assert.That(Mathf.DeltaAngle(90f, rig.Pivot.eulerAngles.y), Is.EqualTo(0f).Within(4f));

            Vector3 start = motor.transform.position;
            SetKeys(Key.W);
            yield return Frames(12);
            Vector3 displacement = motor.transform.position - start;
            Assert.That(displacement.x, Is.GreaterThan(0.4f));
            Assert.That(Mathf.Abs(displacement.z), Is.LessThan(0.07f));
            Assert.That(Vector3.Dot(motor.transform.forward, Vector3.right), Is.GreaterThan(0.95f));
        }

        [UnityTest]
        public IEnumerator AttackLocksTranslationAndJumpButEachNewHitCanTurn()
        {
            Vector3 start = motor.transform.position;
            SetKeys(Key.D);
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(1);
            Assert.That(combat.CurrentStep, Is.EqualTo(1));
            Assert.That(Vector3.Dot(motor.transform.forward, Vector3.right), Is.GreaterThan(0.99f));

            SetPad();
            SetKeys(Key.A, Key.Space);
            yield return Frames(8);
            Assert.That(HorizontalDistance(start, motor.transform.position), Is.LessThan(0.01f));
            Assert.That(motor.transform.position.y, Is.EqualTo(start.y).Within(0.03f));
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(motor.HorizontalSpeed, Is.Zero);
            Assert.That(Vector3.Dot(motor.transform.forward, Vector3.right), Is.GreaterThan(0.99f),
                "Changing movement input must not turn an already-running attack.");
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack1"), Is.True);

            SetKeys(Key.A);
            for (int frame = 0; combat.CurrentStep == 1 && combat.NormalizedTime < 0.7f && frame < 30; frame++)
                yield return null;
            Assert.That(combat.CurrentStep, Is.EqualTo(1));
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(1);
            Assert.That(combat.HasBufferedAttack, Is.True);
            SetPad();
            for (int frame = 0; combat.CurrentStep == 1 && frame < 20; frame++)
                yield return null;
            Assert.That(combat.CurrentStep, Is.EqualTo(2));
            Assert.That(Vector3.Dot(motor.transform.forward, Vector3.left), Is.GreaterThan(0.99f));
            SetKeys(Key.D);
            yield return Frames(5);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack2"), Is.True);
            Assert.That(Vector3.Dot(motor.transform.forward, Vector3.left), Is.GreaterThan(0.99f));
            Assert.That(HorizontalDistance(start, motor.transform.position), Is.LessThan(0.01f));

            // No additional attack press: finish hit two, then resume the held movement input.
            yield return Frames(40);
            Assert.That(combat.IsAttacking, Is.False);
            Assert.That(motor.StateName, Is.EqualTo("Move"));
            Assert.That(motor.transform.position.x, Is.GreaterThan(start.x + 0.2f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), Is.True);

            SetKeys();
            yield return Frames(2);
            float locomotionAnimatorSpeed = animator.speed;
            Assert.That(locomotionAnimatorSpeed, Is.EqualTo(1f));
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(1);
            Assert.That(combat.IsAttacking, Is.True);
            Assert.That(animator.speed, Is.Zero, "The combat FSM owns the attack animation clock.");
            combat.enabled = false;
            Assert.That(combat.IsAttacking, Is.False, "OnDisable must cancel an in-progress attack.");
            Assert.That(animator.speed, Is.EqualTo(locomotionAnimatorSpeed),
                "Disabling combat must return control of animation time to locomotion.");
            combat.enabled = true;
            SetPad();
        }

        [UnityTest]
        public IEnumerator CinemachineAndUrpAreActiveAndTheCameraAvoidsTheWall()
        {
            var pipeline = QualitySettings.renderPipeline != null
                ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            Assert.That(pipeline, Is.InstanceOf<UniversalRenderPipelineAsset>());
            var brain = FindInTestScene<CinemachineBrain>();
            var virtualCamera = FindInTestScene<CinemachineCamera>();
            var body = virtualCamera.GetComponent<CinemachineThirdPersonFollow>();
            Assert.That(brain.ActiveVirtualCamera, Is.SameAs(virtualCamera));
            Assert.That(brain.GetComponent<UniversalAdditionalCameraData>(), Is.Not.Null);
            Assert.That(virtualCamera.Follow, Is.SameAs(rig.Pivot));
            Assert.That(body.AvoidObstacles.Enabled, Is.True);

            // Submit a render explicitly: batch mode does not present an end-of-frame screenshot.
            // This optional scene-only artifact does not include the immediate-mode debug HUD.
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                CaptureSceneForVisualReview(brain.GetComponent<UnityEngine.Camera>());


            var controller = motor.GetComponent<CharacterController>();
            controller.enabled = false;
            motor.transform.position = new Vector3(0f, 0f, -4f);
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return Frames(24);
            Assert.That(brain.transform.position.z, Is.GreaterThan(-5.8f),
                "The wall at z=-6 must push the camera to the player's side of the wall.");
            Assert.That(Vector3.Distance(brain.transform.position, rig.Pivot.position), Is.LessThan(3f));
        }

        [UnityTest]
        public IEnumerator CharacterControllerStopsAtWallsAndCeilings()
        {
            var controller = motor.GetComponent<CharacterController>();
            controller.enabled = false;
            motor.transform.position = new Vector3(0f, 0f, -4f);
            controller.enabled = true;
            Physics.SyncTransforms();
            SetKeys(Key.S, Key.LeftShift);
            yield return Frames(30);
            Assert.That(motor.transform.position.z, Is.InRange(-5.6f, -5.3f),
                "Running into the 3 m wall must stop the character on its near side.");

            SetKeys();
            controller.enabled = false;
            motor.transform.position = new Vector3(9f, 0f, 1f);
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return Frames(3);
            SetKeys(Key.Space);
            yield return Frames(1);
            SetKeys();
            float peak = motor.transform.position.y;
            for (int frame = 0; frame < 48; frame++)
            {
                yield return null;
                peak = Mathf.Max(peak, motor.transform.position.y);
            }
            Assert.That(peak, Is.InRange(0.3f, 0.65f),
                "The test ceiling must interrupt the 1.4 m jump without being crossed.");
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(motor.transform.position.y, Is.EqualTo(0f).Within(0.06f));
        }
        [UnityTest]
        public IEnumerator ReleasingAndRecapturingInputDoesNotStartAnAttack()
        {
            SetKeys(Key.D);
            yield return Frames(2);
            SetPad(Vector2.zero, GamepadButton.Start);
            yield return Frames(1);
            Assert.That(input.GameplayEnabled, Is.False);
            Vector3 releasedAt = motor.transform.position;
            yield return Frames(4);
            Assert.That(HorizontalDistance(releasedAt, motor.transform.position), Is.LessThan(0.01f));

            SetKeys();
            SetPad();
            yield return Frames(1);
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(4);
            Assert.That(input.GameplayEnabled, Is.True);
            Assert.That(combat.IsAttacking, Is.False, "The recapture press must be consumed.");

            SetPad();
            yield return Frames(1);
            SetPad(Vector2.zero, GamepadButton.West);
            yield return Frames(1);
            Assert.That(combat.CurrentStep, Is.EqualTo(1), "A new press after recapture must attack normally.");
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
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(outputDirectory, "M0_Prototype.png"),
                    pixels.EncodeToPNG());
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Optional M0 scene capture was unavailable: " + exception.Message);
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

        private T FindInTestScene<T>() where T : Component
        {
            foreach (GameObject root in testScene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>();
                if (component != null) return component;
            }
            Assert.Fail(typeof(T).Name + " was not created by the M0 bootstrap.");
            return null;
        }

        private static IEnumerator Frames(int count)
        {
            for (int frame = 0; frame < count; frame++) yield return null;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b) =>
            Vector3.ProjectOnPlane(b - a, Vector3.up).magnitude;
    }
}
