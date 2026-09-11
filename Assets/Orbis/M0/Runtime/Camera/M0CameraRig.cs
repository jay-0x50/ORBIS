using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Orbis.M0
{
    /// <summary>회전 입력은 독립 피벗에만 적용하고 실제 카메라는 Cinemachine이 제어한다.</summary>
    [DefaultExecutionOrder(-75)]
    public sealed class M0CameraRig : MonoBehaviour
    {
        // 기획서 미정 기본값: 어깨 높이 1.5m, 거리 4.5m, 시야각 60도.
        [SerializeField] private float pivotHeight = 1.5f;
        [SerializeField, Min(0.5f)] private float distance = 4.5f;
        // 마우스는 프레임당 픽셀 delta이므로 deltaTime을 곱하지 않는다.
        [SerializeField] private float mouseDegreesPerPixel = 0.12f;
        [SerializeField] private float stickDegreesPerSecond = 150f;
        [SerializeField] private float minimumPitch = -35f;
        [SerializeField] private float maximumPitch = 65f;
        private Transform follow;
        private M0Input input;
        private float yaw;
        private float pitch = 15f;
        private CinemachineCamera virtualCamera;
        public Transform Pivot => transform;

        public void Configure(Transform target, M0Input playerInput)
        {
            follow = target;
            input = playerInput;
            yaw = target.eulerAngles.y;
            transform.SetPositionAndRotation(target.position + Vector3.up * pivotHeight,
                Quaternion.Euler(pitch, yaw, 0f));

            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(transform.parent, true);
            cameraObject.tag = "MainCamera";
            var output = cameraObject.AddComponent<UnityEngine.Camera>();
            output.nearClipPlane = 0.1f;
            output.farClipPlane = 200f; // M0 테스트 아레나용 기본값.
            output.clearFlags = CameraClearFlags.SolidColor;
            output.backgroundColor = new Color(0.14f, 0.2f, 0.28f);
            cameraObject.AddComponent<AudioListener>();
            output.GetUniversalAdditionalCameraData();
            var brain = cameraObject.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;

            var rigCamera = new GameObject("M0 Cinemachine Third Person Camera");
            rigCamera.transform.SetParent(transform.parent, true);
            virtualCamera = rigCamera.AddComponent<CinemachineCamera>();
            virtualCamera.Follow = transform;
            virtualCamera.Lens.FieldOfView = 60f;
            var body = rigCamera.AddComponent<CinemachineThirdPersonFollow>();
            body.CameraDistance = distance;
            body.ShoulderOffset = new Vector3(0.35f, 0f, 0f);
            body.VerticalArmLength = 0.2f;
            body.CameraSide = 1f;
            body.Damping = new Vector3(0.08f, 0.1f, 0.08f);
            body.AvoidObstacles = new CinemachineThirdPersonFollow.ObstacleSettings
            {
                Enabled = true,
                CollisionFilter = (1 << 8) | (1 << 9),
                IgnoreTag = "Player",
                CameraRadius = 0.2f,
                DampingIntoCollision = 0f,
                DampingFromCollision = 0.25f
            };
        }

        /// <summary>Reset an existing orbit for prototype checkpoints without creating another camera.</summary>
        public void SetOrbit(float yawDegrees, float pitchDegrees = 15f)
        {
            yaw = Mathf.Repeat(yawDegrees, 360f);
            pitch = Mathf.Clamp(pitchDegrees, minimumPitch, maximumPitch);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void Update()
        {
            if (input == null || !input.GameplayEnabled) return;
            Vector2 look = input.LookDelta * mouseDegreesPerPixel
                + input.LookStick * (stickDegreesPerSecond * Time.deltaTime);
            yaw = Mathf.Repeat(yaw + look.x, 360f);
            pitch = Mathf.Clamp(pitch - look.y, minimumPitch, maximumPitch);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void LateUpdate()
        {
            if (follow != null) transform.position = follow.position + Vector3.up * pivotHeight;
        }
    }
}
