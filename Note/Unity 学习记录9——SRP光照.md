## Unity 学习记录——SRP中的光照

将从最小的lit shader开始，该着色器计算漫反射方向照明，暂时不考虑阴影。

### Lit Shader

将前一个教程的的`Unlit`变成`Lit`，即

```hlsl
//Lit.hlsl
#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED

…

VertexOutput LitPassVertex (VertexInput input) {
	…
}

float4 LitPassFragment (VertexOutput input) : SV_TARGET {
	…
}

#endif // MYRP_LIT_INCLUDED
```

```shader
Shader "My Pipeline/Lit" {
	
	Properties {
		_Color ("Color", Color) = (1, 1, 1, 1)
	}
	
	SubShader {
		
		Pass {
			HLSLPROGRAM
			
			#pragma target 3.5
			
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling
			
			#pragma vertex LitPassVertex
			#pragma fragment LitPassFragment
			
			#include "../ShaderLibrary/Lit.hlsl"
			
			ENDHLSL
		}
	}
}
```

想着我们可以使用新的shader了，只不过现在只是和Unlit的相同

为了计算定向光的贡献，我们需要知道表面法线。所以我们必须在顶点输入和输出结构中添加法线向量。

```hlsl
struct VertexInput {
	float4 pos : POSITION;
	float3 normal : NORMAL;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput {
	float4 clipPos : SV_POSITION;
	float3 normal : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};
```

在`LitPassVertex`中将法线从对象空间转换为世界空间。由于我们假设我们只使用统一尺度，我们可以简单地使用模型矩阵的3×3部分，然后在`LitPassFragment`中对每个片段进行归一化（对非均匀尺度的支持将要求我们使用world->obj矩阵的转置）

```hlsl
VertexOutput LitPassVertex (VertexInput input) {
	…
	output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);
	return output;
}

float4 LitPassFragment (VertexOutput input) : SV_TARGET {
	UNITY_SETUP_INSTANCE_ID(input);
	input.normal = normalize(input.normal);
	…
}
```

漫反射光

漫射光的贡献取决于光线照射到表面的角度，这是通过计算表面法线的点积和光线来自的方向而得到的（计算如果得到负值舍去为0），得到基本的只有以向垂直向上的法线的光照：

```hlsl
float4 LitPassFragment (VertexOutput input) : SV_TARGET {
	UNITY_SETUP_INSTANCE_ID(input);
	input.normal = normalize(input.normal);
	float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;
	
	float3 diffuseLight = saturate(dot(input.normal, float3(0, 1, 0)));
	float3 color = diffuseLight * albedo;
	return float4(color, 1);
}
```

<hr>

###可见光

场景中可以有多个灯光，因此我们也应该支持多个灯光。有多种方法可以做到这一点。 Unity的默认管道在每个对象的单独传递中呈现每个灯光。 Lightweight 管线对每个对象，单个pass中渲染所有灯光。 HD管线使用延迟渲染，渲染所有对象的表面数据，然后每个光源传递一次。

我们将使用与Lightweight管线相同的方法，因此每个对象都会渲染一次，并考虑所有灯光。我们通过发送GPU当前可见的所有光源的数据来做到。那些在场景中但是不会影响渲染的光源将会被忽略

光源buffer

在一个pass中渲染完所有的light，前提条件就是所有的light必须是同时可用的。现在我们只支持定向光，这也就意味着我们需要知道每个灯的颜色和方向。为了支持任意数量的灯光，我们把它们放到一个叫做`_LightBuffer`的缓冲区中，并且需要同时给出这个Array的大小

```hlsl
CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
CBUFFER_END

#define MAX_VISIBLE_LIGHTS 4
CBUFFER_START(_LightBuffer)
	float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightDirections[MAX_VISIBLE_LIGHTS];
CBUFFER_END
```

在下方需要添加一个DiffuseLight函数，使用光数据来处理光照计算。它需要一个光源的索引和法向量作为参数，从数组中提取相关数据，然后执行漫反射光计算并返回它，由光的颜色调制。

