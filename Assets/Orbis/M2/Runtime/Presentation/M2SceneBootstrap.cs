using System.Collections.Generic;
using Orbis.M0;
using Orbis.M1;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Orbis.M2
{
    /// <summary>A single M2 graybox region with traversal, one field puzzle and one challenge room.</summary>
    [DefaultExecutionOrder(-1200)]
    public sealed class M2SceneBootstrap : MonoBehaviour
    {
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
        private readonly List<Renderer[]> statueRenderers = new List<Renderer[]>();
        private ElementalActor[] challengeActors;
        private Renderer[][] challengeRenderers;
        private MaterialPropertyBlock tintBlock;
        private M0Input input;
        private PlayerMotor player;
        private M0CameraRig cameraRig;
        private BasicAttackCombo combat;
        private ElementalReactionManager reactions;
        private Transform chest;
        private Transform glider;
        private Transform chestLid;
        private Renderer[] chestRenderers;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private Vector2 hudScroll;
        private string message = "Explore the cliff, water, fire statues and challenge room.";

        public bool ShowHud { get; set; } = true;
        public bool EnableDebugKeys { get; set; } = true;
        public ExplorationMotor Traversal { get; private set; }
        public PartyManager Party { get; private set; }
        public WaterVolume Water { get; private set; }
        public ClimbableSurface Cliff { get; private set; }
        public FieldElementPuzzle FieldPuzzle { get; private set; }
        public ChallengeRoom Challenge { get; private set; }
        public Vector3 SpawnPosition => new Vector3(0f, 0.1f, 0f);
        public Vector3 CliffBasePosition => new Vector3(0f, 0.1f, 11.4f);
        public Vector3 SummitPosition => new Vector3(0f, 8.1f, 16f);
        public Vector3 SwimPosition => new Vector3(-18f, -0.5f, 16f);
        public Vector3 FieldPuzzlePosition => new Vector3(14f, 0.1f, 5.5f);
        public Vector3 ChallengePosition => new Vector3(14f, 0.1f, 21.5f);
        public Vector3 ChestPosition => new Vector3(14f, 0f, 10.5f);

        private void Awake()
        {
            // Unity native objects must be constructed on the main thread, not in field initializers.
            tintBlock = new MaterialPropertyBlock();
            BuildTerrain();
            BuildLighting();
            BuildSharedPlayer();
            BuildFieldPuzzle();
            BuildChallenge();
            Physics.SyncTransforms();
        }

        private void BuildSharedPlayer()
        {
            var shared = new GameObject("M0 M1 Shared Player Core");
            shared.SetActive(false);
            shared.transform.SetParent(transform, false);
            var core = shared.AddComponent<M1SceneBootstrap>();
            core.BuildEnvironment = false;
            core.ShowHud = false;
            core.EnableDebugKeys = false;
            shared.SetActive(true);
            Party = core.Party;
            reactions = core.Manager;
            player = shared.GetComponentInChildren<PlayerMotor>();
            input = player.GetComponent<M0Input>();
            combat = player.GetComponent<BasicAttackCombo>();
            cameraRig = shared.GetComponentInChildren<M0CameraRig>();
            Traversal = player.gameObject.AddComponent<ExplorationMotor>();
            Traversal.Configure(input, player, combat, cameraRig.Pivot, new StaminaPool());
            Traversal.DamageOccurred += ApplyTraversalDamage;
            // Plain original geometry makes the glide mode visible without a VFX or animation asset dependency.
            glider = Child("Placeholder Glider", player.transform, Vector3.zero);
            Box("Glider Wing", glider, new Vector3(0f, 2.1f, 0.15f), new Vector3(2.8f, 0.06f, 0.85f), "Highlight", false);
            Box("Glider Left Support", glider, new Vector3(-0.4f, 1.75f, 0.15f), new Vector3(0.04f, 0.65f, 0.04f), "Metal", false);
            Box("Glider Right Support", glider, new Vector3(0.4f, 1.75f, 0.15f), new Vector3(0.04f, 0.65f, 0.04f), "Metal", false);
            glider.gameObject.SetActive(false);
        }

        private void BuildTerrain()
        {
            Transform terrain = Child("Exploration Region", transform, Vector3.zero);
            // One 60 x 55 m prototype region. Land is solid to y=-4; the basin floor is y=-3.
            Box("South Ground", terrain, new Vector3(0f, -2f, -3f), new Vector3(60f, 4f, 24f), "Ground");
            Box("North Ground", terrain, new Vector3(0f, -2f, 32f), new Vector3(60f, 4f, 16f), "Ground");
            Box("East Ground", terrain, new Vector3(11.5f, -2f, 16.5f), new Vector3(37f, 4f, 15f), "Ground");
            Box("West Ground", terrain, new Vector3(-25.5f, -2f, 16.5f), new Vector3(9f, 4f, 15f), "Ground");
            Box("Lake Floor", terrain, new Vector3(-14f, -3.2f, 16.5f), new Vector3(14f, 0.4f, 15f), "Sand");
            BuildShore(terrain);

            GameObject cliff = Box("Climbable Eight Meter Cliff", terrain, new Vector3(0f, 4f, 16f),
                new Vector3(8f, 8f, 8f), "Cliff");
            Cliff = cliff.AddComponent<ClimbableSurface>();
            Box("Summit Mark", terrain, new Vector3(0f, 8.012f, 16f), new Vector3(1.5f, 0.02f, 1.5f), "Highlight", false);
            Box("Cliff Approach", terrain, new Vector3(0f, 0.012f, 7f), new Vector3(0.14f, 0.02f, 8f), "Highlight", false);
            Box("Puzzle Path", terrain, new Vector3(7f, 0.013f, 4.5f), new Vector3(14f, 0.02f, 0.14f), "Highlight", false);
            Box("Water Path", terrain, new Vector3(-9f, 0.014f, 5f), new Vector3(18f, 0.02f, 0.14f), "Highlight", false);

            var waterRoot = new GameObject("Water Volume");
            waterRoot.transform.SetParent(terrain, false);
            waterRoot.transform.localPosition = new Vector3(-14f, -1.5f, 16.5f);
            waterRoot.layer = 11;
            Water = waterRoot.AddComponent<WaterVolume>();
            Water.Configure(Vector3.zero, new Vector3(14f, 3f, 15f), 0f);
            GameObject surface = Box("Water Surface", terrain, new Vector3(-14f, -0.01f, 16.5f),
                new Vector3(14f, 0.02f, 15f), "Water", false);
            surface.layer = 11;
            surface.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            surface.GetComponent<Renderer>().receiveShadows = false;

            Box("North Boundary", terrain, new Vector3(0f, 1f, 40f), new Vector3(60f, 2f, 0.4f), "Stone");
            Box("South Boundary", terrain, new Vector3(0f, 1f, -15f), new Vector3(60f, 2f, 0.4f), "Stone");
            Box("West Boundary", terrain, new Vector3(-30f, 1f, 12.5f), new Vector3(0.4f, 2f, 55f), "Stone");
            Box("East Boundary", terrain, new Vector3(30f, 1f, 12.5f), new Vector3(0.4f, 2f, 55f), "Stone");
        }

        private void BuildShore(Transform parent)
        {
            // An eight-meter run for a three-meter rise makes a 20.6-degree walkable shore.
            // A closed wedge avoids underwater gaps and lets swimming transition to ground naturally.
            var shore = new GameObject("Gentle Shore");
            shore.transform.SetParent(parent, false);
            shore.layer = 8;
            var mesh = new Mesh { name = "M2 Shore Wedge" };
            mesh.vertices = new[]
            {
                new Vector3(-15f,-3f,9f), new Vector3(-7f,0f,9f), new Vector3(-7f,0f,24f), new Vector3(-15f,-3f,24f),
                new Vector3(-15f,-3.3f,9f), new Vector3(-7f,-3.3f,9f), new Vector3(-7f,-3.3f,24f), new Vector3(-15f,-3.3f,24f)
            };
            mesh.triangles = new[] { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4,
                3,7,6, 3,6,2, 0,4,7, 0,7,3, 1,2,6, 1,6,5 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            shore.AddComponent<MeshFilter>().sharedMesh = mesh;
            shore.AddComponent<MeshRenderer>().sharedMaterial = Material("Sand");
            shore.AddComponent<MeshCollider>().sharedMesh = mesh;
            runtimeMeshes.Add(mesh);
        }

        private void BuildFieldPuzzle()
        {
            Transform zone = Child("Ordered Fire Statue Puzzle", transform, Vector3.zero);
            Box("Puzzle Courtyard", zone, new Vector3(14f, -0.01f, 8.5f), new Vector3(10f, 0.02f, 7f), "Stone", false);
            var actors = new ElementalActor[3];
            for (int i = 0; i < actors.Length; i++)
            {
                actors[i] = CreateTarget("Fire Statue " + (i + 1), zone, new Vector3(11f + i * 3f, 0f, 8f), i + 1);
                statueRenderers.Add(actors[i].GetComponentsInChildren<Renderer>());
            }
            chest = Child("Field Reward Chest", zone, ChestPosition);
            Box("Chest Base", chest, new Vector3(0f, 0.4f, 0f), new Vector3(1.5f, 0.8f, 1.1f), "Chest");
            chestLid = Child("Chest Lid Pivot", chest, new Vector3(0f, 0.8f, 0.55f));
            Box("Chest Lid", chestLid, new Vector3(0f, 0.08f, -0.55f), new Vector3(1.5f, 0.2f, 1.1f), "Chest", false);
            Box("Chest Latch", chest, new Vector3(0f, 0.6f, -0.565f), new Vector3(0.2f, 0.3f, 0.04f), "Metal", false);
            chestRenderers = chest.GetComponentsInChildren<Renderer>();
            FieldPuzzle = zone.gameObject.AddComponent<FieldElementPuzzle>();
            FieldPuzzle.Configure(reactions, actors, RewardInventory.Session);
            FieldPuzzle.Changed += RefreshPuzzle;
            RefreshPuzzle();
        }

        private void BuildChallenge()
        {
            Transform room = Child("Small Reaction Challenge Room", transform, Vector3.zero);
            Box("Room Floor", room, new Vector3(14f, -0.005f, 27f), new Vector3(12f, 0.02f, 14f), "Stone", false);
            Box("Room West Wall", room, new Vector3(8f, 1.5f, 27f), new Vector3(0.4f, 3f, 14f), "Cliff");
            Box("Room East Wall", room, new Vector3(20f, 1.5f, 27f), new Vector3(0.4f, 3f, 14f), "Cliff");
            Box("Room North Wall", room, new Vector3(14f, 1.5f, 34f), new Vector3(12f, 3f, 0.4f), "Cliff");
            Box("Entrance Left", room, new Vector3(10.5f, 1.5f, 20f), new Vector3(5f, 3f, 0.4f), "Cliff");
            Box("Entrance Right", room, new Vector3(17.5f, 1.5f, 20f), new Vector3(5f, 3f, 0.4f), "Cliff");
            Box("Challenge Start Marker", room, new Vector3(14f, 0.012f, 21.5f), new Vector3(1.8f, 0.02f, 1.8f), "Highlight", false);
            var actors = new[]
            {
                CreateTarget("Challenge Target 1", room, new Vector3(11f, 0f, 26f), 1),
                CreateTarget("Challenge Target 2", room, new Vector3(14f, 0f, 28f), 2),
                CreateTarget("Challenge Target 3", room, new Vector3(17f, 0f, 26f), 3)
            };
            Challenge = room.gameObject.AddComponent<ChallengeRoom>();
            Challenge.Configure(reactions, actors, RewardInventory.Session);
            challengeActors = actors;
            challengeRenderers = new Renderer[actors.Length][];
            for (int i = 0; i < actors.Length; i++) challengeRenderers[i] = actors[i].GetComponentsInChildren<Renderer>();
            Challenge.Changed += RefreshChallenge;
            RefreshChallenge();
        }

        private ElementalActor CreateTarget(string name, Transform parent, Vector3 position, int ordinal)
        {
            Transform target = Child(name, parent, position);
            target.gameObject.layer = 9;
            var collider = target.gameObject.AddComponent<CapsuleCollider>();
            collider.center = Vector3.up;
            collider.height = 2f;
            collider.radius = 0.42f;
            Box("Statue Body", target, new Vector3(0f, 0.9f, 0f), new Vector3(0.65f, 1.6f, 0.65f), "Cliff", false);
            Primitive("Statue Head", PrimitiveType.Sphere, target, new Vector3(0f, 1.9f, 0f),
                new Vector3(0.5f, 0.5f, 0.5f), "Stone", false);
            for (int i = 0; i < ordinal; i++)
                Box("Order Mark " + (i + 1), target, new Vector3((i - (ordinal - 1) * 0.5f) * 0.16f, 1.05f, -0.34f),
                    new Vector3(0.08f, 0.36f, 0.04f), "Highlight", false);
            foreach (Transform child in target.GetComponentsInChildren<Transform>()) child.gameObject.layer = 9;
            target.gameObject.AddComponent<TrainingDummy>();
            var actor = target.gameObject.AddComponent<ElementalActor>();
            actor.Configure(name, ElementType.None, ActorTeam.Enemy);
            actor.IsOnField = true;
            return actor;
        }

        private void RefreshPuzzle()
        {
            for (int i = 0; i < statueRenderers.Count; i++)
                foreach (Renderer renderer in statueRenderers[i])
                    if (renderer.name == "Statue Body" || renderer.name == "Statue Head")
                        Tint(renderer, FieldPuzzle.Model.IsLit(i) ? new Color32(0xFF, 0x5A, 0x1F, 0xFF) : new Color(0.38f, 0.41f, 0.43f));
            Color chestColor = FieldPuzzle.Model.RewardClaimed ? new Color(0.25f, 0.3f, 0.27f) :
                FieldPuzzle.Model.IsUnlocked ? new Color(0.9f, 0.74f, 0.34f) : new Color(0.4f, 0.25f, 0.12f);
            foreach (Renderer renderer in chestRenderers)
                if (renderer.name != "Chest Latch") Tint(renderer, chestColor);
            chestLid.localRotation = Quaternion.Euler(FieldPuzzle.Model.RewardClaimed ? 75f : 0f, 0f, 0f);
        }

        private void RefreshChallenge()
        {
            for (int i = 0; i < challengeActors.Length; i++)
            {
                Color color = Challenge.IsDefeated(i) ? new Color(0.24f, 0.65f, 0.4f) :
                    challengeActors[i].AuraElement == ElementType.Fire ? new Color32(0xFF, 0x5A, 0x1F, 0xFF) :
                    challengeActors[i].AuraElement == ElementType.Water ? new Color32(0x1F, 0xA2, 0xFF, 0xFF) :
                    new Color(0.38f, 0.41f, 0.43f);
                foreach (Renderer renderer in challengeRenderers[i])
                    if (renderer.name == "Statue Body" || renderer.name == "Statue Head") Tint(renderer, color);
            }
        }
        private void LateUpdate()
        {
            if (Traversal == null) return;
            RefreshChallenge();
            if (!EnableDebugKeys || !input.GameplayEnabled) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f1Key.wasPressedThisFrame) Warp(SpawnPosition, "Spawn");
            else if (keyboard.f2Key.wasPressedThisFrame) Warp(CliffBasePosition, "Cliff base: E grab, WASD climb");
            else if (keyboard.f3Key.wasPressedThisFrame) Warp(SummitPosition, "Summit: jump or walk off, then Space to glide");
            else if (keyboard.f4Key.wasPressedThisFrame) Warp(SwimPosition, "Water: Space ascend, Ctrl descend; east slope leads out");
            else if (keyboard.f5Key.wasPressedThisFrame) Warp(FieldPuzzlePosition, "Ignis: strike statues I, II, III from left to right");
            else if (keyboard.f10Key.wasPressedThisFrame) Warp(ChallengePosition, "Challenge room: F at the gold entry marker");
            if (keyboard.fKey.wasPressedThisFrame && !combat.IsAttacking) Interact();
            if (input.ResetPressed) ResetPrototype();
        }

        private void Warp(Vector3 position, string notice)
        {
            Traversal.Teleport(position);
            player.transform.rotation = Quaternion.identity;
            cameraRig.SetOrbit(0f);
            message = notice;
        }

        private void Interact()
        {
            // Default interaction reach is 2.5 m, short enough to require approaching the object.
            if (Vector3.Distance(player.transform.position, ChestPosition) <= 2.5f)
            {
                message = FieldPuzzle.TryClaimReward() ? "Chest opened: +5 enhancement materials." :
                    FieldPuzzle.Model.RewardClaimed ? "This chest reward was already claimed this session." :
                    "Light statues I, II, III with Fire before opening this chest.";
                return;
            }
            if (Vector3.Distance(player.transform.position, ChallengePosition) <= 2.5f)
            {
                if (Challenge.State == ChallengeState.Completed)
                    message = Challenge.TryClaimReward() ? "Challenge reward: +3 enhancement materials." :
                        "This challenge reward was already claimed this session.";
                else if (Challenge.State == ChallengeState.Running)
                    message = "Challenge running: trigger Vaporize once on each of the three targets.";
                else
                    message = Challenge.TryStart() ? "Challenge started: 60 seconds, three Vaporize targets." : "The challenge cannot start now.";
                return;
            }
            message = "Move within 2.5 m of the field chest or challenge entry marker, then press F.";
        }

        private void ResetPrototype()
        {
            reactions.ResetState();
            Party.ResetParty();
            FieldPuzzle.ResetPuzzle();
            Challenge.ResetChallenge();
            Traversal.ResetTraversal();
            Traversal.Teleport(SpawnPosition);
            player.transform.rotation = Quaternion.identity;
            cameraRig.SetOrbit(0f);
            message = "Traversal and puzzle reset. Claimed session rewards remain claimed.";
        }

        private void OnGUI()
        {
            if (!ShowHud) return;
            if (Traversal == null) return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            }
            GUILayout.BeginArea(new Rect(12f, 12f, Mathf.Min(470f, Screen.width - 24f), Mathf.Min(610f, Screen.height - 24f)), GUI.skin.box);
            GUILayout.Label("ORBIS / M2 EXPLORATION", titleStyle);
            StaminaPool stamina = Traversal.Stamina;
            GUILayout.Label("Shared party stamina " + stamina.Current.ToString("0") + " / " + stamina.Maximum.ToString("0"), bodyStyle);
            Rect bar = GUILayoutUtility.GetRect(100f, 14f, GUILayout.ExpandWidth(true));
            Color previous = GUI.color;
            GUI.color = new Color(0.12f, 0.17f, 0.2f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = stamina.Current > 20f ? new Color(0.35f, 0.83f, 0.52f) : new Color(0.95f, 0.4f, 0.2f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(stamina.Current / stamina.Maximum), bar.height), Texture2D.whiteTexture);
            GUI.color = previous;
            hudScroll = GUILayout.BeginScrollView(hudScroll, false, true);
            GUILayout.Label("Mode: " + Traversal.Mode + (Traversal.IsUnderwater ? " / UNDERWATER" : "") +
                " | Fall damage: " + Traversal.AccumulatedFallDamage.ToString("0.#") +
                " | Water rescues: " + Traversal.RescueCount, bodyStyle);
            GUILayout.Label("WASD move | Shift run | Space jump / glide\nE grab/release cliff | WASD climb\nWater: Space ascend, Ctrl descend | F interact\n1-4 switch party | Left click attack | Esc release", bodyStyle);
            GUILayout.Label("Active: " + Party.ActiveMember.Name + " / " + Party.ActiveMember.Actor.Element +
                " | Damage " + Party.ActiveMember.Actor.DamageTaken.ToString("0.#") + " | 1 Ignis  2 Maris  3 Sparkle  4 Aura", bodyStyle);
            GUILayout.Label("Stamina: run 2/s after 3s; climb 8/s; glide 5/s; swim 6/s. Rest on land to recover.", bodyStyle);
            GUILayout.Space(5f);
            GUILayout.Label("FIELD PUZZLE: Fire statues I > II > III\nLit " + FieldPuzzle.Model.LitCount + "/3 | Chest " +
                (FieldPuzzle.Model.RewardClaimed ? "claimed" : FieldPuzzle.Model.IsUnlocked ? "unlocked - approach and F" : "locked"), bodyStyle);
            GUILayout.Label("CHALLENGE: " + Challenge.State + " | " + Challenge.RemainingTime.ToString("0.0") +
                "s | Targets " + Challenge.DefeatedCount + "/" + Challenge.RequiredCount, bodyStyle);
            GUILayout.Label("At the gold entry marker, F starts/retries or claims the reward. Use Fire then Water (or reverse) on each target.", bodyStyle);
            GUILayout.Label("Enhancement materials: " + RewardInventory.Session.EnhancementMaterials + " (this play session)", bodyStyle);
            GUILayout.Space(5f);
            GUILayout.Label("TEST WARPS: F1 spawn, F2 cliff, F3 summit,\nF4 water, F5 statues, F10 challenge | R reset\nM1 debug keys F6-F9 are disabled here.", bodyStyle);
            GUILayout.Label(message, bodyStyle);
            if (!input.GameplayEnabled) GUILayout.Label("Click the Game view to capture the cursor. Esc releases it for HUD scrolling.", bodyStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void ApplyTraversalDamage(float damage)
        {
            if (reactions != null && Party != null && Party.ActiveMember != null)
                reactions.ApplyEnvironmentalDamage(Party.ActiveMember.Actor, damage);
        }
        private void BuildLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.65f, 0.74f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.45f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.19f, 0.22f, 0.23f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.18f, 0.24f, 0.29f);
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 100f;
            Transform sun = Child("M2 Sun", transform, Vector3.zero);
            sun.rotation = Quaternion.Euler(48f, -30f, 0f);
            var light = sun.gameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.color = new Color(1f, 0.95f, 0.86f);
            light.shadows = LightShadows.Soft;
            RenderSettings.sun = light;
        }

        private GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, string material, bool solid = true)
            => Primitive(name, PrimitiveType.Cube, parent, position, scale, material, solid);

        private GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position,
            Vector3 scale, string material, bool solid)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.layer = 8;
            item.GetComponent<Renderer>().sharedMaterial = Material(material);
            if (!solid)
            {
                Collider collider = item.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
            }
            return item;
        }

        private Material Material(string name)
        {
            if (materials.TryGetValue(name, out Material material)) return material;
            material = Resources.Load<Material>("M2/" + name);
            if (material == null) Debug.LogError("Missing M2 material: " + name + ". Run Orbis > M2 > Setup and Validate.", this);
            materials.Add(name, material);
            return material;
        }

        private void Tint(Renderer renderer, Color color)
        {
            renderer.GetPropertyBlock(tintBlock);
            tintBlock.SetColor("_BaseColor", color);
            tintBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(tintBlock);
            tintBlock.Clear();
        }

        private static Transform Child(string name, Transform parent, Vector3 position)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = position;
            return child;
        }

        private void OnDestroy()
        {
            if (Traversal != null) Traversal.DamageOccurred -= ApplyTraversalDamage;
            if (FieldPuzzle != null) FieldPuzzle.Changed -= RefreshPuzzle;
            if (Challenge != null) Challenge.Changed -= RefreshChallenge;
            foreach (Mesh mesh in runtimeMeshes) if (mesh != null) Destroy(mesh);
        }
    }
}