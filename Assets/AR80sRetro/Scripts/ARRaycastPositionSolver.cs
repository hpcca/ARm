using System.Collections.Generic;
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
        [SerializeField] private TrackableType trackableTypes = TrackableType.PlaneWithinPolygon;
        [SerializeField] private Vector2 anchorInBoundingBox = new Vector2(0.5f, 0.9f);

        private void Reset()
        {
            raycastManager = FindObjectOfType<ARRaycastManager>();
            arCamera = Camera.main;
        }

        public bool TrySolvePose(DetectionResult detection, out Pose pose)
        {
            pose = default;

            if (raycastManager == null)
            {
                return false;
            }

            Vector2 effectiveAnchor = anchorInBoundingBox;
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
                return false;
            }

            pose = Hits[0].pose;

            if (arCamera != null)
            {
                Vector3 toCamera = arCamera.transform.position - pose.position;
                toCamera.y = 0f;
                if (toCamera.sqrMagnitude > 0.0001f)
                {
                    pose.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
            }

            return true;
        }
    }
}
