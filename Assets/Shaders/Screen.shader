// A CRT showing a CCTV camera: monochrome-ish, scanlines, slight flicker. Unlit (it glows).
Shader "TheTape/Screen"
{
    Properties
    {
        _MainTex ("Feed", 2D) = "black" {}
        _On ("On", Float) = 1
        _Mono ("Mono", Float) = 0.85
        _Bright ("Bright", Float) = 1.1
        _Gain ("IR gain", Float) = 7
        _Snow ("Snow (no signal)", Float) = 0
        _Roll ("Roll (bad tracking)", Float) = 0
        _Clean ("Clean picture", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _On, _Mono, _Bright, _Gain, _Snow, _Roll, _Clean;
            struct A { float4 v : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert (A i) { V o; o.p = TransformObjectToHClip(i.v.xyz); o.uv = i.uv; return o; }
            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            float4 frag (V i) : SV_Target
            {
                if (_On < 0.5) return float4(0.015, 0.017, 0.016, 1);
                float2 uv = i.uv;
                // bad tracking: the picture rolls and the lines wave
                uv.y = frac(uv.y + _Roll);
                uv.x += sin(uv.y * 30 + _Time.y * 25) * 0.025 * saturate(_Snow * 1.5 + _Roll * 3);
                float2 d = uv - 0.5;
                uv = 0.5 + d * (1 + dot(d, d) * lerp(0.12, 0.05, _Clean));        // tube curvature
                float3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                c = 1 - exp(-c * lerp(_Gain, 2.6, _Clean));     // CCTV with infrared: sees into the dark (the tape: a camcorder)
                float g = dot(c, float3(0.3, 0.59, 0.11));
                c = lerp(c, float3(g, g * 1.02, g * 0.98), lerp(_Mono, 0.0, _Clean));     // the tape is in colour
                float row = floor(uv.y * 240);
                c *= 1 - (1 - _Clean) * 0.15 * (1 - fmod(row, 2));          // scanlines (not on the clean picture)
                c += (hash(uv * 300 + _Time.y * 13) - 0.5) * lerp(0.06, 0.012, _Clean);
                c *= 1 - (1 - _Clean) * 0.05 * (1 - sin(_Time.y * 50));
                c *= _Bright;
                // snow, and the dark seam between frames while it rolls
                float sn = hash(floor(i.uv * float2(240, 180)) + floor(_Time.y * 24) * 7.13);
                c = lerp(c, sn.xxx * 0.9, saturate(_Snow));
                if (_Roll > 0.001 && uv.y < 0.035) c *= 0.12;
                if (any(uv < 0) || any(uv > 1)) c = 0;
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
