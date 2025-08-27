using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneManagement.Runtime
{
    [Serializable]
    public class PreloadSettings
    {
        public string sceneName;
        public bool autoPreload = true;
        public float preloadDelay;
        public int priority;
    }

    public class ScenePreloader : MonoBehaviour
    {
        public static ScenePreloader Instance { get; private set; }

        [Header("Preload Configuration")] [SerializeField]
        private List<PreloadSettings> preloadQueue = new();

        [SerializeField] private int maxConcurrentPreloads = 3;
        [SerializeField] private bool preloadOnStart = true;
        [SerializeField] private bool enableSmartPreloading = true;

        private readonly Queue<PreloadSettings> pendingPreloads = new();
        private readonly HashSet<string> currentlyPreloading = new();
        private Dictionary<string, float> sceneUsageFrequency = new();
        private readonly Dictionary<string, DateTime> lastAccessTime = new();

        public event Action<string> OnPreloadStarted;
        public event Action<string> OnPreloadCompleted;
        public event Action<string, float> OnPreloadProgress;
        public event Action<string, string> OnPreloadFailed;

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

        private void Start()
        {
            if (preloadOnStart)
            {
                StartPreloadQueue();
            }
        }

        private void Initialize()
        {
            LoadUsageData();

            if (SceneManagerCore.Instance != null)
            {
                SceneManagerCore.Instance.OnSceneLoaded += OnSceneLoadedCallback;
                SceneManagerCore.Instance.OnSceneLoadStarted += OnSceneAccessedCallback;
            }

            SortPreloadQueueByPriority();
        }

        private void OnDestroy()
        {
            SaveUsageData();
        }

        public void AddToPreloadQueue(string sceneName, int priority = 0, float delay = 0f)
        {
            if (string.IsNullOrEmpty(sceneName))
                return;

            PreloadSettings settings = new PreloadSettings
            {
                sceneName = sceneName,
                priority = priority,
                preloadDelay = delay,
                autoPreload = true
            };

            preloadQueue.Add(settings);
            SortPreloadQueueByPriority();
        }

        public void RemoveFromPreloadQueue(string sceneName)
        {
            preloadQueue.RemoveAll(p => p.sceneName == sceneName);
        }

        public void StartPreloadQueue()
        {
            pendingPreloads.Clear();

            foreach (var preloadSetting in preloadQueue)
            {
                if (preloadSetting.autoPreload)
                {
                    pendingPreloads.Enqueue(preloadSetting);
                }
            }

            StartCoroutine(ProcessPreloadQueue());
        }

        public void PreloadScene(string sceneName, int priority = 0)
        {
            if (string.IsNullOrEmpty(sceneName) || currentlyPreloading.Contains(sceneName))
                return;

            if (SceneManagerCore.Instance != null &&
                (SceneManagerCore.Instance.IsSceneLoaded(sceneName) ||
                 SceneManagerCore.Instance.IsScenePreloaded(sceneName)))
                return;

            StartCoroutine(PreloadSceneCoroutine(sceneName, priority));
        }

        private IEnumerator ProcessPreloadQueue()
        {
            while (pendingPreloads.Count > 0)
            {
                while (currentlyPreloading.Count >= maxConcurrentPreloads)
                {
                    yield return new WaitForSeconds(0.1f);
                }

                PreloadSettings settings = pendingPreloads.Dequeue();

                if (settings.preloadDelay > 0)
                {
                    yield return new WaitForSeconds(settings.preloadDelay);
                }

                PreloadScene(settings.sceneName, settings.priority);
                yield return new WaitForSeconds(0.1f);
            }
        }

        private IEnumerator PreloadSceneCoroutine(string sceneName, int priority)
        {
            currentlyPreloading.Add(sceneName);
            OnPreloadStarted?.Invoke(sceneName);

            if (SceneManagerCore.Instance != null)
            {
                try
                {
                    SceneManagerCore.Instance.PreloadScene(sceneName);
                }
                catch (Exception ex)
                {
                    OnPreloadFailed?.Invoke(sceneName, ex.Message);
                    currentlyPreloading.Remove(sceneName);
                    yield break;
                }

                var timeout = 30f + (priority * 5f); // 优先级影响超时时间
                var elapsed = 0f;

                while (!SceneManagerCore.Instance.IsScenePreloaded(sceneName) && elapsed < timeout)
                {
                    elapsed += Time.deltaTime;
                    OnPreloadProgress?.Invoke(sceneName, elapsed / timeout);
                    yield return null;
                }

                if (elapsed >= timeout)
                {
                    OnPreloadFailed?.Invoke(sceneName, "Preload timeout");
                }
                else
                {
                    OnPreloadCompleted?.Invoke(sceneName);
                    UpdateSceneUsage(sceneName);
                }
            }
            else
            {
                OnPreloadFailed?.Invoke(sceneName, "SceneManagerCore instance not found");
            }

            currentlyPreloading.Remove(sceneName);
        }

        public void SmartPreload(string currentSceneName)
        {
            if (!enableSmartPreloading)
                return;

            List<string> recommendedScenes = GetRecommendedScenes(currentSceneName);

            foreach (string sceneName in recommendedScenes)
            {
                if (currentlyPreloading.Count >= maxConcurrentPreloads)
                    break;

                PreloadScene(sceneName, GetSmartPreloadPriority(sceneName));
            }
        }

        private List<string> GetRecommendedScenes(string currentSceneName)
        {
            List<string> recommended = new List<string>();

            var sortedScenes = new List<KeyValuePair<string, float>>();
            foreach (var kvp in sceneUsageFrequency)
            {
                if (kvp.Key != currentSceneName)
                {
                    float score = CalculatePreloadScore(kvp.Key);
                    sortedScenes.Add(new KeyValuePair<string, float>(kvp.Key, score));
                }
            }

            sortedScenes.Sort((a, b) => b.Value.CompareTo(a.Value));

            for (int i = 0; i < Mathf.Min(3, sortedScenes.Count); i++)
            {
                recommended.Add(sortedScenes[i].Key);
            }

            return recommended;
        }

        private float CalculatePreloadScore(string sceneName)
        {
            var frequencyScore = sceneUsageFrequency.GetValueOrDefault(sceneName, 0f);

            var recencyScore = 0f;
            if (lastAccessTime.TryGetValue(sceneName, out var time))
            {
                var timeSinceAccess = DateTime.Now - time;
                recencyScore = Mathf.Max(0f, 1f - (float)(timeSinceAccess.TotalHours / 24f));
            }

            return frequencyScore * 0.7f + recencyScore * 0.3f;
        }

        private int GetSmartPreloadPriority(string sceneName)
        {
            float score = CalculatePreloadScore(sceneName);
            return Mathf.RoundToInt(score * 100f);
        }

        private void UpdateSceneUsage(string sceneName)
        {
            if (!sceneUsageFrequency.TryAdd(sceneName, 1f))
            {
                sceneUsageFrequency[sceneName]++;
            }

            lastAccessTime[sceneName] = DateTime.Now;
        }

        private void OnSceneLoadedCallback(string sceneName, Scene scene)
        {
            UpdateSceneUsage(sceneName);

            if (enableSmartPreloading)
            {
                SmartPreload(sceneName);
            }
        }

        private void OnSceneAccessedCallback(string sceneName)
        {
            lastAccessTime[sceneName] = DateTime.Now;
        }

        private void SortPreloadQueueByPriority()
        {
            preloadQueue.Sort((a, b) => b.priority.CompareTo(a.priority));
        }

        private void LoadUsageData()
        {
            string json = PlayerPrefs.GetString("SceneUsageData", "{}");
            try
            {
                SceneUsageData data = JsonUtility.FromJson<SceneUsageData>(json);
                if (data is { usageFrequency: not null })
                {
                    sceneUsageFrequency = data.usageFrequency;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load scene usage data: {ex.Message}");
            }
        }

        private void SaveUsageData()
        {
            try
            {
                SceneUsageData data = new SceneUsageData
                {
                    usageFrequency = sceneUsageFrequency
                };
                string json = JsonUtility.ToJson(data);
                PlayerPrefs.SetString("SceneUsageData", json);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to save scene usage data: {ex.Message}");
            }
        }

        public bool IsPreloading(string sceneName)
        {
            return currentlyPreloading.Contains(sceneName);
        }

        public void ClearPreloadQueue()
        {
            preloadQueue.Clear();
            pendingPreloads.Clear();
        }

        public void SetMaxConcurrentPreloads(int count)
        {
            maxConcurrentPreloads = Mathf.Max(1, count);
        }

        public void EnableSmartPreloading(bool enable)
        {
            enableSmartPreloading = enable;
        }

        public Dictionary<string, float> GetSceneUsageStats()
        {
            return new Dictionary<string, float>(sceneUsageFrequency);
        }
    }

    [Serializable]
    public class SceneUsageData
    {
        public Dictionary<string, float> usageFrequency = new();
    }
}