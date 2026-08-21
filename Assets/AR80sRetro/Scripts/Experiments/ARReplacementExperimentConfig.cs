using UnityEngine;

namespace AR80sRetro.Experiments
{
    [DisallowMultipleComponent]
    public sealed class ARReplacementExperimentConfig : MonoBehaviour
    {
        [SerializeField] private ExperimentSessionConfig session = new ExperimentSessionConfig();
        [SerializeField] private ExperimentAblationMode ablationMode =
            ExperimentAblationMode.A3OcclusionFadeFallback;

        [Header("Custom Ablation Flags")]
        [SerializeField] private bool customDepthPositionFusion = true;
        [SerializeField] private bool customTemporalTracking = true;
        [SerializeField] private bool customOcclusionFadeFallback = true;

        [Header("Diagnostics")]
        [SerializeField] private bool debugOverlayEnabled = true;
        [SerializeField] private bool loggingEnabled;

        public ExperimentSessionConfig Session => session;
        public ExperimentAblationMode AblationMode => ablationMode;
        public string AblationModeId => ToModeId(ablationMode);
        public bool DebugOverlayEnabled => debugOverlayEnabled;
        public bool LoggingEnabled => loggingEnabled;

        public bool DepthPositionFusionEnabled
        {
            get
            {
                switch (ablationMode)
                {
                    case ExperimentAblationMode.A0PlaneOnly:
                        return false;
                    case ExperimentAblationMode.Custom:
                        return customDepthPositionFusion;
                    default:
                        return true;
                }
            }
        }

        public bool TemporalTrackingEnabled
        {
            get
            {
                switch (ablationMode)
                {
                    case ExperimentAblationMode.A2TemporalTracking:
                    case ExperimentAblationMode.A3OcclusionFadeFallback:
                        return true;
                    case ExperimentAblationMode.Custom:
                        return customTemporalTracking;
                    default:
                        return false;
                }
            }
        }

        public bool OcclusionFadeFallbackEnabled
        {
            get
            {
                switch (ablationMode)
                {
                    case ExperimentAblationMode.A3OcclusionFadeFallback:
                        return true;
                    case ExperimentAblationMode.Custom:
                        return customOcclusionFadeFallback;
                    default:
                        return false;
                }
            }
        }

        public void FillRecordContext(ExperimentFrameRecord record)
        {
            if (record == null)
            {
                return;
            }

            ExperimentSessionConfig currentSession = session ?? new ExperimentSessionConfig();
            record.SessionId = currentSession.SessionId;
            record.TrialId = currentSession.TrialId;
            record.ObjectId = currentSession.ObjectId;
            record.ExpectedObjectCount = currentSession.ExpectedObjectCount;
            record.SceneId = currentSession.SceneId;
            record.ConditionId = currentSession.ConditionId;
            record.DistanceCondition = currentSession.DistanceCondition;
            record.ViewCondition = currentSession.ViewCondition;
            record.OcclusionPercent = currentSession.OcclusionPercent;
            record.LightingCondition = currentSession.LightingCondition;
            record.AblationMode = AblationModeId;
            record.DepthFusionEnabled = DepthPositionFusionEnabled;
            record.TemporalTrackingEnabled = TemporalTrackingEnabled;
            record.OcclusionFadeEnabled = OcclusionFadeFallbackEnabled;
        }

        private static string ToModeId(ExperimentAblationMode mode)
        {
            switch (mode)
            {
                case ExperimentAblationMode.A0PlaneOnly:
                    return "A0";
                case ExperimentAblationMode.A1DepthFusion:
                    return "A1";
                case ExperimentAblationMode.A2TemporalTracking:
                    return "A2";
                case ExperimentAblationMode.A3OcclusionFadeFallback:
                    return "A3";
                default:
                    return "CUSTOM";
            }
        }
    }
}
