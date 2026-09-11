using System.Collections.Generic;
using NUnit.Framework;
using Orbis.M1;
using UnityEngine;

namespace Orbis.M2.Tests
{
    public sealed class ContentTests
    {
        [Test]
        public void PuzzleRequiresThreeOrderedFireHitsBeforeReward()
        {
            var inventory = new RewardInventory();
            var puzzle = new OrderedFirePuzzleModel(inventory, "field");
            Assert.That(puzzle.TryClaimReward(), Is.False);
            puzzle.RegisterHit(0, ElementType.Fire);
            puzzle.RegisterHit(1, ElementType.Fire);
            Assert.That(puzzle.IsUnlocked, Is.False);
            puzzle.RegisterHit(2, ElementType.Fire);
            Assert.That(puzzle.IsUnlocked, Is.True);
            Assert.That(puzzle.TryClaimReward(), Is.True);
            Assert.That(inventory.EnhancementMaterials, Is.EqualTo(5));
        }

        [Test]
        public void RepeatedFireComboHitsOnLitStatueDoNotResetProgress()
        {
            var puzzle = new OrderedFirePuzzleModel(new RewardInventory(), "field");
            puzzle.RegisterHit(0, ElementType.Fire);
            puzzle.RegisterHit(0, ElementType.Fire);
            puzzle.RegisterHit(0, ElementType.Fire);
            Assert.That(puzzle.LitCount, Is.EqualTo(1));
            puzzle.RegisterHit(1, ElementType.Fire);
            puzzle.RegisterHit(0, ElementType.Fire);
            Assert.That(puzzle.LitCount, Is.EqualTo(2));
        }

        [Test]
        public void WrongOrderOrElementResetsUnfinishedPuzzle()
        {
            var puzzle = new OrderedFirePuzzleModel(new RewardInventory(), "field");
            puzzle.RegisterHit(0, ElementType.Fire);
            puzzle.RegisterHit(2, ElementType.Fire);
            Assert.That(puzzle.LitCount, Is.Zero);
            puzzle.RegisterHit(0, ElementType.Fire);
            puzzle.RegisterHit(1, ElementType.Water);
            Assert.That(puzzle.LitCount, Is.Zero);
        }

        [Test]
        public void PropagationAndUnknownTargetsNeverAdvanceOrResetPuzzle()
        {
            var puzzle = new OrderedFirePuzzleModel(new RewardInventory(), "field");
            puzzle.RegisterHit(0, ElementType.Fire);
            puzzle.RegisterHit(1, ElementType.Fire, true);
            puzzle.RegisterHit(1, ElementType.Water, true);
            puzzle.RegisterHit(-1, ElementType.Water);
            puzzle.RegisterHit(99, ElementType.Fire);
            Assert.That(puzzle.LitCount, Is.EqualTo(1));
        }

        [Test]
        public void ClaimedRewardCannotBeRepeatedByResetOrSceneReconstruction()
        {
            var inventory = new RewardInventory();
            var first = new OrderedFirePuzzleModel(inventory, "field");
            for (int i = 0; i < 3; i++) first.RegisterHit(i, ElementType.Fire);
            Assert.That(first.TryClaimReward(), Is.True);
            Assert.That(first.TryClaimReward(), Is.False);
            first.ResetProgress();
            Assert.That(first.TryClaimReward(), Is.False);
            var reconstructed = new OrderedFirePuzzleModel(inventory, "field");
            Assert.That(reconstructed.RewardClaimed, Is.True);
            Assert.That(reconstructed.IsUnlocked, Is.True);
            Assert.That(reconstructed.TryClaimReward(), Is.False);
            Assert.That(inventory.EnhancementMaterials, Is.EqualTo(5));
        }

        [Test]
        public void IndependentContentRewardsShareOneSessionCounter()
        {
            var inventory = new RewardInventory();
            Assert.That(inventory.TryGrant("field", 5), Is.True);
            Assert.That(inventory.TryGrant("challenge", 3), Is.True);
            Assert.That(inventory.TryGrant("field", 5), Is.False);
            Assert.That(inventory.EnhancementMaterials, Is.EqualTo(8));
        }

