using System;
using UnityEngine;

namespace SceneManagement.Runtime
{
    [Serializable]
    public class SceneManagerSettings
    {
        [Header("Core Settings")] public bool autoInitialize = true;
        public bool enableLogging = true;
        public bool persistAcrossScenes = true;

        [Header("Loading Settings")] public bool useAsyncLoading = true;
        public bool enablePreloading = true;
        public int maxConcurrentPreloads = 3;

        [Header("Transition Settings")] public bool enableTransitions = true;
        public float defaultTransitionDuration = 1.0f;
        public bool showLoadingScreen = true;

        [Header("Validation Settings")] public bool validateScenesOnLoad = true;
        public bool blockLoadOnCriticalErrors = true;

        [Header("Save/Load Settings")] public bool enableAutoSave = true;
        public float autoSaveInterval = 300f;
        public int maxAutoSaveSlots = 5;
        public bool enableEncryption;
    }

    public class SceneManagerMain : MonoBehaviour
    {
        public static SceneManagerMain Instance { get; private set; }

        [Header("Scene Manager Configuration")] [SerializeField]
        private SceneManagerSettings settings = new();

        [Header("Component References")] [SerializeField]
        private SceneManagerCore sceneManagerCore;

        [SerializeField] private SceneTransition sceneTransition;
        [SerializeField] private ScenePreloader scenePreloader;
        [SerializeField] private SceneDataManager sceneDataManager;
        [SerializeField] private SceneValidator sceneValidator;
        [SerializeField] private SceneEvents sceneEvents;

        public SceneManagerCore SceneManager => sceneManagerCore;
        public SceneTransition Transition => sceneTransition;
        public ScenePreloader Preloader => scenePreloader;
        public SceneDataManager DataManager => sceneDataManager;
        public SceneValidator Validator => sceneValidator;
        public SceneEvents Events => sceneEvents;

        private bool isInitialized;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;

                if (settings.persistAcrossScenes)
                {
                    DontDestroyOnLoad(gameObject);
                }

                if (settings.autoInitialize)
                {
                    InitializeSceneManager();
                }
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void InitializeSceneManager()
        {
            if (isInitialized)
            {
                Debug.LogWarning("SceneManagerMain is already initialized!");
                return;
            }

            if (settings.enableLogging)
            {
                Debug.Log("[SceneManagerMain] Initializing Scene Management System...");
            }

            CreateOrFindComponents();
            ConfigureComponents();

            isInitialized = true;

            if (settings.enableLogging)
            {
                Debug.Log("[SceneManagerMain] Scene Management System initialized successfully!");
            }
        }

        private void CreateOrFindComponents()
        {
            if (sceneManagerCore == null)
            {
                sceneManagerCore = GetComponent<SceneManagerCore>();
                if (sceneManagerCore == null)
                {
                    sceneManagerCore = gameObject.AddComponent<SceneManagerCore>();
                }
            }

            if (sceneTransition == null && settings.enableTransitions)
            {
                sceneTransition = GetComponent<SceneTransition>();
                if (sceneTransition == null)
                {
                    sceneTransition = gameObject.AddComponent<SceneTransition>();
                }
            }

            if (scenePreloader == null && settings.enablePreloading)
            {
                scenePreloader = GetComponent<ScenePreloader>();
                if (scenePreloader == null)
                {
                    scenePreloader = gameObject.AddComponent<ScenePreloader>();
                }
            }

            if (sceneDataManager == null)
            {
                sceneDataManager = GetComponent<SceneDataManager>();
                if (sceneDataManager == null)
                {
                    sceneDataManager = gameObject.AddComponent<SceneDataManager>();
                }
            }

            if (sceneValidator == null && settings.validateScenesOnLoad)
            {
                sceneValidator = GetComponent<SceneValidator>();
                if (sceneValidator == null)
                {
                    sceneValidator = gameObject.AddComponent<SceneValidator>();
                }
            }

            if (sceneEvents == null)
            {
                sceneEvents = GetComponent<SceneEvents>();
                if (sceneEvents == null)
                {
                    sceneEvents = gameObject.AddComponent<SceneEvents>();
                }
            }
        }

        private void ConfigureComponents()
        {
            if (scenePreloader != null)
            {
                scenePreloader.SetMaxConcurrentPreloads(settings.maxConcurrentPreloads);
                scenePreloader.EnableSmartPreloading(settings.enablePreloading);
            }

            if (sceneDataManager != null)
            {
                sceneDataManager.EnableAutoSave(settings.enableAutoSave);
                sceneDataManager.SetAutoSaveInterval(settings.autoSaveInterval);
            }

            if (sceneValidator != null)
            {
                sceneValidator.SetValidationSettings(
                    settings.validateScenesOnLoad,
                    true,
                    settings.enableLogging,
                    settings.blockLoadOnCriticalErrors
                );
            }

            if (sceneEvents != null)
            {
                sceneEvents.SetEventSettings(true, 10, settings.enableLogging);
            }
        }

