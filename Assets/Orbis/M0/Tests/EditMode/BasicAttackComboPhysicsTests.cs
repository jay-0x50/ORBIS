using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Orbis.M0.Tests
{
    public sealed class BasicAttackComboPhysicsTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        // 열린 씬의 일반 테스트 오브젝트들과 겹치지 않는 위치에서 물리 판정을 검증한다.
        private readonly Vector3 origin = new Vector3(10000f, 0f, 10000f);
        private BasicAttackCombo combo;

        [SetUp]
        public void SetUp()
        {
            GameObject player = MakeObject("Test Player", origin, 0);
            combo = player.AddComponent<BasicAttackCombo>();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(objects[i]);
            objects.Clear();
            Physics.SyncTransforms();
        }

        [Test]
        public void MultipleCollidersAndMultipleActiveFramesCountOnlyOneHitPerStrike()
        {
            TrainingDummy dummy = MakeDummy();
            GameObject extraCollider = MakeObject("Second Collider", dummy.transform.position, 9);
            extraCollider.transform.SetParent(dummy.transform, true);
            extraCollider.AddComponent<SphereCollider>().radius = 0.25f;
            Physics.SyncTransforms();

            combo.RequestAttack(true);
            combo.Tick(0.18f);
            combo.Tick(0.03f);
            combo.Tick(0.03f);
            Assert.That(dummy.HitCount, Is.EqualTo(1));
            Assert.That(combo.TotalHitCount, Is.EqualTo(1));
        }

        [Test]
        public void NextStrikeCanHitTheSameTargetAgain()
        {
            TrainingDummy dummy = MakeDummy();
            Physics.SyncTransforms();
            combo.RequestAttack(true);
            combo.Tick(0.35f);
            combo.RequestAttack(true);
            combo.Tick(0.36f);
            Assert.That(combo.CurrentStep, Is.EqualTo(2));
            Assert.That(dummy.HitCount, Is.EqualTo(2));
            Assert.That(dummy.LastComboStep, Is.EqualTo(2));
        }

        [Test]
        public void LongFrameDoesNotSkipTheHit()
        {
            TrainingDummy dummy = MakeDummy();
            Physics.SyncTransforms();
            combo.RequestAttack(true);
            combo.Tick(0.8f);
            Assert.That(combo.IsAttacking, Is.False);
            Assert.That(dummy.HitCount, Is.EqualTo(1));
        }

        [Test]
        public void WorldWallBetweenPlayerAndDummyBlocksTheHit()
        {
            TrainingDummy dummy = MakeDummy();
            GameObject wall = MakeObject("World Wall", origin + new Vector3(0f, 0.9f, 0.5f), 8);
            wall.AddComponent<BoxCollider>().size = new Vector3(2f, 2f, 0.1f);
            Physics.SyncTransforms();
            combo.RequestAttack(true);
            combo.Tick(0.5f);
            Assert.That(dummy.HitCount, Is.Zero);
            Assert.That(combo.TotalHitCount, Is.Zero);
        }

        [Test]
        public void AirborneAttackNeverHits()
        {
            TrainingDummy dummy = MakeDummy();
            Physics.SyncTransforms();
            combo.RequestAttack(false);
            combo.Tick(1f);
            Assert.That(combo.IsAttacking, Is.False);
            Assert.That(dummy.HitCount, Is.Zero);
        }

        private TrainingDummy MakeDummy()
        {
            GameObject target = MakeObject("Test Dummy", origin + new Vector3(0f, 0.9f, 1.1f), 9);
            target.AddComponent<BoxCollider>().size = new Vector3(0.4f, 1.6f, 0.4f);
            return target.AddComponent<TrainingDummy>();
        }

        private GameObject MakeObject(string name, Vector3 position, int layer)
        {
            var instance = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = layer };
            instance.transform.position = position;
            objects.Add(instance);
            return instance;
        }
    }
}

