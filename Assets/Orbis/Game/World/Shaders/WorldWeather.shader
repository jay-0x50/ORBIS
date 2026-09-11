Shader "Orbis/World/WeatherParticles"
{
    Properties
    {
        _BaseColor("Weather tint",Color)=(.65,.76,.80,1)
        _Opacity("Maximum opacity",Range(0,1))=.24
        _Mode("Rain 0 / wind 1",Float)=0
        _OrbisWeatherEffects("Weather marker",Float)=1
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+100"}
        Pass
        {
            Name "WorldWeatherForward"
            Tags {"LightMode"="UniversalForwardOnly"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WeatherVertex
            #pragma fragment WeatherFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _Opacity,_Mode,_OrbisWeatherEffects;
            float4 _WeatherOrigin,_WeatherCameraPosition,_WeatherWindDirection,_WeatherState;
            float _WeatherTime,_WeatherShelter;
            CBUFFER_END
            struct Attributes {float3 positionOS:POSITION;float2 uv:TEXCOORD0;float4 seed:TEXCOORD1;};
            struct Varyings
            {
                float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;
                float3 positionWS:TEXCOORD1;float3 appearance:TEXCOORD2;
            };
            Varyings WeatherVertex(Attributes input)
            {
                Varyings output=(Varyings)0;
                float rain=saturate(_WeatherState.x),storm=saturate(_WeatherState.y),wind=saturate(_WeatherState.z);
                float3 direction=_WeatherWindDirection.xyz;
                float time=_WeatherTime;
                float3 center;float3 tangent;float ribbonLength;
                float density;float fade;
                if(_Mode<.5)
                {
                    // A 40m cell with 28m height and independent seeds: deterministic immediately on every camera.
                    float speed=lerp(14.0,22.0,storm),drift=lerp(.6,4.5,max(wind,storm));
                    float3 phase=frac(input.seed.xyz+float3(direction.x*drift/40,-speed/28,direction.z*drift/40)*time
                        -_WeatherOrigin.xyz/float3(40,28,40));
                    center=_WeatherOrigin.xyz+float3((phase.x-.5)*40,(phase.y-.5)*28,(phase.z-.5)*40);
                    tangent=normalize(float3(direction.x*drift,-speed,direction.z*drift));
                    ribbonLength=lerp(.38,.85,input.seed.w)*lerp(1,1.35,storm);
                    density=rain*lerp(.60,1.0,storm);
                    fade=smoothstep(0,.06,phase.y)*smoothstep(0,.06,1-phase.y);
                }
                else
                {
                    float3 phase=frac(input.seed.xyz+float3(direction.x*.16,.01,direction.z*.16)*time
                        -_WeatherOrigin.xyz/float3(38,18,38));
                    center=_WeatherOrigin.xyz+float3((phase.x-.5)*38,(phase.y-.5)*18,(phase.z-.5)*38);
                    tangent=direction;ribbonLength=lerp(1.8,4.8,input.seed.w);
                    density=wind*.75;
                    fade=pow(saturate(sin(frac(input.seed.w+time*.19)*3.14159265)),2);
                    fade*=smoothstep(0,.06,phase.y)*smoothstep(0,.06,1-phase.y);
                }
                float3 view=normalize(_WeatherCameraPosition.xyz-center+float3(.0001,0,0));
                float3 side=cross(tangent,view);
                if(dot(side,side)<.0001)side=float3(1,0,0);else side=normalize(side);
                float distanceToCamera=length(center-_WeatherCameraPosition.xyz);
                // Thin sub-pixel-to-one-pixel ribbons; no large camera-facing rain cards obscuring characters.
                float worldPixel=2*max(distanceToCamera,1)/(max(abs(UNITY_MATRIX_P._m11),.01)*max(_ScreenParams.y,1));
                float width=_Mode<.5?clamp(worldPixel*.9,.008,.025):clamp(worldPixel*.75,.010,.032);
                float along=input.positionOS.x-.5;
                float3 position=center+tangent*(along*ribbonLength)+side*(input.positionOS.y*width);
                if(_Mode>.5)position.y+=sin(input.positionOS.x*3.14159265)*.23*sin(input.seed.z*20+time*.8);
                output.positionWS=position;output.positionCS=TransformWorldToHClip(position);output.uv=input.uv;
                float edgeFade=1-smoothstep(14,_Mode<.5?20:19,max(abs(center.x-_WeatherOrigin.x),abs(center.z-_WeatherOrigin.z)));
                float nearFade=smoothstep(.65,2.0,distanceToCamera);
                float shelter=lerp(1,smoothstep(5,13,distanceToCamera),saturate(_WeatherShelter));
                // Sorted occupancy supports gradual state transitions without building or resimulating a ParticleSystem.
                float occupancy=saturate((density-input.seed.w)*18);
                output.appearance=float3(fade*edgeFade*nearFade*shelter*occupancy,ComputeFogFactor(output.positionCS.z),storm);
                return output;
            }
            half4 WeatherFragment(Varyings input):SV_Target
            {
                float across=1-abs(input.uv.y*2-1);
                float along=sin(saturate(input.uv.x)*3.14159265);
                float shape=pow(saturate(across),.8)*pow(saturate(along),.65);
                float2 screenUV=GetNormalizedScreenSpaceUV(input.positionCS);
                float depth=SampleSceneDepth(screenUV);
                float eyeDepth=LinearEyeDepth(depth,_ZBufferParams);
                if(unity_OrthoParams.w>.5)
                {
                    #if UNITY_REVERSED_Z
                    depth=1-depth;
                    #endif
                    eyeDepth=lerp(_ProjectionParams.y,_ProjectionParams.z,depth);
                }
                float particleDepth=-TransformWorldToView(input.positionWS).z;
                // Real opaque camera depth prevents intersections with the authored Terrain and roof/wall meshes.
                float intersection=saturate((eyeDepth-particleDepth)/.45);
                float alpha=shape*input.appearance.x*intersection*_Opacity;
                alpha*=lerp(1,1.30,input.appearance.z);
                alpha=MixFogColor(alpha.xxx,half3(0,0,0),input.appearance.y).r;
                clip(alpha-.001);
                return half4(_BaseColor.rgb,alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
