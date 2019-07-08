using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipeline : RenderPipeline
{
    private DrawRendererFlags drawFlags;

    public MyPipeline(bool dynamicBatching, bool instancing)
    {
        GraphicsSettings.lightsUseLinearIntensity = true;

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

    private static int lightIndicesOffsetAndCountID = Shader.PropertyToID("unity_LightIndicesOffsetAndCount");
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

        if (cull.visibleLights.Count > 0) 
        {
            ConfigureLights();
        }
        else//控制避免0光源情况，这种情况下直接置为0
        {
            cameraBuffer.SetGlobalVector(lightIndicesOffsetAndCountID, Vector4.zero);
        }


        cameraBuffer.BeginSample("Render Camera");

        //传入4个buffer控制光源颜色、定向光方向/点光源位置、点光源范围、聚光灯方向
        cameraBuffer.SetGlobalVectorArray(visibleLightColorsID, visibleLightColors);
        cameraBuffer.SetGlobalVectorArray(visibleLightDirectionsOrPositionsID, visibleLightDirectionsOrPosition);
        cameraBuffer.SetGlobalVectorArray(visibleLightAttenuationsID, visibleLightAttenuations);
        cameraBuffer.SetGlobalVectorArray(visibleLightSpotDirectionsID, VisibleLightSpotDirections);
        
        
        context.ExecuteCommandBuffer(cameraBuffer);
        //cameraBuffer.Release();
        cameraBuffer.Clear();

        var drawSetting = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit"))
        {
            flags = drawFlags//,
            //rendererConfiguration = RendererConfiguration.PerObjectLightIndices8
        };
        if (cull.visibleLights.Count > 0)//控制光源数量不会出现<=0的情况
        {
            drawSetting.rendererConfiguration = RendererConfiguration.PerObjectLightIndices8;
        }

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

    private const int maxVisibleLights = 16;
    private static int visibleLightColorsID = Shader.PropertyToID("_VisibleLightColors");
    private static int visibleLightDirectionsOrPositionsID = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
    private static int visibleLightAttenuationsID = Shader.PropertyToID("_VisibleLightAttenuations");
    private static int visibleLightSpotDirectionsID = Shader.PropertyToID("_VisibleLightSpotDirections");
    
    Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
    Vector4[] visibleLightDirectionsOrPosition = new Vector4[maxVisibleLights];
    Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
    Vector4[] VisibleLightSpotDirections = new Vector4[maxVisibleLights];

    void ConfigureLights()
    {
        //int i = 0;
        for (int i = 0; i < cull.visibleLights.Count; i++)
        {
            if (i == maxVisibleLights)//保证数组的安全性
            {
                break;
            }
            VisibleLight light = cull.visibleLights[i];
            visibleLightColors[i] = light.finalColor;
            
            Vector4 attenuation = Vector4.zero;
            attenuation.w = 1f;

            if (light.lightType == LightType.Directional)
            {
                Vector4 v = light.localToWorld.GetColumn(2);
                v.x = -v.x;
                v.y = -v.y;
                v.z = -v.z;
            }
            else
            {
                visibleLightDirectionsOrPosition[i] = light.localToWorld.GetColumn(3);
                
                //在点光源的情况下，把范围放在矢量的X分量中
                attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.000001f);
                
                //考察是否是聚光灯的情况，如果是，跟定向光一样要对光翻转成为obj->光源的形式
                if (light.lightType == LightType.Spot)
                {
                    Vector4 v = light.localToWorld.GetColumn(2);
                    v.x = -v.x;
                    v.y = -v.y;
                    v.z = -v.z;
                    VisibleLightSpotDirections[i] = v;

                    float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;//转换到弧度
                    float outerCos = Mathf.Cos(outerRad);
                    float outerTan = Mathf.Tan(outerRad);
                    float innerCos = Mathf.Cos(Mathf.Atan(46f / 64f * outerTan));

                    float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
                    attenuation.z = 1f / angleRange;
                    attenuation.w = -outerCos * attenuation.z;

                }
            }

            visibleLightAttenuations[i] = attenuation;
        }

        //得到可见光索引列表，如果超过上限，超过的都置-1（防止数组越界）
        if (cull.visibleLights.Count > maxVisibleLights)
        {
            int[] lightIndices = cull.GetLightIndexMap();
            for (int i = maxVisibleLights; i < cull.visibleLights.Count; i++)
            {
                lightIndices[i] = -1;
            }
            cull.SetLightIndexMap(lightIndices);
        }


        //清除不需要的光源
//        for (; i < maxVisibleLights; i++)
//        {
//            visibleLightColors[i] = Color.clear;
//        }
    }
}