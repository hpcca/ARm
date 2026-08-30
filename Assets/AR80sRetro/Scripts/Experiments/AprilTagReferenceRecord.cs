using System.Globalization;
using System.Text;
using UnityEngine;

namespace AR80sRetro.Experiments
{
    public sealed class AprilTagReferenceRecord
    {
        private static readonly char[] CsvQuoteCharacters = { ',', '"', '\r', '\n' };

        public const string CsvHeader =
            "session_id,trial_id,reference_sample_id,timestamp_ms,monotonic_timestamp_ms," +
            "cpu_image_timestamp_s,tag_family,target_tag_id,tag_size_m,decimation," +
            "image_width,image_height,intrinsics_available,intrinsics_width," +
            "intrinsics_height,fx_px,fy_px,cx_px,cy_px,fx_fy_relative_delta," +
            "principal_offset_x_fraction,principal_offset_y_fraction," +
            "intrinsics_gate_passed,camera_world_position_x,camera_world_position_y," +
            "camera_world_position_z,camera_world_rotation_x,camera_world_rotation_y," +
            "camera_world_rotation_z,camera_world_rotation_w,tag_detected," +
            "detected_tag_count,tag_camera_position_x,tag_camera_position_y," +
            "tag_camera_position_z,tag_camera_rotation_x,tag_camera_rotation_y," +
            "tag_camera_rotation_z,tag_camera_rotation_w,tag_world_position_x," +
            "tag_world_position_y,tag_world_position_z,tag_world_rotation_x," +
            "tag_world_rotation_y,tag_world_rotation_z,tag_world_rotation_w," +
            "object_gt_camera_position_x,object_gt_camera_position_y," +
            "object_gt_camera_position_z,object_gt_camera_rotation_x," +
            "object_gt_camera_rotation_y,object_gt_camera_rotation_z," +
            "object_gt_camera_rotation_w,object_gt_world_position_x," +
            "object_gt_world_position_y,object_gt_world_position_z," +
            "object_gt_world_rotation_x,object_gt_world_rotation_y," +
            "object_gt_world_rotation_z,object_gt_world_rotation_w," +
            "object_gt_world_yaw_deg,reference_latency_ms,success,failure_reason," +
            "result_source";

        public string SessionId;
        public string TrialId;
        public long SampleId;
        public long TimestampMs;
        public double MonotonicTimestampMs = double.NaN;
        public double CpuImageTimestampSeconds = double.NaN;
        public string TagFamily = "tagStandard41h12";
        public int TargetTagId;
        public float TagSizeMeters = float.NaN;
        public int Decimation;
        public int ImageWidth;
        public int ImageHeight;
        public bool IntrinsicsAvailable;
        public int IntrinsicsWidth;
        public int IntrinsicsHeight;
        public float FxPixels = float.NaN;
        public float FyPixels = float.NaN;
        public float CxPixels = float.NaN;
        public float CyPixels = float.NaN;
        public float FxFyRelativeDelta = float.NaN;
        public float PrincipalOffsetXFraction = float.NaN;
        public float PrincipalOffsetYFraction = float.NaN;
        public bool IntrinsicsGatePassed;
        public Vector3 CameraWorldPosition = NaNVector();
        public Quaternion CameraWorldRotation = NaNQuaternion();
        public bool TagDetected;
        public int DetectedTagCount;
        public Vector3 TagCameraPosition = NaNVector();
        public Quaternion TagCameraRotation = NaNQuaternion();
        public Vector3 TagWorldPosition = NaNVector();
        public Quaternion TagWorldRotation = NaNQuaternion();
        public Vector3 ObjectGroundTruthCameraPosition = NaNVector();
        public Quaternion ObjectGroundTruthCameraRotation = NaNQuaternion();
        public Vector3 ObjectGroundTruthWorldPosition = NaNVector();
        public Quaternion ObjectGroundTruthWorldRotation = NaNQuaternion();
        public float ObjectGroundTruthWorldYawDegrees = float.NaN;
        public float ReferenceLatencyMs;
        public bool Success;
        public string FailureReason;
        public string ResultSource = "Measured";

