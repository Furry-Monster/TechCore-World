using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace SceneManagement.Runtime
{
    [Serializable]
    public class SceneEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public class SceneLoadEvent : UnityEvent<string, Scene>
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

    public class SceneEvents
    {
        private static SceneEvents instance;
        public static SceneEvents Instance => instance ??= new SceneEvents();

        private List<SceneEventListener> eventListeners = new List<SceneEventListener>();

        public SceneEvent OnAnySceneLoadStarted = new();
        public SceneLoadEvent OnAnySceneLoaded = new();
        public SceneEvent OnAnySceneUnloadStarted = new();
        public SceneEvent OnAnySceneUnloaded = new();
        public SceneProgressEvent OnAnySceneProgress = new();
        public SceneErrorEvent OnAnySceneError = new();
        public SceneTransitionEvent OnAnyTransitionStarted = new();
        public SceneTransitionEvent OnAnyTransitionCompleted = new();

        private readonly Dictionary<string, List<Action<string>>> sceneSpecificCallbacks = new();
        private readonly Dictionary<SceneEventType, List<Action<string, object[]>>> globalCallbacks = new();

        private readonly Queue<SceneEventData> eventQueue = new();
        private bool isProcessingEvents;
        private bool processEventsAsync = true;
        private int maxEventsPerFrame = 10;
        private bool logEvents;

        private struct SceneEventData
        {
            public SceneEventType eventType;
            public string sceneName;
            public object[] parameters;
            public DateTime timestamp;
        }

        private SceneEvents()
        {
            InitializeCallbackDictionaries();
        }

        public void Initialize(SceneManagerCore coreManager, SceneTransition transition,
            ScenePreloader preloader, SceneValidator validator)
        {
            SubscribeToComponents(coreManager, transition, preloader, validator);
        }

        private void InitializeCallbackDictionaries()
        {
            foreach (SceneEventType eventType in Enum.GetValues(typeof(SceneEventType)))
            {
                if (eventType != SceneEventType.None && eventType != SceneEventType.All)
                {
                    globalCallbacks[eventType] = new List<Action<string, object[]>>();
                }
            }
        }

        private void SubscribeToComponents(SceneManagerCore coreManager, SceneTransition transition,
            ScenePreloader preloader, SceneValidator validator)
        {
            if (coreManager != null)
            {
                coreManager.OnSceneLoadStarted += OnSceneLoadStartedCallback;
                coreManager.OnSceneLoaded += OnSceneLoadedCallback;
                coreManager.OnSceneUnloadStarted += OnSceneUnloadStartedCallback;
                coreManager.OnSceneUnloaded += OnSceneUnloadedCallback;
                coreManager.OnSceneLoadProgress += OnSceneLoadProgressCallback;
                coreManager.OnSceneLoadFailed += OnSceneLoadFailedCallback;
            }

            if (transition != null)
            {
                transition.OnTransitionStarted += OnTransitionStartedCallback;
                transition.OnTransitionCompleted += OnTransitionCompletedCallback;
            }

            if (preloader != null)
            {
                preloader.OnPreloadStarted += OnPreloadStartedCallback;
                preloader.OnPreloadCompleted += OnPreloadCompletedCallback;
            }

            if (validator != null)
            {
                validator.OnSceneValidated += OnSceneValidatedCallback;
            }
        }

        // 需要外部驱动的更新方法
        public void Update()
        {
            if (processEventsAsync && eventQueue.Count > 0 && !isProcessingEvents)
            {
                ProcessEventQueueSync();
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
            var eventData = new SceneEventData
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

        private void ProcessEventQueueSync()
        {
            isProcessingEvents = true;
            var eventsProcessed = 0;

            while (eventQueue.Count > 0 && eventsProcessed < maxEventsPerFrame)
            {
                var eventData = eventQueue.Dequeue();
                ProcessEvent(eventData.eventType, eventData.sceneName, eventData.parameters);
                eventsProcessed++;
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
                        if (parameters.Length > 0 && parameters[0] is Scene)
                        {
                            OnAnySceneLoaded?.Invoke(sceneName, (Scene)parameters[0]);
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
            catch (Exception ex)
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
                            if (parameters.Length > 0 && parameters[0] is Scene)
                            {
                                listener.onSceneLoadEvent?.Invoke(sceneName, (Scene)parameters[0]);
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
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[SceneEvents] Error invoking event listener '{listener.listenerName}': {ex.Message}");
                }
            }
        }

        private void InvokeGlobalCallbacks(SceneEventType eventType, string sceneName, object[] parameters)
        {
            if (globalCallbacks.TryGetValue(eventType, out var callbacks))
            {
                for (var i = callbacks.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        callbacks[i]?.Invoke(sceneName, parameters);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[SceneEvents] Error invoking global callback: {ex.Message}");
                        callbacks.RemoveAt(i);
                    }
                }
            }
        }

        private void InvokeSceneSpecificCallbacks(string sceneName)
        {
            if (sceneSpecificCallbacks.TryGetValue(sceneName, out var callbacks))
            {
                for (var i = callbacks.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        callbacks[i]?.Invoke(sceneName);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[SceneEvents] Error invoking scene-specific callback: {ex.Message}");
                        callbacks.RemoveAt(i);
                    }
                }
            }
        }

        public void Subscribe(SceneEventType eventType, Action<string, object[]> callback)
        {
            if (callback == null) return;

            if (!globalCallbacks.ContainsKey(eventType))
            {
                globalCallbacks[eventType] = new List<Action<string, object[]>>();
            }

            globalCallbacks[eventType].Add(callback);
        }

        public void Unsubscribe(SceneEventType eventType, Action<string, object[]> callback)
        {
            if (callback == null || !globalCallbacks.TryGetValue(eventType, out var globalCallback))
                return;

            globalCallback.Remove(callback);
        }

        public void SubscribeToScene(string sceneName, Action<string> callback)
        {
            if (string.IsNullOrEmpty(sceneName) || callback == null) return;

            if (!sceneSpecificCallbacks.ContainsKey(sceneName))
            {
                sceneSpecificCallbacks[sceneName] = new List<Action<string>>();
            }

            sceneSpecificCallbacks[sceneName].Add(callback);
        }

        public void UnsubscribeFromScene(string sceneName, Action<string> callback)
        {
            if (string.IsNullOrEmpty(sceneName) || callback == null) return;

            if (sceneSpecificCallbacks.TryGetValue(sceneName, out var specificCallback))
            {
                specificCallback.Remove(callback);
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

        public void EnableEventListener(string listenerName, bool listenerEnabled)
        {
            var listener = eventListeners.Find(l => l.listenerName == listenerName);
            if (listener != null)
            {
                listener.isEnabled = listenerEnabled;
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

        public void SetEventSettings(bool asyncProcessing, int maxEvents, bool logging)
        {
            processEventsAsync = asyncProcessing;
            maxEventsPerFrame = maxEvents;
            logEvents = logging;
        }

        // 事件回调方法
        private void OnSceneLoadStartedCallback(string sceneName)
        {
            TriggerEvent(SceneEventType.LoadStarted, sceneName);
        }

        private void OnSceneLoadedCallback(string sceneName, Scene scene)
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
    }
}