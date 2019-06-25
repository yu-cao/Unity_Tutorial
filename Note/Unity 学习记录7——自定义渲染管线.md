##Unity 学习记录——自定义渲染管线

以下内容使用的是Unity 2018.4.2f1 LTS版本，如果用2019版本可能有名字空间或者API名称有变化，需要查阅手册进行（而且可能会遇到很多坑还没被踩过，掉进去很容易一时半会出不来）而且笔者是C++，对于C#用词可能不正确，望指正

之前Unity支持两个预定义的pipeline，一个用于前向渲染，一个用于延迟渲染，开发者只能启用，禁用或者覆盖管道的一些功能，但是不可能大幅度偏离设计修改渲染管线

Unity 2018增加了对可编写脚本的渲染管道的支持，使得从头开始设计管道成为可能，尽管仍然需要依赖Unity来完成许多单独的步骤，例如剔除。 Unity 2018推出了两种采用这种新方法制造的新管道，轻量级管道和高清管道。两个管道仍处于预览阶段，可编写脚本的渲染管道API仍标记为实验技术。但在这一点上它足够稳定，我们可以继续创建自己的管道。

我们将设置一个绘制未点亮的形状的mini渲染管线。为来我们可以扩展我们的管道，添加光照，阴影和更高级的功能。

<hr>

我们将使用线性空间，所以在*Edit / Project Settings / Player* 中调整*Other Setting/Rendering/Color Space*改成Linear（默认值为Gamma空间），同时将*Window / Package*中的Package部分尽量删除，因为我们不需要

Unity默认使用的是正向渲染管线，当我们使用自定义渲染管线时，我们需要在*Edit/Project Setting/Graphics*中选择一个渲染方式（默认为none）

我们要为我们的自定义管线资产创建一个新的script，它必须继承`RenderPipelineAssist`，这个东西在`UnityEngine.Experimental.Rendering`的名字空间中定义

```c#
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class MyPipelineAsset : RenderPipelineAsset
{
    protected override IRenderPipeline InternalCreatePipeline()
    {
        return new MyPipeline();
    }
}
```

建立这个pipeine资产的目的是给Unity一个方法去拥有一个pipeline对象的实例来进行渲染。资产本身只是一个handle或者一个存储pipeline设置的地方。我们通过重载`CreatePipeline`的方式进行实例化，返回值为MyPipeline这个类（之后马上回给出定义）

我们接下来需要把这个类型的asset加入到我们的项目中，为此，我们在前面加入：

```c#
[CreateAssetMenu(menuName = "Rendering/My Pipeline")]
public class MyPipelineAsset : RenderPipelineAsset{...}
```

这就能够在*Asset/Create*菜单中产生一个条目，我们把它放在Rendering的子菜单中，然后在我们的项目中创建一个这个asset，放到之前说的本来是`none`的*Scriptable Render Pipeline Settings*中

替换之后，我们通过*Window / Analysis / Frame Debugger*查看可以看到，的确，什么都没有被渲染出来了，因为我们在没有提供有效替换的情况下绕过了默认管道。同时，在*Project Setting/Graphics*中的很多设置也发生了变化或者消失

<hr>

接下来就要创建我们的渲染管线了，Render是虚基类的接口，所以我们要在这里给出实现（RenderPipeline.Render不绘制任何内容，但检查管道对象是否有效用于渲染，第一个参数是渲染内容，第二个参数的相机数组）：

```c#
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class MyPipeline : RenderPipeline 
{
	public override void Render (ScriptableRenderContext renderContext, Camera[] cameras) 
	{
		base.Render(renderContext, cameras);
	}
}
```

最简单的方法是先绘制一个天空盒，我们将代码修改如下，这里，相机的方向不会影响天空盒的绘制。我们把相机参数传给DrawSkybox函数但事实上它只是来检查skybox是否应该被绘制

而要正确渲染天空盒和场景，我们要设定view->project矩阵。目前，这个矩阵(`unity_MatrixVP`)总是一样的，我们要把相机属性传给context需要通过它的`SetupCameraProperties`这个方法

```c#
public class MyPipeline : RenderPipeline
{
    public override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
    {
        base.Render(renderContext, cameras);

        foreach (var camera in cameras)
        {
            Render(renderContext, camera);
        }
    }

    void Render(ScriptableRenderContext context, Camera camera)
    {
        context.SetupCameraProperties(camera);

        context.DrawSkybox(camera);
        context.Submit();
    }
}
```

