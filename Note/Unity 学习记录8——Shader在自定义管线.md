### Unity学习记录8——Shader与自定义管线

基本思路很简单：

```hlsl
#ifndef MYRP_UNLIT_INCLUDED
#define MYRP_UNLIT_INCLUDED

//Unity提供的矩阵
float4x4 unity_ObjectToWorld;
float4x4 unity_MatrixVP;

struct VertexInput
{
    float4 pos : POSITION;
};

struct VertexOutput
{
    float4 clipPos : SV_POSITION;
};

VertexOutput UnlitPassVertex (VertexInput input)
{
    VertexOutput output;
    float4 worldPos = mul(unity_ObjectToWorld, float4(input.pos.xyz, 1.0));//显式float4，优化计算
    output.clipPos = mul(unity_MatrixVP, worldPos);//世界->投影
    return output;
}

float4 UnlitPassFragment(VertexOutput input) : SV_TARGET
{
    return 1;
}

#endif
```

```shader
Shader "My Pipeline/Unlit"
{
    Properties { }
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment
            
            #include "../ShaderLibrary/Unlit.hlsl"
            
            ENDHLSL
        }
    }
}
```

<hr>

只读缓冲区（Constant Buffer）

Unity提供的VP矩阵可以在同一个相机的一帧中进行复用。正是因为这个，Unity把这些矩阵放到了只读缓冲区中。尽管我们定义它们作为变量，但是它们实际山就是在渲染单个物体时保持不变的。VP矩阵放入每帧的buffer，M矩阵放入每次draw的buffer中

为了尽可能高效，我们还将使用只读缓冲区。 Unity将VP矩阵放在UnityPerFrame缓冲区中，将M矩阵放在UnityPerDraw缓冲区中。当然还有更多的数据放在这些缓冲区中，但暂时不考虑。除了使用cbuffer关键字之外，常量缓冲区定义为结构，并且仍然像以前一样访问。

```hlsl
cbuffer UnityPerFrame {
	float4x4 unity_MatrixVP;
};

cbuffer UnityPerDraw {
	float4x4 unity_ObjectToWorld;
}
```

<hr>

Core Library

由于只读缓冲区不会使得所有平台受益，所以Unity是利用宏来处理跨平台问题的。

我们使用带有name参数的`CBUFFER_START`宏而不是直接写入cbuffer，并且随附的`CBUFFER_END`宏替换缓冲区的末尾

```hlsl
CBUFFER_START(UnityPerFrame)
	float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
CBUFFER_END
```

这将会导致编译器错误，因为我们未定义这两个宏。我们将利用Unity的Core Library来渲染管道，而不是弄清楚何时适合使用常量缓冲区并自己定义宏。它可以通过包管理器窗口添加到我们的项目中。切换到All Packages列表并在Advanced下启用Show preview packages，然后选择Render-pipelines.core并安装它。

现在我们可以包含公共库功能，我们可以通过Packages / com.unity.render-pipelines.core / ShaderLibrary / Common.hlsl访问它。它定义了多个有用的函数和宏，以及常量缓冲区宏，使用之前需要include。

```hlsl
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

CBUFFER_START(UnityPerFrame)
float4x4 unity_MatrixVP;
CBUFFER_END
```

<hr>

编译目标等级

在包含库之后，我们的着色器无法为OpenGL ES 2编译。这是因为默认情况下，Unity使用不支持核心库的OpenGL ES 2的着色器编译器。我们可以通过在我们的着色器中添加#pragma prefer_hlslcc gles来解决这个问题，这就是Unity在Lightweight渲染管道中为其着色器所做的事情。但是，我们根本不会支持OpenGL ES 2，因为它仅在定位旧移动设备时才有用。我们通过使用#pragma target指令来定位着色器级别3.5而不是默认级别（2.5）来实现

```c#
			#pragma target 3.5
			
			#pragma vertex UnlitPassVertex
			#pragma fragment UnlitPassFragment
```

