Texture2D SourceTexture : register(t0);
SamplerState SourceSampler : register(s0);
Texture2D RingTexture : register(t1);
SamplerState RingSampler : register(s1);
Texture2D ScatterTexture : register(t2);
SamplerState ScatterSampler : register(s2);

cbuffer Constants : register(b0)
{
    float4 inputBounds : packoffset(c0);
    float3 sun : packoffset(c1.x);
    float halfFieldTangent : packoffset(c1.w);
    float pitchCosine : packoffset(c2.x);
    float pitchSine : packoffset(c2.y);
    float sunAzimuth : packoffset(c2.z);
    float sunElevation : packoffset(c2.w);
    float amount : packoffset(c3.x);
    float ringStart : packoffset(c3.y);
    float ringSpan : packoffset(c3.z);
    float ringWidth : packoffset(c3.w);
    float scatterWidth : packoffset(c4.x);
    float scatterHeight : packoffset(c4.y);
    float scatterTexel : packoffset(c4.z);
    float scatterHalfSpan : packoffset(c4.w);
};

static const float Pi = 3.14159265358979;
static const float TwoPi = 2.0 * Pi;
static const float DegreesPerRadian = 180.0 / Pi;
static const float HalfTexel = 0.5;

float3 SrgbToLinear(float3 encoded)
{
    float3 low = encoded / 12.92;
    float3 high = pow((encoded + 0.055) / 1.055, 2.4);
    return lerp(high, low, step(encoded, 0.04045));
}

float3 LinearToSrgb(float3 linearColor)
{
    float3 low = linearColor * 12.92;
    float3 high = 1.055 * pow(linearColor, 1.0 / 2.4) - 0.055;
    return lerp(high, low, step(linearColor, 0.0031308));
}

float4 main(
    float4 position : SV_POSITION,
    float4 scenePosition : SCENE_POSITION,
    float4 uv0 : TEXCOORD0
) : SV_TARGET
{
    float4 source = SourceTexture.SampleLevel(SourceSampler, uv0.xy, 0);
    if (amount <= 0.0 || source.a <= 0.0)
        return source;

    float2 center = 0.5 * (inputBounds.xy + inputBounds.zw);
    float focalLength = 0.5 * (inputBounds.z - inputBounds.x) / halfFieldTangent;
    float3 direction = normalize(float3(scenePosition.xy - center, focalLength));
    float3 light = float3(0.0, 0.0, 0.0);

    float angle = acos(clamp(dot(direction, sun), -1.0, 1.0)) * DegreesPerRadian;
    float ringPosition = (angle - ringStart) / ringSpan;
    if (ringPosition >= 0.0 && ringPosition <= 1.0)
    {
        float2 ringUv = float2((ringPosition * (ringWidth - 1.0) + HalfTexel) / ringWidth, HalfTexel);
        light += RingTexture.SampleLevel(RingSampler, ringUv, 0).rgb;
    }

    float worldDown = direction.y * pitchCosine - direction.z * pitchSine;
    float worldForward = direction.y * pitchSine + direction.z * pitchCosine;
    float elevation = asin(clamp(-worldDown, -1.0, 1.0)) * DegreesPerRadian;
    float turn = atan2(direction.x, worldForward) - sunAzimuth;
    turn -= TwoPi * round(turn / TwoPi);
    float scatterU = abs(turn) * DegreesPerRadian / (scatterTexel * scatterWidth);
    float scatterV = (elevation - sunElevation + scatterHalfSpan) / (scatterTexel * scatterHeight);
    if (scatterU <= 1.0 && scatterV >= 0.0 && scatterV <= 1.0)
        light += ScatterTexture.SampleLevel(ScatterSampler, float2(scatterU, scatterV), 0).rgb;

    float3 linearColor = SrgbToLinear(saturate(source.rgb / source.a));
    linearColor += (1.0 - exp(-light)) * amount;
    return float4(LinearToSrgb(saturate(linearColor)) * source.a, source.a);
}
