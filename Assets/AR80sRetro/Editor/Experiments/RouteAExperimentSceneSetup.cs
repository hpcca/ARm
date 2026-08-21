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
    public static class RouteAExperimentSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SystemObjectName = "AR80sRetro System";

        [MenuItem("Tools/AR 80s Retro/Experiments/Apply and Validate Scene Wiring")]
        public static void ApplyAndValidate()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject systemObject = FindSceneObject(scene, SystemObjectName);
            if (systemObject == null)
            {
                throw new InvalidOperationException(
                    $"Scene object '{SystemObjectName}' was not found in {ScenePath}.");
            }

            ARReplacementExperimentConfig config =
                GetOrAddComponent<ARReplacementExperimentConfig>(systemObject);
            ARReplacementExperimentLogger logger =
                GetOrAddComponent<ARReplacementExperimentLogger>(systemObject);
            YoloObjectDetector detector = RequireComponent<YoloObjectDetector>(systemObject);
            RetroDetectionPipeline pipeline = RequireComponent<RetroDetectionPipeline>(systemObject);
            RetroReplacementManager replacementManager =
                RequireComponent<RetroReplacementManager>(systemObject);
            ARRaycastPositionSolver positionSolver =
                RequireComponent<ARRaycastPositionSolver>(systemObject);
            ARDepthFrameProvider depthProvider = RequireComponent<ARDepthFrameProvider>(systemObject);
            YoloDetectionOverlay overlay = RequireComponent<YoloDetectionOverlay>(systemObject);

            SetReference(depthProvider, "experimentConfig", config);
            SetReference(positionSolver, "experimentConfig", config);
            SetReference(replacementManager, "experimentConfig", config);
            SetReference(pipeline, "experimentConfig", config);
            SetReference(overlay, "experimentConfig", config);

            SetReference(logger, "experimentConfig", config);
            SetReference(logger, "pipeline", pipeline);
            SetReference(logger, "detector", detector);
            SetReference(logger, "depthProvider", depthProvider);
            SetReference(logger, "positionSolver", positionSolver);
            SetReference(logger, "replacementManager", replacementManager);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException($"Failed to save {ScenePath}.");
            }

            ValidateScene(scene, systemObject);
            AssetDatabase.SaveAssets();
            Debug.Log("Route A experiment scene wiring is valid.");
        }

        [MenuItem("Tools/AR 80s Retro/Experiments/Validate Scene Wiring")]
        public static void ValidateOnly()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject systemObject = FindSceneObject(scene, SystemObjectName);
            ValidateScene(scene, systemObject);
            Debug.Log("Route A experiment scene validation passed.");
        }

        private static void ValidateScene(Scene scene, GameObject systemObject)
        {
            List<string> errors = new List<string>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                errors.Add($"Scene is not loaded: {ScenePath}");
            }

            if (systemObject == null)
            {
                errors.Add($"Missing GameObject: {SystemObjectName}");
            }
            else
            {
                ValidateSingleComponent<ARReplacementExperimentConfig>(systemObject, errors);
                ValidateSingleComponent<ARReplacementExperimentLogger>(systemObject, errors);
                ValidateSingleComponent<YoloObjectDetector>(systemObject, errors);
                ValidateSingleComponent<RetroDetectionPipeline>(systemObject, errors);
                ValidateSingleComponent<RetroReplacementManager>(systemObject, errors);
                ValidateSingleComponent<ARRaycastPositionSolver>(systemObject, errors);
                ValidateSingleComponent<ARDepthFrameProvider>(systemObject, errors);
                ValidateSingleComponent<YoloDetectionOverlay>(systemObject, errors);

                ValidateReference(systemObject.GetComponent<ARDepthFrameProvider>(), "experimentConfig", errors);
                ValidateReference(systemObject.GetComponent<ARRaycastPositionSolver>(), "experimentConfig", errors);
                ValidateReference(systemObject.GetComponent<RetroReplacementManager>(), "experimentConfig", errors);
                ValidateReference(systemObject.GetComponent<RetroDetectionPipeline>(), "experimentConfig", errors);
                ValidateReference(systemObject.GetComponent<YoloDetectionOverlay>(), "experimentConfig", errors);

                ARReplacementExperimentLogger logger =
                    systemObject.GetComponent<ARReplacementExperimentLogger>();
                ValidateReference(logger, "experimentConfig", errors);
                ValidateReference(logger, "pipeline", errors);
                ValidateReference(logger, "detector", errors);
                ValidateReference(logger, "depthProvider", errors);
                ValidateReference(logger, "positionSolver", errors);
                ValidateReference(logger, "replacementManager", errors);
            }

            int missingScriptCount = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transform.gameObject);
                }
            }

            if (missingScriptCount > 0)
            {
                errors.Add($"Scene contains {missingScriptCount} missing script reference(s).");
            }

            ValidateExperimentDataContract(errors);
            ValidateAblationPresets(errors);

            AROcclusionManager[] occlusionManagers =
                UnityEngine.Object.FindObjectsOfType<AROcclusionManager>(true);
            if (occlusionManagers.Length != 1)
            {
                errors.Add($"Expected exactly one AROcclusionManager, found {occlusionManagers.Length}.");
            }
            else
            {
                GameObject cameraObject = occlusionManagers[0].gameObject;
                if (cameraObject.GetComponent<Camera>() == null
                    || cameraObject.GetComponent<ARCameraBackground>() == null)
                {
                    errors.Add(
                        "AROcclusionManager is not on the AR Camera GameObject with ARCameraBackground.");
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Route A scene validation failed:\n- " + string.Join("\n- ", errors));
            }
        }

        private static GameObject FindSceneObject(Scene scene, string objectName)
        {
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

        private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(gameObject);
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

        private static void SetReference(
            UnityEngine.Object target,
            string propertyName,
            UnityEngine.Object value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Serialized property '{propertyName}' was not found on {target.GetType().Name}.");
            }

            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void ValidateSingleComponent<T>(
            GameObject gameObject,
            List<string> errors) where T : Component
        {
            int count = gameObject.GetComponents<T>().Length;
            if (count != 1)
            {
                errors.Add($"Expected one {typeof(T).Name} on {gameObject.name}, found {count}.");
            }
        }

        private static void ValidateReference(
            UnityEngine.Object target,
            string propertyName,
            List<string> errors)
        {
            if (target == null)
            {
                errors.Add($"Cannot validate '{propertyName}' because target component is missing.");
                return;
            }

            SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
            if (property == null || property.objectReferenceValue == null)
            {
                errors.Add($"{target.GetType().Name}.{propertyName} is not assigned.");
            }
        }

        private static void ValidateExperimentDataContract(List<string> errors)
        {
            ExperimentFrameRecord record = new ExperimentFrameRecord
            {
                SessionId = "session,quoted",
                TrialId = "trial_\"quoted\"\nline",
                FrameId = 1,
                TimestampMs = 2,
                YoloConfidence = 1.5f,
                FailureReason = "expected,failure",
                Success = false
            };
            string csvLine = record.ToCsvLine();
            int headerColumns = CountCsvColumns(ExperimentFrameRecord.CsvHeader);
            int recordColumns = CountCsvColumns(csvLine);
            if (headerColumns != recordColumns)
            {
                errors.Add(
                    $"CSV header has {headerColumns} columns but a record has {recordColumns}.");
            }

            if (!csvLine.Contains("\"session,quoted\"")
                || !csvLine.Contains("\"trial_\"\"quoted\"\"\nline\"")
                || !csvLine.Contains("1.5"))
            {
                errors.Add("CSV escaping or InvariantCulture formatting validation failed.");
            }
        }

        private static void ValidateAblationPresets(List<string> errors)
        {
            GameObject temporaryObject = new GameObject("Route A Ablation Validation");
            temporaryObject.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                ARReplacementExperimentConfig config =
                    temporaryObject.AddComponent<ARReplacementExperimentConfig>();
                SerializedObject serializedConfig = new SerializedObject(config);
                SerializedProperty mode = serializedConfig.FindProperty("ablationMode");
                bool[,] expected =
                {
                    { false, false, false },
                    { true, false, false },
                    { true, true, false },
                    { true, true, true }
                };
                for (int modeIndex = 0; modeIndex < 4; modeIndex++)
                {
                    mode.enumValueIndex = modeIndex;
                    serializedConfig.ApplyModifiedPropertiesWithoutUndo();
                    if (config.DepthPositionFusionEnabled != expected[modeIndex, 0]
                        || config.TemporalTrackingEnabled != expected[modeIndex, 1]
                        || config.OcclusionFadeFallbackEnabled != expected[modeIndex, 2])
                    {
                        errors.Add($"Ablation preset mapping failed for mode index {modeIndex}.");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporaryObject);
            }
        }

        private static int CountCsvColumns(string value)
        {
            int columns = 1;
            bool insideQuotes = false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character == '"')
                {
                    if (insideQuotes && i + 1 < value.Length && value[i + 1] == '"')
                    {
                        i++;
                    }
                    else
                    {
                        insideQuotes = !insideQuotes;
                    }
                }
                else if (character == ',' && !insideQuotes)
                {
                    columns++;
                }
            }

            return columns;
        }
    }
}
#endif
