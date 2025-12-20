// Matcap shader from Alkahest (credits to Cohae of course)

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

cbuffer scope_alkahest_view : register(b0) {
    float4x4 world_to_camera;
};

cbuffer scope_frame : register(b13)
{
    float4 time		            : packoffset(c0);
    float4 exposure		        : packoffset(c1);
    float4 random_seed_scales	: packoffset(c2);
    float4 overrides		    : packoffset(c3);
} // cbuffer scope_frame

SamplerState s_linear_clamp : register(s1);

Texture2D RtNormal : register(t0);

Texture2D MatcapDiffuse : register(t1);
Texture2D MatcapSpecular : register(t2);

// Decode a packed normal (0.0-1.0 -> -1.0-1.0)
float3 DecodeNormal(float3 n) {
    return n * 2.0 - 1.0;
}

float2 MatcapUV(float3 eye, float3 normal) {
    float2 muv = normal.xy * 0.5 + 0.5;
    return float2(muv.x, 1.0 - muv.y);
}

void PSMain(
    VSOutput input,
    out float4 light_diffuse : SV_Target0,
    out float4 light_specular : SV_Target1
) {
    float4 rt1 = RtNormal.Sample(s_linear_clamp, input.uv);
    float3 normal = DecodeNormal(rt1.xyz);
    float smoothness = length(normal) * 4 - 3;
    float3 viewNormal = mul((float3x3)world_to_camera, normalize(normal));

    float2 uv = MatcapUV((-transpose(camera_to_world)[2].xyz), viewNormal);
    float4 diffuse = MatcapDiffuse.Sample(s_linear_clamp, uv);
    float4 specular = MatcapSpecular.Sample(s_linear_clamp, uv);
    light_diffuse = diffuse.xxxx * exposure.z;
    light_specular = max(1 - smoothness, specular) * exposure.z;
    light_diffuse.w = 1;
    light_specular.w = 1;
}