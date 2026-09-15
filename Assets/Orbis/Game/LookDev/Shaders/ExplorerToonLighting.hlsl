#ifndef ORBIS_EXPLORER_TOON_LIGHTING_INCLUDED
#define ORBIS_EXPLORER_TOON_LIGHTING_INCLUDED

// Step 1: textured palette, a three-band main-light ramp, and the existing cloned inverted hull.
// Universal Unlit "Keep Lighting Variants" supplies the real main-light/shadow variants.
// Step 2 optionally adds reference-colored rim and highlights. No renderer feature or post-process is injected here.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

// Optional supplied-atlas normal mapping. This fragment is included in ExplorerToonLighting.hlsl.
// Geometry normals still drive the inverted hull; perturbed normals drive light only.
void ExplorerAtlasNormal_float(UnityTexture2D NormalMap, float2 UV, float Strength,
    float3 WorldNormal, float3 WorldTangent, float3 WorldBitangent, out float3 Normal)
{
    Normal = WorldNormal;
    if (Strength <= 0.0)
        return; // Existing materials preserve their exact old lighting path.
    float3 n = SafeNormalize(WorldNormal);
    float3 t = WorldTangent - n * dot(WorldTangent, n);
    if (dot(t, t) < 0.000001 || dot(WorldBitangent, WorldBitangent) < 0.000001)
        return; // Missing tangents must not produce NaNs on unrelated legacy meshes.
    t = normalize(t);
    float handedness = dot(cross(n, t), WorldBitangent) < 0.0 ? -1.0 : 1.0;
    float3 b = cross(n, t) * handedness;
    float4 packed = SAMPLE_TEXTURE2D(NormalMap.tex, NormalMap.samplerstate, NormalMap.GetTransformedUV(UV));
    // Unity's importer packs the source Blender OpenGL tangent map for the active platform.
    // UnpackNormalScale handles RG/AG/RGB formats; manual RGB*2-1 would fail on BC5/DXT5nm.
    float3 tangentNormal = UnpackNormalScale(packed, saturate(Strength));
    Normal = SafeNormalize(t * tangentNormal.x + b * tangentNormal.y + n * tangentNormal.z);
}

void ExplorerHull_float(float3 PositionOS, float3 NormalOS, float OutlinePixels, float OutlineOnly, out float3 Position)
{
    Position = PositionOS;
    if (OutlineOnly > 0.5)
    {
        float3 worldPosition = TransformObjectToWorld(PositionOS);
        float3 worldNormal = SafeNormalize(TransformObjectToWorldNormal(NormalOS));
        float4 clipPosition = TransformWorldToHClip(worldPosition);
        float3 viewNormal = TransformWorldToViewDir(worldNormal);
        // ArtOutlineSync already multiplies the source _OutlineScale into this per-submesh pixel width.
        // Extrude a real world-space hull, not only clip XY (which leaves reverse faces coplanar).
        float metresPerPixel = 2.0 * abs(clipPosition.w) /
            (max(_ScreenParams.y, 1.0) * max(abs(UNITY_MATRIX_P._m11), 0.001));
        float shell = metresPerPixel * max(OutlinePixels, 0.0) / max(length(viewNormal.xy), 0.2);
        Position = TransformWorldToObject(worldPosition + worldNormal * shell);
    }
}

void ExplorerAlbedo_float(UnityTexture2D BaseMap, float2 UV, float4 BaseColor,
    float4 PaletteColor, float PaletteStrength, float TextureReference, float4 GoldColor,
    float4 VertexColor, float VertexColorStrength, out float3 Albedo, out float Alpha)
{
    // Preserve _BaseMap_ST, including the EX_Skin fixed skin-texel sampling contract.
    float4 sampleColor = SAMPLE_TEXTURE2D(BaseMap.tex, BaseMap.samplerstate, BaseMap.GetTransformedUV(UV));
    float3 original = sampleColor.rgb * BaseColor.rgb;
    // Match the importer CPU sample: Color.linear.grayscale uses these exact weights.
    float luminance = dot(sampleColor.rgb, float3(0.299, 0.587, 0.114));
    // TextureReference is the original textile's luminance in the shader's working color space.
    float clothDetail = clamp(luminance / max(TextureReference, 0.001), 0.6, 1.3);
    float3 recolored = PaletteColor.rgb * clothDetail;
    // Art default: navy textile has R <= B; its warm gold embroidery has significantly greater R than B.
    // This soft mask preserves embroidered details rather than recoloring the whole cape one flat navy.
    float warmGold = smoothstep(0.10, 0.25, max(0.0, sampleColor.r - sampleColor.b));
    recolored = lerp(recolored, GoldColor.rgb * clothDetail, warmGold);
    Albedo = lerp(original, recolored, saturate(PaletteStrength));
    // Optional weapon palette: Blender exports colors_type='LINEAR'; URP forwards COLOR without sRGB conversion.
    // A white BaseMap/BaseColor at strength 1 preserves those sampled linear RGB values in one material.
    // Keep the zero-strength path untouched for existing characters, and never use vertex alpha for M3 coverage.
    if (VertexColorStrength > 0.0)
        Albedo *= lerp(float3(1.0, 1.0, 1.0), VertexColor.rgb, saturate(VertexColorStrength));
    Alpha = sampleColor.a * BaseColor.a;
}