现在可以在Editor和Game中都能看到Skybox，证明已经成功了

<hr>

###Command Buffer

Context会相比较实际的渲染存在延迟，直到我们submit。在此之前，我们可以对它进行配置以供后续的执行。某些命令可以有专用方法发出执行，但是绝大多数命令需要通过一个命令缓冲区(command buffer)间接发出执行，这个对象定义在`UnityEngine.Rendering`名字空间中

```c#
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class MyPipeline : RenderPipeline
{
    public override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
    {
        base.Render(renderContext, cameras);

        foreach (var camera in cameras)
        {
            Render(renderContext, camera);
        }
    }

    void Render(ScriptableRenderContext context, Camera camera)
    {
        context.SetupCameraProperties(camera);
        var buffer = new CommandBuffer();
        buffer.ClearRenderTarget(true, false, Color.clear);
        context.ExecuteCommandBuffer(buffer);
        buffer.Release();

        context.DrawSkybox(camera);
        context.Submit();
    }
}
```

我们这里的`ExecuteCommandBuffer`不会立刻执行命令，而是把命令存储到内部的context的buffer中，然后最好要在执行这个语句后立刻Release掉buffer（如果我们不再需要原先在里面的东西）

执行空的command buffer就是什么都不做。我们添加它以便清除render target，以确保渲染不受先前绘制的内容的影响。这可以通过command buffer实现，但不能直接通过context实现。

可以通过调用ClearRenderTarget将清除命令添加到缓冲区。它需要三个参数：两个布尔值和一个颜色。第一个参数控制是否清除深度信息，第二个参数控制是否清除颜色，第三个参数是清除后替代的颜色

我们可以通过`CameraClearFlags`来确认我们想要清除的目标是否正确，这是一个枚举类，可以用作一组位标志。该值的每个位用于指示是否启用某个功能。(使用AND操作进行)

```c#
		CameraClearFlags clearFlags = camera.clearFlags;
		buffer.ClearRenderTarget(
			(clearFlags & CameraClearFlags.Depth) != 0,
			(clearFlags & CameraClearFlags.Color) != 0,
			camera.backgroundColor
		);
```

我们可以给command buffer命名来增加可读性

```c#
		var buffer = new CommandBuffer {
			name = camera.name
		};
```

<hr>

###剔除

我们现在只能渲染天空盒，我们对于里面的物体无法进行渲染。针对相机，我们要注意的是我们不是渲染整个场景，而是渲染在相机视椎体内的东西。换言之，我们剔除那些落在摄像机视锥之外物体。

找出可以剔除的内容需要我们跟踪多个相机设置和矩阵，我们可以使用`ScriptableCullingParameters`结构。我们可以将该工作委托给静态`CullResults.GetCullingParameters`方法，而不是自己填充它。它将摄像机作为输入并产生剔除参数作为输出。但是注意，我们必须自己提供可供它储存的参数。同时这个方法的返回值表示是否创建了有效参数，当无效时，我们可以退出渲染。即：

```c#
	void Render (ScriptableRenderContext context, Camera camera) {
		ScriptableCullingParameters cullingParameters;
		if (!CullResults.GetCullingParameters(camera, out cullingParameters))
    {
      return;
    }

		…
	}
```

一旦我们有了剔除参数，我们就可以用它们来剔除。这是通过使用剔除参数和上下文作为参数调用静态CullResults.Cull方法来完成的。结果是一个CullResults结构，其中包含有关可见内容的信息。即：

```c#
		if (!CullResults.GetCullingParameters(camera, out cullingParameters)) {
			return;
		}

		CullResults cull = CullResults.Cull(ref cullingParameters, context);
```

现在，我们知道了哪些东西会被剔除，我们可以绘制我们自己的东西了

这个实现在`DrawRenderers`这个函数中，用cull.visibleRenderers作为第一个参数，告诉它哪些东西会被渲染，这个函数还要求我们提供`DrawRendererSettings`和`FilterRenderersSettings`两个结构，后面的过滤器参数为`true`代表我们将会不过滤任何东西

```c#
        buffer.Release();
        
        var drawSetting = new DrawRendererSettings();
        var filterSettings = new FilterRenderersSettings(true);

        context.DrawRenderers(cull.visibleRenderers, ref drawSetting, filterSettings);
        
        context.DrawSkybox(camera);
```

此外，我们必须通过为其构造函数提供相机和着色器传递作为参数来配置绘图设置。相机用于设置排序和剔除图层，而着色器通过的通道控件用于渲染。

