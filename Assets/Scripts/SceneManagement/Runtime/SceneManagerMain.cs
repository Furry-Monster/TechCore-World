using System;
using UnityEngine;

namespace SceneManagement.Runtime
{
    [Serializable]
    public class SceneManagerSettings
    {
        [Header("Core Settings")] 
        public bool autoInitialize = true;
        public bool enableLogging = true;
        public bool persistAcrossScenes = true;

        [Header("Loading Settings")] 
        public bool useAsyncLoading = true;
        public bool enablePreloading = true;
        public int maxConcurrentPreloads = 3;

        [Header("Transition Settings")] 
        public bool enableTransitions = true;
        public float defaultTransitionDuration = 1.0f;
        public bool showLoadingScreen = true;

        [Header("Validation Settings")] 
        public bool validateScenesOnLoad = true;
        public bool blockLoadOnCriticalErrors = true;
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

        // MonoBehaviour组件（需要Unity生命周期的）
        private SceneManagerCore sceneManagerCore;
        private SceneTransition sceneTransition;

        // 纯C#类组件（手动管理的）
        private ScenePreloader scenePreloader;
        private SceneEvents sceneEvents;
        private SceneValidator sceneValidator;

        // 公共访问器
        public SceneManagerCore SceneManager => sceneManagerCore;
        public SceneTransition Transition => sceneTransition;
        public ScenePreloader Preloader => scenePreloader;
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

        private void Update()
        {
            // 驱动纯C#类的更新
            sceneEvents?.Update();
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

            CreateOrFindMonoBehaviourComponents();
            InitializePureCSharpComponents();
            ConfigureComponents();

            IsInitialized = true;

            if (settings.enableLogging)
            {
                Debug.Log("[SceneManagerMain] Scene Management System initialized successfully!");
            }
        }

        private void CreateOrFindMonoBehaviourComponents()
        {
            // 核心组件（必须有）
            sceneManagerCore ??= GetComponent<SceneManagerCore>()
                                 ?? gameObject.AddComponent<SceneManagerCore>();

            // 过渡组件（可选）
            if (settings.enableTransitions)
            {
                sceneTransition ??= GetComponent<SceneTransition>()
                                    ?? gameObject.AddComponent<SceneTransition>();
            }
        }

        private void InitializePureCSharpComponents()
        {
            // 初始化纯C#类组件
            scenePreloader = ScenePreloader.Instance;
            sceneEvents = SceneEvents.Instance;
            sceneValidator = SceneValidator.Instance;

            // 设置组件间的依赖关系
            scenePreloader.Initialize(sceneManagerCore);
            sceneEvents.Initialize(sceneManagerCore, sceneTransition, scenePreloader, sceneValidator);
            
            if (settings.validateScenesOnLoad)
            {
                sceneValidator.Initialize(
                    settings.validateScenesOnLoad,
                    true,
                    settings.enableLogging,
                    settings.blockLoadOnCriticalErrors
                );
            }
        }

        private void ConfigureComponents()
        {
            // 配置预加载器
            if (scenePreloader != null)
            {
                scenePreloader.Configure(
                    settings.maxConcurrentPreloads,
                    settings.enablePreloading
                );
            }

            // 配置验证器
            if (sceneValidator != null && settings.validateScenesOnLoad)
            {
                sceneValidator.Configure(
                    validation: settings.validateScenesOnLoad,
                    autoValidate: true,
                    logging: settings.enableLogging,
                    blockOnCritical: settings.blockLoadOnCriticalErrors
                );
            }

            // 配置事件系统
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

            if (scenePreloader != null)
            {
                scenePreloader.Shutdown();
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

        // 配置API
        public void ConfigurePreloader(int maxConcurrent, bool smartPreloading = true, System.Collections.Generic.List<PreloadSettings> initialQueue = null)
        {
            scenePreloader?.Configure(maxConcurrent, smartPreloading, initialQueue);
        }

        public void ConfigureValidator(System.Collections.Generic.List<SceneValidationRule> rules = null, bool validation = true, bool autoValidate = true, bool logging = true, bool blockOnCritical = true)
        {
            sceneValidator?.Configure(rules, validation, autoValidate, logging, blockOnCritical);
        }

        public void ConfigureEvents(bool asyncProcessing = true, int maxEvents = 10, bool logging = true)
        {
            sceneEvents?.SetEventSettings(asyncProcessing, maxEvents, logging);
        }
    }
}