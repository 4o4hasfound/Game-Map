// Light in dusty air: an additive cone under a lamp (and the dust motes floating in it). Brightest along the axis,
// fading towards the floor and at grazing edges, fading with distance like the fog. The game drives _Color with
// the lamp's flicker level, so the shaft stutters with the light.
Shader "TheTape/LightShaft"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "white" {}
        _Color ("Colour", Color) = (1, 0.9, 0.7, 0.2)
        _EdgePower ("Edge softness", Float) = 2.4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _EdgePower;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float facing : TEXCOORD1; float dist : TEXCOORD2; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 vd = normalize(_WorldSpaceCameraPos - wp);
                o.facing = abs(dot(n, vd));
                o.dist = distance(_WorldSpaceCameraPos, wp);
                return o;
            }
            float4 frag (v2f i) : SV_Target
            {
                float m = tex2D(_MainTex, i.uv).r;
                float edge = pow(saturate(i.facing), _EdgePower);
                float fade = exp(-i.dist * 0.07) * saturate((i.dist - 0.4) / 1.2);     // no flat sheet in your face
                float a = m * edge * fade * i.color.a * i.color.a * _Color.a;      // fades out well before the floor
                return float4(_Color.rgb * i.color.rgb * a, 1);
            }
            ENDCG
        }
    }
}
