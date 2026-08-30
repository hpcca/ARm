using System;
using System.Collections.Generic;
using AR80sRetro.Experiments;
using UnityEngine;

namespace AR80sRetro
{
    public sealed class RetroDetectionPipeline : MonoBehaviour
    {
        [SerializeField] private YoloObjectDetector detector;
        [SerializeField] private RetroReplacementManager replacementManager;
        [SerializeField] private ARReplacementExperimentConfig experimentConfig;

        private readonly List<ExperimentFrameRecord> experimentRecords =
            new List<ExperimentFrameRecord>(16);

        public event Action<ExperimentFrameRecord> ExperimentRecordReady;

        private void Reset()
        {
            detector = FindObjectOfType<YoloObjectDetector>();
            replacementManager = FindObjectOfType<RetroReplacementManager>();
        }

        private void OnEnable()
        {
            if (detector != null)
            {
                detector.DetectionsReady += HandleDetectionsReady;
                detector.InferenceCycleFailed += HandleInferenceCycleFailed;
            }
        }

        private void OnDisable()
        {
            if (detector != null)
            {
                detector.DetectionsReady -= HandleDetectionsReady;
                detector.InferenceCycleFailed -= HandleInferenceCycleFailed;
            }
        }

        private void HandleDetectionsReady(IReadOnlyList<DetectionResult> detections)
        {
            if (!ShouldCollectExperimentRecords())
            {
                if (replacementManager != null)
                {
                    replacementManager.ApplyDetections(detections);
                }

                return;
            }

            DetectionCycleDiagnostics cycleDiagnostics = detector.LastCycleDiagnostics;
            experimentRecords.Clear();
            if (replacementManager != null)
            {
                replacementManager.ApplyDetections(
                    detections,
                    cycleDiagnostics,
                    experimentRecords);
            }
            else
            {
                experimentRecords.Add(CreateCycleFailureRecord(
                    cycleDiagnostics,
                    "replacement_manager_missing"));
            }

            if (detections == null || detections.Count == 0)
            {
                experimentRecords.Add(CreateCycleFailureRecord(
                    cycleDiagnostics,
                    "no_detections"));
            }

            float totalLatencyMs = ExperimentClock.ElapsedMilliseconds(
                cycleDiagnostics.CycleStartTimestamp);
            for (int i = 0; i < experimentRecords.Count; i++)
            {
                ExperimentFrameRecord record = experimentRecords[i];
                record.TotalLatencyMs = totalLatencyMs;
                ExperimentRecordReady?.Invoke(record);
            }
        }

        private void HandleInferenceCycleFailed(DetectionCycleDiagnostics cycleDiagnostics)
        {
            if (!ShouldCollectExperimentRecords())
            {
                return;
            }

            ExperimentFrameRecord record = CreateCycleFailureRecord(
                cycleDiagnostics,
                cycleDiagnostics.FailureReason);
            record.TotalLatencyMs = ExperimentClock.ElapsedMilliseconds(
                cycleDiagnostics.CycleStartTimestamp);
            ExperimentRecordReady?.Invoke(record);
        }

        private bool ShouldCollectExperimentRecords()
        {
            return experimentConfig != null
                && experimentConfig.LoggingEnabled
                && ExperimentRecordReady != null;
        }

        private ExperimentFrameRecord CreateCycleFailureRecord(
            DetectionCycleDiagnostics cycleDiagnostics,
            string failureReason)
        {
            ExperimentFrameRecord record = new ExperimentFrameRecord
            {
                FrameId = cycleDiagnostics.FrameId,
                TimestampMs = cycleDiagnostics.TimestampMs,
                CycleMonotonicMs = ExperimentClock.TimestampMilliseconds(
                    cycleDiagnostics.CycleStartTimestamp),
                CaptureLatencyMs = cycleDiagnostics.CaptureLatencyMs,
                YoloLatencyMs = cycleDiagnostics.YoloLatencyMs,
                OutputReadbackLatencyMs = cycleDiagnostics.OutputReadbackLatencyMs,
                Success = false,
                FailureReason = failureReason
            };
            experimentConfig.FillRecordContext(record);
            return record;
        }
    }
}
