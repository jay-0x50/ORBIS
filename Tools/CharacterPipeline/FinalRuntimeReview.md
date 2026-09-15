# Final runtime code review

Read-only review on 2026-09-15. Compared the six existing runtime files against `D:/Project/ORBIS`; each target SHA matched `Tools/CharacterPipeline/ProjectBaseline.json`. Reviewed the four new requested animation files and supporting binding/profile/visual-turn lifecycle code. No Assets edit, Unity execution, gameplay tuning, shader rejudgment, or new tests were performed.

**No reproducible major gameplay/serialization/event-lifetime/native-resource defect was identified in the requested current paths.** This is a scoped source review, not a proof of all runtime behavior or visual/contact quality.

| Area | Evidence from the current change |
|---|---|
| `BasicAttackCombo` | The original ComboSequence, hit windows, per-step target deduplication, physics overlap/obstruction query, `RegisterHit` and `HitLanded` remain unchanged. Driver calls only synchronize/clear presentation. Switching first restores any legacy Animator-speed loan; the new driver never borrows it. |
| `PlayerMotor` | Existing collision movement, attack restrictions, action ownership, gravity and speed formulas remain in `TickMotor`. The new observation is taken afterward and does not issue another Move or damage call. Visual replacement releases the old driver, restores the legacy clock, and resets observation; warp/reset also clears observation. `EndAction(owner)` retains its owner guard. Combat StepStarted subscription is removed before Configure/re-added and removed on destruction. |
| `ExplorerController` | The change publishes Hurt's existing 0.25-second clock and a successful Skill's initial normalized time. TryStart, cooldown ownership, cast cancellation, damage, elemental application and hit deduplication are unchanged. Existing external subscriptions are balanced on disable/destroy; sequence delegates are removed on destroy. |
| `M3Presentation` | The addition only reads a playing, finite-duration Timeline into the current Burst owner's normalized visual time. Existing Ultimate cancellation/finish ownership and event unsubscription remain intact. No Timeline damage or resource-spend callback was added. |
| `ExplorationMotor` | Existing movement/stamina/fall/rescue computation is unchanged. Mode changes notify the driver, and teleport resets the motion observation. InputActionMap disable/disposal remains balanced. No additional traversal controller is created. |
| `ArtCharacterRoster` | The new wrapper is a child under the existing visual root, not the pawn/camera/rig root. Only the selected protagonist is instantiated. The model is rebound to its measured driver; visible-height fitting still disposes its scratch Mesh in `finally`, and model colliders remain disabled. Existing prewarm/party switch event subscriptions are balanced. Whole-pawn/scene destruction owns the visual hierarchy. |
| `BossAnimationPresenter` / `BossPoseClock` | The Presenter reads M4 state, remaining time, telegraph progress and HP after M4's -55 Update (Presenter -40), then writes Animator state/time/layer weights. It never ticks combat, applies damage, changes the root or registers an attack. No event subscription or native-resource ownership is introduced. Reset/disable clears presentation clocks. Recovery after exposure cannot fabricate a delayed attack contact; a first observed saved Defeated state starts at its final pose. |
| `HumanAnimationDriver` | Controller/Avatar/profile contracts are cached and validated; visual writes target Animator layers/parameters and an explicit facing wrapper. It cannot move the gameplay root. Action clearing is owner-scoped; Release resets visual turn, stop and contact state without forcing Animator.speed. Profile/build checks and final binding reject animation damage events. |
| `HumanFootIK` | The current implementation uses managed cached rest-geometry arrays and bounded ground queries, not per-frame BakeMesh/native Mesh creation. Geometry calibration is lazy and failure-latched. Collider queries exclude the Player layer and player descendants; IK changes Animator body/foot goals, not the CharacterController or root. Disable clears contacts/bind state. Reach/slab caps and terrain-to-flat handoff were reviewed separately; final real terrain runs remain their behavioral evidence. |

Existing serialized fields in the six baseline files were not renamed or removed. Added driver/tracker/action-clock references are private transient fields; no save-schema field or gameplay actor identity was added. New profile/presenter references are validated by the final binding and saved Field tests; this review does not replace those actual reference checks.

Limits: this was not a new PlayMode/performance run, concurrency fuzz test, memory profiler trace, malformed-asset recovery test, or an exhaustive arbitrary-at-runtime Avatar replacement test. Managed small arrays and physics fallback allocations exist; their cost needs actual profiling before a performance claim. Invalid authored controller/profile data intentionally fails validation and should not be silently converted to another animation path. Remaining foot contact/artifact/pose issues are visual QA, not cleared by this source review. Files changed after the hashes below invalidate the corresponding review scope.

## Reviewed current SHA-256

| File | SHA-256 |
|---|---|
| `Assets/Orbis/M0/Runtime/Combat/BasicAttackCombo.cs` | `9ae6f5fc8ee0e29868c679c455d536aa86ba72300aca95be694dbee4bb36a054` |
| `Assets/Orbis/M0/Runtime/Player/PlayerMotor.cs` | `e28c95e882e854a0b48ac69f6090e208e8ec950f70e4c02c09f9ace6d75959d3` |
| `Assets/Orbis/M16/Runtime/Combat/ExplorerController.cs` | `9066b452e50cbd1b90511b9cdc789f87c25b006d3d19c2e8b26d7c28df70e1f8` |
| `Assets/Orbis/M3/Runtime/Presentation/M3Presentation.cs` | `1f11ae53ce5909f292dfdce6450fddec8c8f8c196b49331de0ea194b2f2e4e33` |
| `Assets/Orbis/M2/Runtime/Traversal/ExplorationMotor.cs` | `53a985fa985e74733431d853cd66e70dacb24272f730becf86f6767c83c943fe` |
| `Assets/Orbis/Art/Runtime/Characters/ArtCharacterRoster.cs` | `97ab24a3f6f727b708b9bfd907c23323e0452e2d3530445c280d3447959dbf25` |
| `Assets/Orbis/Game/Runtime/Animation/BossAnimationPresenter.cs` | `63cf2d219f5979dcc20c8aa195fdcec79a2174635f89db534330a61cdedb1575` |
| `Assets/Orbis/Game/Runtime/Animation/BossPoseClock.cs` | `c04ebcb6805ec99cbf9f92849fa4daa9796ecd229c02318b800739c08ab3a0b6` |
| `Assets/Orbis/M0/Runtime/Animation/HumanAnimationDriver.cs` | `708e8071324c9208d60b2e235219ad9499b5e994e0059e5e918541000b2a7a9b` |
| `Assets/Orbis/M0/Runtime/Animation/HumanFootIK.cs` | `53849a2da03bf920b27d24495802c5f4f3bd225402c0927e88c5ff1a03febbae` |
