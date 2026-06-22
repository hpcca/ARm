using System.Collections.Generic;
using UnityEngine;

namespace AR80sRetro
{
    public sealed class RetroReplacementManager : MonoBehaviour
    {
        private sealed class TrackedReplacement
        {
            public GameObject Instance;
            public Pose LastPose;
            public Vector3 TargetScale;
            public int ConfirmedFrames;
            public float LastSeenTime;
        }

        [SerializeField] private RetroPrefabLibrary prefabLibrary;
        [SerializeField] private ARRaycastPositionSolver positionSolver;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private Camera arCamera;
        [SerializeField] private float lostGraceSeconds = 1f;
        [SerializeField] private float duplicateRadiusMeters = 0.25f;

        private readonly Dictionary<string, TrackedReplacement> trackedByLabel = new Dictionary<string, TrackedReplacement>();

        private void Awake()
        {
            if (arCamera == null)
            {
                arCamera = Camera.main;
            }
        }

        private void Reset()
        {
            positionSolver = FindObjectOfType<ARRaycastPositionSolver>();
            arCamera = Camera.main;
            contentRoot = transform;
        }

        private void Update()
        {
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

                if (!detection.IsValid(rule.MinConfidence))
                {
                    continue;
                }

                if (!positionSolver.TrySolvePose(detection, out Pose pose))
                {
                    continue;
                }

                pose.position += Vector3.up * rule.VerticalOffsetMeters;
                UpdateReplacement(rule, detection, pose, now);
            }
        }

        private void UpdateReplacement(
            RetroReplacementRule rule,
            DetectionResult detection,
            Pose pose,
            float now)
        {
            string key = rule.DetectionLabel.ToLowerInvariant();
            if (!trackedByLabel.TryGetValue(key, out TrackedReplacement tracked))
            {
                tracked = new TrackedReplacement();
                trackedByLabel.Add(key, tracked);
            }

            if (tracked.Instance != null)
            {
                float distance = Vector3.Distance(tracked.Instance.transform.position, pose.position);
                if (distance > duplicateRadiusMeters)
                {
                    tracked.ConfirmedFrames = 0;
                }
            }

            tracked.ConfirmedFrames++;
            tracked.LastSeenTime = now;
            tracked.LastPose = pose;

            if (tracked.Instance == null)
            {
                if (tracked.ConfirmedFrames < rule.ConfirmationFrames)
                {
                    return;
                }

                tracked.Instance = Instantiate(rule.Prefab, pose.position, pose.rotation, contentRoot);
                tracked.Instance.transform.localScale = rule.SpawnScale;
                tracked.TargetScale = EstimateTargetScale(tracked.Instance, rule, detection, pose);
                tracked.Instance.transform.localScale = tracked.TargetScale;
                AlignBottomToPlane(tracked.Instance, pose.position.y);
                return;
            }

            Transform instanceTransform = tracked.Instance.transform;
            float smoothing = rule.PositionSmoothing;
            tracked.TargetScale = EstimateTargetScale(tracked.Instance, rule, detection, pose);
            instanceTransform.localScale = Vector3.Lerp(
                instanceTransform.localScale,
                tracked.TargetScale,
                smoothing);
            instanceTransform.SetPositionAndRotation(
                Vector3.Lerp(instanceTransform.position, pose.position, smoothing),
                Quaternion.Slerp(instanceTransform.rotation, pose.rotation, smoothing));
            AlignBottomToPlane(tracked.Instance, pose.position.y);
        }

        private Vector3 EstimateTargetScale(
            GameObject instance,
            RetroReplacementRule rule,
            DetectionResult detection,
            Pose pose)
        {
            if (!rule.EstimateScaleFromBoundingBox || arCamera == null)
            {
                return rule.SpawnScale;
            }

            if (!TryGetRendererBounds(instance, out Bounds bounds) || bounds.size.y <= 0.0001f)
            {
                return rule.SpawnScale;
            }

            float distance = Vector3.Distance(arCamera.transform.position, pose.position);
            float visibleWorldHeight = 2f
                * distance
                * Mathf.Tan(arCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float targetHeight = detection.NormalizedBox.height
                * visibleWorldHeight
                * rule.EstimatedHeightMultiplier;
            float scaleMultiplier = targetHeight / bounds.size.y;
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
            s_ReusableExpiredKeys.Clear();

            foreach (KeyValuePair<string, TrackedReplacement> item in trackedByLabel)
            {
                if (now - item.Value.LastSeenTime > lostGraceSeconds)
                {
                    s_ReusableExpiredKeys.Add(item.Key);
                }
            }

            for (int i = 0; i < s_ReusableExpiredKeys.Count; i++)
            {
                string key = s_ReusableExpiredKeys[i];
                TrackedReplacement tracked = trackedByLabel[key];
                if (tracked.Instance != null)
                {
                    Destroy(tracked.Instance);
                }

                trackedByLabel.Remove(key);
            }
        }

        private static readonly List<string> s_ReusableExpiredKeys = new List<string>();
    }
}
