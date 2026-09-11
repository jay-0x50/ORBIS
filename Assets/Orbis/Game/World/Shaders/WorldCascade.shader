Shader "Orbis/World/Cascade"
{
    Properties
    {
        _BaseColor("Flowing freshwater", Color)=(.20,.52,.56,.82)
        _DeepColor("Water depth tint", Color)=(.10,.30,.36,.78)
        _FoamColor("Foam", Color)=(.78,.90,.86,1)
        _FlowSpeed("Flow metres per second", Range(0,5))=1.7
        _FoamAmount("Broken foam coverage", Range(0,1))=.6
        _OrbisWorldWater("World water marker", Float)=1
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20"}
        Pass
        {
            Name "CascadeForward"
            Tags {"LightMode"="UniversalForwardOnly"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CascadeVertex
            #pragma fragment CascadeFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor,_DeepColor,_FoamColor;
            float _FlowSpeed,_FoamAmount,_OrbisWorldWater;
            CBUFFER_END
            // UV.y is metres downstream for ribbons; pools use normalized disk UV.
            // COLOR = (rapid foam, unused, pool mode, edge opacity), authored by WorldWaterfallBuilder.
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings
            {
                float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;
                float2 uv:TEXCOORD2;half4 color:COLOR;half fog:TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings CascadeVertex(Attributes input)
            {
                Varyings output=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,output);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.uv=input.uv;output.color=input.color;output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            float Hash(float2 p)
            {
                p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);
            }
            float Noise(float2 p)
            {
                float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            half4 CascadeFragment(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float time=_Time.y;
                float pool=step(.5,input.color.b);
                float advected=input.uv.y-time*_FlowSpeed;
                float across=input.uv.x+.031*sin(input.uv.y*.21)+.017*sin(input.uv.y*.58+time*.23);
                // Long irregular water veins merge into a broad falling sheet, rather than repeating short white dashes.
                float streak=Noise(float2(across*13,advected*.20));
                streak=.72*streak+.28*Noise(float2(across*31,advected*.57));
                float foam=smoothstep(.44,.76,streak)*lerp(.2,1,input.color.r)*_FoamAmount;
                foam+=smoothstep(.57,.86,Noise(float2(across*7,advected*.32)))*input.color.r*.16;
                float bank=smoothstep(.79,.97,abs(input.uv.x*2-1));
                foam=max(foam,bank*Noise(float2(input.uv.x*25,advected*2))*input.color.r*.7);
                float2 radial=input.uv*2-1;
                float radius=length(radial);
                if(pool>.5)
                {
                    float cells=Noise(radial*9+float2(time*.12,time*.08));
                    float ripple=sin((radius*4.2-time*.55+cells*.85)*6.2831853)*.5+.5;
                    foam=(smoothstep(.66,.94,ripple)*smoothstep(.25,.68,cells)+smoothstep(.6,.83,cells)*.25)*input.color.r*_FoamAmount;
                    streak=Noise(radial*3+float2(time*.08,-time*.06));
                }
                half3 albedo=lerp(_DeepColor.rgb,_BaseColor.rgb,saturate(.50+streak*.65));
                albedo=lerp(albedo,_FoamColor.rgb,foam);
                // Foam and shallow water keep a generous ambient floor; no bloom or per-pixel scene sampling is needed.
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadow=ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                float4 shadow=TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun=GetMainLight(shadow);
                half light=.78h+.22h*saturate(abs(dot(normalize(input.normalWS),sun.direction)));
                half3 illumination=lerp(half3(.86,.91,.93),max(sun.color,half3(.4,.4,.4)),.38h)*light;
                illumination*=lerp(.86h,1.0h,sun.shadowAttenuation);
                half alpha=lerp(_BaseColor.a,_FoamColor.a,foam)*input.color.a;
                if(pool<.5)
                {
                    float fringe=smoothstep(.65,.97,abs(input.uv.x*2-1));
                    alpha*=lerp(1,smoothstep(.15,.52,Noise(float2(across*11,advected*.38))),fringe*.72);
                }
                // Authored disk/ribbon edge opacity avoids rectangular waterfall cards and sharp circular foam plates.
                clip(alpha-.005h);
                return half4(MixFog(albedo*illumination,input.fog),alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
