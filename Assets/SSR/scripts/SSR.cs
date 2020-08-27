using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


[ExecuteInEditMode]
[ImageEffectAllowedInSceneView]
[RequireComponent(typeof(Camera))]
public class SSR : MonoBehaviour
{
    private enum RenderResolution
    {
        Full = 1,
        Half = 2
    };

    private enum DebugPass
    {
        Combine = 4,
        SSRColor = 5
    };

    private enum TraceApprox
    {
        HiZTrace = 0,
        LinearTrace = 1
    };


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    [Header("RayTrace Property")]
    [SerializeField]
    RenderResolution RayCastingResolution = RenderResolution.Full;


    [Range(1, 4)]
    [SerializeField]
    int RayNums = 1;


    [Range(0, 1)]
    [SerializeField]
    float BRDFBias = 0.7f;


    [Range(0.05f, 5)]
    [SerializeField]
    float Thickness = 0.1f;


    [Range(0, 0.5f)]
    [SerializeField]
    float ScreenFade = 0.1f;

    [Range(32, 512)]
    [SerializeField]
    int HiZ_RaySteps = 58;

    private int HiZ_MaxLevel = 10;
    private int HiZ_StartLevel = 1;
    private int HiZ_StopLevel = 0;

    [Header("Filtter Property")]

    [SerializeField]
    Texture2D BlueNoise_LUT = null;


    [SerializeField]
    Texture PreintegratedGF_LUT = null;


    [Range(1, 9)]
    [SerializeField]
    int SpatioSampler = 9;


    [Range(0, 0.99f)]
    [SerializeField]
    float TemporalWeight = 0.98f;

    [Range(1, 5)]
    [SerializeField]
    float TemporalScale = 1.25f;

    [Header("Debug Property")]
    [SerializeField]
    DebugPass DeBugPass = DebugPass.SSRColor;

    public bool enableTemporal = true;
    [Range(0, 2.0f)]
    public float _SSRIntensity = 1.0f;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static int RenderPass_HiZ_Depth = 0;
    private static int RenderPass_HiZ3D_MultiSpp = 1;
    private static int RenderPass_Spatiofilter_MultiSPP = 2;
    private static int RenderPass_Temporalfilter_MultiSpp = 3;

    private Camera RenderCamera;

    private CommandBuffer ScreenSpaceReflectionBuffer_ = null;

    private CommandBuffer ScreenSpaceReflectionBuffer
    {
        get
        {
            if (ScreenSpaceReflectionBuffer_ == null)
            {
                ScreenSpaceReflectionBuffer_ = new CommandBuffer();
                ScreenSpaceReflectionBuffer_.name = "StochasticScreenSpaceReflection";
            }
            return ScreenSpaceReflectionBuffer_;
        }
    }
    private Material StochasticScreenSpaceReflectionMaterial_ = null;
    private Material StochasticScreenSpaceReflectionMaterial
    {
        get
        {
            if (StochasticScreenSpaceReflectionMaterial_ == null) StochasticScreenSpaceReflectionMaterial_ = new Material(Shader.Find("Hidden/StochasticScreenSpaceReflection"));
            return StochasticScreenSpaceReflectionMaterial_;
        }
    }

    private Vector2 RandomSampler = new Vector2(0, 0);
    private Vector2 CameraSize;
    private Matrix4x4 SSR_ProjectionMatrix;
    private Matrix4x4 SSR_ViewProjectionMatrix;
    private Matrix4x4 SSR_Prev_ViewProjectionMatrix;
    private Matrix4x4 SSR_WorldToCameraMatrix;
    private Matrix4x4 SSR_CameraToWorldMatrix;

    private RenderTexture[] SSR_TraceMask_RT = new RenderTexture[2]; private RenderTargetIdentifier[] SSR_TraceMask_ID = new RenderTargetIdentifier[2];
    private RenderTexture SSR_Spatial_RT, SSR_TemporalPrev_RT, SSR_TemporalCurr_RT, SSR_CombineScene_RT, SSR_HierarchicalDepth_RT, SSR_HierarchicalDepth_BackUp_RT, SSR_SceneColor_RT;

