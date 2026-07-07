using System.Collections.Generic;
using UnityEngine;

namespace AR80sRetro
{
    public sealed class RetroReplacementManager : MonoBehaviour
    {
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
            public GameObject Instance;
            public Pose LastPose;
            public Pose PendingMovePose;
            public Vector3 LockedScale;
            public Quaternion LockedRotation;
            public Vector3 MoveVelocity;
            public int ConfirmedFrames;
            public int PendingMoveFrames;
            public float LastSeenTime;
            public bool HasMoveTarget;
            public ReplacementState State = ReplacementState.Searching;
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

        // Demo scope: one active replacement per detected label. Use spatial tracks if same-class multi-instance is needed.
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
            UpdateLostStates();
            SmoothActiveMoves();
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
                trackedByLabel.TryGetValue(key, out TrackedReplacement tracked);
                bool hasLockedInstance = tracked != null && tracked.Instance != null;
                float requiredConfidence = hasLockedInstance
                    ? rule.TrackingMinConfidence
                    : rule.MinConfidence;

                if (!detection.IsValid(requiredConfidence))
                {
                    continue;
                }

                if (!positionSolver.TrySolvePose(detection, rule.RaycastAnchorInBoundingBox, out Pose pose))
                {
                    continue;
                }

                pose.position += Vector3.up * rule.VerticalOffsetMeters;
                pose.rotation *= rule.RotationOffset;
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
            string key = rule.DetectionLabel.ToLowerInvariant();
            if (tracked == null)
            {
                tracked = new TrackedReplacement();
                trackedByLabel.Add(key, tracked);
            }

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
            foreach (KeyValuePair<string, TrackedReplacement> item in trackedByLabel)
            {
                TrackedReplacement tracked = item.Value;
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

        private void UpdateLostStates()
        {
            if (lostStateDelaySeconds <= 0f)
            {
                return;
            }

            float now = Time.time;
            foreach (KeyValuePair<string, TrackedReplacement> item in trackedByLabel)
            {
                TrackedReplacement tracked = item.Value;
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

            if (!TryGetRendererBounds(instance, out Bounds bounds) || bounds.size.y <= 0.0001f)
            {
                return GetBaseScale(rule);
            }

            float distance = Vector3.Distance(arCamera.transform.position, pose.position);
            float visibleWorldHeight = 2f
                * distance
                * Mathf.Tan(arCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float targetHeight = detection.NormalizedBox.height
                * visibleWorldHeight
                * rule.EstimatedHeightMultiplier
                * rule.ScaleCalibrationMultiplier;
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
            if (!destroyWhenLost || lostGraceSeconds <= 0f)
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