<hr>

###动态批处理

现在我们有了一个最小的自定义着色器，我们可以用它来进一步研究我们的管道如何渲染。一个很大的问题是它的效率如何。我们将通过使用我们Unlit material的一堆球体填充场景来测试它。

通过Frame debugger查看时，会注意到每个球体都需要自己单独的绘制调用。这不是很有效，因为每次绘制调用都会引入开销，因为CPU和GPU需要进行通信。理想情况下，多个球体可以通过一次调用一起绘制。

启用动态批处理

```c#
		var drawSettings = new DrawRendererSettings(
			camera, new ShaderPassName("SRPDefaultUnlit")
		);
		drawSettings.flags = DrawRendererFlags.EnableDynamicBatching;
		drawSettings.sorting.flags = SortFlags.CommonOpaque;
```

改变后，我们发现仍然没有动态批处理，但原因已经改变。动态批处理意味着Unity在绘制之前将对象合并在一个网格中。这需要每帧的CPU时间并保持检查它仅限于小网格。

submesh要求我们动态批处理的顶点数<= 300，球体的顶点数太大，而加入换成Cube，则可以完成动态批处理

颜色：

```shader
	Properties {
		_Color ("Color", Color) = (1, 1, 1, 1)
	}
```

```c#
CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
CBUFFER_END

CBUFFER_START(UnityPerMaterial)
	float4 _Color;
CBUFFER_END

struct VertexInput {
	float4 pos : POSITION;
};

…

float4 UnlitPassFragment (VertexOutput input) : SV_TARGET {
	return _Color;
}
```

可选的动态批处理

动态批处理可能是一个好处，也可能最终没有产生太大的影响，甚至可能减慢速度。如果场景中不包含许多共享相同材质的小网格，则禁用动态批处理可能是有意义的，因为Unity不必弄清楚是否每帧都使用它。

因此，我们将添加一个选项，以便为我们的管线启用动态批处理。我们不能依赖player setting。相反，我们向MyPipelineAsset添加了一个切换配置选项按钮，因此我们可以通过编辑器中的管道资产对其进行配置。

```c#
public class MyPipelineAsset : RenderPipelineAsset
{
    [SerializeField] private bool dynamicBatching;
    
    protected override IRenderPipeline InternalCreatePipeline()
    {
        return new MyPipeline(dynamicBatching);
    }
}

//MyPipeline.cs
//编写新的构造函数
	DrawRendererFlags drawFlags;

	public MyPipeline (bool dynamicBatching) {
		if (dynamicBatching) {
			drawFlags = DrawRendererFlags.EnableDynamicBatching;
		}
	}

...
  //Render()中
  		drawSettings.flags = drawFlags;
```

<hr>

###GPU实例化

动态批处理不是我们可以减少每帧绘制调用次数的唯一方法。另一种方法是使用GPU实例化。在实例化的情况下，**CPU通过单个绘制调用告诉GPU多次绘制特定的网格材质，然后进行组合**。这使得可以对使用相同网格和材质的对象进行分组，而无需构造新网格。这也消除了网格大小的限制。

可选的实例化

默认情况下启用GPU实例化，但我们使用自定义绘制标记覆盖它。让GPU实例化也是可选的，这样可以很容易地比较结果与否。将另一个切换添加到MyPipelineAsset并将其传递给构造函数调用。

```c#
	[SerializeField]
	bool instancing;
	
	protected override IRenderPipeline InternalCreatePipeline () {
		return new MyPipeline(dynamicBatching, instancing);
	}

//MyPipeline.cs
	public MyPipeline (bool dynamicBatching, bool instancing) {
		if (dynamicBatching) {
			drawFlags = DrawRendererFlags.EnableDynamicBatching;
		}
		if (instancing) {
      //注意这里使用的是OR操作符，flag只有一个，当两个都被打开时，unity更喜欢实现批处理
			drawFlags |= DrawRendererFlags.EnableInstancing;
		}
	}
```

