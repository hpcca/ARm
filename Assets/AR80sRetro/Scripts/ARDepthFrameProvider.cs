using System;
using System.Collections.Generic;
using AR80sRetro.Experiments;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace AR80sRetro
{
    public sealed class ARDepthFrameProvider : MonoBehaviour
    {
        [SerializeField] private AROcclusionManager occlusionManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private EnvironmentDepthMode requestedDepthMode = EnvironmentDepthMode.Fastest;
        [SerializeField] private bool requestTemporalSmoothing = true;
        [SerializeField, Range(0f, 0.2f)] private float sampleRadiusNormalized = 0.025f;
        [SerializeField, Range(0f, 1f)] private float minDepthMeters = 0.15f;
        [SerializeField, Range(1f, 20f)] private float maxDepthMeters = 8f;
        [SerializeField, Range(0, 255)] private int minConfidence = 1;
        [SerializeField, Min(0f)] private float depthAvailabilityGraceSeconds = 0.5f;
        [SerializeField] private bool flipDepthX;
        [SerializeField] private bool flipDepthY = true;
        [SerializeField] private bool logDepthAvailability;

        [Header("Experiment")]
        [SerializeField] private ARReplacementExperimentConfig experimentConfig;

        private readonly List<float> depthSamples = new List<float>(25);
        private int lastAvailabilityLogFrame = -120;
        private float lastDepthTextureTime = float.NegativeInfinity;

        public bool IsDepthActive => IsEnvironmentDepthUsable;

        public bool IsEnvironmentDepthSupported => occlusionManager != null
            && occlusionManager.descriptor != null
            && occlusionManager.descriptor.environmentDepthImageSupported == Supported.Supported;

        public bool IsEnvironmentDepthUsable { get; private set; }
        public EnvironmentDepthMode ConfiguredDepthMode => requestedDepthMode;
        public bool TemporalSmoothingRequested => requestTemporalSmoothing;
        public int SampleGridSize => sampleRadiusNormalized > 0f ? 5 : 1;
        public float SampleRadiusNormalized => sampleRadiusNormalized;
        public float MinimumDepthMeters => minDepthMeters;
        public float MaximumDepthMeters => maxDepthMeters;
        public int MinimumConfidence => minConfidence;
        public float DepthAvailabilityGraceSeconds => depthAvailabilityGraceSeconds;

        private void Awake()
        {
            if (arCamera == null)
            {
                arCamera = Camera.main;
            }

            if (occlusionManager == null && arCamera != null)
            {
                occlusionManager = arCamera.GetComponent<AROcclusionManager>();
            }

            if (occlusionManager != null
                && arCamera != null
                && occlusionManager.gameObject != arCamera.gameObject)
            {
                Debug.LogWarning(
                    "AROcclusionManager must be on the same GameObject as the AR Camera and ARCameraBackground for render occlusion.",
                    this);
            }
        }

        private void Reset()
        {
            arCamera = Camera.main;
            occlusionManager = arCamera != null
                ? arCamera.GetComponent<AROcclusionManager>()
                : FindObjectOfType<AROcclusionManager>();
        }

        private void OnEnable()
        {
            ApplyDepthSettings();
            UpdateDepthAvailability();
        }

        private void Update()
        {
            ApplyDepthSettings();
            UpdateDepthAvailability();
        }

        public bool TrySampleWorldPoint(
            DetectionResult detection,
            Vector2 normalizedAnchorInBox,
            out Vector3 worldPoint,
            out float depthMeters,
            out float confidence)
        {
            return TrySampleWorldPoint(
                detection,
                normalizedAnchorInBox,
                out worldPoint,
                out depthMeters,
                out confidence,
                out _);
        }

        public bool TrySampleWorldPoint(
            DetectionResult detection,
            Vector2 normalizedAnchorInBox,
            out Vector3 worldPoint,
            out float depthMeters,
            out float confidence,
            out DepthSampleDiagnostics diagnostics)
        {
            long startTimestamp = ExperimentClock.Timestamp();
            worldPoint = default;
            depthMeters = 0f;
            confidence = 0f;

            if (occlusionManager == null || arCamera == null)
            {
                diagnostics = new DepthSampleDiagnostics(
                    true,
                    false,
                    false,
                    0,
                    0f,
                    0f,
                    ExperimentClock.ElapsedMilliseconds(startTimestamp));
                return false;
            }

            Vector2 screenPoint = detection.ToScreenPoint(Screen.width, Screen.height, normalizedAnchorInBox);
            if (!TrySampleDepthMeters(
                    detection,
                    normalizedAnchorInBox,
                    out depthMeters,
                    out confidence,
                    out int validSampleCount,
                    out bool confidenceAvailable))
            {
                MaybeLogDepthUnavailable();
                diagnostics = new DepthSampleDiagnostics(
                    true,
                    false,
                    confidenceAvailable,
                    validSampleCount,
                    0f,
                    0f,
                    ExperimentClock.ElapsedMilliseconds(startTimestamp));
                return false;
            }

            Vector3 viewportPoint = new Vector3(
                Mathf.Clamp01(screenPoint.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(screenPoint.y / Mathf.Max(1f, Screen.height)),
                depthMeters);
            worldPoint = arCamera.ViewportToWorldPoint(viewportPoint);
            diagnostics = new DepthSampleDiagnostics(
                true,
                true,
                confidenceAvailable,
                validSampleCount,
                depthMeters,
                confidence,
                ExperimentClock.ElapsedMilliseconds(startTimestamp));
            return true;
        }

        private void ApplyDepthSettings()
        {
            if (occlusionManager == null)
            {
                return;
            }

            bool requiresEnvironmentDepth = experimentConfig == null
                || experimentConfig.DepthPositionFusionEnabled
                || experimentConfig.OcclusionFadeFallbackEnabled;
            EnvironmentDepthMode effectiveDepthMode = requiresEnvironmentDepth
                ? requestedDepthMode
                : EnvironmentDepthMode.Disabled;
            if (occlusionManager.requestedEnvironmentDepthMode != effectiveDepthMode)
            {
                occlusionManager.requestedEnvironmentDepthMode = effectiveDepthMode;
            }

            if (occlusionManager.environmentDepthTemporalSmoothingRequested != requestTemporalSmoothing)
            {
                occlusionManager.environmentDepthTemporalSmoothingRequested = requestTemporalSmoothing;
            }

            OcclusionPreferenceMode effectiveOcclusionMode = experimentConfig == null
                || experimentConfig.OcclusionFadeFallbackEnabled
                ? OcclusionPreferenceMode.PreferEnvironmentOcclusion
                : OcclusionPreferenceMode.NoOcclusion;
            if (occlusionManager.requestedOcclusionPreferenceMode != effectiveOcclusionMode)
            {
                occlusionManager.requestedOcclusionPreferenceMode = effectiveOcclusionMode;
            }
        }

        private void UpdateDepthAvailability()
        {
            bool subsystemCanProvideDepth = IsEnvironmentDepthSupported
                && occlusionManager.enabled
                && occlusionManager.gameObject.activeInHierarchy
                && occlusionManager.currentEnvironmentDepthMode != EnvironmentDepthMode.Disabled;
            bool hasCurrentTexture = subsystemCanProvideDepth
                && occlusionManager.environmentDepthTexture != null;
            if (hasCurrentTexture)
            {
                lastDepthTextureTime = Time.unscaledTime;
            }

            IsEnvironmentDepthUsable = hasCurrentTexture
                || (subsystemCanProvideDepth
                    && Time.unscaledTime - lastDepthTextureTime <= depthAvailabilityGraceSeconds);
        }

        private bool TrySampleDepthMeters(
            DetectionResult detection,
            Vector2 normalizedAnchorInBox,
            out float medianDepth,
            out float medianConfidence,
            out int validSampleCount,
            out bool confidenceAvailable)
        {
            medianDepth = 0f;
            medianConfidence = 0f;
            validSampleCount = 0;
            confidenceAvailable = false;
            depthSamples.Clear();

            if (!occlusionManager.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage depthImage))
            {
                return false;
            }

            bool hasConfidenceImage = occlusionManager.TryAcquireEnvironmentDepthConfidenceCpuImage(
                out XRCpuImage confidenceImage);
            confidenceAvailable = hasConfidenceImage;

            try
            {
                int gridRadius = sampleRadiusNormalized > 0f ? 2 : 0;
                float confidenceSum = 0f;
                int confidenceCount = 0;

                for (int y = -gridRadius; y <= gridRadius; y++)
                {
                    for (int x = -gridRadius; x <= gridRadius; x++)
                    {
                        Vector2 normalizedPoint = new Vector2(
                            detection.NormalizedBox.x + detection.NormalizedBox.width * normalizedAnchorInBox.x,
                            detection.NormalizedBox.y + detection.NormalizedBox.height * normalizedAnchorInBox.y);
                        normalizedPoint.x += x * sampleRadiusNormalized;
                        normalizedPoint.y += y * sampleRadiusNormalized;
                        normalizedPoint.x = Mathf.Clamp01(normalizedPoint.x);
                        normalizedPoint.y = Mathf.Clamp01(normalizedPoint.y);

                        int depthX = NormalizedToImageX(normalizedPoint.x, depthImage.width);
                        int depthY = NormalizedToImageY(normalizedPoint.y, depthImage.height);
                        if (!TryReadDepth(depthImage, depthX, depthY, out float depth)
                            || depth < minDepthMeters
                            || depth > maxDepthMeters)
                        {
                            continue;
                        }

                        float confidenceValue = 255f;
                        if (hasConfidenceImage)
                        {
                            int confidenceX = NormalizedToImageX(normalizedPoint.x, confidenceImage.width);
                            int confidenceY = NormalizedToImageY(normalizedPoint.y, confidenceImage.height);
                            if (!TryReadConfidence(confidenceImage, confidenceX, confidenceY, out confidenceValue)
                                || confidenceValue < minConfidence)
                            {
                                continue;
                            }
                        }

                        depthSamples.Add(depth);
                        confidenceSum += confidenceValue;
                        confidenceCount++;
                    }
                }

                if (depthSamples.Count == 0)
                {
                    return false;
                }

                depthSamples.Sort();
                validSampleCount = depthSamples.Count;
                medianDepth = depthSamples[depthSamples.Count / 2];
                medianConfidence = confidenceCount > 0 ? confidenceSum / confidenceCount : 0f;
                return true;
            }
            finally
            {
                if (hasConfidenceImage)
                {
                    confidenceImage.Dispose();
                }

                depthImage.Dispose();
            }
        }

        private int NormalizedToImageX(float normalizedX, int width)
        {
            float value = flipDepthX ? 1f - normalizedX : normalizedX;
            return Mathf.Clamp(Mathf.RoundToInt(value * (width - 1)), 0, width - 1);
        }

        private int NormalizedToImageY(float normalizedY, int height)
        {
            float value = flipDepthY ? normalizedY : 1f - normalizedY;
            return Mathf.Clamp(Mathf.RoundToInt(value * (height - 1)), 0, height - 1);
        }

        private static bool TryReadDepth(XRCpuImage image, int x, int y, out float depth)
        {
            depth = 0f;
            if (image.planeCount <= 0)
            {
                return false;
            }

            XRCpuImage.Plane plane = image.GetPlane(0);
            int offset = y * plane.rowStride + x * plane.pixelStride;
            NativeArray<byte> data = plane.data;

            switch (image.format)
            {
                case XRCpuImage.Format.DepthFloat32:
                case XRCpuImage.Format.OneComponent32:
                    if (offset + 3 >= data.Length)
                    {
                        return false;
                    }

                    depth = BitConverter.ToSingle(new[]
                    {
                        data[offset],
                        data[offset + 1],
                        data[offset + 2],
                        data[offset + 3]
                    }, 0);
                    return !float.IsNaN(depth) && !float.IsInfinity(depth);

                case XRCpuImage.Format.DepthUint16:
                    if (offset + 1 >= data.Length)
                    {
                        return false;
                    }

                    ushort millimeters = (ushort)(data[offset] | (data[offset + 1] << 8));
                    depth = millimeters * 0.001f;
                    return millimeters > 0;

                default:
                    return false;
            }
        }

        private static bool TryReadConfidence(XRCpuImage image, int x, int y, out float confidence)
        {
            confidence = 0f;
            if (image.planeCount <= 0)
            {
                return false;
            }

            XRCpuImage.Plane plane = image.GetPlane(0);
            int offset = y * plane.rowStride + x * plane.pixelStride;
            NativeArray<byte> data = plane.data;
            if (offset < 0 || offset >= data.Length)
            {
                return false;
            }

            confidence = data[offset];
            return true;
        }

        private void MaybeLogDepthUnavailable()
        {
            if (!logDepthAvailability || Time.frameCount - lastAvailabilityLogFrame < 120)
            {
                return;
            }

            lastAvailabilityLogFrame = Time.frameCount;
            Debug.Log(
                $"Depth unavailable. requested={occlusionManager.requestedEnvironmentDepthMode}, current={occlusionManager.currentEnvironmentDepthMode}",
                this);
        }
    }
}