```hlsl
CBUFFER_START(_LightBuffer)
	float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightDirections[MAX_VISIBLE_LIGHTS];
CBUFFER_END

float3 DiffuseLight (int index, float3 normal) {
	float3 lightColor = _VisibleLightColors[index].rgb;
	float3 lightDirection = _VisibleLightDirections[index].xyz;
	float diffuse = saturate(dot(normal, lightDirection));
	return diffuse * lightColor;
}
```

在`LitPassFragment`中，使用for循环每个光源调用一次新函数，累积影响片段的总漫反射光。

```hlsl
float4 LitPassFragment (VertexOutput input) : SV_TARGET {
	…
	
	float3 diffuseLight = 0;
	for (int i = 0; i < MAX_VISIBLE_LIGHTS; i++) {
		diffuseLight += DiffuseLight(i, input.normal);
	}
	float3 color = diffuseLight * albedo;
	return float4(color, 1);
}
```

现在，得到了所有物体都是黑色的情况，因为我们的LightBuffer中没有数据。我们现在需要在MyPipeline中增加相同的数组，要保证增加的数组大小是与之前定义的是相同的。同时使用`Shader.PropertyToID`查找到相关着色器的属性标识符，进行更改。

```c#
	const int maxVisibleLights = 4;
	
	static int visibleLightColorsId =
		Shader.PropertyToID("_VisibleLightColors");
	static int visibleLightDirectionsId =
		Shader.PropertyToID("_VisibleLightDirections");
	
	Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
	Vector4[] visibleLightDirections = new Vector4[maxVisibleLights];
```

可以通过在命令缓冲区上调用`SetGlobalVectorArray`方法将数组复制到GPU，然后执行它。因为我们已经有了`cameraBuffer`，所以我们在启动Render Camera示例的同时使用该缓冲区。

但是，我们必须在复制矢量之前配置光源。我们为此建议一个`ConfigureLight`的方法，在剔除过程中，Unity还会确定哪些灯可见。此信息通过`visibleLights`列表提供，该列表是剔除结果的一部分。列表的元素是`VisibleLight`结构，其中包含我们需要的所有数据，循环遍历列表。`VisibleLight.finalColor`字段保存灯光的颜色。它是灯光的颜色乘以其强度，也转换为正确的色彩空间。所以我们可以直接将它复制到`visibleLightColors`的同一个索引处：

```c#
		cameraBuffer.ClearRenderTarget(
			(clearFlags & CameraClearFlags.Depth) != 0,
			(clearFlags & CameraClearFlags.Color) != 0,
			camera.backgroundColor
		);

		ConfigureLights();

		cameraBuffer.BeginSample("Render Camera");

	void ConfigureLights () {
		for (int i = 0; i < cull.visibleLights.Count; i++) {
			VisibleLight light = cull.visibleLights[i];
      visibleLightColors[i] = light.finalColor;
		}
	}
```

但是，默认情况下，Unity认为光的强度是在伽马空间中定义的，即使我们在线性空间中工作也是如此。这是Unity默认渲染管道的延续。我们的新管线认为它是线性值。此行为可以通过布尔GraphicsSettings.lightsUseLinearIntensity属性控制。这是一个项目设置，但只能通过代码进行调整。我们只需要设置一次，在我们在MyPipeline的构造函数方法中这样调整：

```c#
	public MyPipeline (bool dynamicBatching, bool instancing) {
		GraphicsSettings.lightsUseLinearIntensity = true;
		…
	}
```

更改此设置仅在重新应用其图形设置时影响编辑器，这不会自动发生。进入和退出Play模式将应用它。

除此之外，定向光的方向由其旋转确定。光线沿着本地的Z轴。我们可以通过`VisibleLight.localtoWorld`矩阵在世界空间中找到此向量。该矩阵的第三列定义了变换的本机Z方向向量，我们可以通过`Matrix4x4.GetColumn`方法获得该向量，索引2作为参数（第三列）

这为我们提供了光线照射的方向，但在着色器中我们使用从表面到光源的方向。因此，在将向量分配给`visibleLightDirections`之前，我们必须取反该向量。由于方向向量的第四个分量始终为零，我们只需要取反X，Y和Z即可

```c#
			VisibleLight light = cull.visibleLights[i];
			visibleLightColors[i] = light.finalColor;
			Vector4 v = light.localToWorld.GetColumn(2);
			v.x = -v.x;
			v.y = -v.y;
			v.z = -v.z;
			visibleLightDirections[i] = v;
```

