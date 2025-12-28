// ---- Created with 3Dmigoto v1.3.16 on Thu Dec 11 23:54:56 2025
Buffer<float4> t0 : register(t0);
Buffer<uint4> t1 : register(t1);

cbuffer cb12 : register(b12)
{
  float4 cb12[16];
}

cbuffer cb1 : register(b1)
{
  float4 cb1[24];
}




// 3Dmigoto declarations
#define cmp -


void VSMain(
  float4 v0 : POSITION0,
  float4 v1 : NORMAL0,
  float4 v2 : TANGENT0,
  float2 v3 : TEXCOORD0,
  uint v4 : SV_VERTEXID0,
  out float4 o0 : TEXCOORD0,
  out float4 o1 : TEXCOORD1,
  out float4 o2 : TEXCOORD2,
  out float4 o3 : TEXCOORD3,
  out float3 o4 : TEXCOORD4,
  out float4 o5 : TEXCOORD8,
  out float4 o6 : SV_POSITION0)
{
// Needs manual fix for instruction:
// unknown dcl_: dcl_input_sgv v4.x, vertex_id
  float4 r0,r1,r2,r3,r4,r5,r6,r7,r8,r9,r10,r11;
  uint4 bitmask, uiDest;
  float4 fDest;

  r0.xyz = v0.xyz * cb1[5].www + cb1[5].xyz;
  r0.w = 1;
  r1.xyz = cb1[3].xyz + -cb12[7].xyz;
  
  r4.x = cb1[0].x;
  r4.y = cb1[1].x;
  r4.z = cb1[2].x;
  r4.w = r1.x;
  r2.x = dot(r4.xyzw, r0.xyzw);
  
  r5.x = cb1[0].y;
  r5.y = cb1[1].y;
  r5.z = cb1[2].y;
  r5.w = r1.y;
  r2.y = dot(r5.xyzw, r0.xyzw);
  
  r6.x = cb1[0].z;
  r6.y = cb1[1].z;
  r6.z = cb1[2].z;
  r6.w = r1.z;
  r2.z = dot(r6.xyzw, r0.xyzw);
  
  r1.xyzw = cb12[1].xyzw * r2.yyyy;
  r1.xyzw = cb12[0].xyzw * r2.xxxx + r1.xyzw;
  r1.xyzw = cb12[2].xyzw * r2.zzzz + r1.xyzw;
  o6.xyzw = cb12[14].xyzw + r1.xyzw;
  
  o4.xyz = cb12[7].xyz + r2.xyz;
  r0.x = dot(v1.xyz, v1.xyz);
  r0.x = rsqrt(r0.x);
  r0.xyw = v1.xyz * r0.xxx;
  r1.x = dot(r4.xyz, r0.xyw);
  r1.y = dot(r5.xyz, r0.xyw);
  r1.z = dot(r6.xyz, r0.xyw);
  r0.x = dot(v2.xyz, v2.xyz);
  r0.x = rsqrt(r0.x);
  r0.xyw = v2.xyz * r0.xxx;
  r2.x = dot(r4.xyz, r0.xyw);
  r2.y = dot(r5.xyz, r0.xyw);
  r2.z = dot(r6.xyz, r0.xyw);
  r0.xyw = r2.yzx * r1.zxy;
  r0.xyw = r1.yzx * r2.zxy + -r0.xyw;
  o2.xyz = v2.www * r0.xyw;
  r0.xy = v3.xy * cb1[6].xy + cb1[6].zw;
  r0.w = saturate(cb1[7].z * r1.z);
  o0.w = saturate(cb1[7].w + r0.w);
  r0.w = 32767.0996 * v1.w;
  r0.w = (int)r0.w;
  r1.w = (int)r0.w >> 3;
  r1.w = (int)r1.w & 4095;
  r0.w = (int)r0.w & 7;
  r0.w = (int)r0.w;
  o2.w = 0.100000001 + r0.w;
  r3.xyzw = t1.Load(r1.w).xyzw;
  r3.yw = (uint2)r3.yw << int2(8,8);
  r3.xy = (int2)r3.yw | (int2)r3.xz;
  r3.xy = f16tof32(r3.xy);
  o3.zw = r3.xy * r0.xy;
  o0.xyz = r1.xyz;
  o1.w = r0.z;
  o1.xyz = r2.xyz;
  o3.xy = r0.xy;
  
  //r0.x = min(asuint(cb1[4].w), (uint)v4.x);
  o5.xyzw = t0.Load(v4.x).xyzw;
  return;
}
