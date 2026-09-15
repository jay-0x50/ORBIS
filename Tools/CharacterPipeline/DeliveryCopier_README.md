# Guarded delivery copier — prepared, production execution pending

`apply_delivery.py` consumes a reviewed `prepare_delivery.py` plan. The CLI cannot change its staging root, `D:/Project/ORBIS` destination, or pinned 1,793-file baseline. It rejects root/path junctions, protected original `Assets/blend` content, `.git`, changed plan inputs, unexpected existing files, changed target bytes, and required metadata witnesses. It reconstructs the exact plan before copying; rehashing an altered plan alone is insufficient.

Run from the staging project. A dry run creates only a fresh staging report directory and performs no target mutation:

```powershell
& 'C:/Python314/python.exe' 'Tools/CharacterPipeline/apply_delivery.py' --plan 'Tools/CharacterPipeline/DeliveryPreparation/Plan02.json' --run-dir 'Tools/CharacterPipeline/DeliveryRuns/Review02'
```

After reviewing the current plan and dry-run result, the explicit copy command uses a different fresh run directory:

```powershell
& 'C:/Python314/python.exe' 'Tools/CharacterPipeline/apply_delivery.py' --plan 'Tools/CharacterPipeline/DeliveryPreparation/Plan02.json' --run-dir 'Tools/CharacterPipeline/DeliveryRuns/Apply02' --apply
```

The numbered paths are examples, not a claim that Plan02 exists or is approved. Production dry-run and apply were **not** executed during helper preparation. The parent task controls the actual plan/version and execution.

Every selected source, target and required metadata file is checked before target mutation, including equal files that will be skipped. Each replaced target is first copied and verified into the fresh run's `backups/<relative path>`. New content is streamed into an exclusively created target sibling temporary file, flushed, SHA-verified, and replaced with `os.replace` only after another source/temporary/target check. Final verification covers all copied files plus skipped dependencies and required metadata.

`Run.json` records the plan file SHA, canonical plan SHA, current phase, verified backups, temporary paths and completed copies. Failure preserves this evidence and does not roll back automatically. Owned temporary files are retained for inspection; they are never deleted by the helper. Read-only attributes are never changed to force a write.

Replacement is atomic **per file**, not across the project. A crash can leave a partially delivered project. There is no cross-process lock: a writer racing between the final SHA check and `os.replace` cannot be excluded by these checks. Stop Unity/importers and other writers for the delivery window. A stale plan must be regenerated and reviewed; a partially applied plan will fail exact reconstruction rather than silently resume.

Metadata checks follow the plan's declared scope. `selected` checks required selected GUIDs and companion/folder metadata; `target-assets` additionally checks collisions across target `Assets`. `none` is accepted for non-Assets delivery only. This helper neither invents `.meta` files nor imports assets. Credits remains a separate guarded edit because its existing target is outside the pinned baseline.

Verification is limited to synthetic staging/target fixtures under `Tools/CharacterPipeline/DeliveryCopierTests`. `test_apply_delivery.py` exercises both successful delivery and failures before copying, after backup, and after one replacement. The test patches module constants only inside synthetic fixtures; there is no production CLI bypass. Windows junction protection is implemented by canonical-root equality plus the planner's per-component symlink/junction checks; actual junction creation is not part of the synthetic tests.
