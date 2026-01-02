cbuffer cbViewProj : register(b12)
{
    row_major float4x4 viewProj : packoffset(c0);;
};

struct VSOutput
{
    float4 position : SV_POSITION;
    float4 color    : COLOR;
};

VSOutput VSMain(uint vertexID : SV_VertexID)
{
    VSOutput output;
	
	float gridSize = 10;      // half-extent of grid in world units
    float gridSpacing = 2;   // distance between grid lines
	
    int lineIndex = vertexID / 2;      // which line
    bool isStart = (vertexID % 2) == 0; // start or end of line
    float4 color = float4(0.25, 0.25, 0.25, 1);

    int numLines = (int)(gridSize * 2 / gridSpacing) + 1;

    // Horizontal lines along X, Z varies
    if (lineIndex < numLines)
    {
        float z = -gridSize + lineIndex * gridSpacing;
        float x = isStart ? -gridSize : gridSize;
        output.position = mul(float4(x, z, 0, 1), viewProj);
        output.color = color;
    }
    // Vertical lines along Z, X varies
    else
    {
        int vLineIndex = lineIndex - numLines;
        float x = -gridSize + vLineIndex * gridSpacing;
        float z = isStart ? -gridSize : gridSize;
        output.position = mul(float4(x, z, 0, 1), viewProj);
        output.color = color;
    }
	
    return output;
}

struct PSInput
{
    float4 position : SV_POSITION;
    float4 color    : COLOR;
};

float4 PSMain(PSInput input) : SV_Target
{
    return input.color;
}