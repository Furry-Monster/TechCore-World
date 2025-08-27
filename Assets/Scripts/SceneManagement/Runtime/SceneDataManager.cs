using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneManagement.Runtime
{
    [Serializable]
    public class SceneState
    {
        public string sceneName;
        public Vector3 playerPosition;
        public Quaternion playerRotation;
        public Dictionary<string, object> customData = new();
        public float timeStamp;
        public string checksum;

        public SceneState(string name)
        {
            sceneName = name;
            timeStamp = Time.realtimeSinceStartup;
        }
    }

    [Serializable]
    public class GameSaveData
    {
        public string saveName;
        public DateTime saveTime;
        public string currentScene;
        public Dictionary<string, SceneState> sceneStates = new();
        public Dictionary<string, object> globalGameData = new();
        public string version = "1.0";
    }

    public interface ISceneStatePersistent
    {
        Dictionary<string, object> SaveState();
        void LoadState(Dictionary<string, object> state);
        string GetPersistentID();
    }

    public class SceneDataManager : MonoBehaviour
    {
        public static SceneDataManager Instance { get; private set; }

        [Header("Save/Load Settings")] [SerializeField]
        private string saveDirectory = "GameSaves";

        [SerializeField] private bool autoSaveEnabled = true;
        [SerializeField] private float autoSaveInterval = 300f;
        [SerializeField] private int maxAutoSaveSlots = 5;
        [SerializeField] private bool enableEncryption;

        private Dictionary<string, SceneState> currentSceneStates = new();
        private Dictionary<string, object> globalGameData = new();
        private float lastAutoSaveTime;
        private string currentSaveName = "AutoSave";

        public event Action<string> OnSaveStarted;
        public event Action<string, bool> OnSaveCompleted;
        public event Action<string> OnLoadStarted;
        public event Action<string, bool> OnLoadCompleted;
        public event Action<string> OnAutoSave;

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
            CreateSaveDirectory();

            if (SceneManagerCore.Instance != null)
            {
                SceneManagerCore.Instance.OnSceneLoaded += OnSceneLoadedCallback;
                SceneManagerCore.Instance.OnSceneUnloadStarted += OnSceneUnloadCallback;
            }
        }

        private void Update()
        {
            if (autoSaveEnabled && Time.realtimeSinceStartup - lastAutoSaveTime >= autoSaveInterval)
            {
                AutoSave();
            }
        }

        private void CreateSaveDirectory()
        {
            var fullPath = Path.Combine(Application.persistentDataPath, saveDirectory);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
        }

        public void SaveGame(string saveName = null)
        {
            if (string.IsNullOrEmpty(saveName))
                saveName = currentSaveName;

            OnSaveStarted?.Invoke(saveName);

            try
            {
                SaveCurrentSceneState();

                var saveData = new GameSaveData
                {
                    saveName = saveName,
                    saveTime = DateTime.Now,
                    currentScene = GetCurrentSceneName(),
                    sceneStates = new Dictionary<string, SceneState>(currentSceneStates),
                    globalGameData = new Dictionary<string, object>(globalGameData)
                };

                var filePath = GetSaveFilePath(saveName);
                var jsonData = JsonUtility.ToJson(saveData, true);

                if (enableEncryption)
                {
                    jsonData = EncryptData(jsonData);
                }

                File.WriteAllText(filePath, jsonData);
                OnSaveCompleted?.Invoke(saveName, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save game: {ex.Message}");
                OnSaveCompleted?.Invoke(saveName, false);
            }
        }

        public bool LoadGame(string saveName)
        {
            OnLoadStarted?.Invoke(saveName);

            try
            {
                var filePath = GetSaveFilePath(saveName);
                if (!File.Exists(filePath))
                {
                    OnLoadCompleted?.Invoke(saveName, false);
                    return false;
                }

                var jsonData = File.ReadAllText(filePath);

                if (enableEncryption)
                {
                    jsonData = DecryptData(jsonData);
                }

                var saveData = JsonUtility.FromJson<GameSaveData>(jsonData);

                if (saveData == null)
                {
                    OnLoadCompleted?.Invoke(saveName, false);
                    return false;
                }

                currentSceneStates = saveData.sceneStates ?? new Dictionary<string, SceneState>();
                globalGameData = saveData.globalGameData ?? new Dictionary<string, object>();
                currentSaveName = saveName;

                if (!string.IsNullOrEmpty(saveData.currentScene))
                {
                    if (SceneManagerCore.Instance != null)
                    {
                        SceneManagerCore.Instance.LoadScene(saveData.currentScene);
                    }
                    else
                    {
                        SceneManager.LoadScene(saveData.currentScene);
                    }
                }

                OnLoadCompleted?.Invoke(saveName, true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load game: {ex.Message}");
                OnLoadCompleted?.Invoke(saveName, false);
                return false;
            }
        }

        public void SaveCurrentSceneState()
        {
            var currentScene = GetCurrentSceneName();
            if (string.IsNullOrEmpty(currentScene))
                return;

            var sceneState = GetOrCreateSceneState(currentScene);

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                sceneState.playerPosition = player.transform.position;
                sceneState.playerRotation = player.transform.rotation;
            }

            var persistentObjects = FindObjectsOfType<MonoBehaviour>()
                .OfType<ISceneStatePersistent>()
                .ToArray();

            sceneState.customData.Clear();
            foreach (var persistentObj in persistentObjects)
            {
                try
                {
                    var id = persistentObj.GetPersistentID();
                    var objState = persistentObj.SaveState();
                    if (objState != null && !string.IsNullOrEmpty(id))
                    {
                        sceneState.customData[id] = objState;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to save state for persistent object: {ex.Message}");
                }
            }

            sceneState.timeStamp = Time.realtimeSinceStartup;
            sceneState.checksum = GenerateChecksum(sceneState);
            currentSceneStates[currentScene] = sceneState;
        }

        public void LoadSceneState(string sceneName)
        {
            if (!currentSceneStates.TryGetValue(sceneName, out var sceneState))
                return;

            if (!ValidateChecksum(sceneState))
            {
                Debug.LogWarning($"Checksum validation failed for scene {sceneName}");
                return;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                player.transform.position = sceneState.playerPosition;
                player.transform.rotation = sceneState.playerRotation;
            }

            var persistentObjects = FindObjectsOfType<MonoBehaviour>()
                .OfType<ISceneStatePersistent>()
                .ToArray();

            foreach (var persistentObj in persistentObjects)
            {
                try
                {
                    var id = persistentObj.GetPersistentID();
                    if (sceneState.customData.TryGetValue(id, out var data))
                    {
                        if (data is Dictionary<string, object> objData)
                        {
                            persistentObj.LoadState(objData);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to load state for persistent object: {ex.Message}");
                }
            }
        }

        public void SetGlobalData(string key, object value)
        {
            globalGameData[key] = value;
        }

        public T GetGlobalData<T>(string key, T defaultValue = default(T))
        {
            if (globalGameData.TryGetValue(key, out var data))
            {
                try
                {
                    return (T)data;
                }
                catch (Exception)
                {
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        public bool HasGlobalData(string key)
        {
            return globalGameData.ContainsKey(key);
        }

        public void RemoveGlobalData(string key)
        {
            globalGameData.Remove(key);
        }

        public string[] GetAvailableSaves()
        {
            var savePath = Path.Combine(Application.persistentDataPath, saveDirectory);
            if (!Directory.Exists(savePath))
                return Array.Empty<string>();

            var files = Directory.GetFiles(savePath, "*.json");
            var saveNames = new List<string>();

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                saveNames.Add(fileName);
            }

            return saveNames.ToArray();
        }

        public bool DeleteSave(string saveName)
        {
            try
            {
                var filePath = GetSaveFilePath(saveName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to delete save {saveName}: {ex.Message}");
            }

            return false;
        }

        private void AutoSave()
        {
            var autoSaveName = $"AutoSave_{DateTime.Now:yyyyMMdd_HHmmss}";
            SaveGame(autoSaveName);
            OnAutoSave?.Invoke(autoSaveName);

            CleanupOldAutoSaves();
            lastAutoSaveTime = Time.realtimeSinceStartup;
        }

        private void CleanupOldAutoSaves()
        {
            var saves = GetAvailableSaves();
            var autoSaves = new List<string>();

            foreach (var save in saves)
            {
                if (save.StartsWith("AutoSave_"))
                {
                    autoSaves.Add(save);
                }
            }

            if (autoSaves.Count > maxAutoSaveSlots)
            {
                autoSaves.Sort();
                for (var i = 0; i < autoSaves.Count - maxAutoSaveSlots; i++)
                {
                    DeleteSave(autoSaves[i]);
                }
            }
        }

        private SceneState GetOrCreateSceneState(string sceneName)
        {
            if (!currentSceneStates.ContainsKey(sceneName))
            {
                currentSceneStates[sceneName] = new SceneState(sceneName);
            }

            return currentSceneStates[sceneName];
        }

        private string GetSaveFilePath(string saveName)
        {
            return Path.Combine(Application.persistentDataPath, saveDirectory, saveName + ".json");
        }

        private string GetCurrentSceneName()
        {
            return SceneManager.GetActiveScene().name;
        }

        private string GenerateChecksum(SceneState sceneState)
        {
            var data =
                $"{sceneState.sceneName}{sceneState.playerPosition}{sceneState.playerRotation}{sceneState.timeStamp}";
            return data.GetHashCode().ToString();
        }

        private bool ValidateChecksum(SceneState sceneState)
        {
            var expectedChecksum = GenerateChecksum(sceneState);
            return expectedChecksum == sceneState.checksum;
        }

        private string EncryptData(string data)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
        }

        private string DecryptData(string encryptedData)
        {
            var bytes = Convert.FromBase64String(encryptedData);
            return Encoding.UTF8.GetString(bytes);
        }

        private void OnSceneLoadedCallback(string sceneName, Scene scene)
        {
            LoadSceneState(sceneName);
        }

        private void OnSceneUnloadCallback(string sceneName)
        {
            SaveCurrentSceneState();
        }

        public void EnableAutoSave(bool enable)
        {
            autoSaveEnabled = enable;
        }

        public void SetAutoSaveInterval(float interval)
        {
            autoSaveInterval = Mathf.Max(60f, interval);
        }
    }
}