现在，我们得到了一个正常的漫反射光照，我们现在可以支持4个光源的光照

拓展光源数量

但是当有超过四个可见光源时，我们的管道失败并带有索引越界异常。我们最多只能支持四个可见光，但Unity在剔除时并没有考虑到这一点。所以visibleLights最终会有比我们的数组更多的元素。当我们超过最大值时，我们必须中止循环。这意味着我们只是忽略了一些可见光源（光源顺序可以按照光照类型、强度等进行排序）

```c#
		for (int i = 0; i < cull.visibleLights.Count; i++) {
			if (i == maxVisibleLights) {
				break;
			}
			VisibleLight light = cull.visibleLights[i];
			…
		}
```

此外，还存在bug：当可见光量减少时，它们仍然可见，因为我们不会重置其数据。我们可以通过在完成可见光之后继续遍历我们的array清除所有未使用的灯光的颜色：

```c#
		int i = 0;
		for (; i < cull.visibleLights.Count; i++) {
			…
		}
		for (; i < maxVisibleLights; i++) {
			visibleLightColors[i] = Color.clear;
		}
```

<hr>

### 点光源

我们目前仅支持定向灯，但通常场景只有一个方向灯和额外很多的点光源。虽然我们可以为场景添加点光源，但它们目前被解释为方向灯。我们现在要解决这个问题。

点光源与定向光不同，它的位置是非常重要的参数，我们将它们的direction和位置信息存在同一个数组上（也就是说，我们下面要先进行重命名）：

```c#
	static int visibleLightColorsId =
		Shader.PropertyToID("_VisibleLightColors");
	static int visibleLightDirectionsOrPositionsId =
		Shader.PropertyToID("_VisibleLightDirectionsOrPositions");

	Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
	Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
```

`ConfigureLights`可以使用`VisibleLight.lightType`来检查每个灯的类型。在定向光源的情况下，存储方向是正确的。否则，存储光源在世界空间下的位置，可以从其local->world矩阵的第四列中提取。

```c#
			if (light.lightType == LightType.Directional) {
				Vector4 v = light.localToWorld.GetColumn(2);
				v.x = -v.x;
				v.y = -v.y;
				v.z = -v.z;
				visibleLightDirectionsOrPositions[i] = v;
			}
			else {
				visibleLightDirectionsOrPositions[i] =
					light.localToWorld.GetColumn(3);
			}
```

在着色器中重命名数组。在`DiffuseLight`中，首先假设我们仍然在处理定向光。

```hlsl
CBUFFER_START(_LightBuffer)
	float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHTS];
CBUFFER_END

float3 DiffuseLight (int index, float3 normal) {
	float3 lightColor = _VisibleLightColors[index].rgb;
	float4 lightPositionOrDirection = _VisibleLightDirectionsOrPositions[index];
	float3 lightDirection = lightPositionOrDirection.xyz;
	float diffuse = saturate(dot(normal, lightDirection));
	return diffuse * lightColor;
}
```

但是如果我们处理点光源，我们必须自己计算光线方向：首先，我们从光源位置减去表面位置，这需要我们在函数中添加一个额外的参数。这为我们提供了世界空间中的光矢量，我们通过归一化将其转化为方向。

```c#
float3 DiffuseLight (int index, float3 normal, float3 worldPos) {
	float3 lightColor = _VisibleLightColors[index].rgb;
	float4 lightPositionOrDirection = _VisibleLightDirectionsOrPositions[index];
	float3 lightVector =
		lightPositionOrDirection.xyz - worldPos;
	float3 lightDirection = normalize(lightVector);
	float diffuse = saturate(dot(normal, lightDirection));
	return diffuse * lightColor;
}
```

这适用于点光源，但对于定向光源来说却毫无意义。它确实为定向光源引入了不需要的标准化，但是为避免这种情况而进行分支是更加不值得的。

```hlsl
		lightPositionOrDirection.xyz - worldPos * lightPositionOrDirection.w;
```

为了完成这项工作，我们需要知道`LitPassFragment`中片段的世界空间位置。我们已经在`LitPassVertex`中使用它，因此将其作为附加输出添加并传递它。

