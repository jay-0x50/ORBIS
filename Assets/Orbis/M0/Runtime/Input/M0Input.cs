using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbis.M0
{
    /// <summary>입력 에셋의 사본을 소유하므로 씬 재시작 시 공유 액션을 오염시키지 않는다.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class M0Input : MonoBehaviour
    {
        [SerializeField] private InputActionAsset controls;
        private InputActionAsset instance;
        private InputActionMap gameplay;
        private InputAction move, lookDelta, lookStick, run, jump, attack, release, reset;
        private bool captured;
        private bool focused = true;
        private int suppressUntilFrame;

        public bool GameplayEnabled { get; private set; }
        public bool PresentationLocked { get; private set; }
        public bool HasGameplayFocus => captured && focused;
        public Vector2 Move { get; private set; }
        public Vector2 LookDelta { get; private set; }
        public Vector2 LookStick { get; private set; }
        public bool RunHeld { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool AttackPressed { get; private set; }
        public bool ResetPressed { get; private set; }

        private void Awake()
        {
            if (controls == null) controls = Resources.Load<InputActionAsset>("M0Controls");
            if (controls == null)
            {
                Debug.LogError("M0Controls.inputactions resource is missing.", this);
                enabled = false;
                return;
            }

            instance = Instantiate(controls);
            gameplay = instance.FindActionMap("Gameplay", true);
            move = gameplay.FindAction("Move", true);
            lookDelta = gameplay.FindAction("LookDelta", true);
            lookStick = gameplay.FindAction("LookStick", true);
            run = gameplay.FindAction("Run", true);
            jump = gameplay.FindAction("Jump", true);
            attack = gameplay.FindAction("Attack", true);
            release = gameplay.FindAction("ReleaseCursor", true);
            reset = gameplay.FindAction("Reset", true);
        }

        private void OnEnable()
        {
            if (gameplay == null) return;
            gameplay.Enable();
            SetCapture(true);
        }

        private void Update()
        {
            ClearFrame();
            if (gameplay == null || !focused) return;
            if (release.WasPressedThisFrame())
            {
                SetCapture(false);
                return;
            }

            // Editor의 Escape, Alt-Tab에 의해 외부에서 커서가 풀렸을 때도 게임 입력을 멈춘다.
            if (!Application.isBatchMode && captured && Cursor.lockState != CursorLockMode.Locked)
                SetCapture(false);

            if (!captured)
            {
                // 다시 클릭한 프레임과 다음 입력 갱신까지 버려 재획득 클릭이 공격이 되지 않게 한다.
                if (attack.WasPressedThisFrame()) SetCapture(true);
                return;
            }

            if (PresentationLocked || Time.frameCount <= suppressUntilFrame) return;
            GameplayEnabled = true;
            Move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            LookDelta = lookDelta.ReadValue<Vector2>();
            LookStick = lookStick.ReadValue<Vector2>();
            RunHeld = run.IsPressed();
            JumpPressed = jump.WasPressedThisFrame();
            AttackPressed = attack.WasPressedThisFrame();
            ResetPressed = reset.WasPressedThisFrame();
        }

        /// <summary>Temporarily suppress gameplay for presentation without releasing or recapturing the cursor.</summary>
        public void SetPresentationLocked(bool locked)
        {
            PresentationLocked = locked;
            suppressUntilFrame = Time.frameCount + 1;
            ClearFrame();
        }

        private void SetCapture(bool value)
        {
            captured = value;
            suppressUntilFrame = Time.frameCount + 1;
            if (!Application.isBatchMode)
            {
                Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !value;
            }
            ClearFrame();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus || Application.isBatchMode;
            if (!hasFocus && !Application.isBatchMode)
            {
                SetCapture(false);
                // 포커스 복귀 시 held 상태가 남지 않도록 현재 액션 상태를 폐기한다.
                gameplay?.Disable();
            }
            else if (isActiveAndEnabled) gameplay?.Enable();
        }

        private void OnDisable()
        {
            gameplay?.Disable();
            SetCapture(false);
        }

        private void OnDestroy()
        {
            if (instance != null) Destroy(instance);
        }

        private void ClearFrame()
        {
            GameplayEnabled = false;
            Move = LookDelta = LookStick = Vector2.zero;
            RunHeld = JumpPressed = AttackPressed = ResetPressed = false;
        }
    }
}

