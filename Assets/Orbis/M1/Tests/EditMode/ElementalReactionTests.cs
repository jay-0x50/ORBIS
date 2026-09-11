using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Orbis.M1.Tests
{
    public sealed class ElementalReactionTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly Vector3 origin = new Vector3(16000f, 0f, 16000f);
        private ElementalReactionManager manager;
        private ElementalActor source;
        private ElementalActor target;

        [SetUp]
        public void SetUp()
        {
            manager = MakeObject("Reaction Manager", origin).AddComponent<ElementalReactionManager>();
            manager.AutoTick = false;
            source = MakeActor("source", ActorTeam.Player, origin + Vector3.back * 4f);
            target = MakeActor("target", ActorTeam.Enemy, origin);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            Physics.SyncTransforms();
        }

        [TestCase(ElementType.Fire, ElementType.Water, ReactionType.Vaporize)]
        [TestCase(ElementType.Water, ElementType.Fire, ReactionType.Vaporize)]
        [TestCase(ElementType.Water, ElementType.Lightning, ReactionType.ElectroCharged)]
        [TestCase(ElementType.Lightning, ElementType.Water, ReactionType.ElectroCharged)]
        [TestCase(ElementType.Fire, ElementType.Lightning, ReactionType.Overload)]
        [TestCase(ElementType.Lightning, ElementType.Fire, ReactionType.Overload)]
        [TestCase(ElementType.Wind, ElementType.Fire, ReactionType.Swirl)]
        [TestCase(ElementType.Fire, ElementType.Wind, ReactionType.Swirl)]
        [TestCase(ElementType.Wind, ElementType.Water, ReactionType.Swirl)]
        [TestCase(ElementType.Water, ElementType.Wind, ReactionType.Swirl)]
        [TestCase(ElementType.Wind, ElementType.Lightning, ReactionType.Swirl)]
        [TestCase(ElementType.Lightning, ElementType.Wind, ReactionType.Swirl)]
        [TestCase(ElementType.Rock, ElementType.Fire, ReactionType.Crystallize)]
        [TestCase(ElementType.Fire, ElementType.Rock, ReactionType.Crystallize)]
        [TestCase(ElementType.Rock, ElementType.Water, ReactionType.Crystallize)]
        [TestCase(ElementType.Water, ElementType.Rock, ReactionType.Crystallize)]
        [TestCase(ElementType.Rock, ElementType.Wind, ReactionType.Crystallize)]
        [TestCase(ElementType.Wind, ElementType.Rock, ReactionType.Crystallize)]
        [TestCase(ElementType.Rock, ElementType.Lightning, ReactionType.Crystallize)]
        [TestCase(ElementType.Lightning, ElementType.Rock, ReactionType.Crystallize)]
        public void ReactionPairsWorkInBothOrders(ElementType first, ElementType second, ReactionType expected)
        {
            manager.Apply(target, first, source, 0f);
            Assert.That(manager.Apply(target, second, source, 0f), Is.EqualTo(expected));
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.None));
        }

        [TestCase(ElementType.Fire, ElementType.Water, 20f)]
        [TestCase(ElementType.Water, ElementType.Fire, 15f)]
        public void VaporizeUsesIncomingElementMultiplierAndConsumesAura(ElementType first, ElementType incoming, float damage)
        {
            int reactions = 0;
            manager.Reacted += _ => reactions++;
            manager.Apply(target, first, source, 0f);
            manager.Apply(target, incoming, source, 10f);
            Assert.That(target.DamageTaken, Is.EqualTo(damage));
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.None));
            manager.Apply(target, incoming, source, 10f);
            Assert.That(reactions, Is.EqualTo(1), "Consumed aura must not cause a second reaction.");
            Assert.That(target.DamageTaken, Is.EqualTo(damage + 10f));
        }

        [Test]
        public void SameElementRefreshesDurationAndOwnerWithoutReaction()
        {
            ElementalActor otherSource = MakeActor("other source", ActorTeam.Player, origin + Vector3.left * 4f);
            int reactions = 0;
            manager.Reacted += _ => reactions++;
            manager.Apply(target, ElementType.Fire, source, 10f, 2f);
            manager.Tick(1f);
            manager.Apply(target, ElementType.Fire, otherSource, 10f, 4f);
            Assert.That(target.AuraRemainingTime, Is.EqualTo(4f));
            Assert.That(target.AuraSourceId, Is.EqualTo(otherSource.SourceId));
            Assert.That(target.DamageTaken, Is.EqualTo(20f));
            Assert.That(reactions, Is.Zero);
        }

        [Test]
        public void RetainChangesOnlyLivingMatchingSourceAttachmentsToExactlyFourSeconds()
        {
            ElementalActor other = MakeActor("other target", ActorTeam.Enemy, origin + Vector3.right * 8f);
            ElementalActor otherSource = MakeActor("other source", ActorTeam.Player, origin + Vector3.left * 4f);
            manager.Apply(target, ElementType.Fire, source, 0f, 8f);
            manager.Apply(other, ElementType.Water, otherSource, 0f, 8f);
            manager.Tick(1f);
            Assert.That(manager.RetainSourceAttachments(source.SourceId, 4f), Is.EqualTo(1));
            Assert.That(target.AuraRemainingTime, Is.EqualTo(4f));
            Assert.That(other.AuraRemainingTime, Is.EqualTo(7f));
            manager.Tick(4f);
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.None));
            Assert.That(manager.RetainSourceAttachments(source.SourceId), Is.Zero);
            Assert.That(target.AuraSourceId, Is.Empty);
        }

        [Test]
        public void ConsumedAuraCannotBeResurrectedBySourceRetention()
        {
            manager.Apply(target, ElementType.Fire, source, 0f);
            manager.Apply(target, ElementType.Water, source, 0f);
            Assert.That(manager.RetainSourceAttachments(source.SourceId), Is.Zero);
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.None));
        }

        [Test]
        public void OffFieldActorsStillExpireAuraAndShield()
        {
            manager.Apply(source, ElementType.Water, target, 0f, 2f);
            source.GrantShield(ElementType.Fire, 25f, 1f);
            source.IsOnField = false;
            manager.Tick(1.1f);
            Assert.That(source.AuraRemainingTime, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(source.ShieldAmount, Is.Zero);
            manager.Tick(1f);
            Assert.That(source.AuraElement, Is.EqualTo(ElementType.None));
        }

        [Test]
        public void FriendlyAndOffFieldActorsCannotReceiveNewApplications()
        {
            ElementalActor ally = MakeActor("ally", ActorTeam.Player, origin + Vector3.right);
            int applications = 0;
            manager.Applied += _ => applications++;
            manager.Apply(ally, ElementType.Fire, source);
            target.IsOnField = false;
            manager.Apply(target, ElementType.Fire, source);
            target.IsOnField = true;
            source.IsOnField = false;
            manager.Apply(target, ElementType.Fire, source);
            Assert.That(applications, Is.Zero);
            Assert.That(target.DamageTaken, Is.Zero);
            Assert.That(ally.AuraElement, Is.EqualTo(ElementType.None));
        }

        [Test]
        public void ElectroChargedTicksThreeTimesAndChainsOneHopWithoutNewReactions()
        {
            ElementalActor neighbour = MakeActor("neighbour", ActorTeam.Enemy, origin + Vector3.right * 2f);
            ElementalActor twoHops = MakeActor("two hops", ActorTeam.Enemy, origin + Vector3.right * 4.5f);
            ElementalActor ally = MakeActor("ally", ActorTeam.Player, origin + Vector3.left);
            ElementalActor offField = MakeActor("off field", ActorTeam.Enemy, origin + Vector3.back);
            offField.IsOnField = false;
            int reactions = 0;
            manager.Reacted += _ => reactions++;
            manager.Apply(target, ElementType.Water, source, 0f);
            manager.Apply(target, ElementType.Lightning, source, 10f);
            source.IsOnField = false; // 전환해도 기존 감전은 계속 진행한다.
            manager.Tick(10f);
            Assert.That(target.DamageTaken, Is.EqualTo(22f));
            Assert.That(neighbour.DamageTaken, Is.EqualTo(6f));
            Assert.That(twoHops.DamageTaken, Is.Zero);
            Assert.That(ally.DamageTaken, Is.Zero);
            Assert.That(offField.DamageTaken, Is.Zero);
            Assert.That(reactions, Is.EqualTo(1));
            Assert.That(manager.PendingElectroChargedCount, Is.Zero);
            Assert.That(neighbour.AuraElement, Is.EqualTo(ElementType.None));
        }

        [Test]
        public void ReapplyingElectroChargedRefreshesInsteadOfStacking()
        {
            manager.Apply(target, ElementType.Water, source, 0f);
            manager.Apply(target, ElementType.Lightning, source, 10f);
            manager.Tick(0.5f);
            manager.Apply(target, ElementType.Water, source, 0f);
            manager.Apply(target, ElementType.Lightning, source, 10f);
            Assert.That(manager.PendingElectroChargedCount, Is.EqualTo(1));
            manager.Tick(3f);
            Assert.That(target.DamageTaken, Is.EqualTo(32f));
        }

        [Test]
        public void DestroyedSourceCancelsPendingDamageWithoutErrors()
        {
            manager.Apply(target, ElementType.Water, source, 0f);
            manager.Apply(target, ElementType.Lightning, source, 10f);
            Object.DestroyImmediate(source.gameObject);
            manager.Tick(3f);
            Assert.That(target.DamageTaken, Is.EqualTo(10f));
            Assert.That(manager.PendingElectroChargedCount, Is.Zero);
        }

        [Test]
        public void SwirlReplacesNeighbourAuraWithoutRecursivelyTriggeringOverload()
        {
            ElementalActor neighbour = MakeActor("neighbour", ActorTeam.Enemy, origin + Vector3.right * 2f);
            ElementalActor ally = MakeActor("ally", ActorTeam.Player, origin + Vector3.left);
            manager.Apply(neighbour, ElementType.Lightning, source, 0f);
            manager.Apply(target, ElementType.Fire, source, 0f);
            int reactions = 0;
            int propagationEvents = 0;
            manager.Reacted += _ => reactions++;
            manager.Applied += evt => { if (evt.IsPropagation) propagationEvents++; };
            manager.Apply(target, ElementType.Wind, source, 10f);
            Assert.That(reactions, Is.EqualTo(1));
            Assert.That(propagationEvents, Is.EqualTo(1));
            Assert.That(neighbour.AuraElement, Is.EqualTo(ElementType.Fire));
            Assert.That(neighbour.AuraSourceId, Is.EqualTo(source.SourceId));
            Assert.That(neighbour.DamageTaken, Is.EqualTo(5f));
            Assert.That(ally.DamageTaken, Is.Zero);
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.None));
        }

        [TestCase(ElementType.Rock, ElementType.Wind)]
        [TestCase(ElementType.Wind, ElementType.Rock)]
        public void WindRockCrystallizationGrantsWindShieldToSource(ElementType first, ElementType incoming)
        {
            manager.Apply(target, first, source, 0f);
            manager.Apply(target, incoming, source, 0f);
            Assert.That(source.ShieldElement, Is.EqualTo(ElementType.Wind));
            Assert.That(source.ShieldAmount, Is.EqualTo(25f));
            Assert.That(source.ShieldRemainingTime, Is.EqualTo(6f));
            Assert.That(target.ShieldAmount, Is.Zero);
        }

        [Test]
        public void ShieldAbsorbsThenRecordsOnlyUnabsorbedDamage()
        {
            source.GrantShield(ElementType.Fire, 25f, 6f);
            manager.Apply(source, ElementType.Water, target, 10f);
            Assert.That(source.DamageTaken, Is.Zero);
            Assert.That(source.ShieldAmount, Is.EqualTo(15f));
            manager.Apply(source, ElementType.Water, target, 30f);
            Assert.That(source.DamageTaken, Is.EqualTo(15f));
            Assert.That(source.TotalShieldAbsorbed, Is.EqualTo(25f));
            Assert.That(source.ShieldElement, Is.EqualTo(ElementType.None));
        }

        [Test]
        public void LargeTickPreservesShieldExpiryOrderAroundDotTicks()
        {
            manager.Apply(target, ElementType.Water, source, 0f);
            manager.Apply(target, ElementType.Lightning, source, 10f);
            target.GrantShield(ElementType.Lightning, 10f, 1.5f);
            manager.Tick(3f);
            Assert.That(target.TotalShieldAbsorbed, Is.EqualTo(4f));
            Assert.That(target.DamageTaken, Is.EqualTo(18f));
            Assert.That(target.ShieldAmount, Is.Zero);
        }

        [Test]
        public void OverloadAddsAreaDamageOnceAndFiltersAlliesAndOffFieldActors()
        {
            ElementalActor neighbour = MakeActor("neighbour", ActorTeam.Enemy, origin + Vector3.right * 2f);
            ElementalActor ally = MakeActor("ally", ActorTeam.Player, origin + Vector3.left);
            ElementalActor offField = MakeActor("off field", ActorTeam.Enemy, origin + Vector3.back);
            offField.IsOnField = false;
            manager.Apply(target, ElementType.Fire, source, 0f);
            manager.Apply(target, ElementType.Lightning, source, 10f);
            Assert.That(target.DamageTaken, Is.EqualTo(18f));
            Assert.That(neighbour.DamageTaken, Is.EqualTo(8f));
            Assert.That(ally.DamageTaken, Is.Zero);
            Assert.That(offField.DamageTaken, Is.Zero);
            Assert.That(target.LastKnockback.magnitude, Is.EqualTo(1.5f).Within(0.01f));
        }

        [Test]
        public void OverloadKnockbackStopsBeforeWorldWall()
        {
            GameObject wall = MakeObject("wall", origin + new Vector3(0f, 0.9f, 0.9f));
            wall.layer = 8;
            wall.AddComponent<BoxCollider>().size = new Vector3(3f, 2f, 0.2f);
            Physics.SyncTransforms();
            manager.Apply(target, ElementType.Fire, source, 0f);
            manager.Apply(target, ElementType.Lightning, source, 10f);
            Assert.That(target.LastKnockback.z, Is.GreaterThan(0f));
            Assert.That(target.LastKnockback.z, Is.LessThan(0.6f));
            Assert.That(target.transform.position.z, Is.LessThan(origin.z + 0.6f));
        }

        [Test]
        public void TargetResetRemovesPendingDot()
        {
            manager.Apply(target, ElementType.Water, source, 0f);
            manager.Apply(target, ElementType.Lightning, source, 10f);
            manager.ResetTarget(target);
            manager.Tick(4f);
            Assert.That(target.DamageTaken, Is.Zero);
            Assert.That(target.AuraElement, Is.EqualTo(ElementType.None));
            Assert.That(manager.PendingElectroChargedCount, Is.Zero);
        }

        private ElementalActor MakeActor(string id, ActorTeam team, Vector3 position)
        {
            GameObject item = MakeObject(id, position);
            item.layer = team == ActorTeam.Enemy ? 9 : 10;
            var collider = item.AddComponent<CapsuleCollider>();
            collider.center = Vector3.up * 0.9f;
            collider.height = 1.8f;
            collider.radius = 0.3f;
            var actor = item.AddComponent<ElementalActor>();
            actor.Configure(id, ElementType.Fire, team);
            Physics.SyncTransforms();
            return actor;
        }

        private GameObject MakeObject(string name, Vector3 position)
        {
            var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
            item.transform.position = position;
            objects.Add(item);
            return item;
        }
    }
}
