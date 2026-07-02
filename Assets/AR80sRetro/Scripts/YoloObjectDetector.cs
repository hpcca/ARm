using System;
using System.Collections.Generic;
using Unity.Sentis;
using UnityEngine;
namespace AR80sRetro
{
    public sealed class YoloObjectDetector : MonoBehaviour
    {
        private readonly struct Candidate
        {
            public Candidate(Rect box, float confidence, string label)
            {
                Box = box;
                Confidence = confidence;
                Label = label;
            }
            public Rect Box { get; }
            public float Confidence { get; }
            public string Label { get; }
        }
        private const int InputSize = 640;
        private const int CocoClassCount = 80;
        // 多物体类别定义
        private const int CupClassIndex = 41;
        private const string CupLabel = "cup";

        private const int PhoneClassIndex = 67;
        private const string PhoneLabel = "phone";

        private const int TvClassIndex = 62;
        private const string TvLabel = "tv";

        [Header("Dependencies")]
        [SerializeField] private ModelAsset modelAsset;
        [SerializeField] private ARCameraFrameProvider frameProvider;
        [Header("Inference")]
        [SerializeField] private BackendType backendType = BackendType.GPUCompute;
        [SerializeField, Min(0.1f)] private float inferenceIntervalSeconds = 0.5f;
        [SerializeField, Range(0.05f, 1f)] private float confidenceThreshold = 0.45f;
        [SerializeField, Range(0.05f, 1f)] private float iouThreshold = 0.45f;
        [SerializeField, Min(1)] private int maxDetections = 3;
        [SerializeField] private bool logDetections = true;
        [SerializeField, Min(1)] private int diagnosticLogInterval = 10;
        public event Action<IReadOnlyList<DetectionResult>> DetectionsReady;
        private readonly List<Candidate> candidates = new List<Candidate>(32);
        private readonly List<Candidate> selectedCandidates = new List<Candidate>(8);
        private readonly List<DetectionResult> detectionResults = new List<DetectionResult>(8);
        private Worker worker;
        private Tensor<float> inputTensor;
        private float nextInferenceTime;
        private bool hasLoggedOutputShape;
        private bool initializationFailed;
        private int inferenceCount;
        private void Reset()
        {
            frameProvider = FindObjectOfType<ARCameraFrameProvider>();
        }
        private void OnEnable()
        {
            TryInitialize();
        }
        private void Update()
        {
            if (Time.time < nextInferenceTime)
            {
                return;
            }
            nextInferenceTime = Time.time + inferenceIntervalSeconds;
            if (!TryInitialize() || !frameProvider.TryUpdateFrame())
            {
                return;
            }
            RunInference(frameProvider.CameraTexture);
        }
        private bool TryInitialize()
        {
            if (worker != null)
            {
                return true;
            }
            if (initializationFailed)
            {
                return false;
            }
            if (modelAsset == null || frameProvider == null)
            {
                return false;
            }
            try
            {
                Model runtimeModel = ModelLoader.Load(modelAsset);
                BackendType selectedBackend = backendType;
                if (selectedBackend == BackendType.GPUCompute && !SystemInfo.supportsComputeShaders)
                {
                    selectedBackend = BackendType.CPU;
                    Debug.LogWarning("Compute shaders are unavailable. YOLO is using the CPU backend.", this);
                }
                worker = new Worker(runtimeModel, selectedBackend);
                inputTensor = new Tensor<float>(new TensorShape(1, 3, InputSize, InputSize));
                Debug.Log($"YOLO initialized with {selectedBackend} backend.", this);
                return true;
            }
            catch (Exception exception)
            {
                initializationFailed = true;
                Debug.LogException(exception, this);
                return false;
            }
        }
        private void RunInference(Texture2D cameraTexture)
        {
            try
            {
                TextureTransform transform = new TextureTransform()
                    .SetDimensions(InputSize, InputSize, 3)
                    .SetTensorLayout(TensorLayout.NCHW)
                    .SetCoordOrigin(CoordOrigin.TopLeft);
                TextureConverter.ToTensor(cameraTexture, inputTensor, transform);
                worker.Schedule(inputTensor);
                Tensor<float> output = worker.PeekOutput() as Tensor<float>;
                if (output == null)
                {
                    Debug.LogError("YOLO output is not a float tensor.", this);
                    return;
                }
                using (Tensor<float> cpuOutput = output.ReadbackAndClone())
                {
                    if (!hasLoggedOutputShape)
                    {
                        hasLoggedOutputShape = true;
                        Debug.Log($"YOLO output shape: {cpuOutput.shape}", this);
                    }
                    ParseTargetDetections(cpuOutput);
                }
                inferenceCount++;
                DetectionsReady?.Invoke(detectionResults);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        // 重构：识别 cup / phone / tv 三类目标
        private void ParseTargetDetections(Tensor<float> output)
        {
            candidates.Clear();
            selectedCandidates.Clear();
            detectionResults.Clear();
            if (output.shape.rank != 3 || output.shape[0] != 1)
            {
                Debug.LogError($"Unsupported YOLO output shape: {output.shape}", this);
                return;
            }
            int dimensionOne = output.shape[1];
            int dimensionTwo = output.shape[2];
            int expectedChannels = CocoClassCount + 4;
            bool channelsFirst = dimensionOne == expectedChannels;
            bool channelsLast = dimensionTwo == expectedChannels;
            if (!channelsFirst && !channelsLast)
            {
                Debug.LogError(
                    $"Expected YOLO output [1,84,N] or [1,N,84], received {output.shape}.",
                    this);
                return;
            }
            int boxCount = channelsFirst ? dimensionTwo : dimensionOne;
            ReadOnlySpan<float> values = output.AsReadOnlySpan();

            for (int boxIndex = 0; boxIndex < boxCount; boxIndex++)
            {
                // 读取三类物体置信度
                float cupConf = ReadOutput(values, channelsFirst, boxCount, boxIndex, CupClassIndex + 4);
                float phoneConf = ReadOutput(values, channelsFirst, boxCount, boxIndex, PhoneClassIndex + 4);
                float tvConf = ReadOutput(values, channelsFirst, boxCount, boxIndex, TvClassIndex + 4);

                // 获取当前框最高置信度+对应标签
                float maxConf = Mathf.Max(cupConf, phoneConf, tvConf);
                string targetLabel = "";
                if (maxConf == cupConf) targetLabel = CupLabel;
                else if (maxConf == phoneConf) targetLabel = PhoneLabel;
                else if (maxConf == tvConf) targetLabel = TvLabel;

                if (maxConf < confidenceThreshold)
                    continue;

                // 读取框坐标
                float centerX = ReadOutput(values, channelsFirst, boxCount, boxIndex, 0);
                float centerY = ReadOutput(values, channelsFirst, boxCount, boxIndex, 1);
                float width = ReadOutput(values, channelsFirst, boxCount, boxIndex, 2);
                float height = ReadOutput(values, channelsFirst, boxCount, boxIndex, 3);

                Rect normalizedBox = new Rect(
                    Mathf.Clamp01((centerX - width * 0.5f) / InputSize),
                    Mathf.Clamp01((centerY - height * 0.5f) / InputSize),
                    Mathf.Clamp01(width / InputSize),
                    Mathf.Clamp01(height / InputSize));

                if (normalizedBox.width <= 0f || normalizedBox.height <= 0f)
                    continue;

                Rect screenBox = frameProvider.ImageRectToScreenRect(normalizedBox);
                candidates.Add(new Candidate(screenBox, maxConf, targetLabel));
            }

            candidates.Sort((left, right) => right.Confidence.CompareTo(left.Confidence));
            ApplyNonMaximumSuppression();

            int resultCount = Mathf.Min(maxDetections, selectedCandidates.Count);
            for (int i = 0; i < resultCount; i++)
            {
                Candidate candidate = selectedCandidates[i];
                detectionResults.Add(new DetectionResult(candidate.Label, candidate.Confidence, candidate.Box));
            }

            if (logDetections && detectionResults.Count > 0)
            {
                foreach (var res in detectionResults)
                {
                    Debug.Log($"YOLO detect: {res.Label}, confidence={res.Confidence:F2}, box={res.NormalizedBox}", this);
                }
            }
        }

        private void ApplyNonMaximumSuppression()
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                bool overlapsSelected = false;
                for (int j = 0; j < selectedCandidates.Count; j++)
                {
                    if (CalculateIntersectionOverUnion(candidate.Box, selectedCandidates[j].Box) > iouThreshold)
                    {
                        overlapsSelected = true;
                        break;
                    }
                }
                if (!overlapsSelected)
                {
                    selectedCandidates.Add(candidate);
                }
                if (selectedCandidates.Count >= maxDetections)
                    break;
            }
        }
        private static float ReadOutput(
            ReadOnlySpan<float> values,
            bool channelsFirst,
            int boxCount,
            int boxIndex,
            int channelIndex)
        {
            return channelsFirst
                ? values[channelIndex * boxCount + boxIndex]
                : values[boxIndex * (CocoClassCount + 4) + channelIndex];
        }
        private static float CalculateIntersectionOverUnion(Rect first, Rect second)
        {
            float intersectionXMin = Mathf.Max(first.xMin, second.xMin);
            float intersectionYMin = Mathf.Max(first.yMin, second.yMin);
            float intersectionXMax = Mathf.Min(first.xMax, second.xMax);
            float intersectionYMax = Mathf.Min(first.yMax, second.yMax);
            float intersectionWidth = Mathf.Max(0f, intersectionXMax - intersectionXMin);
            float intersectionHeight = Mathf.Max(0f, intersectionYMax - intersectionYMin);
            float intersectionArea = intersectionWidth * intersectionHeight;
            float unionArea = first.width * first.height + second.width * second.height - intersectionArea;
            return unionArea > 0f ? intersectionArea / unionArea : 0f;
        }
        private void OnDisable()
        {
            DisposeResources();
        }
        private void OnDestroy()
        {
            DisposeResources();
        }
        private void DisposeResources()
        {
            worker?.Dispose();
            worker = null;
            inputTensor?.Dispose();
            inputTensor = null;
        }
    }
}