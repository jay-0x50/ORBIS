# Actual PolarisGripTerrain03 / After10 review

The targeted crest handoff regression is resolved in this capture; interior terrain improvements are preserved. This approves the bounded Polaris terrain fix against these recipes, not all locomotion polish, downhill coverage or performance. The remaining raw −8.537mm value is an identical pre-existing low-contact flat-ground sample. No runtime code was changed during this review.

Actual Unity result: 1 passed, 0 failed, 0 ignored; 1,500 JPEG frames. Compared with PolarisGripTerrain02 (After09), using the same model, profile, grip, input and camera. `analysis.json` contains source JSON hashes, controls and diagnostics. The executed FootIK SHA256 is `53849a2da03bf920b27d24495802c5f4f3bd225402c0927e88c5ff1a03febbae`, probe `2e12cc11179a135b10b9dd38fe306ab942eee08b900a6ed4564cdf4e969442be`.

The pending variant `490fb1ae…` only clears a gate-ray diagnostic during ResetContacts; it was not the runtime used for these images. Capture provenance must retain the actual53849 variant.

## Actual contact and image evidence

| Sample | After09 minimum | After10 minimum |
|---|---:|---:|
| Slope24,157 Right, mixed supports | −74.509mm | +0.0687mm |
| Slope24,158 Right, plateau handoff | −22.644mm | 0mm |
| Slope24,159 Right | −7.217mm | −0.0153mm |
| Slope12 whole sequence | −50.468mm | −8.537mm |
| Slope24 whole sequence | −74.509mm | −8.537mm |
| CrossSlopeTurns whole sequence | −0.223mm | −0.223mm |

Same-angle actual Slope24 frames156,157,158,159 and231 were opened and inspected. The right shoe lands at157 and remains on the plateau through158/159; no new gross knee/shoe fold or visible large downward snap replaces the old penetration. The rear leg continues its swing. These are actual URP JPEGs under `TestResults/CharacterPipeline/Motion/After/PolarisGripTerrain03/Polaris/Terrain/Slope24`.

The remaining −8.537mm is frame231 Right. The entire shoe is on the plateau, root z=14.667m, contact=.0918 and applied IK weight=.01437. The same minimum and exact RightFoot world position occur in both After09 and the original corrected-camera After08 baseline. This does not establish zero penetration; it establishes that this residual is not introduced by the terrain fix.

CrossSlopeTurns has exact equality of every recorded bone object and every actual sole point across all300 frames versus After09. Interior adapted-foot minima remain approximately −.084mm on Slope12, −.112mm on Slope24 and −.223mm on Cross. All constraints are feasible, geometryReady=true, attempts1, counts87/77. Maximum vertical ankle-goal corrections remain .119278/.180130/.195168m; unresolved-clearance diagnostics stay below3.8µm.

## Handoff and cost

Shared support is active61…157 and259…299 on both straight-slope recipes. The right plateau foot stays at full solve during mixed-support157. On158…160 its actual handoff correction is at most13.351mm (12°) or20.744mm (24°), after which ordinary weighting is used whenever the predicted legacy goal is clear.

The armed handoff flag lasts61…224 (12°) or61…225 (24°); it then turns off before the next slope approach259. It does not remain latched indefinitely. The residual amount follows the existing .075s pelvis Lerp, but the actual flag persists after the value is below nominal float world-coordinate spacing. Therefore do not claim an exact 32-bit precision cutoff or a specific subsecond exit time from source inspection alone; intermediate numeric precision and the clearance condition determine the observed exit. The flag being armed does not mean full IK is always applied: only13 foot-frames apply additional handoff clearance in either straight recipe, with several tiny7.6µm observations near rest.

Gate+clearance queries peak at231/frame across both feet. Means:109.44 (12°),102.52 (24°),94.44 (Cross). Cross normally short-circuits at its inclined root and its ray count is unchanged. Fresh flat support adds67 gate queries; old central/root queries are separate. Captured inactive-action gate counters in the53849 variant may be stale until the next ground pass, which is the reason for the pending diagnostic-reset line. Fixed frame capture does not establish acceptable real-time CPU cost or FPS.

## Flat and action behavior

Fresh Flat300 has exact equality of all compared controls and every recorded bone world position versus After09. The handoff never arms there. All three terrain recipes also preserve the compared controls exactly.

The Actions fixture includes wall-clock Timeline Burst/BurstReplay. M3UltimateDirector's UnscaledGameTime and M3Feedback's realtime clock explain why actionParameter/timeScale and the finish frame can differ despite identical captureDeltaTime. In03 the replay finishes255, versus253 in02. Root motion and attack clocks remain equal. These real-time windows must not be treated as deterministic frame-by-frame body animation tests.

Lifecycle observations in03: damage195 interrupts Burst into Hurt; replay215 starts Burst at0; cancel270 reaches Idle with timeScale1; warp280 changes the root to the requested position and the following movement resumes; reset285 and final299 are Idle with cleared support/handoff/residual. This fixture is flat, so it proves the unarmed lifecycle path only. A reset performed while actually on a slope is a separate coverage question.

Longer downhill, Stella's distinct foot geometry, explicit armed terrain lifecycle resets, and actual runtime performance remain to be checked by root. No new scene/model/controller/gameplay files were changed by this read-only comparison.

## 2026-09-15 delivery correction: image versus measurement timing

The earlier image paragraph's claim that the shoe visibly lands **at frame157** is not established by those JPGs. Delivery-time byte checks found After09/After10 JPG157 identical (SHA256 `ac51245c8e8c4a45edfbff1976f8476204cea1eb9ed3a2fc8c85164ac75d2698`). The −74.509mm → +0.0687mm change belongs to the actual fixed-skin measurement JSON157, and must not be presented as a pixel difference at that same index.

Both runs contain300 distinct images, so this is not evidence of a frozen whole sequence. Selected same-index hashes match at100/134/157/200/288 and differ at78/158/159/231. Actual JPG158 was reopened in both runs: the previous output shows the front boot penetrating the floor, while the After10 output exposes the boot tip. This is an independent visual observation; exact pose-time alignment with the numeric index is unverified. The recorder measures bones and freshly baked skin in LateUpdate, then immediately submits a separate URP camera request. GPU skin/render scheduling is a plausible timing cause, not a confirmed diagnosis. No cross-run image-copy path was found in the recorder.

`Tools/CharacterPipeline/DeliveryPreparation/LocomotionEvidenceClosure01.json` records the selected file hashes, source-reading references and delivery omissions. The final user summary is `Docs/CharacterPipeline/Locomotion_Final.md`; it includes the later completed downhill and armed lifecycle checks. Entire raw frame/measurement directories remain in staging and are not copied as part of the bounded evidence delivery.
