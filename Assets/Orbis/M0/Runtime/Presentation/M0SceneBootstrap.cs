using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.M0
{
    /// <summary>
    /// Builds the self-contained M0 test space. All meshes are original Unity primitives;
    /// the first-import Editor setup supplies the URP materials and Animator clips.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class M0SceneBootstrap : MonoBehaviour
    {
        // M1 reuses this prototype arena with its own party/element debug HUD.
        public bool ShowHud { get; set; } = true;
        // A later milestone can supply its own environment while retaining the M0 avatar and camera.
        public bool BuildEnvironment { get; set; } = true;

        private const int WorldLayer = 8;
        private const int DummyLayer = 9;
        private const int PlayerLayer = 10;
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private M0Input input;
        private PlayerMotor motor;
        private BasicAttackCombo combat;
        private TrainingDummy dummy;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;

        private void Awake()
        {
            if (BuildEnvironment)
            {
                BuildArena();
                BuildLighting();
            }
            BuildPlayer();
            if (BuildEnvironment) BuildDummy();
        }

        private void BuildPlayer()
        {
            var player = new GameObject("Player");
            player.transform.SetParent(transform, false);
            // M0 default: a 1.8 m character starts slightly above the floor to settle naturally.
            player.transform.position = new Vector3(0f, 0.1f, 0f);
            player.layer = PlayerLayer;
            player.tag = "Player";
            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.radius = 0.3f;
            controller.skinWidth = 0.03f;
            controller.stepOffset = 0.3f;
            controller.slopeLimit = 50f;
            controller.minMoveDistance = 0f;

            Transform visual = Child("Visual", player.transform, Vector3.zero);
            Primitive("Torso", PrimitiveType.Cube, visual, new Vector3(0f, 1.15f, 0f),
                new Vector3(0.64f, 0.62f, 0.32f), "PlayerBody", false);
            Transform head = Primitive("Head", PrimitiveType.Sphere, visual, new Vector3(0f, 1.65f, 0f),
                new Vector3(0.36f, 0.4f, 0.36f), "Skin", false).transform;
            // A forward-facing visor makes the player's facing direction easy to read.
            Primitive("Visor", PrimitiveType.Cube, head, new Vector3(0f, 0.07f, 0.43f),
                new Vector3(0.8f, 0.23f, 0.16f), "PlayerAccent", false);

            Transform leftArm = Child("LeftArmPivot", visual, new Vector3(-0.42f, 1.43f, 0f));
            Transform rightArm = Child("RightArmPivot", visual, new Vector3(0.42f, 1.43f, 0f));
            Primitive("LeftArm", PrimitiveType.Cube, leftArm, new Vector3(0f, -0.28f, 0f),
                new Vector3(0.2f, 0.56f, 0.24f), "PlayerBody", false);
            Primitive("RightArm", PrimitiveType.Cube, rightArm, new Vector3(0f, -0.28f, 0f),
                new Vector3(0.2f, 0.56f, 0.24f), "PlayerBody", false);
            Transform leftLeg = Child("LeftLegPivot", visual, new Vector3(-0.18f, 0.88f, 0f));
            Transform rightLeg = Child("RightLegPivot", visual, new Vector3(0.18f, 0.88f, 0f));
            Primitive("LeftLeg", PrimitiveType.Cube, leftLeg, new Vector3(0f, -0.4f, 0f),
                new Vector3(0.24f, 0.8f, 0.27f), "PlayerAccent", false);
            Primitive("RightLeg", PrimitiveType.Cube, rightLeg, new Vector3(0f, -0.4f, 0f),
                new Vector3(0.24f, 0.8f, 0.27f), "PlayerAccent", false);
            Primitive("SwordGrip", PrimitiveType.Cube, rightArm, new Vector3(0f, -0.5f, 0.06f),
                new Vector3(0.12f, 0.13f, 0.3f), "PlayerAccent", false);
            Primitive("SwordGuard", PrimitiveType.Cube, rightArm, new Vector3(0f, -0.5f, 0.23f),
                new Vector3(0.3f, 0.1f, 0.07f), "PlayerAccent", false);
            Primitive("SwordBlade", PrimitiveType.Cube, rightArm, new Vector3(0f, -0.5f, 0.67f),
                new Vector3(0.075f, 0.12f, 0.85f), "Sword", false);
            SetLayerRecursive(visual, PlayerLayer);

            var animator = visual.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("M0/PlayerAnimator");
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (animator.runtimeAnimatorController == null)
                Debug.LogError("M0 Animator is missing. Exit Play Mode, then run Orbis > M0 > Setup and Validate.", this);

            input = player.AddComponent<M0Input>();
            combat = player.AddComponent<BasicAttackCombo>();
            combat.Configure(animator);
            var rig = new GameObject("Camera Rig").AddComponent<M0CameraRig>();
            rig.transform.SetParent(transform, false);
            rig.Configure(player.transform, input);
            motor = player.AddComponent<PlayerMotor>();
            motor.Configure(input, rig.transform, animator, combat);
        }

        private void BuildDummy()
        {
            Transform target = Child("Training Dummy", transform, new Vector3(0f, 0f, 2f));
            target.gameObject.layer = DummyLayer;
            var collider = target.gameObject.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 1f, 0f);
            collider.height = 1.9f;
            collider.radius = 0.4f;
            Primitive("DummyBody", PrimitiveType.Cylinder, target, new Vector3(0f, 1f, 0f),
                new Vector3(0.65f, 0.52f, 0.65f), "Dummy", false);
            Primitive("DummyHead", PrimitiveType.Sphere, target, new Vector3(0f, 1.73f, 0f),
                new Vector3(0.42f, 0.42f, 0.42f), "Dummy", false);
            Primitive("DummyPost", PrimitiveType.Cylinder, target, new Vector3(0f, 0.23f, 0f),
                new Vector3(0.13f, 0.23f, 0.13f), "Platform", false);
            Primitive("TargetMark", PrimitiveType.Cube, target, new Vector3(0f, 1.05f, -0.33f),
                new Vector3(0.26f, 0.26f, 0.025f), "Marking", false);
            SetLayerRecursive(target, DummyLayer);
            dummy = target.gameObject.AddComponent<TrainingDummy>();
        }

        private void BuildArena()
        {
            Transform arena = Child("Movement and Camera Test Arena", transform, Vector3.zero);
            Primitive("Ground", PrimitiveType.Cube, arena, new Vector3(0f, -0.15f, 0f),
                new Vector3(36f, 0.3f, 36f), "Ground", true);
            // M0-only test geometry: step (0.25 m), jump platform (0.9 m), and 18-degree slope.
            Primitive("Small Step", PrimitiveType.Cube, arena, new Vector3(3.5f, 0.125f, -2f),
                new Vector3(2.5f, 0.25f, 2f), "Platform", true);
            Primitive("Jump Platform", PrimitiveType.Cube, arena, new Vector3(5f, 0.45f, 2.5f),
                new Vector3(3f, 0.9f, 3f), "Platform", true);
            GameObject ramp = Primitive("Walkable Ramp", PrimitiveType.Cube, arena, new Vector3(-5f, 0.79f, 3f),
                new Vector3(3f, 0.2f, 5f), "Platform", true);
            ramp.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            Primitive("Ramp Landing", PrimitiveType.Cube, arena, new Vector3(-5f, 0.82f, 6.35f),
                new Vector3(3f, 1.64f, 2.1f), "Platform", true);
            Primitive("Camera Collision Wall", PrimitiveType.Cube, arena, new Vector3(0f, 1.5f, -6f),
                new Vector3(8f, 3f, 0.4f), "Platform", true);
            Primitive("Head Clearance Test", PrimitiveType.Cube, arena, new Vector3(9f, 2.5f, 1f),
                new Vector3(3f, 0.3f, 3f), "Platform", true);

            for (int i = -3; i <= 3; i++)
            {
                Primitive("Grid X " + i, PrimitiveType.Cube, arena, new Vector3(i * 4f, 0.007f, 0f),
                    new Vector3(0.025f, 0.01f, 32f), "Marking", false);
                Primitive("Grid Z " + i, PrimitiveType.Cube, arena, new Vector3(0f, 0.008f, i * 4f),
                    new Vector3(32f, 0.01f, 0.025f), "Marking", false);
            }
            Primitive("North Boundary", PrimitiveType.Cube, arena, new Vector3(0f, 1f, 18f),
                new Vector3(36f, 2f, 0.3f), "Platform", true);
            Primitive("South Boundary", PrimitiveType.Cube, arena, new Vector3(0f, 1f, -18f),
                new Vector3(36f, 2f, 0.3f), "Platform", true);
            Primitive("East Boundary", PrimitiveType.Cube, arena, new Vector3(18f, 1f, 0f),
                new Vector3(0.3f, 2f, 36f), "Platform", true);
            Primitive("West Boundary", PrimitiveType.Cube, arena, new Vector3(-18f, 1f, 0f),
                new Vector3(0.3f, 2f, 36f), "Platform", true);
            SetLayerRecursive(arena, WorldLayer);
        }

        private void BuildLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.75f, 0.84f);
            RenderSettings.ambientEquatorColor = new Color(0.37f, 0.45f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.23f, 0.26f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.12f, 0.19f, 0.24f);
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 75f;
            Transform sun = Child("Sun", transform, Vector3.zero);
            sun.rotation = Quaternion.Euler(48f, -35f, 0f);
            var light = sun.gameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.94f, 0.82f);
            light.shadows = LightShadows.Soft;
            RenderSettings.sun = light;
        }

        private void LateUpdate()
        {
            if (input != null && input.ResetPressed && dummy != null)
                dummy.ResetHits();
        }

        private void OnGUI()
        {
            if (!ShowHud || motor == null || combat == null)
                return;

            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold };
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            }

            // A debug-only HUD keeps this milestone observable without adding a UI system.
            float panelWidth = Mathf.Min(470f, Screen.width - 24f);
            GUILayout.BeginArea(new Rect(12f, 12f, panelWidth, 272f), GUI.skin.box);
            GUILayout.Label("ORBIS  /  M0 CORE PROTOTYPE", titleStyle);
            GUILayout.Label("WASD: walk  |  Shift: run  |  Space: jump\nMouse: camera  |  Left click: 3-hit combo\nEsc: release cursor  |  Click: resume  |  R: reset", bodyStyle);
            GUILayout.Space(6f);
            GUILayout.Label("State: " + motor.StateName + "  |  " + (motor.IsGrounded ? "Grounded" : "Airborne") +
                "  |  Speed: " + motor.HorizontalSpeed.ToString("0.0") + " m/s", bodyStyle);
            string attack = combat.IsAttacking ? "Attack " + combat.CurrentStep + " / 3 (" +
                (combat.NormalizedTime * 100f).ToString("0") + "%)" : "Ready";
            GUILayout.Label("Combo: " + attack + "  |  Dummy hits: " + (dummy != null ? dummy.HitCount : 0), bodyStyle);
            GUILayout.Label("Next hit: " + (combat.HasBufferedAttack ? "QUEUED" : "not queued"), bodyStyle);
            GUILayout.Label("Click in the latter half of a swing to queue its next hit. Movement and jump are locked during attacks; move input aims each new hit.", bodyStyle);
            if (input != null && !input.GameplayEnabled)
                GUILayout.Label("Click the Game view to capture the cursor and resume.", bodyStyle);
            GUILayout.EndArea();
        }

        private GameObject Primitive(string objectName, PrimitiveType type, Transform parent,
            Vector3 position, Vector3 scale, string materialName, bool solid)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = objectName;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.layer = WorldLayer;
            item.GetComponent<Renderer>().sharedMaterial = LoadMaterial(materialName);
            if (!solid)
            {
                var collider = item.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
            }
            return item;
        }

        private Material LoadMaterial(string resourceName)
        {
            if (materials.TryGetValue(resourceName, out Material material))
                return material;
            material = Resources.Load<Material>("M0/" + resourceName);
            if (material == null)
                Debug.LogError("Missing M0 material: " + resourceName + ". Run Orbis > M0 > Setup and Validate.", this);
            materials.Add(resourceName, material);
            return material;
        }

        private static Transform Child(string objectName, Transform parent, Vector3 position)
        {
            var child = new GameObject(objectName).transform;
            child.SetParent(parent, false);
            child.localPosition = position;
            return child;
        }

        private static void SetLayerRecursive(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root)
                SetLayerRecursive(child, layer);
        }
    }
}
