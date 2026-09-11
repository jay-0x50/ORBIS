using System;
using System.Collections.Generic;
using NUnit.Framework;
using Orbis.M0;
using Orbis.M1;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.M16.Tests
{
    public sealed class ExplorerCombatTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<Object> assets = new List<Object>();
        private readonly Vector3 origin = new Vector3(26000f, 0f, 26000f);
        private ElementalReactionManager manager;
        private PlayerMotor motor;
        private M0Input input;
        private BasicAttackCombo combo;
        private PartyManager party;
        private PartyMember[] members;

        [SetUp]
        public void SetUp()
        {
            manager = MakeObject("M16 reactions").AddComponent<ElementalReactionManager>();
            manager.AutoTick = false;
            var player = MakeObject("M16 shared pawn");
            player.layer = 10;
            input = player.AddComponent<M0Input>();
            combo = player.AddComponent<BasicAttackCombo>();
            motor = player.AddComponent<PlayerMotor>();
            motor.Configure(input, MakeObject("camera direction").transform, null, combo);
            combo.Configure(null);
            party = MakeObject("M16 party").AddComponent<PartyManager>();
            members = new[] { Member("legacy-fire", ElementType.Fire), Member("water", ElementType.Water),
                Member("wind", ElementType.Wind), Member("lightning", ElementType.Lightning) };
            party.Configure(input, motor, combo, manager, members);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            for (int i = assets.Count - 1; i >= 0; i--) if (assets[i] != null) Object.DestroyImmediate(assets[i]);
            assets.Clear();
            Physics.SyncTransforms();
        }

        private GameObject MakeObject(string name)
        {
            var value = new GameObject(name); value.transform.position = origin;
            objects.Add(value); return value;
        }
        private ElementalActor Actor(string id, ElementType element, ActorTeam team)
        {
            var actor = MakeObject(id).AddComponent<ElementalActor>(); actor.Configure(id, element, team); return actor;
        }
        private PartyMember Member(string id, ElementType element, bool permanent = false)
            => new PartyMember(id, Actor(id, element, ActorTeam.Player), permanent);
        private ExplorerSkillDefinition Skill(ElementType element)
        {
            var skill = ScriptableObject.CreateInstance<ExplorerSkillDefinition>(); assets.Add(skill);
            skill.Id = "skill." + element; skill.Element = element; return skill;
        }
        private PartyMember InstallExplorer()
        {
            var member = Member("stella", ElementType.Fire, true);
            party.RegisterPermanentMember(member); return member;
        }

        [TestCase(ElementType.Fire)]
        [TestCase(ElementType.Water)]
        [TestCase(ElementType.Wind)]
        [TestCase(ElementType.Rock)]
        [TestCase(ElementType.Lightning)]
        public void CastSnapshotsAllDataAndCrossesTheHitBoundaryOnlyOnce(ElementType element)
        {
            var sequence = new ExplorerSkillSequence();
            var skill = Skill(element);
            int hits = 0, endings = 0;
            ExplorerCast hit = null;
            sequence.HitReady += value => { hits++; hit = value; };
            sequence.Ended += () => endings++;
            Assert.That(sequence.TryStart(skill, 2f), Is.True);
            skill.Element = element == ElementType.Fire ? ElementType.Water : ElementType.Fire;
            skill.Damage = 1000f; skill.Duration = 5f; skill.HitTime = 4f;
            sequence.Tick(.24f);
            Assert.That(hits, Is.Zero);
            Assert.That(sequence.CurrentCast.Element, Is.EqualTo(element));
            sequence.Tick(.02f);
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(hit.Damage, Is.EqualTo(25f));
            Assert.That(hit.Duration, Is.EqualTo(.5f));
            sequence.Tick(8f); sequence.Tick(8f);
            Assert.That(hits, Is.EqualTo(1)); Assert.That(endings, Is.EqualTo(1));
            Assert.That(sequence.IsCasting, Is.False); Assert.That(sequence.CooldownRemaining, Is.Zero);
        }

        [Test]
        public void HugeFrameCannotSkipTheScheduledHitOrEmitItAfterCompletion()
        {
            var sequence = new ExplorerSkillSequence(); int hits = 0;
            sequence.HitReady += _ => hits++;
            sequence.TryStart(Skill(ElementType.Rock), 2f);
            sequence.Tick(.9f);
            Assert.That(hits, Is.EqualTo(1)); Assert.That(sequence.IsCasting, Is.False);
            Assert.That(sequence.CooldownRemaining, Is.EqualTo(1.1f).Within(.0001f));
            sequence.Tick(.2f); Assert.That(hits, Is.EqualTo(1));
        }

        [Test]
        public void CancelAndDifferentElementsShareTheOriginalCooldown()
        {
            var sequence = new ExplorerSkillSequence(); int hits = 0;
            sequence.HitReady += _ => hits++;
            sequence.TryStart(Skill(ElementType.Fire), 2f); sequence.Tick(.1f); sequence.Cancel();
            Assert.That(sequence.CooldownRemaining, Is.EqualTo(1.9f).Within(.0001f));
            foreach (ElementType element in new[] { ElementType.Water, ElementType.Wind, ElementType.Rock, ElementType.Lightning })
                Assert.That(sequence.TryStart(Skill(element), 2f), Is.False);
            sequence.Tick(1.91f); Assert.That(hits, Is.Zero);
            Assert.That(sequence.TryStart(Skill(ElementType.Water), 2f), Is.True);
            Assert.That(sequence.CurrentCast.Element, Is.EqualTo(ElementType.Water));
        }

        [Test]
        public void CooldownRestoreOnlyExtendsAndDoesNotCancelTheCast()
        {
            var sequence = new ExplorerSkillSequence();
            sequence.TryStart(Skill(ElementType.Fire), 2f); sequence.Tick(.1f);
            var cast = sequence.CurrentCast;
            sequence.RestoreCooldown(.2f);
            Assert.That(sequence.CooldownRemaining, Is.EqualTo(1.9f).Within(.0001f));
            sequence.RestoreCooldown(4f);
            Assert.That(sequence.CooldownRemaining, Is.EqualTo(4f)); Assert.That(sequence.CurrentCast, Is.SameAs(cast));
            Assert.Throws<ArgumentOutOfRangeException>(() => sequence.RestoreCooldown(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => sequence.Tick(-1f));
            Assert.That(sequence.CurrentCast, Is.SameAs(cast)); Assert.That(sequence.CooldownRemaining, Is.EqualTo(4f));
        }

        [Test]
        public void HitListenerCanCancelWithoutDuplicatingHitsOrLosingTheCooldown()
        {
            var sequence = new ExplorerSkillSequence(); int hits = 0, endings = 0;
            sequence.HitReady += _ => { hits++; sequence.Cancel(); sequence.Tick(.1f); };
            sequence.Ended += () => endings++;
            sequence.TryStart(Skill(ElementType.Fire), 2f); sequence.Tick(.5f);
            Assert.That(hits, Is.EqualTo(1)); Assert.That(endings, Is.EqualTo(1));
            Assert.That(sequence.IsCasting, Is.False); Assert.That(sequence.CooldownRemaining, Is.EqualTo(1.4f).Within(.0001f));
        }

        [Test]
        public void InvalidAuthoringCannotStartOrConsumeCooldown()
        {
            var sequence = new ExplorerSkillSequence(); var skill = Skill(ElementType.Fire);
            skill.HitTime = skill.Duration + .01f;
            Assert.Throws<InvalidOperationException>(() => sequence.TryStart(skill, 2f));
            Assert.That(sequence.IsCasting, Is.False); Assert.That(sequence.CooldownRemaining, Is.Zero);
        }

        [Test]
        public void PermanentMemberOccupiesOneOfFourSlotsAndCannotBeReplacedRemovedOrMoved()
        {
            var explorer = InstallExplorer();
            Assert.That(party.Members.Count, Is.EqualTo(4)); Assert.That(party.PermanentMember, Is.SameAs(explorer));
            Assert.That(party.Members[0], Is.SameAs(explorer)); Assert.That(members[0].Actor.IsOnField, Is.False);
            Assert.That(party.Members[1], Is.SameAs(members[1]));
            int events = 0; party.ActiveMemberChanged += _ => events++;
            party.RegisterPermanentMember(explorer); Assert.That(events, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => party.RegisterPermanentMember(Member("polaris", ElementType.Fire, true)));
            Assert.Throws<InvalidOperationException>(() => party.Configure(input, motor, combo, manager, members));
            Assert.Throws<ArgumentException>(() => party.Configure(input, motor, combo, manager,
                new[] { members[1], explorer, members[2], members[3] }));
            Assert.Throws<ArgumentException>(() => party.Configure(input, motor, combo, manager,
                new[] { explorer, Member("second-permanent", ElementType.Water, true), members[2], members[3] }));
            Assert.That(party.ActiveMember, Is.SameAs(explorer)); Assert.That(explorer.Actor.IsOnField, Is.True);
        }

        [Test]
        public void ReconfigurationCanChangeCompanionsButKeepsTheRegisteredExplorerAndAllowsMatchingElements()
        {
            var explorer = InstallExplorer();
            var companion = Member("another-fire", ElementType.Fire);
            party.Configure(input, motor, combo, manager, new[] { explorer, companion, members[2], members[3] });
            Assert.That(party.Members[0], Is.SameAs(explorer)); Assert.That(party.Members[1], Is.SameAs(companion));
            Assert.That(party.TrySwitch(1), Is.True); Assert.That(party.TrySwitch(0), Is.True);
            Assert.That(party.ActiveMember, Is.SameAs(explorer));
        }

        [Test]
        public void ElementOnlyRefreshPreservesActorStatusCastStatePositionAndIdentity()
        {
            var explorer = InstallExplorer();
            var enemy = Actor("enemy", ElementType.Water, ActorTeam.Enemy);
            manager.Apply(explorer.Actor, ElementType.Water, enemy, 10f, 4f);
            explorer.Actor.GrantShield(ElementType.Wind, 25f, 6f); manager.Tick(1f);
            motor.SetTraversalMotion(-2f, 0f, false);
            Assert.That(motor.TryBeginAction(PlayerActionState.Skill), Is.True);
            Vector3 position = motor.transform.position;
            int changed = 0, switched = 0;
            party.ActiveElementChanged += _ => changed++;
            party.ActiveMemberChanged += _ => switched++;
            explorer.Actor.SetElement(ElementType.Rock); party.RefreshActiveElement();
            Assert.That(changed, Is.EqualTo(1)); Assert.That(switched, Is.Zero);
            Assert.That(explorer.Actor.SourceId, Is.EqualTo("stella"));
            Assert.That(explorer.Actor.AuraElement, Is.EqualTo(ElementType.Water));
            Assert.That(explorer.Actor.AuraRemainingTime, Is.EqualTo(3f));
            Assert.That(explorer.Actor.ShieldAmount, Is.EqualTo(25f));
            Assert.That(explorer.Actor.ShieldRemainingTime, Is.EqualTo(5f));
            Assert.That(explorer.Actor.DamageTaken, Is.EqualTo(10f));
            Assert.That(motor.State, Is.EqualTo(PlayerActionState.Skill));
            Assert.That(motor.VerticalSpeed, Is.EqualTo(-2f)); Assert.That(motor.transform.position, Is.EqualTo(position));
            Assert.Throws<ArgumentOutOfRangeException>(() => explorer.Actor.SetElement(ElementType.None));
        }

        [Test]
        public void PhysicalDamageUsesShieldAccountingWithoutConsumingOrRefreshingAnAura()
        {
            var explorer = InstallExplorer(); var target = Actor("target", ElementType.None, ActorTeam.Enemy);
            manager.Apply(target, ElementType.Fire, explorer.Actor, 0f, 4f); manager.Tick(1f);
            target.GrantShield(ElementType.Water, 4f, 6f);
            int applications = 0, reactions = 0, damages = 0;
            ElementDamageEvent damage = default;
            manager.Applied += _ => applications++; manager.Reacted += _ => reactions++;
            manager.DamageApplied += value => { damages++; damage = value; };
            Assert.That(manager.DealPhysicalDamage(target, explorer.Actor, 10f), Is.True);
            Assert.That(applications + reactions, Is.Zero); Assert.That(damages, Is.EqualTo(1));
            Assert.That(damage.Element, Is.EqualTo(ElementType.None)); Assert.That(damage.Reaction, Is.EqualTo(ReactionType.None));
            Assert.That(damage.AppliedDamage, Is.EqualTo(6f)); Assert.That(target.TotalShieldAbsorbed, Is.EqualTo(4f));
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.Fire)); Assert.That(target.AuraRemainingTime, Is.EqualTo(3f));
            explorer.Actor.IsOnField = false;
            Assert.That(manager.DealPhysicalDamage(target, explorer.Actor, 10f), Is.False);
            Assert.That(manager.DealPhysicalDamage(explorer.Actor, explorer.Actor, 10f), Is.False);
            Assert.That(damages, Is.EqualTo(1));
        }

        [Test]
        public void ExistingComboBridgeDealsPhysicalWeaponDamageForExplorerAndElementsForCompanions()
        {
            var explorer = InstallExplorer(); explorer.BasicAttackDamage = 17f;
            var target = Actor("target", ElementType.None, ActorTeam.Enemy);
            target.transform.position = origin + Vector3.forward;
            target.gameObject.layer = 9;
            target.gameObject.AddComponent<CapsuleCollider>().center = Vector3.up * .9f;
            target.gameObject.AddComponent<TrainingDummy>();
            var child = MakeObject("second hurtbox"); child.transform.SetParent(target.transform, false);
            child.transform.localPosition = Vector3.zero; child.layer = 9;
            child.AddComponent<SphereCollider>().center = Vector3.up * .9f;
            var bridge = motor.gameObject.AddComponent<PartyCombatBridge>(); bridge.Configure(combo, party, manager);
            Physics.SyncTransforms();
            combo.RequestAttack(true); combo.Tick(.2f); combo.Tick(.03f);
            Assert.That(target.DamageTaken, Is.EqualTo(17f)); Assert.That(target.AuraElement, Is.EqualTo(ElementType.None));
            combo.CancelAttack(); Assert.That(party.TrySwitch(1), Is.True);
            combo.RequestAttack(true); combo.Tick(.2f);
            Assert.That(target.DamageTaken, Is.EqualTo(27f)); Assert.That(target.AuraElement, Is.EqualTo(ElementType.Water));
        }

        [Test]
        public void SharedActionStatesRespectPreemptionAndWrongOwnerCannotReleaseLocks()
        {
            InstallExplorer();
            combo.RequestAttack(true);
            Assert.That(motor.State, Is.EqualTo(PlayerActionState.Attack));
            Assert.That(motor.TryBeginAction(PlayerActionState.Skill), Is.False);
            Assert.That(motor.TryBeginAction(PlayerActionState.Burst), Is.True);
            Assert.That(combo.IsAttacking, Is.False);
            Assert.That(party.TrySwitch(1), Is.False);
            motor.EndAction(PlayerActionState.Skill); Assert.That(motor.State, Is.EqualTo(PlayerActionState.Burst));
            motor.EndAction(PlayerActionState.Burst);
            Assert.That(motor.TryBeginAction(PlayerActionState.Skill), Is.True);
            Assert.That(motor.CanBeginAction(PlayerActionState.Burst), Is.False);
            Assert.That(motor.TryBeginAction(PlayerActionState.Hurt), Is.True);
            Assert.That(motor.TryBeginAction(PlayerActionState.Skill), Is.False); Assert.That(party.TrySwitch(1), Is.False);
            Assert.That(motor.TryBeginAction(PlayerActionState.Dead), Is.True);
            motor.EndAction(PlayerActionState.Hurt);
            Assert.That(motor.State, Is.EqualTo(PlayerActionState.Dead));
            Assert.That(motor.TryBeginAction(PlayerActionState.Hurt), Is.False);
            motor.EndAction(PlayerActionState.Dead);
            Assert.That(motor.State, Is.EqualTo(PlayerActionState.Idle)); Assert.That(party.TrySwitch(1), Is.True);
        }

        [Test]
        public void SkillPoseClockRestoresBothOldAndNewAnimatorsWhenTheVisualChanges()
        {
            Animator first = AnimatorFor("first"); Animator second = AnimatorFor("second");
            first.speed = 1.3f; second.speed = .7f;
            motor.SetVisualAnimator(first); motor.TryBeginAction(PlayerActionState.Skill);
            Assert.That(first.speed, Is.Zero);
            motor.SetActionNormalizedTime(PlayerActionState.Skill, .4f); first.Update(0f);
            Assert.That(first.GetCurrentAnimatorStateInfo(0).IsName("Attack1"), Is.True);
            Assert.That(first.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(.4f).Within(.001f));
            motor.SetVisualAnimator(second);
            Assert.That(first.speed, Is.EqualTo(1.3f)); Assert.That(second.speed, Is.Zero);
            motor.EndAction(PlayerActionState.Skill);
            Assert.That(second.speed, Is.EqualTo(.7f)); Assert.That(motor.ActionLocked, Is.False);
        }
        private Animator AnimatorFor(string name)
        {
            var animator = MakeObject(name).AddComponent<Animator>();
            var controller = new AnimatorController(); assets.Add(controller);
            controller.AddLayer("Base Layer"); var machine = controller.layers[0].stateMachine; assets.Add(machine);
            foreach (string stateName in new[] { "Idle", "Jump", "Attack1" })
            {
                var clip = new AnimationClip { name = stateName }; assets.Add(clip);
                clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, .5f, 0f));
                var state = machine.AddState(stateName); state.motion = clip; assets.Add(state);
            }
            animator.runtimeAnimatorController = controller; animator.Rebind(); animator.Update(0f); return animator;
        }
    }
}
