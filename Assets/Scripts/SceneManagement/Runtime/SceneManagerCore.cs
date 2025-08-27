using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneManagement.Runtime
{
    public class SceneManagerCore : MonoBehaviour
    {
        public static SceneManagerCore Instance { get; private set; }

        [Header("Scene Loading Settings")] [SerializeField]
        private bool loadScenesAdditively = true;

        [SerializeField] private bool preloadScenes;
        [SerializeField] private LoadSceneMode defaultLoadMode = LoadSceneMode.Single;

        private readonly Dictionary<string, Scene> loadedScenes = new();
        private readonly Dictionary<string, AsyncOperation> preloadedScenes = new();
        private readonly HashSet<string> scenesInTransition = new();

        public event Action<string> OnSceneLoadStarted;
        public event Action<string, Scene> OnSceneLoaded;
        public event Action<string> OnSceneUnloadStarted;
        public event Action<string> OnSceneUnloaded;
        public event Action<string, float> OnSceneLoadProgress;
        public event Action<string, string> OnSceneLoadFailed;

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
            SceneManager.sceneLoaded += OnSceneLoadedCallback;
            SceneManager.sceneUnloaded += OnSceneUnloadedCallback;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    loadedScenes[scene.name] = scene;
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoadedCallback;
                SceneManager.sceneUnloaded -= OnSceneUnloadedCallback;
            }
        }

        public void LoadScene(string sceneName, LoadSceneMode loadMode = LoadSceneMode.Single)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                OnSceneLoadFailed?.Invoke(sceneName, "Scene name is null or empty");
                return;
            }

            if (scenesInTransition.Contains(sceneName))
            {
                OnSceneLoadFailed?.Invoke(sceneName, "Scene is already in transition");
                return;
            }

            if (loadedScenes.ContainsKey(sceneName))
            {
                OnSceneLoadFailed?.Invoke(sceneName, "Scene is already loaded");
                return;
            }

            StartCoroutine(LoadSceneAsync(sceneName, loadMode));
        }

        public void LoadSceneAsync(string sceneName, Action<Scene> onComplete = null,
            LoadSceneMode loadMode = LoadSceneMode.Single)
        {
            StartCoroutine(LoadSceneAsyncCoroutine(sceneName, loadMode, onComplete));
        }

        private IEnumerator LoadSceneAsync(string sceneName, LoadSceneMode loadMode)
        {
            yield return LoadSceneAsyncCoroutine(sceneName, loadMode);
        }

        private IEnumerator LoadSceneAsyncCoroutine(string sceneName, LoadSceneMode loadMode,
            Action<Scene> onComplete = null)
        {
            scenesInTransition.Add(sceneName);
            OnSceneLoadStarted?.Invoke(sceneName);

            AsyncOperation asyncOperation;

            if (preloadedScenes.ContainsKey(sceneName))
            {
                asyncOperation = preloadedScenes[sceneName];
                preloadedScenes.Remove(sceneName);
            }
            else
            {
                asyncOperation = SceneManager.LoadSceneAsync(sceneName, loadMode);
            }

            if (asyncOperation == null)
            {
                scenesInTransition.Remove(sceneName);
                OnSceneLoadFailed?.Invoke(sceneName, "Failed to start scene loading operation");
                yield break;
            }

            asyncOperation.allowSceneActivation = false;

            while (asyncOperation.progress < 0.9f)
            {
                OnSceneLoadProgress?.Invoke(sceneName, asyncOperation.progress);
                yield return null;
            }

            OnSceneLoadProgress?.Invoke(sceneName, 1.0f);
            asyncOperation.allowSceneActivation = true;

            yield return asyncOperation;

            scenesInTransition.Remove(sceneName);
            onComplete?.Invoke(SceneManager.GetSceneByName(sceneName));
        }

        public void UnloadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            if (scenesInTransition.Contains(sceneName))
            {
                return;
            }

            if (!loadedScenes.ContainsKey(sceneName))
            {
                return;
            }

            StartCoroutine(UnloadSceneAsync(sceneName));
        }

        private IEnumerator UnloadSceneAsync(string sceneName)
        {
            scenesInTransition.Add(sceneName);
            OnSceneUnloadStarted?.Invoke(sceneName);

            var asyncOperation = SceneManager.UnloadSceneAsync(sceneName);

            if (asyncOperation == null)
            {
                scenesInTransition.Remove(sceneName);
                yield break;
            }

            yield return asyncOperation;

            scenesInTransition.Remove(sceneName);
            loadedScenes.Remove(sceneName);
        }

        public void PreloadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || preloadedScenes.ContainsKey(sceneName) ||
                loadedScenes.ContainsKey(sceneName))
            {
                return;
            }

            var asyncOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (asyncOperation != null)
            {
                asyncOperation.allowSceneActivation = false;
                preloadedScenes[sceneName] = asyncOperation;
            }
        }

        public void ActivatePreloadedScene(string sceneName)
        {
            if (preloadedScenes.ContainsKey(sceneName))
            {
                preloadedScenes[sceneName].allowSceneActivation = true;
                preloadedScenes.Remove(sceneName);
            }
        }

        public bool IsSceneLoaded(string sceneName)
        {
            return loadedScenes.ContainsKey(sceneName);
        }

        public bool IsScenePreloaded(string sceneName)
        {
            return preloadedScenes.ContainsKey(sceneName);
        }

        public bool IsSceneInTransition(string sceneName)
        {
            return scenesInTransition.Contains(sceneName);
        }

        public Scene GetLoadedScene(string sceneName)
        {
            loadedScenes.TryGetValue(sceneName, out var scene);
            return scene;
        }

        public string[] GetLoadedSceneNames()
        {
            var sceneNames = new string[loadedScenes.Count];
            loadedScenes.Keys.CopyTo(sceneNames, 0);
            return sceneNames;
        }

        public string GetActiveSceneName()
        {
            return SceneManager.GetActiveScene().name;
        }

        public void SetActiveScene(string sceneName)
        {
            if (loadedScenes.TryGetValue(sceneName, out var scene))
            {
                SceneManager.SetActiveScene(scene);
            }
        }

        private void OnSceneLoadedCallback(Scene scene, LoadSceneMode mode)
        {
            loadedScenes[scene.name] = scene;
            OnSceneLoaded?.Invoke(scene.name, scene);
        }

        private void OnSceneUnloadedCallback(Scene scene)
        {
            loadedScenes.Remove(scene.name);
            OnSceneUnloaded?.Invoke(scene.name);
        }
    }
}