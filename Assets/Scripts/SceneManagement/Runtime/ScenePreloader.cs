using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

    public class ScenePreloader
    {
        private static ScenePreloader instance;
        public static ScenePreloader Instance => instance ??= new ScenePreloader();

        private List<PreloadSettings> preloadQueue = new();
        private int maxConcurrentPreloads = 3;
        private bool enableSmartPreloading = true;

        private readonly Queue<PreloadSettings> pendingPreloads = new();
        private readonly HashSet<string> currentlyPreloading = new();
        private Dictionary<string, float> sceneUsageFrequency = new();
        private readonly Dictionary<string, DateTime> lastAccessTime = new();

        public event Action<string> OnPreloadStarted;
        public event Action<string> OnPreloadCompleted;
        public event Action<string, float> OnPreloadProgress;
        public event Action<string, string> OnPreloadFailed;

        private ScenePreloader()
        {
            LoadUsageData();
        }

        public void Initialize(SceneManagerCore coreManager)
        {
            if (coreManager != null)
            {
                coreManager.OnSceneLoaded += OnSceneLoadedCallback;
                coreManager.OnSceneLoadStarted += OnSceneAccessedCallback;
            }

            SortPreloadQueueByPriority();
        }

        public void Shutdown()
        {
            SaveUsageData();
        }

        public void AddToPreloadQueue(string sceneName, int priority = 0, float delay = 0f)
        {
            if (string.IsNullOrEmpty(sceneName))
                return;

            var settings = new PreloadSettings
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

            // 委托给SceneManagerCore来处理协程
            if (SceneManagerCore.Instance != null)
            {
                SceneManagerCore.Instance.StartCoroutine(ProcessPreloadQueueCoroutine());
            }
        }

        public void PreloadScene(string sceneName, int priority = 0)
        {
            if (string.IsNullOrEmpty(sceneName) || currentlyPreloading.Contains(sceneName))
                return;

            if (SceneManagerCore.Instance != null &&
                (SceneManagerCore.Instance.IsSceneLoaded(sceneName) ||
                 SceneManagerCore.Instance.IsScenePreloaded(sceneName)))
                return;

            // 委托给SceneManagerCore来处理协程
            if (SceneManagerCore.Instance != null)
            {
                SceneManagerCore.Instance.StartCoroutine(PreloadSceneCoroutine(sceneName, priority));
            }
        }

        private System.Collections.IEnumerator ProcessPreloadQueueCoroutine()
        {
            while (pendingPreloads.Count > 0)
            {
                while (currentlyPreloading.Count >= maxConcurrentPreloads)
                {
                    yield return new UnityEngine.WaitForSeconds(0.1f);
                }

                var settings = pendingPreloads.Dequeue();

                if (settings.preloadDelay > 0)
                {
                    yield return new UnityEngine.WaitForSeconds(settings.preloadDelay);
                }

                PreloadScene(settings.sceneName, settings.priority);
                yield return new UnityEngine.WaitForSeconds(0.1f);
            }
        }

        private System.Collections.IEnumerator PreloadSceneCoroutine(string sceneName, int priority)
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

                var timeout = 30f + (priority * 5f);
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

            var recommendedScenes = GetRecommendedScenes(currentSceneName);

            foreach (var sceneName in recommendedScenes)
            {
                if (currentlyPreloading.Count >= maxConcurrentPreloads)
                    break;

                PreloadScene(sceneName, GetSmartPreloadPriority(sceneName));
            }
        }

        private List<string> GetRecommendedScenes(string currentSceneName)
        {
            var recommended = new List<string>();

            var sortedScenes = new List<KeyValuePair<string, float>>();
            foreach (var kvp in sceneUsageFrequency)
            {
                if (kvp.Key != currentSceneName)
                {
                    var score = CalculatePreloadScore(kvp.Key);
                    sortedScenes.Add(new KeyValuePair<string, float>(kvp.Key, score));
                }
            }

            sortedScenes.Sort((a, b) => b.Value.CompareTo(a.Value));

            for (var i = 0; i < Mathf.Min(3, sortedScenes.Count); i++)
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
            var score = CalculatePreloadScore(sceneName);
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

        private void OnSceneLoadedCallback(string sceneName, UnityEngine.SceneManagement.Scene scene)
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
            var json = PlayerPrefs.GetString("SceneUsageData", "{}");
            try
            {
                var data = JsonUtility.FromJson<SceneUsageData>(json);
                if (data?.usageFrequency != null)
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
                var data = new SceneUsageData
                {
                    usageFrequency = sceneUsageFrequency
                };
                var json = JsonUtility.ToJson(data);
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

        // 配置方法
        public void Configure(int maxConcurrent, bool smartPreloading, List<PreloadSettings> initialQueue = null)
        {
            maxConcurrentPreloads = maxConcurrent;
            enableSmartPreloading = smartPreloading;
            
            if (initialQueue != null)
            {
                preloadQueue = new List<PreloadSettings>(initialQueue);
                SortPreloadQueueByPriority();
            }
        }
    }

    [Serializable]
    public class SceneUsageData
    {
        public Dictionary<string, float> usageFrequency = new();
    }
}