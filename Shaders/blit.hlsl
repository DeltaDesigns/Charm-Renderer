struct VSOutput {
    float4 position   : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float2 screen_pos : TEXCOORD1;
};

cbuffer cb12 : register(b12) {
    row_major float4x4 world_to_projective  : packoffset(c0);
    row_major float4x4 camera_to_world      : packoffset(c4);
    float4 target		                    : packoffset(c8);
    float4 view_miscellaneous		        : packoffset(c9);
    float4 view_unk20                       : packoffset(c10);
    row_major float4x4 camera_to_projective : packoffset(c11);
    float4 unk15                            : packoffset(c15);
}

VSOutput VSMain(uint vertex_i : SV_VertexID)
{
    VSOutput output;

    output.uv = float2(0, 0);
    output.uv.x = vertex_i == 1 ? 2 : 0;
    output.uv.y = vertex_i == 2 ? 2 : 0;

    output.position = float4(output.uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    output.screen_pos = (output.position.xy * float2(0.5, -0.5) + 0.5) * target;

    return output;
}

Texture2D Source : register(t0);
SamplerState Sampler : register(s0);

float4 PSMain(VSOutput input) : SV_Target0
{
    return Source.Sample(Sampler, input.uv);
}
