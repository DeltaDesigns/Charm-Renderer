cbuffer cb12 : register(b12) {
    row_major float4x4 world_to_projective  : packoffset(c0);
    row_major float4x4 camera_to_world      : packoffset(c4);
    float4 target		                    : packoffset(c8);
    float4 view_miscellaneous		        : packoffset(c9);
    float4 view_unk20                       : packoffset(c10);
    row_major float4x4 camera_to_projective : packoffset(c11);
    float4 unk15                            : packoffset(c15);
}

cbuffer cb1 : register(b1)
{
  row_major float4x4 mesh_to_world; // c0
}

struct VS_IN
{
    float4 pos : POSITION;
};

struct VS_OUT
{
    float4 pos : SV_POSITION;
};

VS_OUT VSMain(VS_IN input)
{
	float4 r0,r1,r2,r3;
	VS_OUT o;
	
	r0.x = mesh_to_world[0].x;
	r0.y = mesh_to_world[1].x;
	r0.z = mesh_to_world[2].x;
	r1.xyw = mesh_to_world[3].xyz + -camera_to_world[3].xyz;
	r0.w = r1.x;
	r2.xyz = input.pos.xyz; //* position_scale.xyz + position_offset.xyz;
	r2.w = 1;
	r0.x = dot(r0.xyzw, r2.xyzw);
	r3.w = r1.y;
	r3.x = mesh_to_world[0].y;
	r3.y = mesh_to_world[1].y;
	r3.z = mesh_to_world[2].y;
	r0.y = dot(r3.xyzw, r2.xyzw);
	r1.x = mesh_to_world[0].z;
	r1.y = mesh_to_world[1].z;
	r1.z = mesh_to_world[2].z;
	r0.z = dot(r1.xyzw, r2.xyzw);

	r1.xyzw = world_to_projective[1].xyzw * r0.yyyy;
	r1.xyzw = world_to_projective[0].xyzw * r0.xxxx + r1.xyzw;
	r0.xyzw = world_to_projective[2].xyzw * r0.zzzz + r1.xyzw;
	o.pos = camera_to_projective[3].xyzw + r0.xyzw;
	return o;
}