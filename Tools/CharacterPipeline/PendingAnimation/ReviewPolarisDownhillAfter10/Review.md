# Actual Polaris long-downhill review

The dedicated Downhill24 recipe supplies the missing sustained-descent evidence. Root recorded600 frames total (unchanged flat300 + downhill300), Unity1 passed /0 failed /0 ignored. This review reads the completed actual JSON and opens its actual URP frames; no code or Unity/GPU execution was performed here.

The downhill input spans30…119 walking and120…179 running,150 frames /5 seconds. Root z decreases30→10.5m, horizontal travel19.5m and height loss8.65357m. Every successive root Y in that interval is non-increasing. Every sampled foot throughout the300-frame recipe remains over the actual24° support. This is materially broader than the old straight-ramp recipe's six-frame downhill portion.

Actual fixed-skin-vertex minimum is−0.12546mm (initial Idle frame1 Left), maximum clearance157.13mm; missing surface samples0; foot-frames below−6mm0. Every foot sample reports geometry ready, attempts1, counts87/77, support active and constraints feasible. Maximum final vertical ankle correction .216919m stays within .32m; maximum unresolved clearance3.75µm. Reach/slab projection remains explicitly recorded, maximum .125563m.

Same fixed camera actual frames78 (walk),145 (run) and190 (stop step) were opened. Boots remain above/on the ramp and no large new knee/shoe fold is visible. The rear cape occludes some shoe detail, so these views cannot establish all-angle aesthetic completion. No separate same-model Before clip of this new recipe exists; the comparison is against known geometric/contact constraints, not a claimed before/after image improvement.

128 qualified near-contact intervals remain after excluding explicit stop re-steps. The largest measured same-vertex tangent speed is .2146m/s at uphill-return frame219, not the descent. This descriptive diagnostic uses contact≥.8 for3 frames, ±6mm surface proximity at both endpoints and consistent normals; it is not an assertion of zero sliding.

Gate+clearance queries peak195/frame and average99.89. These fixed-delta capture data do not measure real-time FPS. Actual slope lifecycle reset and runtime performance remain separate checks. Source motion/surface SHA256 and grouped diagnostics are preserved in `analysis.json`.

Actual source: `TestResults/CharacterPipeline/Motion/After/PolarisDownhillAfter10/Polaris/Terrain/Downhill24`.
