#ifndef ORBIS_WORLD_TERRAIN_LIGHTING_INCLUDED
#define ORBIS_WORLD_TERRAIN_LIGHTING_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Shared world atmosphere multiplier, already mixed 6.5% toward a regional palette by WorldAtmosphere.
// This is global, so the base terrain and its engine-created add/basemap materials agree.
float4 _OrbisBiomeAmbient;

half OrbisGroundBand(half ndotl)
{
    // Provisional art values: three diffuse levels and narrow antialiased boundaries.
    half value = saturate(ndotl);
    half width = max(fwidth(value), .018h);
    half band = lerp(.34h, .68h, smoothstep(.18h-width, .18h+width, value));
    return lerp(band, 1.0h, smoothstep(.64h-width, .64h+width, value));
}
half3 OrbisGroundLight(Light light, half3 normal, uint layers)
{
#ifdef _LIGHT_LAYERS
    if (!IsMatchingLightLayer(light.layerMask, layers)) return 0;
#endif
    return light.color * (OrbisGroundBand(dot(normal,light.direction)) *
        light.distanceAttenuation * light.shadowAttenuation * .78h);
}
half4 OrbisTerrainFragment(InputData inputData, half3 albedo, half metallic, half3 specular,
    half smoothness, half occlusion, half3 emission, half alpha)
{
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, occlusion);
    Light mainLight = GetMainLight(inputData, shadowMask, ao);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);
    uint layers = GetMeshRenderingLayer();
    // The authored island has very little baked GI. A restrained sky fill keeps cast shadows
    // in the character's cool (.68,.73,.78) family instead of near-black. It fades out in full sun.
    half visibleSun = saturate(OrbisGroundBand(dot(inputData.normalWS,mainLight.direction)) *
        mainLight.shadowAttenuation * mainLight.distanceAttenuation);
    half3 skyFill = half3(.68h,.73h,.78h) * (.28h * (1-visibleSun));
    half3 illumination = max(max(inputData.bakedGI,0) * .55h,skyFill) * ao.indirectAmbientOcclusion;
    illumination += OrbisGroundLight(mainLight, inputData.normalWS, layers);
#if defined(_ADDITIONAL_LIGHTS)
    uint count = GetAdditionalLightsCount();
#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light extra = GetAdditionalLight(lightIndex,inputData,shadowMask,ao);
        illumination += OrbisGroundLight(extra,inputData.normalWS,layers);
    }
#endif
    LIGHT_LOOP_BEGIN(count)
        Light extra = GetAdditionalLight(lightIndex,inputData,shadowMask,ao);
        illumination += OrbisGroundLight(extra,inputData.normalWS,layers);
    LIGHT_LOOP_END
#endif
#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    illumination += inputData.vertexLighting * .78h;
#endif
    half3 regionalTint = dot(abs(_OrbisBiomeAmbient),float4(1,1,1,1)) > .00001 ?
        max(_OrbisBiomeAmbient.rgb,0) : half3(1,1,1);
    // No view-dependent specular/reflection lobe on dry grass, soil and stone. Alpha remains the
    // official splat pass weight; TerrainLitPasses applies it once and preserves additive fog.
    return half4(albedo * illumination * regionalTint + emission, alpha);
}
#endif