    private static int SSR_Jitter_ID = Shader.PropertyToID("_SSR_Jitter");
    private static int SSR_BRDFBias_ID = Shader.PropertyToID("_SSR_BRDFBias");
    private static int SSR_NumSteps_Linear_ID = Shader.PropertyToID("_SSR_NumSteps_Linear");
    private static int SSR_NumSteps_HiZ_ID = Shader.PropertyToID("_SSR_NumSteps_HiZ");
    private static int SSR_NumRays_ID = Shader.PropertyToID("_SSR_NumRays");
    private static int SSR_NumResolver_ID = Shader.PropertyToID("_SSR_NumResolver");
    private static int SSR_ScreenFade_ID = Shader.PropertyToID("_SSR_ScreenFade");
    private static int SSR_Thickness_ID = Shader.PropertyToID("_SSR_Thickness");
    private static int SSR_TemporalScale_ID = Shader.PropertyToID("_SSR_TemporalScale");
    private static int SSR_TemporalWeight_ID = Shader.PropertyToID("_SSR_TemporalWeight");
    private static int SSR_ScreenSize_ID = Shader.PropertyToID("_SSR_ScreenSize");
    private static int SSR_RayCastSize_ID = Shader.PropertyToID("_SSR_RayCastSize");
    private static int SSR_NoiseSize_ID = Shader.PropertyToID("_SSR_NoiseSize");
    private static int SSR_RayStepSize_ID = Shader.PropertyToID("_SSR_RayStepSize");
    private static int SSR_ProjInfo_ID = Shader.PropertyToID("_SSR_ProjInfo");
    private static int SSR_CameraClipInfo_ID = Shader.PropertyToID("_SSR_CameraClipInfo");
    private static int SSR_TraceDistance_ID = Shader.PropertyToID("_SSR_TraceDistance");
    private static int SSR_BackwardsRay_ID = Shader.PropertyToID("_SSR_BackwardsRay");
    private static int SSR_TraceBehind_ID = Shader.PropertyToID("_SSR_TraceBehind");
    private static int SSR_CullBack_ID = Shader.PropertyToID("_SSR_CullBack");
    private static int SSR_HiZ_PrevDepthLevel_ID = Shader.PropertyToID("_SSR_HiZ_PrevDepthLevel");
    private static int SSR_HiZ_MaxLevel_ID = Shader.PropertyToID("_SSR_HiZ_MaxLevel");
    private static int SSR_HiZ_StartLevel_ID = Shader.PropertyToID("_SSR_HiZ_StartLevel");
    private static int SSR_HiZ_StopLevel_ID = Shader.PropertyToID("_SSR_HiZ_StopLevel");

    private static int SSR_Noise_ID = Shader.PropertyToID("_SSR_Noise");
    private static int SSR_PreintegratedGF_LUT_ID = Shader.PropertyToID("_SSR_PreintegratedGF_LUT");

    private static int SSR_HierarchicalDepth_ID = Shader.PropertyToID("_SSR_HierarchicalDepth_RT");
    private static int SSR_SceneColor_ID = Shader.PropertyToID("_SSR_SceneColor_RT");
    private static int SSR_CombineScene_ID = Shader.PropertyToID("_SSR_CombienReflection_RT");

    private static int SSR_Trace_ID = Shader.PropertyToID("_SSR_RayCastRT");
    private static int SSR_Mask_ID = Shader.PropertyToID("_SSR_RayMask_RT");
    private static int SSR_Spatial_ID = Shader.PropertyToID("_SSR_Spatial_RT");
    private static int SSR_TemporalPrev_ID = Shader.PropertyToID("_SSR_TemporalPrev_RT");
    private static int SSR_TemporalCurr_ID = Shader.PropertyToID("_SSR_TemporalCurr_RT");


