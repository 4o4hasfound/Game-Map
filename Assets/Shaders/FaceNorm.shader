// Crops a region of the webcam picture. _Crop = (scaleX, scaleY, offsetX, offsetY) in uv.
// Mode 0: colour scaled to -1..1 for BlazeFace. Mode 1: grey (eye patches). Mode 2: plain colour (face crop).
// _ToGamma: the project renders in linear colour, and Unity may decode the camera picture to linear when it is
// sampled; the face model expects ordinary (gamma) camera values, so they are encoded back. _Gain brightens a dim
// picture (a dark room at night) towards a normal exposure before detection.
Shader "Hidden/TheTape/FaceNorm"
{
    Properties { _MainTex ("Tex", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Crop;
            float _Mode;
            float _ToGamma;
            float _Gain;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            float4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv * _Crop.xy + _Crop.zw;
                float4 c = tex2D(_MainTex, uv);
                if (any(uv < 0) || any(uv > 1)) c = 0;
                float3 rgb = c.rgb;
                if (_ToGamma > 0.5) rgb = LinearToGammaSpace(max(rgb, 0));
                rgb = saturate(rgb * max(_Gain, 1));
                if (_Mode < 0.5) return float4(rgb * 2 - 1, 1);
                if (_Mode < 1.5) { float g = dot(rgb, float3(0.3, 0.59, 0.11)); return float4(g, g, g, 1); }
                return float4(rgb, 1);
            }
            ENDCG
        }
    }
}