Material支持

为我们的管道启用GPU实例化并不意味着对象是自动实例化的。它必须由他们使用的材料支持。因为并不总是需要实例化，所以它是可选的，这需要两个着色器变体：一个支持实例化，另一个不支持实例化。我们可以通过将`#pragma multi_compile_instancing`指令添加到着色器来创建所有必需的变体。在我们的例子中，它产生两个着色器变体，一个带有，另一个没有定义`INSTANCING_ON`关键字。

```shader
			#pragma target 3.5
			
			#pragma multi_compile_instancing
			
			#pragma vertex UnlitPassVertex
			#pragma fragment UnlitPassFragment
```

此更改还会为我们的材料显示新的材料配置选项：启用GPU实例化。

Shader的支持

启用实例化时，会告诉GPU使用相同的常量数据多次绘制相同的网格。但M矩阵是该数据的一部分。这意味着我们最终会以完全相同的方式多次渲染相同的网格。

要解决该问题，必须将包含所有对象的M矩阵的数组放入常量缓冲区中。每个实例都使用自己的索引绘制，该索引可用于从数组中检索正确的M矩阵。

我们现在必须要么在实例化时使用`unity_ObjectToWorld`，要么在实例化时使用矩阵数组。为了使两种情况下`UnlitPassVertex`中的代码保持相同，我们将为矩阵定义一个宏：`UNITY_MATRIX_M`。我们使用该宏名称，因为核心库有一个包含文件，该文件定义了支持我们实例化的宏，并且它还重新定义了`UNITY_MATRIX_M`以在需要时使用矩阵数组。（也就是说，define掉原来的obj->World的矩阵，新的包在include之后会取代`unity_ObjectToWorld`移作他用）

包含文件是`UnityInstancing.hlsl`，因为它可能重新定义`UNITY_MATRIX_M`，我们必须在自己定义宏之后包含它。下面是修改后的`Unlit.hlsl`

```hlsl
//使用constant buffer
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#define UNIYT_MATRIX_M unity_ObjectToWorld
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

//使用Unity提供的矩阵，使用constant buffer进行优化
CBUFFER_START(UnityPerFrame)
    float4x4 unity_ObjectToWorld;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
    float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerMaterial)
    float4 _Color;
CBUFFER_END

struct VertexInput
{
    float4 pos : POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput
{
    float4 clipPos : SV_POSITION;
};

VertexOutput UnlitPassVertex (VertexInput input)
{
    VertexOutput output;
    UNITY_SETUP_INSTANCE_ID(input);
    float4 worldPos = mul(UNIYT_MATRIX_M, float4(input.pos.xyz, 1.0));//显式float4，优化计算
    output.clipPos = mul(unity_MatrixVP, worldPos);//世界->投影
    return output;
}

float4 UnlitPassFragment(VertexOutput input) : SV_TARGET
{
    return _Color;
}
```

我们的立方体现在得到实例化。就像动态批处理一样，我们最终会有多个批次，因为我们使用的是不同的材料。确保使用的所有材料都启用了GPU实例化。

除了Obj->World的矩阵之外，默认情况下，world->obj矩阵也被放在实例化缓冲区中。这些是M矩阵的逆矩阵，当使用非均匀尺度时，它们是法向量所需的。但我们只使用统一尺度，因此我们不需要那些额外的矩阵。我们可以通过将`#pragma instancing_options assumeuniformscaling`指令添加到我们的着色器来告知Unity。如果确实需要支持非均匀缩放，则必须使用未启用此选项的着色器。

<hr>

### 更多的颜色

想在场景中包含更多颜色，我们需要制作更多材料，这意味着我们最终会有更多batches。但是如果矩阵可以放在数组中，应该可以对颜色做同样的事情。然后我们可以在一个批次中组合不同颜色的对象。