        [Test]
        public void ActualElementApplicationsDrivePuzzleButSwirlPropagationDoesNot()
        {
            var objects = new List<GameObject>();
            try
            {
                Vector3 origin = new Vector3(22000f, 0f, 22000f);
                var managerObject = new GameObject("Content Test Manager");
                objects.Add(managerObject);
                var manager = managerObject.AddComponent<ElementalReactionManager>();
                manager.AutoTick = false;
                ElementalActor player = MakeActor(objects, "Player", origin + Vector3.back * 5f, ActorTeam.Player);
                var statues = new[]
                {
                    MakeActor(objects, "First", origin, ActorTeam.Enemy),
                    MakeActor(objects, "Second", origin + Vector3.right * 2f, ActorTeam.Enemy),
                    MakeActor(objects, "Third", origin + Vector3.right * 4f, ActorTeam.Enemy)
                };
                var puzzle = managerObject.AddComponent<FieldElementPuzzle>();
                var inventory = new RewardInventory();
                puzzle.Configure(manager, statues, inventory, "field");
                ElementalActor unrelated = MakeActor(objects, "Unrelated", origin + Vector3.forward, ActorTeam.Enemy);
                manager.Apply(unrelated, ElementType.Fire, player, 0f);
                manager.Apply(unrelated, ElementType.Wind, player, 0f);
                Assert.That(statues[0].AuraElement, Is.EqualTo(ElementType.Fire));
                Assert.That(puzzle.Model.LitCount, Is.Zero, "Swirl aura is not a direct statue input.");
                for (int i = 0; i < 3; i++) manager.Apply(statues[i], ElementType.Fire, player, 10f);
                Assert.That(puzzle.Model.IsUnlocked, Is.True);
                Assert.That(puzzle.TryClaimReward(), Is.True);
                Assert.That(inventory.EnhancementMaterials, Is.EqualTo(5));
            }
            finally
            {
                for (int i = objects.Count - 1; i >= 0; i--)
                    if (objects[i] != null) Object.DestroyImmediate(objects[i]);
                Physics.SyncTransforms();
            }
        }

        [Test]
        public void ChallengeCompletesOnlyAfterThreeDistinctVaporizeTargets()
        {
            var inventory = new RewardInventory();
            var challenge = new ChallengeRoomModel(inventory, "challenge");
            Assert.That(challenge.RegisterReaction(0, ReactionType.Vaporize), Is.False);
            Assert.That(challenge.TryStart(), Is.True);
            Assert.That(challenge.TryStart(), Is.False);
            Assert.That(challenge.RegisterReaction(0, ReactionType.Overload), Is.False);
            Assert.That(challenge.RegisterReaction(0, ReactionType.Vaporize), Is.True);
            Assert.That(challenge.RegisterReaction(0, ReactionType.Vaporize), Is.False);
            challenge.RegisterReaction(1, ReactionType.Vaporize);
            Assert.That(challenge.State, Is.EqualTo(ChallengeState.Running));
            Assert.That(challenge.TryClaimReward(), Is.False);
            challenge.RegisterReaction(2, ReactionType.Vaporize);
            Assert.That(challenge.State, Is.EqualTo(ChallengeState.Completed));
            Assert.That(inventory.EnhancementMaterials, Is.Zero, "Completion unlocks F claim instead of granting automatically.");
            Assert.That(challenge.TryClaimReward(), Is.True);
            Assert.That(inventory.EnhancementMaterials, Is.EqualTo(3));
        }

        [Test]
        public void TimeoutRejectsLateReactionsAndRetryRestoresAllTargets()
        {
            var challenge = new ChallengeRoomModel(new RewardInventory(), "challenge");
            challenge.TryStart();
            challenge.RegisterReaction(0, ReactionType.Vaporize);
            challenge.Tick(60f);
            Assert.That(challenge.State, Is.EqualTo(ChallengeState.Failed));
            Assert.That(challenge.RemainingTime, Is.Zero);
            Assert.That(challenge.RegisterReaction(1, ReactionType.Vaporize), Is.False);
            Assert.That(challenge.TryClaimReward(), Is.False);
            Assert.That(challenge.TryStart(), Is.True);
            Assert.That(challenge.DefeatedCount, Is.Zero);
            Assert.That(challenge.RemainingTime, Is.EqualTo(60f));
            Assert.That(challenge.IsDefeated(0), Is.False);
        }

        [Test]
        public void ChallengeCompletionStopsTheCountdown()
        {
            var challenge = new ChallengeRoomModel(new RewardInventory(), "challenge");
            challenge.TryStart();
            challenge.Tick(10f);
            for (int i = 0; i < 3; i++) challenge.RegisterReaction(i, ReactionType.Vaporize);
            challenge.Tick(100f);
            Assert.That(challenge.State, Is.EqualTo(ChallengeState.Completed));
            Assert.That(challenge.RemainingTime, Is.EqualTo(50f));
        }

