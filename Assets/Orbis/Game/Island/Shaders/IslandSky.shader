Shader "Orbis/Island Sky"
{
    Properties
    {
        _Zenith("Zenith", Color) = (0.13,0.40,0.67,1)
        _Horizon("Horizon", Color) = (0.72,0.86,0.89,1)
        _Cloud("Cloud", Color) = (1,0.98,0.90,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Zenith, _Horizon, _Cloud;
            CBUFFER_END
            float4 _OrbisWeather;
            float _OrbisWeatherTime,_OrbisWeatherFlash;
            struct Input { float4 positionOS: POSITION; };
            struct Output { float4 positionCS: SV_POSITION; float3 direction: TEXCOORD0; };
            Output vert(Input input)
            {
                Output output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.direction=input.positionOS.xyz;
                return output;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 a=floor(p),b=frac(p);b=b*b*(3-2*b);
                return lerp(lerp(hash(a),hash(a+float2(1,0)),b.x),lerp(hash(a+float2(0,1)),hash(a+1),b.x),b.y);
            }
            half4 frag(Output input):SV_Target
            {
                float3 d=normalize(input.direction);
                float h=saturate(d.y);
                half3 color=lerp(_Horizon.rgb,_Zenith.rgb,pow(h,.55));
                float2 p=d.xz/(max(.08,d.y)+.3)*3;
                p+=float2(.012,.005)*_OrbisWeatherTime*_OrbisWeather.z;
                float f=noise(p)*.55+noise(p*2.05)*.28+noise(p*4.1)*.12+noise(p*8.2)*.05;
                float cloud=smoothstep(lerp(.56,.27,_OrbisWeather.w),lerp(.73,.6,_OrbisWeather.w),f)*smoothstep(.02,.18,h)*(1-smoothstep(.75,1,h));
                color=lerp(color,_Cloud.rgb,cloud*.86);
                float sun=dot(d,normalize(float3(.46,.65,-.6)));
                color+=half3(1,.85,.59)*pow(saturate(sun),220)*.9;
                color+=half3(1,.76,.42)*pow(saturate(sun),18)*.08;
                color=lerp(color,color*.50+half3(.11,.14,.19),_OrbisWeather.w*.82);
                color+=half3(.17,.19,.23)*_OrbisWeatherFlash;
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
