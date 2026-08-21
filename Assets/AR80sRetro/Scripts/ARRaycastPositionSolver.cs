using System.Collections.Generic;
using AR80sRetro.Experiments;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace AR80sRetro
{
    public sealed class ARRaycastPositionSolver : MonoBehaviour
    {
        private static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();

        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private ARDepthFrameProvider depthProvider;
        [SerializeField] private TrackableType trackableTypes = TrackableType.PlaneWithinPolygon;
        [SerializeField] private Vector2 anchorInBoundingBox = new Vector2(0.5f, 0.9f);
        [SerializeField] private bool faceCamera;
        [SerializeField, Range(0f, 1f)] private float depthHorizontalWeight = 0.75f;
        [SerializeField, Min(0.05f)] private float maxDepthPlaneHorizontalDeltaMeters = 0.6f;

        [Header("Experiment")]
        [SerializeField] private ARReplacementExperimentConfig experimentConfig;

        public float DepthHorizontalWeight => depthHorizontalWeight;
        public float MaxDepthPlaneHorizontalDeltaMeters => maxDepthPlaneHorizontalDeltaMeters;

        private void Reset()
        {
            raycastManager = FindObjectOfType<ARRaycastManager>();
            depthProvider = FindObjectOfType<ARDepthFrameProvider>();
            arCamera = Camera.main;
        }

        public bool TrySolvePose(DetectionResult detection, out Pose pose)
        {
            return TrySolvePose(detection, anchorInBoundingBox, out pose);
        }

        public bool TrySolvePose(
            DetectionResult detection,
            Vector2 normalizedAnchorInBox,
            out Pose pose)
        {
            return TrySolvePose(
                detection,
                normalizedAnchorInBox,
                out pose,
                out _);
        }

        public bool TrySolvePose(
            DetectionResult detection,
            Vector2 normalizedAnchorInBox,
            out Pose pose,
            out PositionSolveDiagnostics diagnostics)
        {
            long raycastStartTimestamp = ExperimentClock.Timestamp();
            pose = default;
            diagnostics = new PositionSolveDiagnostics
            {
                Depth = new DepthSampleDiagnostics(false, false, false, 0, 0f, 0f, 0f)
            };

            if (raycastManager == null)
            {
                diagnostics.FailureReason = "raycast_manager_missing";
                diagnostics.RaycastFusionLatencyMs = ExperimentClock.ElapsedMilliseconds(
                    raycastStartTimestamp);
                return false;
            }

            Vector2 effectiveAnchor = normalizedAnchorInBox;
            if (effectiveAnchor == Vector2.zero)
            {
                effectiveAnchor = new Vector2(0.5f, 0.9f);
            }

            Vector2 screenPoint = detection.ToScreenPoint(
                Screen.width,
                Screen.height,
                effectiveAnchor);
            if (!raycastManager.Raycast(screenPoint, Hits, trackableTypes))
            {
                diagnostics.FailureReason = "plane_raycast_failed";
                diagnostics.RaycastFusionLatencyMs = ExperimentClock.ElapsedMilliseconds(
                    raycastStartTimestamp);
                return false;
            }

            pose = Hits[0].pose;
            Vector3 planePosition = pose.position;
            diagnostics.PlaneRaycastSuccess = true;
            diagnostics.PlanePosition = planePosition;
            float raycastLatencyMs = ExperimentClock.ElapsedMilliseconds(raycastStartTimestamp);

            bool depthFusionEnabled = experimentConfig == null
                || experimentConfig.DepthPositionFusionEnabled;
            bool hasDepthPoint = false;
            Vector3 depthWorldPoint = default;
            if (depthFusionEnabled && depthProvider != null && depthHorizontalWeight > 0f)
            {
                hasDepthPoint = depthProvider.TrySampleWorldPoint(
                    detection,
                    new Vector2(0.5f, 0.5f),
                    out depthWorldPoint,
                    out _,
                    out _,
                    out DepthSampleDiagnostics depthDiagnostics);
                diagnostics.Depth = depthDiagnostics;
            }

            long fusionStartTimestamp = ExperimentClock.Timestamp();
            if (hasDepthPoint)
            {
                diagnostics.DepthWorldPosition = depthWorldPoint;
                Vector2 planeHorizontal = new Vector2(planePosition.x, planePosition.z);
                Vector2 depthHorizontal = new Vector2(depthWorldPoint.x, depthWorldPoint.z);
                float horizontalDelta = Vector2.Distance(planeHorizontal, depthHorizontal);
                float effectiveWeight = horizontalDelta > maxDepthPlaneHorizontalDeltaMeters
                    ? depthHorizontalWeight * 0.35f
                    : depthHorizontalWeight;

                pose.position = new Vector3(
                    Mathf.Lerp(planePosition.x, depthWorldPoint.x, effectiveWeight),
                    planePosition.y,
                    Mathf.Lerp(planePosition.z, depthWorldPoint.z, effectiveWeight));
            }

            if (faceCamera && arCamera != null)
            {
                Vector3 toCamera = arCamera.transform.position - pose.position;
                toCamera.y = 0f;
                if (toCamera.sqrMagnitude > 0.0001f)
                {
                    pose.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
            }

            diagnostics.FusedPosition = pose.position;
            diagnostics.RaycastFusionLatencyMs = raycastLatencyMs
                + ExperimentClock.ElapsedMilliseconds(fusionStartTimestamp);
            return true;
        }
    }
}
