using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using SceneManagement.Runtime;

namespace TechCore.SceneManagement
{
    public class SceneManagerDemo : MonoBehaviour
    {
        [Header("UI References")] [SerializeField]
        private Button loadSceneButton;

        [SerializeField] private Button unloadSceneButton;
        [SerializeField] private Button preloadSceneButton;
        [SerializeField] private Button transitionButton;
        [SerializeField] private Button saveGameButton;
        [SerializeField] private Button loadGameButton;
        [SerializeField] private Button validateSceneButton;

        [SerializeField] private Dropdown sceneDropdown;
        [SerializeField] private Dropdown saveSlotDropdown;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private Text statusText;
        [SerializeField] private Text logText;
        [SerializeField] private ScrollRect logScrollRect;

        [Header("Demo Settings")] [SerializeField]
        private string[] demoScenes = { "MainMenu", "GameLevel1", "GameLevel2", "Settings" };

        [SerializeField] private string[] saveSlots = { "Save1", "Save2", "Save3", "AutoSave" };
        [SerializeField] private bool enableDetailedLogging = true;

        private readonly Queue<string> logMessages = new();
        private const int maxLogMessages = 50;

        private void Start()
        {
            InitializeDemo();
            SetupEventListeners();
            UpdateUI();
        }

        private void InitializeDemo()
        {
            SetupDropdowns();
            SetupButtons();
            AddLogMessage("Scene Manager Demo initialized");

            if (SceneManagerCore.Instance == null)
            {
                AddLogMessage("Warning: SceneManagerCore not found! Creating instance...");
                GameObject sceneManagerObj = new GameObject("SceneManagerCore");
                sceneManagerObj.AddComponent<SceneManagerCore>();
            }

            if (SceneTransition.Instance == null)
            {
                AddLogMessage("Warning: SceneTransition not found! Creating instance...");
                GameObject transitionObj = new GameObject("SceneTransition");
                transitionObj.AddComponent<SceneTransition>();
            }
        }

        private void SetupDropdowns()
        {
            if (sceneDropdown != null)
            {
                sceneDropdown.ClearOptions();
                sceneDropdown.AddOptions(new List<string>(demoScenes));
            }

            if (saveSlotDropdown != null)
            {
                saveSlotDropdown.ClearOptions();
                saveSlotDropdown.AddOptions(new List<string>(saveSlots));
            }
        }

        private void SetupButtons()
        {
            if (loadSceneButton != null)
                loadSceneButton.onClick.AddListener(OnLoadSceneClicked);

            if (unloadSceneButton != null)
                unloadSceneButton.onClick.AddListener(OnUnloadSceneClicked);

            if (preloadSceneButton != null)
                preloadSceneButton.onClick.AddListener(OnPreloadSceneClicked);

            if (transitionButton != null)
                transitionButton.onClick.AddListener(OnTransitionClicked);

            if (saveGameButton != null)
                saveGameButton.onClick.AddListener(OnSaveGameClicked);

            if (loadGameButton != null)
                loadGameButton.onClick.AddListener(OnLoadGameClicked);

            if (validateSceneButton != null)
                validateSceneButton.onClick.AddListener(OnValidateSceneClicked);
        }

        private void SetupEventListeners()
        {
            if (SceneManagerCore.Instance != null)
            {
                SceneManagerCore.Instance.OnSceneLoadStarted += OnSceneLoadStarted;
                SceneManagerCore.Instance.OnSceneLoaded += OnSceneLoaded;
                SceneManagerCore.Instance.OnSceneUnloadStarted += OnSceneUnloadStarted;
                SceneManagerCore.Instance.OnSceneUnloaded += OnSceneUnloaded;
                SceneManagerCore.Instance.OnSceneLoadProgress += OnSceneLoadProgress;
                SceneManagerCore.Instance.OnSceneLoadFailed += OnSceneLoadFailed;
            }

            if (SceneTransition.Instance != null)
            {
                SceneTransition.Instance.OnTransitionStarted += OnTransitionStarted;
                SceneTransition.Instance.OnTransitionCompleted += OnTransitionCompleted;
            }

            if (ScenePreloader.Instance != null)
            {
                ScenePreloader.Instance.OnPreloadStarted += OnPreloadStarted;
                ScenePreloader.Instance.OnPreloadCompleted += OnPreloadCompleted;
                ScenePreloader.Instance.OnPreloadFailed += OnPreloadFailed;
            }

            if (SceneDataManager.Instance != null)
            {
                SceneDataManager.Instance.OnSaveStarted += OnSaveStarted;
                SceneDataManager.Instance.OnSaveCompleted += OnSaveCompleted;
                SceneDataManager.Instance.OnLoadStarted += OnLoadStarted;
                SceneDataManager.Instance.OnLoadCompleted += OnLoadCompleted;
            }

            if (SceneValidator.Instance != null)
            {
                SceneValidator.Instance.OnSceneValidated += OnSceneValidated;
                SceneValidator.Instance.OnValidationFailed += OnValidationFailed;
                SceneValidator.Instance.OnCriticalValidationError += OnCriticalValidationError;
            }
        }

