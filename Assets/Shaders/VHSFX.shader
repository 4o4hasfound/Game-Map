// Handheld camcorder recorded to VHS, ported from the earlier HotelGame (VHSFX.shader) to blits driven by VhsScreen.cs.
// Passes 0-5: auto exposure (log-luminance + adaptation), bloom (prefilter / down / up), rotational motion blur.
// Pass 6, the tape: per-line timebase jitter, tape wobble and flagging, a rolling tracking band (the tape reacts
// when it is near), head-switching noise at the bottom, luma/chroma separation (soft luma, smeared and delayed
// chroma), edge-enhancement halos, chroma noise, dropouts, streak noise, ACES tonemapping and a faded colour response,
// fast-forward bars (being caught, being pulled in), scanlines and vignette.
Shader "Hidden/TheTape/VHSFX"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex, _LumTex, _PrevLumTex, _SmearTex, _PrevTex;
    float _Smear, _Ghost, _Motion, _Focus, _Overscan;
    float4 _WB;
    float _AdaptSpeed, _DeltaTime, _First;
    float _Key, _MinExposure, _MaxExposure, _Bright, _Guard;
    float _Threshold, _Knee, _BloomIntensity;
    float4x4 _CurRot, _PrevInvRot;
    float _TanX, _TanY, _BlurScale, _MaxBlur;
    float _Distortion, _DistortScale, _Vignette, _Grain, _Frame, _TimeU;
    float _Jitter, _Wobble, _Tracking, _TrackPos, _HeadSwitch, _ChromaBlur, _ChromaShift, _Saturation, _BlackLevel, _Halo;
    float _Dropouts, _Scanlines, _FF, _ChromaNoise;
    float4 _Tint;

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
        return clamp(_Key / max(lum, 1e-4), _MinExposure, _MaxExposure) * _Bright;
    }

    float4 fragLogLum(v2f i) : SV_Target
    {
        float2 o = float2(0.25 / 64.0, 0.25 / 64.0);
        // r: log luminance (the geometric mean the exposure follows); g: plain luminance clamped at 2, so a lit
        // wall or a lightning flash filling part of the frame pulls the gain down (a camcorder iris protecting its
        // highlights) while a small bare bulb doesn't
        float a = Luma(tex2D(_MainTex, i.uv + float2(-o.x, -o.y)).rgb);
        float b = Luma(tex2D(_MainTex, i.uv + float2( o.x, -o.y)).rgb);
        float c = Luma(tex2D(_MainTex, i.uv + float2(-o.x,  o.y)).rgb);
        float d = Luma(tex2D(_MainTex, i.uv + float2( o.x,  o.y)).rgb);
        float l = (log(a + 1e-4) + log(b + 1e-4) + log(c + 1e-4) + log(d + 1e-4)) * 0.25;
        float h = (min(a, 2) + min(b, 2) + min(c, 2) + min(d, 2)) * 0.25;
        float w = 1 - 0.5 * saturate(length(i.uv - 0.5) * 1.6);
        return float4(l * w + (1 - w) * log(0.02), h * w + (1 - w) * 0.02, 0, 1);
    }

    float4 fragAdapt(v2f i) : SV_Target
    {
        float2 m = tex2D(_MainTex, float2(0.5, 0.5)).rg;
        float cur = max(exp(m.r), m.g * _Guard);
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

    // camera rotation between frames smears the picture like a 1/50 s camcorder shutter
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
        return float4(acc / 6, 1);
    }

    // CCD smear: a bright light bleeds a vertical streak up and down the whole column (90s camcorder sensors)
    float4 fragSmear(v2f i) : SV_Target
    {
        float3 acc = 0;
        float wsum = 0;
        for (int k = 0; k < 32; k++)
        {
            float y = (k + 0.5) / 32.0;
            float w = exp(-abs(y - i.uv.y) * 2.5);
            float3 c = tex2D(_MainTex, float2(i.uv.x, y)).rgb;
            acc += max(c - 2.5, 0) * w;
            wsum += w;
        }
        return float4(acc / wsum * float3(0.9, 0.95, 1.1), 1);
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
    // sample on the tape's line raster (bilinear filtering between screen rows gives the interlaced softness)
    float3 Line(float2 uv, float ln)
    {
        float y0 = (ln + 0.5) / VLINES;
        return tex2D(_MainTex, float2(uv.x, y0 + 0.25 / VLINES)).rgb;
    }

    float4 fragVHS(v2f i) : SV_Target
    {
        float2 uv = i.uv;
        float aspect = _MainTex_TexelSize.z / _MainTex_TexelSize.w;
        // one tape "pixel" is about 1/480 of the picture width whatever the screen resolution
        float tx = 1.0 / 480.0;
        // small camcorder lens barrel
        float2 c = uv - 0.5;
        float r2 = dot(float2(c.x * aspect, c.y), float2(c.x * aspect, c.y));
        uv = 0.5 + c * (1 + _Distortion * r2) * _DistortScale;

        // timebase errors: every line a little early or late, the frame wobbles, the top bends (flagging)
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

        // luma: ~3 MHz bandwidth: soft horizontal Gaussian
        float ys = 0, yw = 0;
        for (int k = -2; k <= 2; k++)
        {
            float w = exp(-k * k / 2.6);
            ys += RGB2YIQ(Line(suv + float2(k * tx * (0.95 + _Focus * 3.0), _Focus * k * 0.0015), ln)).x * w;
            yw += w;
        }
        float Y = ys / yw;
        // chroma: under 0.5 MHz: a wide smear, delayed to the right and half a line down.
        // It is averaged on a soft-clipped picture and scaled back by the local luma: in HDR a bare bulb carries
        // chroma dozens of times larger than the wall beside it, and smearing that raw painted red halos next to
        // every light
        float2 cs = 0;
        float cw = 0;
        for (int k2 = -2; k2 <= 2; k2++)
        {
            float w = exp(-k2 * k2 / 2.5);
            float3 cr = tex2D(_MainTex, float2(suv.x + (k2 * _ChromaBlur * (1 + _Focus) - _ChromaShift) * tx, (ln - 0.5) / VLINES)).rgb;
            cs += RGB2YIQ(cr / (1 + Luma(cr) * 4)).yz * w;
            cw += w;
        }
        float2 IQ = cs / cw * (1 + max(Y, 0) * 4);
        // VCR sharpening: ringing on either side of edges
        float yl = (ln + 0.5) / VLINES;
        float yb = Luma(tex2D(_MainTex, float2(suv.x - 2.2 * tx, yl)).rgb) * 0.5 + Luma(tex2D(_MainTex, float2(suv.x + 2.2 * tx, yl)).rgb) * 0.5;
        Y += (Y - yb) * _Halo;
        // chroma noise: slow streaks of colour along each line
        float cseg = floor(uv.x * 40 + Hash12(float2(ln, 3)) * 7);
        float2 cn = float2(Hash12(float2(cseg, ln + _Frame * 13.1)), Hash12(float2(cseg + 91, ln + _Frame * 7.7))) - 0.5;
        IQ += cn * _ChromaNoise * (0.35 + 0.65 * saturate(Y * 6));
        float3 col = max(YIQ2RGB(float3(Y, IQ)), 0);

        float exposure = Exposure();
        col = col * exposure + tex2D(_BloomTex, suv).rgb * _BloomIntensity + tex2D(_SmearTex, float2(suv.x, i.uv.y)).rgb * _Smear;
        col *= _WB.rgb;                                    // auto white balance, never quite right
        col = saturate((col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14));

        // faded tape colour response: lifted blacks, soft clipped whites, washed colour
        float l = Luma(col);
        col = lerp(l.xxx, col, _Saturation * lerp(0.88, 1.0, saturate(l * 3)));
        col *= _Tint.rgb;
        col = 1 - (1 - col) * (1 - col * 0.12);
        col = col * (0.93 - _BlackLevel) + _BlackLevel;

        // tape noise: short horizontal streaks (FM luma carrier noise); the camcorder gains up in the dark
        float2 pp = floor(i.uv * _MainTex_TexelSize.zw);
        float seg = floor(uv.x * 480 / (2 + 3 * Hash12(float2(ln, floor(_Frame)))));
        float streak = Hash12(float2(seg, ln + _Frame * 17.3)) - 0.5;
        float fine = Hash12(pp + _Frame * 17.3) - 0.5;
        float gainUp = saturate((exposure - 1.2) / 1.3);
        float chunky = Hash12(float2(floor(pp.x / 2), floor(pp.y / 2)) + _Frame * 5.1) - 0.5;      // gain-up snow comes in clumps
        col += (streak * 0.75 + fine * 0.25 + chunky * 0.2 * gainUp) * min(_Grain, 0.05) * (0.35 + 0.65 * saturate(l * 4) + 0.3 * gainUp);
        float lineN = Hash12(float2(ln * 1.7, _Frame));
        if (lineN > 1 - _Dropouts)
        {
            float xs = Hash12(float2(ln, _Frame * 3.1));
            float sg = smoothstep(0.0, 0.02, uv.x - xs) * (1 - smoothstep(0.0, 0.03, uv.x - xs - 0.06));
            col = lerp(col, col * 1.5 + 0.12, sg * 0.5);
        }
        col = lerp(col, float3(1, 1, 1) * (Hash12(pp * 0.5 + _Frame) * 0.8), saturate(band * 0.22 * Hash12(float2(ln, _Frame * 2))));
        col = lerp(col, float3(1, 1, 1) * Hash12(float2(floor(pp.x / 3), ln + _Frame)) * 0.6, hs * 0.45);

        // low light: the slow shutter leaves trails; fast pans comb the interlaced fields
        float3 prev = tex2D(_PrevTex, i.uv).rgb;
        float ghost = _Ghost * (0.4 + 0.6 * gainUp);
        col = lerp(col, prev, saturate(ghost));
        float odd = step(0.5, frac(floor(i.uv.y * VLINES) * 0.5));
        col = lerp(col, prev, odd * saturate(_Motion) * 0.55);
        // line structure and vignette
        col *= 1 - _Scanlines * (0.5 + 0.5 * sin(i.uv.y * _MainTex_TexelSize.w * 3.14159));
        col *= 1 - _Vignette * smoothstep(0.15, 1.1, r2);
        if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) col = 0;
        // overscan: the blanking edges of the picture, ragged with timebase error
        float edgeL = _Overscan + (Hash12(float2(ln, floor(_Frame * 0.5))) - 0.5) * 0.004;
        float blank = step(i.uv.x, edgeL) + step(1 - edgeL * 0.6, i.uv.x);
        col = lerp(col, float3(0.012, 0.012, 0.014), saturate(blank));
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
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragSmear
            #pragma target 3.0
            ENDCG }
    }
    Fallback Off
}