void ExplorerThreeBand_float(float3 Albedo, float3 WorldPosition, float3 WorldNormal,
    UnityTexture2D Ramp, float4 ShadowColor, float4 EmissionColor,
    float2 UV, float FaceLighting, float3 FaceForwardWS, float3 FaceRightWS, out float3 Color, out float Band)
{
    float3 lightDirection;
    float3 lightColor;
    float shadow;
#if defined(SHADERGRAPH_PREVIEW)
    lightDirection = normalize(float3(0.4, 0.75, -0.5));
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
    float diffuse = saturate(dot(SafeNormalize(WorldNormal), lightDirection)) * saturate(shadow);
    if (FaceLighting > 0.5)
    {
        // EX_Face only: head-following axes come from the renderer property block, not mesh object axes.
        // An analytic UV cheek mask avoids tiny nose/cheek triangles and hair self-shadow acne breaking the painted face.
        // The body still casts shadows, while this face shading intentionally does not multiply the received shadow map.
        float3 faceForward = dot(FaceForwardWS, FaceForwardWS) > 0.001 ? SafeNormalize(FaceForwardWS) : float3(0, 0, 1);
        float3 faceRight = dot(FaceRightWS, FaceRightWS) > 0.001 ? SafeNormalize(FaceRightWS) : float3(1, 0, 0);
        // Project onto the animated head's horizontal plane so high key light does not push the entire
        // face back and forth across the 0.65 band threshold. An overhead-only key defaults to front light.
        float2 horizontalLight = float2(dot(faceRight, lightDirection), dot(faceForward, lightDirection));
        float horizontalLength = length(horizontalLight);
        horizontalLight = horizontalLength >= 0.001 ? horizontalLight / horizontalLength : float2(0.0, 1.0);
        float side = horizontalLight.x;
        float facing = horizontalLight.y;
        float oriented = (UV.x - 0.5) * (side >= 0.0 ? 1.0 : -1.0);
        // Art defaults: the shadow occupies up to 40% of the face under side light;
        // 0.015 UV feather avoids an unstable boundary before the existing three-band quantization.
        float boundary = -0.5 + 0.4 * saturate(abs(side) / max(abs(facing), 0.2));
        boundary = lerp(boundary, 0.5, saturate(-facing));
        float cheekLight = smoothstep(boundary - 0.015, boundary + 0.015, oriented);
        diffuse = lerp(0.38, 0.9, cheekLight) * saturate((facing + 0.3) / 1.3);
    }
    // Three discrete cells, sampled at the center of each _Ramp third. These thresholds retain the old art defaults.
    Band = diffuse > 0.65 ? 2.0 : (diffuse > 0.18 ? 1.0 : 0.0);
    float2 rampUV = float2((Band + 0.5) / 3.0, 0.5);
    float3 ramp = SAMPLE_TEXTURE2D_LOD(Ramp.tex, Ramp.samplerstate, rampUV, 0).rgb;
    Color = Albedo * lerp(ShadowColor.rgb, lightColor, saturate(ramp)) + EmissionColor.rgb;
}