        private void OnLoadSceneClicked()
        {
            if (sceneDropdown != null && SceneManagerCore.Instance != null)
            {
                string sceneName = demoScenes[sceneDropdown.value];
                AddLogMessage($"Loading scene: {sceneName}");
                SceneManagerCore.Instance.LoadScene(sceneName);
            }
        }

        private void OnUnloadSceneClicked()
        {
            if (sceneDropdown != null && SceneManagerCore.Instance != null)
            {
                string sceneName = demoScenes[sceneDropdown.value];
                AddLogMessage($"Unloading scene: {sceneName}");
                SceneManagerCore.Instance.UnloadScene(sceneName);
            }
        }

        private void OnPreloadSceneClicked()
        {
            if (sceneDropdown != null && SceneManagerCore.Instance != null)
            {
                string sceneName = demoScenes[sceneDropdown.value];
                AddLogMessage($"Preloading scene: {sceneName}");
                SceneManagerCore.Instance.PreloadScene(sceneName);
            }
        }

        private void OnTransitionClicked()
        {
            if (sceneDropdown != null && SceneTransition.Instance != null)
            {
                string sceneName = demoScenes[sceneDropdown.value];
                AddLogMessage($"Transitioning to scene: {sceneName}");
                SceneTransition.Instance.FadeToScene(sceneName, 2f, true);
            }
        }

        private void OnSaveGameClicked()
        {
            if (saveSlotDropdown != null && SceneDataManager.Instance != null)
            {
                string saveSlot = saveSlots[saveSlotDropdown.value];
                AddLogMessage($"Saving game to slot: {saveSlot}");
                SceneDataManager.Instance.SaveGame(saveSlot);
            }
        }

        private void OnLoadGameClicked()
        {
            if (saveSlotDropdown != null && SceneDataManager.Instance != null)
            {
                string saveSlot = saveSlots[saveSlotDropdown.value];
                AddLogMessage($"Loading game from slot: {saveSlot}");
                SceneDataManager.Instance.LoadGame(saveSlot);
            }
        }

        private void OnValidateSceneClicked()
        {
            if (sceneDropdown != null && SceneValidator.Instance != null)
            {
                string sceneName = demoScenes[sceneDropdown.value];
                AddLogMessage($"Validating scene: {sceneName}");
                var results = SceneValidator.Instance.ValidateScene(sceneName);

                foreach (var result in results)
                {
                    string status = result.passed ? "PASS" : "FAIL";
                    AddLogMessage($"[{status}] {result.ruleName}: {result.message}");
                }
            }
        }

        private void OnSceneLoadStarted(string sceneName)
        {
            AddLogMessage($"Scene load started: {sceneName}");
            UpdateStatus($"Loading {sceneName}...");
        }

        private void OnSceneLoaded(string sceneName, UnityEngine.SceneManagement.Scene scene)
        {
            AddLogMessage($"Scene loaded successfully: {sceneName}");
            UpdateStatus($"Loaded: {sceneName}");
            SetProgress(0f);
        }

        private void OnSceneUnloadStarted(string sceneName)
        {
            AddLogMessage($"Scene unload started: {sceneName}");
            UpdateStatus($"Unloading {sceneName}...");
        }

        private void OnSceneUnloaded(string sceneName)
        {
            AddLogMessage($"Scene unloaded: {sceneName}");
            UpdateStatus("Ready");
        }

        private void OnSceneLoadProgress(string sceneName, float progress)
        {
            if (enableDetailedLogging)
            {
                AddLogMessage($"Loading progress for {sceneName}: {progress:P1}");
            }

            SetProgress(progress);
        }

        private void OnSceneLoadFailed(string sceneName, string error)
        {
            AddLogMessage($"Scene load failed: {sceneName} - {error}");
            UpdateStatus($"Error: {error}");
        }

        private void OnTransitionStarted()
        {
            AddLogMessage("Scene transition started");
            UpdateStatus("Transitioning...");
        }

        private void OnTransitionCompleted()
        {
            AddLogMessage("Scene transition completed");
            UpdateStatus("Ready");
        }

        private void OnPreloadStarted(string sceneName)
        {
            AddLogMessage($"Preload started: {sceneName}");
        }

        private void OnPreloadCompleted(string sceneName)
        {
            AddLogMessage($"Preload completed: {sceneName}");
        }

