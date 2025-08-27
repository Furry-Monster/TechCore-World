using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SceneManagement.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Demo
{
    public class SimpleSceneManagerDemo : MonoBehaviour
    {
        private static SimpleSceneManagerDemo Instance { get; set; }

        [Header("Demo Settings")] [SerializeField]
        private string[] testScenes = { "MainMenu", "GameLevel1", "GameLevel2", "Settings" };

        [SerializeField] private string[] saveSlots = { "Save1", "Save2", "Save3", "AutoSave" };
        [SerializeField] private bool showDebugInfo = true;

        private int selectedSceneIndex;
        private int selectedSaveIndex;
        private Vector2 scrollPosition;
        private readonly List<string> logMessages = new();
        private bool showLogs = true;
        private string statusMessage = "Ready";
        private bool showDemo = true;

        private void Awake()
        {
            // 实现单例模式，防止重复实例
            if (Instance == null)
            {
                Instance = this;
                // 让Demo在场景切换时不被销毁
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                // 如果已经存在实例，销毁当前对象
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            // 只有主实例才执行初始化
            if (Instance != this) return;

            // 确保场景管理器已初始化
            EnsureSceneManagerExists();
            SubscribeToEvents();
            AddLog("Simple Scene Manager Demo initialized");
        }

        private void EnsureSceneManagerExists()
        {
            if (SceneManagerMain.Instance == null)
            {
                var managerObject = new GameObject("SceneManager");
                var sceneManager = managerObject.AddComponent<SceneManagerMain>();

                // 配置基本设置
                var settings = new SceneManagerSettings
                {
                    autoInitialize = true,
                    enableLogging = true,
                    persistAcrossScenes = true,
                    enableTransitions = true,
                    enablePreloading = true,
                    validateScenesOnLoad = true
                };

                sceneManager.SetSettings(settings);
                sceneManager.InitializeSceneManager();

                AddLog("SceneManager automatically created and initialized");
            }
        }

        private void SubscribeToEvents()
        {
            if (SceneManagerMain.Instance?.Events != null)
            {
                SceneManagerMain.Instance.Events.OnAnySceneLoadStarted.AddListener(OnSceneLoadStarted);
                SceneManagerMain.Instance.Events.OnAnySceneLoaded.AddListener(OnSceneLoaded);
                SceneManagerMain.Instance.Events.OnAnySceneProgress.AddListener(OnSceneProgress);
                SceneManagerMain.Instance.Events.OnAnySceneError.AddListener(OnSceneError);
                SceneManagerMain.Instance.Events.OnAnyTransitionStarted.AddListener(OnTransitionStarted);
                SceneManagerMain.Instance.Events.OnAnyTransitionCompleted.AddListener(OnTransitionCompleted);
            }

            if (SceneManagerMain.Instance?.DataManager != null)
            {
                SceneManagerMain.Instance.DataManager.OnSaveCompleted += OnSaveCompleted;
                SceneManagerMain.Instance.DataManager.OnLoadCompleted += OnLoadCompleted;
            }

            if (SceneManagerMain.Instance?.Preloader != null)
            {
                SceneManagerMain.Instance.Preloader.OnPreloadCompleted += OnPreloadCompleted;
                SceneManagerMain.Instance.Preloader.OnPreloadFailed += OnPreloadFailed;
            }
        }

        private void OnGUI()
        {
            // 添加一个小按钮来切换Demo显示
            if (GUI.Button(new Rect(10, 10, 100, 30), showDemo ? "Hide Demo" : "Show Demo"))
            {
                showDemo = !showDemo;
            }

            if (!showDemo) return;

            GUILayout.BeginArea(new Rect(10, 50, Screen.width - 20, Screen.height - 70));

            // 标题栏显示持久化状态
            GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(35));
            GUILayout.Label("🎮 Unity Scene Manager Demo", GUILayout.ExpandWidth(true));
            GUILayout.Label("📌 Persistent UI", GUI.skin.box, GUILayout.Width(100));
            if (GUILayout.Button("❌ Destroy Demo", GUILayout.Width(120)))
            {
                DestroyDemo();
                return;
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            // 左侧控制面板
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(300));
            DrawControlPanel();
            GUILayout.EndVertical();

            GUILayout.Space(10);

            // 右侧信息面板
            GUILayout.BeginVertical(GUI.skin.box);
            DrawInfoPanel();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void DrawControlPanel()
        {
            GUILayout.Label("🎛️ Control Panel", GUI.skin.label);
            GUILayout.Space(5);

            // 场景选择
            GUILayout.Label($"Select Scene ({testScenes.Length} available):");
            selectedSceneIndex = GUILayout.SelectionGrid(selectedSceneIndex, testScenes, 2);
            var selectedScene = testScenes[selectedSceneIndex];

            GUILayout.Space(10);

            // 场景操作按钮
            GUILayout.Label("🎬 Scene Operations:");

            GUI.backgroundColor = Color.green;
            if (GUILayout.Button($"Load Scene: {selectedScene}", GUILayout.Height(30)))
            {
                LoadScene(selectedScene);
            }

            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button($"Transition to: {selectedScene}", GUILayout.Height(30)))
            {
                TransitionToScene(selectedScene);
            }

            GUI.backgroundColor = Color.yellow;
            if (GUILayout.Button($"Preload: {selectedScene}", GUILayout.Height(30)))
            {
                PreloadScene(selectedScene);
            }

            GUI.backgroundColor = Color.red;
            if (GUILayout.Button($"Unload: {selectedScene}", GUILayout.Height(30)))
            {
                UnloadScene(selectedScene);
            }

            GUI.backgroundColor = Color.white;

            GUILayout.Space(15);

            // 存档系统
            GUILayout.Label("💾 Save/Load System:");
            GUILayout.Label($"Save Slot ({saveSlots.Length} slots):");
            selectedSaveIndex = GUILayout.SelectionGrid(selectedSaveIndex, saveSlots, 4);
            var selectedSave = saveSlots[selectedSaveIndex];

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = Color.blue;
            if (GUILayout.Button($"Save: {selectedSave}"))
            {
                SaveGame(selectedSave);
            }

            GUI.backgroundColor = Color.magenta;
            if (GUILayout.Button($"Load: {selectedSave}"))
            {
                LoadGame(selectedSave);
            }

            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(15);

            // 其他功能
            GUILayout.Label("🔧 Other Functions:");

            if (GUILayout.Button("Validate Current Scene", GUILayout.Height(25)))
            {
                ValidateScene();
            }

            if (GUILayout.Button("Clear All Logs", GUILayout.Height(25)))
            {
                logMessages.Clear();
            }

            GUILayout.Space(10);

            // 设置开关
            GUILayout.Label("⚙️ Settings:");
            showLogs = GUILayout.Toggle(showLogs, "Show Logs");
            showDebugInfo = GUILayout.Toggle(showDebugInfo, "Show Debug Info");
        }

        private void DrawInfoPanel()
        {
            GUILayout.Label("ℹ️ System Information", GUI.skin.label);
            GUILayout.Space(5);

            // 添加持久化说明
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("📌 Persistent Demo Info:", GUI.skin.label);
            GUILayout.Label("• This GUI survives scene transitions (DontDestroyOnLoad)");
            GUILayout.Label("• You can test scene loading without losing the demo interface");
            GUILayout.Label("• Use 'Hide Demo' button to minimize when not needed");
            GUILayout.EndVertical();
            GUILayout.Space(5);

            if (showDebugInfo)
            {
                DrawSystemStatus();
                GUILayout.Space(10);
            }

            if (showLogs)
            {
                DrawLogPanel();
            }
        }

        private void DrawSystemStatus()
        {
            GUILayout.Label("📊 System Status:", GUI.skin.box);

            var isInitialized = SceneManagerMain.Instance?.IsInitialized() ?? false;
            var currentScene = SceneManager.GetActiveScene().name;

            GUILayout.Label($"🟢 Status: {statusMessage}");
            GUILayout.Label($"🔧 Initialized: {(isInitialized ? "✅" : "❌")}");
            GUILayout.Label($"🎬 Current Scene: {currentScene}");

            if (SceneManagerMain.Instance?.SceneManager != null)
            {
                var loadedScenes = SceneManagerMain.Instance.SceneManager.GetLoadedSceneNames();
                GUILayout.Label($"📚 Loaded Scenes: {loadedScenes.Length}");

                if (loadedScenes.Length > 0)
                {
                    GUILayout.Label($"   📋 {string.Join(", ", loadedScenes)}");
                }
            }

            if (SceneManagerMain.Instance?.Preloader != null)
            {
                var usageStats = SceneManagerMain.Instance.Preloader.GetSceneUsageStats();
                GUILayout.Label($"📈 Usage Stats: {usageStats.Count} scenes tracked");
            }
        }

        private void DrawLogPanel()
        {
            GUILayout.Label("📝 Event Log:", GUI.skin.box);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));

            foreach (var log in logMessages.TakeLast(20))
            {
                GUILayout.Label(log, GUI.skin.textArea);
            }

            GUILayout.EndScrollView();
        }

        // 场景操作方法
        private void LoadScene(string sceneName)
        {
            AddLog($"🔄 Loading scene: {sceneName}");
            statusMessage = $"Loading {sceneName}...";

            if (SceneManagerMain.Instance != null)
            {
                SceneManagerMain.Instance.LoadSceneWithTransition(sceneName, 1f, false);
            }
            else
            {
                SceneManager.LoadScene(sceneName);
            }
        }

        private void TransitionToScene(string sceneName)
        {
            AddLog($"✨ Transitioning to scene: {sceneName}");
            statusMessage = $"Transitioning to {sceneName}...";

            if (SceneManagerMain.Instance != null)
            {
                SceneManagerMain.Instance.LoadSceneWithTransition(sceneName, 2f, true);
            }
            else
            {
                LoadScene(sceneName);
            }
        }

        private void PreloadScene(string sceneName)
        {
            AddLog($"⚡ Preloading scene: {sceneName}");
            statusMessage = $"Preloading {sceneName}...";

            if (SceneManagerMain.Instance != null)
            {
                SceneManagerMain.Instance.PreloadScene(sceneName);
            }
        }

        private void UnloadScene(string sceneName)
        {
            AddLog($"🗑️ Unloading scene: {sceneName}");
            statusMessage = $"Unloading {sceneName}...";

            if (SceneManagerMain.Instance?.SceneManager != null)
            {
                SceneManagerMain.Instance.SceneManager.UnloadScene(sceneName);
            }
        }

        private void SaveGame(string saveName)
        {
            AddLog($"💾 Saving game to: {saveName}");
            statusMessage = $"Saving to {saveName}...";

            if (SceneManagerMain.Instance != null)
            {
                SceneManagerMain.Instance.SaveGame(saveName);
            }
        }

        private void LoadGame(string saveName)
        {
            AddLog($"📂 Loading game from: {saveName}");
            statusMessage = $"Loading from {saveName}...";

            if (SceneManagerMain.Instance != null)
            {
                var success = SceneManagerMain.Instance.LoadGame(saveName);
                if (!success)
                {
                    AddLog($"❌ Failed to load save: {saveName}");
                }
            }
        }

        private void ValidateScene()
        {
            AddLog("🔍 Validating current scene...");
            statusMessage = "Validating scene...";

            if (SceneManagerMain.Instance != null)
            {
                SceneManagerMain.Instance.ValidateCurrentScene();
            }
        }

        // 事件回调
        private void OnSceneLoadStarted(string sceneName)
        {
            AddLog($"🚀 Scene load started: {sceneName}");
            statusMessage = $"Loading {sceneName}...";
        }

        private void OnSceneLoaded(string sceneName, Scene scene)
        {
            AddLog($"✅ Scene loaded: {sceneName}");
            statusMessage = "Ready";
        }

        private void OnSceneProgress(string sceneName, float progress)
        {
            statusMessage = $"Loading {sceneName}... {progress:P1}";
        }

        private void OnSceneError(string sceneName, string error)
        {
            AddLog($"❌ Scene error in {sceneName}: {error}");
            statusMessage = $"Error: {error}";
        }

        private void OnTransitionStarted()
        {
            AddLog("🎭 Scene transition started");
            statusMessage = "Transitioning...";
        }

        private void OnTransitionCompleted()
        {
            AddLog("🎉 Scene transition completed");
            statusMessage = "Ready";
        }

        private void OnSaveCompleted(string saveName, bool success)
        {
            var result = success ? "✅" : "❌";
            AddLog($"{result} Save {(success ? "completed" : "failed")}: {saveName}");
            statusMessage = success ? "Save completed" : "Save failed";
        }

        private void OnLoadCompleted(string saveName, bool success)
        {
            var result = success ? "✅" : "❌";
            AddLog($"{result} Load {(success ? "completed" : "failed")}: {saveName}");
            statusMessage = success ? "Load completed" : "Load failed";
        }

        private void OnPreloadCompleted(string sceneName)
        {
            AddLog($"⚡ Preload completed: {sceneName}");
        }

        private void OnPreloadFailed(string sceneName, string error)
        {
            AddLog($"❌ Preload failed for {sceneName}: {error}");
        }

        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            logMessages.Add($"[{timestamp}] {message}");

            // 限制日志数量
            if (logMessages.Count > 50)
            {
                logMessages.RemoveAt(0);
            }
        }

        private void OnDestroy()
        {
            // 只有主实例才需要清理
            if (Instance != this) return;

            // 取消订阅事件
            if (SceneManagerMain.Instance?.Events != null)
            {
                SceneManagerMain.Instance.Events.OnAnySceneLoadStarted.RemoveListener(OnSceneLoadStarted);
                SceneManagerMain.Instance.Events.OnAnySceneLoaded.RemoveListener(OnSceneLoaded);
                SceneManagerMain.Instance.Events.OnAnySceneProgress.RemoveListener(OnSceneProgress);
                SceneManagerMain.Instance.Events.OnAnySceneError.RemoveListener(OnSceneError);
                SceneManagerMain.Instance.Events.OnAnyTransitionStarted.RemoveListener(OnTransitionStarted);
                SceneManagerMain.Instance.Events.OnAnyTransitionCompleted.RemoveListener(OnTransitionCompleted);
            }

            if (SceneManagerMain.Instance?.DataManager != null)
            {
                SceneManagerMain.Instance.DataManager.OnSaveCompleted -= OnSaveCompleted;
                SceneManagerMain.Instance.DataManager.OnLoadCompleted -= OnLoadCompleted;
            }

            if (SceneManagerMain.Instance?.Preloader != null)
            {
                SceneManagerMain.Instance.Preloader.OnPreloadCompleted -= OnPreloadCompleted;
                SceneManagerMain.Instance.Preloader.OnPreloadFailed -= OnPreloadFailed;
            }

            // 清理单例引用
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void DestroyDemo()
        {
            AddLog("🗑️ Demo destroyed by user");
            Destroy(gameObject);
        }

        // 编辑器快捷键
        [Conditional("UNITY_EDITOR")]
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) LoadScene(testScenes[0]);
            if (Input.GetKeyDown(KeyCode.F2)) LoadScene(testScenes[1]);
            if (Input.GetKeyDown(KeyCode.F3)) TransitionToScene(testScenes[2]);
            if (Input.GetKeyDown(KeyCode.F5)) SaveGame(saveSlots[0]);
            if (Input.GetKeyDown(KeyCode.F9)) LoadGame(saveSlots[0]);
        }
    }
}