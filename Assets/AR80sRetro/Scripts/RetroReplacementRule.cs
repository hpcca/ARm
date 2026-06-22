using System;
using UnityEngine;

namespace AR80sRetro
{
    [Serializable]
    public sealed class RetroReplacementRule
    {
        [SerializeField] private string detectionLabel = "cup";
        [SerializeField] private GameObject prefab;
        [SerializeField] private Vector3 spawnScale = Vector3.one;
        [SerializeField] private float verticalOffsetMeters;
        [SerializeField] private float minConfidence = 0.6f;
        [SerializeField] private int confirmationFrames = 5;
        [SerializeField, Range(0.01f, 1f)] private float positionSmoothing = 0.18f;
        [SerializeField] private bool estimateScaleFromBoundingBox = true;
        [SerializeField, Min(0.1f)] private float estimatedHeightMultiplier = 0.9f;
        [SerializeField] private Vector2 scaleMultiplierRange = new Vector2(0.25f, 4f);

        public string DetectionLabel => detectionLabel;
        public GameObject Prefab => prefab;
        public Vector3 SpawnScale => spawnScale;
        public float VerticalOffsetMeters => verticalOffsetMeters;
        public float MinConfidence => minConfidence;
        public int ConfirmationFrames => Mathf.Max(1, confirmationFrames);
        public float PositionSmoothing => positionSmoothing;
        public bool EstimateScaleFromBoundingBox => estimateScaleFromBoundingBox;
        public float EstimatedHeightMultiplier => estimatedHeightMultiplier;
        public Vector2 ScaleMultiplierRange => scaleMultiplierRange;
    }
}
