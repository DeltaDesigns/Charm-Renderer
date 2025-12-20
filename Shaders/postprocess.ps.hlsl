Texture2D t0 : register(t0);
Texture2D t1 : register(t1);
Texture2D t2 : register(t2);

SamplerState sampLinear : register(s0);

cbuffer displayMode : register(b0)
{
    float4 type : packoffset(c0);
}

#define cmp -
float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD) : SV_Target
{
    float4 r0, r1;
    float4 rt0 = t0.Sample(sampLinear, uv);
    float4 rt1 = t1.Sample(sampLinear, uv);
    float4 rt2 = t2.Sample(sampLinear, uv);

    float alpha = 1; //1 - (rt2.w < 0.5 ? 0 : 1);

    // Albedo + AO
    r0.x = saturate(rt2.y + rt2.y);
    r0.x = max(9.99999975e-05, r0.x);
    r0.x = log2(r0.x);
    r0.x = 1 * r0.x;
    r0.x = exp2(r0.x);
    
    if (type.x == 1) // Albedo
        return float4(rt0.xyz, 1.0);
    else if (type.x == 2) // Normal
        return float4(rt1.xyz * alpha, 1.0);
    else if (type.x == 3) // Stack
        return float4(rt2.xyz * alpha, 1.0);
    else if (type.x == 4) // Metal
        return float4(rt2.xxx, 1.0);
    else if (type.x == 5) // AO
        return float4(r0.xxx * alpha, 1.0);
    else if (type.x == 6) // Roughness
    {
        float3 normal = rt1.xyz * float3(2, 2, 2) + float3(-1, -1, -1);
        float length = sqrt(dot(normal.xyz, normal.xyz));
        
		// Roughness
        r0.x = length * 4 + -3;
        r0.y = saturate(-0.5 * r0.x);
        r0.yzw = r0.yyy * float3(1, -1, 0) + float3(0, 1, 0);
        r1.x = r0.x * r0.x;
        r0.x = cmp(r0.x < -0.0105999997);
        return float4(r0.xxx ? r0.yzw : 1 -  r1.xxx, 1);
    }
    else if (type.x == 7) // Emission
    {
        r0.x = rt2.y;
        r0.x = saturate(r0.x * 2 + -1.00784314);
        r0.x = r0.x * 13 + -7;
        r0.x = exp2(r0.x);
        r0.x = -0.0078125 + r0.x;
        return float4(r0.xxx, 1.0);
    }

   return float4(rt0.xyz * r0.xxx, 1.0);
}
