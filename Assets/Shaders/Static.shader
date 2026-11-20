// White static: the edge of the tape (the street doors) and dead screens.
Shader "TheTape/Static"
{
    Properties { _Level ("Level", Float) = 0.85 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _Level;
            struct A { float4 v : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert (A i) { V o; o.p = TransformObjectToHClip(i.v.xyz); o.uv = i.uv; return o; }
            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            float4 frag (V i) : SV_Target
            {
                float2 c = floor(i.uv * float2(160, 120));
                float n = hash(c + floor(_Time.y * 30) * 17.13);
                float row = hash(float2(floor(i.uv.y * 60), floor(_Time.y * 12)));
                float v = (n * 0.75 + row * 0.25) * _Level;
                return float4(v, v, v * 1.03, 1);
            }
            ENDHLSL
        }
    }
}
