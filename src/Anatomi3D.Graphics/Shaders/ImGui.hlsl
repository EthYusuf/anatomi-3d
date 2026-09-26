// Dear ImGui arayüz çizimi
cbuffer ImGuiCB : register(b0)
{
    float4x4 ProjectionMatrix;
};

struct VSIn
{
    float2 pos : POSITION;
    float2 uv : TEXCOORD0;
    float4 col : COLOR0;
};

struct PSIn
{
    float4 pos : SV_POSITION;
    float4 col : COLOR0;
    float2 uv : TEXCOORD0;
};

sampler Sampler0 : register(s0);
Texture2D Texture0 : register(t0);

PSIn VSMain(VSIn i)
{
    PSIn o;
    o.pos = mul(float4(i.pos.xy, 0.0, 1.0), ProjectionMatrix);
    o.col = i.col;
    o.uv = i.uv;
    return o;
}

float4 PSMain(PSIn i) : SV_Target
{
    return i.col * Texture0.Sample(Sampler0, i.uv);
}