```hlsl
struct VertexOutput {
	float4 clipPos : SV_POSITION;
	float3 normal : TEXCOORD0;
	float3 worldPos : TEXCOORD1;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexOutput LitPassVertex (VertexInput input) {
	…
	output.worldPos = worldPos.xyz;
	return output;
}

float4 LitPassFragment (VertexOutput input) : SV_TARGET {
	…
	
	float3 diffuseLight = 0;
	for (int i = 0; i < MAX_VISIBLE_LIGHTS; i++) {
		diffuseLight += DiffuseLight(i, input.normal, input.worldPos);
	}
	float3 color = diffuseLight * albedo;
	return float4(color, 1);
}
```

距离衰减与光照范围

光的衰减与距离的平方呈反比关系。但是如果距离光源太近，很可能出现除以0的情况，所以我们要对这种情况进行控制，强制执行一个微小的最小值

```hlsl
	float diffuse = saturate(dot(normal, lightDirection));
	
	float distanceSqr = max(dot(lightVector, lightVector), 0.00001);
	diffuse /= distanceSqr;
	
	return diffuse * lightColor;
```

由于光矢量与定向光源的方向矢量相同，因此平方距离最终为1。这意味着定向光源将不受距离衰减的影响，这是符合预期的

我们应该为点光源定义一个有效照亮距离的范围，对于过于遥远的物体，这个点光源是没有什么意义的，但是依然会被认为是可见的，然后被考虑进去渲染。轻量级管线使用的是以下这个距离方程，其中r是光源范围：
$$
\left(1-\left(\frac{d^{2}}{r^{2}}\right)^{2}\right)^{2}
$$
光源范围也同样是场景数据的一部分，我们需要把它对于每个光源从CPU送到GPU，我们将会使用另一个数组来传递这个衰减数据。

```c#
	static int visibleLightColorsId =
		Shader.PropertyToID("_VisibleLightColors");
	static int visibleLightDirectionsOrPositionsId =
		Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
	static int visibleLightAttenuationsId =
		Shader.PropertyToID("_VisibleLightAttenuations");

	Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
	Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
	Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
...
//把这个新的数组在Render()中传递给GPU
		cameraBuffer.SetGlobalVectorArray(
			visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightAttenuationsId, visibleLightAttenuations
		);
...
//在点光源的情况下，把范围放在矢量的X分量中
			Vector4 attenuation = Vector4.zero;

			if (light.lightType == LightType.Directional) {
				…
			}
			else {
				visibleLightDirectionsOrPositions[i] =
					light.localToWorld.GetColumn(3);
				attenuation.x = 1f /
					Mathf.Max(light.range * light.range, 0.00001f);
			}
			
			visibleLightAttenuations[i] = attenuation;
```

然后在GPU中完成计算，将新数组添加到着色器，计算由范围引起的衰减，并将其计入最终的漫反射贡献中

```hlsl
CBUFFER_START(_LightBuffer)
	float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHTS];
CBUFFER_END

float3 DiffuseLight (int index, float3 normal, float3 worldPos) {
	float3 lightColor = _VisibleLightColors[index].rgb;
	float4 lightPositionOrDirection = _VisibleLightDirectionsOrPositions[index];
	float4 lightAttenuation = _VisibleLightAttenuations[index];
	
	float3 lightVector =
		lightPositionOrDirection.xyz - worldPos * lightPositionOrDirection.w;
	float3 lightDirection = normalize(lightVector);
	float diffuse = saturate(dot(normal, lightDirection));
	
	float rangeFade = dot(lightVector, lightVector) * lightAttenuation.x;
	rangeFade = saturate(1.0 - rangeFade * rangeFade);
	rangeFade *= rangeFade;
	
	float distanceSqr = max(dot(lightVector, lightVector), 0.00001);
	diffuse *= rangeFade / distanceSqr;
	
	return diffuse * lightColor;
}
```

定向光源还是不受影响的，因为它们的`lightAttenuation.x`始终为0，因此`rangeFade`始终为1。

<hr>

### 聚光灯

聚光灯像点光源一样工作，但仅限于锥形而不是向各个方向照射。

跟之前一样进行调整：

