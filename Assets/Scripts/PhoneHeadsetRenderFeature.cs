using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class PhoneHeadsetRenderFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader compositeShader;
    private Material material;
    private HeadsetPass pass;

    public override void Create()
    {
        CoreUtils.Destroy(material);
        material = compositeShader != null ? CoreUtils.CreateEngineMaterial(compositeShader) : null;
        pass = material != null ? new HeadsetPass(material) : null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData.camera;
        if (pass != null && camera.TryGetComponent(out PhoneHeadsetAR headset) &&
            headset.IsActive && headset.Stereo.IsReady)
            renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

    private sealed class HeadsetPass : ScriptableRenderPass
    {
        private readonly Material material;
        private readonly MaterialPropertyBlock properties = new();
        private static readonly int BackgroundId = Shader.PropertyToID("_BlitTexture");
        private static readonly int LeftId = Shader.PropertyToID("_LeftEye");
        private static readonly int RightId = Shader.PropertyToID("_RightEye");
        private static readonly int SecondCursorId = Shader.PropertyToID("_HeadsetSecondCursor");
        private static readonly int CursorId = Shader.PropertyToID("_HeadsetCursor");
        private static readonly int AspectId = Shader.PropertyToID("_HeadsetAspect");
        private static readonly int ViewScaleId = Shader.PropertyToID("_HeadsetViewScale");
        private static readonly int ImageOffsetId = Shader.PropertyToID("_HeadsetImageOffset");
        private static readonly int ScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

        private sealed class CompositeData
        {
            public Material Material;
            public MaterialPropertyBlock Properties;
            public TextureHandle Background;
            public Texture Left, Right;
            public Vector4 Cursor, SecondCursor;
            public float Aspect;
            public float ViewScale;
            public float ImageOffset;
        }

        public HeadsetPass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;

            var headset = frameData.Get<UniversalCameraData>().camera.GetComponent<PhoneHeadsetAR>();
            var source = resources.activeColorTexture;
            var descriptor = graph.GetTextureDesc(source);
            descriptor.name = "Phone Headset Color";
            descriptor.clearBuffer = false;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            var destination = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<CompositeData>("Phone Headset Composite", out var data))
            {
                data.Material = material;
                data.Properties = properties;
                data.Background = source;
                var stereo = headset.Stereo;
                var eyeInfo = new RenderTargetInfo
                {
                    width = stereo.LeftTexture.width,
                    height = stereo.LeftTexture.height,
                    volumeDepth = 1,
                    msaaSamples = 1,
                    format = stereo.LeftTexture.graphicsFormat
                };
                data.Left = stereo.LeftTexture;
                data.Right = stereo.RightTexture;
                data.Cursor = headset.Cursor;
                data.SecondCursor = headset.SecondCursor;
                data.Aspect = (float)descriptor.width / descriptor.height;
                data.ViewScale = headset.ViewScale;
                data.ImageOffset = headset.ImageOffset;
                builder.UseTexture(data.Background);
                builder.UseTexture(graph.ImportTexture(stereo.Left, eyeInfo));
                builder.UseTexture(graph.ImportTexture(stereo.Right, eyeInfo));
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (CompositeData pass, RasterGraphContext context) =>
                {
                    var block = pass.Properties;
                    block.SetTexture(BackgroundId, pass.Background);
                    block.SetTexture(LeftId, pass.Left);
                    block.SetTexture(RightId, pass.Right);
                    block.SetVector(CursorId, pass.Cursor);
                    block.SetVector(SecondCursorId, pass.SecondCursor);
                    block.SetFloat(AspectId, pass.Aspect);
                    block.SetFloat(ViewScaleId, pass.ViewScale);
                    block.SetFloat(ImageOffsetId, pass.ImageOffset);
                    block.SetVector(ScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    context.cmd.DrawProcedural(Matrix4x4.identity, pass.Material, 0, MeshTopology.Triangles, 3, 1, block);
                });
            }
            resources.cameraColor = destination;
        }
    }
}
