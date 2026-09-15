# SavedField03 actual result

Source: `TestResults/CharacterPipeline/SavedField03.xml` and `FieldFinal/SavedField03/SavedFieldBosses.json`. The actual PlayMode run passed 1, failed 0, skipped 0. This document is a read-only interpretation, not a new run.

The preserved source scene SHA-256 is `bfc7347b2e49ebf07d13289463d413e666fde53f5e6c8f0adb2a98dc345103d5`. All five original Actor/Core/Collider bindings survived normal Field initialization. Root drift was exactly 0 in the recorded samples; all Core collider counts remained 0. All five default weakness colors and runtime Core references matched. Fire alone performed one actual AutoTick attack (`maximumAttackTime=0.33333334`), then exposed after the existing three weak hits and displayed white. Other bosses' `exposedCoreColor=false` means untested in this fixture, not a failed assertion. Addressables owned handles returned to 0.

| Boss | Worst Bake(true)/CPU LBS difference across snapshots | Body minimum above actual collider before / Idle36 | Tail minimum before / Idle36 | Individually measured feet |
|---|---:|---:|---:|---|
| Fire | 0.1304 mm | +0.610 / +0.610 mm | +299.110 / +301.163 mm | None: six feet exist but this fixture does not select Fire |
| Water | 0.1931 mm | -30.313 / -19.919 mm | +526.352 / +533.547 mm | Not applicable to the approved swimming anatomy |
| Wind | 0.0460 mm | +4.576 / +4.576 mm | +19.590 / +19.585 mm | Four; all +4.576 to +4.578 mm |
| Rock | 0.1304 mm | -0.908 / -0.908 mm | No Tail group measured | None: two feet exist but this fixture does not select Rock |
| Lightning | 0.1528 mm | +0.610 / +0.610 mm | +333.008 / +337.486 mm | Four; all +0.610 mm |

The rejected Bake(false)/full-matrix route disagreed with CPU LBS by up to 909.612 m. The true/full route agreed across every vertex at each sampled pose; the prior hundreds-of-metres offset was a fixture measurement error. No model was repositioned to obtain this result.

Idle maximum bone displacements over the observed 36 frames were Fire 29.095 mm, Water 213.736 mm, Wind 25.315 mm, Rock 10.042 mm, Lightning 26.336 mm. These prove that the saved Generic rigs were playing; they do not grade movement quality. The `SavedBeforeInitialize` snapshot occurs after three normal Unity frames, so it is not a frozen FBX bind-pose measurement. In particular it must not overwrite earlier static Wind tail observations.

Coverage detail: `LowestWeighted` measures vertices with summed influence above 0.25. The explicit four-foot branch selects only `Zephyr` and `Voltheim`, using `FrontLeg.L3`, `FrontLeg.R3`, `RearLeg.L3`, `RearLeg.R3`. Fire's source prefab additionally has `MiddleLeg.L3`/`MiddleLeg.R3`; all six Fire feet remain unmeasured individually. Rock uses `Leg.L3`/`Leg.R3`, also not selected. Empty `feet` arrays do not demonstrate missing bones or contact success.

Remaining limits: only two ground snapshots per boss plus the first Fire attack, not continuous contact sweeps; body/tail minima do not cover every surface point; negative Water/Rock clearances remain explicit; the decorative pad metric is its mesh-bounds centre about 35 mm above root, not its exact top surface. No rendering, marker visibility, complete foot contact, animation perfection, or frame-rate claim follows from this Passed XML.
