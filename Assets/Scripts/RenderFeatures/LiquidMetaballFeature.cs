using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class LiquidMetaballFeature : ScriptableRendererFeature
{
    [Serializable]
    public class LiquidMetaballFeatureSettings
    {
        public LayerMask liquidLayer;
        public Material thresholdMaterial;
        public RenderPassEvent RenderTiming = RenderPassEvent.AfterRenderingTransparents;
    }
    
    public LiquidMetaballFeatureSettings settings = new LiquidMetaballFeatureSettings();
    private LiquidMetaballFeaturePass m_MetalballPass;

    public override void Create()
    {
        m_MetalballPass = new LiquidMetaballFeaturePass(settings.liquidLayer, settings.thresholdMaterial);
        m_MetalballPass.renderPassEvent = settings.RenderTiming;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.thresholdMaterial == null)
        {
            Debug.LogWarning("Liquid metaball feature requires a material");
            return;
        }
        
        renderer.EnqueuePass(m_MetalballPass);
    }

    class LiquidMetaballFeaturePass : ScriptableRenderPass
    {
        private LayerMask m_LayerMask;
        private Material m_ThresholdMaterial;

        public LiquidMetaballFeaturePass(LayerMask layerMask, Material material)
        {
            m_LayerMask = layerMask;
            m_ThresholdMaterial = material;
        }

        private class ParticlePassData
        {
            public RendererListHandle RendererListHandle;
        }

        private class CompositePassData
        {
            public TextureHandle sourceTexture;
            public Material material;
        }

        static void ExecutePass(ParticlePassData data, RasterGraphContext context)
        {
            context.cmd.ClearRenderTarget(false, true, Color.clear);
            context.cmd.DrawRendererList(data.RendererListHandle);
        }

        static void ExecuteCompositePass(CompositePassData data, RasterGraphContext context)
        {
            Blitter.BlitTexture(context.cmd, data.sourceTexture, new Vector4(1, 1, 0, 0), data.material, 0);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle accumulationTexture;
            
            // pass ONE
            using (var builder = renderGraph.AddRasterRenderPass<ParticlePassData>("Liquid Particle Accumulation", out var passData))
            {
                RenderTextureDescriptor cameraDesc = cameraData.cameraTargetDescriptor;
                TextureDesc textureDesc = new TextureDesc(cameraDesc.width, cameraDesc.height)
                {
                    colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                    depthBufferBits = 0,
                    useMipMap = false,
                    msaaSamples = MSAASamples.None,
                    name = "LiquidAccumulationBuffer",
                    clearBuffer = true,
                    clearColor = Color.clear
                };

                accumulationTexture = renderGraph.CreateTexture(textureDesc);
                
                builder.SetRenderAttachment(accumulationTexture, 0, AccessFlags.Write);

                FilteringSettings filterSettings = new FilteringSettings(RenderQueueRange.transparent, m_LayerMask);

                SortingSettings sortingSettings = new SortingSettings(cameraData.camera)
                {
                    criteria = SortingCriteria.CommonTransparent
                };

                DrawingSettings drawSettings =
                    new DrawingSettings(new ShaderTagId("Universal Forward"), sortingSettings);
                drawSettings.SetShaderPassName(1, new ShaderTagId("SRPDefaultUnlit"));

                RendererListParams listParams =
                    new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);

                passData.RendererListHandle = renderGraph.CreateRendererList(listParams);
                builder.UseRendererList(passData.RendererListHandle);
                builder.SetRenderFunc((ParticlePassData data, RasterGraphContext context) => ExecutePass(data, context));
            }
            
            // pass TWO
            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Liquid Threshold Composite", out var passData))
            {
                if (!accumulationTexture.IsValid()) return;

                passData.sourceTexture = accumulationTexture;
                passData.material = m_ThresholdMaterial;
                
                builder.UseTexture(passData.sourceTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc((CompositePassData data, RasterGraphContext context) => ExecuteCompositePass(data, context));
            }
        }
    }
}
