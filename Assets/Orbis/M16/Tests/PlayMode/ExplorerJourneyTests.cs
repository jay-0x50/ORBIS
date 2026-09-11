using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orbis.Art;
using Orbis.M0;
using Orbis.M1;
using Orbis.M4;
using Orbis.M15;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Orbis.M16.Tests
{
    public sealed class ExplorerJourneyTests
    {
        string directory;
        M4ProgressService profile;
        ExplorerSelectionScreen selection;
        M4SceneBootstrap scene;
        Keyboard keyboard;
        bool cursorVisible, background;
        CursorLockMode cursorLock;
        float scale, fixedDelta;
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
        InputSettings.BackgroundBehavior backgroundInput;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            cursorVisible = Cursor.visible; cursorLock = Cursor.lockState;
            scale = Time.timeScale; fixedDelta = Time.fixedDeltaTime; background = Application.runInBackground;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            backgroundInput = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground = true; Time.timeScale = 1f;
            keyboard = InputSystem.AddDevice<Keyboard>(); Keys();
            directory = Path.Combine(Path.GetTempPath(), "Orbis-M16-Play-" + Guid.NewGuid().ToString("N"));
            profile = new M4ProgressService(Path.Combine(directory, "m4-progress.json"));
            ExplorerJourney.Stop();
            M4Session.UseProgressForTests(profile);
            yield return SceneManager.LoadSceneAsync("Assets/Orbis/M16/Scenes/M16_CharacterSelection.unity");
            selection = Object.FindAnyObjectByType<ExplorerSelectionScreen>();
            Assert.That(selection, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (scene != null) scene.Presentation.Ultimate.Cancel();
            ExplorerJourney.Stop();
            var router = Object.FindAnyObjectByType<M4RegionRouter>();
            if (router != null)
            {
                float deadline = Time.realtimeSinceStartup + 15f;
                while (router.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            }
            var empty = SceneManager.CreateScene("M16 cleanup");
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(empty);
            if (previous.IsValid() && previous.isLoaded) yield return SceneManager.UnloadSceneAsync(previous);
            if (router != null) Object.Destroy(router.gameObject);
            yield return null;
            M4Session.UseProgressForTests(null);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            InputSystem.settings.backgroundBehavior = backgroundInput;
            Time.timeScale = scale; Time.fixedDeltaTime = fixedDelta;
            Application.runInBackground = background;
            Cursor.visible = cursorVisible; Cursor.lockState = cursorLock;
            Assert.That(Path.GetFileName(directory), Does.StartWith("Orbis-M16-Play-"));
            Assert.That(Path.GetDirectoryName(directory), Is.EqualTo(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        void Keys(params Key[] values)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(values));

        }

        IEnumerator Enter(ExplorerChoice choice)
        {
            Assert.That(selection.Select(choice), Is.True, selection.Notice);
            Assert.That(selection.BeginJourney(), Is.True, ExplorerJourney.LastError);
            // The authored island now starts in the central grassland; legacy installations still start in Agnia.
            yield return AwaitRegion(Application.CanStreamedLevelBeLoaded(ExplorerJourney.IslandScene) ? M4RegionId.Zephyr : M4RegionId.Agnia);
        }

        IEnumerator AwaitRegion(M4RegionId region)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            do
            {
                yield return null;
                scene = Object.FindAnyObjectByType<M4SceneBootstrap>();
                if (scene != null && scene.Region == region && !M4RegionRouter.Instance.IsLoading && ExplorerJourney.Current != null)
                    break;
            } while (Time.realtimeSinceStartup < deadline);
            Assert.That(scene, Is.Not.Null, M4RegionRouter.Instance.LastError);
            Assert.That(scene.Region, Is.EqualTo(region));
            Assert.That(ExplorerJourney.Current, Is.Not.Null);
            for (int i = 0; i < 12; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator ChoiceIsRequiredAndSavedStellaSpawnsAsTheOnlyFixedExplorer()
        {
            Assert.That(selection.Choice, Is.EqualTo(ExplorerChoice.Unselected));
            Assert.That(Object.FindAnyObjectByType<PlayerMotor>(), Is.Null);
            Assert.That(selection.BeginJourney(), Is.False);
            yield return Enter(ExplorerChoice.Stella);
            Assert.That(profile.GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Stella));
            Assert.That(new M4ProgressService(profile.SavePath).GetExplorerSnapshot().choice, Is.EqualTo(ExplorerChoice.Stella));
            Assert.That(scene.Party.Members.Count, Is.EqualTo(4));
            Assert.That(scene.Party.PermanentMember, Is.SameAs(scene.Party.Members[0]));
            Assert.That(scene.Party.ActiveIndex, Is.Zero);
            Assert.That(scene.Party.Members[0].Name, Is.EqualTo("스텔라"));
            Assert.That(Object.FindObjectsByType<ElementalActor>().Count(x => x.SourceId == "polaris"), Is.Zero);
            Assert.That(Object.FindObjectsByType<PlayerMotor>().Length, Is.EqualTo(1));
            var definitions = Resources.Load<ExplorerCatalog>("M16/Catalog");
            Assert.That(definitions.Explorers.Length, Is.EqualTo(2));
            Assert.That(definitions.Get(ExplorerChoice.Stella).Weapon, Is.SameAs(definitions.Get(ExplorerChoice.Polaris).Weapon));
            Assert.That(definitions.Get(ExplorerChoice.Stella).Weapon.DisplayName, Is.EqualTo("여정의 검"));
            var gacha = Resources.Load<GachaCatalog>("M15/Catalog");
            Assert.That(gacha.Characters.Length, Is.EqualTo(18));
            Assert.That(gacha.Characters.Any(x => x.Id == "stella" || x.Id == "polaris"), Is.False);
            Assert.That(profile.GetEconomySnapshot().owned, Is.Empty);
            Assert.That(ExplorerJourney.Select(ExplorerChoice.Polaris), Is.False);
        }

        [UnityTest]
        public IEnumerator PolarisKeepsIdentityAndGrayboxWhenElementAndPartyChangeAndAcrossTravel()
        {
            yield return Enter(ExplorerChoice.Polaris);
            var explorer = ExplorerJourney.Current;
            var actor = explorer.Actor;
            var art = scene.GetComponent<ArtScenePresentation>().Characters;
            var graybox = art.ActiveVisual;
            var animator = art.ActiveAnimator;
            Assert.That(graybox, Is.SameAs(art.PermanentVisual));
            Assert.That(explorer.TryChangeElement(ElementType.Wind), Is.True);
            Assert.That(explorer.Actor, Is.SameAs(actor));
            Assert.That(art.ActiveAnimator, Is.SameAs(animator));
            Assert.That(art.ActiveVisual, Is.SameAs(graybox));
            Assert.That(art.ActiveElement, Is.EqualTo(ElementType.Wind));
            Assert.That(scene.Party.TrySwitch(3), Is.True); // Aura also uses Wind: identity must not be inferred from element.
            string companionId = scene.Party.ActiveMember.Actor.SourceId;
            Assert.That(companionId, Is.Not.EqualTo("polaris"));
            Assert.That(scene.Party.TrySwitch(0), Is.True);
            Assert.That(art.ActiveVisual, Is.SameAs(graybox));
            Assert.That(scene.Party.TrySwitch(3), Is.True);
            Assert.That(M4RegionRouter.Instance.Travel(M4RegionId.Teluna), Is.True);
            yield return AwaitRegion(M4RegionId.Teluna);
            Assert.That(scene.Party.ActiveMember.Actor.SourceId, Is.EqualTo(companionId));
            Assert.That(scene.Party.Members[0].Actor.SourceId, Is.EqualTo("polaris"));
            Assert.That(ExplorerJourney.Current.SelectedElement, Is.EqualTo(ElementType.Wind));
            Assert.That(scene.Party.TrySwitch(0), Is.True);
            Keys(Key.Tab); yield return null;
            Keys(); yield return null;
            Assert.That(ExplorerJourney.Current.SelectedElement, Is.EqualTo(ElementType.Rock));
            Assert.That(profile.GetExplorerSnapshot().element, Is.EqualTo(ElementType.Rock));
        }

        [UnityTest]
        public IEnumerator PhysicalComboPreservesAuraAndGSkillUsesTheCastSnapshotWithoutResettingCooldown()
        {
            yield return Enter(ExplorerChoice.Stella);
            var explorer = ExplorerJourney.Current;
            var motor = explorer.GetComponent<PlayerMotor>();
            var combat = explorer.GetComponent<BasicAttackCombo>();
            var targetObject = new GameObject("M16 test target");
            targetObject.transform.SetParent(scene.transform);
            targetObject.transform.position = motor.transform.position + motor.transform.forward * 1.4f;
            targetObject.layer = 9;
            var collider = targetObject.AddComponent<CapsuleCollider>();
            collider.height = 1.8f; collider.radius = .35f; collider.center = Vector3.up * .9f;
            var dummy = targetObject.AddComponent<TrainingDummy>();
            var target = targetObject.AddComponent<ElementalActor>();
            target.Configure("m16.test.target", ElementType.None, ActorTeam.Enemy);
            Physics.SyncTransforms();
            ReactionType targetReaction = ReactionType.None;
            scene.Manager.Reacted += entry => { if (entry.Target == target) targetReaction = entry.Reaction; };
            scene.Manager.Apply(target, ElementType.Water, explorer.Actor, 0f, 10f);
            combat.RequestAttack(true); combat.Tick(.5f);
            Assert.That(dummy.HitCount, Is.EqualTo(1));
            Assert.That(target.DamageTaken, Is.EqualTo(explorer.Definition.Weapon.BaseAttack));
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.Water));
            combat.CancelAttack(); scene.Presentation.ClearTransient();
            explorer.AutoTick = false;
            yield return null;
            Keys(Key.G); yield return null;
            Keys();
            Assert.That(explorer.IsCasting, Is.True);
            Assert.That(motor.StateName, Is.EqualTo("Skill"));
            Assert.That(explorer.CurrentCastElement, Is.EqualTo(ElementType.Fire));
            float remaining = explorer.CooldownRemaining;
            Assert.That(explorer.TryChangeElement(ElementType.Lightning), Is.True);
            Assert.That(explorer.CurrentCastElement, Is.EqualTo(ElementType.Fire));
            Assert.That(explorer.CooldownRemaining, Is.EqualTo(remaining));
            explorer.Tick(.5f);
            Assert.That(targetReaction, Is.EqualTo(ReactionType.Vaporize));
            Assert.That(explorer.IsCasting, Is.False);
            Assert.That(explorer.TryCastSkill(), Is.False);
            Assert.That(scene.Party.TrySwitch(1), Is.True);
            explorer.Tick(.25f);
            Assert.That(scene.Party.TrySwitch(0), Is.True);
            Assert.That(explorer.CooldownRemaining, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator PermanentMemberCannotBeDismissedAndBurstHurtDeadReuseTheSameMotor()
        {
            yield return Enter(ExplorerChoice.Stella);
            var explorer = ExplorerJourney.Current;
            var motor = explorer.GetComponent<PlayerMotor>();
            var original = scene.Party.Members.ToArray();
            var reordered = (PartyMember[])original.Clone();
            reordered[0] = original[1]; reordered[1] = original[0];
            Assert.Throws<ArgumentException>(() => scene.Party.Configure(motor.GetComponent<M0Input>(), motor,
                motor.GetComponent<BasicAttackCombo>(), scene.Manager, reordered));
            Assert.That(scene.Party.PermanentMember, Is.SameAs(original[0]));
            Assert.That(scene.Presentation.PlayUltimatePresentation(), Is.True);
            Assert.That(motor.StateName, Is.EqualTo("Burst"));
            Assert.That(explorer.TryCastSkill(), Is.False);
            scene.Presentation.Ultimate.Cancel();
            Assert.That(motor.StateName, Is.Not.EqualTo("Burst"));
            scene.Manager.ApplyEnvironmentalDamage(explorer.Actor, 1f);
            Assert.That(motor.StateName, Is.EqualTo("Hurt"));
            Assert.That(scene.Presentation.PlayUltimatePresentation(), Is.False);
            scene.Manager.ApplyEnvironmentalDamage(explorer.Actor, 1000f);
            Assert.That(motor.StateName, Is.EqualTo("Dead"));
            yield return null;
            Assert.That(scene.Vitals.Health, Is.GreaterThan(0f));
            Assert.That(motor.StateName, Is.Not.EqualTo("Dead"));
            Assert.That(ExplorerJourney.Current.GetComponent<PlayerMotor>(), Is.SameAs(motor));
        }
    }
}

