cbuffer cb1 : register(b1)
{
  row_major float4x4 mesh_to_world; 
  float4 base_color;                
  uint4 flags;                      
}

cbuffer cb12 : register(b12) {
    row_major float4x4 world_to_projective  : packoffset(c0);
    row_major float4x4 camera_to_world      : packoffset(c4);
    float4 target		                    : packoffset(c8);
    float4 view_miscellaneous		        : packoffset(c9);
    float4 view_unk20                       : packoffset(c10);
    row_major float4x4 camera_to_projective : packoffset(c11);
    float4 unk15                            : packoffset(c15);
}

struct SkinVertex
{
  uint4 indices;
  float4 weights;
};

StructuredBuffer<SkinVertex> skin_vertices : register(t20);
StructuredBuffer<float4> skin_bone_rows : register(t21);

struct VSInput
{
  float4 position : POSITION0;
  float4 normal : NORMAL0;
  uint vertex_id : SV_VertexID;
};

struct VSOutput
{
  float4 position : SV_POSITION;
  float3 world_position : TEXCOORD0;
  float3 world_normal : TEXCOORD1;
};

void ApplySkinning(uint vertex, inout float3 pos, inout float3 nrm)
{
  SkinVertex sv = skin_vertices[vertex];
  float weight_sum = sv.weights.x + sv.weights.y + sv.weights.z + sv.weights.w;
  if (weight_sum <= 0.0001)
    return;

  float4 p4 = float4(pos, 1);
  float3 p = 0, n = 0;
  [unroll]
  for (int i = 0; i < 4; i++)
  {
    float w = sv.weights[i] / weight_sum;
    if (w <= 0)
      continue;

    uint b = sv.indices[i] * 3;
    float4 row0 = skin_bone_rows[b];
    float4 row1 = skin_bone_rows[b + 1];
    float4 row2 = skin_bone_rows[b + 2];

    p += w * float3(dot(row0, p4), dot(row1, p4), dot(row2, p4));
    n += w * float3(dot(row0.xyz, nrm), dot(row1.xyz, nrm), dot(row2.xyz, nrm));
  }

  pos = p;
  nrm = n;
}

VSOutput VSMain(VSInput input)
{
  VSOutput o;

  float3 pos = input.position.xyz;
  float3 nrm = input.normal.xyz;
  if (flags.x != 0)
    ApplySkinning(input.vertex_id, pos, nrm);

  float4 world = mul(float4(pos, 1), mesh_to_world);
  o.world_position = world.xyz;
  o.world_normal = mul(nrm, (float3x3)mesh_to_world);
  o.position = mul(world, world_to_projective);
  return o;
}

float4 PSMain(VSOutput input) : SV_Target0
{
  float3 n = cross(ddy(input.world_position), ddx(input.world_position));
  if (flags.y != 0 && dot(input.world_normal, input.world_normal) > 1e-8)
    n = input.world_normal;
  n = normalize(n);

  float3 v = normalize(camera_to_world[3].xyz - input.world_position);
  float facing = abs(dot(n, v));

  return float4(base_color.rgb * (0.3 + 0.7 * facing), 1);
}
