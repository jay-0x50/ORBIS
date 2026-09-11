// ORBIS-owned lighting wrapper. Terrain geometry/blending/support passes stay in the installed Unity URP package.
Shader "Hidden/Orbis/World/TerrainAdd"
{
    Properties
    {
        [HideInInspector][ToggleUI] _EnableHeightBlend("EnableHeightBlend", Float) = 0
        _HeightTransition("Height transition", Range(0,1)) = 0
        [HideInInspector][PerRendererData] _NumLayersCount("Total layer count", Float) = 1
        [HideInInspector] _Control("Control", 2D) = "red" {}
        [HideInInspector] _Splat0("Layer 0", 2D) = "grey" {}
        [HideInInspector] _Normal0("Normal 0", 2D) = "bump" {}
        [HideInInspector] _Mask0("Mask 0", 2D) = "grey" {}
        [HideInInspector][Gamma] _Metallic0("Metallic 0", Range(0,1)) = 0
        [HideInInspector] _Smoothness0("Smoothness 0", Range(0,1)) = 0
        [HideInInspector] _Splat1("Layer 1", 2D) = "grey" {}
        [HideInInspector] _Normal1("Normal 1", 2D) = "bump" {}
        [HideInInspector] _Mask1("Mask 1", 2D) = "grey" {}
        [HideInInspector][Gamma] _Metallic1("Metallic 1", Range(0,1)) = 0
        [HideInInspector] _Smoothness1("Smoothness 1", Range(0,1)) = 0
        [HideInInspector] _Splat2("Layer 2", 2D) = "grey" {}
        [HideInInspector] _Normal2("Normal 2", 2D) = "bump" {}
        [HideInInspector] _Mask2("Mask 2", 2D) = "grey" {}
        [HideInInspector][Gamma] _Metallic2("Metallic 2", Range(0,1)) = 0
        [HideInInspector] _Smoothness2("Smoothness 2", Range(0,1)) = 0
        [HideInInspector] _Splat3("Layer 3", 2D) = "grey" {}
        [HideInInspector] _Normal3("Normal 3", 2D) = "bump" {}
        [HideInInspector] _Mask3("Mask 3", 2D) = "grey" {}
        [HideInInspector][Gamma] _Metallic3("Metallic 3", Range(0,1)) = 0
        [HideInInspector] _Smoothness3("Smoothness 3", Range(0,1)) = 0
        [HideInInspector] _MainTex("Base map", 2D) = "grey" {}
        [HideInInspector] _MetallicTex("Base metallic", 2D) = "black" {}
        [HideInInspector] _BaseColor("Base color", Color) = (1,1,1,1)
        [HideInInspector] _Cutoff("Hole cutoff", Range(0,1)) = .5
        [HideInInspector] _TerrainHolesTexture("Terrain holes", 2D) = "white" {}
        [ToggleUI] _EnableInstancedPerPixelNormal("Instanced per-pixel normal", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Geometry-99" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "TerrainCompatible" = "True" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite Off
            Blend One One
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SplatmapVert
            #pragma fragment SplatmapFragment
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma multi_compile_fragment __ _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _MASKMAP
            #pragma shader_feature_local_fragment _TERRAIN_BLEND_HEIGHT
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #define TERRAIN_SPLAT_ADDPASS 1
            #define _METALLICSPECGLOSSMAP 1
            #define _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A 1
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "WorldTerrainLighting.hlsl"
            // Lighting is already included, so this alias only redirects the official terrain fragment call.
            #define UniversalFragmentPBR OrbisTerrainFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
            #undef UniversalFragmentPBR
            ENDHLSL
        }

    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}