着色器传递通过字符串标识，该字符串必须包装在`ShaderPassName`结构中。由于我们只支持pipeline中的unlit material，因此我们将使用Unity的default unlit pass，通过`SRPDefaultUnlit`识别。

```c#
		var drawSettings = new DrawRendererSettings(
			camera, new ShaderPassName("SRPDefaultUnlit")
		);
```

现在，我们能看到Unlit Opaque这个material，但是带透明的Unlit Transparent的material还是不可见的，但是我们在Frame Debug中发现其实它也是被渲染了的，为什么会这样呢？

因为我们没写入深度信息，使得最后渲染的skybox覆盖了它，解决方案也很简单：延迟绘制透明物体，直到天空盒之后

这里我们配置如下：

我们限制skybox之前只能绘制Opaque的物体：这部分操作通过设置`renderQueueRange`来进行，这个队列表示渲染队列为0~2500；之后改变渲染队列为`RenderQueueRange.transparent`（渲染队列为2500~5000）再进行一次渲染

```c#
		var filterSettings = new FilterRenderersSettings(true) {
			renderQueueRange = RenderQueueRange.opaque
		};

		context.DrawRenderers(
			cull.visibleRenderers, ref drawSettings, filterSettings
		);

		context.DrawSkybox(camera);

//重设渲染队列，再渲染
		filterSettings.renderQueueRange = RenderQueueRange.transparent;
		context.DrawRenderers(
			cull.visibleRenderers, ref drawSettings, filterSettings
		);
```

**但是这也做也会有问题：不透明物体间的相互遮挡等**。为了尽可能减少过度绘制，我们应该首先绘制最近的形状。这可以通过在绘制之前对物体进行排序来完成，这是通过`SortFlags`来控制的。

drawSettngs中包含`DrawRendererSortSettings`类型的排序结构，其中包含排序标志。在绘制不透明形状之前将其设置为`SortFlags.CommonOpaque`。这指示Unity按距离，从前到后以及其他一些标准对被渲染物体进行排序。

```c#
		var drawSettings = new DrawRendererSettings(
			camera, new ShaderPassName("SRPDefaultUnlit")
		);
		drawSettings.sorting.flags = SortFlags.CommonOpaque;
```

但是，透明渲染的工作方式是通过blend使得结果显得透明。这需要从后到前的反向绘制顺序。我们可以使用`SortFlags.CommonTransparent`

```c#
		context.DrawSkybox(camera);

		drawSettings.sorting.flags = SortFlags.CommonTransparent;
		filterSettings.renderQueueRange = RenderQueueRange.transparent;
		context.DrawRenderers(
			cull.visibleRenderers, ref drawSettings, filterSettings
		);
```

<hr>

### 优化

SPR能够正确渲染只是它的一部分（当然是最重要的一部分）还有其他需要考虑的事项，例如它是否足够快，是否会产生多余的临时对象，是否与Unity Editor的良好集成。

让我们检查一下我们的管线在内存管理方面是否表现良好，或者它是否每帧都分配内存，因为这将触发频繁的GC。通过*Window / Analysis / Profiler*在Hierarchy mode下检查CPU使用率数据来完成的。

我们可以看到GC有一个很频繁的周期。查看可以发现在Render中会有较大的GC，我们看改进策略：`CullResults`的可重用化

```c#
	CullResults cull;

	…

	void Render (ScriptableRenderContext context, Camera camera) {
		…

		//CullResults cull = CullResults.Cull(ref cullingParameters, context);
		CullResults.Cull(ref cullingParameters, context, ref cull);
		
		…
	}
```

另一个来源是我们使用相机的name属性。每次我们得到它的值时，它从native code中获取名称数据，这需要创建一个新的字符串。所以我们将命令缓冲区永久性命名为Render Camera即可。此外command buffer本身也是一个对象，我们也让它可重用。用cameraBuffer字段替换局部变量。由于对象初始化语法，我们可以创建一个命名命令缓冲区作为其默认值。唯一的另一个变化是我们必须清除命令缓冲区而不是Release，我们可以使用它的Clear方法。

```c#
	CommandBuffer cameraBuffer = new CommandBuffer {
		name = "Render Camera"
	};

	…

	void Render (ScriptableRenderContext context, Camera camera) {
		…

		//var buffer = new CommandBuffer() {
		//	name = "Render Camera"
		//};
		cameraBuffer.ClearRenderTarget(true, false, Color.clear);
		context.ExecuteCommandBuffer(cameraBuffer);
		//buffer.Release();
		cameraBuffer.Clear();

		…
	}
```

