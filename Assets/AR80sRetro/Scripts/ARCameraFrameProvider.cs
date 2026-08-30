using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace AR80sRetro
{
    public sealed class ARCameraFrameProvider : MonoBehaviour
    {
        public enum FrameRotation
        {
            None,
            Clockwise90,
            CounterClockwise90
        }

        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField, Min(64)] private int outputWidth = 640;
        [SerializeField, Min(64)] private int outputHeight = 640;
        [SerializeField] private TextureFormat outputFormat = TextureFormat.RGB24;
        [SerializeField] private FrameRotation frameRotation = FrameRotation.Clockwise90;
        [SerializeField] private Vector2 screenOffsetNormalized;
        [SerializeField] private Vector2 screenScale = Vector2.one;

        private Texture2D cameraTexture;
        private Texture2D unrotatedTexture;
        private byte[] rotatedPixels;

        public Texture2D CameraTexture => cameraTexture;
        public bool HasFrame { get; private set; }
        public FrameRotation ConfiguredFrameRotation => frameRotation;
        public Quaternion CpuImageToUnityCameraRotation =>
            GetCpuImageToUnityCameraRotation(frameRotation);
        public string CpuImageToUnityCameraTransformId =>
            GetCpuImageToUnityCameraTransformId(frameRotation);
        public string DepthUvTransformId =>
            $"screen_top_left_to_cpu_top_left_v1_{frameRotation}_mirror_y";
        public event Action<Texture2D> FrameReady;

        public static Quaternion GetCpuImageToUnityCameraRotation(
            FrameRotation rotation)
        {
            switch (rotation)
            {
                case FrameRotation.Clockwise90:
                    return Quaternion.Euler(0f, 0f, 90f);
                case FrameRotation.CounterClockwise90:
                    return Quaternion.Euler(0f, 0f, -90f);
                default:
                    return Quaternion.identity;
            }
        }

        public static string GetCpuImageToUnityCameraTransformId(
            FrameRotation rotation)
        {
            return $"apriltag_cpu_pose_to_unity_camera_v1_{rotation}";
        }

        public static Vector2 OutputImageToCpuImageNormalized(
            Vector2 outputImagePoint,
            FrameRotation rotation)
        {
            Vector2 clampedPoint = new Vector2(
                Mathf.Clamp01(outputImagePoint.x),
                Mathf.Clamp01(outputImagePoint.y));
            switch (rotation)
            {
                case FrameRotation.Clockwise90:
                    return new Vector2(
                        1f - clampedPoint.y,
                        1f - clampedPoint.x);
                case FrameRotation.CounterClockwise90:
                    return new Vector2(
                        clampedPoint.y,
                        clampedPoint.x);
                default:
                    return new Vector2(
                        1f - clampedPoint.x,
                        clampedPoint.y);
            }
        }

        public bool TryScreenPointToCpuImageNormalized(
            Vector2 screenPointNormalizedTopLeft,
            out Vector2 cpuImagePointNormalizedTopLeft)
        {
            cpuImagePointNormalizedTopLeft = default;
            if (cameraTexture == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return false;
            }

            Vector2 effectiveScreenScale = screenScale;
            if (effectiveScreenScale.x <= 0f || effectiveScreenScale.y <= 0f)
            {
                effectiveScreenScale = Vector2.one;
            }

            Vector2 uncroppedScreenPoint = new Vector2(
                (screenPointNormalizedTopLeft.x - 0.5f - screenOffsetNormalized.x)
                    / effectiveScreenScale.x + 0.5f,
                (screenPointNormalizedTopLeft.y - 0.5f - screenOffsetNormalized.y)
                    / effectiveScreenScale.y + 0.5f);

            float aspectFillScale = Mathf.Max(
                (float)Screen.width / cameraTexture.width,
                (float)Screen.height / cameraTexture.height);
            float renderedWidth = cameraTexture.width * aspectFillScale;
            float renderedHeight = cameraTexture.height * aspectFillScale;
            float croppedX = (renderedWidth - Screen.width) * 0.5f;
            float croppedY = (renderedHeight - Screen.height) * 0.5f;
            Vector2 outputImagePoint = new Vector2(
                (uncroppedScreenPoint.x * Screen.width + croppedX) / renderedWidth,
                (uncroppedScreenPoint.y * Screen.height + croppedY) / renderedHeight);

            cpuImagePointNormalizedTopLeft = OutputImageToCpuImageNormalized(
                outputImagePoint,
                frameRotation);
            return true;
        }

        public Rect ImageRectToScreenRect(Rect imageRect)
        {
            if (cameraTexture == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return imageRect;
            }

            float scale = Mathf.Max(
                (float)Screen.width / cameraTexture.width,
                (float)Screen.height / cameraTexture.height);
            float renderedWidth = cameraTexture.width * scale;
            float renderedHeight = cameraTexture.height * scale;
            float croppedX = (renderedWidth - Screen.width) * 0.5f;
            float croppedY = (renderedHeight - Screen.height) * 0.5f;

            float xMin = (imageRect.xMin * renderedWidth - croppedX) / Screen.width;
            float xMax = (imageRect.xMax * renderedWidth - croppedX) / Screen.width;
            float yMin = (imageRect.yMin * renderedHeight - croppedY) / Screen.height;
            float yMax = (imageRect.yMax * renderedHeight - croppedY) / Screen.height;

            Vector2 center = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
            Vector2 size = new Vector2(xMax - xMin, yMax - yMin);
            Vector2 effectiveScreenScale = screenScale;
            if (effectiveScreenScale.x <= 0f || effectiveScreenScale.y <= 0f)
            {
                effectiveScreenScale = Vector2.one;
            }

            center = Vector2.Scale(center - new Vector2(0.5f, 0.5f), effectiveScreenScale)
                + new Vector2(0.5f, 0.5f)
                + screenOffsetNormalized;
            size = Vector2.Scale(size, effectiveScreenScale);

            return Rect.MinMaxRect(
                Mathf.Clamp01(center.x - size.x * 0.5f),
                Mathf.Clamp01(center.y - size.y * 0.5f),
                Mathf.Clamp01(center.x + size.x * 0.5f),
                Mathf.Clamp01(center.y + size.y * 0.5f));
        }

        private void Reset()
        {
            cameraManager = FindObjectOfType<ARCameraManager>();
        }

        public bool TryUpdateFrame()
        {
            HasFrame = false;

            if (cameraManager == null || !cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            {
                return false;
            }

            using (image)
            {
                XRCpuImage.ConversionParams conversionParams = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, image.width, image.height),
                    outputDimensions = new Vector2Int(outputWidth, outputHeight),
                    outputFormat = outputFormat,
                    transformation = XRCpuImage.Transformation.MirrorY
                };

                int dataSize = image.GetConvertedDataSize(conversionParams);
                NativeArray<byte> buffer = new NativeArray<byte>(dataSize, Allocator.Temp);

                try
                {
                    image.Convert(conversionParams, buffer);
                    UploadConvertedFrame(buffer);
                }
                finally
                {
                    buffer.Dispose();
                }
            }

            HasFrame = true;
            FrameReady?.Invoke(cameraTexture);
            return true;
        }

        private void UploadConvertedFrame(NativeArray<byte> buffer)
        {
            if (frameRotation == FrameRotation.None)
            {
                EnsureTexture(ref cameraTexture, outputWidth, outputHeight, "AR Camera CPU Frame");
                cameraTexture.LoadRawTextureData(buffer);
                cameraTexture.Apply(false, false);
                return;
            }

            if (outputFormat != TextureFormat.RGB24)
            {
                Debug.LogError("Frame rotation currently requires RGB24 output.", this);
                return;
            }

            EnsureTexture(ref unrotatedTexture, outputWidth, outputHeight, "AR Camera CPU Frame Raw");
            unrotatedTexture.LoadRawTextureData(buffer);
            unrotatedTexture.Apply(false, false);

            int rotatedWidth = outputHeight;
            int rotatedHeight = outputWidth;
            int byteCount = rotatedWidth * rotatedHeight * 3;
            if (rotatedPixels == null || rotatedPixels.Length != byteCount)
            {
                rotatedPixels = new byte[byteCount];
            }

            NativeArray<byte>.ReadOnly source = buffer.AsReadOnly();
            for (int sourceY = 0; sourceY < outputHeight; sourceY++)
            {
                for (int sourceX = 0; sourceX < outputWidth; sourceX++)
                {
                    int destinationX;
                    int destinationY;

                    if (frameRotation == FrameRotation.Clockwise90)
                    {
                        destinationX = outputHeight - 1 - sourceY;
                        destinationY = sourceX;
                    }
                    else
                    {
                        destinationX = sourceY;
                        destinationY = outputWidth - 1 - sourceX;
                    }

                    int sourceIndex = (sourceY * outputWidth + sourceX) * 3;
                    int destinationIndex = (destinationY * rotatedWidth + destinationX) * 3;
                    rotatedPixels[destinationIndex] = source[sourceIndex];
                    rotatedPixels[destinationIndex + 1] = source[sourceIndex + 1];
                    rotatedPixels[destinationIndex + 2] = source[sourceIndex + 2];
                }
            }

            EnsureTexture(ref cameraTexture, rotatedWidth, rotatedHeight, "AR Camera CPU Frame Rotated");
            cameraTexture.LoadRawTextureData(rotatedPixels);
            cameraTexture.Apply(false, false);
        }

        private void EnsureTexture(
            ref Texture2D texture,
            int width,
            int height,
            string textureName)
        {
            if (texture != null
                && texture.width == width
                && texture.height == height
                && texture.format == outputFormat)
            {
                return;
            }

            if (texture != null)
            {
                Destroy(texture);
            }

            texture = new Texture2D(width, height, outputFormat, false);
            texture.name = textureName;
        }

        private void OnDestroy()
        {
            if (cameraTexture != null)
            {
                Destroy(cameraTexture);
            }

            if (unrotatedTexture != null)
            {
                Destroy(unrotatedTexture);
            }
        }
    }
}
