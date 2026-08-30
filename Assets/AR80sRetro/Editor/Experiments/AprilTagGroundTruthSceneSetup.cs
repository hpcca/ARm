#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AR80sRetro.Experiments;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

namespace AR80sRetro.Editor.Experiments
{
    public static class AprilTagGroundTruthSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SystemObjectName = "AR80sRetro System";
        private const string CupPrefabPath =
            "Assets/AR80sRetro/Models/cup/prefab/cup.prefab";
        private const string CalibrationId = "fixture_cup01_tag0_20260826_v1";
        private const string ModelAlignmentId =
            "cup_prefab_422aab88_bounds_bottom_plusz_20260826_v1";
        private const string PilotSessionId = "pilot_gt_cup01_axis_check_20260826";
        private const string PilotTrialId = "axis_check_000";

        private static readonly Vector3 TagFromObjectPositionMeters =
            new Vector3(0.208f, 0f, 0f);
        private static readonly Vector3 TagFromObjectEulerDegrees =
            new Vector3(0f, 90f, -90f);

        [MenuItem("Tools/AR 80s Retro/Experiments/Apply AprilTag GT Wiring")]
        public static void ApplyAndValidate()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before applying AprilTag GT wiring. " +
                    "The setup deliberately does not replace an unrelated dirty scene.");
            }

            GameObject systemObject = FindSceneObject(scene, SystemObjectName);
            if (systemObject == null)
            {
                throw new InvalidOperationException(
                    $"Scene object '{SystemObjectName}' was not found in {ScenePath}.");
            }

            ARReplacementExperimentConfig config =
                RequireComponent<ARReplacementExperimentConfig>(systemObject);
            ARReplacementExperimentLogger logger =
                RequireComponent<ARReplacementExperimentLogger>(systemObject);
            ARCameraFrameProvider frameProvider =
                RequireComponent<ARCameraFrameProvider>(systemObject);
            ARDepthFrameProvider depthProvider =
                RequireComponent<ARDepthFrameProvider>(systemObject);
            ConfigurePilotSession(config);
            ARCameraManager cameraManager =
                UnityEngine.Object.FindObjectOfType<ARCameraManager>(true);
            if (cameraManager == null)
            {
                throw new InvalidOperationException("No ARCameraManager exists in the active scene.");
            }

            Camera arCamera = cameraManager.GetComponent<Camera>();
            if (arCamera == null)
            {
                throw new InvalidOperationException(
                    "ARCameraManager is not attached to the AR Camera GameObject.");
            }

            AprilTagGroundTruthRecorder recorder =
                systemObject.GetComponent<AprilTagGroundTruthRecorder>();
            if (recorder == null)
            {
                recorder = Undo.AddComponent<AprilTagGroundTruthRecorder>(systemObject);
            }

            Vector3 modelFromObjectPosition = CalculatePrefabBottomCentreLocal();
            SerializedObject serializedFrameProvider = new SerializedObject(frameProvider);
            SetInteger(
                serializedFrameProvider,
                "frameRotation",
                (int)ARCameraFrameProvider.FrameRotation.Clockwise90);
            serializedFrameProvider.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(frameProvider);

            SerializedObject serializedDepthProvider = new SerializedObject(depthProvider);
            SetObjectReference(serializedDepthProvider, "frameProvider", frameProvider);
            serializedDepthProvider.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(depthProvider);

            SerializedObject serializedRecorder = new SerializedObject(recorder);
            SetObjectReference(serializedRecorder, "experimentConfig", config);
            SetObjectReference(serializedRecorder, "experimentLogger", logger);
            SetObjectReference(serializedRecorder, "cameraManager", cameraManager);
            SetObjectReference(serializedRecorder, "arCamera", arCamera);
            SetObjectReference(serializedRecorder, "frameProvider", frameProvider);
            SetInteger(serializedRecorder, "targetTagId", 0);
            SetFloat(serializedRecorder, "tagSizeMeters", 0.08f);
            SetInteger(serializedRecorder, "decimation", 2);
            SetFloat(serializedRecorder, "sampleIntervalSeconds", 0.25f);
            SetInteger(serializedRecorder, "maximumImageWidth", 960);
            SetVector3(
                serializedRecorder,
                "tagFromObjectPositionMeters",
                TagFromObjectPositionMeters);
            SetVector3(
                serializedRecorder,
                "tagFromObjectEulerDegrees",
                TagFromObjectEulerDegrees);
            SetVector3(
                serializedRecorder,
                "modelFromObjectPositionLocalUnits",
                modelFromObjectPosition);
            SetVector3(serializedRecorder, "modelFromObjectEulerDegrees", Vector3.zero);
            SetString(serializedRecorder, "modelAlignmentId", ModelAlignmentId);
            SetString(serializedRecorder, "calibrationId", CalibrationId);
            SetFloat(serializedRecorder, "tagSizeMeasurementUncertaintyMeters", 0.0005f);
            SetFloat(serializedRecorder, "tagToObjectTranslationUncertaintyMeters", 0.002f);
            SetFloat(serializedRecorder, "tagToObjectYawUncertaintyDegrees", 1f);
            SetFloat(serializedRecorder, "maxFxFyRelativeDelta", 0.01f);
            SetFloat(serializedRecorder, "maxPrincipalOffsetFraction", 0.01f);
            SetInteger(serializedRecorder, "recordsPerBatch", 16);
            SetFloat(serializedRecorder, "flushIntervalSeconds", 2f);
            serializedRecorder.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(recorder);

            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException($"Failed to save {ScenePath}.");
            }

            AssetDatabase.SaveAssets();
            ValidateInternal(scene, modelFromObjectPosition);
            Debug.Log(
                "AprilTag GT wiring is valid. " +
                $"Calibration={CalibrationId}, Tag size=0.080 m detection edge, " +
                $"^T p_O={TagFromObjectPositionMeters}, " +
                $"^M p_O={modelFromObjectPosition}, Android=ARM64 only, Portrait locked.");
        }

        [MenuItem("Tools/AR 80s Retro/Experiments/Validate AprilTag GT Wiring")]
        public static void ValidateOnly()
        {
            Scene scene = SceneManager.GetActiveScene();
            ValidateInternal(scene, CalculatePrefabBottomCentreLocal());
            Debug.Log("AprilTag GT scene and Android pre-build validation passed.");
        }

        private static void ValidateInternal(
            Scene scene,
            Vector3 expectedModelFromObjectPosition)
        {
            List<string> errors = new List<string>();
            if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            {
                errors.Add($"Active scene must be {ScenePath}.");
            }

            GameObject systemObject = FindSceneObject(scene, SystemObjectName);
            AprilTagGroundTruthRecorder recorder =
                systemObject != null
                    ? systemObject.GetComponent<AprilTagGroundTruthRecorder>()
                    : null;
            if (recorder == null)
            {
                errors.Add($"Missing AprilTagGroundTruthRecorder on {SystemObjectName}.");
            }
            else
            {
                SerializedObject serializedRecorder = new SerializedObject(recorder);
                RequireReference(serializedRecorder, "experimentConfig", errors);
                RequireReference(serializedRecorder, "experimentLogger", errors);
                RequireReference(serializedRecorder, "cameraManager", errors);
                RequireReference(serializedRecorder, "arCamera", errors);
                RequireReference(serializedRecorder, "frameProvider", errors);
                RequireInteger(serializedRecorder, "targetTagId", 0, errors);
                RequireFloat(serializedRecorder, "tagSizeMeters", 0.08f, errors);
                RequireVector3(
                    serializedRecorder,
                    "tagFromObjectPositionMeters",
                    TagFromObjectPositionMeters,
                    errors);
                RequireVector3(
                    serializedRecorder,
                    "tagFromObjectEulerDegrees",
                    TagFromObjectEulerDegrees,
                    errors);
                RequireVector3(
                    serializedRecorder,
                    "modelFromObjectPositionLocalUnits",
                    expectedModelFromObjectPosition,
                    errors);
                RequireString(serializedRecorder, "calibrationId", CalibrationId, errors);
                RequireString(serializedRecorder, "modelAlignmentId", ModelAlignmentId, errors);
            }

            if (systemObject != null)
            {
                ARCameraFrameProvider frameProvider =
                    systemObject.GetComponent<ARCameraFrameProvider>();
                if (frameProvider == null)
                {
                    errors.Add($"Missing ARCameraFrameProvider on {SystemObjectName}.");
                }
                else
                {
                    SerializedObject serializedFrameProvider =
                        new SerializedObject(frameProvider);
                    RequireInteger(
                        serializedFrameProvider,
                        "frameRotation",
                        (int)ARCameraFrameProvider.FrameRotation.Clockwise90,
                        errors);
                }

                ARDepthFrameProvider depthProvider =
                    systemObject.GetComponent<ARDepthFrameProvider>();
                if (depthProvider == null)
                {
                    errors.Add($"Missing ARDepthFrameProvider on {SystemObjectName}.");
                }
                else
                {
                    SerializedObject serializedDepthProvider =
                        new SerializedObject(depthProvider);
                    RequireReference(serializedDepthProvider, "frameProvider", errors);
                }

                ARReplacementExperimentConfig config =
                    systemObject.GetComponent<ARReplacementExperimentConfig>();
                if (config == null)
                {
                    errors.Add($"Missing ARReplacementExperimentConfig on {SystemObjectName}.");
                }
                else
                {
                    SerializedObject serializedConfig = new SerializedObject(config);
                    RequireString(
                        serializedConfig,
                        "session.sessionId",
                        PilotSessionId,
                        errors);
                    RequireString(
                        serializedConfig,
                        "session.trialId",
                        PilotTrialId,
                        errors);
                    RequireFloat(
                        serializedConfig,
                        "session.occlusionPercent",
                        0f,
                        errors);
                    if (!RequireProperty(serializedConfig, "loggingEnabled").boolValue)
                    {
                        errors.Add("Route A and AprilTag reference logging must be enabled.");
                    }
                }
            }

            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                errors.Add("Android target architecture must be ARM64 only for AprilTag 1.0.3.");
            }

            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.Portrait)
            {
                errors.Add("Default interface orientation must be locked to Portrait.");
            }

            if (!PlayerSettings.allowedAutorotateToPortrait
                || PlayerSettings.allowedAutorotateToPortraitUpsideDown
                || PlayerSettings.allowedAutorotateToLandscapeLeft
                || PlayerSettings.allowedAutorotateToLandscapeRight)
            {
                errors.Add("Android autorotation flags must permit Portrait only.");
            }

            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android)
                != ScriptingImplementation.IL2CPP)
            {
                errors.Add("Android scripting backend must be IL2CPP.");
            }

            bool sceneEnabled = false;
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                if (buildScene.enabled && buildScene.path == ScenePath)
                {
                    sceneEnabled = true;
                    break;
                }
            }

            if (!sceneEnabled)
            {
                errors.Add($"{ScenePath} is not enabled in Build Settings.");
            }

            Vector2 mappedCpuPoint =
                ARCameraFrameProvider.OutputImageToCpuImageNormalized(
                    new Vector2(0.2f, 0.3f),
                    ARCameraFrameProvider.FrameRotation.Clockwise90);
            if ((mappedCpuPoint - new Vector2(0.7f, 0.8f)).sqrMagnitude > 1e-8f)
            {
                errors.Add(
                    $"Clockwise90 inverse CPU-image mapping is {mappedCpuPoint}; " +
                    "expected (0.7, 0.8).");
            }

            Vector3 mappedCameraAxis =
                ARCameraFrameProvider.GetCpuImageToUnityCameraRotation(
                    ARCameraFrameProvider.FrameRotation.Clockwise90)
                * Vector3.right;
            if ((mappedCameraAxis - Vector3.up).sqrMagnitude > 1e-8f)
            {
                errors.Add(
                    $"Clockwise90 camera-axis mapping sends +X to {mappedCameraAxis}; " +
                    "expected Unity camera +Y.");
            }

            int missingScriptCount = 0;
            if (scene.IsValid() && scene.isLoaded)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        missingScriptCount +=
                            GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                                transform.gameObject);
                    }
                }
            }

            if (missingScriptCount > 0)
            {
                errors.Add($"Scene contains {missingScriptCount} missing script reference(s).");
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "AprilTag GT pre-build validation failed:\n- " +
                    string.Join("\n- ", errors));
            }
        }

        private static Vector3 CalculatePrefabBottomCentreLocal()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CupPrefabPath);
            if (root == null)
            {
                throw new InvalidOperationException($"Could not load {CupPrefabPath}.");
            }

            try
            {
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"No Renderer exists in {CupPrefabPath}.");
                }

                Bounds localBounds = new Bounds();
                bool initialized = false;
                foreach (Renderer renderer in renderers)
                {
                    Bounds worldBounds = renderer.bounds;
                    Vector3 min = worldBounds.min;
                    Vector3 max = worldBounds.max;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 worldCorner = new Vector3(
                            (corner & 1) == 0 ? min.x : max.x,
                            (corner & 2) == 0 ? min.y : max.y,
                            (corner & 4) == 0 ? min.z : max.z);
                        Vector3 localCorner = root.transform.InverseTransformPoint(worldCorner);
                        if (!initialized)
                        {
                            localBounds = new Bounds(localCorner, Vector3.zero);
                            initialized = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localCorner);
                        }
                    }
                }

                return new Vector3(
                    localBounds.center.x,
                    localBounds.min.y,
                    localBounds.center.z);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigurePilotSession(ARReplacementExperimentConfig config)
        {
            SerializedObject serializedConfig = new SerializedObject(config);
            SetString(serializedConfig, "session.sessionId", PilotSessionId);
            SetString(serializedConfig, "session.trialId", PilotTrialId);
            SetString(serializedConfig, "session.objectId", "cup01");
            SetInteger(serializedConfig, "session.expectedObjectCount", 1);
            SetString(serializedConfig, "session.sceneId", "fixture_board_01");
            SetString(serializedConfig, "session.conditionId", "gt_axis_sanity_yaw000");
            SetString(serializedConfig, "session.distanceCondition", "1.0m");
            SetString(serializedConfig, "session.viewCondition", "frontal");
            SetFloat(serializedConfig, "session.occlusionPercent", 0f);
            SetString(serializedConfig, "session.lightingCondition", "normal_diffuse");
            SetString(
                serializedConfig,
                "session.sceneDescription",
                "Fixed Tag 0/cup fixture; full 9x9 pattern 144 mm; " +
                "detection edge 80 mm; Tag centre (112,148.5) mm; " +
                "cup-bottom centre (320,148.5) mm; handle toward Tag +X; " +
                "AprilTag is evaluation-only and never feeds Route A.");
            RequireProperty(serializedConfig, "loggingEnabled").boolValue = true;
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        private static GameObject FindSceneObject(Scene scene, string objectName)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (string.Equals(transform.name, objectName, StringComparison.Ordinal))
                    {
                        return transform.gameObject;
                    }
                }
            }

            return null;
        }

        private static T RequireComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component == null)
            {
                throw new InvalidOperationException(
                    $"{gameObject.name} is missing required component {typeof(T).Name}.");
            }

            return component;
        }

        private static SerializedProperty RequireProperty(
            SerializedObject serializedObject,
            string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Serialized property '{propertyName}' was not found on " +
                    serializedObject.targetObject.GetType().Name + ".");
            }

            return property;
        }

        private static void SetObjectReference(
            SerializedObject target,
            string propertyName,
            UnityEngine.Object value)
        {
            RequireProperty(target, propertyName).objectReferenceValue = value;
        }

        private static void SetInteger(
            SerializedObject target,
            string propertyName,
            int value)
        {
            RequireProperty(target, propertyName).intValue = value;
        }

        private static void SetFloat(
            SerializedObject target,
            string propertyName,
            float value)
        {
            RequireProperty(target, propertyName).floatValue = value;
        }

        private static void SetVector3(
            SerializedObject target,
            string propertyName,
            Vector3 value)
        {
            RequireProperty(target, propertyName).vector3Value = value;
        }

        private static void SetString(
            SerializedObject target,
            string propertyName,
            string value)
        {
            RequireProperty(target, propertyName).stringValue = value;
        }

        private static void RequireReference(
            SerializedObject target,
            string propertyName,
            List<string> errors)
        {
            if (RequireProperty(target, propertyName).objectReferenceValue == null)
            {
                errors.Add($"{target.targetObject.GetType().Name}.{propertyName} is unassigned.");
            }
        }

        private static void RequireInteger(
            SerializedObject target,
            string propertyName,
            int expected,
            List<string> errors)
        {
            int actual = RequireProperty(target, propertyName).intValue;
            if (actual != expected)
            {
                errors.Add($"{propertyName} is {actual}; expected {expected}.");
            }
        }

        private static void RequireFloat(
            SerializedObject target,
            string propertyName,
            float expected,
            List<string> errors)
        {
            float actual = RequireProperty(target, propertyName).floatValue;
            if (!Mathf.Approximately(actual, expected))
            {
                errors.Add($"{propertyName} is {actual}; expected {expected}.");
            }
        }

        private static void RequireVector3(
            SerializedObject target,
            string propertyName,
            Vector3 expected,
            List<string> errors)
        {
            Vector3 actual = RequireProperty(target, propertyName).vector3Value;
            if ((actual - expected).sqrMagnitude > 1e-10f)
            {
                errors.Add($"{propertyName} is {actual}; expected {expected}.");
            }
        }

        private static void RequireString(
            SerializedObject target,
            string propertyName,
            string expected,
            List<string> errors)
        {
            string actual = RequireProperty(target, propertyName).stringValue;
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                errors.Add($"{propertyName} is '{actual}'; expected '{expected}'.");
            }
        }
    }
}
#endif
