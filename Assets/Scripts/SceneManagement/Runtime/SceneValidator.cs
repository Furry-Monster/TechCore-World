using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneManagement.Runtime
{
    [Serializable]
    public class SceneValidationRule
    {
        public string ruleName;
        public string description;
        public bool isEnabled = true;
        public ValidationType validationType;
        public string expectedValue;
        public string warningMessage;
        public string errorMessage;
    }

    public enum ValidationType
    {
        SceneExists,
        RequiredComponent,
        RequiredTag,
        MinimumGameObjects,
        MaximumGameObjects,
        RequiredLayer,
        CustomValidation
    }

    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    [Serializable]
    public class ValidationResult
    {
        public string sceneName;
        public string ruleName;
        public bool passed;
        public ValidationSeverity severity;
        public string message;
        public string details;
        public DateTime timestamp;

        public ValidationResult(string scene, string rule, bool success, ValidationSeverity sev, string msg,
            string det = "")
        {
            sceneName = scene;
            ruleName = rule;
            passed = success;
            severity = sev;
            message = msg;
            details = det;
            timestamp = DateTime.Now;
        }
    }

    public class SceneValidator : MonoBehaviour
    {
        public static SceneValidator Instance { get; private set; }

        [Header("Validation Settings")] [SerializeField]
        private bool validateOnSceneLoad = true;

        [SerializeField] private bool validateOnBuild = true;
        [SerializeField] private bool logValidationResults = true;
        [SerializeField] private bool blockLoadOnCriticalErrors = true;

        [Header("Validation Rules")] [SerializeField]
        private List<SceneValidationRule> validationRules = new();

        private readonly Dictionary<string, List<ValidationResult>> validationHistory = new();

        private readonly HashSet<string> validatedScenes = new();

        public event Action<string, List<ValidationResult>> OnSceneValidated;
        public event Action<string, ValidationResult> OnValidationFailed;
        public event Action<string> OnCriticalValidationError;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                Initialize();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Initialize()
        {
            if (SceneManagerCore.Instance != null)
            {
                SceneManagerCore.Instance.OnSceneLoadStarted += OnSceneLoadStartedCallback;
                SceneManagerCore.Instance.OnSceneLoaded += OnSceneLoadedCallback;
            }

            InitializeDefaultRules();
        }

        private void InitializeDefaultRules()
        {
            if (validationRules.Count == 0)
            {
                validationRules.AddRange(new[]
                {
                    new SceneValidationRule
                    {
                        ruleName = "MainCamera",
                        description = "Scene must have a main camera",
                        validationType = ValidationType.RequiredTag,
                        expectedValue = "MainCamera",
                        errorMessage = "No main camera found in scene"
                    },
                    new SceneValidationRule
                    {
                        ruleName = "Player",
                        description = "Scene should have a player object",
                        validationType = ValidationType.RequiredTag,
                        expectedValue = "Player",
                        warningMessage = "No player object found in scene"
                    },
                    new SceneValidationRule
                    {
                        ruleName = "GameManager",
                        description = "Scene should have a GameManager component",
                        validationType = ValidationType.RequiredComponent,
                        expectedValue = "GameManager",
                        warningMessage = "No GameManager component found in scene"
                    }
                });
            }
        }

        public List<ValidationResult> ValidateScene(string sceneName)
        {
            List<ValidationResult> results = new List<ValidationResult>();

            if (string.IsNullOrEmpty(sceneName))
            {
                results.Add(new ValidationResult(sceneName, "SceneName", false,
                    ValidationSeverity.Error, "Scene name is null or empty"));
                return results;
            }

            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.isLoaded)
            {
                results.Add(new ValidationResult(sceneName, "SceneLoaded", false,
                    ValidationSeverity.Error, "Scene is not loaded"));
                return results;
            }

            foreach (var rule in validationRules)
            {
                if (!rule.isEnabled) continue;

                ValidationResult result = ValidateRule(sceneName, scene, rule);
                results.Add(result);

                if (!result.passed)
                {
                    OnValidationFailed?.Invoke(sceneName, result);

                    if (result.severity == ValidationSeverity.Critical)
                    {
                        OnCriticalValidationError?.Invoke(sceneName);
                    }
                }
            }

            validationHistory[sceneName] = results;
            validatedScenes.Add(sceneName);

            if (logValidationResults)
            {
                LogValidationResults(sceneName, results);
            }

            OnSceneValidated?.Invoke(sceneName, results);
            return results;
        }

        private ValidationResult ValidateRule(string sceneName, Scene scene, SceneValidationRule rule)
        {
            try
            {
                switch (rule.validationType)
                {
                    case ValidationType.SceneExists:
                        return ValidateSceneExists(sceneName, rule);

                    case ValidationType.RequiredTag:
                        return ValidateRequiredTag(sceneName, scene, rule);

                    case ValidationType.RequiredComponent:
                        return ValidateRequiredComponent(sceneName, scene, rule);

                    case ValidationType.RequiredLayer:
                        return ValidateRequiredLayer(sceneName, scene, rule);

                    case ValidationType.MinimumGameObjects:
                        return ValidateMinimumGameObjects(sceneName, scene, rule);

                    case ValidationType.MaximumGameObjects:
                        return ValidateMaximumGameObjects(sceneName, scene, rule);

                    case ValidationType.CustomValidation:
                        return ValidateCustomRule(sceneName, scene, rule);

                    default:
                        return new ValidationResult(sceneName, rule.ruleName, false,
                            ValidationSeverity.Error, $"Unknown validation type: {rule.validationType}");
                }
            }
            catch (Exception ex)
            {
                return new ValidationResult(sceneName, rule.ruleName, false,
                    ValidationSeverity.Error, $"Validation error: {ex.Message}", ex.StackTrace);
            }
        }

        private ValidationResult ValidateSceneExists(string sceneName, SceneValidationRule rule)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                var name = Path.GetFileNameWithoutExtension(scenePath);

                if (name == sceneName)
                {
                    return new ValidationResult(sceneName, rule.ruleName, true,
                        ValidationSeverity.Info, "Scene exists in build settings");
                }
            }

            return new ValidationResult(sceneName, rule.ruleName, false,
                ValidationSeverity.Error, rule.errorMessage ?? "Scene not found in build settings");
        }

        private ValidationResult ValidateRequiredTag(string sceneName, Scene scene, SceneValidationRule rule)
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();

            foreach (GameObject root in rootObjects)
            {
                if (FindGameObjectWithTag(root, rule.expectedValue) != null)
                {
                    return new ValidationResult(sceneName, rule.ruleName, true,
                        ValidationSeverity.Info, $"Found object with tag: {rule.expectedValue}");
                }
            }

            ValidationSeverity severity = !string.IsNullOrEmpty(rule.errorMessage)
                ? ValidationSeverity.Error
                : ValidationSeverity.Warning;
            string message = !string.IsNullOrEmpty(rule.errorMessage)
                ? rule.errorMessage
                : rule.warningMessage ?? $"No object with tag '{rule.expectedValue}' found";

            return new ValidationResult(sceneName, rule.ruleName, false, severity, message);
        }

        private ValidationResult ValidateRequiredComponent(string sceneName, Scene scene, SceneValidationRule rule)
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();

            foreach (GameObject root in rootObjects)
            {
                if (FindComponentInChildren(root, rule.expectedValue) != null)
                {
                    return new ValidationResult(sceneName, rule.ruleName, true,
                        ValidationSeverity.Info, $"Found component: {rule.expectedValue}");
                }
            }

            ValidationSeverity severity = !string.IsNullOrEmpty(rule.errorMessage)
                ? ValidationSeverity.Error
                : ValidationSeverity.Warning;
            string message = !string.IsNullOrEmpty(rule.errorMessage)
                ? rule.errorMessage
                : rule.warningMessage ?? $"No component '{rule.expectedValue}' found";

            return new ValidationResult(sceneName, rule.ruleName, false, severity, message);
        }

        private ValidationResult ValidateRequiredLayer(string sceneName, Scene scene, SceneValidationRule rule)
        {
            int layerIndex = LayerMask.NameToLayer(rule.expectedValue);
            if (layerIndex == -1)
            {
                return new ValidationResult(sceneName, rule.ruleName, false,
                    ValidationSeverity.Error, $"Layer '{rule.expectedValue}' does not exist");
            }

            GameObject[] rootObjects = scene.GetRootGameObjects();

            foreach (GameObject root in rootObjects)
            {
                if (FindGameObjectOnLayer(root, layerIndex) != null)
                {
                    return new ValidationResult(sceneName, rule.ruleName, true,
                        ValidationSeverity.Info, $"Found object on layer: {rule.expectedValue}");
                }
            }

            ValidationSeverity severity = !string.IsNullOrEmpty(rule.errorMessage)
                ? ValidationSeverity.Error
                : ValidationSeverity.Warning;
            string message = !string.IsNullOrEmpty(rule.errorMessage)
                ? rule.errorMessage
                : rule.warningMessage ?? $"No object on layer '{rule.expectedValue}' found";

            return new ValidationResult(sceneName, rule.ruleName, false, severity, message);
        }

        private ValidationResult ValidateMinimumGameObjects(string sceneName, Scene scene, SceneValidationRule rule)
        {
            if (!int.TryParse(rule.expectedValue, out int minCount))
            {
                return new ValidationResult(sceneName, rule.ruleName, false,
                    ValidationSeverity.Error, "Invalid minimum count value");
            }

            GameObject[] allObjects = scene.GetRootGameObjects();
            int totalCount = 0;

            foreach (GameObject root in allObjects)
            {
                totalCount += CountAllChildren(root);
            }

            bool passed = totalCount >= minCount;
            ValidationSeverity severity = passed ? ValidationSeverity.Info : ValidationSeverity.Warning;
            string message = passed
                ? $"Scene has {totalCount} objects (minimum: {minCount})"
                : $"Scene has only {totalCount} objects, minimum required: {minCount}";

            return new ValidationResult(sceneName, rule.ruleName, passed, severity, message);
        }

        private ValidationResult ValidateMaximumGameObjects(string sceneName, Scene scene, SceneValidationRule rule)
        {
            if (!int.TryParse(rule.expectedValue, out int maxCount))
            {
                return new ValidationResult(sceneName, rule.ruleName, false,
                    ValidationSeverity.Error, "Invalid maximum count value");
            }

            GameObject[] allObjects = scene.GetRootGameObjects();
            int totalCount = 0;

            foreach (GameObject root in allObjects)
            {
                totalCount += CountAllChildren(root);
            }

            bool passed = totalCount <= maxCount;
            ValidationSeverity severity = passed ? ValidationSeverity.Info : ValidationSeverity.Warning;
            string message = passed
                ? $"Scene has {totalCount} objects (maximum: {maxCount})"
                : $"Scene has {totalCount} objects, maximum allowed: {maxCount}";

            return new ValidationResult(sceneName, rule.ruleName, passed, severity, message);
        }

        private ValidationResult ValidateCustomRule(string sceneName, Scene scene, SceneValidationRule rule)
        {
            return new ValidationResult(sceneName, rule.ruleName, true,
                ValidationSeverity.Info, "Custom validation not implemented");
        }

        private GameObject FindGameObjectWithTag(GameObject parent, string tag)
        {
            if (parent.CompareTag(tag))
                return parent;

            for (int i = 0; i < parent.transform.childCount; i++)
            {
                GameObject result = FindGameObjectWithTag(parent.transform.GetChild(i).gameObject, tag);
                if (result != null)
                    return result;
            }

            return null;
        }

        private Component FindComponentInChildren(GameObject parent, string componentName)
        {
            Component component = parent.GetComponent(componentName);
            if (component != null)
                return component;

            for (int i = 0; i < parent.transform.childCount; i++)
            {
                Component result = FindComponentInChildren(parent.transform.GetChild(i).gameObject, componentName);
                if (result != null)
                    return result;
            }

            return null;
        }

        private GameObject FindGameObjectOnLayer(GameObject parent, int layer)
        {
            if (parent.layer == layer)
                return parent;

            for (int i = 0; i < parent.transform.childCount; i++)
            {
                GameObject result = FindGameObjectOnLayer(parent.transform.GetChild(i).gameObject, layer);
                if (result != null)
                    return result;
            }

            return null;
        }

        private int CountAllChildren(GameObject parent)
        {
            int count = 1; // Count the parent itself

            for (int i = 0; i < parent.transform.childCount; i++)
            {
                count += CountAllChildren(parent.transform.GetChild(i).gameObject);
            }

            return count;
        }

        private void LogValidationResults(string sceneName, List<ValidationResult> results)
        {
            int passedCount = 0;
            int warningCount = 0;
            int errorCount = 0;

            foreach (var result in results)
            {
                if (result.passed)
                {
                    passedCount++;
                }
                else
                {
                    switch (result.severity)
                    {
                        case ValidationSeverity.Warning:
                            warningCount++;
                            Debug.LogWarning($"[SceneValidator] {sceneName}: {result.message}");
                            break;
                        case ValidationSeverity.Error:
                        case ValidationSeverity.Critical:
                            errorCount++;
                            Debug.LogError($"[SceneValidator] {sceneName}: {result.message}");
                            break;
                    }
                }
            }

            Debug.Log(
                $"[SceneValidator] {sceneName} validation complete: {passedCount} passed, {warningCount} warnings, {errorCount} errors");
        }

        public bool HasCriticalErrors(string sceneName)
        {
            if (!validationHistory.TryGetValue(sceneName, out var results))
                return false;

            foreach (var result in results)
            {
                if (!result.passed && result.severity == ValidationSeverity.Critical)
                    return true;
            }

            return false;
        }

        public bool IsSceneValidated(string sceneName)
        {
            return validatedScenes.Contains(sceneName);
        }

        public List<ValidationResult> GetValidationHistory(string sceneName)
        {
            return validationHistory.TryGetValue(sceneName, out var result)
                ? new List<ValidationResult>(result)
                : new List<ValidationResult>();
        }

        public void AddValidationRule(SceneValidationRule rule)
        {
            if (rule != null && !string.IsNullOrEmpty(rule.ruleName))
            {
                validationRules.Add(rule);
            }
        }

        public void RemoveValidationRule(string ruleName)
        {
            validationRules.RemoveAll(r => r.ruleName == ruleName);
        }

        public void EnableRule(string ruleName, bool ruleEnabled)
        {
            var rule = validationRules.Find(r => r.ruleName == ruleName);
            if (rule != null)
            {
                rule.isEnabled = ruleEnabled;
            }
        }

        private void OnSceneLoadStartedCallback(string sceneName)
        {
            if (validateOnSceneLoad && blockLoadOnCriticalErrors)
            {
                if (IsSceneValidated(sceneName) && HasCriticalErrors(sceneName))
                {
                    Debug.LogError(
                        $"[SceneValidator] Blocking scene load due to critical validation errors: {sceneName}");
                }
            }
        }

        private void OnSceneLoadedCallback(string sceneName, Scene scene)
        {
            if (validateOnSceneLoad)
            {
                ValidateScene(sceneName);
            }
        }

        public void SetValidationSettings(bool onLoad, bool onBuild, bool logResults, bool blockCritical)
        {
            validateOnSceneLoad = onLoad;
            validateOnBuild = onBuild;
            logValidationResults = logResults;
            blockLoadOnCriticalErrors = blockCritical;
        }
    }
}