```c#
	static int visibleLightAttenuationsId =
		Shader.PropertyToID("_VisibleLightAttenuations");
	static int visibleLightSpotDirectionsId =
		Shader.PropertyToID("_VisibleLightSpotDirections");

	Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
	Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
	Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
	Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];
	
	…
	
	void Render (ScriptableRenderContext context, Camera camera) {
		…
			cameraBuffer.SetGlobalVectorArray(
			visibleLightAttenuationsId, visibleLightAttenuations
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightSpotDirectionsId, visibleLightSpotDirections
		);
		…
	}
```

在`ConfigureLights`中，当不处理定向灯时，还要检查灯是否是聚光灯。如果是，设置方向向量，就像定向灯一样，但是将其分配给`visibleLightSpotDirections`

```c#
			if (light.lightType == LightType.Directional) {
				Vector4 v = light.localToWorld.GetColumn(2);
				v.x = -v.x;
				v.y = -v.y;
				v.z = -v.z;
				visibleLightDirectionsOrPositions[i] = v;
			}
			else {
				visibleLightDirectionsOrPositions[i] =
					light.localToWorld.GetColumn(3);
				attenuation.x = 1f /
					Mathf.Max(light.range * light.range, 0.00001f);

				if (light.lightType == LightType.Spot) {
					Vector4 v = light.localToWorld.GetColumn(2);
					v.x = -v.x;
					v.y = -v.y;
					v.z = -v.z;
					visibleLightSpotDirections[i] = v;
				}
			}
```

在GPU中加入这个部分

```hlsl
CBUFFER_START(_LightBuffer)
	float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHTS];
CBUFFER_END

float3 DiffuseLight (int index, float3 normal, float3 worldPos) {
	float3 lightColor = _VisibleLightColors[index].rgb;
	float4 lightPositionOrDirection = _VisibleLightDirectionsOrPositions[index];
	float4 lightAttenuation = _VisibleLightAttenuations[index];
	float3 spotDirection = _VisibleLightSpotDirections[index].xyz;
	
	…
}
```

我们接下来要控制角度衰减

聚光灯的圆锥指定的正角度小于180°。我们可以通过获取光点方向和光方向的点积来确定表面点是否位于锥体内。如果结果最多是配置的光斑角度的一半的余弦，则片段受到光的影响。

锥体边缘没有瞬间切断。相反，存在光衰退的过渡范围。该范围可以由褪色开始的内部光斑角度和光强度达到零的外部光斑角度来定义。然而，Unity的聚光灯只允许我们设置外角。 Unity的默认管道使用光cookie来确定衰减，而轻量级管道使用平滑函数计算衰减，该函数假设内角和外角之间存在固定关系。

要确定衰减，首先将光点角度的一半从度数转换为弧度，然后计算其余弦值。配置的角度可通过`VisibleLight.spotAngle`获得。

```c#
				if (light.lightType == LightType.Spot) {
					…
					
					float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
					float outerCos = Mathf.Cos(outerRad);
				}
```

在轻量级管线计算中，外角和内角的关系是$$
\tan \left(r_{i}\right)=\frac{46}{64} \tan \left(r_{o}\right)
$$，ri是半内角，r0是半外角。我们可以由此计算出外角的cos值：$$
\cos \left(r_{i}\right)=\cos \left(\arctan \left(\frac{46}{64} \tan \left(r_{o}\right)\right)\right)
$$，翻译成代码：

```c#
					float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
					float outerCos = Mathf.Cos(outerRad);
					float outerTan = Mathf.Tan(outerRad);
					float innerCos =
						Mathf.Cos(Mathf.Atan(((46f / 64f) * outerTan));
```

这里的渐变遵循以下公式：$$
\frac{D_{s} \cdot D_{l}-\cos \left(r_{o}\right)}{\cos \left(r_{i}\right)-\cos \left(r_{o}\right)}
$$，然后进行平方，Ds·Dl是spot方向和光线方向的点积

上式可以化简成$\left(D*{s} \cdot D*{l}\right) a+b, \text{其中 }a=\frac{1}{\cos \left(r*{i}\right)-\cos \left(r*{o}\right)}, b=-\cos \left(r_{o}\right) a$，因此，我们可以在`ConfigureLight`中进行计算，然后存储

