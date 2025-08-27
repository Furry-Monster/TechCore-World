using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SceneManagement.Runtime
{
    [Serializable]
    public class SceneEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public class SceneLoadEvent : UnityEvent<string, UnityEngine.SceneManagement.Scene>
    {
    }

    [Serializable]
    public class SceneProgressEvent : UnityEvent<string, float>
    {
    }

    [Serializable]
    public class SceneErrorEvent : UnityEvent<string, string>
    {
    }

    [Serializable]
    public class SceneTransitionEvent : UnityEvent
    {
    }

    [Flags]
    public enum SceneEventType
    {
        None = 0,
        LoadStarted = 1 << 0,
        LoadCompleted = 1 << 1,
        LoadProgress = 1 << 2,
        LoadFailed = 1 << 3,
        UnloadStarted = 1 << 4,
        UnloadCompleted = 1 << 5,
        TransitionStarted = 1 << 6,
        TransitionCompleted = 1 << 7,
        PreloadStarted = 1 << 8,
        PreloadCompleted = 1 << 9,
        ValidationCompleted = 1 << 10,
        All = ~0
    }

    [Serializable]
    public class SceneEventListener
    {
        public string listenerName;
        public SceneEventType eventTypes = SceneEventType.All;
        public bool isEnabled = true;
        public string targetSceneName = "";
        public SceneEvent onSceneEvent;
        public SceneLoadEvent onSceneLoadEvent;
        public SceneProgressEvent onSceneProgressEvent;
        public SceneErrorEvent onSceneErrorEvent;
        public SceneTransitionEvent onSceneTransitionEvent;
    }

    public class SceneEvents : MonoBehaviour
    {
        public static SceneEvents Instance { get; private set; }

        [Header("Event Listeners")] [SerializeField]
        private List<SceneEventListener> eventListeners = new List<SceneEventListener>();

        [Header("Global Events")] public SceneEvent OnAnySceneLoadStarted;
        public SceneLoadEvent OnAnySceneLoaded;
        public SceneEvent OnAnySceneUnloadStarted;
        public SceneEvent OnAnySceneUnloaded;
        public SceneProgressEvent OnAnySceneProgress;
        public SceneErrorEvent OnAnySceneError;
        public SceneTransitionEvent OnAnyTransitionStarted;
        public SceneTransitionEvent OnAnyTransitionCompleted;

        private Dictionary<string, List<System.Action<string>>> sceneSpecificCallbacks =
            new Dictionary<string, List<System.Action<string>>>();

        private Dictionary<SceneEventType, List<System.Action<string, object[]>>> globalCallbacks =
            new Dictionary<SceneEventType, List<System.Action<string, object[]>>>();

        private Queue<SceneEventData> eventQueue = new Queue<SceneEventData>();
        private bool isProcessingEvents = false;

        [Header("Event Settings")] [SerializeField]
        private bool processEventsAsync = true;

        [SerializeField] private int maxEventsPerFrame = 10;
        [SerializeField] private bool logEvents = false;

        private struct SceneEventData
        {
            public SceneEventType eventType;
            public string sceneName;
            public object[] parameters;
            public DateTime timestamp;
        }

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
            InitializeCallbackDictionaries();
            SubscribeToSceneManagerEvents();
        }

        private void Update()
        {
            if (processEventsAsync && eventQueue.Count > 0 && !isProcessingEvents)
            {
                StartCoroutine(ProcessEventQueue());
            }
        }

        private void InitializeCallbackDictionaries()
        {
            foreach (SceneEventType eventType in System.Enum.GetValues(typeof(SceneEventType)))
            {
                if (eventType != SceneEventType.None && eventType != SceneEventType.All)
                {
                    globalCallbacks[eventType] = new List<System.Action<string, object[]>>();
                }
            }
        }

        private void SubscribeToSceneManagerEvents()
        {
            if (SceneManagerCore.Instance != null)
            {
                SceneManagerCore.Instance.OnSceneLoadStarted += OnSceneLoadStartedCallback;
                SceneManagerCore.Instance.OnSceneLoaded += OnSceneLoadedCallback;
                SceneManagerCore.Instance.OnSceneUnloadStarted += OnSceneUnloadStartedCallback;
                SceneManagerCore.Instance.OnSceneUnloaded += OnSceneUnloadedCallback;
                SceneManagerCore.Instance.OnSceneLoadProgress += OnSceneLoadProgressCallback;
                SceneManagerCore.Instance.OnSceneLoadFailed += OnSceneLoadFailedCallback;
            }

            if (SceneTransition.Instance != null)
            {
                SceneTransition.Instance.OnTransitionStarted += OnTransitionStartedCallback;
                SceneTransition.Instance.OnTransitionCompleted += OnTransitionCompletedCallback;
            }

            if (ScenePreloader.Instance != null)
            {
                ScenePreloader.Instance.OnPreloadStarted += OnPreloadStartedCallback;
                ScenePreloader.Instance.OnPreloadCompleted += OnPreloadCompletedCallback;
            }

            if (SceneValidator.Instance != null)
            {
                SceneValidator.Instance.OnSceneValidated += OnSceneValidatedCallback;
            }
        }

        public void TriggerEvent(SceneEventType eventType, string sceneName, params object[] parameters)
        {
            if (processEventsAsync)
            {
                EnqueueEvent(eventType, sceneName, parameters);
            }
            else
            {
                ProcessEvent(eventType, sceneName, parameters);
            }
        }

        private void EnqueueEvent(SceneEventType eventType, string sceneName, object[] parameters)
        {
            SceneEventData eventData = new SceneEventData
            {
                eventType = eventType,
                sceneName = sceneName,
                parameters = parameters,
                timestamp = DateTime.Now
            };

            eventQueue.Enqueue(eventData);

            if (logEvents)
            {
                Debug.Log($"[SceneEvents] Enqueued event: {eventType} for scene: {sceneName}");
            }
        }

        private System.Collections.IEnumerator ProcessEventQueue()
        {
            isProcessingEvents = true;
            int eventsProcessed = 0;

            while (eventQueue.Count > 0 && eventsProcessed < maxEventsPerFrame)
            {
                SceneEventData eventData = eventQueue.Dequeue();
                ProcessEvent(eventData.eventType, eventData.sceneName, eventData.parameters);
                eventsProcessed++;

                if (eventsProcessed >= maxEventsPerFrame)
                {
                    yield return null;
                    eventsProcessed = 0;
                }
            }

            isProcessingEvents = false;
        }

        private void ProcessEvent(SceneEventType eventType, string sceneName, object[] parameters)
        {
            if (logEvents)
            {
                Debug.Log($"[SceneEvents] Processing event: {eventType} for scene: {sceneName}");
            }

            InvokeGlobalEvents(eventType, sceneName, parameters);
            InvokeEventListeners(eventType, sceneName, parameters);
            InvokeGlobalCallbacks(eventType, sceneName, parameters);
            InvokeSceneSpecificCallbacks(sceneName);
        }

        private void InvokeGlobalEvents(SceneEventType eventType, string sceneName, object[] parameters)
        {
            try
            {
                switch (eventType)
                {
                    case SceneEventType.LoadStarted:
                        OnAnySceneLoadStarted?.Invoke(sceneName);
                        break;

                    case SceneEventType.LoadCompleted:
                        if (parameters.Length > 0 && parameters[0] is UnityEngine.SceneManagement.Scene)
                        {
                            OnAnySceneLoaded?.Invoke(sceneName, (UnityEngine.SceneManagement.Scene)parameters[0]);
                        }

                        break;

                    case SceneEventType.UnloadStarted:
                        OnAnySceneUnloadStarted?.Invoke(sceneName);
                        break;

                    case SceneEventType.UnloadCompleted:
                        OnAnySceneUnloaded?.Invoke(sceneName);
                        break;

                    case SceneEventType.LoadProgress:
                        if (parameters.Length > 0 && parameters[0] is float)
                        {
                            OnAnySceneProgress?.Invoke(sceneName, (float)parameters[0]);
                        }

                        break;

                    case SceneEventType.LoadFailed:
                        if (parameters.Length > 0 && parameters[0] is string)
                        {
                            OnAnySceneError?.Invoke(sceneName, (string)parameters[0]);
                        }

                        break;

                    case SceneEventType.TransitionStarted:
                        OnAnyTransitionStarted?.Invoke();
                        break;

                    case SceneEventType.TransitionCompleted:
                        OnAnyTransitionCompleted?.Invoke();
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SceneEvents] Error invoking global events: {ex.Message}");
            }
        }

        private void InvokeEventListeners(SceneEventType eventType, string sceneName, object[] parameters)
        {
            foreach (var listener in eventListeners)
            {
                if (!listener.isEnabled || !listener.eventTypes.HasFlag(eventType))
                    continue;

                if (!string.IsNullOrEmpty(listener.targetSceneName) && listener.targetSceneName != sceneName)
                    continue;

                try
                {
                    switch (eventType)
                    {
                        case SceneEventType.LoadStarted:
                        case SceneEventType.UnloadStarted:
                        case SceneEventType.UnloadCompleted:
                        case SceneEventType.PreloadStarted:
                        case SceneEventType.PreloadCompleted:
                            listener.onSceneEvent?.Invoke(sceneName);
                            break;

                        case SceneEventType.LoadCompleted:
                            if (parameters.Length > 0 && parameters[0] is UnityEngine.SceneManagement.Scene)
                            {
                                listener.onSceneLoadEvent?.Invoke(sceneName,
                                    (UnityEngine.SceneManagement.Scene)parameters[0]);
                            }

                            break;

                        case SceneEventType.LoadProgress:
                            if (parameters.Length > 0 && parameters[0] is float)
                            {
                                listener.onSceneProgressEvent?.Invoke(sceneName, (float)parameters[0]);
                            }

                            break;

                        case SceneEventType.LoadFailed:
                            if (parameters.Length > 0 && parameters[0] is string)
                            {
                                listener.onSceneErrorEvent?.Invoke(sceneName, (string)parameters[0]);
                            }

                            break;

                        case SceneEventType.TransitionStarted:
                        case SceneEventType.TransitionCompleted:
                            listener.onSceneTransitionEvent?.Invoke();
                            break;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(
                        $"[SceneEvents] Error invoking event listener '{listener.listenerName}': {ex.Message}");
                }
            }
        }

        private void InvokeGlobalCallbacks(SceneEventType eventType, string sceneName, object[] parameters)
        {
            if (globalCallbacks.ContainsKey(eventType))
            {
                var callbacks = globalCallbacks[eventType];
                for (int i = callbacks.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        callbacks[i]?.Invoke(sceneName, parameters);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[SceneEvents] Error invoking global callback: {ex.Message}");
                        callbacks.RemoveAt(i);
                    }
                }
            }
        }

        private void InvokeSceneSpecificCallbacks(string sceneName)
        {
            if (sceneSpecificCallbacks.ContainsKey(sceneName))
            {
                var callbacks = sceneSpecificCallbacks[sceneName];
                for (int i = callbacks.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        callbacks[i]?.Invoke(sceneName);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[SceneEvents] Error invoking scene-specific callback: {ex.Message}");
                        callbacks.RemoveAt(i);
                    }
                }
            }
        }

        public void Subscribe(SceneEventType eventType, System.Action<string, object[]> callback)
        {
            if (callback == null) return;

            if (!globalCallbacks.ContainsKey(eventType))
            {
                globalCallbacks[eventType] = new List<System.Action<string, object[]>>();
            }

            globalCallbacks[eventType].Add(callback);
        }

        public void Unsubscribe(SceneEventType eventType, System.Action<string, object[]> callback)
        {
            if (callback == null || !globalCallbacks.ContainsKey(eventType)) return;

            globalCallbacks[eventType].Remove(callback);
        }

        public void SubscribeToScene(string sceneName, System.Action<string> callback)
        {
            if (string.IsNullOrEmpty(sceneName) || callback == null) return;

            if (!sceneSpecificCallbacks.ContainsKey(sceneName))
            {
                sceneSpecificCallbacks[sceneName] = new List<System.Action<string>>();
            }

            sceneSpecificCallbacks[sceneName].Add(callback);
        }

        public void UnsubscribeFromScene(string sceneName, System.Action<string> callback)
        {
            if (string.IsNullOrEmpty(sceneName) || callback == null) return;

            if (sceneSpecificCallbacks.ContainsKey(sceneName))
            {
                sceneSpecificCallbacks[sceneName].Remove(callback);
            }
        }

        public void AddEventListener(SceneEventListener listener)
        {
            if (listener != null && !string.IsNullOrEmpty(listener.listenerName))
            {
                eventListeners.Add(listener);
            }
        }

        public void RemoveEventListener(string listenerName)
        {
            eventListeners.RemoveAll(l => l.listenerName == listenerName);
        }

        public void EnableEventListener(string listenerName, bool enabled)
        {
            var listener = eventListeners.Find(l => l.listenerName == listenerName);
            if (listener != null)
            {
                listener.isEnabled = enabled;
            }
        }

        public void ClearAllCallbacks()
        {
            foreach (var callbacks in globalCallbacks.Values)
            {
                callbacks.Clear();
            }

            foreach (var callbacks in sceneSpecificCallbacks.Values)
            {
                callbacks.Clear();
            }
        }

        private void OnSceneLoadStartedCallback(string sceneName)
        {
            TriggerEvent(SceneEventType.LoadStarted, sceneName);
        }

        private void OnSceneLoadedCallback(string sceneName, UnityEngine.SceneManagement.Scene scene)
        {
            TriggerEvent(SceneEventType.LoadCompleted, sceneName, scene);
        }

        private void OnSceneUnloadStartedCallback(string sceneName)
        {
            TriggerEvent(SceneEventType.UnloadStarted, sceneName);
        }

        private void OnSceneUnloadedCallback(string sceneName)
        {
            TriggerEvent(SceneEventType.UnloadCompleted, sceneName);
        }

        private void OnSceneLoadProgressCallback(string sceneName, float progress)
        {
            TriggerEvent(SceneEventType.LoadProgress, sceneName, progress);
        }

        private void OnSceneLoadFailedCallback(string sceneName, string error)
        {
            TriggerEvent(SceneEventType.LoadFailed, sceneName, error);
        }

        private void OnTransitionStartedCallback()
        {
            TriggerEvent(SceneEventType.TransitionStarted, "");
        }

        private void OnTransitionCompletedCallback()
        {
            TriggerEvent(SceneEventType.TransitionCompleted, "");
        }

        private void OnPreloadStartedCallback(string sceneName)
        {
            TriggerEvent(SceneEventType.PreloadStarted, sceneName);
        }

        private void OnPreloadCompletedCallback(string sceneName)
        {
            TriggerEvent(SceneEventType.PreloadCompleted, sceneName);
        }

        private void OnSceneValidatedCallback(string sceneName, List<ValidationResult> results)
        {
            TriggerEvent(SceneEventType.ValidationCompleted, sceneName, results);
        }

        public void SetEventSettings(bool asyncProcessing, int maxEvents, bool logging)
        {
            processEventsAsync = asyncProcessing;
            maxEventsPerFrame = maxEvents;
            logEvents = logging;
        }
    }
}