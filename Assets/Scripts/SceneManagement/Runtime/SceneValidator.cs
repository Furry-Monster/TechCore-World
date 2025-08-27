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

        public ValidationResult(string scene, string rule, bool success, ValidationSeverity sev, string msg, string det = "")
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

    public interface IValidationRule
    {
        ValidationResult Validate(Scene scene);
        string RuleName { get; }
        bool IsEnabled { get; set; }
    }

    public class SceneValidator
    {
        private static SceneValidator instance;
        public static SceneValidator Instance => instance ??= new SceneValidator();

        private List<SceneValidationRule> validationRules = new();
        private List<IValidationRule> customValidationRules = new();

        private bool enableValidation = true;
        private bool autoValidateOnLoad = true;
        private bool enableLogging = true;
        private bool blockLoadOnCriticalErrors = true;

        public event Action<string, List<ValidationResult>> OnSceneValidated;
        public event Action<string, ValidationResult> OnValidationRuleCompleted;
        public event Action<string, string> OnValidationBlocked;

        private SceneValidator()
        {
            InitializeDefaultRules();
        }

        private void InitializeDefaultRules()
        {
            validationRules = new List<SceneValidationRule>
            {
                new SceneValidationRule
                {
                    ruleName = "Scene Exists",
                    description = "Verify that the scene file exists",
                    validationType = ValidationType.SceneExists,
                    warningMessage = "Scene file may be missing or moved",
                    errorMessage = "Scene file not found"
                },
                new SceneValidationRule
                {
                    ruleName = "Minimum GameObjects",
                    description = "Scene should have at least one GameObject",
                    validationType = ValidationType.MinimumGameObjects,
                    expectedValue = "1",
                    warningMessage = "Scene appears to be empty",
                    errorMessage = "Scene has no GameObjects"
                }
            };
        }

        public void Initialize(bool validation, bool autoValidate, bool logging, bool blockOnCritical)
        {
            enableValidation = validation;
            autoValidateOnLoad = autoValidate;
            enableLogging = logging;
            blockLoadOnCriticalErrors = blockOnCritical;
        }

        public void SetValidationSettings(bool validation, bool autoValidate, bool logging, bool blockOnCritical)
        {
            Initialize(validation, autoValidate, logging, blockOnCritical);
        }

        public List<ValidationResult> ValidateScene(string sceneName)
        {
            if (!enableValidation)
            {
                return new List<ValidationResult>();
            }

            var results = new List<ValidationResult>();
            var scene = GetSceneByName(sceneName);

            if (!scene.IsValid())
            {
                var result = new ValidationResult(sceneName, "Scene Access", false, ValidationSeverity.Critical,
                    $"Cannot access scene '{sceneName}' for validation", "Scene may not be loaded or may not exist");
                results.Add(result);

                if (enableLogging)
                {
                    Debug.LogError($"[SceneValidator] {result.message}: {result.details}");
                }

                OnSceneValidated?.Invoke(sceneName, results);
                return results;
            }

            foreach (var rule in validationRules)
            {
                if (!rule.isEnabled) continue;

                try
                {
                    var result = ValidateRule(scene, rule);
                    results.Add(result);

                    OnValidationRuleCompleted?.Invoke(sceneName, result);

                    if (enableLogging && !result.passed)
                    {
                        var logMethod = result.severity == ValidationSeverity.Critical ? 
                            (System.Action<string>)Debug.LogError :
                            result.severity == ValidationSeverity.Error ? Debug.LogError :
                            result.severity == ValidationSeverity.Warning ? Debug.LogWarning : Debug.Log;

                        logMethod($"[SceneValidator] {result.message}");
                    }
                }
                catch (Exception ex)
                {
                    var errorResult = new ValidationResult(sceneName, rule.ruleName, false, ValidationSeverity.Error,
                        $"Validation rule '{rule.ruleName}' failed with exception", ex.Message);
                    results.Add(errorResult);

                    if (enableLogging)
                    {
                        Debug.LogError($"[SceneValidator] Exception in rule '{rule.ruleName}': {ex.Message}");
                    }
                }
            }

            foreach (var customRule in customValidationRules)
            {
                if (!customRule.IsEnabled) continue;

                try
                {
                    var result = customRule.Validate(scene);
                    results.Add(result);
                    OnValidationRuleCompleted?.Invoke(sceneName, result);
                }
                catch (Exception ex)
                {
                    var errorResult = new ValidationResult(sceneName, customRule.RuleName, false, ValidationSeverity.Error,
                        $"Custom validation rule '{customRule.RuleName}' failed", ex.Message);
                    results.Add(errorResult);

                    if (enableLogging)
                    {
                        Debug.LogError($"[SceneValidator] Exception in custom rule '{customRule.RuleName}': {ex.Message}");
                    }
                }
            }

            var hasCriticalErrors = results.Exists(r => r.severity == ValidationSeverity.Critical && !r.passed);
            if (hasCriticalErrors && blockLoadOnCriticalErrors)
            {
                var criticalError = results.Find(r => r.severity == ValidationSeverity.Critical && !r.passed);
                OnValidationBlocked?.Invoke(sceneName, criticalError.message);

                if (enableLogging)
                {
                    Debug.LogError($"[SceneValidator] Scene loading blocked due to critical validation failure: {criticalError.message}");
                }
            }

            OnSceneValidated?.Invoke(sceneName, results);

            if (enableLogging)
            {
                var passedCount = results.FindAll(r => r.passed).Count;
                var totalCount = results.Count;
                Debug.Log($"[SceneValidator] Scene '{sceneName}' validation completed: {passedCount}/{totalCount} rules passed");
            }

            return results;
        }

        private ValidationResult ValidateRule(Scene scene, SceneValidationRule rule)
        {
            switch (rule.validationType)
            {
                case ValidationType.SceneExists:
                    return ValidateSceneExists(scene, rule);

                case ValidationType.RequiredComponent:
                    return ValidateRequiredComponent(scene, rule);

                case ValidationType.RequiredTag:
                    return ValidateRequiredTag(scene, rule);

                case ValidationType.MinimumGameObjects:
                    return ValidateMinimumGameObjects(scene, rule);

                case ValidationType.MaximumGameObjects:
                    return ValidateMaximumGameObjects(scene, rule);

                case ValidationType.RequiredLayer:
                    return ValidateRequiredLayer(scene, rule);

                default:
                    return new ValidationResult(scene.name, rule.ruleName, false, ValidationSeverity.Warning,
                        $"Unknown validation type: {rule.validationType}");
            }
        }

        private ValidationResult ValidateSceneExists(Scene scene, SceneValidationRule rule)
        {
            var scenePath = scene.path;
            var exists = !string.IsNullOrEmpty(scenePath) && File.Exists(scenePath);

            return new ValidationResult(scene.name, rule.ruleName, exists,
                exists ? ValidationSeverity.Info : ValidationSeverity.Critical,
                exists ? "Scene file exists" : rule.errorMessage,
                exists ? $"Scene path: {scenePath}" : $"Expected scene file not found");
        }

        private ValidationResult ValidateRequiredComponent(Scene scene, SceneValidationRule rule)
        {
            var rootObjects = scene.GetRootGameObjects();
            var componentType = Type.GetType(rule.expectedValue);

            if (componentType == null)
            {
                return new ValidationResult(scene.name, rule.ruleName, false, ValidationSeverity.Error,
                    $"Component type '{rule.expectedValue}' not found", "Check component name and namespace");
            }

            var found = false;
            foreach (var rootObj in rootObjects)
            {
                if (rootObj.GetComponentInChildren(componentType) != null)
                {
                    found = true;
                    break;
                }
            }

            return new ValidationResult(scene.name, rule.ruleName, found,
                found ? ValidationSeverity.Info : ValidationSeverity.Warning,
                found ? $"Required component '{rule.expectedValue}' found" : rule.warningMessage);
        }

        private ValidationResult ValidateRequiredTag(Scene scene, SceneValidationRule rule)
        {
            var found = GameObject.FindGameObjectWithTag(rule.expectedValue) != null;

            return new ValidationResult(scene.name, rule.ruleName, found,
                found ? ValidationSeverity.Info : ValidationSeverity.Warning,
                found ? $"Required tag '{rule.expectedValue}' found" : rule.warningMessage);
        }

        private ValidationResult ValidateMinimumGameObjects(Scene scene, SceneValidationRule rule)
        {
            var count = scene.rootCount;
            var minimum = int.TryParse(rule.expectedValue, out var min) ? min : 1;
            var passed = count >= minimum;

            return new ValidationResult(scene.name, rule.ruleName, passed,
                passed ? ValidationSeverity.Info : ValidationSeverity.Warning,
                passed ? $"Scene has {count} root GameObjects (minimum: {minimum})" : rule.warningMessage,
                $"Current count: {count}, Required minimum: {minimum}");
        }

        private ValidationResult ValidateMaximumGameObjects(Scene scene, SceneValidationRule rule)
        {
            var count = scene.rootCount;
            var maximum = int.TryParse(rule.expectedValue, out var max) ? max : 100;
            var passed = count <= maximum;

            return new ValidationResult(scene.name, rule.ruleName, passed,
                passed ? ValidationSeverity.Info : ValidationSeverity.Warning,
                passed ? $"Scene has {count} root GameObjects (maximum: {maximum})" : rule.warningMessage,
                $"Current count: {count}, Allowed maximum: {maximum}");
        }

        private ValidationResult ValidateRequiredLayer(Scene scene, SceneValidationRule rule)
        {
            var layerName = rule.expectedValue;
            var layerIndex = LayerMask.NameToLayer(layerName);
            var layerExists = layerIndex != -1;

            return new ValidationResult(scene.name, rule.ruleName, layerExists,
                layerExists ? ValidationSeverity.Info : ValidationSeverity.Warning,
                layerExists ? $"Required layer '{layerName}' exists" : rule.warningMessage,
                layerExists ? $"Layer index: {layerIndex}" : $"Layer '{layerName}' not found in project settings");
        }

        private Scene GetSceneByName(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.name == sceneName)
                {
                    return scene;
                }
            }

            return default(Scene);
        }

        public void AddValidationRule(SceneValidationRule rule)
        {
            if (rule != null && !string.IsNullOrEmpty(rule.ruleName))
            {
                validationRules.Add(rule);
            }
        }

        public void AddCustomValidationRule(IValidationRule customRule)
        {
            if (customRule != null)
            {
                customValidationRules.Add(customRule);
            }
        }

        public void RemoveValidationRule(string ruleName)
        {
            validationRules.RemoveAll(r => r.ruleName == ruleName);
            customValidationRules.RemoveAll(r => r.RuleName == ruleName);
        }

        public void EnableValidationRule(string ruleName, bool enabled)
        {
            var rule = validationRules.Find(r => r.ruleName == ruleName);
            if (rule != null)
            {
                rule.isEnabled = enabled;
            }

            var customRule = customValidationRules.Find(r => r.RuleName == ruleName);
            if (customRule != null)
            {
                customRule.IsEnabled = enabled;
            }
        }

        public void ClearValidationRules()
        {
            validationRules.Clear();
            customValidationRules.Clear();
            InitializeDefaultRules();
        }

        public List<SceneValidationRule> GetValidationRules()
        {
            return new List<SceneValidationRule>(validationRules);
        }

        public bool ShouldBlockSceneLoad(List<ValidationResult> results)
        {
            if (!blockLoadOnCriticalErrors) return false;
            return results.Exists(r => r.severity == ValidationSeverity.Critical && !r.passed);
        }

        public void Configure(List<SceneValidationRule> rules = null, bool validation = true, bool autoValidate = true, bool logging = true, bool blockOnCritical = true)
        {
            if (rules != null)
            {
                validationRules = new List<SceneValidationRule>(rules);
            }

            Initialize(validation, autoValidate, logging, blockOnCritical);
        }
    }
}