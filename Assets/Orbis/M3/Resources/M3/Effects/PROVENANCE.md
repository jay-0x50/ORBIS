# GPU graph provenance

The eight .vfx assets are generated and authored for ORBIS by M3VfxBuilder.Build. Their context skeleton is derived from Unity Visual Effect Graph 17.6.0, Editor/Templates/Simple_Burst.vfx (com.unity.visualeffectgraph, copyright Unity Technologies ApS). Unity's package declares the Unity Companion License for Unity-dependent projects. The installed package LICENSE.md is retained here as Unity_VFX_LICENSE.txt.

ORBIS authored the graph systems, finite spawn policy, exposed controls, particle budgets, color gradients, size/lifetime curves, position/velocity/force values and all five procedural PNG particle textures. Ring, spark, soft plume, star and faceted shard masks are mathematical drawings in the builder; no third-party image assets were downloaded.

PrimaryColor/SecondaryColor are exposed Vector4 values; Scale is an exposed float controlling position, velocity and particle size. Each system spawns once per OnPlay. Reinit already emits OnPlay; do not follow it with another Play call. DeltaTime + IgnoreTimeScale keeps particles animated through gameplay hitstop.

Last particle expiry from emission: Vaporize 2.1 s, ElectroCharged 0.65 s, Overload 1.05 s, Swirl 1.65 s, Crystallize 1.65 s, Impact 0.5 s, Burst 2.3 s, Residue 4.0 s. Pool leases should include at least one frame of margin. Scale does not change lifetime.

Build preserves existing .vfx assets that already expose the full runtime contract. Textures are also created only when missing. The finished assets are committed so normal play/build does not depend on invoking the reflective authoring tool. Validate checks the runtime contract, single-loop emitters, time-scale independence and generated shader source.
