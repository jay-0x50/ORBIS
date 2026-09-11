Shader "Orbis/Art/UnifiedToon"
{
    Properties
    {
        [MainTexture] _BaseMap("Color atlas",2D)="white"{}
        [MainColor] _BaseColor("Tint",Color)=(1,1,1,1)
        [HDR] _EmissionColor("Emission",Color)=(0,0,0,0)
        _ShadowColor("Shadow tone",Color)=(.48,.52,.63,1)
        _OutlineColor("Outline",Color)=(.055,.065,.085,1)
        _OutlinePixels("Unified outline width (pixels)",Float)=1.5
        _OutlineScale("Local outline scale",Range(0,1))=1
        _BandSoftness("Lighting band softness",Range(0,1))=0
        _ShadowStrength("Received shadow strength",Range(0,1))=1
        _AmbientFill("Soft fill light",Range(0,1))=0
        [HideInInspector] _OutlineOnly("Outline pass material",Float)=0
        [HideInInspector] _Cull("Cull",Float)=2
        [HideInInspector] _SrcBlend("Source blend",Float)=1
        [HideInInspector] _DstBlend("Destination blend",Float)=0
        [HideInInspector] _ZWrite("Depth write",Float)=1
        [HideInInspector] _Threshold("M3 dissolve",Range(0,1))=0
        [HideInInspector] _PrimaryColor("M3 primary",Color)=(1,1,1,1)
        [HideInInspector] _SecondaryColor("M3 edge",Color)=(1,1,1,1)
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _BaseColor, _ShadowColor, _OutlineColor, _PrimaryColor, _SecondaryColor, _EmissionColor;
        float _OutlinePixels, _OutlineOnly, _Cull, _Threshold;
        float _OutlineScale, _BandSoftness, _ShadowStrength, _AmbientFill;
        CBUFFER_END
        struct Attributes {float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID};
        struct Varyings {float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; float fog:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO};
        float DissolveNoise(float3 p) {return frac(sin(dot(floor(p*22),float3(12.9898,78.233,45.164)))*43758.5453);}
        void DissolveClip(float3 p)
        {
            if(_Threshold>.0001) clip(DissolveNoise(p)-_Threshold);
        }
        ENDHLSL
        Pass
        {
            Name "ToonForward"
            Tags {"LightMode"="UniversalForward"}
            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            Varyings vert(Attributes input)
            {
                Varyings output=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                if(_OutlineOnly>.5)
                {
                    float3 normalVS=TransformWorldToViewDir(output.normalWS);
                    // Match the requested pixel width with a real 3D normal shell. Moving only clip XY
                    // leaves reverse faces coplanar and paints double-sided grass/roof surfaces black.
                    float perPixel=2*abs(output.positionCS.w)/(_ScreenParams.y*max(abs(UNITY_MATRIX_P._m11),.001));
                    float shell=perPixel*_OutlinePixels/max(length(normalVS.xy),.2);
                    output.positionCS=TransformWorldToHClip(output.positionWS+output.normalWS*shell);
                }
                output.uv=TRANSFORM_TEX(input.uv,_BaseMap);
                output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 frag(Varyings input):SV_Target
            {
                DissolveClip(input.positionWS);
                if(_OutlineOnly>.5)
                {
                    // Zero-width facial detail overrides must not leave a coplanar black back face.
                    clip(_OutlinePixels-.001);
                    return half4(MixFog(_OutlineColor.rgb,input.fog),1);
                }
                half4 atlas=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*_BaseColor;
                clip(atlas.a-.01);
                Light light=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float diffuse=saturate(dot(normalize(input.normalWS),light.direction));
                // Unspecified art defaults: three lighting bands, neutral warm atlas colors retained.
                float band=diffuse>.65?1:diffuse>.18?.78:.48;
                // All defaults preserve the existing three-band look; only explorer materials opt into soft fill.
                float softBand=lerp(.48,.78,smoothstep(.04,.38,diffuse));
                softBand=lerp(softBand,1,smoothstep(.42,.88,diffuse));
                band=lerp(band,softBand,_BandSoftness);
                float shadow=lerp(1,light.shadowAttenuation,_ShadowStrength);
                float shade=lerp(.52,band,shadow);
                shade=lerp(shade,1,_AmbientFill);
                half3 color=atlas.rgb*lerp(_ShadowColor.rgb,light.color,shade)+_EmissionColor.rgb;
                if(_Threshold>.001) color+=_SecondaryColor.rgb*step(DissolveNoise(input.positionWS)-_Threshold,.045)*.5;
                return half4(MixFog(color,input.fog),atlas.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags {"LightMode"="ShadowCaster"}
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection; float3 _LightPosition;
            Varyings shadowVert(Attributes input)
            {
                Varyings output=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                float3 normal=TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection=normalize(_LightPosition-output.positionWS);
                #else
                float3 lightDirection=_LightDirection;
                #endif
                output.positionCS=TransformWorldToHClip(ApplyShadowBias(output.positionWS,normal,lightDirection));
                #if UNITY_REVERSED_Z
                output.positionCS.z=min(output.positionCS.z,output.positionCS.w*UNITY_NEAR_CLIP_VALUE);
                #else
                output.positionCS.z=max(output.positionCS.z,output.positionCS.w*UNITY_NEAR_CLIP_VALUE);
                #endif
                output.uv=TRANSFORM_TEX(input.uv,_BaseMap);
                return output;
            }
            half4 shadowFrag(Varyings input):SV_Target
            {
                clip(.5-_OutlineOnly); DissolveClip(input.positionWS);
                clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).a*_BaseColor.a-.01);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
