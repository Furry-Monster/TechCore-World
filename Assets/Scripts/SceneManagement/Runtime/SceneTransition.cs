using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SceneManagement.Runtime
{
    public class SceneTransition : MonoBehaviour
    {
        public static SceneTransition Instance { get; private set; }

        [Header("Transition Settings")] [SerializeField]
        private Canvas transitionCanvas;

        [SerializeField] private Image fadeImage;
        [SerializeField] private float defaultFadeDuration = 1.0f;
        [SerializeField] private AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] private AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

        [Header("Loading Screen")] [SerializeField]
        private GameObject loadingScreen;

        [SerializeField] private Slider progressBar;
        [SerializeField] private Text loadingText;
        [SerializeField] private string[] loadingMessages = { "Loading...", "请稍候...", "加载中..." };

        private bool isTransitioning;
        private Coroutine currentTransition;

        public event Action OnTransitionStarted;
        public event Action OnTransitionCompleted;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeTransition();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void InitializeTransition()
        {
            if (transitionCanvas == null)
            {
                GameObject canvasObj = new GameObject("TransitionCanvas");
                canvasObj.transform.SetParent(transform);
                transitionCanvas = canvasObj.AddComponent<Canvas>();
                transitionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                transitionCanvas.sortingOrder = 1000;

                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);

                canvasObj.AddComponent<GraphicRaycaster>();
            }

            if (fadeImage == null)
            {
                GameObject fadeObj = new GameObject("FadeImage");
                fadeObj.transform.SetParent(transitionCanvas.transform, false);
                fadeImage = fadeObj.AddComponent<Image>();
                fadeImage.color = Color.black;

                RectTransform rectTransform = fadeImage.GetComponent<RectTransform>();
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.sizeDelta = Vector2.zero;
                rectTransform.anchoredPosition = Vector2.zero;
            }

            SetupLoadingScreen();
            SetTransitionVisibility(false);
        }

        private void SetupLoadingScreen()
        {
            if (loadingScreen == null)
            {
                GameObject loadingObj = new GameObject("LoadingScreen");
                loadingObj.transform.SetParent(transitionCanvas.transform, false);
                loadingScreen = loadingObj;

                RectTransform rectTransform = loadingObj.AddComponent<RectTransform>();
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.sizeDelta = Vector2.zero;
                rectTransform.anchoredPosition = Vector2.zero;

                GameObject textObj = new GameObject("LoadingText");
                textObj.transform.SetParent(loadingObj.transform, false);
                loadingText = textObj.AddComponent<Text>();
                loadingText.text = loadingMessages[0];
                loadingText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                loadingText.fontSize = 36;
                loadingText.alignment = TextAnchor.MiddleCenter;
                loadingText.color = Color.white;

                RectTransform textRect = textObj.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0.5f, 0.4f);
                textRect.anchorMax = new Vector2(0.5f, 0.6f);
                textRect.sizeDelta = new Vector2(400, 100);
                textRect.anchoredPosition = Vector2.zero;

                GameObject progressObj = new GameObject("ProgressBar");
                progressObj.transform.SetParent(loadingObj.transform, false);
                progressBar = progressObj.AddComponent<Slider>();
                progressBar.minValue = 0f;
                progressBar.maxValue = 1f;
                progressBar.value = 0f;

                RectTransform progressRect = progressObj.GetComponent<RectTransform>();
                progressRect.anchorMin = new Vector2(0.3f, 0.3f);
                progressRect.anchorMax = new Vector2(0.7f, 0.35f);
                progressRect.sizeDelta = Vector2.zero;
                progressRect.anchoredPosition = Vector2.zero;

                GameObject background = new GameObject("Background");
                background.transform.SetParent(progressObj.transform, false);
                Image bgImage = background.AddComponent<Image>();
                bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

                GameObject fill = new GameObject("Fill");
                fill.transform.SetParent(progressObj.transform, false);
                Image fillImage = fill.AddComponent<Image>();
                fillImage.color = new Color(0.3f, 0.8f, 0.3f, 1f);

                progressBar.targetGraphic = fillImage;
                progressBar.fillRect = fill.GetComponent<RectTransform>();
                progressBar.handleRect = null;
            }

            loadingScreen.SetActive(false);
        }

        public void FadeToScene(string sceneName, float duration = -1f, bool showLoadingScreen = true)
        {
            if (isTransitioning) return;

            if (duration < 0) duration = defaultFadeDuration;

            if (currentTransition != null)
            {
                StopCoroutine(currentTransition);
            }

            currentTransition = StartCoroutine(FadeToSceneCoroutine(sceneName, duration, showLoadingScreen));
        }

        public void FadeIn(float duration = -1f, Action onComplete = null)
        {
            if (duration < 0) duration = defaultFadeDuration;
            StartCoroutine(FadeCoroutine(true, duration, onComplete));
        }

        public void FadeOut(float duration = -1f, Action onComplete = null)
        {
            if (duration < 0) duration = defaultFadeDuration;
            StartCoroutine(FadeCoroutine(false, duration, onComplete));
        }

        private IEnumerator FadeToSceneCoroutine(string sceneName, float duration, bool showLoadingScreen)
        {
            isTransitioning = true;
            OnTransitionStarted?.Invoke();

            yield return FadeCoroutine(true, duration * 0.3f);

            if (showLoadingScreen)
            {
                loadingScreen.SetActive(true);
                UpdateLoadingText(0);
            }

            bool sceneLoadCompleted = false;
            float loadProgress = 0f;

            if (SceneManagerCore.Instance != null)
            {
                SceneManagerCore.Instance.OnSceneLoadProgress += (name, progress) =>
                {
                    if (name == sceneName)
                    {
                        loadProgress = progress;
                        if (showLoadingScreen)
                        {
                            UpdateLoadingProgress(progress);
                        }
                    }
                };

                SceneManagerCore.Instance.OnSceneLoaded += (name, scene) =>
                {
                    if (name == sceneName)
                    {
                        sceneLoadCompleted = true;
                    }
                };

                SceneManagerCore.Instance.LoadScene(sceneName);
            }
            else
            {
                SceneManager.LoadScene(sceneName);
                sceneLoadCompleted = true;
            }

            while (!sceneLoadCompleted)
            {
                if (showLoadingScreen)
                {
                    UpdateLoadingText(Time.time);
                }

                yield return null;
            }

            if (showLoadingScreen)
            {
                loadingScreen.SetActive(false);
            }

            yield return FadeCoroutine(false, duration * 0.7f);

            isTransitioning = false;
            OnTransitionCompleted?.Invoke();
        }

        private IEnumerator FadeCoroutine(bool fadeIn, float duration, Action onComplete = null)
        {
            SetTransitionVisibility(true);

            float startAlpha = fadeIn ? 0f : 1f;
            float endAlpha = fadeIn ? 1f : 0f;
            AnimationCurve curve = fadeIn ? fadeInCurve : fadeOutCurve;

            Color startColor = fadeImage.color;
            startColor.a = startAlpha;
            fadeImage.color = startColor;

            float elapsedTime = 0f;

            while (elapsedTime < duration)
            {
                elapsedTime += Time.deltaTime;
                float t = elapsedTime / duration;
                float alpha = Mathf.Lerp(startAlpha, endAlpha, curve.Evaluate(t));

                Color color = fadeImage.color;
                color.a = alpha;
                fadeImage.color = color;

                yield return null;
            }

            Color finalColor = fadeImage.color;
            finalColor.a = endAlpha;
            fadeImage.color = finalColor;

            if (!fadeIn)
            {
                SetTransitionVisibility(false);
            }

            onComplete?.Invoke();
        }

        private void SetTransitionVisibility(bool visible)
        {
            if (transitionCanvas != null)
            {
                transitionCanvas.gameObject.SetActive(visible);
            }
        }

        private void UpdateLoadingProgress(float progress)
        {
            if (progressBar != null)
            {
                progressBar.value = progress;
            }
        }

        private void UpdateLoadingText(float time)
        {
            if (loadingText != null && loadingMessages.Length > 0)
            {
                int messageIndex = Mathf.FloorToInt(time) % loadingMessages.Length;
                int dotCount = Mathf.FloorToInt((time * 2f) % 4f);
                string dots = new string('.', dotCount);
                loadingText.text = loadingMessages[messageIndex] + dots;
            }
        }

        public bool IsTransitioning()
        {
            return isTransitioning;
        }

        public void SetFadeColor(Color color)
        {
            if (fadeImage != null)
            {
                Color newColor = color;
                newColor.a = fadeImage.color.a;
                fadeImage.color = newColor;
            }
        }

        public void SetLoadingMessages(string[] messages)
        {
            if (messages != null && messages.Length > 0)
            {
                loadingMessages = messages;
            }
        }
    }
}