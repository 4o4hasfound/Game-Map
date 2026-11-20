// Grime, water stains, soot: a texture that darkens whatever is behind it (multiply blend), independent of light.
// RGB is the stain colour, A how strongly it applies; _Strength scales the whole decal.
Shader "TheTape/Multiply"
{
    Properties
    {
        _MainTex ("Stain (RGB colour, A amount)", 2D) = "white" {}
        _Strength ("Strength", Range(0, 1.5)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend DstColor Zero
        ZWrite Off
        Cull Off
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Strength;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.uv, _MainTex); return o; }
            float4 frag (v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv);
                return float4(lerp(float3(1, 1, 1), t.rgb, saturate(t.a * _Strength)), 1);
            }
            ENDCG
        }
    }
}
