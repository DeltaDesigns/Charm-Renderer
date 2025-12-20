// ---- Created with 3Dmigoto v1.3.16 on Tue Dec 09 13:30:55 2025
cbuffer cb12 : register(b12)
{
  float4 cb12[10];
}

cbuffer cb0 : register(b0)
{
  float4 cb0[4];
}




// 3Dmigoto declarations
#define cmp -


void main(
  uint v0 : SV_VERTEXID0,
  out float4 o0 : TEXCOORD0,
  out float4 o1 : TEXCOORD1,
  out float4 o2 : SV_POSITION0)
{
// Needs manual fix for instruction:
// unknown dcl_: dcl_input_sgv v0.x, vertex_id
  float4 r0,r1;
  uint4 bitmask, uiDest;
  float4 fDest;

  r0.x = (uint)v0.x;
  r0.xy = r0.xx * float2(0.25,0.5) + float2(0.125,0.25);
  r0.xy = frac(r0.xy);
  r0.xy = cmp(r0.xy >= float2(0.5,0.5));
  r0.xy = r0.xy ? float2(1,1) : 0;
  r0.xy = r0.xy * float2(2,2) + float2(-1,-1);
  r1.xyzw = cb0[1].xyzw * r0.yyyy;
  r1.xyzw = cb0[0].xyzw * r0.xxxx + r1.xyzw;
  o2.xy = r0.xy;
  r0.xyzw = cb0[2].xyzw * cb12[9].xxxx + r1.xyzw;
  r0.xyzw = cb0[3].xyzw + r0.xyzw;
  o0.xyzw = r0.xyzw;
  r1.xyzw = cb12[5].xyzw * r0.yyyy;
  r1.xyzw = cb12[4].xyzw * r0.xxxx + r1.xyzw;
  r1.xyzw = cb12[6].xyzw * r0.zzzz + r1.xyzw;
  o1.xyzw = cb12[7].xyzw * r0.wwww + r1.xyzw;
  o2.z = cb12[9].x;
  o2.w = 1;
  return;
}