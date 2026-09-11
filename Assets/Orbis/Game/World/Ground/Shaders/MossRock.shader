Shader "Orbis/World/MossRock"
{
    Properties
    {
        _StoneMap("Stone albedo",2D)="white"{}
        _MossMap("Moss albedo",2D)="white"{}
        _StoneColor("Stone tint",Color)=(1,1,1,1)
        _MossColor("Moss tint",Color)=(1,1,1,1)
        _TextureScale("Triplanar repeats per metre",Float)=.22
        _TriplanarSharpness("Triplanar blend sharpness",Range(1,8))=4
        _MossStart("Moss minimum upward normal",Range(0,1))=.36
        _MossEnd("Moss full upward normal",Range(0,1))=.83
        _MossCoverage("Moss coverage",Range(0,1))=.78
        _AmbientStrength("Ambient SH strength",Range(0,2))=.72
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _StoneMap_ST,_MossMap_ST;
        half4 _StoneColor,_MossColor;
        float _TextureScale,_TriplanarSharpness,_MossStart,_MossEnd,_MossCoverage,_AmbientStrength;
        CBUFFER_END
        TEXTURE2D(_StoneMap);SAMPLER(sampler_StoneMap);
        TEXTURE2D(_MossMap);SAMPLER(sampler_MossMap);
        // WorldAtmosphere supplies an already blended, linear regional tint. Unset globals mean neutral white.
        float4 _OrbisBiomeAmbient;
        ENDHLSL
        Pass
        {
            Name "MossRockForward"
            Tags {"LightMode"="UniversalForwardOnly"}
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex RockVertex
            #pragma fragment RockFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            struct RockAttributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct RockVaryings
            {
                float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half fog:TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };
            RockVaryings RockVertex(RockAttributes input)
            {
                RockVaryings output=(RockVaryings)0;UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half3 Triplanar(TEXTURE2D_PARAM(map,sampler_map),float3 p,half3 weights)
            {
                return SAMPLE_TEXTURE2D(map,sampler_map,p.zy).rgb*weights.x+
                    SAMPLE_TEXTURE2D(map,sampler_map,p.xz).rgb*weights.y+
                    SAMPLE_TEXTURE2D(map,sampler_map,p.xy).rgb*weights.z;
            }
            half4 RockFragment(RockVaryings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(input.positionCS);
                #endif
                half3 normal=normalize(input.normalWS),weights=pow(abs(normal),_TriplanarSharpness);
                weights/=max(dot(weights,half3(1,1,1)),.0001h);
                float3 p=input.positionWS*max(_TextureScale,.001);
                half3 stone=Triplanar(TEXTURE2D_ARGS(_StoneMap,sampler_StoneMap),p,weights)*_StoneColor.rgb;
                half3 moss=Triplanar(TEXTURE2D_ARGS(_MossMap,sampler_MossMap),p,weights)*_MossColor.rgb;
                // A world-space low-frequency modulation breaks the moss edge without UV seams or extra textures.
                float variation=sin(input.positionWS.x*.61+sin(input.positionWS.z*.37))*sin(input.positionWS.z*.49)*.09;
                half coverage=smoothstep(_MossStart,_MossEnd,max(0,normal.y)+variation)*_MossCoverage;
                half3 albedo=lerp(stone,moss,coverage);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadow=ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                float4 shadow=TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun=GetMainLight(shadow);half diffuse=saturate(dot(normal,sun.direction));
                // Same Step 4 diffuse bands and energy budget as the terrain wrapper, without specular glare.
                half width=max(fwidth(diffuse),.018h);
                half band=lerp(.34h,.68h,smoothstep(.18h-width,.18h+width,diffuse));
                band=lerp(band,1.0h,smoothstep(.64h-width,.64h+width,diffuse));
                // Match the terrain's cool shadow floor; leave full-sun surfaces at the existing exposure.
                half visibleSun=saturate(band*sun.shadowAttenuation*sun.distanceAttenuation);
                half3 skyFill=half3(.68h,.73h,.78h)*(.28h*(1-visibleSun));
                half3 ambient=max(max(SampleSH(normal),0)*_AmbientStrength,skyFill);
                half3 regionalTint=dot(abs(_OrbisBiomeAmbient),float4(1,1,1,1))>.00001?
                    max(_OrbisBiomeAmbient.rgb,0):half3(1,1,1);
                half3 lit=albedo*(ambient+sun.color*(band*sun.shadowAttenuation*sun.distanceAttenuation*.78h))*regionalTint;
                return half4(MixFog(lit,input.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags {"LightMode"="ShadowCaster"}
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
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
            ZWrite On ColorMask R Cull Back
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
            ZWrite On Cull Back
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
    Fallback Off
}
