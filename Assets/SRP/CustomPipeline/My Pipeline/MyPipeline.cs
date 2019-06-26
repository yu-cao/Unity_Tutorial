using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipeline : RenderPipeline
{
    private DrawRendererFlags drawFlags;

    public MyPipeline(bool dynamicBatching, bool instancing)
    {
        if (dynamicBatching)
        {
            drawFlags = DrawRendererFlags.EnableDynamicBatching;
        }

        if (instancing)
        {
            //注意这里使用的是OR操作符，flag只有一个，当两个都被打开时，unity更喜欢实现批处理
            drawFlags |= DrawRendererFlags.EnableInstancing;
        }
    }
        
    public override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
    {
        base.Render(renderContext, cameras);

        foreach (var camera in cameras)
        {
            Render(renderContext, camera);
        }
    }

    private CullResults cull;//性能优化：将对象可重用减少GC

    private CommandBuffer cameraBuffer = new CommandBuffer
    {
        name = "Render Camera"//性能优化：使用恒定的buffer name，并且使得其可重用
    };
    
    
    void Render(ScriptableRenderContext context, Camera camera)
    {
        ScriptableCullingParameters cullingParameters;
        if (!CullResults.GetCullingParameters(camera, out cullingParameters))
        {
            return;
        }
#if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
#endif
        
        //CullResults cull = CullResults.Cull(ref cullingParameters, context);
        CullResults.Cull(ref cullingParameters, context, ref cull);
        
        context.SetupCameraProperties(camera);

        cameraBuffer.ClearRenderTarget(true, false, Color.clear);
        cameraBuffer.BeginSample("Render Camera");
        
        context.ExecuteCommandBuffer(cameraBuffer);
        //cameraBuffer.Release();
        cameraBuffer.Clear();
        
        var drawSetting = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit"));
        drawSetting.flags = drawFlags;
        drawSetting.sorting.flags = SortFlags.CommonOpaque;
        
        var filterSettings = new FilterRenderersSettings(true)
        {
            renderQueueRange = RenderQueueRange.opaque
        };

        context.DrawRenderers(cull.visibleRenderers, ref drawSetting, filterSettings);
        
        context.DrawSkybox(camera);
        
        drawSetting.sorting.flags = SortFlags.CommonTransparent;
        filterSettings.renderQueueRange = RenderQueueRange.transparent;
        context.DrawRenderers(cull.visibleRenderers, ref drawSetting, filterSettings);
        
        DrawDefaultPipeline(context, camera);//使用Unity默认的管线
        
        //Profiler采样
        cameraBuffer.EndSample("Render Camera");
        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();
        
        context.Submit();
    }

    private Material errorMaterial;
    [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
    void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
    {
        if (errorMaterial == null)
        {
            Shader errorShader= Shader.Find("Hidden/InternalErrorShader");
            errorMaterial = new Material(errorShader) {hideFlags = HideFlags.HideAndDontSave};
        }
        var drawSetting = new DrawRendererSettings(camera, new ShaderPassName("ForwardBase"));
        drawSetting.SetOverrideMaterial(errorMaterial, 0);//设置表达 使用了错误shader的material
        
        var filterSetting = new FilterRenderersSettings(true);
        
        context.DrawRenderers(cull.visibleRenderers, ref drawSetting, filterSetting);
    }
}