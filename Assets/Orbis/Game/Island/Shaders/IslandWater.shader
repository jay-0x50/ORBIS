Shader "Orbis/Island Water"
{
    Properties
    {
        _HeightMap("Island elevation",2D)="black"{}
        _WaterLevel("Water elevation (m)",Float)=0
        _DeepColor("Deep water",Color)=(.045,.28,.41,1)
        _ShallowColor("Shallow water",Color)=(.22,.65,.64,1)
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+20"}
        Pass
        {
            Name "Water" Tags {"LightMode"="UniversalForward"}
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_HeightMap);SAMPLER(sampler_HeightMap);
            CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor,_ShallowColor;
            float _WaterLevel;
            CBUFFER_END
            struct Input {float4 positionOS:POSITION;};
            struct Output {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;};
            Output vert(Input input)
            {Output output;output.positionWS=TransformObjectToWorld(input.positionOS.xyz);output.positionCS=TransformWorldToHClip(output.positionWS);return output;}
            half4 frag(Output input):SV_Target
            {
                float2 uv=(input.positionWS.xz+1000)/2000;
                float ground=SAMPLE_TEXTURE2D(_HeightMap,sampler_HeightMap,saturate(uv)).r*320-60;
                if(any(uv<0)||any(uv>1))ground=-60;
                float depth=max(0,_WaterLevel-ground);
                half3 color=lerp(_ShallowColor.rgb,_DeepColor.rgb,saturate(depth/18));
                float2 p=input.positionWS.xz;
                float wave=sin(p.x*.30+p.y*.16+_Time.y*.8)+sin(p.y*.37-p.x*.1-_Time.y*.6);
                float crest=pow(saturate(wave*.35+.33),14);
                // Fade sub-metre crests in distant views to avoid a repeated dot grid on the overview.
                color+=crest*.04*saturate(1-distance(GetCameraPositionWS(),input.positionWS)/250);
                float foam=(1-smoothstep(.12,.8,depth))*(.62+.25*sin(p.x*1.3+p.y*.9+_Time.y));
                color=lerp(color,half3(.82,.94,.88),foam);
                float3 n=normalize(float3(cos(p.x*.17+_Time.y)*.04,1,sin(p.y*.21+_Time.y*.7)*.04));
                float3 view=GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light sun=GetMainLight();
                color+=pow(saturate(dot(n,normalize(view+sun.direction))),140)*.25;
                // Fragment fog avoids visible diagonals on a large sea plane.
                float fog=ComputeFogFactor(TransformWorldToHClip(input.positionWS).z);
                return half4(MixFog(color,fog),1);
            }
            ENDHLSL
        }
    }
}