```c#
					float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
					float outerCos = Mathf.Cos(outerRad);
					float outerTan = Mathf.Tan(outerRad);
					float innerCos = Mathf.Cos(Mathf.Atan(((64f - 18f) / 64f) * outerTan));
					float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
					attenuation.z = 1f / angleRange;
					attenuation.w = -outerCos * attenuation.z;
```

在着色器中来计算点衰落因子。然后使用结果调制漫反射光：

```hlsl
	float rangeFade = dot(lightVector, lightVector) * lightAttenuation.x;
	rangeFade = saturate(1.0 - rangeFade * rangeFade);
	rangeFade *= rangeFade;
	
	float spotFade = dot(spotDirection, lightDirection);
	spotFade = saturate(spotFade * lightAttenuation.z + lightAttenuation.w);
	spotFade *= spotFade;
	
	float distanceSqr = max(dot(lightVector, lightVector), 0.00001);
	diffuse *= spotFade * rangeFade / distanceSqr;
```

要防止光斑衰减计算影响其他光类型，将其衰减矢量的W分量设置为1。

```c#
			Vector4 attenuation = Vector4.zero;
			attenuation.w = 1f;
```

<hr>

### 逐物体的光照

我们目前每个对象最多支持四个灯。事实上，我们总是为每个物体计算四盏灯的照明，即使这不是必要的。

目前，这些81个球体用一次绘制调用渲染（假设启用了GPU实例）但是每个球体片段计算了四次光贡献（即总调用=灯光数目*物体数目）。如果我们能够以某种方式仅计算每个物体所需的灯光（有些物体可能主要只受到一盏灯的影响），那会更好。这样我们也可以增加支持的可见光数量。

光索引

在剔除过程中，Unity会确定可见的灯光，这也包括确定哪些灯光会影响每个物体。我们可以要求Unity以光索引列表的形式将此信息发送到GPU。

Unity目前支持两种光索引格式。**第一种方法是将8个索引存储到2个float4中去，这两个float4是按对象设置的。第二种方法是在单个缓冲区中放置所有对象的光索引列表，类似于GPU实例化数据的存储方式。**但是，Unity 2018.3中禁用了第二种方法，仅支持第一种方法。

我们通过将绘图设置的`rendererConfiguration`字段设置为`RendererConfiguration.PerObjectLightIndices8`，指示Unity通过float4字段设置灯光索引：

```c#
		var drawSettings = new DrawRendererSettings(
			camera, new ShaderPassName("SRPDefaultUnlit")
		) {
			flags = drawFlags,
			rendererConfiguration = RendererConfiguration.PerObjectLightIndices8
		};
		//drawSettings.flags = drawFlags;
		drawSettings.sorting.flags = SortFlags.CommonOpaque;
```

Unity现在必须为每个obj设置额外的GPU数据，这会影响GPU实例化。 Unity尝试对受相同灯光影响的对象进行分组，但更偏爱根据距离进行分组。此外，光指数基于每个对象的相对光重要性来排序，这可以进一步分割批次。在网格示例的情况下，我最终得到了30次绘制调用，这远远超过1，但仍然远低于81。

索引通过`unity_4LightIndices0`和`unity_4LightIndices1`向量提供，它们应该是`UnityPerDraw`缓冲区的一部分。除此之外，还有另一个float4向量`unity_LightIndicesOffsetAndCount`，其Y分量包含影响对象的灯光数量。它的X组件包含使用第二种方法时的偏移量（即单个缓冲区中放置所有对象的光索引列表这个方法），因此我们现在可以忽略它。

```hlsl
CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
	float4 unity_LightIndicesOffsetAndCount;
	float4 unity_4LightIndices0, unity_4LightIndices1;
CBUFFER_END
```

现在我们可以限制我们的只有在需要的时候才调用DiffuseLight，但是我们必须要检索正确的光索引，我们现在只是支持四个可见光，我们需要的是`unity_4LightIndices0`，我们可以将其作为数组索引加以检索。

```hlsl
	for (int i = 0; i < unity_LightIndicesOffsetAndCount.y; i++) {
		int lightIndex = unity_4LightIndices0[i];
		diffuseLight +=
			DiffuseLight(lightIndex, input.normal, input.worldPos);

	}
```

