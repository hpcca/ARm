using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AR80sRetro
{
    public sealed class RetroReplacementManager : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int SrcBlendAlphaId = Shader.PropertyToID("_SrcBlendAlpha");
        private static readonly int DstBlendAlphaId = Shader.PropertyToID("_DstBlendAlpha");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        private enum ReplacementState
        {
            Searching,
            Acquiring,
            Locked,
            TrackingMove,
            Lost
        }

        private sealed class TrackedReplacement
        {
            public string Label;
            public GameObject Instance;
            public Pose LastPose;
            public Pose PendingMovePose;
            public Vector3 LockedScale;
            public Quaternion LockedRotation;
            public Vector3 MoveVelocity;
            public int ConfirmedFrames;
            public int PendingMoveFrames;
            public float LastSeenTime;
            public float LastObservationConfidence = 1f;
            public float TrackingConfidenceThreshold;
            public float CurrentOpacity = 1f;
            public bool HasMoveTarget;
            public int LastMatchedFrame = -1;
            public ReplacementState State = ReplacementState.Searching;
            public List<RuntimeMaterialState> FadeMaterials;
        }

        private sealed class RuntimeMaterialState
        {
            public Material Material;
            public bool HasBaseColor;
            public Color BaseColor;
            public bool HasColor;
            public Color Color;
            public bool HasSurface;
            public float Surface;
            public bool HasBlend;
            public float Blend;
            public bool HasSrcBlend;
            public float SrcBlend;
            public bool HasDstBlend;
            public float DstBlend;
            public bool HasSrcBlendAlpha;
            public float SrcBlendAlpha;
            public bool HasDstBlendAlpha;
            public float DstBlendAlpha;
            public bool HasZWrite;
            public float ZWrite;
            public int RenderQueue;
            public string RenderType;
            public bool SurfaceTransparentKeyword;
            public bool AlphaPremultiplyKeyword;
            public bool ShadowCasterPassEnabled;
        }

        [SerializeField] private RetroPrefabLibrary prefabLibrary;
        [SerializeField] private ARRaycastPositionSolver positionSolver;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private Camera arCamera;
        [SerializeField] private bool destroyWhenLost;
        [SerializeField] private float lostGraceSeconds = 1f;
        [SerializeField, Min(0f)] private float lostStateDelaySeconds = 1f;
        [SerializeField, Min(0.05f)] private float reacquireMatchRadiusMeters = 0.4f;
        [SerializeField] private float duplicateRadiusMeters = 0.25f;
        [SerializeField, Min(0.005f)] private float movementDeadZoneMeters = 0.05f;
        [SerializeField, Min(1)] private int movementConfirmationFrames = 3;
        [SerializeField, Min(0.01f)] private float movementSmoothTime = 0.25f;

        [Header("Depth Occlusion Fallback")]
        [SerializeField] private ARDepthFrameProvider depthProvider;
        [SerializeField] private bool fadeWhenDepthUnavailable = true;
        [SerializeField, Min(0f)] private float depthStartupGraceSeconds = 2f;
        [SerializeField, Min(0f)] private float fallbackFadeDelaySeconds = 0.35f;
        [SerializeField, Min(0.05f)] private float fallbackFadeDurationSeconds = 0.35f;
        [SerializeField, Range(0f, 1f)] private float fallbackMinimumOpacity = 0.2f;

        private readonly List<TrackedReplacement> trackedReplacements = new List<TrackedReplacement>();

        private void Awake()
        {
            if (arCamera == null)
            {
                arCamera = Camera.main;
            }

            if (depthProvider == null)
            {
                depthProvider = FindObjectOfType<ARDepthFrameProvider>();
            }
        }

        private void Reset()
        {
            positionSolver = FindObjectOfType<ARRaycastPositionSolver>();
            depthProvider = FindObjectOfType<ARDepthFrameProvider>();
            arCamera = Camera.main;
            contentRoot = transform;
        }

        private void Update()
        {
            UpdateLostStates();
            SmoothActiveMoves();
            UpdateFallbackVisibility();
            RemoveExpiredReplacements();
        }

        public void ApplyDetections(IReadOnlyList<DetectionResult> detections)
        {
            if (detections == null || prefabLibrary == null || positionSolver == null)
            {
                return;
            }

            float now = Time.time;
            for (int i = 0; i < detections.Count; i++)
            {
                DetectionResult detection = detections[i];
                if (!prefabLibrary.TryGetRule(detection.Label, out RetroReplacementRule rule))
                {
                    continue;
                }

                string key = rule.DetectionLabel.ToLowerInvariant();
                if (!detection.IsValid(0f))
                {
                    continue;
                }

                if (!positionSolver.TrySolvePose(detection, rule.RaycastAnchorInBoundingBox, out Pose pose))
                {
                    continue;
                }

                pose.position += Vector3.up * rule.VerticalOffsetMeters;
                pose.rotation *= rule.RotationOffset;

                TrackedReplacement tracked = FindBestTrack(key, pose, Time.frameCount);
                bool hasLockedInstance = tracked != null && tracked.Instance != null;
                float requiredConfidence = hasLockedInstance
                    ? rule.TrackingMinConfidence
                    : rule.MinConfidence;
                if (!detection.IsValid(requiredConfidence))
                {
                    if (hasLockedInstance)
                    {
                        tracked.LastMatchedFrame = Time.frameCount;
                        tracked.LastObservationConfidence = detection.Confidence;
                        tracked.TrackingConfidenceThreshold = rule.TrackingMinConfidence;
                    }

                    continue;
                }

                if (tracked == null)
                {
                    tracked = new TrackedReplacement
                    {
                        Label = key
                    };
                    trackedReplacements.Add(tracked);
                }

                tracked.LastMatchedFrame = Time.frameCount;
                tracked.LastObservationConfidence = detection.Confidence;
                tracked.TrackingConfidenceThreshold = rule.TrackingMinConfidence;
                UpdateReplacement(rule, detection, pose, now, tracked);
            }
        }

        private void UpdateReplacement(
            RetroReplacementRule rule,
            DetectionResult detection,
            Pose pose,
            float now,
            TrackedReplacement tracked)
        {
            if (tracked.Instance != null
                && tracked.State == ReplacementState.Lost
                && !IsWithinReacquireRadius(tracked, pose))
            {
                return;
            }

            tracked.LastSeenTime = now;

            if (tracked.Instance == null)
            {
                tracked.State = ReplacementState.Acquiring;

                if (tracked.ConfirmedFrames > 0
                    && Vector3.Distance(tracked.LastPose.position, pose.position) > duplicateRadiusMeters)
                {
                    tracked.ConfirmedFrames = 0;
                }

                tracked.ConfirmedFrames++;
                tracked.LastPose = pose;

                if (tracked.ConfirmedFrames < rule.ConfirmationFrames)
                {
                    return;
                }

                tracked.Instance = Instantiate(rule.Prefab, pose.position, pose.rotation, contentRoot);
                tracked.Instance.transform.localScale = GetBaseScale(rule);
                tracked.LockedScale = EstimateTargetScale(tracked.Instance, rule, detection, pose);
                tracked.LockedRotation = pose.rotation;
                tracked.Instance.transform.localScale = tracked.LockedScale;
                tracked.Instance.transform.rotation = tracked.LockedRotation;
                AlignBottomToPlane(tracked.Instance, pose.position.y);
                tracked.State = ReplacementState.Locked;
                return;
            }

            tracked.Instance.transform.localScale = tracked.LockedScale;
            tracked.Instance.transform.rotation = tracked.LockedRotation;
            QueueMoveIfStable(tracked, pose);
        }

        private void QueueMoveIfStable(TrackedReplacement tracked, Pose pose)
        {
            float distanceFromLockedPose = Vector3.Distance(tracked.LastPose.position, pose.position);
            if (distanceFromLockedPose < movementDeadZoneMeters)
            {
                tracked.PendingMoveFrames = 0;
                tracked.State = ReplacementState.Locked;
                return;
            }

            if (tracked.PendingMoveFrames == 0
                || Vector3.Distance(tracked.PendingMovePose.position, pose.position) > movementDeadZoneMeters)
            {
                tracked.PendingMovePose = pose;
                tracked.PendingMoveFrames = 1;
                return;
            }

            tracked.PendingMovePose = pose;
            tracked.PendingMoveFrames++;
            if (tracked.PendingMoveFrames < movementConfirmationFrames)
            {
                return;
            }

            tracked.LastPose = pose;
            tracked.HasMoveTarget = true;
            tracked.State = ReplacementState.TrackingMove;
            tracked.PendingMoveFrames = 0;
        }

        private void SmoothActiveMoves()
        {
            for (int i = 0; i < trackedReplacements.Count; i++)
            {
                TrackedReplacement tracked = trackedReplacements[i];
                if (tracked.Instance == null || !tracked.HasMoveTarget)
                {
                    continue;
                }

                Transform instanceTransform = tracked.Instance.transform;
                Vector3 targetPosition = instanceTransform.position;
                targetPosition.x = tracked.LastPose.position.x;
                targetPosition.z = tracked.LastPose.position.z;
                instanceTransform.position = Vector3.SmoothDamp(
                    instanceTransform.position,
                    targetPosition,
                    ref tracked.MoveVelocity,
                    movementSmoothTime);
                instanceTransform.localScale = tracked.LockedScale;
                instanceTransform.rotation = tracked.LockedRotation;
                AlignBottomToPlane(tracked.Instance, tracked.LastPose.position.y);

                Vector2 currentHorizontal = new Vector2(instanceTransform.position.x, instanceTransform.position.z);
                Vector2 targetHorizontal = new Vector2(tracked.LastPose.position.x, tracked.LastPose.position.z);
                if (Vector2.Distance(currentHorizontal, targetHorizontal) <= 0.005f)
                {
                    tracked.HasMoveTarget = false;
                    tracked.MoveVelocity = Vector3.zero;
                    tracked.State = ReplacementState.Locked;
                }
            }
        }

        private void UpdateFallbackVisibility()
        {
            bool depthCanRenderOcclusion = depthProvider != null
                && depthProvider.IsEnvironmentDepthUsable;
            bool useFallback = fadeWhenDepthUnavailable
                && !depthCanRenderOcclusion
                && Time.timeSinceLevelLoad >= depthStartupGraceSeconds;
            float minimumOpacity = Mathf.Clamp01(fallbackMinimumOpacity);
            float fadeSpeed = Mathf.Max(
                0.01f,
                (1f - minimumOpacity) / Mathf.Max(0.05f, fallbackFadeDurationSeconds));
            float now = Time.time;

            for (int i = 0; i < trackedReplacements.Count; i++)
            {
                TrackedReplacement tracked = trackedReplacements[i];
                if (tracked.Instance == null)
                {
                    continue;
                }

                float targetOpacity = 1f;
                if (useFallback)
                {
                    float secondsSinceConfidentDetection = now - tracked.LastSeenTime;
                    float lostProgress = Mathf.InverseLerp(
                        fallbackFadeDelaySeconds,
                        fallbackFadeDelaySeconds + fallbackFadeDurationSeconds,
                        secondsSinceConfidentDetection);
                    targetOpacity = Mathf.Lerp(1f, minimumOpacity, lostProgress);

                    if (tracked.TrackingConfidenceThreshold > 0f
                        && tracked.LastObservationConfidence < tracked.TrackingConfidenceThreshold)
                    {
                        float confidenceRatio = Mathf.Clamp01(
                            tracked.LastObservationConfidence / tracked.TrackingConfidenceThreshold);
                        float confidenceOpacity = Mathf.Lerp(
                            minimumOpacity,
                            1f,
                            confidenceRatio);
                        targetOpacity = Mathf.Min(targetOpacity, confidenceOpacity);
                    }
                }

                float nextOpacity = Mathf.MoveTowards(
                    tracked.CurrentOpacity,
                    targetOpacity,
                    fadeSpeed * Time.deltaTime);
                ApplyOpacity(tracked, nextOpacity);
            }
        }

        private static void ApplyOpacity(TrackedReplacement tracked, float opacity)
        {
            opacity = Mathf.Clamp01(opacity);
            if (Mathf.Approximately(tracked.CurrentOpacity, opacity))
            {
                return;
            }

            if (tracked.FadeMaterials == null)
            {
                CaptureFadeMaterials(tracked);
            }

            if (tracked.FadeMaterials == null)
            {
                tracked.CurrentOpacity = opacity;
                return;
            }

            bool transparent = opacity < 0.999f;
            for (int i = 0; i < tracked.FadeMaterials.Count; i++)
            {
                RuntimeMaterialState state = tracked.FadeMaterials[i];
                Material material = state.Material;
                if (material == null)
                {
                    continue;
                }

                if (transparent)
                {
                    ConfigureTransparent(material, state);
                }
                else
                {
                    RestoreSurfaceState(material, state);
                }

                if (state.HasBaseColor)
                {
                    Color color = state.BaseColor;
                    color.a *= opacity;
                    material.SetColor(BaseColorId, color);
                }

                if (state.HasColor)
                {
                    Color color = state.Color;
                    color.a *= opacity;
                    material.SetColor(ColorId, color);
                }
            }

            tracked.CurrentOpacity = opacity;
        }

        private static void CaptureFadeMaterials(TrackedReplacement tracked)
        {
            if (tracked.Instance == null)
            {
                return;
            }

            tracked.FadeMaterials = new List<RuntimeMaterialState>();
            HashSet<Material> capturedMaterials = new HashSet<Material>();
            Renderer[] renderers = tracked.Instance.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Material[] materials = renderers[rendererIndex].materials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || !capturedMaterials.Add(material))
                    {
                        continue;
                    }

                    RuntimeMaterialState state = new RuntimeMaterialState
                    {
                        Material = material,
                        HasBaseColor = material.HasProperty(BaseColorId),
                        HasColor = material.HasProperty(ColorId),
                        HasSurface = material.HasProperty(SurfaceId),
                        HasBlend = material.HasProperty(BlendId),
                        HasSrcBlend = material.HasProperty(SrcBlendId),
                        HasDstBlend = material.HasProperty(DstBlendId),
                        HasSrcBlendAlpha = material.HasProperty(SrcBlendAlphaId),
                        HasDstBlendAlpha = material.HasProperty(DstBlendAlphaId),
                        HasZWrite = material.HasProperty(ZWriteId),
                        RenderQueue = material.renderQueue,
                        RenderType = material.GetTag("RenderType", false, string.Empty),
                        SurfaceTransparentKeyword = material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"),
                        AlphaPremultiplyKeyword = material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"),
                        ShadowCasterPassEnabled = material.GetShaderPassEnabled("ShadowCaster")
                    };

                    if (state.HasBaseColor)
                    {
                        state.BaseColor = material.GetColor(BaseColorId);
                    }

                    if (state.HasColor)
                    {
                        state.Color = material.GetColor(ColorId);
                    }

                    if (state.HasSurface)
                    {
                        state.Surface = material.GetFloat(SurfaceId);
                    }

                    if (state.HasBlend)
                    {
                        state.Blend = material.GetFloat(BlendId);
                    }

                    if (state.HasSrcBlend)
                    {
                        state.SrcBlend = material.GetFloat(SrcBlendId);
                    }

                    if (state.HasDstBlend)
                    {
                        state.DstBlend = material.GetFloat(DstBlendId);
                    }

                    if (state.HasSrcBlendAlpha)
                    {
                        state.SrcBlendAlpha = material.GetFloat(SrcBlendAlphaId);
                    }

                    if (state.HasDstBlendAlpha)
                    {
                        state.DstBlendAlpha = material.GetFloat(DstBlendAlphaId);
                    }

                    if (state.HasZWrite)
                    {
                        state.ZWrite = material.GetFloat(ZWriteId);
                    }

                    tracked.FadeMaterials.Add(state);
                }
            }
        }

        private static void ConfigureTransparent(Material material, RuntimeMaterialState state)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            if (state.HasSurface)
            {
                material.SetFloat(SurfaceId, 1f);
            }

            if (state.HasBlend)
            {
                material.SetFloat(BlendId, 0f);
            }

            if (state.HasSrcBlend)
            {
                material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            }

            if (state.HasDstBlend)
            {
                material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            }

            if (state.HasSrcBlendAlpha)
            {
                material.SetFloat(SrcBlendAlphaId, (float)BlendMode.One);
            }

            if (state.HasDstBlendAlpha)
            {
                material.SetFloat(DstBlendAlphaId, (float)BlendMode.OneMinusSrcAlpha);
            }

            if (state.HasZWrite)
            {
                material.SetFloat(ZWriteId, 0f);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
        }

        private static void RestoreSurfaceState(Material material, RuntimeMaterialState state)
        {
            material.SetOverrideTag("RenderType", state.RenderType);
            if (state.HasSurface)
            {
                material.SetFloat(SurfaceId, state.Surface);
            }

            if (state.HasBlend)
            {
                material.SetFloat(BlendId, state.Blend);
            }

            if (state.HasSrcBlend)
            {
                material.SetFloat(SrcBlendId, state.SrcBlend);
            }

            if (state.HasDstBlend)
            {
                material.SetFloat(DstBlendId, state.DstBlend);
            }

            if (state.HasSrcBlendAlpha)
            {
                material.SetFloat(SrcBlendAlphaId, state.SrcBlendAlpha);
            }

            if (state.HasDstBlendAlpha)
            {
                material.SetFloat(DstBlendAlphaId, state.DstBlendAlpha);
            }

            if (state.HasZWrite)
            {
                material.SetFloat(ZWriteId, state.ZWrite);
            }

            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", state.SurfaceTransparentKeyword);
            SetKeyword(material, "_ALPHAPREMULTIPLY_ON", state.AlphaPremultiplyKeyword);
            material.renderQueue = state.RenderQueue;
            material.SetShaderPassEnabled("ShadowCaster", state.ShadowCasterPassEnabled);
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }

        private void UpdateLostStates()
        {
            if (lostStateDelaySeconds <= 0f)
            {
                return;
            }

            float now = Time.time;
            for (int i = 0; i < trackedReplacements.Count; i++)
            {
                TrackedReplacement tracked = trackedReplacements[i];
                if (tracked.State == ReplacementState.Searching
                    || tracked.State == ReplacementState.Lost)
                {
                    continue;
                }

                if (now - tracked.LastSeenTime <= lostStateDelaySeconds)
                {
                    continue;
                }

                if (tracked.Instance == null)
                {
                    tracked.State = ReplacementState.Searching;
                    tracked.ConfirmedFrames = 0;
                    tracked.PendingMoveFrames = 0;
                    continue;
                }

                tracked.State = ReplacementState.Lost;
                tracked.HasMoveTarget = false;
                tracked.PendingMoveFrames = 0;
                tracked.MoveVelocity = Vector3.zero;
            }
        }

        private bool IsWithinReacquireRadius(TrackedReplacement tracked, Pose pose)
        {
            if (reacquireMatchRadiusMeters <= 0f)
            {
                return true;
            }

            return Vector3.Distance(tracked.LastPose.position, pose.position) <= reacquireMatchRadiusMeters;
        }

        private TrackedReplacement FindBestTrack(
            string label,
            Pose pose,
            int frame)
        {
            TrackedReplacement best = null;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < trackedReplacements.Count; i++)
            {
                TrackedReplacement tracked = trackedReplacements[i];
                if (!string.Equals(tracked.Label, label, System.StringComparison.OrdinalIgnoreCase)
                    || tracked.LastMatchedFrame == frame)
                {
                    continue;
                }

                float distance = Vector3.Distance(tracked.LastPose.position, pose.position);
                float matchRadius = tracked.Instance == null
                    ? duplicateRadiusMeters
                    : reacquireMatchRadiusMeters;

                if (matchRadius > 0f && distance > matchRadius)
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = tracked;
                }
            }

            return best;
        }

        private Vector3 EstimateTargetScale(
            GameObject instance,
            RetroReplacementRule rule,
            DetectionResult detection,
            Pose pose)
        {
            if (!rule.EstimateScaleFromBoundingBox || arCamera == null)
            {
                return GetBaseScale(rule);
            }

            if (!TryGetRendererBounds(instance, out Bounds bounds)
                || (bounds.size.x <= 0.0001f && bounds.size.y <= 0.0001f))
            {
                return GetBaseScale(rule);
            }

            float distance = Vector3.Distance(arCamera.transform.position, pose.position);
            float visibleWorldHeight = 2f
                * distance
                * Mathf.Tan(arCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float visibleWorldWidth = visibleWorldHeight * arCamera.aspect;
            float targetHeight = detection.NormalizedBox.height
                * visibleWorldHeight
                * rule.EstimatedHeightMultiplier
                * rule.ScaleCalibrationMultiplier;
            float targetWidth = detection.NormalizedBox.width
                * visibleWorldWidth
                * rule.EstimatedWidthMultiplier
                * rule.ScaleCalibrationMultiplier;
            float scaleMultiplier = CalculateScaleMultiplier(rule, bounds, targetWidth, targetHeight);
            Vector2 range = rule.ScaleMultiplierRange;
            if (range.x <= 0f && range.y <= 0f)
            {
                range = new Vector2(0.25f, 4f);
            }
            scaleMultiplier = Mathf.Clamp(
                scaleMultiplier,
                Mathf.Min(range.x, range.y),
                Mathf.Max(range.x, range.y));
            return Vector3.Scale(instance.transform.localScale, Vector3.one * scaleMultiplier);
        }

        private static float CalculateScaleMultiplier(
            RetroReplacementRule rule,
            Bounds bounds,
            float targetWidth,
            float targetHeight)
        {
            float heightMultiplier = bounds.size.y > 0.0001f
                ? targetHeight / bounds.size.y
                : 0f;
            float widthMultiplier = bounds.size.x > 0.0001f
                ? targetWidth / bounds.size.x
                : 0f;

            switch (rule.BoundingBoxScaleAxis)
            {
                case RetroReplacementRule.ScaleBoundingBoxAxis.Width:
                    return widthMultiplier > 0f ? widthMultiplier : heightMultiplier;
                case RetroReplacementRule.ScaleBoundingBoxAxis.MaxDimension:
                    return Mathf.Max(widthMultiplier, heightMultiplier);
                default:
                    return heightMultiplier > 0f ? heightMultiplier : widthMultiplier;
            }
        }

        private static Vector3 GetBaseScale(RetroReplacementRule rule)
        {
            return Vector3.Scale(
                rule.SpawnScale,
                Vector3.one * rule.ScaleCalibrationMultiplier);
        }

        private static void AlignBottomToPlane(GameObject instance, float planeHeight)
        {
            if (!TryGetRendererBounds(instance, out Bounds bounds))
            {
                return;
            }

            Transform instanceTransform = instance.transform;
            Vector3 position = instanceTransform.position;
            position.y += planeHeight - bounds.min.y;
            instanceTransform.position = position;
        }

        private static bool TryGetRendererBounds(GameObject instance, out Bounds bounds)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private void RemoveExpiredReplacements()
        {
            if (lostGraceSeconds <= 0f)
            {
                return;
            }

            float now = Time.time;
            for (int i = trackedReplacements.Count - 1; i >= 0; i--)
            {
                TrackedReplacement tracked = trackedReplacements[i];
                if (now - tracked.LastSeenTime <= lostGraceSeconds)
                {
                    continue;
                }

                if (tracked.Instance == null)
                {
                    ReleaseFadeMaterials(tracked);
                    trackedReplacements.RemoveAt(i);
                    continue;
                }

                if (!destroyWhenLost)
                {
                    continue;
                }

                ReleaseFadeMaterials(tracked);
                Destroy(tracked.Instance);

                trackedReplacements.RemoveAt(i);
            }
        }

        private static void ReleaseFadeMaterials(TrackedReplacement tracked)
        {
            if (tracked.FadeMaterials == null)
            {
                return;
            }

            for (int i = 0; i < tracked.FadeMaterials.Count; i++)
            {
                Material material = tracked.FadeMaterials[i].Material;
                if (material != null)
                {
                    Object.Destroy(material);
                }
            }

            tracked.FadeMaterials.Clear();
            tracked.FadeMaterials = null;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < trackedReplacements.Count; i++)
            {
                ReleaseFadeMaterials(trackedReplacements[i]);
            }
        }
    }
}