现在，性能调优暂时告一段落。

<hr>

### Frame Debugger采样

这里我们想改进Frame Debugger显示的数据。 Unity的管道显示事件的嵌套层次结构，但我们的管道都在根级别。我们可以使用Command buffer来开始和结束探查器的采样来构建层次结构。

```c#
		cameraBuffer.BeginSample("Render Camera");
		cameraBuffer.ClearRenderTarget(true, false, Color.clear);
		cameraBuffer.EndSample("Render Camera");
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();
```

我们现在看到一个Render Camera级别嵌套在命令缓冲区的原始Render Camera中，而后者又包含clear操作。

我们更进一步，将与相机相关的所有其他动作嵌套在其中。这要求我们在提交上下文之前延迟采样的结束。所以我们必须在那时插入一个额外的ExecuteCommandBuffer，只包含结束样本的指令。为此使用相同的命令缓冲区，在完成后再次清除它。

```c#
		cameraBuffer.BeginSample("Render Camera");
		cameraBuffer.ClearRenderTarget(true, false, Color.clear);
		//cameraBuffer.EndSample("Render Camera");
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();

		…

		cameraBuffer.EndSample("Render Camera");
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();

		context.Submit();
```



<hr>

渲染默认的管线

由于我们的管道仅支持Unlit Shader，因此不会渲染使用不同着色器的对象，从而使它们不可见。虽然这是正确的，但它隐藏了一些事物，即某些对象使用了错误的着色器。Unity的默认管线中把它们显示为明显不正确的洋红色。让我们为此添加一个专用的DrawDefaultPipeline方法，带有context和摄像头参数。在绘制完透明形状后，我们将在最后调用它。

```c#
	void Render (ScriptableRenderContext context, Camera camera) {
		…

		drawSettings.sorting.flags = SortFlags.CommonTransparent;
		filterSettings.renderQueueRange = RenderQueueRange.transparent;
		context.DrawRenderers(
			cull.visibleRenderers, ref drawSettings, filterSettings
		);

		DrawDefaultPipeline(context, camera);

		cameraBuffer.EndSample("Render Camera");
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();

		context.Submit();
	}

	void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera) {}
```

Unity默认的surface shader有一个ForwardBass的pass，只是被用来作为正向渲染的第一个pass。我们可以用它来辨识具有与默认管线使用相同材质的对象。通过新的设定选择这个pass，配合新的filter设定一起用来渲染。我们在这里不关心渲染队列，因为他们是无效的

```c#
	void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera) {
		var drawSettings = new DrawRendererSettings(
			camera, new ShaderPassName("ForwardBase")
		);
		
		var filterSettings = new FilterRenderersSettings(true);
		
		context.DrawRenderers(
			cull.visibleRenderers, ref drawSettings, filterSettings
		);
	}
```

现在，我们可以看到Standard系列的球了，但是我们会发现——它们没有颜色，只有黑色和根据Alpha的透明度

因为我们的管线不支持forward base pass，所以它们无法被正确渲染。没有设置必要的数据，因此依赖于光照的所有内容都会变为黑色。我们应该用一个比较明显错误的颜色对它们进行渲染以提醒我们

为此，我们需要一个“error material”，为此添加字段。然后，在DrawDefaultPipeline的开头，创建error material（如果它尚不存在）。这是通过Shader.Find检索Hidden / InternalErrorShader，然后使用该着色器创建新材质来完成的。此外，将材质的隐藏标志设置为HideFlags.HideAndDontSave，这样它就不会显示在项目窗口中，也不会与所有其他资源一起保存。

```c#
	Material errorMaterial;

	…

	void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera) {
		if (errorMaterial == null) {
			Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
			errorMaterial = new Material(errorShader) {
				hideFlags = HideFlags.HideAndDontSave
			};
		}
		
		…
	}
```

在draw setting中一个选项是当渲染时重载使用的material，使用时的其中的`SetOverrideMaterial`方法，第一个参数为我们使用的material，第二个参数指出用于渲染的material's shader的pass的索引，这里因为我们使用的是表达error的shader，所以我们只需要1个pass就可以了，也就是0。

```c#
		var drawSettings = new DrawRendererSettings(
			camera, new ShaderPassName("ForwardBase")
		);
		drawSettings.SetOverrideMaterial(errorMaterial, 0);
```

