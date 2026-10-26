// Handheld camcorder recorded to VHS (built-in render pipeline, driven by VHSPostFX.cs).
// Passes 0-5 are the HW1 body-cam pipeline (auto exposure, bloom, rotational motion blur); pass 6 adds the tape:
//   per-line timebase jitter, tape wobble, rolling tracking band, head-switching noise at the bottom,
//   luma/chroma separation (sharp-ish luma, smeared and delayed chroma), edge-enhancement halos,
//   dropouts, snow, scanlines, depth haze, ACES tonemapping and a faded VHS colour response.
Shader "Hidden/VHSFX"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex, _LumTex, _PrevLumTex;
    sampler2D_float _CameraDepthTexture;
    float _AdaptSpeed, _DeltaTime, _First;
    float _Key, _MinExposure, _MaxExposure;
    float _Threshold, _Knee, _BloomIntensity;
    float4x4 _CurRot, _PrevInvRot;
    float _TanX, _TanY, _BlurScale, _MaxBlur;
    float _Distortion, _DistortScale, _Vignette, _Grain, _Frame, _TimeU;
    float _Jitter, _Wobble, _Tracking, _TrackPos, _HeadSwitch, _ChromaBlur, _ChromaShift, _Saturation, _BlackLevel, _Halo;
    float _Dropouts, _Scanlines, _FogDensity, _FF, _ChromaNoise;
    float4 _FogColor, _Tint;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

    float Hash12(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float Exposure()
    {
        float lum = tex2D(_LumTex, float2(0.5, 0.5)).r;
        return clamp(_Key / max(lum, 1e-4), _MinExposure, _MaxExposure);
    }

    float4 fragLogLum(v2f i) : SV_Target
    {
        float2 o = float2(0.25 / 64.0, 0.25 / 64.0);
        float l = 0;
        l += log(Luma(tex2D(_MainTex, i.uv + float2(-o.x, -o.y)).rgb) + 1e-4);
        l += log(Luma(tex2D(_MainTex, i.uv + float2( o.x, -o.y)).rgb) + 1e-4);
        l += log(Luma(tex2D(_MainTex, i.uv + float2(-o.x,  o.y)).rgb) + 1e-4);
        l += log(Luma(tex2D(_MainTex, i.uv + float2( o.x,  o.y)).rgb) + 1e-4);
        l *= 0.25;
        float w = 1 - 0.5 * saturate(length(i.uv - 0.5) * 1.6);
        return float4(l * w + (1 - w) * log(0.02), 0, 0, 1);
    }

    float4 fragAdapt(v2f i) : SV_Target
    {
        float cur = exp(tex2D(_MainTex, float2(0.5, 0.5)).r);
        if (_First > 0.5) return float4(cur, 0, 0, 1);
        float prev = tex2D(_PrevLumTex, float2(0.5, 0.5)).r;
        return float4(prev + (cur - prev) * (1 - exp(-_DeltaTime * _AdaptSpeed)), 0, 0, 1);
    }

    float4 fragPrefilter(v2f i) : SV_Target
    {
        float2 t = _MainTex_TexelSize.xy;
        float3 c = tex2D(_MainTex, i.uv + t * float2(-0.5, -0.5)).rgb + tex2D(_MainTex, i.uv + t * float2(0.5, -0.5)).rgb
                 + tex2D(_MainTex, i.uv + t * float2(-0.5, 0.5)).rgb + tex2D(_MainTex, i.uv + t * float2(0.5, 0.5)).rgb;
        c *= 0.25 * Exposure();
        float br = max(c.r, max(c.g, c.b));
        float soft = clamp(br - _Threshold + _Knee, 0, 2 * _Knee);
        soft = soft * soft / (4 * _Knee + 1e-5);
        float contrib = max(soft, br - _Threshold) / max(br, 1e-5);
        return float4(min(c * contrib, 20), 1);
    }

    float4 fragDown(v2f i) : SV_Target
    {
        float2 t = _MainTex_TexelSize.xy;
        float3 c = tex2D(_MainTex, i.uv).rgb * 4 + tex2D(_MainTex, i.uv + t * float2(-1, -1)).rgb + tex2D(_MainTex, i.uv + t * float2(1, -1)).rgb
                 + tex2D(_MainTex, i.uv + t * float2(-1, 1)).rgb + tex2D(_MainTex, i.uv + t * float2(1, 1)).rgb;
        return float4(c / 8, 1);
    }

    float4 fragUp(v2f i) : SV_Target
    {
        float2 t = _MainTex_TexelSize.xy;
        float3 c = tex2D(_MainTex, i.uv).rgb * 4;
        c += (tex2D(_MainTex, i.uv + float2(t.x, 0)).rgb + tex2D(_MainTex, i.uv - float2(t.x, 0)).rgb
            + tex2D(_MainTex, i.uv + float2(0, t.y)).rgb + tex2D(_MainTex, i.uv - float2(0, t.y)).rgb) * 2;
        c += tex2D(_MainTex, i.uv + t).rgb + tex2D(_MainTex, i.uv - t).rgb
           + tex2D(_MainTex, i.uv + float2(t.x, -t.y)).rgb + tex2D(_MainTex, i.uv + float2(-t.x, t.y)).rgb;
        return float4(c / 16, 1);
    }

    float4 fragMotion(v2f i) : SV_Target
    {
        float3 vd = float3((i.uv.x * 2 - 1) * _TanX, (i.uv.y * 2 - 1) * _TanY, 1);
        float3 world = mul((float3x3)_CurRot, vd);
        float3 pl = mul((float3x3)_PrevInvRot, world);
        float2 prevUV = float2(pl.x / (pl.z * _TanX), pl.y / (pl.z * _TanY)) * 0.5 + 0.5;
        float2 vel = (i.uv - prevUV) * _BlurScale;
        if (pl.z < 0.05) vel = 0;
        float len = length(vel);
        if (len > _MaxBlur) vel *= _MaxBlur / len;
        float jitter = Hash12(i.uv * _MainTex_TexelSize.zw) - 0.5;
        float3 acc = 0;
        for (int k = 0; k < 6; k++)
        {
            float s = (k + jitter) / 5.0 - 0.5;
            acc += tex2D(_MainTex, i.uv - vel * s).rgb;
        }
        // depth haze (dust in the air), in linear HDR before exposure
        float d = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));
        float f = 1 - exp(-_FogDensity * max(d - 1.5, 0));
        acc = lerp(acc / 6, _FogColor.rgb, saturate(f));
        return float4(acc, 1);
    }

    float3 RGB2YIQ(float3 c)
    {
        return float3(dot(c, float3(0.299, 0.587, 0.114)), dot(c, float3(0.596, -0.274, -0.322)), dot(c, float3(0.211, -0.523, 0.312)));
    }

    float3 YIQ2RGB(float3 y)
    {
        return float3(y.x + 0.956 * y.y + 0.621 * y.z, y.x - 0.272 * y.y - 0.647 * y.z, y.x - 1.106 * y.y + 1.703 * y.z);
    }

    #define VLINES 576.0
    // Sample on the tape's line raster, a little between lines (interlaced fields blur vertically).
    float3 Line(float2 uv, float ln)
    {
        float y0 = (ln + 0.5) / VLINES;
        // bilinear filtering between the two nearest screen rows supplies the slight vertical softness
        return tex2D(_MainTex, float2(uv.x, y0 + 0.25 / VLINES)).rgb;
    }

    float4 fragVHS(v2f i) : SV_Target
    {
        float2 px = _MainTex_TexelSize.xy;
        float2 uv = i.uv;
        float aspect = _MainTex_TexelSize.z / _MainTex_TexelSize.w;
        // one tape "pixel" is about 1/480 of the picture width whatever the screen resolution
        float tx = 1.0 / 480.0;
        // small camcorder lens barrel
        float2 c = uv - 0.5;
        float r2 = dot(float2(c.x * aspect, c.y), float2(c.x * aspect, c.y));
        uv = 0.5 + c * (1 + _Distortion * r2) * _DistortScale;

        // timebase errors: every video line is a little early or late, the whole frame wobbles,
        // and the top of the picture bends sideways (flagging) as the capstan speed hunts
        float ln = floor(uv.y * VLINES);
        float lj = (Hash12(float2(ln, floor(_Frame * 0.5))) - 0.5) * _Jitter;
        float wob = sin(uv.y * 7 + _TimeU * 1.3) * sin(uv.y * 31 + _TimeU * 0.7) * _Wobble;
        float flag = pow(saturate((uv.y - 0.9) / 0.1), 2) * sin(_TimeU * 0.9 + sin(_TimeU * 2.3)) * _Wobble * 0.6;
        float dx = (lj + wob + flag) * tx * 0.5;

        // tracking band rolling through the picture
        float band = 0;
        if (_Tracking > 0.001)
        {
            float bd = abs(uv.y - _TrackPos);
            band = saturate(1 - bd / (0.03 + 0.08 * _Tracking)) * _Tracking;
            dx += (Hash12(float2(ln, _Frame)) - 0.3) * band * 0.05;
        }
        // head switching: the bottom lines are torn sideways and noisy
        float hs = saturate((0.028 - uv.y) / 0.028) * _HeadSwitch;
        dx += hs * (0.015 + 0.02 * Hash12(float2(floor(ln * 0.5), floor(_Frame * 0.5))));
        // fast-forward search: big noise bars
        if (_FF > 0.001)
        {
            float bar = step(0.85, frac(uv.y * 3 - _TimeU * 2.3));
            dx += bar * _FF * (Hash12(float2(ln, _Frame)) - 0.5) * 0.1;
            band = max(band, bar * _FF);
        }
        float2 suv = float2(uv.x + dx, uv.y);

        // luma: ~3 MHz bandwidth (about 250 lines across): soft horizontal Gaussian
        float ys = 0, yw = 0;
        for (int k = -2; k <= 2; k++)
        {
            float w = exp(-k * k / 2.6);
            ys += RGB2YIQ(Line(suv + float2(k * tx * 0.7, 0), ln)).x * w;
            yw += w;
        }
        float Y = ys / yw;
        // chroma: under 0.5 MHz: a wide smear, delayed to the right and one line down
        float2 cs = 0;
        float cw = 0;
        for (int k2 = -2; k2 <= 2; k2++)
        {
            float w = exp(-k2 * k2 / 2.5);
            cs += RGB2YIQ(tex2D(_MainTex, float2(suv.x + (k2 * _ChromaBlur - _ChromaShift) * tx, (ln - 0.5) / VLINES)).rgb).yz * w;
            cw += w;
        }
        float2 IQ = cs / cw;
        // VCR sharpening: ringing on either side of edges
        float yl = (ln + 0.5) / VLINES;
        float yb = Luma(tex2D(_MainTex, float2(suv.x - 2.2 * tx, yl)).rgb) * 0.5 + Luma(tex2D(_MainTex, float2(suv.x + 2.2 * tx, yl)).rgb) * 0.5;
        Y += (Y - yb) * _Halo;
        // chroma noise: slow streaks of colour along each line
        float cseg = floor(uv.x * 40 + Hash12(float2(ln, 3)) * 7);
        float2 cn = float2(Hash12(float2(cseg, ln + _Frame * 13.1)), Hash12(float2(cseg + 91, ln + _Frame * 7.7))) - 0.5;
        IQ += cn * _ChromaNoise * (0.35 + 0.65 * saturate(Y * 6));   // colour noise shows mostly in lit areas
        float3 col = max(YIQ2RGB(float3(Y, IQ)), 0);

        float exposure = Exposure();
        col = col * exposure + tex2D(_BloomTex, suv).rgb * _BloomIntensity;
        col = saturate((col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14));

        // faded tape colour response: lifted blacks, soft clipped whites, washed colour in the shadows
        float l = Luma(col);
        col = lerp(l.xxx, col, _Saturation * lerp(0.55, 1.0, saturate(l * 3)));
        col *= _Tint.rgb;
        col = 1 - (1 - col) * (1 - col * 0.12);           // highlight shoulder
        col = col * (0.93 - _BlackLevel) + _BlackLevel;

        // tape noise: short horizontal streaks (the noise of the FM luma carrier), not digital grain;
        // amplitude is fixed so dark rooms stay readable
        float2 pp = floor(i.uv * _MainTex_TexelSize.zw);
        float seg = floor(uv.x * 480 / (2 + 3 * Hash12(float2(ln, floor(_Frame)))));
        float streak = Hash12(float2(seg, ln + _Frame * 17.3)) - 0.5;
        float fine = Hash12(pp + _Frame * 17.3) - 0.5;
        col += (streak * 0.75 + fine * 0.35) * min(_Grain, 0.03) * (0.6 + 0.4 * saturate(l * 4));
        float lineN = Hash12(float2(ln * 1.7, _Frame));
        if (lineN > 1 - _Dropouts)
        {
            float xs = Hash12(float2(ln, _Frame * 3.1));
            float sg = smoothstep(0.0, 0.02, uv.x - xs) * (1 - smoothstep(0.0, 0.03, uv.x - xs - 0.06));
            col = lerp(col, col * 1.5 + 0.12, sg * 0.5);
        }
        col = lerp(col, float3(1, 1, 1) * (Hash12(pp * 0.5 + _Frame) * 0.8), saturate(band * 0.22 * Hash12(float2(ln, _Frame * 2))));
        col = lerp(col, float3(1, 1, 1) * Hash12(float2(floor(pp.x / 3), ln + _Frame)) * 0.6, hs * 0.45);

        // line structure and vignette
        col *= 1 - _Scanlines * (0.5 + 0.5 * sin(i.uv.y * _MainTex_TexelSize.w * 3.14159));
        col *= 1 - _Vignette * smoothstep(0.15, 1.1, r2);
        if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) col = 0;
        return float4(saturate(col), 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragLogLum
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragAdapt
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragPrefilter
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDown
            ENDCG }
        Pass { Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragUp
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragMotion
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragVHS
            #pragma target 3.0
            ENDCG }
    }
    Fallback Off
}
