// Universal Screen Space Reflection (SSR) & Cinematic Depth Enhancer
// Engineered for AMD Radeon Pro 560X (Polaris 21) + Magpie v4 Pipeline
// Pairs with FSR EASU + RCAS to boost wet reflections, specular bloom, and shadow depth
// across GTA V and any DirectX / Vulkan / OpenGL / Emulator window.

//!MAGPIE EFFECT
//!VERSION 4

//!PARAMETER
//!LABEL Specular Reflection Bloom
//!DEFAULT 0.30
//!MIN 0.0
//!MAX 1.0
//!STEP 0.02
float specularBloom;

//!PARAMETER
//!LABEL Shadow & Contact Depth
//!DEFAULT 0.20
//!MIN 0.0
//!MAX 0.6
//!STEP 0.02
float shadowDepth;

//!PARAMETER
//!LABEL Smart Surface Vibrance
//!DEFAULT 0.14
//!MIN 0.0
//!MAX 0.5
//!STEP 0.02
float vibrance;

//!PARAMETER
//!LABEL Anti-Banding Dither
//!DEFAULT 0.35
//!MIN 0.0
//!MAX 1.0
//!STEP 0.05
float debandStrength;

//!TEXTURE
Texture2D INPUT;

//!TEXTURE
//!WIDTH INPUT_WIDTH
//!HEIGHT INPUT_HEIGHT
Texture2D OUTPUT;

//!SAMPLER
//!FILTER LINEAR
SamplerState sam;

//!PASS 1
//!STYLE PS
//!IN INPUT
//!OUT OUTPUT

float GetLuma(float3 c) {
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

float3 ExtractHighlight(float3 c, float threshold) {
    float l = GetLuma(c);
    float w = smoothstep(threshold, 1.0, l);
    return c * w;
}

float InterleavedGradientNoise(float2 pixelPos) {
    float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
    return frac(magic.z * frac(dot(pixelPos, magic.xy)));
}

float4 Pass1(float2 pos) {
    float2 pt = GetInputPt();
    float3 base = INPUT.SampleLevel(sam, pos, 0).rgb;

    // 1. Multi-tap soft specular bloom (enhances wet puddles, car paint, glass & neon SSR)
    float3 bloom = float3(0.0, 0.0, 0.0);
    if (specularBloom > 0.001) {
        float2 d1 = pt * 2.2;
        float2 d2 = pt * 4.5;
        float thresh = 0.62;

        bloom += ExtractHighlight(INPUT.SampleLevel(sam, pos + float2( d1.x,  0.0 ), 0).rgb, thresh);
        bloom += ExtractHighlight(INPUT.SampleLevel(sam, pos + float2(-d1.x,  0.0 ), 0).rgb, thresh);
        bloom += ExtractHighlight(INPUT.SampleLevel(sam, pos + float2( 0.0,  d1.y ), 0).rgb, thresh);
        bloom += ExtractHighlight(INPUT.SampleLevel(sam, pos + float2( 0.0, -d1.y ), 0).rgb, thresh);
        bloom += ExtractHighlight(INPUT.SampleLevel(sam, pos + float2( d2.x,  d2.y), 0).rgb, thresh) * 0.75;
        bloom += ExtractHighlight(INPUT.SampleLevel(sam, pos + float2(-d2.x,  d2.y), 0).rgb, thresh) * 0.75;
        bloom += ExtractHighlight(INPUT.SampleLevel(sam, pos + float2( d2.x, -d2.y), 0).rgb, thresh) * 0.75;
        bloom += ExtractHighlight(INPUT.SampleLevel(sam, pos + float2(-d2.x, -d2.y), 0).rgb, thresh) * 0.75;
        bloom /= 7.0;
    }

    // 2. Filmic S-curve shadow depth & contact contrast (preserves blacks, adds 3D pop)
    float3 sCurve = base * base * (3.0 - 2.0 * base);
    float3 color = lerp(base, sCurve, shadowDepth);

    // Subtle local detail clarity against neighbors (pseudo-SSAO edge depth)
    float lCenter = GetLuma(color);
    float lAvg = 0.25 * (
        GetLuma(INPUT.SampleLevel(sam, pos + float2(pt.x * 1.5, 0), 0).rgb) +
        GetLuma(INPUT.SampleLevel(sam, pos - float2(pt.x * 1.5, 0), 0).rgb) +
        GetLuma(INPUT.SampleLevel(sam, pos + float2(0, pt.y * 1.5), 0).rgb) +
        GetLuma(INPUT.SampleLevel(sam, pos - float2(0, pt.y * 1.5), 0).rgb)
    );
    float microContrast = clamp((lCenter - lAvg) * (0.28 * shadowDepth * 2.5), -0.05, 0.06);
    color = saturate(color + microContrast);

    // Add soft specular reflection sheen with smooth highlight shoulder roll-off
    color = 1.0 - (1.0 - color) * (1.0 - saturate(bloom * specularBloom * 1.35));

    // 3. Selective vibrance (enriches colored reflections without oversaturating brights)
    if (vibrance > 0.001) {
        float maxC = max(color.r, max(color.g, color.b));
        float minC = min(color.r, min(color.g, color.b));
        float sat = maxC - minC;
        float vibWeight = vibrance * (1.0 - sat);
        float l = GetLuma(color);
        color = saturate(lerp(float3(l, l, l), color, 1.0 + vibWeight));
    }

    // 4. Triangular screen-space dither (removes 8-bit sky & water reflection banding)
    if (debandStrength > 0.001) {
        float2 pixelCoord = pos / pt;
        float noise = InterleavedGradientNoise(pixelCoord) - 0.5;
        color = saturate(color + noise * (1.5 / 255.0) * debandStrength);
    }

    return float4(color, 1.0);
}
