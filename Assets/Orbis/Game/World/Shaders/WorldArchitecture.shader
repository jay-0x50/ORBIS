Shader "Orbis/World/Architecture"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo",2D)="white"{}
        [MainColor] _BaseColor("Source palette",Color)=(1,1,1,1)
        _Ramp("Shared three-band ramp",2D)="white"{}
        _ShadowColor("Quiet shadow color",Color)=(.68,.72,.78,1)
        _AmbientFill("Sky fill",Range(0,1))=.24
        _SpecColor("Highlight color",Color)=(1,1,1,1)
        _SpecStrength("Highlight strength",Range(0,1))=0
        _SpecPower("Highlight power",Range(8,128))=48
        _RimColor("Edge highlight",Color)=(1,1,1,1)
        _RimStrength("Edge strength",Range(0,1))=0
        _RimPower("Edge power",Range(1,8))=3.2
        [HDR] _EmissionColor("Window glow",Color)=(0,0,0,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull",Float)=2
        [HideInInspector] _OrbisArchitecture("Architecture marker",Float)=1
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor,_ShadowColor,_SpecColor,_RimColor,_EmissionColor;
        float _AmbientFill,_SpecStrength,_SpecPower,_RimStrength,_RimPower,_Cull,_OrbisArchitecture;
        CBUFFER_END
        // Camera-local atmosphere bridge supplies already diluted, linear M3 palette multipliers.
        float4 _OrbisBiomeAmbient;
        TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
        TEXTURE2D(_Ramp);SAMPLER(sampler_Ramp);
        ENDHLSL
        Pass
        {
            Name "ArchitectureForward"
            Tags {"LightMode"="UniversalForwardOnly"}
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ArchitectureVertex
            #pragma fragment ArchitectureFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            struct ArchitectureAttributes
            {
                float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct ArchitectureVaryings
            {
                float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;
                float2 uv:TEXCOORD2;half fog:TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };
            ArchitectureVaryings ArchitectureVertex(ArchitectureAttributes input)
            {
                ArchitectureVaryings output=(ArchitectureVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,output);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);output.uv=TRANSFORM_TEX(input.uv,_BaseMap);
                output.fog=ComputeFogFactor(output.positionCS.z);return output;
            }
            half4 ArchitectureFragment(ArchitectureVaryings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(input.positionCS);
                #endif
                half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb*_BaseColor.rgb;
                half3 normal=normalize(input.normalWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadow=ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                float4 shadow=TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun=GetMainLight(shadow);
                half diffuse=saturate(dot(normal,sun.direction))*sun.shadowAttenuation;
                half band=diffuse>.65h?2.0h:(diffuse>.18h?1.0h:0.0h);
                half3 ramp=SAMPLE_TEXTURE2D_LOD(_Ramp,sampler_Ramp,float2((band+.5h)/3.0h,.5),0).rgb;
                half3 sky=max(SampleSH(normal),half3(.10,.10,.10))*_AmbientFill;
                half3 lighting=lerp(_ShadowColor.rgb,sun.color,saturate(ramp))+sky;
                half3 view=GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 halfway=SafeNormalize(sun.direction+view);
                half specular=pow(saturate(dot(normal,halfway)),max(8,_SpecPower))*_SpecStrength;
                specular*=sun.shadowAttenuation*saturate(dot(normal,sun.direction));
                // Deliberately restrained back-edge accent; the opaque diffuse keeps three discrete bands.
                half rim=pow(1-saturate(dot(normal,view)),max(1,_RimPower))*_RimStrength;
                rim*=saturate(.55h-.45h*dot(normal,sun.direction));
                half3 color=albedo*lighting+_SpecColor.rgb*sun.color*specular+_RimColor.rgb*rim+_EmissionColor.rgb;
                half3 biome=dot(abs(_OrbisBiomeAmbient),float4(1,1,1,1))>.0001?_OrbisBiomeAmbient.rgb:half3(1,1,1);
                return half4(MixFog(color*biome,input.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags {"LightMode"="ShadowCaster"}
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags {"LightMode"="DepthOnly"}
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags {"LightMode"="DepthNormalsOnly"}
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
