// Vertex Shader
struct VSOutput
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

static const float4 vertices[4] = {
    float4(-1.0, 1.0, 0.0, 1.0),  // Top Left
    float4(1.0, 1.0, 0.0, 1.0),   // Top Right
    float4(-1.0, -1.0, 0.0, 1.0), // Bottom Left
    float4(1.0, -1.0, 0.0, 1.0)   // Bottom Right
};

static const float2 uvs[4] = {
    float2(0.0, 0.0), // Top Left
    float2(1.0, 0.0), // Top Right
    float2(0.0, 1.0), // Bottom Left
    float2(1.0, 1.0)  // Bottom Right
};

VSOutput VSMain(uint vertexID: SV_VertexID)
{
    VSOutput output;

    output.pos = vertices[vertexID];
    output.uv = uvs[vertexID];

    return output;
}

Texture2D shading_result : register(t0);
SamplerState samplerState : register(s0);
void PSMain(VSOutput input, out float out_luminance: SV_Target0)
{
    float4 color = shading_result.Sample(samplerState, input.uv);
    float luminance = dot(color.xyz, float3(0.2126, 0.7152, 0.0722));
    float logLum = log(max(luminance, 1e-4));
	
    out_luminance = logLum;
}