        public void LoadSceneWithTransition(string sceneName, float? duration = null, bool? showLoading = null)
        {
            if (!isInitialized)
            {
                Debug.LogError("SceneManagerMain is not initialized!");
                return;
            }

            if (sceneTransition != null && settings.enableTransitions)
            {
                float transitionDuration = duration ?? settings.defaultTransitionDuration;
                bool showLoadingScreen = showLoading ?? settings.showLoadingScreen;

                sceneTransition.FadeToScene(sceneName, transitionDuration, showLoadingScreen);
            }
            else if (sceneManagerCore != null)
            {
                sceneManagerCore.LoadScene(sceneName);
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
            }
        }

        public void PreloadScene(string sceneName)
        {
            if (!isInitialized)
            {
                Debug.LogError("SceneManagerMain is not initialized!");
                return;
            }

            if (scenePreloader != null && settings.enablePreloading)
            {
                scenePreloader.PreloadScene(sceneName);
            }
            else if (sceneManagerCore != null)
            {
                sceneManagerCore.PreloadScene(sceneName);
            }
        }

        public void SaveGame(string saveName = null)
        {
            if (!isInitialized)
            {
                Debug.LogError("SceneManagerMain is not initialized!");
                return;
            }

            if (sceneDataManager != null)
            {
                sceneDataManager.SaveGame(saveName);
            }
        }

        public bool LoadGame(string saveName)
        {
            if (!isInitialized)
            {
                Debug.LogError("SceneManagerMain is not initialized!");
                return false;
            }

            if (sceneDataManager != null)
            {
                return sceneDataManager.LoadGame(saveName);
            }

            return false;
        }

        public void ValidateCurrentScene()
        {
            if (!isInitialized)
            {
                Debug.LogError("SceneManagerMain is not initialized!");
                return;
            }

            if (sceneValidator != null)
            {
                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                sceneValidator.ValidateScene(currentScene);
            }
        }

        public void SetSettings(SceneManagerSettings newSettings)
        {
            settings = newSettings;

            if (isInitialized)
            {
                ConfigureComponents();
            }
        }

        public SceneManagerSettings GetSettings()
        {
            return settings;
        }

        public bool IsInitialized()
        {
            return isInitialized;
        }

        public void Shutdown()
        {
            if (sceneEvents != null)
            {
                sceneEvents.ClearAllCallbacks();
            }

            isInitialized = false;

            if (settings.enableLogging)
            {
                Debug.Log("[SceneManagerMain] Scene Management System shutdown completed.");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Shutdown();
                Instance = null;
            }
        }

        [Serializable]
        public class SceneManagerInfo
        {
            public string version = "1.0.0";

            public string[] supportedFeatures =
            {
                "Async Scene Loading",
                "Scene Transitions",
                "Scene Preloading",
                "Save/Load System",
                "Scene Validation",
                "Event System"
            };
        }

        public static SceneManagerInfo GetSystemInfo()
        {
            return new SceneManagerInfo();
        }

        [ContextMenu("Create Scene Management System")]
        public void CreateCompleteSystem()
        {
            if (Application.isPlaying)
            {
                InitializeSceneManager();
            }
            else
            {
                Debug.Log("Scene Management System will be created when entering Play Mode.");
            }
        }

        [ContextMenu("Validate System Components")]
        public void ValidateSystemComponents()
        {
            bool allValid = true;
            string report = "Scene Management System Validation Report:\n";

            if (sceneManagerCore == null)
            {
                report += "⚠ SceneManagerCore: Missing\n";
                allValid = false;
            }
            else
            {
                report += "✓ SceneManagerCore: OK\n";
            }

            if (settings.enableTransitions)
            {
                if (sceneTransition == null)
                {
                    report += "⚠ SceneTransition: Missing (but enabled in settings)\n";
                    allValid = false;
                }
                else
                {
                    report += "✓ SceneTransition: OK\n";
                }
            }

            if (settings.enablePreloading)
            {
                if (scenePreloader == null)
                {
                    report += "⚠ ScenePreloader: Missing (but enabled in settings)\n";
                    allValid = false;
                }
                else
                {
                    report += "✓ ScenePreloader: OK\n";
                }
            }

            if (sceneDataManager == null)
            {
                report += "⚠ SceneDataManager: Missing\n";
                allValid = false;
            }
            else
            {
                report += "✓ SceneDataManager: OK\n";
            }

            if (settings.validateScenesOnLoad)
            {
                if (sceneValidator == null)
                {
                    report += "⚠ SceneValidator: Missing (but enabled in settings)\n";
                    allValid = false;
                }
                else
                {
                    report += "✓ SceneValidator: OK\n";
                }
            }

            if (sceneEvents == null)
            {
                report += "⚠ SceneEvents: Missing\n";
                allValid = false;
            }
            else
            {
                report += "✓ SceneEvents: OK\n";
            }

            report += allValid ? "\n✅ All components are properly configured!" : "\n❌ Some components need attention.";
            Debug.Log(report);
        }
    }
}