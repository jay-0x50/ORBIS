"""Create reviewable presentation-only integration copies; never change live Assets here."""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Tools/CharacterPipeline/PendingIntegration"
FILES = {
    "motor": "Assets/Orbis/M0/Runtime/Player/PlayerMotor.cs",
    "combo": "Assets/Orbis/M0/Runtime/Combat/BasicAttackCombo.cs",
    "traversal": "Assets/Orbis/M2/Runtime/Traversal/ExplorationMotor.cs",
    "roster": "Assets/Orbis/Art/Runtime/Characters/ArtCharacterRoster.cs",
    "explorer": "Assets/Orbis/M16/Runtime/Combat/ExplorerController.cs",
    "presentation": "Assets/Orbis/M3/Runtime/Presentation/M3Presentation.cs",
}

def swap(text, before, after, count=1):
    if text.count(before) != count:
        raise ValueError(f"Integration source changed ({text.count(before)} != {count}): {before[:100]}")
    return text.replace(before, after)

def motor(s):
    s=swap(s,"using UnityEngine;","using UnityEngine;\nusing Orbis.M0.Animation;")
    s=swap(s,"        private Animator animator;","        private Animator animator;\n        private ICharacterAnimationDriver animationDriver;\n        private readonly CharacterMotionTracker motionTracker = new CharacterMotionTracker();\n        private bool externallyClockedAction;")
    s=swap(s,"            animator = visualAnimator;","            animationDriver?.Release();\n            RestoreActionAnimatorClock();\n            animator = visualAnimator;\n            animationDriver = CharacterAnimationBinding.Resolve(animator);\n            animationDriver?.ResetPresentation();\n            ResetMotionObservation();",2)
    # SetVisualAnimator already restores the legacy clock before releasing the old driver.
    s=swap(s,"            RestoreActionAnimatorClock();\n            animationDriver?.Release();\n            RestoreActionAnimatorClock();","            animationDriver?.Release();\n            RestoreActionAnimatorClock();")
    s=swap(s,"            if (!configured) return;\n            if (input.ResetPressed", "            if (!configured) return;\n            TickMotor();\n            animationDriver?.Observe(motionTracker.Sample(transform, input.Move, IsGrounded, State, Time.deltaTime));\n        }\n\n        private void TickMotor()\n        {\n            if (input.ResetPressed")
    s=swap(s,"            actionNormalizedTime = 0f;","            actionNormalizedTime = 0f;\n            externallyClockedAction = false;")
    s=swap(s,"            actionState = PlayerActionState.Idle;","            animationDriver?.ClearAction(owner);\n            actionState = PlayerActionState.Idle;\n            externallyClockedAction = false;")
    s=swap(s,"            actionNormalizedTime = Mathf.Clamp01(normalizedTime);","            externallyClockedAction = true;\n            actionNormalizedTime = Mathf.Clamp01(normalizedTime);")
    s=swap(s,"            if (!ownsActionAnimatorClock)\n", "            if (animationDriver != null)\n            {\n                animationDriver.SampleAction(actionState, actionNormalizedTime, externallyClockedAction);\n                return;\n            }\n            if (!ownsActionAnimatorClock)\n")
    s=swap(s,"        private void AnimateLocomotion(bool force)\n        {","        private void AnimateLocomotion(bool force)\n        {\n            if (animationDriver != null) return; // Actual post-collision observation owns the new tree.")
    s=swap(s,"            if (ActionLocked) { SampleActionPose(); return; }","            if (ActionLocked) { SampleActionPose(); return; }\n            if (animationDriver != null) return;")
    s=swap(s,"        public void ResetToSpawn()", "        public void ResetMotionObservation() => motionTracker.Reset();\n\n        public void ResetToSpawn()")
    s=swap(s,"            Traversal?.ResetTraversal();","            Traversal?.ResetTraversal();\n            ResetMotionObservation();")
    s=swap(s,"            if (combat != null) combat.CancelAttack();","            if (combat != null) combat.CancelAttack();\n            animationDriver?.Release();\n            ResetMotionObservation();")
    return s

def combo(s):
    s=swap(s,"using UnityEngine;","using UnityEngine;\nusing Orbis.M0.Animation;")
    s=swap(s,"        private ComboSequence sequence;","        private ComboSequence sequence;\n        private ICharacterAnimationDriver animationDriver;")
    s=swap(s,"            animator = targetAnimator;","            animationDriver?.ClearAttack();\n            animator = targetAnimator;\n            animationDriver = CharacterAnimationBinding.Resolve(animator);")
    s=swap(s,"            sequence?.CancelAttack();","            sequence?.CancelAttack();\n            animationDriver?.ClearAttack();")
    s=swap(s,"        private void SynchronizeAttackPose()\n        {", "        private void SynchronizeAttackPose()\n        {\n            if (animationDriver != null)\n            {\n                if (IsAttacking) animationDriver.SampleAttack(CurrentStep, NormalizedTime);\n                else animationDriver.ClearAttack();\n                return;\n            }")
    return s

