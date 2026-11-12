struct VS_OUT {
    float4 pos : SV_Position;
    float2 uv : TEXCOORD0;
};

VS_OUT vs_main(uint id : SV_VertexID) {
    VS_OUT output;
    output.uv = float2((id << 1) & 2, id & 2);
    output.pos = float4(output.uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return output;
}

Texture2D srcTex : register(t0);
SamplerState srcSampler : register(s0);

float4 ps_linear(VS_OUT input) : SV_Target {
    return srcTex.Sample(srcSampler, input.uv);
}

float4 ps_bicubic(VS_OUT input) : SV_Target {
    float2 texSize;
    srcTex.GetDimensions(texSize.x, texSize.y);
    float2 invTexSize = 1.0 / texSize;
    float2 coord = input.uv * texSize - 0.5;
    float2 f = frac(coord);
    float2 index = coord - f;

    float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    float2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    float2 w3 = f * f * (-0.5 + 0.5 * f);

    float2 w12 = w1 + w2;
    float2 tc12 = (index + 1.0 + w2 / w12) * invTexSize;
    float2 tc0 = (index - 0.5) * invTexSize;
    float2 tc3 = (index + 2.5) * invTexSize;

    float4 c = srcTex.Sample(srcSampler, float2(tc12.x, tc12.y)) * (w12.x * w12.y)
             + srcTex.Sample(srcSampler, float2(tc0.x, tc12.y)) * (w0.x * w12.y)
             + srcTex.Sample(srcSampler, float2(tc3.x, tc12.y)) * (w3.x * w12.y)
             + srcTex.Sample(srcSampler, float2(tc12.x, tc0.y)) * (w12.x * w0.y)
             + srcTex.Sample(srcSampler, float2(tc12.x, tc3.y)) * (w12.x * w3.y);

    return c;
}
