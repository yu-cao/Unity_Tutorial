##Unity 学习记录——自定义渲染管线

以下内容使用的是Unity 2018.4.2f1 LTS版本，如果用2019版本可能有名字空间或者API名称有变化，需要查阅手册进行（而且可能会遇到很多坑还没被踩过，掉进去很容易一时半会出不来）

之前Unity支持两个预定义的pipeline，一个用于前向渲染，一个用于延迟渲染，开发者只能启用，禁用或者覆盖管道的一些功能，但是不可能大幅度偏离设计修改渲染管线

Unity 2018增加了对可编写脚本的渲染管道的支持，使得从头开始设计管道成为可能，尽管仍然需要依赖Unity来完成许多单独的步骤，例如剔除。 Unity 2018推出了两种采用这种新方法制造的新管道，轻量级管道和高清管道。两个管道仍处于预览阶段，可编写脚本的渲染管道API仍标记为实验技术。但在这一点上它足够稳定，我们可以继续创建自己的管道。

我们将设置一个绘制未点亮的形状的mini渲染管线。为来我们可以扩展我们的管道，添加光照，阴影和更高级的功能。

<hr>

我们将使用线性空间，所以在*Edit / Project Settings / Player* 中调整*Other Setting/Rendering/Color Space*改成Linear（默认值为Gamma空间），同时将*Window / Package*中的Package部分尽量删除，因为我们不需要

Unity默认使用的是正向渲染管线，当我们使用自定义渲染管线时，我们需要在*Edit/Project Setting/Graphics*中选择一个渲染方式（默认为none）

我们要为我们的自定义管线资产创建一个新的script，它必须继承`RenderPipelineAssist`，这个东西在`UnityEngine.Rendering`的名字空间中定义

```c#
using UnityEngine;
using UnityEngine.Rendering;

public class MyPipelineAsset : RenderPipelineAsset
{
    protected override RenderPipeline CreatePipeline()
    {
        return null;
    }
}
```

建立这个pipeine资产的目的是给Unity一个方法去拥有一个pipeline对象的实例来进行渲染。资产本身只是一个handle或者一个存储pipeline设置的地方。我们通过重载`CreatePipeline`的方式进行实例化，现在还没有定义我们pipeline object的类型，我们这里直接返回null

我们接下来需要把这个类型的asset加入到我们的项目中，为此，我们在前面加入：

```c#
[CreateAssetMenu]
public class MyPipelineAsset : RenderPipelineAsset{...}
```

这就能够在*Asset/Create*菜单中产生一个条目，我们把它放在Rendering的子菜单中，然后在我们的项目中创建一个这个asset，放到之前说的本来是`none`的*Scriptable Render Pipeline Settings*中

替换之后，我们通过*Window / Analysis / Frame Debugger*查看可以看到，的确，什么都没有被渲染出来了，因为我们在没有提供有效替换的情况下绕过了默认管道。同时，在*Project Setting/Graphics*中的很多设置也发生了变化或者消失