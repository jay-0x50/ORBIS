using System.Collections.Generic;
using Orbis.M0;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbis.M1
{
    /// <summary>
    /// M1 verification scene: reuses M0 movement, camera and attacks, then layers four
    /// independent party actors onto one shared placeholder avatar. No M2 content.
    /// </summary>
    [DefaultExecutionOrder(-1100)]
    public sealed class M1SceneBootstrap : MonoBehaviour
    {
        [SerializeField] private bool useRockFourthMember;
        private readonly List<ElementalActor> targets = new List<ElementalActor>();
        private readonly List<TrainingDummy> trainingDummies = new List<TrainingDummy>();
        private readonly Dictionary<ElementalActor, Pose> targetStartPoses = new Dictionary<ElementalActor, Pose>();
        private readonly List<string> recentEvents = new List<string>();
        private readonly Dictionary<ElementalActor, Renderer[]> targetRenderers = new Dictionary<ElementalActor, Renderer[]>();
        private readonly Dictionary<ElementalActor, ElementType> displayedAuras = new Dictionary<ElementalActor, ElementType>();
        private MaterialPropertyBlock tintBlock;
        private M0Input input;
        private PlayerMotor motor;
        private BasicAttackCombo combat;
        private Renderer[] avatarRenderers;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private Vector2 statusScroll;
        private Vector2 eventsScroll;

        public PartyManager Party { get; private set; }
        public ElementalReactionManager Manager { get; private set; }
        public ElementalActor PrimaryTarget { get; private set; }
        // M4 may select four of the five existing characters to match regional mechanics.
        public ElementType[] RosterOverride { get; set; }
        public bool UsesRockFourthMember => useRockFourthMember;
        public bool UseRockFourthMember { get => useRockFourthMember; set => useRockFourthMember = value; }
        public bool ShowPlaceholderAuraTint { get; set; } = true;
        public bool BuildEnvironment { get; set; } = true;
        public bool ShowHud { get; set; } = true;
        public bool EnableDebugKeys { get; set; } = true;

        private void Awake()
        {
            // Native rendering objects must be created on the main thread, after scene deserialization.
            tintBlock = new MaterialPropertyBlock();
            Manager = gameObject.AddComponent<ElementalReactionManager>();
            var coreRoot = new GameObject("M0 Shared Core");
            coreRoot.transform.SetParent(transform, false);
            coreRoot.SetActive(false);
            var core = coreRoot.AddComponent<M0SceneBootstrap>();
            core.BuildEnvironment = BuildEnvironment;
            core.ShowHud = false;
            coreRoot.SetActive(true);

            motor = coreRoot.GetComponentInChildren<PlayerMotor>();
            input = motor.GetComponent<M0Input>();
            combat = motor.GetComponent<BasicAttackCombo>();
            avatarRenderers = motor.GetComponentInChildren<Animator>().GetComponentsInChildren<Renderer>();

            if (BuildEnvironment) CreateTargets(coreRoot.GetComponentInChildren<TrainingDummy>());
            PartyMember[] members = CreatePartyActors(motor.transform);
            Party = gameObject.AddComponent<PartyManager>();
            Party.Configure(input, motor, combat, Manager, members);
            Party.ActiveMemberChanged += ShowActiveMember;
            gameObject.AddComponent<PartyCombatBridge>().Configure(combat, Party, Manager);
            Manager.Applied += OnElementApplied;
            Manager.Reacted += OnReaction;
            Manager.DamageApplied += OnDamage;
            ShowActiveMember(Party.ActiveMember);
            AddEvent(useRockFourthMember ? "Crystallize roster ready: slot 4 Grom / Rock." : "Party roster ready: slot 4 Aura / Wind.");
        }

        private PartyMember[] CreatePartyActors(Transform player)
        {
            var actorRoot = new GameObject("Party Actors").transform;
            actorRoot.SetParent(player, false);
            string[] names = { "Ignis", "Maris", "Sparkle", useRockFourthMember ? "Grom" : "Aura" };
            ElementType[] elements = { ElementType.Fire, ElementType.Water, ElementType.Lightning,
                useRockFourthMember ? ElementType.Rock : ElementType.Wind };
            if (RosterOverride != null)
            {
                if (RosterOverride.Length != 4) throw new System.ArgumentException("A prototype party needs four members.");
                elements = (ElementType[])RosterOverride.Clone();
                var unique = new HashSet<ElementType>();
                for (int i = 0; i < elements.Length; i++)
                {
                    if (!unique.Add(elements[i])) throw new System.ArgumentException("Party elements must be distinct.");
                    switch (elements[i])
                    {
                        case ElementType.Fire: names[i] = "Ignis"; break;
                        case ElementType.Water: names[i] = "Maris"; break;
                        case ElementType.Lightning: names[i] = "Sparkle"; break;
                        case ElementType.Wind: names[i] = "Aura"; break;
                        case ElementType.Rock: names[i] = "Grom"; break;
                        default: throw new System.ArgumentException("Only the five existing characters are available.");
                    }
                }
            }
            var members = new PartyMember[4];
            for (int i = 0; i < members.Length; i++)
            {
                var actorObject = new GameObject(names[i] + " Actor");
                actorObject.transform.SetParent(actorRoot, false);
                actorObject.layer = 10;
                var actor = actorObject.AddComponent<ElementalActor>();
                actor.Configure(names[i], elements[i], ActorTeam.Player);
                actor.IsOnField = i == 0;
                // GameObjects stay active so each member retains its own aura and shield timers.
                members[i] = new PartyMember(names[i], actor);
            }
            return members;
        }

        private void CreateTargets(TrainingDummy original)
        {
            // Default M1 test spacing: adjacent targets are 1.78 m from the primary,
            // close enough for propagation while outside the initial single-target swing.
            var left = Instantiate(original.gameObject, original.transform.parent);
            left.name = "Chain Target Left";
            left.transform.localPosition = new Vector3(-1.4f, 0f, 3.1f);
            var right = Instantiate(original.gameObject, original.transform.parent);
            right.name = "Chain Target Right";
            right.transform.localPosition = new Vector3(1.4f, 0f, 3.1f);
            PrimaryTarget = AddEnemy(original.gameObject, "Primary");
            AddEnemy(left, "Left");
            AddEnemy(right, "Right");
        }

        private ElementalActor AddEnemy(GameObject target, string id)
        {
            var actor = target.AddComponent<ElementalActor>();
            actor.Configure(id, ElementType.None, ActorTeam.Enemy);
            actor.IsOnField = true;
            targets.Add(actor);
            targetStartPoses.Add(actor, new Pose(target.transform.position, target.transform.rotation));
            trainingDummies.Add(target.GetComponent<TrainingDummy>());
            targetRenderers.Add(actor, target.GetComponentsInChildren<Renderer>());
            displayedAuras.Add(actor, (ElementType)(-1));
            return actor;
        }

        private void LateUpdate()
        {
            if (Party == null)
                return;
            if (EnableDebugKeys && input.GameplayEnabled)
            {
                Keyboard keyboard = Keyboard.current;
                if (input.ResetPressed)
                    ResetPrototype();
                else if (keyboard != null)
                {
                    if (keyboard.f6Key.wasPressedThisFrame)
                    {
                        ResetTargets();
                        AddEvent("All three targets reset.");
                    }
                    if (keyboard.f7Key.wasPressedThisFrame)
                    {
                        // Debug hit only: validates received aura, off-field retention and shields.
                        // Default damage 10 matches the prototype's basic elemental hit.
                        Manager.Apply(Party.ActiveMember.Actor, ElementType.Water, PrimaryTarget, 10f, 4f);
                        AddEvent("F7: simulated Water hit on " + Party.ActiveMember.Name + ".");
                    }
                    if (keyboard.f9Key.wasPressedThisFrame)
                    {
                        // Explicit test setup for Wind + Rock in the roster that contains Grom.
                        // Clearing targets first prevents a previous aura reacting with the Wind seed.
                        ResetTargets();
                        Manager.Apply(PrimaryTarget, ElementType.Wind, Party.ActiveMember.Actor, 0f, 4f);
                        AddEvent("F9: targets reset; Primary seeded with Wind for 4 seconds.");
                    }
                    if (keyboard.f8Key.wasPressedThisFrame)
                    {
                        Party.NotifyBurstUsed();
                        AddEvent("F8: 1-second switch lock test hook (no burst skill).");
                    }
                }
            }
            if (ShowPlaceholderAuraTint) RefreshTargetColors();
        }

        private void ResetPrototype()
        {
            Manager.ResetState();
            Party.ResetParty();
            motor.ResetToSpawn();
            ResetTargets();
            recentEvents.Clear();
            ShowActiveMember(Party.ActiveMember);
            AddEvent("Prototype reset: slot 1 active, all actor states cleared.");
        }

        private void ResetTargets()
        {
            foreach (ElementalActor target in targets)
            {
                Manager.ResetTarget(target);
                Pose start = targetStartPoses[target];
                target.transform.SetPositionAndRotation(start.position, start.rotation);
            }
            foreach (TrainingDummy dummy in trainingDummies)
                dummy.ResetHits();
            // Overload moves dummy colliders; restore query positions in the same reset frame.
            Physics.SyncTransforms();
        }
        private void ShowActiveMember(PartyMember member)
        {
            Color color = ElementColor(member.Actor.Element);
            foreach (Renderer renderer in avatarRenderers)
            {
                if (renderer.name == "Torso" || renderer.name == "LeftArm" || renderer.name == "RightArm")
                    Tint(renderer, color);
                else if (renderer.name == "LeftLeg" || renderer.name == "RightLeg" || renderer.name == "Visor")
                    Tint(renderer, Color.Lerp(color, Color.black, 0.58f));
            }
            AddEvent("Active: " + member.Name + " / " + member.Actor.Element + ".");
        }

        private void RefreshTargetColors()
        {
            foreach (ElementalActor target in targets)
            {
                ElementType aura = target.AuraElement;
                if (displayedAuras[target] == aura)
                    continue;
                displayedAuras[target] = aura;
                Color color = aura == ElementType.None ? new Color(0.88f, 0.45f, 0.17f) : ElementColor(aura);
                foreach (Renderer renderer in targetRenderers[target])
                    if (renderer.name == "DummyBody" || renderer.name == "DummyHead")
                        Tint(renderer, color);
            }
        }

        private void Tint(Renderer renderer, Color color)
        {
            renderer.GetPropertyBlock(tintBlock);
            tintBlock.SetColor("_BaseColor", color);
            tintBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(tintBlock);
            tintBlock.Clear();
        }

        private void OnElementApplied(ElementApplicationEvent entry)
        {
            AddEvent(ActorId(entry.Source) + " > " + ActorId(entry.Target) + ": " + entry.IncomingElement +
                (entry.IsPropagation ? " [spread]" : ""));
        }

        private void OnReaction(ElementReactionEvent entry)
        {
            Debug.Log("[M1] " + entry.Reaction + " on " + ActorId(entry.Target) + " (" +
                entry.ExistingElement + " + " + entry.IncomingElement + ")", this);
            AddEvent(entry.Reaction + " on " + ActorId(entry.Target) + " (" + entry.ExistingElement +
                " + " + entry.IncomingElement + ", " + entry.AffectedElement + ")");
        }

        private void OnDamage(ElementDamageEvent entry)
        {
            AddEvent(ActorId(entry.Target) + ": damage " + entry.AppliedDamage.ToString("0.#") +
                (entry.AbsorbedDamage > 0f ? ", shield absorbed " + entry.AbsorbedDamage.ToString("0.#") : ""));
        }

        private static string ActorId(ElementalActor actor) => actor != null ? actor.SourceId : "removed actor";

        private void AddEvent(string message)
        {
            recentEvents.Insert(0, Time.time.ToString("0.0") + "  " + message);
            // Debug history is deliberately bounded; there is no combat-log persistence system.
            if (recentEvents.Count > 8)
                recentEvents.RemoveAt(recentEvents.Count - 1);
        }

        private void OnGUI()
        {
            if (!ShowHud || Party == null)
                return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            }
            float width = Mathf.Min(460f, Screen.width - 24f);
            GUILayout.BeginArea(new Rect(12f, 12f, width, Mathf.Min(Screen.height - 24f, 590f)), GUI.skin.box);
            GUILayout.Label("ORBIS / M1 " + (useRockFourthMember ? "CRYSTALLIZE" : "PARTY"), titleStyle);
            statusScroll = GUILayout.BeginScrollView(statusScroll, false, true);
            GUILayout.Label("1-4: switch party | WASD / Shift / Space: move\nMouse: camera | Left click: 3-hit combo | Esc: release\nR: reset all | F6: reset targets | F7: Water hit on self\nF8: test 1s switch lock (no burst skill)\nF9: reset targets + Wind seed (Wind/Rock test)", bodyStyle);
            GUILayout.Label("Active: " + Party.ActiveMember.Name + " | " + motor.StateName +
                " | Switch lock: " + Party.SwitchLockRemaining.ToString("0.00") + " s", bodyStyle);
            GUILayout.Label("Combo: " + (combat.IsAttacking ? combat.CurrentStep + "/3  " +
                (combat.NormalizedTime * 100f).ToString("0") + "%" : "ready") +
                (combat.HasBufferedAttack ? "  NEXT HIT QUEUED" : ""), bodyStyle);
            GUILayout.Label("Click after 60% of each swing to queue the next hit.\nSwitching keeps position and camera; attacks are cancelled.", bodyStyle);
            GUILayout.Space(5f);
            for (int i = 0; i < Party.Members.Count; i++)
            {
                PartyMember member = Party.Members[i];
                Color previous = GUI.color;
                GUI.color = Color.Lerp(ElementColor(member.Actor.Element), Color.white, 0.45f);
                GUILayout.Label((i == Party.ActiveIndex ? "> " : "  ") + (i + 1) + " " + member.Name +
                    " / " + member.Actor.Element + " / " + (member.Actor.IsOnField ? "ON FIELD" : "off field"), bodyStyle);
                GUI.color = previous;
                GUILayout.Label("   " + ActorStatus(member.Actor), bodyStyle);
            }
            GUILayout.Space(5f);
            GUILayout.Label("TARGETS", bodyStyle);
            foreach (ElementalActor target in targets)
                GUILayout.Label(target.SourceId + ": " + ActorStatus(target), bodyStyle);
            GUILayout.Label("Shared avatar + tint = M1 placeholder. No unique weapons or skills.", bodyStyle);
            if (!input.GameplayEnabled)
                GUILayout.Label("Click the Game view to capture the cursor and resume.", bodyStyle);
            if (Screen.width < 950f)
            {
                GUILayout.Space(5f);
                GUILayout.Label("RECENT ELEMENT EVENTS", bodyStyle);
                foreach (string entry in recentEvents)
                    GUILayout.Label(entry, bodyStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (Screen.width >= 950f)
            {
                GUILayout.BeginArea(new Rect(Screen.width - 382f, 12f, 370f, 300f), GUI.skin.box);
                GUILayout.Label("RECENT ELEMENT EVENTS", titleStyle);
                eventsScroll = GUILayout.BeginScrollView(eventsScroll);
                foreach (string entry in recentEvents)
                    GUILayout.Label(entry, bodyStyle);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
        }

        private static string ActorStatus(ElementalActor actor)
        {
            string aura = actor.AuraElement == ElementType.None ? "none" :
                actor.AuraElement + " " + actor.AuraRemainingTime.ToString("0.0") + "s [" + actor.AuraSourceId + "]";
            string shield = actor.ShieldAmount <= 0f ? "0" : actor.ShieldAmount.ToString("0.#") +
                " " + actor.ShieldElement + " " + actor.ShieldRemainingTime.ToString("0.0") + "s";
            return "Aura " + aura + " | Shield " + shield + " | Damage " + actor.DamageTaken.ToString("0.#");
        }

        // Fixed main palette from design document 03.
        private static Color ElementColor(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return new Color32(0xFF, 0x5A, 0x1F, 0xFF);
                case ElementType.Water: return new Color32(0x1F, 0xA2, 0xFF, 0xFF);
                case ElementType.Lightning: return new Color32(0xB2, 0x6C, 0xFF, 0xFF);
                case ElementType.Wind: return new Color32(0x6C, 0xFF, 0xB8, 0xFF);
                case ElementType.Rock: return new Color32(0xD4, 0xA9, 0x3B, 0xFF);
                default: return Color.white;
            }
        }

        private void OnDestroy()
        {
            if (Party != null)
                Party.ActiveMemberChanged -= ShowActiveMember;
            if (Manager != null)
            {
                Manager.Applied -= OnElementApplied;
                Manager.Reacted -= OnReaction;
                Manager.DamageApplied -= OnDamage;
            }
        }
    }
}