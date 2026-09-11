#ifndef ORBIS_FOLIAGE_LIGHTING_INCLUDED
#define ORBIS_FOLIAGE_LIGHTING_INCLUDED

// Texture arrays require shader model 3.5. Custom Function nodes include this file with pragmas.
#pragma target 3.5
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

// UV0 = original source texture UV (Repeat); UV1 = (array slice, rooted wind weight).
// UV2 = (leaf/grass flutter mask, normalized total height). All LOD meshes use the same contract.
// Time comes from a TimeNode, allowing URP's time-based motion pass to evaluate the previous frame.
void FoliageWind_float(float3 PositionOS, float3 NormalOS, float4 UV1, float4 UV2, float Time,
    float3 WindDirection, float4 WindParams, float3 PlayerPosition, float PlayerRadius,
    float WindStrength, float FlutterStrength, float GrassBend, float BillboardMode,
    out float3 Position, out float3 Normal)
{
    float3 originWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float3 world = TransformObjectToWorld(PositionOS);
    float3 normalWS = TransformObjectToWorldNormal(NormalOS);
    if (BillboardMode > 0.5)
    {
        float3 toCamera = GetCameraPositionWS() - originWS;
        toCamera.y = 0.0;
        toCamera = dot(toCamera, toCamera) > 0.0001 ? normalize(toCamera) : float3(0.0, 0.0, 1.0);
        float3 right = normalize(cross(float3(0.0, 1.0, 0.0), toCamera));
        float3x3 objectToWorld = (float3x3)GetObjectToWorldMatrix();
        float scaleX = length(mul(objectToWorld, float3(1.0, 0.0, 0.0)));
        float scaleY = length(mul(objectToWorld, float3(0.0, 1.0, 0.0)));
        world = originWS + right * (PositionOS.x * scaleX) + float3(0.0, PositionOS.y * scaleY, 0.0);
        // The atlas is an unlit base-color bake; give the distant canopy a broad upright lighting normal.
        normalWS = normalize(toCamera + float3(0.0, 0.65, 0.0));
    }
    float2 windXZ = WindDirection.xz;
    windXZ = dot(windXZ, windXZ) > 0.0001 ? normalize(windXZ) : float2(1.0, 0.0);
    float weight = saturate(UV1.y);
    float height = saturate(UV2.y);
    float flutterMask = saturate(UV2.x);
    float seed = dot(originWS.xz, float2(0.137, 0.173));
    float pulse = sin(Time * max(WindParams.w, 0.0) * 6.2831853 + seed) * 0.5 + 0.5;
    float main = max(0.0, WindParams.x) + max(0.0, WindParams.z) * pulse;
    float sway = sin(Time * (0.65 + max(WindParams.y, 0.0) * 0.15) + seed);
    sway += 0.35 * sin(Time * 1.7 + seed * 2.0);
    float bend = sway * main * max(WindStrength, 0.0) * weight * weight;
    float flutter = sin(Time * 4.3 + dot(world, float3(0.71, 0.93, 0.57)));
    flutter *= 0.035 * max(FlutterStrength, 0.0) * max(WindParams.y, 0.0) * main * flutterMask * weight;
    float3 displacement = float3(windXZ.x, 0.0, windXZ.y) * bend + normalWS * flutter;

    if (GrassBend > 0.0 && PlayerRadius > 0.001)
    {
        float2 delta = world.xz - PlayerPosition.xz;
        float distanceToPlayer = length(delta);
        float influence = saturate(1.0 - distanceToPlayer / PlayerRadius);
        // Art default: do not bend grass on a different floor/cliff below the controlled pawn.
        influence *= saturate(1.0 - abs(world.y - PlayerPosition.y) / 2.5);
        float2 away = distanceToPlayer > 0.001 ? delta / distanceToPlayer : windXZ;
        float amount = influence * influence * height * saturate(GrassBend);
        displacement += float3(away.x * 0.65, -0.28, away.y * 0.65) * amount;
    }
    // Contract with WorldGrassField: expanded bounds are 2m; total shader displacement never exceeds 1.2m.
    float displacementLength = length(displacement);
    displacement *= min(1.0, 1.2 / max(displacementLength, 0.0001));
    Position = TransformWorldToObject(world + displacement);
    // Small branch sway rotates the broad normal, while leaf flutter retains the authored leaf shape.
    normalWS = SafeNormalize(normalWS - float3(windXZ.x, 0.0, windXZ.y) * (bend * 0.18));
    Normal = TransformWorldToObjectNormal(normalWS);
}

