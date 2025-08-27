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

        public SceneManagerSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                if (IsInitialized)
                {
                    ConfigureComponents();
                }
            }
        }

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

        public bool IsInitialized { get; private set; }


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
            if (IsInitialized)
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

            IsInitialized = true;

            if (settings.enableLogging)
            {
                Debug.Log("[SceneManagerMain] Scene Management System initialized successfully!");
            }
        }

        private void CreateOrFindComponents()
        {
            sceneManagerCore ??= GetComponent<SceneManagerCore>()
                                 ?? gameObject.AddComponent<SceneManagerCore>();

            if (settings.enableTransitions)
            {
                sceneTransition ??= GetComponent<SceneTransition>()
                                    ?? gameObject.AddComponent<SceneTransition>();
            }

            if (settings.enablePreloading)
            {
                scenePreloader ??= GetComponent<ScenePreloader>()
                                   ?? gameObject.AddComponent<ScenePreloader>();
            }

            sceneDataManager ??= GetComponent<SceneDataManager>()
                                 ?? gameObject.AddComponent<SceneDataManager>();

            if (settings.validateScenesOnLoad)
            {
                sceneValidator ??= GetComponent<SceneValidator>()
                                   ?? gameObject.AddComponent<SceneValidator>();
            }

            sceneEvents ??= GetComponent<SceneEvents>()
                            ?? gameObject.AddComponent<SceneEvents>();
        }

        private void ConfigureComponents()
        {
            scenePreloader?.SetMaxConcurrentPreloads(settings.maxConcurrentPreloads);
            scenePreloader?.EnableSmartPreloading(settings.enablePreloading);

            sceneDataManager?.EnableAutoSave(settings.enableAutoSave);
            sceneDataManager?.SetAutoSaveInterval(settings.autoSaveInterval);

            sceneValidator?.SetValidationSettings(
                settings.validateScenesOnLoad,
                true,
                settings.enableLogging,
                settings.blockLoadOnCriticalErrors
            );

            sceneEvents?.SetEventSettings(true, 10, settings.enableLogging);
        }

        public void LoadSceneWithTransition(string sceneName, float? duration = null, bool? showLoading = null)
        {
            if (!IsInitialized)
            {
                Debug.LogError("SceneManagerMain is not initialized!");
                return;
            }

            if (sceneTransition != null && settings.enableTransitions)
            {
                var transitionDuration = duration ?? settings.defaultTransitionDuration;
                var showLoadingScreen = showLoading ?? settings.showLoadingScreen;

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
            if (!IsInitialized)
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
            if (!IsInitialized)
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
            if (!IsInitialized)
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
            if (!IsInitialized)
            {
                Debug.LogError("SceneManagerMain is not initialized!");
                return;
            }

            if (sceneValidator == null)
                return;

            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            sceneValidator.ValidateScene(currentScene);
        }

        public void Shutdown()
        {
            if (sceneEvents != null)
            {
                sceneEvents.ClearAllCallbacks();
            }

            IsInitialized = false;

            if (settings.enableLogging)
            {
                Debug.Log("[SceneManagerMain] Scene Management System shutdown completed.");
            }
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            Shutdown();
            Instance = null;
        }
    }
}