using System;
using Orbis.M0;
using Orbis.M1;
using Orbis.M4;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orbis.M16
{
    /// <summary>Entry flow and composition adapter. Old milestone demo scenes remain individually testable.</summary>
    public static class ExplorerJourney
    {
        public const string IslandScene = "Assets/Orbis/Game/Scenes/Orbis_Island.unity";
        public const string GameScene = "Assets/Orbis/Game/Scenes/Orbis_OpenWorld.unity";
        public const string LauncherScene = "Assets/Orbis/M4/Scenes/M4_Launcher.unity";
        public static M4ProgressService Profile => M4Session.Progress;
        public static ExplorerController Current { get; private set; }
        public static bool IsActive { get; private set; }
        public static string LastError { get; private set; }
        static ExplorerCatalog catalog;
        static float cooldownUntil;

        public static bool Select(ExplorerChoice choice)
        {
            try
            {
                var definitions = LoadCatalog();
                definitions.Get(choice);
                if (!Profile.TrySelectExplorer(choice, definitions.DefaultElement))
                {
                    LastError = Profile.LastSaveError ?? Profile.LoadMessage;
                    return false;
                }
                LastError = null;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            { LastError = exception.Message; return false; }
        }

        public static bool BeginJourney()
        {
            if (IsActive) { LastError = "여정이 이미 진행 중입니다."; return false; }
            try
            {
                string destination = Application.CanStreamedLevelBeLoaded(IslandScene) ? IslandScene :
                    Application.CanStreamedLevelBeLoaded(GameScene) ? GameScene : LauncherScene;
                if (!Application.CanStreamedLevelBeLoaded(destination))
                    throw new InvalidOperationException("게임 씬 또는 M4 Launcher가 Build Settings에 없습니다.");
                if (!PrepareWorldSession()) return false;
                if (SceneManager.LoadSceneAsync(destination, LoadSceneMode.Single) == null)
                    throw new InvalidOperationException("월드 로드를 시작하지 못했습니다.");
                LastError = null;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                Stop();
                LastError = exception.Message;
                return false;
            }
        }

        /// <summary>Attach selection and travel hooks before initializing an already-loaded authored world.</summary>
        public static bool PrepareWorldSession()
        {
            try
            {
                catalog = LoadCatalog();
                catalog.Get(Profile.GetExplorerSnapshot().choice);
                if (Profile.IsReadOnly) { LastError = Profile.LoadMessage; return false; }
                if (!IsActive)
                {
                    M4SceneBootstrap.PlayerReady -= OnPlayerReady;
                    M4SceneBootstrap.PlayerReady += OnPlayerReady;
                    M4SceneBootstrap.PlayerLeaving -= OnPlayerLeaving;
                    M4SceneBootstrap.PlayerLeaving += OnPlayerLeaving;
                    cooldownUntil = 0f;
                    IsActive = true;
                }
                LastError = null;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            { LastError = exception.Message; return false; }
        }
        static ExplorerCatalog LoadCatalog()
        {
            var value = Resources.Load<ExplorerCatalog>("M16/Catalog");
            if (value == null) throw new InvalidOperationException("Orbis > M1.6 > Setup and Validate를 실행하세요.");
            value.Validate();
            return value;
        }

        static void OnPlayerReady(M4SceneBootstrap scene)
        {
            if (!IsActive || scene == null) return;
            ExplorerSaveData saved = scene.Progress.GetExplorerSnapshot();
            ExplorerDefinition definition = catalog.Get(saved.choice);
            // Compose before presentation installs its avatar roster and before travel restores the active identity.
            PartyMember old = scene.Party.Members[0];
            var actorObject = new GameObject(definition.DisplayName + " Explorer Actor");
            actorObject.transform.SetParent(old.Actor.transform.parent, false);
            actorObject.layer = 10;
            var actor = actorObject.AddComponent<ElementalActor>();
            actor.Configure(definition.Id, saved.element, ActorTeam.Player);
            var explorer = new PartyMember(definition.DisplayName, actor, true);
            scene.Party.RegisterPermanentMember(explorer);
            old.Actor.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(old.Actor.gameObject);
            Current = scene.Traversal.gameObject.AddComponent<ExplorerController>();
            Current.Configure(scene, definition, catalog);
            // Unspecified prototype policy: cooldown continues across region loads using the same scaled game clock.
            Current.RestoreCooldown(Mathf.Max(0f, cooldownUntil - Time.time));
            scene.gameObject.AddComponent<ExplorerStatusHud>().Configure(scene, Current);
        }

        static void OnPlayerLeaving(M4SceneBootstrap scene)
        {
            if (Current == null) return;
            cooldownUntil = Time.time + Current.CooldownRemaining;
            Current.CancelCast();
        }

        /// <summary>End only this optional entry adapter; test fixtures use it before injecting a new temporary profile.</summary>
        public static void Stop()
        {
            M4SceneBootstrap.PlayerReady -= OnPlayerReady;
            M4SceneBootstrap.PlayerLeaving -= OnPlayerLeaving;
            IsActive = false;
            Current = null;
            catalog = null;
            cooldownUntil = 0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void BeginSession() { Stop(); LastError = null; }
    }
}

