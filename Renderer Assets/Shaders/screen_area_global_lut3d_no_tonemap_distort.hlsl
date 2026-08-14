// ---- Created with 3Dmigoto v1.3.16 on Fri Aug 14 11:25:53 2026
Texture2D<float4> t6 : register(t6);

Texture3D<float4> t5 : register(t5);

Texture3D<float4> t4 : register(t4);

Texture2D<float4> t1 : register(t1);

Texture2D<float4> t0 : register(t0);

SamplerState s2_s : register(s2);

SamplerState s1_s : register(s1);

cbuffer cb13 : register(b13)
{
  float4 cb13[6];
}

cbuffer cb0 : register(b0)
{
  float4 cb0[27];
}




// 3Dmigoto declarations
#define cmp -


void main(
  float4 v0 : SV_POSITION0,
  float4 v1 : TEXCOORD0,
  out float4 o0 : SV_TARGET0,
  out float4 o1 : SV_TARGET1)
{
  float4 r0,r1,r2,r3;
  uint4 bitmask, uiDest;
  float4 fDest;
  
  // no_tonemap has no distort variant so doing it myself..
  r0.xyzw = t6.Sample(s1_s, v1.xy).xyzw;
  r0.xy = r0.xy + -r0.zw;
  r1.zw = r0.xy * float2(0.13281, 0.23611) + v1.xy; //r0.xy = r0.xy * cb0[28].xy + v1.xy;
  
  r0.xy = float2(-0.5,-0.5) + r1.zw;
  r1.x = cb0[21].x * r0.x;
  r1.y = cb0[22].x * r0.y;
  r0.x = dot(r1.xy, r1.xy);
  r0.y = sqrt(r0.x);
  r0.x = min(cb0[14].x, r0.x);
  r0.zw = -cb0[19].xy + r0.yy;
  r0.zw = saturate(cb0[17].xy * r0.zw);
  r0.zw = r0.zw * cb0[18].xy + cb0[20].xy;
  r0.y = max(9.99999975e-005, r0.y);
  r0.x = r0.x / r0.y;
  r0.x = r0.z * r0.x;
  r0.yz = r0.xx * r1.xy + v1.xy;
  r1.xy = -r0.xx * r1.xy + v1.xy;
  r2.xyz = t0.Sample(s2_s, r1.zw).xyz; //r2.xyz = t0.Sample(s2_s, v1.xy).xyz;
  r0.x = t0.Sample(s2_s, r0.yz).x;
  r0.z = t0.Sample(s2_s, r1.xy).z;
  
  r0.y = r2.y;
  r0.xyz = r0.xyz + -r2.xyz;
  r0.xyz = r0.www * r0.xyz + r2.xyz;
  r1.xyzw = t1.Sample(s1_s, v1.xy).xyzw;
  r0.xyz = cb0[11].xxx * r0.xyz;
  r0.xyz = r0.xyz * r1.www + r1.xyz;
  r0.xyz = max(float3(0,0,0), r0.xyz);
  r0.xyz = min(cb13[5].zzz, r0.xyz);
  r1.xy = cmp(float2(1,2) == cb0[25].xx);
  r1.z = cmp(cb0[26].x < v1.x);
  r1.x = r1.z ? r1.x : 0;
  r1.x = (int)r1.y | (int)r1.x;
  if (r1.x != 0) {
    r1.yzw = cb0[2].xxx + r0.xyz;
    r1.yzw = log2(r1.yzw);
    r1.yzw = -cb0[2].yyy + r1.yzw;
    r1.yzw = saturate(r1.yzw / cb0[2].zzz);
    r1.yzw = float3(1,1,1) + -r1.yzw;
    r1.yzw = log2(r1.yzw);
    r1.yzw = cb0[2].www * r1.yzw;
    r1.yzw = exp2(r1.yzw);
    r1.yzw = float3(1,1,1) + -r1.yzw;
    r1.yzw = max(float3(0,0,0), r1.yzw);
    r2.x = cb0[1].x + -1;
    r2.x = r2.x / cb0[1].x;
    r2.y = 0.5 / cb0[1].x;
    r1.yzw = r1.yzw * r2.xxx + r2.yyy;
    r1.yzw = t5.Sample(s1_s, r1.yzw).xyz;
    r1.yzw = max(float3(0,0,0), r1.yzw);
    r0.w = dot(r1.yzw, float3(0.300000012,0.589999974,0.109999999));
  }
  if (r1.x == 0) {
    r1.xyz = float3(3.20000005,3.20000005,3.20000005) * r0.xyz;
    r2.x = dot(float3(0.597190022,0.354579985,0.0482299998), r1.xyz);
    r2.y = dot(float3(0.0759999976,0.908339977,0.0156599991), r1.xyz);
    r2.z = dot(float3(0.0284000002,0.133829996,0.837769985), r1.xyz);
    r1.xyz = float3(0.0245785993,0.0245785993,0.0245785993) + r2.xyz;
    r1.xyz = r2.xyz * r1.xyz + float3(-9.05370034e-005,-9.05370034e-005,-9.05370034e-005);
    r3.xyz = r2.xyz * float3(0.983729005,0.983729005,0.983729005) + float3(0.432951003,0.432951003,0.432951003);
    r2.xyz = r2.xyz * r3.xyz + float3(0.238080993,0.238080993,0.238080993);
    r1.xyz = r1.xyz / r2.xyz;
    r2.x = saturate(dot(float3(1.60475004,-0.531080008,-0.0736699998), r1.xyz));
    r2.y = saturate(dot(float3(-0.102080002,1.10812998,-0.00604999997), r1.xyz));
    r2.z = saturate(dot(float3(-0.00326999999,-0.0727600008,1.07602), r1.xyz));
    r1.xyz = r2.xyz * float3(0.96875,0.96875,0.96875) + float3(0.015625,0.015625,0.015625);
    r0.w = t4.Sample(s1_s, r1.xyz).w;
  }
  o0.xyzw = r0.xyzw;
  o1.xyzw = r0.wwww;
  return;
}