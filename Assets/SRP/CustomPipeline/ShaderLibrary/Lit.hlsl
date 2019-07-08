#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED

//使用constant buffer
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

//使用Unity提供的矩阵，使用constant buffer进行优化
CBUFFER_START(UnityPerFrame)
    float4x4 unity_ObjectToWorld;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
    float4x4 unity_MatrixVP;
    //增加光照数目
    float4 unity_LightIndicesOffsetAndCount;
    float4 unity_4LightIndices0, unity_4LightIndices1;
CBUFFER_END

#define MAX_VISIBLE_LIGHTS 16

CBUFFER_START(_LightBuffer)
    float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHTS];
    float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHTS];
CBUFFER_END

float3 DiffuseLight(int index, float3 normal, float3 worldPos)
{
    float3 lightColor = _VisibleLightColors[index].rgb;
    float4 lightPositionOrDirection = _VisibleLightDirectionsOrPositions[index];
    float4 lightAttenuation = _VisibleLightAttenuations[index];//光照衰减
    float3 spotDirection = _VisibleLightSpotDirections[index].xyz;
    
    float3 lightVector = lightPositionOrDirection.xyz - worldPos * lightPositionOrDirection.w;//表面指向光源方向向量
    
    float3 lightDirection = normalize(lightVector);
    float diffuse = saturate(dot(normal, lightDirection));
    
    //衰减公式：(1-(d^2/r^2)^2)^2
    float rangeFade = dot(lightVector, lightVector) * lightAttenuation.x;//衰减范围计算
    rangeFade = saturate(1.0 - rangeFade * rangeFade);
    rangeFade *= rangeFade;
    
    //聚光灯计算
    float spotFade = dot(spotDirection, lightDirection);
    spotFade = saturate(spotFade * lightAttenuation.z + lightAttenuation.w);
    spotFade *= spotFade;
    
	float distanceSqr = max(dot(lightVector, lightVector), 0.00001);//光线衰减
	diffuse *= spotFade * rangeFade / distanceSqr;
    
    return diffuse * lightColor;
}

#define UNITY_MATRIX_M unity_ObjectToWorld

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

UNITY_INSTANCING_BUFFER_START(PerInstance)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)

struct VertexInput {
	float4 pos : POSITION;
	float3 normal : NORMAL;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput {
	float4 clipPos : SV_POSITION;
	float3 normal : TEXCOORD0;
	float3 worldPos : TEXCOORD1;
	float3 VertexLighting : TEXCOORD2;//顶点光源
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexOutput LitPassVertex (VertexInput input) {
	VertexOutput output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
	output.clipPos = mul(unity_MatrixVP, worldPos);
	output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);//假设统一缩放
	output.worldPos = worldPos.xyz;
	
	//顶点光源，将不那么重要的光源放到计算每个顶点的贡献而不是每个光来降低它们的成本
	output.VertexLighting = 0;
	for(int i = 4; i < min(unity_LightIndicesOffsetAndCount.y, 8); i++)
	{
	    int lightIndex = unity_4LightIndices1[i - 4];
	    output.VertexLighting += DiffuseLight(lightIndex, output.normal, output.worldPos);
	}
	
	return output;
}

//float4 itPassFragment (VertexOutput input) : SV_TARGET {
// 	UNITY_SETUP_INSTANCE_ID(input);
// 	input.normal = normalize(input.normal);
// 	return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color);
// }

float4 LitPassFragment (VertexOutput input) : SV_TARGET {
	UNITY_SETUP_INSTANCE_ID(input);
	input.normal = normalize(input.normal);
	float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;
	
	
	float3 diffuseLight = 0;
	for (int i = 0; i < min(unity_LightIndicesOffsetAndCount.y, 4); i++)
	{
	    int lightIndex = unity_4LightIndices0[i];
	    diffuseLight += DiffuseLight(lightIndex, input.normal, input.worldPos);
	}
	
//	for (int i = 4; i < min(unity_LightIndicesOffsetAndCount.y, 8); i++)
//	{
//	    int lightIndex = unity_4LightIndices1[i - 4];
//	    diffuseLight += DiffuseLight(lightIndex, input.normal, input.worldPos);
//	}
	
	//float3 diffuseLight = saturate(dot(input.normal, float3(0, 1, 0)));
	float3 color = diffuseLight * albedo;
	return float4(color, 1);
}

#endif