float2 FoliageAtlasUV(float2 uv)
{
    float3 cameraOS = TransformWorldToObject(GetCameraPositionWS());
    float angle = atan2(cameraOS.x, cameraOS.z);
    float index = fmod(floor(angle * (8.0 / 6.2831853) + 0.5) + 8.0, 8.0);
    float2 tile = float2(fmod(index, 4.0), floor(index / 4.0));
    // 8 yaw views, 4x2; view 0 is +Z, tile 0 is bottom-left. Source tiles are 256px.
    // Art default: a 1.5px inset protects the root-touching tile edge from neighbouring views.
    return (tile + clamp(uv, 1.5 / 256.0, 1.0 - 1.5 / 256.0)) / float2(4.0, 2.0);
}

void FoliageLighting_float(UnityTexture2DArray BaseArray, UnityTexture2D BillboardAtlas,
    float2 UV, float4 UV1, float4 UV2, float3 WorldPosition, float3 WorldNormal,
    float4 BaseColor, float4 ShadowColor, UnityTexture2D Ramp, float Cutoff, float BillboardMode,
    float GrassBend, float3 GrassCameraPosition, float GrassFadeStart, float GrassFadeEnd,
    float LeafSaturation, float LeafShadowLift, float4 LeafTint, float LeafTintStrength, float AmbientStrength,
    float4 BiomeAmbient,
    out float3 Color, out float Alpha, out float ClipThreshold)
{
    float4 albedo;
    if (BillboardMode > 0.5)
        albedo = SAMPLE_TEXTURE2D(BillboardAtlas.tex, BillboardAtlas.samplerstate, FoliageAtlasUV(UV));
    else
        albedo = SAMPLE_TEXTURE2D_ARRAY(BaseArray.tex, BaseArray.samplerstate, UV, max(0.0, floor(UV1.x + 0.5)));
    float3 normal = SafeNormalize(WorldNormal);
    float3 lightDirection;
    float3 lightColor;
    float shadow;
#if defined(SHADERGRAPH_PREVIEW)
    lightDirection = normalize(float3(0.4, 0.75, 0.5));
    lightColor = float3(1.0, 1.0, 1.0);
    shadow = 1.0;
#else
    float4 shadowCoord;
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
        shadowCoord = ComputeScreenPos(TransformWorldToHClip(WorldPosition));
    #else
        shadowCoord = TransformWorldToShadowCoord(WorldPosition);
    #endif
    Light mainLight = GetMainLight(shadowCoord);
    lightDirection = mainLight.direction;
    lightColor = mainLight.color;
    shadow = mainLight.shadowAttenuation;
#endif
    // UV2.x is the authored leaf/grass mask: bark keeps the original color and source detail.
    // Billboards have one quad. Their baked pixels have no separate semantic mask, so chroma rejects
    // the neutral/brown trunk when applying the same leaf correction to the distant source-color bake.
    float styleLeaf = saturate(UV2.x);
    if (BillboardMode > 0.5)
    {
        float maximum = max(albedo.r, max(albedo.g, albedo.b));
        float minimum = min(albedo.r, min(albedo.g, albedo.b));
        styleLeaf *= smoothstep(0.58, 0.82, (maximum - minimum) / max(maximum, 0.001));
    }
    if (LeafSaturation < 0.99999 || LeafShadowLift > 0.0 || LeafTintStrength > 0.0)
    {
        float luma = dot(albedo.rgb, float3(0.2126, 0.7152, 0.0722));
        float3 corrected = lerp(luma.xxx, albedo.rgb, saturate(LeafSaturation));
        // Art controls: lift only dark texels, preserving both their hue and the dark fine branch marks.
        float lifted = luma + max(LeafShadowLift, 0.0) * (1.0 - saturate(luma / 0.45));
        corrected *= min(3.5, lifted / max(luma, 0.01));
        float green = saturate((albedo.g - albedo.r) * 8.0) * saturate((albedo.g - albedo.b) * 8.0);
        float tintLuma = max(dot(LeafTint.rgb, float3(0.2126, 0.7152, 0.0722)), 0.01);
        corrected = lerp(corrected, LeafTint.rgb * (lifted / tintLuma), saturate(LeafTintStrength) * green);
        albedo.rgb = lerp(albedo.rgb, corrected, styleLeaf);
    }
    float ndl = dot(normal, lightDirection);
    // Art default: two-sided leaf cards receive wrapped light; bark keeps its source-facing normal.
    float leaf = max(saturate(UV2.x), step(0.5, BillboardMode));
    float diffuse = lerp(saturate(ndl), saturate(0.35 + 0.65 * abs(ndl)), leaf) * saturate(shadow);
    float band = diffuse > 0.65 ? 2.0 : (diffuse > 0.18 ? 1.0 : 0.0);
    float3 ramp = SAMPLE_TEXTURE2D_LOD(Ramp.tex, Ramp.samplerstate, float2((band + 0.5) / 3.0, 0.5), 0).rgb;
    Color = albedo.rgb * BaseColor.rgb * lerp(ShadowColor.rgb, lightColor, saturate(ramp));
    // Constant sky fill preserves the three discrete diffuse bands. It does not alter wind, alpha or UVs.
    #if defined(SHADERGRAPH_PREVIEW)
    float3 sky = float3(0.35, 0.35, 0.35);
    #else
    float3 sky = max(SampleSH(float3(0.0, 1.0, 0.0)), float3(0.20, 0.20, 0.20));
    #endif
    Color += albedo.rgb * BaseColor.rgb * sky * max(AmbientStrength, 0.0) * styleLeaf;
    // WorldAtmosphere already blends the exact M3 palette gently toward white and passes linear multipliers.
    // No bridge/global state (all zero) is an identity operation, including Shader Graph previews.
    float3 biome = dot(abs(BiomeAmbient), float4(1.0, 1.0, 1.0, 1.0)) > 0.0001 ? BiomeAmbient.rgb : float3(1.0, 1.0, 1.0);
    Color *= biome;
    Alpha = albedo.a * BaseColor.a;
    ClipThreshold = clamp(Cutoff, 0.001, 0.99);

    if (GrassBend > 0.5 && GrassFadeEnd > GrassFadeStart)
    {
        // Draw-specific camera data supports Scene/Review/offscreen cameras without depending on Camera.main.
        float distanceToCamera = distance(WorldPosition, GrassCameraPosition);
        float visibility = saturate((GrassFadeEnd - distanceToCamera) / (GrassFadeEnd - GrassFadeStart));
        // A stable screen-space dither fades solid grass blades without changing the source alpha silhouette.
        float4 clip = TransformWorldToHClip(WorldPosition);
        float2 pixel = floor((clip.xy / max(abs(clip.w), 0.001) * 0.5 + 0.5) * _ScreenParams.xy);
        float noise = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
        Alpha *= visibility >= 0.9999 ? 1.0 : (visibility <= 0.0001 ? 0.0 : step(noise, visibility));
    }
#if !defined(SHADERGRAPH_PREVIEW)
    Color = MixFog(Color, ComputeFogFactor(TransformWorldToHClip(WorldPosition).z));
#endif
}
#endif
