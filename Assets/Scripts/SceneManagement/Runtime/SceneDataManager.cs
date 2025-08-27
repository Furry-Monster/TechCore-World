using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SceneManagement.Runtime
{
    [Serializable]
    public class SceneState
    {
        public string sceneName;
        public Vector3 playerPosition;
        public Quaternion playerRotation;
        public Dictionary<string, object> customData = new Dictionary<string, object>();
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
        public Dictionary<string, SceneState> sceneStates = new Dictionary<string, SceneState>();
        public Dictionary<string, object> globalGameData = new Dictionary<string, object>();
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
        [SerializeField] private bool enableEncryption = false;

        private Dictionary<string, SceneState> currentSceneStates = new Dictionary<string, SceneState>();
        private Dictionary<string, object> globalGameData = new Dictionary<string, object>();
        private float lastAutoSaveTime = 0f;
        private string currentSaveName = "AutoSave";

        public event System.Action<string> OnSaveStarted;
        public event System.Action<string, bool> OnSaveCompleted;
        public event System.Action<string> OnLoadStarted;
        public event System.Action<string, bool> OnLoadCompleted;
        public event System.Action<string> OnAutoSave;

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
            string fullPath = Path.Combine(Application.persistentDataPath, saveDirectory);
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

                GameSaveData saveData = new GameSaveData
                {
                    saveName = saveName,
                    saveTime = DateTime.Now,
                    currentScene = GetCurrentSceneName(),
                    sceneStates = new Dictionary<string, SceneState>(currentSceneStates),
                    globalGameData = new Dictionary<string, object>(globalGameData)
                };

                string filePath = GetSaveFilePath(saveName);
                string jsonData = JsonUtility.ToJson(saveData, true);

                if (enableEncryption)
                {
                    jsonData = EncryptData(jsonData);
                }

                File.WriteAllText(filePath, jsonData);
                OnSaveCompleted?.Invoke(saveName, true);
            }
            catch (System.Exception ex)
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
                string filePath = GetSaveFilePath(saveName);
                if (!File.Exists(filePath))
                {
                    OnLoadCompleted?.Invoke(saveName, false);
                    return false;
                }

                string jsonData = File.ReadAllText(filePath);

                if (enableEncryption)
                {
                    jsonData = DecryptData(jsonData);
                }

                GameSaveData saveData = JsonUtility.FromJson<GameSaveData>(jsonData);

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
                        UnityEngine.SceneManagement.SceneManager.LoadScene(saveData.currentScene);
                    }
                }

                OnLoadCompleted?.Invoke(saveName, true);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to load game: {ex.Message}");
                OnLoadCompleted?.Invoke(saveName, false);
                return false;
            }
        }

        public void SaveCurrentSceneState()
        {
            string currentScene = GetCurrentSceneName();
            if (string.IsNullOrEmpty(currentScene))
                return;

            SceneState sceneState = GetOrCreateSceneState(currentScene);

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                sceneState.playerPosition = player.transform.position;
                sceneState.playerRotation = player.transform.rotation;
            }

            ISceneStatePersistent[] persistentObjects = FindObjectsOfType<MonoBehaviour>()
                .OfType<ISceneStatePersistent>()
                .ToArray();

            sceneState.customData.Clear();
            foreach (var persistentObj in persistentObjects)
            {
                try
                {
                    string id = persistentObj.GetPersistentID();
                    Dictionary<string, object> objState = persistentObj.SaveState();
                    if (objState != null && !string.IsNullOrEmpty(id))
                    {
                        sceneState.customData[id] = objState;
                    }
                }
                catch (System.Exception ex)
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
            if (!currentSceneStates.ContainsKey(sceneName))
                return;

            SceneState sceneState = currentSceneStates[sceneName];

            if (!ValidateChecksum(sceneState))
            {
                Debug.LogWarning($"Checksum validation failed for scene {sceneName}");
                return;
            }

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                player.transform.position = sceneState.playerPosition;
                player.transform.rotation = sceneState.playerRotation;
            }

            ISceneStatePersistent[] persistentObjects = FindObjectsOfType<MonoBehaviour>()
                .OfType<ISceneStatePersistent>()
                .ToArray();

            foreach (var persistentObj in persistentObjects)
            {
                try
                {
                    string id = persistentObj.GetPersistentID();
                    if (sceneState.customData.ContainsKey(id))
                    {
                        var objData = sceneState.customData[id] as Dictionary<string, object>;
                        if (objData != null)
                        {
                            persistentObj.LoadState(objData);
                        }
                    }
                }
                catch (System.Exception ex)
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
            if (globalGameData.ContainsKey(key))
            {
                try
                {
                    return (T)globalGameData[key];
                }
                catch (System.Exception)
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
            string savePath = Path.Combine(Application.persistentDataPath, saveDirectory);
            if (!Directory.Exists(savePath))
                return new string[0];

            string[] files = Directory.GetFiles(savePath, "*.json");
            List<string> saveNames = new List<string>();

            foreach (string file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                saveNames.Add(fileName);
            }

            return saveNames.ToArray();
        }

        public bool DeleteSave(string saveName)
        {
            try
            {
                string filePath = GetSaveFilePath(saveName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to delete save {saveName}: {ex.Message}");
            }

            return false;
        }

        private void AutoSave()
        {
            string autoSaveName = $"AutoSave_{DateTime.Now:yyyyMMdd_HHmmss}";
            SaveGame(autoSaveName);
            OnAutoSave?.Invoke(autoSaveName);

            CleanupOldAutoSaves();
            lastAutoSaveTime = Time.realtimeSinceStartup;
        }

        private void CleanupOldAutoSaves()
        {
            string[] saves = GetAvailableSaves();
            List<string> autoSaves = new List<string>();

            foreach (string save in saves)
            {
                if (save.StartsWith("AutoSave_"))
                {
                    autoSaves.Add(save);
                }
            }

            if (autoSaves.Count > maxAutoSaveSlots)
            {
                autoSaves.Sort();
                for (int i = 0; i < autoSaves.Count - maxAutoSaveSlots; i++)
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
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        private string GenerateChecksum(SceneState sceneState)
        {
            string data =
                $"{sceneState.sceneName}{sceneState.playerPosition}{sceneState.playerRotation}{sceneState.timeStamp}";
            return data.GetHashCode().ToString();
        }

        private bool ValidateChecksum(SceneState sceneState)
        {
            string expectedChecksum = GenerateChecksum(sceneState);
            return expectedChecksum == sceneState.checksum;
        }

        private string EncryptData(string data)
        {
            return System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(data));
        }

        private string DecryptData(string encryptedData)
        {
            byte[] bytes = System.Convert.FromBase64String(encryptedData);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private void OnSceneLoadedCallback(string sceneName, UnityEngine.SceneManagement.Scene scene)
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