def traversal(s):
    s=swap(s,"using Orbis.M0;","using Orbis.M0;\nusing Orbis.M0.Animation;")
    s=swap(s,"        private Animator animator;","        private Animator animator;\n        private ICharacterAnimationDriver animationDriver;")
    s=swap(s,"            animator = motor.GetComponentInChildren<Animator>();","            animator = motor.GetComponentInChildren<Animator>();\n            animationDriver = CharacterAnimationBinding.Resolve(animator);")
    s=swap(s,"            animator = visualAnimator;","            animator = visualAnimator;\n            animationDriver = CharacterAnimationBinding.Resolve(animator);\n            SynchronizeMotionMode();")
    s=swap(s,"                if (Mode != ExplorationMode.Locomotion) animator.Play(\"Jump\", 0, .5f);","                if (animationDriver == null && Mode != ExplorationMode.Locomotion) animator.Play(\"Jump\", 0, .5f);")
    s=swap(s,"            if (animator != null) animator.Play(\"Jump\", 0, 0.5f);","            SynchronizeMotionMode();\n            if (animationDriver == null && animator != null) animator.Play(\"Jump\", 0, 0.5f);")
    s=swap(s,"            Mode = ExplorationMode.Locomotion;","            Mode = ExplorationMode.Locomotion;\n            SynchronizeMotionMode();",3)
    s=swap(s,"            Physics.SyncTransforms();\n            LeaveMode(0f);","            Physics.SyncTransforms();\n            motor.ResetMotionObservation();\n            LeaveMode(0f);")
    anchor="        public void SetStaminaPool(StaminaPool pool)"
    s=swap(s,anchor,"        private void SynchronizeMotionMode()\n        {\n            animationDriver?.SetTraversalMode(Mode == ExplorationMode.Climbing ? CharacterMotionMode.Climbing :\n                Mode == ExplorationMode.Gliding ? CharacterMotionMode.Gliding :\n                Mode == ExplorationMode.Swimming ? CharacterMotionMode.Swimming : CharacterMotionMode.Ground);\n        }\n\n"+anchor)
    return s

def roster(s):
    s=swap(s,"using Orbis.M0;","using Orbis.M0;\nusing Orbis.M0.Animation;")
    s=swap(s,'            animator.Rebind(); animator.Play("Idle", 0, 0f); animator.Update(0f);',
        '''            animator.Rebind();
            var humanDriver = animator.GetComponent<HumanAnimationDriver>();
            if (humanDriver != null)
            {
                // Presentation yaw belongs to a dedicated wrapper; the pawn/camera and rig
                // hierarchy remain unchanged. Its reference only exists after view creation.
                var facing = new GameObject("Visual Facing").transform;
                facing.SetParent(root.transform, false);
                model.transform.SetParent(facing, false);
                humanDriver.Configure(humanDriver.Profile, facing);
            }
            var animationDriver = CharacterAnimationBinding.Resolve(animator);
            if (animationDriver != null) animationDriver.ResetPresentation();
            else animator.Play("Idle", 0, 0f);
            animator.Update(0f);''')
    return s

def explorer(s):
    return swap(s,"                hurtRemaining = Mathf.Max(0f, hurtRemaining - deltaTime);", "                hurtRemaining = Mathf.Max(0f, hurtRemaining - deltaTime);\n                motor.SetActionNormalizedTime(PlayerActionState.Hurt, 1f - hurtRemaining / HurtSeconds);")

def presentation(s):
    return swap(s,"            popupAge += Time.unscaledDeltaTime;", "            popupAge += Time.unscaledDeltaTime;\n            if (motor != null && motor.State == PlayerActionState.Burst && Ultimate != null &&\n                Ultimate.Director != null && Ultimate.Director.state == UnityEngine.Playables.PlayState.Playing &&\n                Ultimate.Director.duration > 0 && !double.IsInfinity(Ultimate.Director.duration))\n                motor.SetActionNormalizedTime(PlayerActionState.Burst, (float)(Ultimate.Director.time / Ultimate.Director.duration));")

def main():
    manifest={"status":"Pending review copies only; no runtime Assets modified.","files":[]}
    for key,relative in FILES.items():
        source=ROOT/relative
        original=source.read_bytes()
        text=source.read_text(encoding="utf-8-sig")
        result=globals()[key](text)
        path=OUT/relative
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_text(result,encoding="utf-8",newline="\n")
        manifest["files"].append({"path":relative,"originalSha256":hashlib.sha256(original).hexdigest(),
                                  "candidateSha256":hashlib.sha256(path.read_bytes()).hexdigest()})
    (OUT/"manifest.json").write_text(json.dumps(manifest,indent=2),encoding="utf-8")
    print(json.dumps(manifest,indent=2))

if __name__=="__main__": main()
