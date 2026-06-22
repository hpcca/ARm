using AR80sRetro;
using Unity.Sentis;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace AR80sRetroEditor
{
    public static class AR80sRetroYoloSetup
    {
        private const string SystemObjectName = "AR80sRetro System";
        private const string ModelPath = "Assets/AR80sRetro/Models/YOLO/yolov8n.onnx";

        [MenuItem("Tools/AR 80s Retro/Configure YOLO Scene")]
        public static void ConfigureScene()
        {
            GameObject systemObject = GameObject.Find(SystemObjectName);
            if (systemObject == null)
            {
                Debug.LogError($"Cannot find scene object '{SystemObjectName}'.");
                return;
            }

            ModelAsset modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"Cannot load Sentis model at '{ModelPath}'.");
                return;
            }

            ARCameraManager cameraManager = Object.FindObjectOfType<ARCameraManager>();
            RetroReplacementManager replacementManager =
                systemObject.GetComponent<RetroReplacementManager>();

            if (cameraManager == null || replacementManager == null)
            {
                Debug.LogError("The scene needs an ARCameraManager and RetroReplacementManager.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(systemObject, "Configure AR 80s Retro YOLO");

            ARCameraFrameProvider frameProvider =
                GetOrAddComponent<ARCameraFrameProvider>(systemObject);
            YoloObjectDetector detector = GetOrAddComponent<YoloObjectDetector>(systemObject);
            RetroDetectionPipeline pipeline =
                GetOrAddComponent<RetroDetectionPipeline>(systemObject);
            YoloDetectionOverlay overlay =
                GetOrAddComponent<YoloDetectionOverlay>(systemObject);

            AssignObjectReference(frameProvider, "cameraManager", cameraManager);
            AssignObjectReference(detector, "modelAsset", modelAsset);
            AssignObjectReference(detector, "frameProvider", frameProvider);
            AssignObjectReference(pipeline, "detector", detector);
            AssignObjectReference(pipeline, "replacementManager", replacementManager);
            AssignObjectReference(overlay, "detector", detector);
            AssignObjectReference(replacementManager, "arCamera", cameraManager.GetComponent<Camera>());

            DisableIfPresent<MockDetectionInput>(systemObject);
            DisableIfPresent<CameraFrameSmokeTest>(systemObject);

            EditorUtility.SetDirty(systemObject);
            EditorSceneManager.MarkSceneDirty(systemObject.scene);
            EditorSceneManager.SaveScene(systemObject.scene);
            Selection.activeGameObject = systemObject;

            Debug.Log("YOLO scene configuration completed. Mock input is disabled.");
        }

        private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(gameObject);
        }

        private static void AssignObjectReference(
            Object target,
            string propertyName,
            Object value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void DisableIfPresent<T>(GameObject gameObject) where T : Behaviour
        {
            T component = gameObject.GetComponent<T>();
            if (component != null)
            {
                component.enabled = false;
                EditorUtility.SetDirty(component);
            }
        }
    }
}