虽然应该没有明显的变化 - 假设最多只有四个可见光 - GPU现在做的工作较少，因为它只计算相关灯光的贡献。可以使用帧调试器来检查每次绘制调用最终使用的灯数。着色器确实变得更加复杂，因为我们现在使用的是可变循环而不是固定循环。是否会导致更好或更差的性能可能会有所不同。我们支持的灯光越明显，这种新方法就越好。

由于我们不再循环通过最大可见光，我们不再需要清除最终未使用的光数据：

```hlsl
	void ConfigureLights () {
		//int i = 0;
		for (int i = 0; i < cull.visibleLights.Count; i++) {
			…
		}
		//for (; i < maxVisibleLights; i++) {
		//	visibleLightColors[i] = Color.clear;
		//}
	}
```

有了4个光源，我们希望可以支持更多的光源。这要求我们每帧向GPU发送更多数据，但大多数对象只会受到几盏灯的影响。在着色器中调整`MAX_VISIBLE_LIGHTS`

```hlsl
#define MAX_VISIBLE_LIGHTS 16
```

在MyPipeline中修改最大可见光源数目：

```c#
	const int maxVisibleLights = 16;
```

重新编译后，Unity会警告我们，我们超过了之前的数组大小。但是不幸的是，不可能只改变着色器中固定数组的大小。这是图形API限制，而不是我们可以做任何事情。在使用新大小之前必须重新启动应用程序，因此必须重新启动Unity编辑器。

在我们开始为场景添加更多灯光之前，我们必须意识到`unity_4LightIndices0`最多只包含四个索引，即使一个对象现在可以受到四个以上灯光的影响。为了防止不正确的结果，我们必须确保我们的光环不超过四个。

```hlsl
	for (int i = 0; i < min(unity_LightIndicesOffsetAndCount.y, 4); i++) {
		int lightIndex = unity_4LightIndices0[i];
		diffuseLight +=
			DiffuseLight(lightIndex, input.normal, input.worldPos);
	}
```

但是我们不必限制每个物体最多四个灯。还有`unity_4LightIndices1`，它可以包含另外四个光指数。让我们简单地在第一个循环之后添加第二个循环，从索引4开始并从`unity_4LightIndices1`中检索光索引。这会将每个对象的最大亮度增加到8。我们应该确保不超过8，因为对象可能会受到场景中更多灯光的影响。

```hlsl
	for (int i = 0; i < min(unity_LightIndicesOffsetAndCount.y, 4); i++) {
		int lightIndex = unity_4LightIndices0[i];
		diffuseLight +=
			DiffuseLight(lightIndex, input.normal, input.worldPos);
	}
	for (int i = 4; i < min(unity_LightIndicesOffsetAndCount.y, 8); i++) {
		int lightIndex = unity_4LightIndices1[i - 4];
		diffuseLight +=
			DiffuseLight(lightIndex, input.normal, input.worldPos);
	}
```

由于光索引是根据相对重要性排序的，通常第二个四重光不像第一个那样明显。此外，大多数物体不会受到那么多灯光的影响。要查看额外四个灯所产生的差异，需要暂时禁用第一个循环。

顶点光源：

由于第二个四重灯的视觉重要性远远低于第一个，因此我们可以通过计算每个顶点的贡献而不是每个光源来降低它们的成本。光贡献将在顶点之间线性插值，这不太准确，但是对于细微的漫射照明是可接受的，只要光距离与三角形边缘长度相比相当大。

可以微调我们支持的像素和顶点光的数量，但我们只需将第二个光环移动到`LitPassVertex`，这只需要调整使用的变量。这意味着我们最多支持四个像素灯和四个顶点灯。必须将顶点光照添加到`VertexOutput`，并将其用作`LitPassFragment中diffuseLight`的初始值。

