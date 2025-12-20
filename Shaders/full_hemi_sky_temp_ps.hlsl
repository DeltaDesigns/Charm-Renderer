// ---- Created with 3Dmigoto v1.3.16 on Tue Dec 09 13:11:37 2025
Texture3D<float4> t1 : register(t1);

Texture3D<float4> t0 : register(t0);

SamplerState s1_s : register(s1);

cbuffer cb0 : register(b0)
{
  float4 cb0[48];
}




// 3Dmigoto declarations
#define cmp -


void main(
  float4 v0 : TEXCOORD0,
  float4 v1 : TEXCOORD1,
  float4 v2 : SV_POSITION0,
  out float4 o0 : SV_TARGET0)
{
  float4 r0,r1,r2;
  uint4 bitmask, uiDest;
  float4 fDest;

  v2.xy = v2.xy /8;	
  r0.xy = float2(-0.5, -0.5) + v2.xy;
  r0.xy = r0.xy * float2(0.015625,0.015625) + float2(-0.5,-0.5);
  r0.xy = r0.xy + r0.xy;
  r0.x = -r0.x * r0.x + 1;
  r0.x = -r0.y * r0.y + r0.x;
  r0.x = max(0, r0.x);
  r0.z = sqrt(r0.x);
  r0.xy = v2.xy * float2(0.03125,0.03125) + float2(-1.015625,-1.015625);
  r0.w = dot(r0.xyz, r0.xyz);
  r0.w = rsqrt(r0.w);
  r0.xyz = r0.xyz * r0.www;
  r0.w = -2 * r0.z;
  r0.xyz = r0.xyz * -r0.www + float3(0,0,-1);
  r0.w = dot(r0.xyz, r0.xyz);
  r0.w = rsqrt(r0.w);
  r0.xyz = r0.xyz * r0.www;
  r0.w = abs(r0.x) + -abs(r0.y);
  r1.x = abs(r0.x) + abs(r0.y);
  r0.w = r0.w / r1.x;
  r1.x = cmp(9.99999975e-006 < r1.x);
  r0.w = r0.w * 0.125 + 0.125;
  r1.yz = cmp(r0.xy >= float2(0,0));
  r1.yz = r1.yz ? float2(1,1) : float2(-1,-1);
  r0.w = r0.w * r1.y + 0.25;
  r0.w = r0.w * r1.z;
  r0.w = frac(r0.w);
  r0.w = 1 + -r0.w;
  r0.w = r1.x ? r0.w : 0.5;
  r0.w = cb0[22].x + r0.w;
  r1.x = -r0.w;
  r1.y = r0.z * -0.5 + 0.5;
  r0.x = dot(r0.xyz, -cb0[29].xyz);
  r0.x = -r0.x * 1.99899995 + cb0[5].x;
  r0.x = cb0[5].x * r0.x + 1;
  r0.x = log2(r0.x);
  r0.x = -1.5 * r0.x;
  r0.x = exp2(r0.x);
  r1.z = cb0[26].x;
  r2.xyzw = t1.Sample(s1_s, r1.xyz).xyzw;
  r1.xyzw = t0.Sample(s1_s, r1.xyz).xyzw;
  r2.xyzw = r2.xyzw + -r1.xyzw;
  r1.xyzw = cb0[47].xxxx * r2.xyzw + r1.xyzw;
  r2.xyzw = cb0[23].xxxx * r1.xyzw;
  r0.yzw = -r1.xyz * cb0[23].xxx + cb0[35].xyz;
  r0.yzw = cb0[35].www * r0.yzw + r2.xyz;
  r0.x = r2.w * r0.x;
  r0.x = cb0[6].x * r0.x;
  r0.x = min(512, r0.x);
  r0.xyz = r0.xxx * cb0[4].xyz + r0.yzw;
  o0.xyz = max(float3(0,0,0), r0.xyz);
  o0.w = 0;
  return;
}
