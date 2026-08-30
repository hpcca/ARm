using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace AR80sRetro.Experiments
{
    [DisallowMultipleComponent]
    public sealed class AprilTagGroundTruthRecorder : MonoBehaviour
    {
        [Serializable]
        private sealed class ReferenceManifest
        {
            public string schema_version;
            public string reference_role;
            public string provider;
            public string provider_version;
            public string tag_family;
            public int tag_id;
            public float tag_size_m;
            public int decimation;
            public float sample_interval_s;
            public int maximum_image_width;
            public string image_transform;
            public string screen_orientation;
            public string camera_frame_rotation;
            public string camera_coordinate_transform_id;
            public float camera_coordinate_rotation_x;
            public float camera_coordinate_rotation_y;
            public float camera_coordinate_rotation_z;
            public float camera_coordinate_rotation_w;
            public string camera_frame_definition;
            public string depth_uv_transform_id;
            public float tag_from_object_position_x;
            public float tag_from_object_position_y;
            public float tag_from_object_position_z;
            public float tag_from_object_rotation_x;
            public float tag_from_object_rotation_y;
            public float tag_from_object_rotation_z;
            public float tag_from_object_rotation_w;
            public string calibration_id;
            public string tag_frame_definition;
            public string model_alignment_id;
            public float model_from_object_position_x;
            public float model_from_object_position_y;
            public float model_from_object_position_z;
            public float model_from_object_rotation_x;
            public float model_from_object_rotation_y;
            public float model_from_object_rotation_z;
            public float model_from_object_rotation_w;
            public string model_from_object_translation_units;
            public string rendered_object_transform_formula;
            public float tag_size_measurement_uncertainty_m;
            public float tag_to_object_translation_uncertainty_m;
            public float tag_to_object_yaw_uncertainty_deg;
            public float max_fx_fy_relative_delta;
            public float max_principal_offset_fraction;
            public string transform_notation;
            public string object_frame_definition;
            public string pose_estimator_limitation;
            public string created_utc;
        }

        private const string TagFamily = "tagStandard41h12";
        private const string ProviderName = "jp.keijiro.apriltag";
        private const string ProviderVersion = "1.0.3";

        [Header("Explicit Dependencies")]
        [SerializeField] private ARReplacementExperimentConfig experimentConfig;
        [SerializeField] private ARReplacementExperimentLogger experimentLogger;
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private ARCameraFrameProvider frameProvider;

        [Header("Evaluation-only AprilTag Reference")]
        [SerializeField, Min(0)] private int targetTagId;
        [SerializeField, Min(0.001f)] private float tagSizeMeters = 0.08f;
        [SerializeField, Min(1)] private int decimation = 2;
        [SerializeField, Min(0.05f)] private float sampleIntervalSeconds = 0.25f;
        [SerializeField, Min(160)] private int maximumImageWidth = 960;

        [Header("Rigid Transform ^Tag T_Object")]
        [Tooltip("Object-frame origin expressed in the AprilTag frame, in metres.")]
        [SerializeField] private Vector3 tagFromObjectPositionMeters;
        [Tooltip("Object-frame orientation expressed in the AprilTag frame.")]
        [SerializeField] private Vector3 tagFromObjectEulerDegrees;

        [Header("Recorded Model Alignment ^Model T_Object (Never Applied at Runtime)")]
        [Tooltip("Object-frame origin in unscaled prefab-local units. This is metadata only.")]
        [SerializeField] private Vector3 modelFromObjectPositionLocalUnits;
        [Tooltip("Object-frame orientation expressed in the prefab Transform frame. Metadata only.")]
        [SerializeField] private Vector3 modelFromObjectEulerDegrees;
        [SerializeField] private string modelAlignmentId = "UNSET";

        [Header("Calibration Provenance")]
        [SerializeField] private string calibrationId = "UNSET";
        [SerializeField, Min(0f)] private float tagSizeMeasurementUncertaintyMeters = 0.0002f;
        [SerializeField, Min(0f)] private float tagToObjectTranslationUncertaintyMeters = 0.001f;
        [SerializeField, Min(0f)] private float tagToObjectYawUncertaintyDegrees = 0.5f;

        [Header("Validity Gates for the Upstream FOV-only Pose API")]
        [SerializeField, Range(0f, 0.2f)] private float maxFxFyRelativeDelta = 0.01f;
        [SerializeField, Range(0f, 0.2f)] private float maxPrincipalOffsetFraction = 0.01f;

        [Header("Buffered Reference Output")]
        [SerializeField, Min(1)] private int recordsPerBatch = 16;
        [SerializeField, Min(0.25f)] private float flushIntervalSeconds = 2f;

        private readonly List<string> pendingCsvLines = new List<string>(32);
        private readonly object writeLock = new object();
        private Task pendingWrite = Task.CompletedTask;
        private AprilTag.TagDetector detector;
        private Texture2D conversionTexture;
        private Color32[] pixelBuffer;
        private string activeOutputDirectory;
        private string referenceCsvPath;
        private string resolvedSessionId;
        private volatile string backgroundWriteError;
        private bool writeErrorReported;
        private int detectorWidth;
        private int detectorHeight;
        private long nextSampleId;
        private float nextSampleTime;
        private float nextFlushTime;

        private void Reset()
        {
            experimentConfig = FindObjectOfType<ARReplacementExperimentConfig>();
            experimentLogger = FindObjectOfType<ARReplacementExperimentLogger>();
            cameraManager = FindObjectOfType<ARCameraManager>();
            arCamera = Camera.main;
            frameProvider = FindObjectOfType<ARCameraFrameProvider>();
        }

        private void Awake()
        {
            if (frameProvider == null)
            {
                frameProvider = FindObjectOfType<ARCameraFrameProvider>();
            }
        }

        private void Update()
        {
            if (pendingCsvLines.Count > 0 && Time.unscaledTime >= nextFlushTime)
            {
                QueuePendingWrite();
            }

            if (!writeErrorReported && !string.IsNullOrEmpty(backgroundWriteError))
            {
                writeErrorReported = true;
                Debug.LogError($"AprilTag reference log write failed: {backgroundWriteError}", this);
            }
        }

        private void LateUpdate()
        {
            if (!IsReferenceLoggingRequested() || !TryEnsureOutput())
            {
                return;
            }

            if (Time.unscaledTime < nextSampleTime)
            {
                return;
            }

            nextSampleTime = Time.unscaledTime + sampleIntervalSeconds;
            CaptureReferenceSample();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                FlushSynchronously();
            }
        }

        private void OnDisable()
        {
            FlushSynchronously();
            DisposeDetectorResources();
        }

        private void OnDestroy()
        {
            FlushSynchronously();
            DisposeDetectorResources();
        }

        private bool IsReferenceLoggingRequested()
        {
            return experimentConfig != null
                && experimentConfig.LoggingEnabled
                && experimentLogger != null;
        }

        private bool TryEnsureOutput()
        {
            string requestedDirectory = experimentLogger.OutputDirectory;
            if (string.IsNullOrEmpty(requestedDirectory))
            {
                return false;
            }

            if (string.Equals(
                requestedDirectory,
                activeOutputDirectory,
                StringComparison.Ordinal))
            {
                return true;
            }

            FlushSynchronously();
            activeOutputDirectory = requestedDirectory;
            resolvedSessionId = new DirectoryInfo(activeOutputDirectory).Name;
            referenceCsvPath = Path.Combine(activeOutputDirectory, "reference_poses.csv");
            File.WriteAllText(
                referenceCsvPath,
                AprilTagReferenceRecord.CsvHeader + Environment.NewLine,
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(activeOutputDirectory, "reference_config.json"),
                JsonUtility.ToJson(CreateReferenceManifest(), true),
                new UTF8Encoding(false));

            pendingCsvLines.Clear();
            backgroundWriteError = null;
            writeErrorReported = false;
            nextSampleId = 0;
            nextSampleTime = Time.unscaledTime;
            nextFlushTime = Time.unscaledTime + flushIntervalSeconds;
            Debug.Log($"AprilTag evaluation reference logging started: {referenceCsvPath}", this);
            return true;
        }

        private ReferenceManifest CreateReferenceManifest()
        {
            Quaternion tagFromObjectRotation = Quaternion.Euler(tagFromObjectEulerDegrees);
            Quaternion modelFromObjectRotation = Quaternion.Euler(modelFromObjectEulerDegrees);
            Quaternion cameraCoordinateRotation = frameProvider != null
                ? frameProvider.CpuImageToUnityCameraRotation
                : Quaternion.identity;
            return new ReferenceManifest
            {
                schema_version = "route_a_apriltag_reference_v2",
                reference_role = "evaluation_only_no_algorithm_feedback",
                provider = ProviderName,
                provider_version = ProviderVersion,
                tag_family = TagFamily,
                tag_id = targetTagId,
                tag_size_m = tagSizeMeters,
                decimation = decimation,
                sample_interval_s = sampleIntervalSeconds,
                maximum_image_width = maximumImageWidth,
                image_transform =
                    "XRCpuImage.MirrorY_then_AprilTag_vertical_flip_then_" +
                    "recorded_pose_rotated_into_Unity_camera_frame",
                screen_orientation = Screen.orientation.ToString(),
                camera_frame_rotation = frameProvider != null
                    ? frameProvider.ConfiguredFrameRotation.ToString()
                    : "Not configured",
                camera_coordinate_transform_id = frameProvider != null
                    ? frameProvider.CpuImageToUnityCameraTransformId
                    : "Not configured",
                camera_coordinate_rotation_x = cameraCoordinateRotation.x,
                camera_coordinate_rotation_y = cameraCoordinateRotation.y,
                camera_coordinate_rotation_z = cameraCoordinateRotation.z,
                camera_coordinate_rotation_w = cameraCoordinateRotation.w,
                camera_frame_definition =
                    "Unity AR Camera local frame: +X screen-right, +Y screen-up, +Z forward",
                depth_uv_transform_id = frameProvider != null
                    ? frameProvider.DepthUvTransformId
                    : "Not configured",
                tag_from_object_position_x = tagFromObjectPositionMeters.x,
                tag_from_object_position_y = tagFromObjectPositionMeters.y,
                tag_from_object_position_z = tagFromObjectPositionMeters.z,
                tag_from_object_rotation_x = tagFromObjectRotation.x,
                tag_from_object_rotation_y = tagFromObjectRotation.y,
                tag_from_object_rotation_z = tagFromObjectRotation.z,
                tag_from_object_rotation_w = tagFromObjectRotation.w,
                calibration_id = string.IsNullOrWhiteSpace(calibrationId)
                    ? "UNSET"
                    : calibrationId.Trim(),
                tag_frame_definition =
                    "Provider-local tag frame: origin at detection-edge centre; " +
                    "+X toward printed-image right; +Y toward printed-image top; " +
                    "+Z into the tag board; verify signs with the physical axis sanity test",
                model_alignment_id = string.IsNullOrWhiteSpace(modelAlignmentId)
                    ? "UNSET"
                    : modelAlignmentId.Trim(),
                model_from_object_position_x = modelFromObjectPositionLocalUnits.x,
                model_from_object_position_y = modelFromObjectPositionLocalUnits.y,
                model_from_object_position_z = modelFromObjectPositionLocalUnits.z,
                model_from_object_rotation_x = modelFromObjectRotation.x,
                model_from_object_rotation_y = modelFromObjectRotation.y,
                model_from_object_rotation_z = modelFromObjectRotation.z,
                model_from_object_rotation_w = modelFromObjectRotation.w,
                model_from_object_translation_units =
                    "unscaled_prefab_local_units_before_output_scale",
                rendered_object_transform_formula =
                    "^W T_O,rendered = ^W T_M(output TRS) * ^M T_O",
                tag_size_measurement_uncertainty_m = tagSizeMeasurementUncertaintyMeters,
                tag_to_object_translation_uncertainty_m =
                    tagToObjectTranslationUncertaintyMeters,
                tag_to_object_yaw_uncertainty_deg = tagToObjectYawUncertaintyDegrees,
                max_fx_fy_relative_delta = maxFxFyRelativeDelta,
                max_principal_offset_fraction = maxPrincipalOffsetFraction,
                transform_notation = "^A T_B maps B-frame coordinates into A-frame coordinates",
                object_frame_definition =
                    "Cup: origin at bottom contact-centre; +Y up; +Z from cup axis toward handle",
                pose_estimator_limitation =
                    "Provider 1.0.3 assumes fx=fy and principal point at image centre; " +
                    "intrinsics_gate_passed must be true before using a sample as operational GT",
                created_utc = DateTime.UtcNow.ToString("O")
            };
        }

        private void CaptureReferenceSample()
        {
            long sampleStartTimestamp = ExperimentClock.Timestamp();
            AprilTagReferenceRecord record = CreateBaseRecord(sampleStartTimestamp);

            if (cameraManager == null || arCamera == null || frameProvider == null)
            {
                string failureReason = cameraManager == null
                    ? "camera_manager_missing"
                    : arCamera == null
                        ? "ar_camera_missing"
                        : "camera_frame_provider_missing";
                CompleteRecord(
                    record,
                    sampleStartTimestamp,
                    false,
                    failureReason);
                return;
            }

            Transform cameraTransform = arCamera.transform;
            record.CameraWorldPosition = cameraTransform.position;
            record.CameraWorldRotation = cameraTransform.rotation;

            if (!cameraManager.TryGetIntrinsics(out XRCameraIntrinsics intrinsics))
            {
                CompleteRecord(
                    record,
                    sampleStartTimestamp,
                    false,
                    "camera_intrinsics_unavailable");
                return;
            }

            record.IntrinsicsAvailable = true;
            record.IntrinsicsWidth = intrinsics.resolution.x;
            record.IntrinsicsHeight = intrinsics.resolution.y;

            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            {
                CompleteRecord(
                    record,
                    sampleStartTimestamp,
                    false,
                    "camera_cpu_image_unavailable");
                return;
            }

            using (image)
            {
                record.CpuImageTimestampSeconds = image.timestamp;
                float downscale = maximumImageWidth > 0
                    ? Mathf.Min(1f, (float)maximumImageWidth / image.width)
                    : 1f;
                int outputWidth = Mathf.Max(1, Mathf.RoundToInt(image.width * downscale));
                int outputHeight = Mathf.Max(1, Mathf.RoundToInt(image.height * downscale));
                record.ImageWidth = outputWidth;
                record.ImageHeight = outputHeight;

                if (intrinsics.resolution.x <= 0 || intrinsics.resolution.y <= 0)
                {
                    CompleteRecord(
                        record,
                        sampleStartTimestamp,
                        false,
                        "camera_intrinsics_resolution_invalid");
                    return;
                }

                float imageAspect = (float)image.width / image.height;
                float intrinsicsAspect =
                    (float)intrinsics.resolution.x / intrinsics.resolution.y;
                if (Mathf.Abs(imageAspect - intrinsicsAspect) > 0.01f)
                {
                    CompleteRecord(
                        record,
                        sampleStartTimestamp,
                        false,
                        "intrinsics_image_aspect_mismatch");
                    return;
                }

                float scaleX = (float)outputWidth / intrinsics.resolution.x;
                float scaleY = (float)outputHeight / intrinsics.resolution.y;
                record.FxPixels = intrinsics.focalLength.x * scaleX;
                record.FyPixels = intrinsics.focalLength.y * scaleY;
                record.CxPixels = intrinsics.principalPoint.x * scaleX;
                record.CyPixels = intrinsics.principalPoint.y * scaleY;
                float meanFocalLength = (record.FxPixels + record.FyPixels) * 0.5f;
                record.FxFyRelativeDelta = meanFocalLength > Mathf.Epsilon
                    ? Mathf.Abs(record.FxPixels - record.FyPixels) / meanFocalLength
                    : float.NaN;
                record.PrincipalOffsetXFraction =
                    Mathf.Abs(record.CxPixels - outputWidth * 0.5f) / outputWidth;
                record.PrincipalOffsetYFraction =
                    Mathf.Abs(record.CyPixels - outputHeight * 0.5f) / outputHeight;
                record.IntrinsicsGatePassed =
                    record.FxFyRelativeDelta <= maxFxFyRelativeDelta
                    && record.PrincipalOffsetXFraction <= maxPrincipalOffsetFraction
                    && record.PrincipalOffsetYFraction <= maxPrincipalOffsetFraction;

                if (record.FyPixels <= Mathf.Epsilon)
                {
                    CompleteRecord(
                        record,
                        sampleStartTimestamp,
                        false,
                        "camera_focal_length_invalid");
                    return;
                }

                EnsureDetectorResources(outputWidth, outputHeight);
                XRCpuImage.ConversionParams conversionParams =
                    new XRCpuImage.ConversionParams
                    {
                        inputRect = new RectInt(0, 0, image.width, image.height),
                        outputDimensions = new Vector2Int(outputWidth, outputHeight),
                        outputFormat = TextureFormat.RGBA32,
                        transformation = XRCpuImage.Transformation.MirrorY
                    };
                int convertedDataSize = image.GetConvertedDataSize(conversionParams);
                NativeArray<byte> convertedData =
                    new NativeArray<byte>(convertedDataSize, Allocator.Temp);
                try
                {
                    image.Convert(conversionParams, convertedData);
                    conversionTexture.LoadRawTextureData(convertedData);
                    conversionTexture.Apply(false, false);
                }
                finally
                {
                    convertedData.Dispose();
                }

                conversionTexture.GetRawTextureData<Color32>().CopyTo(pixelBuffer);
                float verticalFovRadians = 2f * Mathf.Atan(
                    outputHeight * 0.5f / record.FyPixels);
                detector.ProcessImage(pixelBuffer, verticalFovRadians, tagSizeMeters);

                bool foundTarget = false;
                AprilTag.TagPose selectedTag = default;
                int tagCount = 0;
                foreach (AprilTag.TagPose tag in detector.DetectedTags)
                {
                    tagCount++;
                    if (!foundTarget && tag.ID == targetTagId)
                    {
                        foundTarget = true;
                        selectedTag = tag;
                    }
                }

                record.DetectedTagCount = tagCount;
                record.TagDetected = foundTarget;
                if (!foundTarget)
                {
                    CompleteRecord(
                        record,
                        sampleStartTimestamp,
                        false,
                        "target_tag_not_detected");
                    return;
                }

                FillReferencePoses(record, selectedTag, cameraTransform);
                if (!record.IntrinsicsGatePassed)
                {
                    record.ResultSource = "Invalid";
                    CompleteRecord(
                        record,
                        sampleStartTimestamp,
                        false,
                        "intrinsics_model_gate_failed");
                    return;
                }

                CompleteRecord(record, sampleStartTimestamp, true, string.Empty);
            }
        }

        private AprilTagReferenceRecord CreateBaseRecord(long sampleStartTimestamp)
        {
            ExperimentSessionConfig session = experimentConfig.Session
                ?? new ExperimentSessionConfig();
            return new AprilTagReferenceRecord
            {
                SessionId = resolvedSessionId,
                TrialId = session.TrialId,
                SampleId = ++nextSampleId,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MonotonicTimestampMs = ExperimentClock.TimestampMilliseconds(
                    sampleStartTimestamp),
                TagFamily = TagFamily,
                TargetTagId = targetTagId,
                TagSizeMeters = tagSizeMeters,
                Decimation = decimation
            };
        }

        private void FillReferencePoses(
            AprilTagReferenceRecord record,
            AprilTag.TagPose tag,
            Transform cameraTransform)
        {
            Quaternion cpuImageToUnityCamera =
                frameProvider.CpuImageToUnityCameraRotation;
            record.TagCameraPosition = cpuImageToUnityCamera * tag.Position;
            record.TagCameraRotation = cpuImageToUnityCamera * tag.Rotation;
            record.TagWorldPosition = cameraTransform.TransformPoint(
                record.TagCameraPosition);
            record.TagWorldRotation =
                cameraTransform.rotation * record.TagCameraRotation;

            Quaternion tagFromObjectRotation = Quaternion.Euler(tagFromObjectEulerDegrees);
            record.ObjectGroundTruthCameraPosition =
                record.TagCameraPosition
                + record.TagCameraRotation * tagFromObjectPositionMeters;
            record.ObjectGroundTruthCameraRotation =
                record.TagCameraRotation * tagFromObjectRotation;
            record.ObjectGroundTruthWorldPosition = cameraTransform.TransformPoint(
                record.ObjectGroundTruthCameraPosition);
            record.ObjectGroundTruthWorldRotation =
                cameraTransform.rotation * record.ObjectGroundTruthCameraRotation;
            record.ObjectGroundTruthWorldYawDegrees =
                record.ObjectGroundTruthWorldRotation.eulerAngles.y;
        }

        private void CompleteRecord(
            AprilTagReferenceRecord record,
            long sampleStartTimestamp,
            bool success,
            string failureReason)
        {
            record.ReferenceLatencyMs = ExperimentClock.ElapsedMilliseconds(
                sampleStartTimestamp);
            record.Success = success;
            record.FailureReason = success ? string.Empty : failureReason;
            pendingCsvLines.Add(record.ToCsvLine());
            if (pendingCsvLines.Count >= recordsPerBatch)
            {
                QueuePendingWrite();
            }
        }

        private void EnsureDetectorResources(int width, int height)
        {
            if (detector != null && detectorWidth == width && detectorHeight == height)
            {
                return;
            }

            DisposeDetectorResources();
            detector = new AprilTag.TagDetector(width, height, Mathf.Max(1, decimation));
            detectorWidth = width;
            detectorHeight = height;
            conversionTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            conversionTexture.name = "AprilTag Reference CPU Frame";
            pixelBuffer = new Color32[width * height];
        }

        private void DisposeDetectorResources()
        {
            detector?.Dispose();
            detector = null;
            detectorWidth = 0;
            detectorHeight = 0;
            pixelBuffer = null;
            if (conversionTexture != null)
            {
                Destroy(conversionTexture);
                conversionTexture = null;
            }
        }

        private void QueuePendingWrite()
        {
            if (pendingCsvLines.Count == 0 || string.IsNullOrEmpty(referenceCsvPath))
            {
                return;
            }

            string payload = string.Join(Environment.NewLine, pendingCsvLines)
                + Environment.NewLine;
            pendingCsvLines.Clear();
            nextFlushTime = Time.unscaledTime + flushIntervalSeconds;
            string targetPath = referenceCsvPath;
            lock (writeLock)
            {
                pendingWrite = pendingWrite.ContinueWith(
                    _ =>
                    {
                        try
                        {
                            File.AppendAllText(targetPath, payload, new UTF8Encoding(false));
                        }
                        catch (Exception exception)
                        {
                            backgroundWriteError = exception.ToString();
                        }
                    },
                    TaskScheduler.Default);
            }
        }

        private void FlushSynchronously()
        {
            QueuePendingWrite();
            Task writeToWaitFor;
            lock (writeLock)
            {
                writeToWaitFor = pendingWrite;
            }

            try
            {
                writeToWaitFor?.Wait();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }
}
