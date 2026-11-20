// Your face on the avatar's head (seen on CCTV monitors and in mirrors), worn by tape damage.
// _Erase: 0 clear, 0.33 a smear over the eyes, 0.67 half covered by the blob, 1 gone.
Shader "TheTape/FaceTape"
{
    Properties
    {
        _FaceTex ("Face (webcam crop)", 2D) = "grey" {}
        _DefaultFace ("Default face", 2D) = "grey" {}
        _UseCam ("Use camera face", Float) = 0
        _Erase ("Erase", Range(0,1)) = 0
    }
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
            TEXTURE2D(_FaceTex); SAMPLER(sampler_FaceTex);
            TEXTURE2D(_DefaultFace); SAMPLER(sampler_DefaultFace);
            float _UseCam, _Erase;
            struct A { float4 v : POSITION; float3 n : NORMAL; };
            struct V { float4 p : SV_POSITION; float3 o : TEXCOORD0; float3 n : TEXCOORD1; };
            V vert (A i) { V o; o.p = TransformObjectToHClip(i.v.xyz); o.o = i.v.xyz; o.n = i.n; return o; }
            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            float noise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y); }
            float4 frag (V i) : SV_Target
            {
                // front of the head (+z) shows the face; the rest is hair
                float2 uv = float2(i.o.x, i.o.y) * 0.95 + 0.5;
                float front = smoothstep(0.05, 0.35, i.o.z);
                float t = _Time.y;
                float row = floor(uv.y * 48);
                uv.x += (hash(float2(row, floor(t * 12))) - 0.5) * 0.02 * (0.3 + _Erase * 2);
                float3 face = _UseCam > 0.5 ? SAMPLE_TEXTURE2D(_FaceTex, sampler_FaceTex, uv).rgb : SAMPLE_TEXTURE2D(_DefaultFace, sampler_DefaultFace, uv).rgb;
                float g = dot(face, float3(0.3, 0.59, 0.11));
                face = lerp(face, g.xxx, 0.45) * 1.05;
                face *= 0.9 + 0.1 * fmod(row, 2);
                float3 hair = float3(0.05, 0.045, 0.04);
                float3 col = lerp(hair, face, front);
                // erasure: a smear across the eyes first, then the blob takes the rest
                float blobN = noise(uv * 5 + float2(t * 0.7, -t * 0.3)) * 0.6 + noise(uv * 13 - t) * 0.4;
                float eyes = smoothstep(0.12, 0.0, abs(uv.y - 0.58)) * step(0.2, _Erase);
                float cover = saturate(step(blobN, (_Erase - 0.3) * 1.6) + eyes * 0.85);
                if (_Erase > 0.99) cover = 1;
                float3 blob = float3(0.07, 0.07, 0.08) + (hash(uv * 200 + floor(t * 20)) - 0.5) * 0.25;
                col = lerp(col, blob, cover * saturate(front + 0.3));
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
