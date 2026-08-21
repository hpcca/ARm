using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace AR80sRetro.Experiments
{
    [DisallowMultipleComponent]
    public sealed class ARReplacementExperimentLogger : MonoBehaviour
    {
        [Serializable]
        private sealed class SessionManifest
        {
            public string session_id;
            public string trial_id;
            public string object_id;
            public int expected_object_count;
            public string scene_id;
            public string condition_id;
            public string ablation_mode;
            public string build_commit_sha;
            public string build_commit_source;
            public string route_a_base_commit;
            public string route_a_fade_baseline_commit;
            public string unity_version;
            public string ar_foundation_version;
            public string arcore_version;
            public string sentis_version;
            public string application_identifier;
            public string application_version;
            public string device_model;
            public string operating_system;
            public string graphics_device_name;
            public string inference_backend;
            public string yolo_model_identifier;
            public int input_width;
            public int input_height;
            public float confidence_threshold;
            public float iou_threshold;
            public int max_detections;
            public float inference_interval_seconds;
            public string depth_mode;
            public bool depth_temporal_smoothing;
            public int depth_sample_grid_size;
            public float depth_sample_radius_normalized;
            public float depth_min_m;
            public float depth_max_m;
            public int depth_confidence_threshold;
            public float depth_availability_grace_seconds;
            public float depth_horizontal_fusion_weight;
            public float max_depth_plane_horizontal_delta_m;
            public bool depth_position_fusion_enabled;
            public bool temporal_tracking_enabled;
            public bool occlusion_fade_fallback_enabled;
            public float lost_grace_seconds;
            public float lost_state_delay_seconds;
            public float reacquire_match_radius_m;
            public float duplicate_radius_m;
            public float movement_dead_zone_m;
            public int movement_confirmation_frames;
            public float movement_smooth_time_seconds;
            public float depth_startup_grace_seconds;
            public float fade_delay_seconds;
            public float fade_duration_seconds;
            public float fade_minimum_opacity;
            public ReplacementRuleManifest[] replacement_rules;
            public string distance_condition;
            public string view_condition;
            public float occlusion_percent;
            public string lighting_condition;
            public string scene_description;
            public string created_utc;
        }

        [Serializable]
        private sealed class ReplacementRuleManifest
        {
            public string class_label;
            public float placement_min_confidence;
            public float tracking_min_confidence;
            public int confirmation_frames;
            public float vertical_offset_m;
            public float raycast_anchor_x;
            public float raycast_anchor_y;
            public float rotation_offset_yaw_deg;
            public float spawn_scale_x;
            public float spawn_scale_y;
            public float spawn_scale_z;
            public bool estimate_scale_from_bbox;
            public string bbox_scale_axis;
            public float scale_calibration_multiplier;
            public float estimated_height_multiplier;
            public float estimated_width_multiplier;
            public float scale_multiplier_min;
            public float scale_multiplier_max;
        }

        [Header("Explicit Dependencies")]
        [SerializeField] private ARReplacementExperimentConfig experimentConfig;
        [SerializeField] private RetroDetectionPipeline pipeline;
        [SerializeField] private YoloObjectDetector detector;
        [SerializeField] private ARDepthFrameProvider depthProvider;
        [SerializeField] private ARRaycastPositionSolver positionSolver;
        [SerializeField] private RetroReplacementManager replacementManager;

        [Header("Buffered Output")]
        [SerializeField, Min(1)] private int recordsPerBatch = 128;
        [SerializeField, Min(0.25f)] private float flushIntervalSeconds = 2f;

        private readonly List<string> pendingCsvLines = new List<string>(128);
        private readonly object writeLock = new object();
        private Task pendingWrite = Task.CompletedTask;
        private string csvPath;
        private string outputDirectory;
        private string resolvedSessionId;
        private volatile string backgroundWriteError;
        private float nextFlushTime;
        private bool sessionActive;
        private bool writeErrorReported;

        public string OutputDirectory => outputDirectory;

        private void OnEnable()
        {
            if (pipeline != null)
            {
                pipeline.ExperimentRecordReady += HandleRecordReady;
            }
        }

        private void Start()
        {
            if (IsLoggingRequested())
            {
                BeginSession();
            }
        }

        private void Update()
        {
            bool loggingRequested = IsLoggingRequested();
            if (loggingRequested && !sessionActive)
            {
                BeginSession();
            }
            else if (!loggingRequested && sessionActive)
            {
                EndSession();
            }

            if (sessionActive
                && pendingCsvLines.Count > 0
                && (pendingCsvLines.Count >= recordsPerBatch || Time.unscaledTime >= nextFlushTime))
            {
                QueuePendingWrite();
            }

            if (!writeErrorReported && !string.IsNullOrEmpty(backgroundWriteError))
            {
                writeErrorReported = true;
                Debug.LogError($"Route A experiment log write failed: {backgroundWriteError}", this);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && sessionActive)
            {
                FlushSynchronously();
            }
        }

        private void OnDisable()
        {
            if (pipeline != null)
            {
                pipeline.ExperimentRecordReady -= HandleRecordReady;
            }

            EndSession();
        }

        private void OnApplicationQuit()
        {
            EndSession();
        }

        private bool IsLoggingRequested()
        {
            return experimentConfig != null && experimentConfig.LoggingEnabled;
        }

        private void BeginSession()
        {
            if (sessionActive || experimentConfig == null)
            {
                return;
            }

            try
            {
                ExperimentSessionConfig session = experimentConfig.Session
                    ?? new ExperimentSessionConfig();
                string requestedSessionId = SanitizePathSegment(session.SessionId);
                if (string.IsNullOrEmpty(requestedSessionId))
                {
                    requestedSessionId = $"route_a_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                }

                string rootDirectory = Path.Combine(
                    Application.persistentDataPath,
                    "AR80sRetroExperiments");
                outputDirectory = CreateUniqueSessionDirectory(
                    rootDirectory,
                    requestedSessionId,
                    out resolvedSessionId);
                csvPath = Path.Combine(outputDirectory, "frames.csv");
                File.WriteAllText(
                    csvPath,
                    ExperimentFrameRecord.CsvHeader + Environment.NewLine,
                    new UTF8Encoding(false));

                SessionManifest manifest = CreateManifest(session);
                File.WriteAllText(
                    Path.Combine(outputDirectory, "session.json"),
                    JsonUtility.ToJson(manifest, true),
                    new UTF8Encoding(false));

                pendingCsvLines.Clear();
                backgroundWriteError = null;
                writeErrorReported = false;
                nextFlushTime = Time.unscaledTime + flushIntervalSeconds;
                sessionActive = true;
                Debug.Log($"Route A experiment logging started: {outputDirectory}", this);
            }
            catch (Exception exception)
            {
                sessionActive = false;
                csvPath = null;
                outputDirectory = null;
                resolvedSessionId = null;
                Debug.LogException(exception, this);
            }
        }

        private SessionManifest CreateManifest(ExperimentSessionConfig session)
        {
            bool hasConfiguredCommit = !string.IsNullOrWhiteSpace(session.BuildCommitSha);
            return new SessionManifest
            {
                session_id = resolvedSessionId,
                trial_id = session.TrialId,
                object_id = session.ObjectId,
                expected_object_count = session.ExpectedObjectCount,
                scene_id = session.SceneId,
                condition_id = session.ConditionId,
                ablation_mode = experimentConfig.AblationModeId,
                build_commit_sha = hasConfiguredCommit ? session.BuildCommitSha.Trim() : "UNSET",
                build_commit_source = hasConfiguredCommit ? "Configured" : "Not configured",
                route_a_base_commit = "36bc1672c59520a935ea6806bdadd7ecb31c9b3f",
                route_a_fade_baseline_commit = "70ffa4591ce75ea3a37a9880f54c56ca2b22f719",
                unity_version = Application.unityVersion,
                ar_foundation_version = "5.2.0",
                arcore_version = "5.2.0",
                sentis_version = "2.1.3",
                application_identifier = Application.identifier,
                application_version = Application.version,
                device_model = SystemInfo.deviceModel,
                operating_system = SystemInfo.operatingSystem,
                graphics_device_name = SystemInfo.graphicsDeviceName,
                inference_backend = detector != null ? detector.ConfiguredBackend.ToString() : "Not configured",
                yolo_model_identifier = session.YoloModelIdentifier,
                input_width = detector != null ? detector.InputWidth : 0,
                input_height = detector != null ? detector.InputHeight : 0,
                confidence_threshold = detector != null ? detector.ConfidenceThreshold : 0f,
                iou_threshold = detector != null ? detector.IouThreshold : 0f,
                max_detections = detector != null ? detector.MaxDetections : 0,
                inference_interval_seconds = detector != null ? detector.InferenceIntervalSeconds : 0f,
                depth_mode = depthProvider != null
                    ? depthProvider.ConfiguredDepthMode.ToString()
                    : "Not configured",
                depth_temporal_smoothing = depthProvider != null
                    && depthProvider.TemporalSmoothingRequested,
                depth_sample_grid_size = depthProvider != null ? depthProvider.SampleGridSize : 0,
                depth_sample_radius_normalized = depthProvider != null
                    ? depthProvider.SampleRadiusNormalized
                    : 0f,
                depth_min_m = depthProvider != null ? depthProvider.MinimumDepthMeters : 0f,
                depth_max_m = depthProvider != null ? depthProvider.MaximumDepthMeters : 0f,
                depth_confidence_threshold = depthProvider != null
                    ? depthProvider.MinimumConfidence
                    : 0,
                depth_availability_grace_seconds = depthProvider != null
                    ? depthProvider.DepthAvailabilityGraceSeconds
                    : 0f,
                depth_horizontal_fusion_weight = positionSolver != null
                    ? positionSolver.DepthHorizontalWeight
                    : 0f,
                max_depth_plane_horizontal_delta_m = positionSolver != null
                    ? positionSolver.MaxDepthPlaneHorizontalDeltaMeters
                    : 0f,
                depth_position_fusion_enabled = experimentConfig.DepthPositionFusionEnabled,
                temporal_tracking_enabled = experimentConfig.TemporalTrackingEnabled,
                occlusion_fade_fallback_enabled = experimentConfig.OcclusionFadeFallbackEnabled,
                lost_grace_seconds = replacementManager != null
                    ? replacementManager.LostGraceSeconds
                    : 0f,
                lost_state_delay_seconds = replacementManager != null
                    ? replacementManager.LostStateDelaySeconds
                    : 0f,
                reacquire_match_radius_m = replacementManager != null
                    ? replacementManager.ReacquireMatchRadiusMeters
                    : 0f,
                duplicate_radius_m = replacementManager != null
                    ? replacementManager.DuplicateRadiusMeters
                    : 0f,
                movement_dead_zone_m = replacementManager != null
                    ? replacementManager.MovementDeadZoneMeters
                    : 0f,
                movement_confirmation_frames = replacementManager != null
                    ? replacementManager.MovementConfirmationFrames
                    : 0,
                movement_smooth_time_seconds = replacementManager != null
                    ? replacementManager.MovementSmoothTimeSeconds
                    : 0f,
                depth_startup_grace_seconds = replacementManager != null
                    ? replacementManager.DepthStartupGraceSeconds
                    : 0f,
                fade_delay_seconds = replacementManager != null
                    ? replacementManager.FallbackFadeDelaySeconds
                    : 0f,
                fade_duration_seconds = replacementManager != null
                    ? replacementManager.FallbackFadeDurationSeconds
                    : 0f,
                fade_minimum_opacity = replacementManager != null
                    ? replacementManager.FallbackMinimumOpacity
                    : 0f,
                replacement_rules = CreateReplacementRuleManifest(),
                distance_condition = session.DistanceCondition,
                view_condition = session.ViewCondition,
                occlusion_percent = session.OcclusionPercent,
                lighting_condition = session.LightingCondition,
                scene_description = session.SceneDescription,
                created_utc = DateTime.UtcNow.ToString("O")
            };
        }

        private ReplacementRuleManifest[] CreateReplacementRuleManifest()
        {
            if (replacementManager == null || replacementManager.PrefabLibrary == null)
            {
                return Array.Empty<ReplacementRuleManifest>();
            }

            IReadOnlyList<RetroReplacementRule> rules = replacementManager.PrefabLibrary.Rules;
            ReplacementRuleManifest[] manifests = new ReplacementRuleManifest[rules.Count];
            for (int i = 0; i < rules.Count; i++)
            {
                RetroReplacementRule rule = rules[i];
                if (rule == null)
                {
                    manifests[i] = new ReplacementRuleManifest();
                    continue;
                }

                Vector2 anchor = rule.RaycastAnchorInBoundingBox;
                Vector3 scale = rule.SpawnScale;
                Vector2 scaleRange = rule.ScaleMultiplierRange;
                manifests[i] = new ReplacementRuleManifest
                {
                    class_label = rule.DetectionLabel,
                    placement_min_confidence = rule.MinConfidence,
                    tracking_min_confidence = rule.TrackingMinConfidence,
                    confirmation_frames = rule.ConfirmationFrames,
                    vertical_offset_m = rule.VerticalOffsetMeters,
                    raycast_anchor_x = anchor.x,
                    raycast_anchor_y = anchor.y,
                    rotation_offset_yaw_deg = rule.RotationOffset.eulerAngles.y,
                    spawn_scale_x = scale.x,
                    spawn_scale_y = scale.y,
                    spawn_scale_z = scale.z,
                    estimate_scale_from_bbox = rule.EstimateScaleFromBoundingBox,
                    bbox_scale_axis = rule.BoundingBoxScaleAxis.ToString(),
                    scale_calibration_multiplier = rule.ScaleCalibrationMultiplier,
                    estimated_height_multiplier = rule.EstimatedHeightMultiplier,
                    estimated_width_multiplier = rule.EstimatedWidthMultiplier,
                    scale_multiplier_min = Mathf.Min(scaleRange.x, scaleRange.y),
                    scale_multiplier_max = Mathf.Max(scaleRange.x, scaleRange.y)
                };
            }

            return manifests;
        }

        private void HandleRecordReady(ExperimentFrameRecord record)
        {
            if (record == null || !IsLoggingRequested())
            {
                return;
            }

            if (!sessionActive)
            {
                BeginSession();
            }

            if (!sessionActive)
            {
                return;
            }

            record.SessionId = resolvedSessionId;
            pendingCsvLines.Add(record.ToCsvLine());
            if (pendingCsvLines.Count >= recordsPerBatch)
            {
                QueuePendingWrite();
            }
        }

        private void QueuePendingWrite()
        {
            if (pendingCsvLines.Count == 0 || string.IsNullOrEmpty(csvPath))
            {
                return;
            }

            string payload = string.Join(Environment.NewLine, pendingCsvLines)
                + Environment.NewLine;
            pendingCsvLines.Clear();
            nextFlushTime = Time.unscaledTime + flushIntervalSeconds;
            string targetPath = csvPath;

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

        private void EndSession()
        {
            if (!sessionActive)
            {
                return;
            }

            FlushSynchronously();
            sessionActive = false;
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(value.Length);
            string trimmedValue = value.Trim();
            for (int i = 0; i < trimmedValue.Length; i++)
            {
                char character = trimmedValue[i];
                builder.Append(Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character);
            }

            return builder.ToString();
        }

        private static string CreateUniqueSessionDirectory(
            string rootDirectory,
            string requestedSessionId,
            out string actualSessionId)
        {
            Directory.CreateDirectory(rootDirectory);
            string suffix = $"_run_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            actualSessionId = requestedSessionId;
            string candidateDirectory = Path.Combine(rootDirectory, actualSessionId);
            int collisionIndex = 0;
            while (Directory.Exists(candidateDirectory))
            {
                collisionIndex++;
                actualSessionId = requestedSessionId
                    + suffix
                    + (collisionIndex > 1 ? $"_{collisionIndex}" : string.Empty);
                candidateDirectory = Path.Combine(rootDirectory, actualSessionId);
            }

            Directory.CreateDirectory(candidateDirectory);
            return candidateDirectory;
        }
    }
}