    private static int SSR_ProjectionMatrix_ID = Shader.PropertyToID("_SSR_ProjectionMatrix");
    private static int SSR_ViewProjectionMatrix_ID = Shader.PropertyToID("_SSR_ViewProjectionMatrix");
    private static int SSR_LastFrameViewProjectionMatrix_ID = Shader.PropertyToID("_SSR_LastFrameViewProjectionMatrix");
    private static int SSR_InverseProjectionMatrix_ID = Shader.PropertyToID("_SSR_InverseProjectionMatrix");
    private static int SSR_InverseViewProjectionMatrix_ID = Shader.PropertyToID("_SSR_InverseViewProjectionMatrix");
    private static int SSR_WorldToCameraMatrix_ID = Shader.PropertyToID("_SSR_WorldToCameraMatrix");
    private static int SSR_CameraToWorldMatrix_ID = Shader.PropertyToID("_SSR_CameraToWorldMatrix");
    private static int SSR_ProjectToPixelMatrix_ID = Shader.PropertyToID("_SSR_ProjectToPixelMatrix");
    private static int SSR_Intensity_ID = Shader.PropertyToID("_SSRIntensity");

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private int m_SampleIndex = 0;
    private const int k_SampleCount = 64;
    private float GetHaltonValue(int index, int radix)
    {
        float result = 0f;
        float fraction = 1f / radix;

        while (index > 0)
        {
            result += (index % radix) * fraction;
            index /= radix;
            fraction /= radix;
        }
        return result;
    }
    private Vector2 GenerateRandomOffset()
    {
        var offset = new Vector2(GetHaltonValue(m_SampleIndex & 1023, 2), GetHaltonValue(m_SampleIndex & 1023, 3));
        if (m_SampleIndex++ >= k_SampleCount)
            m_SampleIndex = 0;
        return offset;
    }

