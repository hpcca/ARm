using System;
using UnityEngine;

namespace AR80sRetro.Experiments
{
    public enum ExperimentAblationMode
    {
        A0PlaneOnly,
        A1DepthFusion,
        A2TemporalTracking,
        A3OcclusionFadeFallback,
        Custom
    }

    [Serializable]
    public sealed class ExperimentSessionConfig
    {
        [Header("Identity")]
        [SerializeField] private string sessionId;
        [SerializeField] private string trialId = "trial_001";
        [SerializeField] private string objectId = "object_001";
        [SerializeField, Min(1)] private int expectedObjectCount = 1;
        [SerializeField] private string sceneId = "scene_001";
        [SerializeField] private string conditionId = "baseline";

        [Header("Controlled Conditions")]
        [SerializeField] private string distanceCondition = "1.0m";
        [SerializeField] private string viewCondition = "frontal";
        [SerializeField, Range(0f, 100f)] private float occlusionPercent;
        [SerializeField] private string lightingCondition = "normal";

        [Header("Build Provenance")]
        [SerializeField] private string buildCommitSha;
        [SerializeField] private string yoloModelIdentifier = "yolov8n-coco-640";
        [SerializeField, TextArea(2, 6)] private string sceneDescription;

        public string SessionId => sessionId;
        public string TrialId => trialId;
        public string ObjectId => objectId;
        public int ExpectedObjectCount => Mathf.Max(1, expectedObjectCount);
        public string SceneId => sceneId;
        public string ConditionId => conditionId;
        public string DistanceCondition => distanceCondition;
        public string ViewCondition => viewCondition;
        public float OcclusionPercent => occlusionPercent;
        public string LightingCondition => lightingCondition;
        public string BuildCommitSha => buildCommitSha;
        public string YoloModelIdentifier => yoloModelIdentifier;
        public string SceneDescription => sceneDescription;
    }
}
