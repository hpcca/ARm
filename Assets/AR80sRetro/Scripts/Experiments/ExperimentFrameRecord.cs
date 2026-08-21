using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace AR80sRetro.Experiments
{
    public readonly struct DetectionCycleDiagnostics
    {
        public DetectionCycleDiagnostics(
            long frameId,
            long timestampMs,
            long cycleStartTimestamp,
            float captureLatencyMs,
            float yoloLatencyMs,
            float outputReadbackLatencyMs,
            bool succeeded,
            string failureReason)
        {
            FrameId = frameId;
            TimestampMs = timestampMs;
            CycleStartTimestamp = cycleStartTimestamp;
            CaptureLatencyMs = captureLatencyMs;
            YoloLatencyMs = yoloLatencyMs;
            OutputReadbackLatencyMs = outputReadbackLatencyMs;
            Succeeded = succeeded;
            FailureReason = failureReason;
        }

        public long FrameId { get; }
        public long TimestampMs { get; }
        public long CycleStartTimestamp { get; }
        public float CaptureLatencyMs { get; }
        public float YoloLatencyMs { get; }
        public float OutputReadbackLatencyMs { get; }
        public bool Succeeded { get; }
        public string FailureReason { get; }
    }

    public readonly struct DepthSampleDiagnostics
    {
        public DepthSampleDiagnostics(
            bool attempted,
            bool available,
            bool confidenceAvailable,
            int validSampleCount,
            float medianDepthMeters,
            float medianConfidence,
            float latencyMs)
        {
            Attempted = attempted;
            Available = available;
            ConfidenceAvailable = confidenceAvailable;
            ValidSampleCount = validSampleCount;
            MedianDepthMeters = medianDepthMeters;
            MedianConfidence = medianConfidence;
            LatencyMs = latencyMs;
        }

        public bool Attempted { get; }
        public bool Available { get; }
        public bool ConfidenceAvailable { get; }
        public int ValidSampleCount { get; }
        public float MedianDepthMeters { get; }
        public float MedianConfidence { get; }
        public float LatencyMs { get; }
    }

    public struct PositionSolveDiagnostics
    {
        public bool PlaneRaycastSuccess;
        public Vector3 PlanePosition;
        public DepthSampleDiagnostics Depth;
        public Vector3 DepthWorldPosition;
        public Vector3 FusedPosition;
        public float RaycastFusionLatencyMs;
        public string FailureReason;
    }

    public sealed class ExperimentFrameRecord
    {
        private static readonly char[] CsvQuoteCharacters = { ',', '"', '\r', '\n' };

        public const string CsvHeader =
            "session_id,trial_id,frame_id,timestamp_ms,object_id,expected_object_count," +
            "class_label,scene_id," +
            "condition_id,distance_condition,view_condition,occlusion_percent,lighting_condition," +
            "ablation_mode,yolo_confidence,bbox_x,bbox_y,bbox_width,bbox_height," +
            "depth_fusion_enabled,temporal_tracking_enabled,occlusion_fade_enabled," +
            "depth_sampling_attempted,depth_available,depth_valid_sample_count," +
            "depth_sample_capacity,depth_median_m," +
            "depth_confidence_available,depth_confidence," +
            "plane_raycast_success,plane_position_x,plane_position_y,plane_position_z," +
            "depth_world_x,depth_world_y,depth_world_z,fused_position_x,fused_position_y," +
            "fused_position_z,output_yaw_deg,output_scale_x,output_scale_y,output_scale_z," +
            "track_id,track_state,capture_latency_ms,yolo_latency_ms,output_readback_latency_ms," +
            "depth_latency_ms,raycast_fusion_latency_ms,tracking_latency_ms,total_latency_ms," +
            "depth_occlusion_usable,fade_fallback_active,output_opacity,success,failure_reason," +
            "result_source";

        public string SessionId;
        public string TrialId;
        public long FrameId;
        public long TimestampMs;
        public string ObjectId;
        public int ExpectedObjectCount = 1;
        public string ClassLabel;
        public string SceneId;
        public string ConditionId;
        public string DistanceCondition;
        public string ViewCondition;
        public float OcclusionPercent;
        public string LightingCondition;
        public string AblationMode;
        public float YoloConfidence = float.NaN;
        public float BboxX = float.NaN;
        public float BboxY = float.NaN;
        public float BboxWidth = float.NaN;
        public float BboxHeight = float.NaN;
        public bool DepthFusionEnabled;
        public bool TemporalTrackingEnabled;
        public bool OcclusionFadeEnabled;
        public bool DepthSamplingAttempted;
        public bool DepthAvailable;
        public int DepthValidSampleCount;
        public int DepthSampleCapacity;
        public float DepthMedianMeters = float.NaN;
        public bool DepthConfidenceAvailable;
        public float DepthConfidence = float.NaN;
        public bool PlaneRaycastSuccess;
        public Vector3 PlanePosition = NaNVector();
        public Vector3 DepthWorldPosition = NaNVector();
        public Vector3 FusedPosition = NaNVector();
        public float OutputYawDegrees = float.NaN;
        public Vector3 OutputScale = NaNVector();
        public int TrackId = -1;
        public string TrackState;
        public float CaptureLatencyMs;
        public float YoloLatencyMs;
        public float OutputReadbackLatencyMs;
        public float DepthLatencyMs;
        public float RaycastFusionLatencyMs;
        public float TrackingLatencyMs;
        public float TotalLatencyMs;
        public bool DepthOcclusionUsable;
        public bool FadeFallbackActive;
        public float OutputOpacity = float.NaN;
        public bool Success;
        public string FailureReason;
        public string ResultSource = "Measured";

        public string ToCsvLine()
        {
            StringBuilder builder = new StringBuilder(768);
            Append(builder, SessionId);
            Append(builder, TrialId);
            Append(builder, FrameId);
            Append(builder, TimestampMs);
            Append(builder, ObjectId);
            Append(builder, ExpectedObjectCount);
            Append(builder, ClassLabel);
            Append(builder, SceneId);
            Append(builder, ConditionId);
            Append(builder, DistanceCondition);
            Append(builder, ViewCondition);
            Append(builder, OcclusionPercent);
            Append(builder, LightingCondition);
            Append(builder, AblationMode);
            Append(builder, YoloConfidence);
            Append(builder, BboxX);
            Append(builder, BboxY);
            Append(builder, BboxWidth);
            Append(builder, BboxHeight);
            Append(builder, DepthFusionEnabled);
            Append(builder, TemporalTrackingEnabled);
            Append(builder, OcclusionFadeEnabled);
            Append(builder, DepthSamplingAttempted);
            Append(builder, DepthAvailable);
            Append(builder, DepthValidSampleCount);
            Append(builder, DepthSampleCapacity);
            Append(builder, DepthMedianMeters);
            Append(builder, DepthConfidenceAvailable);
            Append(builder, DepthConfidence);
            Append(builder, PlaneRaycastSuccess);
            Append(builder, PlanePosition.x);
            Append(builder, PlanePosition.y);
            Append(builder, PlanePosition.z);
            Append(builder, DepthWorldPosition.x);
            Append(builder, DepthWorldPosition.y);
            Append(builder, DepthWorldPosition.z);
            Append(builder, FusedPosition.x);
            Append(builder, FusedPosition.y);
            Append(builder, FusedPosition.z);
            Append(builder, OutputYawDegrees);
            Append(builder, OutputScale.x);
            Append(builder, OutputScale.y);
            Append(builder, OutputScale.z);
            Append(builder, TrackId >= 0 ? TrackId.ToString(CultureInfo.InvariantCulture) : string.Empty);
            Append(builder, TrackState);
            Append(builder, CaptureLatencyMs);
            Append(builder, YoloLatencyMs);
            Append(builder, OutputReadbackLatencyMs);
            Append(builder, DepthLatencyMs);
            Append(builder, RaycastFusionLatencyMs);
            Append(builder, TrackingLatencyMs);
            Append(builder, TotalLatencyMs);
            Append(builder, DepthOcclusionUsable);
            Append(builder, FadeFallbackActive);
            Append(builder, OutputOpacity);
            Append(builder, Success);
            Append(builder, FailureReason);
            AppendLast(builder, ResultSource);
            return builder.ToString();
        }

        private static Vector3 NaNVector()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }

        private static void Append(StringBuilder builder, string value)
        {
            AppendLast(builder, value);
            builder.Append(',');
        }

        private static void Append(StringBuilder builder, long value)
        {
            Append(builder, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Append(StringBuilder builder, int value)
        {
            Append(builder, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Append(StringBuilder builder, float value)
        {
            Append(builder, float.IsNaN(value)
                ? string.Empty
                : value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void Append(StringBuilder builder, bool value)
        {
            Append(builder, value ? "true" : "false");
        }

        private static void AppendLast(StringBuilder builder, string value)
        {
            string safeValue = value ?? string.Empty;
            bool needsQuotes = safeValue.IndexOfAny(CsvQuoteCharacters) >= 0;
            if (!needsQuotes)
            {
                builder.Append(safeValue);
                return;
            }

            builder.Append('"');
            builder.Append(safeValue.Replace("\"", "\"\""));
            builder.Append('"');
        }
    }

    internal static class ExperimentClock
    {
        private static readonly double MillisecondsPerTick = 1000d / Stopwatch.Frequency;

        public static long Timestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        public static float ElapsedMilliseconds(long startTimestamp)
        {
            return (float)((Stopwatch.GetTimestamp() - startTimestamp) * MillisecondsPerTick);
        }
    }
}
