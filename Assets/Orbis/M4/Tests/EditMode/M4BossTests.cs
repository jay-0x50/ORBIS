using System;
using System.Collections.Generic;
using NUnit.Framework;
using Orbis.M0;
using Orbis.M1;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Orbis.M4.Tests
{
    public sealed class M4BossTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly Vector3 origin = new Vector3(24000f, 0f, 24000f);

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            Physics.SyncTransforms();
        }

        [TestCase(ElementType.Fire, ElementType.Water)]
        [TestCase(ElementType.Water, ElementType.Lightning)]
        [TestCase(ElementType.Wind, ElementType.Rock)]
        [TestCase(ElementType.Rock, ElementType.Fire)]
        [TestCase(ElementType.Lightning, ElementType.Water)]
        public void FiveProfilesHaveExplicitPlayableDefaults(ElementType element, ElementType weakness)
        {
            M4BossProfile profile = M4BossProfile.ForElement(element);
            Assert.That(profile.MaximumHealth, Is.InRange(300f, 400f));
            Assert.That(profile.TelegraphDuration, Is.GreaterThanOrEqualTo(.8f));
            Assert.That(profile.AttackRadius, Is.LessThan(profile.EngagementRadius));
            Assert.That(profile.EngagementRadius, Is.LessThan(profile.LeashRadius));
            Assert.That(M4BossProfile.DefaultWeakness(element), Is.EqualTo(weakness));
        }

        [Test]
        public void OnlyThreeDirectWeakHitsExposeAndCancelThePendingAttack()
        {
            var boss = Model();
            int attacks = 0;
            boss.AttackReady += () => attacks++;
            boss.Tick(0f, true, true);
            Assert.That(boss.State, Is.EqualTo(M4BossState.Telegraph));
            Assert.That(boss.RegisterDirectHit(ElementType.Fire), Is.False);
            Assert.That(boss.RegisterDirectHit(ElementType.Water, true), Is.False);
            boss.RegisterDirectHit(ElementType.Water);
            boss.RegisterDirectHit(ElementType.Water);
            Assert.That(boss.WeakHitCount, Is.EqualTo(2));
            Assert.That(boss.State, Is.EqualTo(M4BossState.Telegraph));
            boss.RegisterDirectHit(ElementType.Water);
            Assert.That(boss.State, Is.EqualTo(M4BossState.Exposed));
            Assert.That(boss.WeakHitCount, Is.Zero);
            boss.Tick(3.5f, true, true);
            Assert.That(boss.RegisterDirectHit(ElementType.Water), Is.False);
            Assert.That(boss.StateRemaining, Is.EqualTo(.5f).Within(1e-5f));
            boss.Tick(.5f, true, true);
            Assert.That(boss.State, Is.EqualTo(M4BossState.Recover));
            Assert.That(attacks, Is.Zero);
        }

        [TestCase(ElementType.Fire)]
        [TestCase(ElementType.Water)]
        [TestCase(ElementType.Wind)]
        [TestCase(ElementType.Rock)]
        [TestCase(ElementType.Lightning)]
        public void ExposedCoreMultipliesEveryIncomingDamageElement(ElementType incoming)
        {
            var boss = Model();
            Assert.That(boss.DamageMultiplier(incoming), Is.EqualTo(1f));
            for (int i = 0; i < 3; i++) boss.RegisterDirectHit(ElementType.Water);
            Assert.That(boss.DamageMultiplier(incoming), Is.EqualTo(2f));
            boss.Tick(4f, true, true);
            Assert.That(boss.DamageMultiplier(incoming), Is.EqualTo(1f));
        }

        [Test]
        public void SimpleWeaknessModeHasNoStunOrAccumulation()
        {
            var boss = Model(false);
            for (int i = 0; i < 5; i++) boss.RegisterDirectHit(ElementType.Water);
            Assert.That(boss.State, Is.EqualTo(M4BossState.Dormant));
            Assert.That(boss.WeakHitCount, Is.Zero);
            Assert.That(boss.DamageMultiplier(ElementType.Water), Is.EqualTo(2f));
            Assert.That(boss.DamageMultiplier(ElementType.Fire), Is.EqualTo(1f));
        }

        [Test]
        public void LargeDeltaDoesNotCreateUnseenRepeatedAttacks()
        {
            var boss = Model();
            int attacks = 0;
            boss.AttackReady += () => attacks++;
            boss.Tick(100f, true, true);
            Assert.That(attacks, Is.Zero, "Engagement must first display a full telegraph.");
            boss.Tick(100f, true, true);
            Assert.That(attacks, Is.EqualTo(1));
            Assert.That(boss.State, Is.EqualTo(M4BossState.Recover));
            boss.Tick(100f, true, true);
            Assert.That(attacks, Is.EqualTo(1));
            Assert.That(boss.StateRemaining, Is.EqualTo(boss.Profile.TelegraphDuration));
        }

        [Test]
        public void LeashEscapeClearsHealthCoreAndPendingAttack()
        {
            var boss = Model();
            boss.Tick(0f, true, true);
            boss.ReceiveDamage(45f);
            boss.RegisterDirectHit(ElementType.Water);
            boss.Tick(.2f, false, false);
            Assert.That(boss.State, Is.EqualTo(M4BossState.Dormant));
            Assert.That(boss.HitPoints, Is.EqualTo(boss.Profile.MaximumHealth));
            Assert.That(boss.WeakHitCount, Is.Zero);
            Assert.That(boss.StateRemaining, Is.Zero);
        }

        [Test]
        public void DefeatIsOneShotAndWeeklyRestoreNeverIssuesAnotherRewardEvent()
        {
            var boss = Model();
            int deaths = 0, attacks = 0;
            boss.Defeated += () => deaths++;
            boss.AttackReady += () => attacks++;
            boss.Tick(0f, true, true);
            boss.ReceiveDamage(10000f);
            boss.ReceiveDamage(10000f);
            boss.Tick(100f, false, false);
            Assert.That(boss.HitPoints, Is.Zero);
            Assert.That(boss.State, Is.EqualTo(M4BossState.Defeated));
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(attacks, Is.Zero);
            boss.Reset(true);
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(boss.State, Is.EqualTo(M4BossState.Defeated));
            boss.Reset(false);
            Assert.That(boss.HitPoints, Is.EqualTo(boss.Profile.MaximumHealth));
            Assert.That(boss.State, Is.EqualTo(M4BossState.Dormant));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidTimeAndDamageCannotPoisonTheState(float invalid)
        {
            var boss = Model();
            Assert.Throws<ArgumentOutOfRangeException>(() => boss.Tick(invalid, true, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => boss.ReceiveDamage(invalid));
            Assert.That(boss.HitPoints, Is.EqualTo(boss.Profile.MaximumHealth));
        }

        [Test]
        public void ActualElementApplicationsUseThirdHitExposureAndDoNotDoubleCountHp()
        {
            SetupRuntime(out var manager, out var party, out var boss, out var vitals);
            ElementalActor source = party.ActiveMember.Actor;
            manager.Apply(boss.Actor, ElementType.Water, source, 10f);
            manager.Apply(boss.Actor, ElementType.Water, source, 10f);
            Assert.That(boss.HitPoints, Is.EqualTo(boss.MaximumHitPoints - 20f));
            manager.Apply(boss.Actor, ElementType.Water, source, 10f);
            Assert.That(boss.State, Is.EqualTo(M4BossState.Exposed));
            Assert.That(boss.Actor.DamageTaken, Is.EqualTo(40f));
            Assert.That(boss.HitPoints, Is.EqualTo(boss.MaximumHitPoints - 40f));
            manager.Apply(boss.Actor, ElementType.Fire, source, 10f); // Reverse vaporize: 10 * 1.5 * exposure 2.
            Assert.That(boss.Actor.DamageTaken, Is.EqualTo(70f));
            Assert.That(boss.HitPoints, Is.EqualTo(boss.MaximumHitPoints - 70f));
        }

        [Test]
        public void SwirlPropagationCannotAdvanceCoreWeakness()
        {
            SetupRuntime(out var manager, out var party, out var boss, out var vitals);
            var neighbor = Actor("Swirl origin", ActorTeam.Enemy, origin + Vector3.right);
            manager.Apply(neighbor, ElementType.Water, party.ActiveMember.Actor, 0f);
            manager.Apply(neighbor, ElementType.Wind, party.ActiveMember.Actor, 10f);
            Assert.That(boss.Actor.AuraElement, Is.EqualTo(ElementType.Water));
            Assert.That(boss.WeakHitCount, Is.Zero);
            Assert.That(boss.State, Is.EqualTo(M4BossState.Dormant));
        }

        [Test]
        public void EncounterResetCancelsDotAndWeeklyDefeatDisablesItsTarget()
        {
            SetupRuntime(out var manager, out var party, out var boss, out var vitals);
            manager.Apply(boss.Actor, ElementType.Water, party.ActiveMember.Actor);
            manager.Apply(boss.Actor, ElementType.Lightning, party.ActiveMember.Actor);
            Assert.That(manager.PendingElectroChargedCount, Is.EqualTo(1));
            boss.ResetEncounter(false);
            Assert.That(manager.PendingElectroChargedCount, Is.Zero);
            Assert.That(boss.Actor.DamageTaken, Is.Zero);
            Assert.That(boss.Actor.AuraElement, Is.EqualTo(ElementType.None));
            manager.Tick(3f);
            Assert.That(boss.HitPoints, Is.EqualTo(boss.MaximumHitPoints));
            boss.ResetEncounter(true);
            Assert.That(boss.Actor.IsOnField, Is.False);
            Assert.That(boss.GetComponent<Collider>().enabled, Is.False);
            manager.Apply(boss.Actor, ElementType.Fire, party.ActiveMember.Actor, 9999f);
            Assert.That(boss.HitPoints, Is.Zero);
            boss.ResetEncounter(false);
            Assert.That(boss.Actor.IsOnField, Is.True);
            Assert.That(boss.GetComponent<Collider>().enabled, Is.True);
        }

        [Test]
        public void BossAttackHonorsRadiusWallAndShieldBeforeSharedVitals()
        {
            SetupRuntime(out var manager, out var party, out var boss, out var vitals);
            party.transform.position = origin + Vector3.back * 2f;
            party.ActiveMember.Actor.GrantShield(ElementType.Fire, 5f);
            Physics.SyncTransforms();
            boss.Tick(0f);
            Assert.That(boss.TelegraphVisible, Is.True);
            boss.Tick(boss.Profile.TelegraphDuration);
            Assert.That(vitals.Health, Is.EqualTo(93f));
            boss.ResetEncounter(false);
            manager.ResetTarget(party.ActiveMember.Actor);
            vitals.Restore();
            GameObject wall = Make("Wall", origin + Vector3.back);
            wall.layer = 8;
            wall.AddComponent<BoxCollider>().size = new Vector3(6f, 5f, .2f);
            Physics.SyncTransforms();
            boss.Tick(0f);
            boss.Tick(boss.Profile.TelegraphDuration);
            Assert.That(vitals.Health, Is.EqualTo(100f));
            Object.DestroyImmediate(wall);
            boss.ResetEncounter(false);
            party.transform.position = origin + Vector3.back * 4f;
            Physics.SyncTransforms();
            boss.Tick(0f);
            boss.Tick(boss.Profile.TelegraphDuration);
            Assert.That(vitals.Health, Is.EqualTo(100f), "A player can leave the telegraphed radius before impact.");
        }

        [Test]
        public void PartySwitchDoesNotHealAndKnockoutOnlyFiresOnceUntilRestored()
        {
            SetupRuntime(out var manager, out var party, out var boss, out var vitals);
            int knockouts = 0;
            vitals.KnockedOut += () => knockouts++;
            manager.ApplyEnvironmentalDamage(party.ActiveMember.Actor, 30f);
            Assert.That(vitals.Health, Is.EqualTo(70f));
            Assert.That(party.TrySwitch(1), Is.True);
            Assert.That(vitals.Health, Is.EqualTo(70f));
            manager.ApplyEnvironmentalDamage(party.Members[0].Actor, 99f);
            Assert.That(vitals.Health, Is.EqualTo(70f));
            manager.ApplyEnvironmentalDamage(party.ActiveMember.Actor, 100f);
            manager.ApplyEnvironmentalDamage(party.ActiveMember.Actor, 100f);
            Assert.That(vitals.IsKnockedOut, Is.True);
            Assert.That(knockouts, Is.EqualTo(1));
            vitals.Restore(70f);
            Assert.That(vitals.Health, Is.EqualTo(70f));
            Assert.That(vitals.IsKnockedOut, Is.False);
            vitals.Restore(999f);
            Assert.That(vitals.Health, Is.EqualTo(100f));
            vitals.Restore(-1f);
            Assert.That(vitals.Health, Is.Zero);
            Assert.That(knockouts, Is.EqualTo(1), "Restoring a snapshot is not a new damage/knockout event.");
        }

        [Test]
        public void ReconfigurationDoesNotStackTheBossDamageAdapter()
        {
            SetupRuntime(out var manager, out var party, out var boss, out var vitals);
            manager.Apply(boss.Actor, ElementType.Water, party.ActiveMember.Actor);
            manager.Apply(boss.Actor, ElementType.Water, party.ActiveMember.Actor);
            manager.Apply(boss.Actor, ElementType.Water, party.ActiveMember.Actor);
            Assert.That(boss.State, Is.EqualTo(M4BossState.Exposed));
            boss.Configure(M4RegionId.Agnia, ElementType.Fire, ElementType.Water,
                manager, party, party.transform, true);
            Assert.That(boss.Actor.IncomingDamageScale(ElementType.Water), Is.EqualTo(1f));
            boss.ResetEncounter(true);
            boss.Configure(M4RegionId.Agnia, ElementType.Fire, ElementType.Water,
                manager, party, party.transform, true);
            Assert.That(boss.GetComponent<Collider>().enabled, Is.True);
            manager.Apply(boss.Actor, ElementType.Water, party.ActiveMember.Actor, 10f);
            Assert.That(boss.Actor.DamageTaken, Is.EqualTo(10f));
            Assert.That(boss.HitPoints, Is.EqualTo(boss.MaximumHitPoints - 10f));
        }
        private static M4BossModel Model(bool exposure = true)
            => new M4BossModel(M4BossProfile.ForElement(ElementType.Fire), ElementType.Water, exposure);

        private void SetupRuntime(out ElementalReactionManager manager, out PartyManager party,
            out M4FieldBoss boss, out M4PlayerVitals vitals)
        {
            manager = Make("M4 test reactions", origin).AddComponent<ElementalReactionManager>();
            manager.AutoTick = false;
            GameObject player = Make("M4 test player", origin + Vector3.back * 2f);
            var input = player.AddComponent<M0Input>();
            var motor = player.AddComponent<PlayerMotor>();
            var controller = player.GetComponent<CharacterController>();
            controller.center = Vector3.up; controller.height = 2f; controller.radius = .35f;
            var combo = player.AddComponent<BasicAttackCombo>();
            combo.Configure(null);
            motor.Configure(input, player.transform, null, combo);
            var members = new PartyMember[4];
            for (int i = 0; i < members.Length; i++)
            {
                ElementalActor actor = Actor("Member " + i, ActorTeam.Player, player.transform.position);
                actor.transform.SetParent(player.transform, true);
                members[i] = new PartyMember("Member " + i, actor);
            }
            party = player.AddComponent<PartyManager>();
            party.Configure(input, motor, combo, manager, members);
            vitals = player.AddComponent<M4PlayerVitals>();
            vitals.Configure(manager, party);
            GameObject target = Make("M4 test boss", origin);
            target.layer = 9;
            var collider = target.AddComponent<CapsuleCollider>();
            collider.center = Vector3.up; collider.height = 2f;
            boss = target.AddComponent<M4FieldBoss>();
            boss.AutoTick = false;
            boss.Configure(M4RegionId.Agnia, ElementType.Fire, ElementType.Water, manager, party, player.transform, true);
            boss.SetPlayerVitals(vitals);
            Physics.SyncTransforms();
        }

        private ElementalActor Actor(string name, ActorTeam team, Vector3 position)
        {
            ElementalActor actor = Make(name, position).AddComponent<ElementalActor>();
            actor.Configure(name, ElementType.Fire, team);
            return actor;
        }
        private GameObject Make(string name, Vector3 position)
        {
            var item = new GameObject(name); item.transform.position = position; objects.Add(item); return item;
        }
    }
}