现在，使用不受支持的材料的对象清楚地显示为不正确的颜色。但这仅适用于Unity默认管道的材质，其着色器具有ForwardBase传递。我们可以通过不同的pass去识别其他内置着色器，特别是PrepassBase，Always，Vertex，VertexLMRGBM和VertexLM。

幸运的是，可以通过调用SetShaderPassName向绘图设置添加多个传递。名称是此方法的第二个参数。它的第一个参数是一个控制draw pass顺序的索引。我们不关心这一点，所以任何顺序都没关系。通过构造函数提供的传递总是具有索引零，只是递增索引以获得额外的传递即可

```c#
		var drawSettings = new DrawRendererSettings(
			camera, new ShaderPassName("ForwardBase")
		);
		drawSettings.SetShaderPassName(1, new ShaderPassName("PrepassBase"));
		drawSettings.SetShaderPassName(2, new ShaderPassName("Always"));
		drawSettings.SetShaderPassName(3, new ShaderPassName("Vertex"));
		drawSettings.SetShaderPassName(4, new ShaderPassName("VertexLMRGBM"));
		drawSettings.SetShaderPassName(5, new ShaderPassName("VertexLM"));
		drawSettings.SetOverrideMaterial(errorMaterial, 0);
```

这涵盖了Unity提供的所有着色器，这足以指出在创建场景时使用不正确的材料。但是我们只需要在开发期间这样做，而不是在build中。所以我们只在编辑器中调用DrawDefaultPipeline。一种方法是向方法添加Conditional属性。（即条件编译）

<hr>

### 条件代码执行

Conditional属性在System.Diagnostics命名空间中定义。我们可以使用该命名空间，但遗憾的是它还包含一个Debug类型，它与UnityEngine.Debug冲突。由于我们只需要属性，我们可以通过使用别名来避免冲突。我们使用特定类型并将其分配给有效的类型名称，而不是使用整个命名空间。在这种情况下，我们将Conditional定义为System.Diagnostics.ConditionalAttribute的别名：

```c#
using Conditional = System.Diagnostics.ConditionalAttribute;
```

将属性添加到我们的这个方法中。它需要一个指定符号的字符串参数。如果在编译期间定义了符号，则方法调用将正常包含它在内。但是，如果未定义符号，则将会省略此方法的调用（包括其所有参数）

要仅在编译Unity编辑器时包含调用，我们必须依赖UNITY_EDITOR符号。在开发构建中的调用，需要将其从发布版本中排除，为此，还需要添加DEVELOPMENT_BUILD符号。

```c#
	[Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
	void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera) {
		…
	}
```

<hr>

在Scene Window中的UI

到目前为止我们还没有考虑过的一件事是Unity的游戏内UI。要测试它，请通过GameObject / UI / Button将UI元素添加到场景中，例如单个按钮。这创建了一个带有按钮的canvas（画布），还有一个事件系统。

事实上，UI会在游戏窗口中渲染，而我们无需做任何事情。 Unity为我们照顾它。frame debugger显示UI作为叠加层单独渲染。

至少，当画布设置为在屏幕空间中渲染时就是这种情况。当设置为在世界空间中渲染时，UI将与其他透明对象一起渲染。

尽管UI在游戏窗口中起作用，但它不会显示场景窗口。 UI始终存在于场景窗口的世界空间中，但我们必须手动将其注入场景中。通过调用静态ScriptableRenderContext.EmitWorldGeometryForSceneView方法，以当前相机作为参数来添加UI。这必须在cull操作之前完成。但这也在游戏窗口中第二次添加了UI。为了防止这种情况，我们必须在渲染场景窗口时才emit UI geometry。也就是`camera.cameraType == CameraType.SceneView`的时候

```c#
		if (!CullResults.GetCullingParameters(camera, out cullingParameters)) {
			return;
		}

		if (camera.cameraType == CameraType.SceneView) {
			ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
		}
		CullResults.Cull(ref cullingParameters, context, ref cull);
```

这仅限于编辑器中有效。条件编译确保在编译构建时不存在`EmitWorldGeometryForSceneView`，这意味着我们现在在尝试构建时遇到编译器错误。为了使它再次工作，我们必须使调用`EmitWorldGeometryForSceneView`的代码也是有条件的。这是通过将代码放在#if和#endif语句之间来完成的。 #if语句需要一个符号，就像Conditional属性一样。通过使用UNITY_EDITOR，仅在编译编辑器时才包含代码。