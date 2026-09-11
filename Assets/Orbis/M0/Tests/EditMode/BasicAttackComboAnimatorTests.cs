using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace Orbis.M0.Tests
{
    public sealed class BasicAttackComboAnimatorTests
    {
        private GameObject player;
        private Animator animator;
        private BasicAttackCombo combo;
        private readonly List<Object> transientAssets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("Animator Clock Test");
            player.transform.position = new Vector3(12000f, 0f, 12000f);
            combo = player.AddComponent<BasicAttackCombo>();
            animator = player.AddComponent<Animator>();
            var controller = new AnimatorController();
            transientAssets.Add(controller);
            controller.AddLayer("Base Layer");
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            transientAssets.Add(machine);
            for (int step = 1; step <= 3; step++)
            {
                var clip = new AnimationClip { name = "Attack" + step, legacy = false };
                clip.SetCurve("", typeof(Transform), "localPosition.y",
                    AnimationCurve.Linear(0f, 0f, ComboSequence.GetTiming(step).Duration, 0f));
                transientAssets.Add(clip);
                AnimatorState state = machine.AddState("Attack" + step);
                state.motion = clip;
                transientAssets.Add(state);
            }
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            animator.Update(0f);
            animator.speed = 1.3f;
            combo.Configure(animator);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(player);
            for (int i = transientAssets.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(transientAssets[i]);
            transientAssets.Clear();
        }

        [Test]
        public void AttackPausesAutomaticClockAndCompletionRestoresItsPreviousSpeed()
        {
            combo.RequestAttack(true);
            Assert.That(animator.speed, Is.Zero);
            combo.Tick(1f);
            Assert.That(animator.speed, Is.EqualTo(1.3f));
        }

        [Test]
        public void CancellationRestoresTheClock()
        {
            combo.RequestAttack(true);
            combo.CancelAttack();
            Assert.That(animator.speed, Is.EqualTo(1.3f));
        }

        [Test]
        public void DetachingAnimatorDuringAnAttackRestoresItsClock()
        {
            combo.RequestAttack(true);
            combo.Configure(null);
            Assert.That(animator.speed, Is.EqualTo(1.3f));
        }

        [Test]
        public void BufferedBoundarySamplesOnlyTheTimeRemainingForTheNextStrike()
        {
            combo.RequestAttack(true);
            combo.Tick(0.35f);
            combo.RequestAttack(true);
            combo.Tick(0.2f); // 1타 잔여 0.15초, 2타 0.05초.
            animator.Update(0.2f); // Unity의 자동 진행에 해당하는 갱신도 시간을 이중으로 더하지 않는다.
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            Assert.That(state.IsName("Attack2"), Is.True);
            Assert.That(state.normalizedTime, Is.EqualTo(combo.NormalizedTime).Within(0.0001f));
        }

        [Test]
        public void AnimatorWithoutControllerKeepsItsSpeed()
        {
            animator.runtimeAnimatorController = null;
            combo.RequestAttack(true);
            combo.Tick(0.2f);
            Assert.That(animator.speed, Is.EqualTo(1.3f));
        }
    }
}