        private void OnPreloadFailed(string sceneName, string error)
        {
            AddLogMessage($"Preload failed: {sceneName} - {error}");
        }

        private void OnSaveStarted(string saveName)
        {
            AddLogMessage($"Save started: {saveName}");
            UpdateStatus($"Saving {saveName}...");
        }

        private void OnSaveCompleted(string saveName, bool success)
        {
            string result = success ? "completed" : "failed";
            AddLogMessage($"Save {result}: {saveName}");
            UpdateStatus(success ? "Save completed" : "Save failed");
        }

        private void OnLoadStarted(string saveName)
        {
            AddLogMessage($"Load started: {saveName}");
            UpdateStatus($"Loading {saveName}...");
        }

        private void OnLoadCompleted(string saveName, bool success)
        {
            string result = success ? "completed" : "failed";
            AddLogMessage($"Load {result}: {saveName}");
            UpdateStatus(success ? "Load completed" : "Load failed");
        }

        private void OnSceneValidated(string sceneName, List<ValidationResult> results)
        {
            int passed = 0;
            int failed = 0;

            foreach (var result in results)
            {
                if (result.passed) passed++;
                else failed++;
            }

            AddLogMessage($"Validation completed for {sceneName}: {passed} passed, {failed} failed");
        }

        private void OnValidationFailed(string sceneName, ValidationResult result)
        {
            AddLogMessage($"Validation failed for {sceneName}: {result.message}");
        }

        private void OnCriticalValidationError(string sceneName)
        {
            AddLogMessage($"CRITICAL validation error in {sceneName}!");
        }

        private void AddLogMessage(string message)
        {
            string timestamp = System.DateTime.Now.ToString("HH:mm:ss");
            string logEntry = $"[{timestamp}] {message}";

            logMessages.Enqueue(logEntry);

            while (logMessages.Count > maxLogMessages)
            {
                logMessages.Dequeue();
            }

            UpdateLogDisplay();
        }

        private void UpdateLogDisplay()
        {
            if (logText != null)
            {
                logText.text = string.Join("\n", logMessages.ToArray());

                if (logScrollRect != null)
                {
                    Canvas.ForceUpdateCanvases();
                    logScrollRect.verticalNormalizedPosition = 0f;
                }
            }
        }

        private void UpdateStatus(string status)
        {
            if (statusText != null)
            {
                statusText.text = $"Status: {status}";
            }
        }

        private void SetProgress(float progress)
        {
            if (progressSlider != null)
            {
                progressSlider.value = progress;
            }
        }

        private void UpdateUI()
        {
            if (SceneManagerCore.Instance != null)
            {
                string[] loadedScenes = SceneManagerCore.Instance.GetLoadedSceneNames();
                if (loadedScenes.Length > 0)
                {
                    AddLogMessage($"Currently loaded scenes: {string.Join(", ", loadedScenes)}");
                }

                string activeScene = SceneManagerCore.Instance.GetActiveSceneName();
                AddLogMessage($"Active scene: {activeScene}");
            }
        }

        private void OnDestroy()
        {
            if (SceneManagerCore.Instance != null)
            {
                SceneManagerCore.Instance.OnSceneLoadStarted -= OnSceneLoadStarted;
                SceneManagerCore.Instance.OnSceneLoaded -= OnSceneLoaded;
                SceneManagerCore.Instance.OnSceneUnloadStarted -= OnSceneUnloadStarted;
                SceneManagerCore.Instance.OnSceneUnloaded -= OnSceneUnloaded;
                SceneManagerCore.Instance.OnSceneLoadProgress -= OnSceneLoadProgress;
                SceneManagerCore.Instance.OnSceneLoadFailed -= OnSceneLoadFailed;
            }

            if (SceneTransition.Instance != null)
            {
                SceneTransition.Instance.OnTransitionStarted -= OnTransitionStarted;
                SceneTransition.Instance.OnTransitionCompleted -= OnTransitionCompleted;
            }
        }

        [System.Serializable]
        public class DemoSceneInfo
        {
            public string sceneName;
            public string displayName;
            public string description;
            public Sprite icon;
        }

        public void CreateSceneManagerComponents()
        {
            GameObject managerRoot = new GameObject("Scene Management System");
            DontDestroyOnLoad(managerRoot);

            managerRoot.AddComponent<SceneManagerCore>();
            managerRoot.AddComponent<SceneTransition>();
            managerRoot.AddComponent<ScenePreloader>();
            managerRoot.AddComponent<SceneDataManager>();
            managerRoot.AddComponent<SceneValidator>();
            managerRoot.AddComponent<SceneEvents>();

            AddLogMessage("Scene Management System components created!");
        }
    }
}