        [Test]
        public void ChallengeRewardCannotRepeatAfterResetReplayOrSceneReconstruction()
        {
            var inventory = new RewardInventory();
            var challenge = new ChallengeRoomModel(inventory, "challenge");
            challenge.TryStart();
            for (int i = 0; i < 3; i++) challenge.RegisterReaction(i, ReactionType.Vaporize);
            Assert.That(challenge.TryClaimReward(), Is.True);
            challenge.ResetProgress();
            challenge.TryStart();
            for (int i = 0; i < 3; i++) challenge.RegisterReaction(i, ReactionType.Vaporize);
            Assert.That(challenge.TryClaimReward(), Is.False);
            var reconstructed = new ChallengeRoomModel(inventory, "challenge");
            Assert.That(reconstructed.State, Is.EqualTo(ChallengeState.Completed));
            Assert.That(reconstructed.TryClaimReward(), Is.False);
            Assert.That(inventory.EnhancementMaterials, Is.EqualTo(3));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidChallengeTimeDoesNotCorruptCountdown(float deltaTime)
        {
            var challenge = new ChallengeRoomModel(new RewardInventory(), "challenge");
            challenge.TryStart();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => challenge.Tick(deltaTime));
            Assert.That(challenge.RemainingTime, Is.EqualTo(60f));
        }

        [Test]
        public void ActualVaporizeDefeatsTargetsAfterDamageAndFailureRetryRestoresColliders()
        {
            var objects = new List<GameObject>();
            try
            {
                Vector3 origin = new Vector3(23000f, 0f, 23000f);
                var managerObject = new GameObject("Challenge Test Manager");
                objects.Add(managerObject);
                var manager = managerObject.AddComponent<ElementalReactionManager>();
                manager.AutoTick = false;
                ElementalActor player = MakeActor(objects, "Player", origin + Vector3.back * 5f, ActorTeam.Player);
                var targets = new[]
                {
                    MakeActor(objects, "First", origin, ActorTeam.Enemy),
                    MakeActor(objects, "Second", origin + Vector3.right * 4f, ActorTeam.Enemy),
                    MakeActor(objects, "Third", origin + Vector3.right * 8f, ActorTeam.Enemy)
                };
                foreach (ElementalActor target in targets) target.gameObject.AddComponent<BoxCollider>();
                var room = managerObject.AddComponent<ChallengeRoom>();
                room.AutoTick = false;
                var inventory = new RewardInventory();
                room.Configure(manager, targets, inventory, "challenge");
                Assert.That(targets[0].IsOnField, Is.False);
                Assert.That(room.TryStart(), Is.True);
                Assert.That(targets[0].GetComponent<BoxCollider>().enabled, Is.True);
                manager.Apply(targets[0], ElementType.Fire, player, 10f);
                manager.Apply(targets[0], ElementType.Water, player, 10f);
                Assert.That(room.DefeatedCount, Is.EqualTo(1));
                Assert.That(targets[0].DamageTaken, Is.EqualTo(30f), "The final vaporized damage must be recorded before target closure.");
                Assert.That(targets[0].IsOnField, Is.False);
                Assert.That(targets[0].GetComponent<BoxCollider>().enabled, Is.False);
                room.Tick(60f);
                Assert.That(room.State, Is.EqualTo(ChallengeState.Failed));
                Assert.That(targets[1].IsOnField, Is.False);
                Assert.That(targets[1].GetComponent<BoxCollider>().enabled, Is.False);
                room.TryStart();
                Assert.That(targets[0].IsOnField, Is.True);
                Assert.That(targets[0].DamageTaken, Is.Zero);
                Assert.That(targets[0].GetComponent<BoxCollider>().enabled, Is.True);
                for (int i = 0; i < 3; i++)
                {
                    manager.Apply(targets[i], ElementType.Water, player, 10f);
                    manager.Apply(targets[i], ElementType.Fire, player, 10f);
                }
                Assert.That(room.State, Is.EqualTo(ChallengeState.Completed));
                Assert.That(room.TryClaimReward(), Is.True);
                Assert.That(inventory.EnhancementMaterials, Is.EqualTo(3));
                room.ResetChallenge();
                room.TryStart();
                Assert.That(targets[2].IsOnField, Is.True);
                Assert.That(room.RewardClaimed, Is.True);
            }
            finally
            {
                for (int i = objects.Count - 1; i >= 0; i--)
                    if (objects[i] != null) Object.DestroyImmediate(objects[i]);
                Physics.SyncTransforms();
            }
        }
        private static ElementalActor MakeActor(List<GameObject> objects, string id, Vector3 position, ActorTeam team)
        {
            var item = new GameObject(id);
            objects.Add(item);
            item.transform.position = position;
            var actor = item.AddComponent<ElementalActor>();
            actor.Configure(id, ElementType.Fire, team);
            return actor;
        }
    }
}