    public void initResources()
    {
        Vector2 HalfCameraSize = new Vector2(CameraSize.x / 2, CameraSize.y / 2);

        //////////// RT creation
        {
            RenderTexture.ReleaseTemporary(SSR_HierarchicalDepth_RT);
            SSR_HierarchicalDepth_RT = RenderTexture.GetTemporary(RenderCamera.pixelWidth, RenderCamera.pixelHeight, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
            SSR_HierarchicalDepth_RT.filterMode = FilterMode.Point;
            SSR_HierarchicalDepth_RT.useMipMap = true;
            SSR_HierarchicalDepth_RT.autoGenerateMips = true;
            SSR_HierarchicalDepth_RT.name = "Hi-z buffer";

            RenderTexture.ReleaseTemporary(SSR_HierarchicalDepth_BackUp_RT);
            SSR_HierarchicalDepth_BackUp_RT = RenderTexture.GetTemporary(RenderCamera.pixelWidth, RenderCamera.pixelHeight, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
            SSR_HierarchicalDepth_BackUp_RT.filterMode = FilterMode.Point;
            SSR_HierarchicalDepth_BackUp_RT.useMipMap = true;
            SSR_HierarchicalDepth_BackUp_RT.autoGenerateMips = false;
            SSR_HierarchicalDepth_BackUp_RT.name = "tmp buffer for hi-z";

            RenderTexture.ReleaseTemporary(SSR_SceneColor_RT);
            SSR_SceneColor_RT = RenderTexture.GetTemporary(RenderCamera.pixelWidth, RenderCamera.pixelHeight, 0, RenderTextureFormat.DefaultHDR);

            /////////////RayMarching and RayMask RT
            RenderTexture.ReleaseTemporary(SSR_TraceMask_RT[0]);
            SSR_TraceMask_RT[0] = RenderTexture.GetTemporary(RenderCamera.pixelWidth / (int)RayCastingResolution, RenderCamera.pixelHeight / (int)RayCastingResolution, 0, RenderTextureFormat.ARGBHalf);
            SSR_TraceMask_RT[0].filterMode = FilterMode.Point;
            SSR_TraceMask_RT[0].name = "raytrace pos and pdf";


            RenderTexture.ReleaseTemporary(SSR_TraceMask_RT[1]);
            SSR_TraceMask_RT[1] = RenderTexture.GetTemporary(RenderCamera.pixelWidth / (int)RayCastingResolution, RenderCamera.pixelHeight / (int)RayCastingResolution, 0, RenderTextureFormat.ARGBHalf);
            SSR_TraceMask_RT[1].filterMode = FilterMode.Point;
            SSR_TraceMask_RT[1].name = "raytrace mask";

            SSR_TraceMask_ID[0] = SSR_TraceMask_RT[0].colorBuffer;
            SSR_TraceMask_ID[1] = SSR_TraceMask_RT[1].colorBuffer;

            ////////////Spatial RT
            RenderTexture.ReleaseTemporary(SSR_Spatial_RT);
            SSR_Spatial_RT = RenderTexture.GetTemporary(RenderCamera.pixelWidth, RenderCamera.pixelHeight, 0, RenderTextureFormat.ARGBHalf);
            SSR_Spatial_RT.filterMode = FilterMode.Bilinear;
            SSR_Spatial_RT.name = "resolved rt";

            //Temporal RT
            RenderTexture.ReleaseTemporary(SSR_TemporalPrev_RT);
            SSR_TemporalPrev_RT = RenderTexture.GetTemporary(RenderCamera.pixelWidth, RenderCamera.pixelHeight, 0, RenderTextureFormat.ARGBHalf);
            SSR_TemporalPrev_RT.filterMode = FilterMode.Bilinear;
            SSR_TemporalPrev_RT.name = "temporal prev rt";

            RenderTexture.ReleaseTemporary(SSR_TemporalCurr_RT);
            SSR_TemporalCurr_RT = RenderTexture.GetTemporary(RenderCamera.pixelWidth, RenderCamera.pixelHeight, 0, RenderTextureFormat.ARGBHalf);
            SSR_TemporalCurr_RT.filterMode = FilterMode.Bilinear;
            SSR_TemporalCurr_RT.name = "temporal rt";

            // combine RT
            RenderTexture.ReleaseTemporary(SSR_CombineScene_RT);
            SSR_CombineScene_RT = RenderTexture.GetTemporary(RenderCamera.pixelWidth, RenderCamera.pixelHeight, 0, RenderTextureFormat.DefaultHDR);
            SSR_CombineScene_RT.filterMode = FilterMode.Point;
            SSR_CombineScene_RT.name = "combine rt";
        }
    }

    public void UpdateUniform()
    {
        if(enableTemporal)
            RandomSampler = GenerateRandomOffset();

        Vector2 HalfCameraSize = new Vector2(CameraSize.x / 2, CameraSize.y / 2);
        Vector2 CurrentCameraSize = new Vector2(RenderCamera.pixelWidth, RenderCamera.pixelHeight);
        CameraSize = CurrentCameraSize;

        StochasticScreenSpaceReflectionMaterial.SetVector(SSR_Jitter_ID, new Vector4((float)CameraSize.x / 1024, (float)CameraSize.y / 1024, RandomSampler.x, RandomSampler.y));
        SSR_WorldToCameraMatrix = RenderCamera.worldToCameraMatrix;
        SSR_CameraToWorldMatrix = SSR_WorldToCameraMatrix.inverse;
        SSR_ProjectionMatrix = GL.GetGPUProjectionMatrix(RenderCamera.projectionMatrix, false);
        SSR_ViewProjectionMatrix = SSR_ProjectionMatrix * SSR_WorldToCameraMatrix;
        StochasticScreenSpaceReflectionMaterial.SetMatrix(SSR_ProjectionMatrix_ID, SSR_ProjectionMatrix);
        StochasticScreenSpaceReflectionMaterial.SetMatrix(SSR_ViewProjectionMatrix_ID, SSR_ViewProjectionMatrix);
        StochasticScreenSpaceReflectionMaterial.SetMatrix(SSR_InverseProjectionMatrix_ID, SSR_ProjectionMatrix.inverse);
        StochasticScreenSpaceReflectionMaterial.SetMatrix(SSR_InverseViewProjectionMatrix_ID, SSR_ViewProjectionMatrix.inverse);
        StochasticScreenSpaceReflectionMaterial.SetMatrix(SSR_WorldToCameraMatrix_ID, SSR_WorldToCameraMatrix);
        StochasticScreenSpaceReflectionMaterial.SetMatrix(SSR_CameraToWorldMatrix_ID, SSR_CameraToWorldMatrix);
        StochasticScreenSpaceReflectionMaterial.SetMatrix(SSR_LastFrameViewProjectionMatrix_ID, SSR_Prev_ViewProjectionMatrix);

        Matrix4x4 warpToScreenSpaceMatrix = Matrix4x4.identity;
        warpToScreenSpaceMatrix.m00 = HalfCameraSize.x; warpToScreenSpaceMatrix.m03 = HalfCameraSize.x;
        warpToScreenSpaceMatrix.m11 = HalfCameraSize.y; warpToScreenSpaceMatrix.m13 = HalfCameraSize.y;

        Matrix4x4 SSR_ProjectToPixelMatrix = warpToScreenSpaceMatrix * SSR_ProjectionMatrix;
        StochasticScreenSpaceReflectionMaterial.SetMatrix(SSR_ProjectToPixelMatrix_ID, SSR_ProjectToPixelMatrix);

        Vector4 SSR_ProjInfo = new Vector4
                ((-2 / (CameraSize.x * SSR_ProjectionMatrix[0])),
                (-2 / (CameraSize.y * SSR_ProjectionMatrix[5])),
                ((1 - SSR_ProjectionMatrix[2]) / SSR_ProjectionMatrix[0]),
                ((1 + SSR_ProjectionMatrix[6]) / SSR_ProjectionMatrix[5]));
        StochasticScreenSpaceReflectionMaterial.SetVector(SSR_ProjInfo_ID, SSR_ProjInfo);

        Vector3 SSR_ClipInfo = (float.IsPositiveInfinity(RenderCamera.farClipPlane)) ?
                new Vector3(RenderCamera.nearClipPlane, -1, 1) :
                new Vector3(RenderCamera.nearClipPlane * RenderCamera.farClipPlane, RenderCamera.nearClipPlane - RenderCamera.farClipPlane, RenderCamera.farClipPlane);
        StochasticScreenSpaceReflectionMaterial.SetVector(SSR_CameraClipInfo_ID, SSR_ClipInfo);


        StochasticScreenSpaceReflectionMaterial.SetTexture(SSR_PreintegratedGF_LUT_ID, PreintegratedGF_LUT);
        StochasticScreenSpaceReflectionMaterial.SetTexture(SSR_Noise_ID, BlueNoise_LUT);
        StochasticScreenSpaceReflectionMaterial.SetVector(SSR_ScreenSize_ID, CameraSize);
        StochasticScreenSpaceReflectionMaterial.SetVector(SSR_RayCastSize_ID, CameraSize / (int)RayCastingResolution);
        StochasticScreenSpaceReflectionMaterial.SetVector(SSR_NoiseSize_ID, new Vector2(1024, 1024));
        StochasticScreenSpaceReflectionMaterial.SetFloat(SSR_BRDFBias_ID, BRDFBias);
        StochasticScreenSpaceReflectionMaterial.SetFloat(SSR_ScreenFade_ID, ScreenFade);
        StochasticScreenSpaceReflectionMaterial.SetFloat(SSR_Thickness_ID, Thickness);
        StochasticScreenSpaceReflectionMaterial.SetInt(SSR_NumSteps_HiZ_ID, HiZ_RaySteps);
        StochasticScreenSpaceReflectionMaterial.SetInt(SSR_NumRays_ID, RayNums);
        StochasticScreenSpaceReflectionMaterial.SetInt(SSR_HiZ_MaxLevel_ID, HiZ_MaxLevel);
        StochasticScreenSpaceReflectionMaterial.SetInt(SSR_HiZ_StartLevel_ID, HiZ_StartLevel);
        StochasticScreenSpaceReflectionMaterial.SetInt(SSR_HiZ_StopLevel_ID, HiZ_StopLevel);

        StochasticScreenSpaceReflectionMaterial.SetInt(SSR_NumResolver_ID, SpatioSampler);
        StochasticScreenSpaceReflectionMaterial.SetFloat(SSR_TemporalScale_ID, TemporalScale);
        StochasticScreenSpaceReflectionMaterial.SetFloat(SSR_TemporalWeight_ID, TemporalWeight);
        StochasticScreenSpaceReflectionMaterial.SetFloat(SSR_Intensity_ID, _SSRIntensity);
    }

    private void OnEnable()
    {
        RenderCamera = gameObject.GetComponent<Camera>();
        if (ScreenSpaceReflectionBuffer != null)
            RenderCamera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, ScreenSpaceReflectionBuffer);
        initResources();
    }

    void OnDisable()
    {
        if (ScreenSpaceReflectionBuffer != null)
            RenderCamera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, ScreenSpaceReflectionBuffer);
    }