        public string ToCsvLine()
        {
            StringBuilder builder = new StringBuilder(1536);
            Append(builder, SessionId);
            Append(builder, TrialId);
            Append(builder, SampleId);
            Append(builder, TimestampMs);
            Append(builder, MonotonicTimestampMs);
            Append(builder, CpuImageTimestampSeconds);
            Append(builder, TagFamily);
            Append(builder, TargetTagId);
            Append(builder, TagSizeMeters);
            Append(builder, Decimation);
            Append(builder, ImageWidth);
            Append(builder, ImageHeight);
            Append(builder, IntrinsicsAvailable);
            Append(builder, IntrinsicsWidth);
            Append(builder, IntrinsicsHeight);
            Append(builder, FxPixels);
            Append(builder, FyPixels);
            Append(builder, CxPixels);
            Append(builder, CyPixels);
            Append(builder, FxFyRelativeDelta);
            Append(builder, PrincipalOffsetXFraction);
            Append(builder, PrincipalOffsetYFraction);
            Append(builder, IntrinsicsGatePassed);
            Append(builder, CameraWorldPosition.x);
            Append(builder, CameraWorldPosition.y);
            Append(builder, CameraWorldPosition.z);
            Append(builder, CameraWorldRotation.x);
            Append(builder, CameraWorldRotation.y);
            Append(builder, CameraWorldRotation.z);
            Append(builder, CameraWorldRotation.w);
            Append(builder, TagDetected);
            Append(builder, DetectedTagCount);
            Append(builder, TagCameraPosition.x);
            Append(builder, TagCameraPosition.y);
            Append(builder, TagCameraPosition.z);
            Append(builder, TagCameraRotation.x);
            Append(builder, TagCameraRotation.y);
            Append(builder, TagCameraRotation.z);
            Append(builder, TagCameraRotation.w);
            Append(builder, TagWorldPosition.x);
            Append(builder, TagWorldPosition.y);
            Append(builder, TagWorldPosition.z);
            Append(builder, TagWorldRotation.x);
            Append(builder, TagWorldRotation.y);
            Append(builder, TagWorldRotation.z);
            Append(builder, TagWorldRotation.w);
            Append(builder, ObjectGroundTruthCameraPosition.x);
            Append(builder, ObjectGroundTruthCameraPosition.y);
            Append(builder, ObjectGroundTruthCameraPosition.z);
            Append(builder, ObjectGroundTruthCameraRotation.x);
            Append(builder, ObjectGroundTruthCameraRotation.y);
            Append(builder, ObjectGroundTruthCameraRotation.z);
            Append(builder, ObjectGroundTruthCameraRotation.w);
            Append(builder, ObjectGroundTruthWorldPosition.x);
            Append(builder, ObjectGroundTruthWorldPosition.y);
            Append(builder, ObjectGroundTruthWorldPosition.z);
            Append(builder, ObjectGroundTruthWorldRotation.x);
            Append(builder, ObjectGroundTruthWorldRotation.y);
            Append(builder, ObjectGroundTruthWorldRotation.z);
            Append(builder, ObjectGroundTruthWorldRotation.w);
            Append(builder, ObjectGroundTruthWorldYawDegrees);
            Append(builder, ReferenceLatencyMs);
            Append(builder, Success);
            Append(builder, FailureReason);
            AppendLast(builder, ResultSource);
            return builder.ToString();
        }

        private static Vector3 NaNVector()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }

        private static Quaternion NaNQuaternion()
        {
            return new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
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
            Append(builder, float.IsNaN(value) || float.IsInfinity(value)
                ? string.Empty
                : value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void Append(StringBuilder builder, double value)
        {
            Append(builder, double.IsNaN(value) || double.IsInfinity(value)
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
            if (safeValue.IndexOfAny(CsvQuoteCharacters) < 0)
            {
                builder.Append(safeValue);
                return;
            }

            builder.Append('"');
            builder.Append(safeValue.Replace("\"", "\"\""));
            builder.Append('"');
        }
    }
}
