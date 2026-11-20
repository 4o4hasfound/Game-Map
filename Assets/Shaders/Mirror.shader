// A mirror: shows the reflection camera's texture at this pixel's screen position.
Shader "TheTape/Mirror"
{
    Properties { _MainTex ("Reflection", 2D) = "black" {} _Tint ("Tint", Color) = (0.8, 0.82, 0.8, 1) }
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Tint;
            struct A { float4 v : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float4 s : TEXCOORD0; float2 uv : TEXCOORD1; };
            V vert (A i) { V o; o.p = TransformObjectToHClip(i.v.xyz); o.s = ComputeScreenPos(o.p); o.uv = i.uv; return o; }
            float4 frag (V i) : SV_Target
            {
                float2 suv = i.s.xy / i.s.w;
                float3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, suv).rgb * _Tint.rgb;
                // old silvering: darker spots towards the edges
                float2 d = abs(i.uv - 0.5) * 2;
                c *= 1 - pow(max(d.x, d.y), 6) * 0.6;
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