**支持每个对象的独特颜色**的第一步是可以为每个对象单独设置颜色。我们不能通过material来做到这一点，因为这是对象共享的资产。我们应该为它创建一个组件，命名为`InstancedColor`，为它提供一个可配置的颜色字段。

要覆盖材质的颜色，我们必须为对象的渲染器组件提供**材质属性块**。通过创建一个新的`MaterialPropertyBlock`对象实例，通过其`SetColor`方法为其赋予`_Color`属性，然后通过调用其`SetPropertyBlock`方法将其传递给对象的`MeshRenderer`组件。

```c#
public class InstancedColor : MonoBehaviour
{
    [SerializeField] private Color color = Color.white;
    private static MaterialPropertyBlock propertyBlock;
    private static int colorID = Shader.PropertyToID("_Color");

    void Awake()
    {
        OnValidate();
    }

    void OnValidate()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        propertyBlock.SetColor(colorID, color);
        GetComponent<MeshRenderer>().SetPropertyBlock(propertyBlock);
    }
}
```

将我们的组件添加到场景中的一个对象上，但只有在我们进入play mode后才会发生变化。要在edit mode下立即查看场景中的颜色更改，需要将设置颜色的代码移动到`OnValidate`方法。然后，`Awake`方法可以简单地调用`OnValidate`，因此我们不需要复制代码。（`OnValidate`是一种特殊的Unity消息方法。在编辑模式下，当加载或更改组件时，它会被调用。因此，每次加载场景和编辑组件时。因此，各个颜色立即出现。）

但是，覆盖每个对象的颜色会导致GPU实例化中断。虽然我们使用的是单一材质，但重要的是用于渲染的数据。当我们覆盖每个对象的颜色时，我们强制它们被单独绘制。

我们的想法是将颜色数据放在一个数组中，这将使实例化再次发挥作用。我们的`_Color`属性必须与M矩阵一样处理。在这种情况下，我们必须是显式的，因为核心库不会为任意属性重新定义宏。相反，我们通过`UNITY_INSTANCING_BUFFER_START`和随后的结束宏手动创建一个用于实例化的常量缓冲区，命名为`PerInstance`以保持我们的命名方案一致。在缓冲区内，我们将颜色定义为`UNITY_DEFINE_INSTANCED_PROP(float4，_Color)`。当没有使用实例化时，最终等于`float4 _Color`，否则我们最终得到一个实例数据数组。

```hlsl
//CBUFFER_START(UnityPerMaterial)
	//float4 _Color;
//CBUFFER_END

UNITY_INSTANCING_BUFFER_START(PerInstance)
	UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)
```

要处理现在可以定义颜色的两种可能方式，我们必须通过`UNITY_ACCESS_INSTANCED_PROP`宏访问它，并将其传递给我们的缓冲区和属性的名称。现在，实例索引也必须在`UnlitPassFragment`中可用。因此，将`UNITY_VERTEX_INPUT_INSTANCE_ID`添加到`VertexOutput`，然后像在`UnlitPassVertex`中一样在`UnlitPassFragment`中使用`UNITY_SETUP_INSTANCE_ID`。为了实现这一点，我们必须将索引从顶点输入复制到顶点输出，我们可以使用`UNITY_TRANSFER_INSTANCE_ID`宏。

```hlsl
struct VertexInput {
	float4 pos : POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput {
	float4 clipPos : SV_POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexOutput UnlitPassVertex (VertexInput input) {
	VertexOutput output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
	output.clipPos = mul(unity_MatrixVP, worldPos);
	return output;
}

float4 UnlitPassFragment (VertexOutput input) : SV_TARGET {
	UNITY_SETUP_INSTANCE_ID(input);
	return UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color);
}
```

所有对象现在最终组合在一个绘制调用中，即使它们都使用不同的颜色。但是，在常量缓冲区中可以放入多少数据是有限制的。最大实例批量大小取决于每个实例的数据变化量。除此之外，缓冲区最大值因平台而异。此外，我们仍然只能使用相同的网格和材料。后面我将会加入光照。