void ExplorerHighlights_float(float3 LitColor, float3 WorldPosition, float3 WorldNormal,
    float4 RimColor, float RimStrength, float RimPower,
    float4 SpecColor, float SpecStrength, float SpecPower, float SkinSpecStrength, out float3 Color)
{
    Color = LitColor;
    // All strengths default to zero: no arithmetic touches Step 1's result until a material opts in.
    if (RimStrength <= 0.0 && SpecStrength <= 0.0 && SkinSpecStrength <= 0.0)
        return;

    float3 normal = SafeNormalize(WorldNormal);
    float3 view;
    float3 lightDirection;
    float3 lightColor;
    float shadow;
    float3 probe;
#if defined(SHADERGRAPH_PREVIEW)
    view = float3(0.0, 0.0, 1.0);
    lightDirection = normalize(float3(0.4, 0.75, -0.5));
    lightColor = float3(1.0, 1.0, 1.0);
    shadow = 1.0;
    probe = float3(1.0, 1.0, 1.0);
#else
    view = GetWorldSpaceNormalizeViewDir(WorldPosition);
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
    // URP 17 SampleSH reads the renderer's interpolated probe/ambient SH coefficients.
    // Its luminance modulates illumination; the reference palette supplies the rim's hue.
    probe = max(float3(0.0, 0.0, 0.0), SampleSH(-lightDirection));
#endif

    if (RimStrength > 0.0)
    {
        float edge = pow(1.0 - saturate(dot(normal, view)), max(RimPower, 1.0));
        float oppositeKey = smoothstep(-0.15, 0.4, -dot(normal, lightDirection));
        float probeLuminance = dot(probe, float3(0.299, 0.587, 0.114));
        // Art default: a small 0.2 back-edge fill, then 0.8 of the actual ambient/probe illumination.
        // This leaves deep unlit probes restrained instead of making the outline a constant glow.
        float probeFill = 0.2 + 0.8 * clamp(probeLuminance, 0.0, 1.25);
        Color += RimColor.rgb * max(RimStrength, 0.0) * edge * oppositeKey * probeFill;
    }

    if (SpecStrength > 0.0 || SkinSpecStrength > 0.0)
    {
        float3 halfway = SafeNormalize(lightDirection + view);
        float normalHalf = saturate(dot(normal, halfway));
        float directVisibility = saturate(dot(normal, lightDirection)) * saturate(shadow);
        float metalLobe = pow(normalHalf, max(SpecPower, 1.0));
        float skinLobe = pow(normalHalf, max(SpecPower * 0.25, 2.0));
        // Per-material art defaults are set by the importer: metal around 0.5, skin around 0.08.
        // The broad skin lobe and narrow metal lobe add highlights without smoothing the three diffuse bands.
        float highlight = metalLobe * max(SpecStrength, 0.0) + skinLobe * max(SkinSpecStrength, 0.0);
        Color += SpecColor.rgb * lightColor * highlight * directVisibility;
    }
}

float ExplorerDissolveNoise(float3 originalWorldPosition)
{
    // Keep the existing M3 world-space appearance pattern.
    return frac(sin(dot(floor(originalWorldPosition * 22.0), float3(12.9898, 78.233, 45.164))) * 43758.5453);
}

void ExplorerSurface_float(float3 LitColor, float BaseAlpha, float3 OriginalWorldPosition,
    float OutlineOnly, float OutlinePixels, float4 OutlineColor, float Threshold,
    float4 PrimaryColor, float4 SecondaryColor, out float3 Color, out float Alpha, out float ClipThreshold)
{
    float noise = ExplorerDissolveNoise(OriginalWorldPosition);
    float coverage = Threshold > 0.0001 ? step(Threshold, noise) : 1.0;
    Color = LitColor;
    Alpha = BaseAlpha * coverage;
    ClipThreshold = 0.01;
    if (OutlineOnly > 0.5)
    {
        Color = OutlineColor.rgb;
        // A zero-width hull must disappear, rather than leave coplanar black fragments.
        Alpha = coverage * step(0.001, OutlinePixels);
        #if defined(SHADERPASS) && defined(SHADERPASS_SHADOWCASTER)
            #if SHADERPASS == SHADERPASS_SHADOWCASTER
                Alpha = 0.0; // The body casts shadows; its outline copy never casts a second shadow.
            #endif
        #endif
    }
    else if (Threshold > 0.001)
    {
        Color += SecondaryColor.rgb * step(noise - Threshold, 0.045) * 0.5;
    }
    // PrimaryColor remains part of the M3 material interface; no new presentation rule consumes it.
#if !defined(SHADERGRAPH_PREVIEW)
    float fog = ComputeFogFactor(TransformWorldToHClip(OriginalWorldPosition).z);
    Color = MixFog(Color, fog);
#endif
}
#endif