```hlsl
struct VertexOutput {
	float4 clipPos : SV_POSITION;
	float3 normal : TEXCOORD0;
	float3 worldPos : TEXCOORD1;
	float3 vertexLighting : TEXCOORD2;//顶点光贴图
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexOutput LitPassVertex (VertexInput input) {
	VertexOutput output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
	output.clipPos = mul(unity_MatrixVP, worldPos);
	output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);
	output.worldPos = worldPos.xyz;
	
	//顶点光部分
	output.vertexLighting = 0;
	for (int i = 4; i < min(unity_LightIndicesOffsetAndCount.y, 8); i++) {
		int lightIndex = unity_4LightIndices1[i - 4];
		output.vertexLighting +=
			DiffuseLight(lightIndex, output.normal, output.worldPos);
	}

	return output;
}

float4 LitPassFragment (VertexOutput input) : SV_TARGET {
	UNITY_SETUP_INSTANCE_ID(input);
	input.normal = normalize(input.normal);
	float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;
	
	float3 diffuseLight = input.vertexLighting;
	for (int i = 0; i < min(unity_LightIndicesOffsetAndCount.y, 4); i++) {
		…
	}
	//for (int i = 4; i < min(unity_LightIndicesOffsetAndCount.y, 8); i++) {
	//	…
	//}
	float3 color = diffuseLight * albedo;
	return float4(color, 1);
}
```

虽然我们现在支持多达16个可见光，但在场景中有足够的光线，我们仍然可以最终超过该限制。发生这种情况时，渲染时会忽略整体最不重要的灯光。但是，这只是因为我们不将其数据复制到着色器。 Unity不知道这一点，也没有消除每个对象的光索引列表中的那些光。所以我们最终会得到超出范围的光指数。为了防止这种情况，我们必须告诉Unity一些灯被消除了。

我们可以通过在剔除结果上调用GetLightIndexMap来获取所有可见光的索引列表。 Unity允许我们修改此映射，然后通过SetLightIndexMap将其分配回剔除结果。重点是Unity将跳过索引已更改为-1的所有灯光。在ConfigureLights的末尾执行此操作，以获取超出最大值的所有灯光。

```c#
	void ConfigureLights () {
		for (int i = 0; i < cull.visibleLights.Count; i++) {
			…
		}

		int[] lightIndices = cull.GetLightIndexMap();
		for (int i = maxVisibleLights; i < cull.visibleLights.Count; i++) {
			lightIndices[i] = -1;
		}
		cull.SetLightIndexMap(lightIndices);
	}
```

只有当我们最终得到太多可见光时，我们才真正需要这样做，这不应该一直发生。

```c#
		if (cull.visibleLights.Count > maxVisibleLights) {
			int[] lightIndices = cull.GetLightIndexMap();
			for (int i = maxVisibleLights; i < cull.visibleLights.Count; i++) {
				lightIndices[i] = -1;
			}
			cull.SetLightIndexMap(lightIndices);
		}
```

不幸的是，`GetLightIndexMap`在每次调用时都会创建一个新数组，所以我们的管道现在每隔一帧就会分配内存太多可见光。我们目前无法做任何事情，但未来的Unity版本将允许我们访问`GetLightIndexMap`的免分配替代品。

零可见光

另一种可能性是可见光为零。这应该可行，但不幸的是，在这种情况下尝试设置光指数时Unity会崩溃。当我们至少有一个可见光时，我们可以通过仅使用每个对象光索引来避免崩溃。

```c#
		var drawSettings = new DrawRendererSettings(
			camera, new ShaderPassName("SRPDefaultUnlit")
		) {
			flags = drawFlags//,
			//rendererConfiguration = RendererConfiguration.PerObjectLightIndices8
		};
		if (cull.visibleLights.Count > 0) {
			drawSettings.rendererConfiguration =
				RendererConfiguration.PerObjectLightIndices8;
		}
```

如果没有灯光，我们也可以完全跳过调用ConfigureLights。

```c#
		if (cull.visibleLights.Count > 0) {
			ConfigureLights();
		}
```

没有Unity设置光数据的副作用是它们保持为最后一个对象设置的值。因此，我们最终可以为所有对象提供非零光照计数。为避免这种情况，我们将手动将unity_LightIndicesOffsetAndCount设置为零

```c#
	static int lightIndicesOffsetAndCountID =
		Shader.PropertyToID("unity_LightIndicesOffsetAndCount");
	
	…
	
	void Render (ScriptableRenderContext context, Camera camera) {
		…
		
		if (cull.visibleLights.Count > 0) {
			ConfigureLights();
		}
		else {
			cameraBuffer.SetGlobalVector(
				lightIndicesOffsetAndCountID, Vector4.zero
			);
		}

		…
	}
```