    void OnDestroy()
    {
        ReleaseSSRBuffer();
    }

    private void OnPreRender()
    {
        UpdateUniform();
        RenderSSR();
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Graphics.Blit(source, destination);
    }

    private void RenderSSR()
    {
        ScreenSpaceReflectionBuffer.Clear();

        // Generate Hi-z buffer
        // 这一步已经产生mipmap了么？
        ScreenSpaceReflectionBuffer.Blit(BuiltinRenderTextureType.ResolvedDepth, SSR_HierarchicalDepth_RT);
        for (int i = 1; i < HiZ_MaxLevel; ++i)
        {
            ScreenSpaceReflectionBuffer.SetGlobalInt(SSR_HiZ_PrevDepthLevel_ID, i - 1);
            ScreenSpaceReflectionBuffer.SetRenderTarget(SSR_HierarchicalDepth_BackUp_RT, i);
            ScreenSpaceReflectionBuffer.DrawMesh(GraphicsUtility.mesh, Matrix4x4.identity, StochasticScreenSpaceReflectionMaterial, 0, RenderPass_HiZ_Depth);
            ScreenSpaceReflectionBuffer.CopyTexture(SSR_HierarchicalDepth_BackUp_RT, 0, i, SSR_HierarchicalDepth_RT, 0, i);
        }
        ScreenSpaceReflectionBuffer.SetGlobalTexture(SSR_HierarchicalDepth_ID, SSR_HierarchicalDepth_RT);

        //// 准备开始光追
        ScreenSpaceReflectionBuffer.SetGlobalTexture(SSR_SceneColor_ID, SSR_SceneColor_RT);
        ScreenSpaceReflectionBuffer.CopyTexture(BuiltinRenderTextureType.CameraTarget, SSR_SceneColor_RT);

        ScreenSpaceReflectionBuffer.SetGlobalTexture(SSR_Trace_ID, SSR_TraceMask_RT[0]);
        ScreenSpaceReflectionBuffer.SetGlobalTexture(SSR_Mask_ID, SSR_TraceMask_RT[1]);
        ScreenSpaceReflectionBuffer.BlitMRT(SSR_TraceMask_ID, SSR_TraceMask_RT[0], StochasticScreenSpaceReflectionMaterial, RenderPass_HiZ3D_MultiSpp);

        ////// 对多条光线进行resolved
        ScreenSpaceReflectionBuffer.SetGlobalTexture(SSR_Spatial_ID, SSR_Spatial_RT);
        ScreenSpaceReflectionBuffer.BlitSRT(SSR_Spatial_RT, StochasticScreenSpaceReflectionMaterial, RenderPass_Spatiofilter_MultiSPP);

        if (enableTemporal)
        {
            ScreenSpaceReflectionBuffer.SetGlobalTexture(SSR_TemporalPrev_ID, SSR_TemporalPrev_RT);
            ScreenSpaceReflectionBuffer.SetGlobalTexture(SSR_TemporalCurr_ID, SSR_TemporalCurr_RT);
            ScreenSpaceReflectionBuffer.BlitSRT(SSR_TemporalCurr_RT, StochasticScreenSpaceReflectionMaterial, RenderPass_Temporalfilter_MultiSpp);
            ScreenSpaceReflectionBuffer.CopyTexture(SSR_TemporalCurr_RT, SSR_TemporalPrev_RT);
        }
        else
        {
            ScreenSpaceReflectionBuffer.CopyTexture(SSR_Spatial_RT, SSR_TemporalCurr_RT);
            ScreenSpaceReflectionBuffer.SetGlobalTexture(SSR_TemporalCurr_ID, SSR_TemporalCurr_RT);
        }

        //////////// 得到数据后，直接做combine
#if UNITY_EDITOR
        ScreenSpaceReflectionBuffer.BlitSRT(SSR_CombineScene_RT, BuiltinRenderTextureType.CameraTarget, StochasticScreenSpaceReflectionMaterial, (int)DeBugPass);
#else
        ScreenSpaceReflectionBuffer.BlitSRT(SSR_CombineScene_RT, BuiltinRenderTextureType.CameraTarget, StochasticScreenSpaceReflectionMaterial, 4);
#endif

        //////Set Last Frame ViewProjection//////
        SSR_Prev_ViewProjectionMatrix = SSR_ViewProjectionMatrix;
    }

    private void ReleaseSSRBuffer()
    {
        RenderTexture.ReleaseTemporary(SSR_HierarchicalDepth_RT);
        RenderTexture.ReleaseTemporary(SSR_SceneColor_RT);
        RenderTexture.ReleaseTemporary(SSR_TraceMask_RT[0]);
        RenderTexture.ReleaseTemporary(SSR_TraceMask_RT[1]);
        RenderTexture.ReleaseTemporary(SSR_Spatial_RT);
        RenderTexture.ReleaseTemporary(SSR_TemporalPrev_RT);
        RenderTexture.ReleaseTemporary(SSR_TemporalCurr_RT);
        RenderTexture.ReleaseTemporary(SSR_CombineScene_RT);

        if (ScreenSpaceReflectionBuffer != null)
        {
            ScreenSpaceReflectionBuffer.Dispose();
        }
    }

}
