using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using Orbis.M0;
using Orbis.M1;
using Orbis.M2;
using Orbis.M3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Orbis.M4
{
    [DefaultExecutionOrder(-1400)]
    public sealed class M4SceneBootstrap : MonoBehaviour, IM4LocalRegionTravel
    {
        [SerializeField] private M4RegionId region;
        [SerializeField] private M4AuthoredRegion authoredRegion;
        [SerializeField] private bool initializeOnAwake = true;
        [SerializeField] private M4IslandRegion[] islandRegions = Array.Empty<M4IslandRegion>();
        private readonly List<M4RegionRuntime> regions = new List<M4RegionRuntime>();
        private M4RegionRuntime activeRegion;
        private M4RegionRouter localRouter;
        private bool initializing;
        public bool IsIsland => islandRegions != null && islandRegions.Length > 0;
        public IReadOnlyList<M4RegionRuntime> Regions => regions.AsReadOnly();
        public M4IslandRegion[] IslandRegions
        {
            get => islandRegions;
            set
            {
                if (IsInitialized || initializing) throw new InvalidOperationException("Island bindings cannot change after initialization starts.");
                islandRegions = value ?? Array.Empty<M4IslandRegion>();
            }
        }
        public event Action<M4RegionId> ActiveRegionChanged;
        public bool IsInitialized { get; private set; }
        public bool InitializeOnAwake { get => initializeOnAwake; set => initializeOnAwake = value; }
        public M4AuthoredRegion AuthoredRegion
        {
            get
            {
                if (activeRegion != null && activeRegion.Authored != null) return activeRegion.Authored;
                if (IsIsland)
                    foreach (var entry in islandRegions)
                        if (entry != null && entry.Id == region) return entry.Authored;
                return authoredRegion;
            }
            set
            {
                if (IsInitialized || initializing) throw new InvalidOperationException("World bindings cannot change after initialization starts.");
                authoredRegion = value;
            }
        }
        private M0Input input;
        private PlayerMotor motor;
        private M0CameraRig rig;
        private BasicAttackCombo combat;
        private M4ProgressService progress;
        private Transform glider;

        private MaterialPropertyBlock tint;
        private bool knockoutPending, travelMenu, previousMenuLock;
        private int travelSelection;


        private float nextCalendarCheck;
        private string observedWeek;
        private string notice = "NPC에게 F로 위임을 수락하세요.";
        private GUIStyle heading, body;
        private Vector2 questScroll, hudScroll;
        public M4RegionId Region => region;
        public M4RegionLayout Layout => activeRegion?.Layout;
        public PartyManager Party { get; private set; }
        public ElementalReactionManager Manager { get; private set; }
        public ExplorationMotor Traversal { get; private set; }
        public M4PlayerVitals Vitals { get; private set; }
        public M4FieldBoss Boss => activeRegion?.Boss;
        public M4RegionContent Content => activeRegion?.Content;
        public M3Presentation Presentation { get; private set; }
        public M4ProgressService Progress => progress;
        public Vector3 SurveyPosition => activeRegion != null ? activeRegion.SurveyPosition : Vector3.zero;
        public bool TravelMenuOpen => travelMenu;
        public bool ShowHud { get; set; } = true;
        public string Notice => notice;
        public int TravelSelection => travelSelection;

        // Optional later-milestone composition hooks. Existing prototype scenes need no subscriber.
        public static event Action<M4SceneBootstrap> PlayerReady;
        public static event Action<M4SceneBootstrap> PlayerLeaving;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCompositionHooks() { PlayerReady = null; PlayerLeaving = null; }

        private void Awake()
        {
            if (initializeOnAwake) Initialize();
        }

        /// <summary>Start this scene once, after optional character selection. Editor-authored objects remain in place.</summary>
        public void Initialize()
        {
            if (IsInitialized || initializing) return;
            if (!Application.isPlaying) throw new InvalidOperationException("Initialize starts gameplay and may only run in Play Mode.");
            // Validate every authored reference before creating a player, camera, or subscribing content events.
            ValidateWorldBindings();
            initializing = true;
            try
            {
                InitializeWorld();
                IsInitialized = true;
            }
            finally { initializing = false; }
        }

        private void InitializeWorld()
        {
            var initialAuthored = AuthoredRegion;
            tint = new MaterialPropertyBlock();
            progress = M4Session.Progress;
            observedWeek = progress.WeeklyPeriodId;
            var world = new GameObject("M4 "+M4RegionCatalog.Get(region).DisplayName);
            world.transform.SetParent(transform,false); world.SetActive(false);
            M1SceneBootstrap core;
            M2SceneBootstrap legacy = null;
            if(!IsIsland && initialAuthored == null && region == M4RegionId.Agnia)
            {
                legacy = world.AddComponent<M2SceneBootstrap>();
                legacy.ShowHud=false; legacy.EnableDebugKeys=false;
                world.SetActive(true);
                core=world.GetComponentInChildren<M1SceneBootstrap>();
                Traversal=legacy.Traversal;
            }
            else
            {
                core=world.AddComponent<M1SceneBootstrap>();
                core.BuildEnvironment=false; core.ShowHud=false; core.EnableDebugKeys=false;
                core.RosterOverride=Roster(region);
                world.SetActive(true);
            }
            core.ShowPlaceholderAuraTint=false;
            Party=core.Party; Manager=core.Manager;
            motor=world.GetComponentInChildren<PlayerMotor>(); input=motor.GetComponent<M0Input>();
            combat=motor.GetComponent<BasicAttackCombo>(); rig=world.GetComponentInChildren<M0CameraRig>();
            var initialLayout=initialAuthored != null ? initialAuthored.CreateLayout() : M4RegionGeometry.Build(world.transform,region);
            if(legacy==null)
            {
                if (initialAuthored == null) BuildLighting(world.transform);
                Traversal=motor.gameObject.AddComponent<ExplorationMotor>();
                Traversal.Configure(input,motor,combat,rig.Pivot,new StaminaPool());
                Traversal.DamageOccurred+=OnTraversalDamage;
                BuildGlider(motor.transform);
            }
            if (IsIsland)
            {
                foreach (var entry in islandRegions)
                {
                    var content = entry.Authored.GetComponent<M4RegionContent>() ?? entry.Authored.gameObject.AddComponent<M4RegionContent>();
                    content.Bind(Manager,entry.Id,entry.Authored);
                    AddRegion(entry.Id,entry.Authored,entry.Authored.CreateLayout(),content,entry.Center.position);
                }
            }
            else
            {
                var content=GetComponent<M4RegionContent>() ?? gameObject.AddComponent<M4RegionContent>();
                if (initialAuthored != null) content.Bind(Manager,region,initialAuthored);
                else content.Build(Manager,region,initialLayout,legacy);
                AddRegion(region,initialAuthored,initialLayout,content,initialLayout.Spawn);
            }
            SetActiveRegion(GetRegion(region));
            Presentation=gameObject.AddComponent<M3Presentation>();
            Presentation.Configure(Manager,Party,motor);
            Vitals=gameObject.AddComponent<M4PlayerVitals>();
            Vitals.Configure(Manager,Party,100f);
            Vitals.KnockedOut+=OnKnockedOut;
            foreach (var context in regions)
            {
                context.Boss=context.Content.BossObject.GetComponent<M4FieldBoss>() ?? context.Content.BossObject.AddComponent<M4FieldBoss>();
                var profile=M4RegionCatalog.Get(context.Id);
                context.Boss.Configure(context.Id,profile.Element,profile.Weakness,Manager,Party,motor.transform,M4PrototypeSettings.UseCoreExposure);
                context.Boss.SetPlayerVitals(Vitals);
                context.Boss.Defeated+=OnBossDefeated;
                context.Boss.ResetEncounter(progress.IsBossDefeated(context.Id));
                foreach(var renderer in context.Content.BossObject.GetComponentsInChildren<Renderer>())
                    if(renderer.name=="Weakness Core") context.BossCore=renderer;
            }
            if (IsIsland) ConfigureIslandCameras();
            Warp(Layout.Spawn);
            PlayerReady?.Invoke(this);
            if(M4Session.HasTraveler)
            {
                Vitals.Restore(M4Session.Health);
                Traversal.Stamina.SetCurrent(M4Session.Stamina);
                for(int i=0;i<Party.Members.Count;i++)
                    if(!string.IsNullOrEmpty(M4Session.ActiveMemberId)
                        ? Party.Members[i].Actor.SourceId==M4Session.ActiveMemberId
                        : Party.Members[i].Actor.Element==M4Session.ActiveElement) {Party.TrySwitch(i);break;}
            }
            if (IsIsland)
            {
                localRouter = M4RegionRouter.Instance;
                localRouter.RegisterLocalWorld(this);
            }
            if(M4RegionRouter.Instance.IsLoading) input.SetPresentationLocked(true);
            Physics.SyncTransforms();
        }

        public void ValidateWorldBindings()
        {
            M4RegionCatalog.Get(region);
            if (!IsIsland) { authoredRegion?.Validate(); return; }
            if (islandRegions.Length != 5) throw new InvalidOperationException("An island must bind all five elemental districts.");
            var ids = new HashSet<M4RegionId>();
            var bindings = new HashSet<M4AuthoredRegion>();
            var actors = new HashSet<ElementalActor>();
            var puzzles = new HashSet<FieldElementPuzzle>();
            var challenges = new HashSet<ChallengeRoom>();
            foreach (var entry in islandRegions)
            {
                if (entry == null || entry.Authored == null || entry.Center == null ||
                    entry.Authored.gameObject.scene != gameObject.scene || entry.Center.gameObject.scene != gameObject.scene ||
                    !ids.Add(entry.Id) || !bindings.Add(entry.Authored))
                    throw new InvalidOperationException("Each island district needs a unique ID, authored binding and same-scene center.");
                M4RegionCatalog.Get(entry.Id); entry.Authored.Validate();
                if (!puzzles.Add(entry.Authored.Puzzle) || !challenges.Add(entry.Authored.Challenge))
                    throw new InvalidOperationException("Island districts cannot share puzzle or challenge components.");
                foreach (var actor in entry.Authored.Statues)
                    if (!actors.Add(actor)) throw new InvalidOperationException("Island districts cannot share target actors.");
                foreach (var actor in entry.Authored.Targets)
                    if (!actors.Add(actor)) throw new InvalidOperationException("Island districts cannot share target actors.");
                if (!actors.Add(entry.Authored.BossObject.GetComponent<ElementalActor>()))
                    throw new InvalidOperationException("Island districts cannot share a boss actor.");
            }
            if (!ids.Contains(region)) throw new InvalidOperationException("The initial district must be bound in the island.");
        }

        private void AddRegion(M4RegionId id, M4AuthoredRegion authored, M4RegionLayout layout,
            M4RegionContent content, Vector3 center)
        {
            var context = new M4RegionRuntime(id,authored,layout,content,center);
            context.PuzzleChanged = () => OnPuzzle(context);
            context.ChallengeChanged = () => OnChallenge(context);
            content.Puzzle.Changed += context.PuzzleChanged;
            content.Challenge.Changed += context.ChallengeChanged;
            regions.Add(context);
        }

        public M4RegionRuntime GetRegion(M4RegionId id)
        {
            foreach (var context in regions) if (context.Id == id) return context;
            throw new ArgumentOutOfRangeException(nameof(id),id,"This world has no initialized district with that ID.");
        }

        private void SetActiveRegion(M4RegionRuntime context)
        {
            if (activeRegion == context) return;
            bool notify = activeRegion != null;
            activeRegion = context; region = context.Id;
            if (notify) ActiveRegionChanged?.Invoke(region);
        }

        /// <summary>Only UI and interaction routing change at a border. The shared pawn and every encounter stay alive.</summary>
        public bool RefreshActiveRegion()
        {
            if (!IsIsland || motor == null || activeRegion == null) return false;
            Vector2 position = new Vector2(motor.transform.position.x,motor.transform.position.z);
            var nearest = activeRegion;
            float currentDistance = Vector2.Distance(position,new Vector2(activeRegion.Center.x,activeRegion.Center.z));
            float nearestDistance = currentDistance;
            foreach (var context in regions)
            {
                float distance = Vector2.Distance(position,new Vector2(context.Center.x,context.Center.z));
                if (distance < nearestDistance) { nearestDistance = distance; nearest = context; }
            }
            // Unspecified border policy: 12m distance advantage prevents HUD flicker while standing on a bisector.
            if (nearest == activeRegion || currentDistance - nearestDistance < 12f) return false;
            SetActiveRegion(nearest);
            return true;
        }

        public bool CanTravelWithinWorld(M4RegionId id)
        {
            if (!IsIsland || !IsInitialized) return false;
            foreach (var context in regions) if (context.Id == id) return true;
            return false;
        }

        public bool TryTravelWithinWorld(M4RegionId id)
        {
            if (!CanTravelWithinWorld(id) || Vitals.IsKnockedOut) return false;
            if (travelMenu) CloseTravelMenu();
            // A fast-travel warp cancels the current cast through the shared action event; its cooldown persists.
            motor.EndAction(PlayerActionState.Skill);
            SetActiveRegion(GetRegion(id));
            Warp(Layout.Spawn);
            notice="지역 이동: "+M4RegionCatalog.Get(id).DisplayName+" / 같은 섬에서 체력·스태미나·파티 유지";
            return true;
        }

        private void ConfigureIslandCameras()
        {
            // Large-island presentation only; legacy prototype camera clipping remains unchanged.
            foreach (var output in GetComponentsInChildren<Camera>(true))
            {
                output.farClipPlane=3000f;
                output.clearFlags=CameraClearFlags.Skybox;
            }
            foreach (var camera in GetComponentsInChildren<CinemachineCamera>(true))
            {
                var lens=camera.Lens; lens.FarClipPlane=3000f; camera.Lens=lens;
            }
        }
        public static ElementType[] Roster(M4RegionId id)
        {
            // Existing five characters only. Regional graybox rosters expose every required mechanic.
            if(id==M4RegionId.Zephyr) return new[] {ElementType.Fire,ElementType.Water,ElementType.Wind,ElementType.Rock};
            if(id==M4RegionId.Granite) return new[] {ElementType.Fire,ElementType.Water,ElementType.Lightning,ElementType.Rock};
            return new[] {ElementType.Fire,ElementType.Water,ElementType.Lightning,ElementType.Wind};
        }

        private void LateUpdate()
        {
            if(Traversal==null) return;
            if (IsIsland) RefreshActiveRegion();
            if(Time.unscaledTime>=nextCalendarCheck)
            {
                nextCalendarCheck=Time.unscaledTime+1f;
                progress.RefreshPeriods();
                if(observedWeek!=progress.WeeklyPeriodId)
                {
                    observedWeek=progress.WeeklyPeriodId;
                    foreach (var context in regions)
                        context.Boss.ResetEncounter(progress.IsBossDefeated(context.Id));
                }
            }
            if(glider!=null) glider.gameObject.SetActive(Traversal.Mode==ExplorationMode.Gliding);
            foreach (var context in regions)
                if(context.BossCore!=null)
                {
                    tint.Clear(); context.BossCore.GetPropertyBlock(tint);
                    tint.SetColor("_BaseColor",context.Boss.State==M4BossState.Exposed?Color.white:
                        M3Palette.Primary(context.Boss.Weakness)); context.BossCore.SetPropertyBlock(tint);
                }
            // Defer rescue until every damage/reaction callback from the previous frame has completed.
            if(knockoutPending) { knockoutPending=false; ResetEncounter(); notice="전투 불능: 입구로 복귀했습니다. 보스에게 다시 도전할 수 있습니다."; return; }
            if(M4RegionRouter.Instance.IsLoading) return;
            Keyboard keys=Keyboard.current;
            if(keys==null) return;
            if(travelMenu)
            {
                if(keys.escapeKey.wasPressedThisFrame) CloseTravelMenu();
                if(keys.upArrowKey.wasPressedThisFrame) travelSelection=(travelSelection+4)%5;
                if(keys.downArrowKey.wasPressedThisFrame) travelSelection=(travelSelection+1)%5;
                if(keys.enterKey.wasPressedThisFrame)
                {
                    var selected=(M4RegionId)travelSelection;
                    CloseTravelMenu();
                    if(selected!=region) M4RegionRouter.Instance.Travel(selected);
                }
                return;
            }
            if(Presentation.Ultimate.IsPlaying)
            {
                if(keys.escapeKey.wasPressedThisFrame||keys.rKey.wasPressedThisFrame) Presentation.Ultimate.Cancel();
                return;
            }
            if(!input.GameplayEnabled) return;
            if(input.ResetPressed) {ResetEncounter();return;}
            if(keys.qKey.wasPressedThisFrame) Presentation.PlayUltimatePresentation();
            if(keys.f1Key.wasPressedThisFrame) Warp(Layout.Spawn);
            else if(keys.f2Key.wasPressedThisFrame) Warp(Layout.PuzzleStatuePositions[1]+Vector3.back*2f+Vector3.up*.1f);
            else if(keys.f3Key.wasPressedThisFrame) Warp(Layout.ChallengeEntry+Vector3.up*.1f);
            else if(keys.f4Key.wasPressedThisFrame) Warp(Layout.BossCenter+Vector3.back*5f+Vector3.up*.1f);
            else if(keys.f5Key.wasPressedThisFrame) Warp(Layout.Npc+Vector3.back*1.8f+Vector3.up*.1f);
            else if(keys.f6Key.wasPressedThisFrame) Warp(SurveyPosition+Vector3.up*.1f);
            else if(keys.f10Key.wasPressedThisFrame) OpenTravelMenu();
            if(keys.fKey.wasPressedThisFrame&&!combat.IsAttacking) Interact();
            if(Vector3.Distance(motor.transform.position,SurveyPosition)<2f)
                progress.RecordObjective(M4ObjectiveKind.Survey,region,EventId("survey"));
        }

        public void Interact()
        {
            if (IsIsland) RefreshActiveRegion();
            Vector3 position=motor.transform.position;
            if(Vector3.Distance(position,Layout.Npc)<2.5f) {InteractNpc();return;}
            if(Vector3.Distance(position,Layout.Chest)<2.5f)
            {
                if(Content.Puzzle.Model.IsUnlocked)
                {
                    if(Content.Puzzle.TryClaimReward()) notice="상자: 강화 재료 +5";
                    else { Content.Puzzle.ResetPuzzle(); notice="퍼즐 연습을 초기화했습니다. 강화 재료는 세션당 한 번만 지급됩니다."; }
                }
                else notice=PuzzleInstruction();
                return;
            }
            if(Vector3.Distance(position,Layout.ChallengeEntry)<2.5f)
            {
                if(Content.Challenge.State==ChallengeState.Completed)
                {
                    if(Content.Challenge.TryClaimReward()) {notice="도전 보상: 강화 재료 +3";return;}
                    Content.Challenge.ResetChallenge();
                }
                notice=Content.Challenge.TryStart()?"도전 시작: 60초 안에 표적 3개에 "+Content.Challenge.Model.RequiredReaction:
                    "도전 진행 중 / "+Content.Challenge.DefeatedCount+"/3";
                return;
            }
            if(Vector3.Distance(position,Layout.Portal)<2.5f) {OpenTravelMenu();return;}
            notice="NPC / 상자 / 도전 입구 / 이동 오벨리스크 2.5m 안에서 F";
        }

        public void InteractNpc()
        {
            int accepted=0,claimed=0;
            // All regional NPCs share the prototype request board. Accept and turn-in are explicit actions.
            var entries=progress.DailyQuests;
            foreach(var quest in entries)
            {
                if(quest.State==M4QuestState.Completed&&progress.Claim(quest.Definition.Id)) claimed++;
                else if(quest.State==M4QuestState.Offered&&progress.Accept(quest.Definition.Id)) accepted++;
            }
            notice=claimed>0?"위임 "+claimed+"개 보상 수령 / 루멘 "+progress.Coins:
                accepted>0?"위임 "+accepted+"개 수락. 오른쪽 목록의 지역·목표를 확인하세요.":"수락한 위임을 완료한 뒤 NPC에게 돌아오세요.";
            if(progress.IsReadOnly||!string.IsNullOrEmpty(progress.LastSaveError))
                notice=progress.LoadMessage+" "+progress.LastSaveError;
        }

        private void OpenTravelMenu()
        {
            if(travelMenu) return;
            Presentation.Ultimate.Cancel(); combat.CancelAttack();
            previousMenuLock=input.PresentationLocked;
            input.SetPresentationLocked(true);
            travelSelection=(int)region; travelMenu=true;
        }
        private void CloseTravelMenu()
        {
            travelMenu=false; input.SetPresentationLocked(previousMenuLock);
        }
        public void PrepareTravel()
        {
            Presentation.Ultimate.Cancel(); combat.CancelAttack();
            if(travelMenu) CloseTravelMenu();
            PlayerLeaving?.Invoke(this);
            M4Session.CaptureTraveler(Vitals.Health,Traversal.Stamina.Current,Party.ActiveMember.Actor.Element,Party.ActiveMember.Actor.SourceId);
            progress.Save();
            input.SetPresentationLocked(true);
        }
        public void FinishTravel() { input.SetPresentationLocked(false); notice="지역 진입: "+M4RegionCatalog.Get(region).DisplayName; }
        public void Warp(Vector3 position)
        {
            if(Presentation!=null) {Presentation.Ultimate.Cancel();Presentation.ClearTransient();}
            Traversal.Teleport(position); motor.transform.rotation=Quaternion.identity; rig.SetOrbit(0f);
        }
        public void ResetEncounter()
        {
            if(travelMenu) CloseTravelMenu();
            Presentation.Ultimate.Cancel(); Presentation.ClearTransient();
            Manager.ResetState(); Party.ResetParty();
            // Island practice reset and rescue reset every encounter consistently; no distant DoT survives.
            foreach (var context in regions)
            {
                context.Content.Puzzle.ResetPuzzle(); context.Content.Challenge.ResetChallenge();
                context.Boss.ResetEncounter(progress.IsBossDefeated(context.Id));
            }
            Vitals.Restore(); Traversal.ResetTraversal(); Warp(Layout.Spawn);
            notice="전투·퍼즐 연습 초기화. 위임/루멘/주간 처치 기록은 유지됩니다.";
        }

        private void OnPuzzle(M4RegionRuntime context)
        {
            bool unlocked=context.Content.Puzzle.Model.IsUnlocked;
            bool completedNow=unlocked&&!context.PuzzleWasUnlocked;
            context.PuzzleWasUnlocked=unlocked;
            // The event belongs to its district even if the player/HUD has since crossed a border.
            if(completedNow)
                progress.RecordObjective(M4ObjectiveKind.FieldPuzzle,context.Id,EventId("puzzle",context.Id));
        }
        private void OnChallenge(M4RegionRuntime context)
        {
            ChallengeState state=context.Content.Challenge.State;
            bool completedNow=state==ChallengeState.Completed&&context.PreviousChallengeState==ChallengeState.Running;
            context.PreviousChallengeState=state;
            if(completedNow)
                progress.RecordObjective(M4ObjectiveKind.ChallengeRoom,context.Id,EventId("challenge",context.Id));
        }
        private void OnBossDefeated(M4RegionId defeated)
        {
            bool saved=progress.RecordBossDefeated(defeated);
            notice=saved?"필드보스 격퇴 / 루멘 +50. 다음 주에 다시 등장합니다.":"보스 격퇴: "+(progress.LastSaveError??progress.LoadMessage);
        }
        private string EventId(string kind)=>EventId(kind,region);
        private string EventId(string kind,M4RegionId id)=>progress.DailyPeriodId+":"+id+":"+kind;
        private void OnKnockedOut()=>knockoutPending=true;
        private void OnTraversalDamage(float amount)=>Manager.ApplyEnvironmentalDamage(Party.ActiveMember.Actor,amount);
        private string PuzzleInstruction()=>PuzzleElementName()+" 원소로 석상 I → II → III를 차례로 적중시키세요.";
        private string PuzzleElementName()=>M4RegionCatalog.Get(region).Element.ToString();

        private void OnGUI()
        {
            if(Party==null||!ShowHud) return;
            if(body==null)
            {
                body=new GUIStyle(GUI.skin.label){fontSize=13,wordWrap=true};
                heading=new GUIStyle(body){fontSize=18,fontStyle=FontStyle.Bold};
            }
            float width=Mathf.Min(440f,Screen.width-24f);
            GUILayout.BeginArea(new Rect(12,12,width,Mathf.Min(620,Screen.height-24)),GUI.skin.box);
            GUILayout.Label("ORBIS M4 / "+M4RegionCatalog.Get(region).DisplayName,heading);
            Bar("HP",Vitals.Health,Vitals.MaximumHealth,new Color(.85f,.3f,.27f));
            Bar("Stamina",Traversal.Stamina.Current,Traversal.Stamina.Maximum,new Color(.32f,.8f,.45f));
            hudScroll=GUILayout.BeginScrollView(hudScroll);
            GUILayout.Label("WASD / Shift / Space 이동 | E 등반 | Q 연출\n물: Ctrl 잠수 / Space 상승 | F 상호작용",body);
            for(int i=0;i<Party.Members.Count;i++) GUILayout.Label((i==Party.ActiveIndex?"> ":"  ")+(i+1)+" "+Party.Members[i].Name+" / "+Party.Members[i].Actor.Element,body);
            GUILayout.Label(PuzzleInstruction()+"\n진행 "+Content.Puzzle.Model.LitCount+"/3",body);
            GUILayout.Label("도전 "+Content.Challenge.Model.RequiredReaction+" / "+Content.Challenge.State+
                " / "+Content.Challenge.RemainingTime.ToString("0")+"초 / "+Content.Challenge.DefeatedCount+"/3",body);
            GUILayout.Label("보스 "+Boss.State+" / 약점 "+Boss.Weakness+" / 코어 적중 "+Boss.WeakHitCount+"/3",body);
            Bar("Boss",Boss.HitPoints,Boss.MaximumHitPoints,new Color(.84f,.53f,.24f));
            GUILayout.Label("약점 3회 → 4초 노출·피해 2배. 공격 예고 링 밖으로 이동해 피하세요.",body);
            GUILayout.Label("F1 시작 / F2 퍼즐 / F3 도전 / F4 보스\nF5 NPC / F6 조사 / F10 지역 선택 / R 연습 초기화",body);
            GUILayout.Label("루멘 "+progress.Coins+" / 강화 재료 "+RewardInventory.Session.EnhancementMaterials,body);
            GUILayout.Label(notice,body);
            if(progress.IsReadOnly||!string.IsNullOrEmpty(progress.LastSaveError)) GUILayout.Label(progress.LoadMessage+"\n"+progress.LastSaveError,body);
            if(Screen.width<900) DrawQuests();
            GUILayout.EndScrollView(); GUILayout.EndArea();
            if(Screen.width>=900)
            {
                GUILayout.BeginArea(new Rect(Screen.width-402,12,390,Mathf.Min(590,Screen.height-24)),GUI.skin.box);
                DrawQuests(); GUILayout.EndArea();
            }
            if(travelMenu)
            {
                GUILayout.BeginArea(new Rect(Screen.width*.5f-210,Screen.height*.5f-140,420,280),GUI.skin.box);
                GUILayout.Label("지역 이동 / ↑↓ 선택 · Enter 이동 · Esc 취소",heading);
                for(int i=0;i<5;i++) GUILayout.Label((i==travelSelection?"> ":"  ")+M4RegionCatalog.Get((M4RegionId)i).DisplayName,body);
                GUILayout.Label("이동 중 체력·스태미나 유지. 전투 중인 보스는 원래 상태로 돌아갑니다.",body);
                GUILayout.EndArea();
            }
            if(M4RegionRouter.Instance.IsLoading) GUI.Label(new Rect(Screen.width*.5f-120,20,320,50),
                "지역 로딩 "+(M4RegionRouter.Instance.Progress*100f).ToString("0")+"%",heading);
        }
        private void DrawQuests()
        {
            GUILayout.Label("NPC 일일 위임 / "+progress.DailyPeriodId,heading);
            GUILayout.Label("NPC 앞 F: 수락 / 완료 후 F: 루멘 +25\n일일 KST 04:00 / 주간 월요일 04:00",body);
            questScroll=GUILayout.BeginScrollView(questScroll);
            foreach(var quest in progress.DailyQuests)
                GUILayout.Label("["+quest.State+"] "+quest.Definition.NpcRequest+"\n"+M4RegionCatalog.Get(quest.Definition.Region).DisplayName+" / "+quest.Definition.Kind,body);
            GUILayout.EndScrollView();
        }
        private void Bar(string label,float value,float maximum,Color color)
        {
            GUILayout.Label(label+" "+value.ToString("0")+" / "+maximum.ToString("0"),body);
            Rect rect=GUILayoutUtility.GetRect(100,10,GUILayout.ExpandWidth(true));
            Color old=GUI.color; GUI.color=new Color(.12f,.14f,.17f);GUI.DrawTexture(rect,Texture2D.whiteTexture);
            GUI.color=color;GUI.DrawTexture(new Rect(rect.x,rect.y,rect.width*Mathf.Clamp01(value/maximum),rect.height),Texture2D.whiteTexture);GUI.color=old;
        }
        private void BuildGlider(Transform parent)
        {
            // Keep one unscaled visibility parent, shared by the graybox wing and its imported replacement.
            var anchor=new GameObject("Placeholder Glider");anchor.transform.SetParent(parent,false);
            var wing=GameObject.CreatePrimitive(PrimitiveType.Cube);wing.name="Glider Wing";
            wing.transform.SetParent(anchor.transform,false);wing.transform.localPosition=Vector3.up*2.1f;wing.transform.localScale=new Vector3(2.8f,.06f,.85f);
            wing.GetComponent<Collider>().enabled=false;Destroy(wing.GetComponent<Collider>());
            wing.GetComponent<Renderer>().sharedMaterial=Resources.Load<Material>("M2/Highlight");
            glider=anchor.transform;anchor.SetActive(false);
        }
        private static void BuildLighting(Transform parent)
        {
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.61f,.67f);
            var sun=new GameObject("M4 Region Sun");sun.transform.SetParent(parent,false);
            sun.transform.rotation=Quaternion.Euler(50,-30,0);
            var light=sun.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.shadows=LightShadows.Soft;
            RenderSettings.sun=light;
        }
        private void OnApplicationPause(bool paused){if(paused)progress?.Save();}
        private void OnApplicationQuit()=>progress?.Save();
        private void OnDestroy()
        {
            if (localRouter != null) localRouter.UnregisterLocalWorld(this);
            foreach (var context in regions)
            {
                if(context.Content != null)
                {
                    if(context.Content.Puzzle!=null)context.Content.Puzzle.Changed-=context.PuzzleChanged;
                    if(context.Content.Challenge!=null)context.Content.Challenge.Changed-=context.ChallengeChanged;
                }
                if(context.Boss!=null)context.Boss.Defeated-=OnBossDefeated;
            }
            if(Vitals!=null)Vitals.KnockedOut-=OnKnockedOut;
            if(Traversal!=null)Traversal.DamageOccurred-=OnTraversalDamage;
        }
    }
}
