using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class DemoGameManager : MonoBehaviour
{
    #region Nested Types
    [Serializable]
    public class ManagerEvent
    {
        public float t;
        public string type;
    }

    [Serializable]
    private struct Envelope
    {
        public string type;
        public Payload payload;
    }

    [Serializable]
    private struct Payload
    {
        public string name;   // e.g. "level_complete"
        public int level;
        public int score;
    }
    #endregion

    #region Fields
    private List<ManagerEvent> managerEvents = new List<ManagerEvent>();
    private float sessionStartTime;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void SendGameEventMessage(string message);
#endif

    [Header("Debug/Test")]
    public bool forceWebGLMode = false;

    public List<TextAsset> levelFiles;
    public static DemoGameManager instance = null;

    private LevelManager levelManager;
    private int levelIndex = 0;
    private bool isWebGL = false;
    private int completionLevelIndex = -1;
    private List<int> randomizedOrder;
    private int randomizedOrderIndex = 0;

    // Transition / init guards
    private bool isInitializing = false;            // prevents InitGame() reentry
    private bool isReloadingForCompletion = false;  // tells OnSceneLoaded to only show code
    private int completedLevelIndex = -1;           // which level just completed

    [HideInInspector] public List<GameObject> enemies;
    [HideInInspector] public int score = 0;
    [HideInInspector] public bool doingSetup = false;
    [HideInInspector] public bool playerDied = false;

    // Cached UI references
    private GameObject scoreTextObj;
    private GameObject levelTextObj;
    private GameObject completionTextObj;

    public List<bool> levelsCompleted;

    private static bool s_sceneCallbackRegistered = false;

    private float spacebarPressedTime = -1f;
    #endregion

    #region Lifecycle
    private void Awake()
    {
        if (instance == null) instance = this;
        else if (instance != this) { Destroy(gameObject); return; }

        DontDestroyOnLoad(gameObject);

        // Cache references after instance assignment
        scoreTextObj = GameObject.FindGameObjectWithTag("ScoreText");
        levelTextObj = GameObject.FindGameObjectWithTag("LevelText");
        completionTextObj = GameObject.Find("Code");
        levelManager = GetComponent<LevelManager>();

        isWebGL = Application.platform == RuntimePlatform.WebGLPlayer || forceWebGLMode;

        // Determine initial level index
        if (isWebGL)
        {
            int urlLevelIndex = GetLevelIndexFromURL();
            if (urlLevelIndex > 0 && urlLevelIndex < levelFiles.Count)
                levelIndex = urlLevelIndex;
            else
                levelIndex = 0; // Tutorial only
        }
        else
        {
            levelIndex = 0;
        }

        // Initialize completion flags list BEFORE any Reveal calls
        levelsCompleted = new List<bool>(levelFiles.Count);
        for (int i = 0; i < levelFiles.Count; i++) levelsCompleted.Add(false);

        // Prepare initial randomization (non-WebGL or after death)
        if (!isWebGL)
        {
            PrepareRandomizedOrder();
            randomizedOrderIndex = 0;
        }

        // Register scene callback once (across domain reloads too)
        CallbackInitialization();
    }

    private void OnDestroy()
    {
        // Be safe in Editor/domain reloads — remove our handler
        SceneManager.sceneLoaded -= OnSceneLoaded;
        s_sceneCallbackRegistered = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static public void CallbackInitialization()
    {
        if (!s_sceneCallbackRegistered)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            s_sceneCallbackRegistered = true;
        }
    }

    static private void OnSceneLoaded(Scene arg0, LoadSceneMode arg1)
    {
        if (instance == null) return;

        // If we just completed a level, ONLY reveal the code and stop.
        if (instance.isReloadingForCompletion)
        {
            instance.isReloadingForCompletion = false;
            if (instance.completedLevelIndex >= 0)
            {
                instance.RevealCompletionCode(instance.completedLevelIndex);
                instance.completedLevelIndex = -1;
            }
            return; // critical: do NOT progress or InitGame
        }

        // If this level was already completed (e.g., refresh), just show the code.
        if (instance.levelsCompleted != null &&
            instance.levelIndex >= 0 &&
            instance.levelIndex < instance.levelsCompleted.Count &&
            instance.levelsCompleted[instance.levelIndex])
        {
            instance.RevealCompletionCode(instance.levelIndex);
            return;
        }

        // Normal flow (death vs progress)
        if (!instance.playerDied)
        {
            if (instance.levelIndex == 0)
            {
                instance.PrepareRandomizedOrder();
                instance.randomizedOrderIndex = 0;
            }
            else
            {
                instance.randomizedOrderIndex++;
                if (instance.randomizedOrderIndex < instance.randomizedOrder.Count)
                    instance.levelIndex = instance.randomizedOrder[instance.randomizedOrderIndex];
                else
                    instance.levelIndex = 0;
            }
            instance.InitGame();
        }
        else
        {
            instance.score = 0;
            instance.levelIndex = 0;
            instance.PrepareRandomizedOrder();
            instance.randomizedOrderIndex = 0;
            instance.InitGame();
        }
    }
    #endregion

    #region Public API
    public void OnPlayerDeath()
    {
        playerDied = true;
        levelsCompleted[levelIndex] = false;

        // Hide completion code if visible
        var codeObj = GameObject.Find("Code");
        if (codeObj != null) codeObj.SetActive(false);

        // Reload current scene
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex, LoadSceneMode.Single);
    }

    public void OnLevelComplete()
    {
        levelsCompleted[levelIndex] = true;
        playerDied = false;

        // Calculate time spent from spacebar to completion
        float timeFromSpacebarToFinish = (spacebarPressedTime > 0f) ? (Time.realtimeSinceStartup - spacebarPressedTime) : -1f;
        Debug.Log($"[LevelComplete] Time from spacebar to finish: {timeFromSpacebarToFinish:F2} seconds");

        // Optionally: Add to completion record (Envelope.Payload)
        // You can extend Payload struct to include this if needed

        // Mark intent explicitly, so OnSceneLoaded knows NOT to InitGame
        isReloadingForCompletion = true;
        completedLevelIndex = levelIndex;

        // Reload to a clean scene that only shows the code
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex, LoadSceneMode.Single);
    }
    #endregion

    #region Game Setup
    private void PrepareRandomizedOrder()
    {
        randomizedOrder = new List<int>();
        for (int i = 1; i < levelFiles.Count; i++) randomizedOrder.Add(i);

        // Fisher–Yates shuffle
        for (int i = 0; i < randomizedOrder.Count; i++)
        {
            int j = UnityEngine.Random.Range(i, randomizedOrder.Count);
            (randomizedOrder[i], randomizedOrder[j]) = (randomizedOrder[j], randomizedOrder[i]);
        }
    }

    private void InitGame()
    {
        if (isInitializing) return;
        isInitializing = true;
        // Hide completion code if visible
        var codeObj = GameObject.Find("Code");
        if (codeObj != null) codeObj.SetActive(false);
        try
        {
            Debug.Log("Init Game");

            // If WebGL and tutorial level, set completionLevelIndex for later
            if (isWebGL && levelIndex == 0) completionLevelIndex = 0;
            else if (isWebGL) completionLevelIndex = levelIndex;

            // If the current level is already complete, don't init again.
            if (levelsCompleted[levelIndex])
            {
                RevealCompletionCode(levelIndex);
                return;
            }

            var levelImage = GameObject.Find("LevelImage");
            if (levelImage != null) levelImage.SetActive(true);

            doingSetup = true;
            playerDied = false;

            levelManager.SetupScene(levelFiles[levelIndex]);

            var grid = levelManager.GetLevel();
            var player = FindObjectOfType<Player>();
            if (player != null)
            {
                player.transform.position = new Vector3(
                    grid.GetLength(0) / 2f - 1f,
                    grid.GetLength(1) - 50f,
                    1f
                );
            }

            var cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.transform.position = new Vector3(
                    grid.GetLength(0) / 2f - 1f,
                    grid.GetLength(1) - 50f,
                    -10f
                );
            }
        }
        finally
        {
            isInitializing = false;
        }
    }
    #endregion

    #region Update & Events
    private void Update()
    {
        if (doingSetup && Input.GetButtonDown("Sword"))
        {
            doingSetup = false;
            var levelImage = GameObject.Find("LevelImage");
            if (levelImage != null) levelImage.SetActive(false);

            spacebarPressedTime = Time.realtimeSinceStartup;
            LogManagerEvent("start_space_press");
        }

        var player = FindObjectOfType<Player>();
        if (player != null && scoreTextObj != null)
        {
            var txt = scoreTextObj.GetComponent<UnityEngine.UI.Text>();
            if (txt != null)
            {
                txt.text =
                    "Health: " + player.health + "\n" +
                    "    : " + player.keyCount + "\n" +
                    "Score: " + score;
            }
        }
    }

    private void LogManagerEvent(string type)
    {
        var e = new ManagerEvent
        {
            t = Time.realtimeSinceStartup - sessionStartTime,
            type = type
        };
        managerEvents.Add(e);

        string json = JsonUtility.ToJson(e);

#if UNITY_WEBGL && !UNITY_EDITOR
        SendGameEventMessage(json);
#else
        Debug.Log($"[ManagerEvent] {json}");
#endif
    }
    #endregion

    #region Completion Code / JS Bridge
    private void RevealCompletionCode(int levelIdx)
    {
        if (completionTextObj == null)
            completionTextObj = GameObject.Find("Code");

        if (completionTextObj != null)
        {
            // Only reveal if the level is completed
            if (levelsCompleted != null &&
                levelIdx >= 0 &&
                levelIdx < levelsCompleted.Count &&
                levelsCompleted[levelIdx])
            {
                completionTextObj.SetActive(true);
                // Notify JS plugin with human readable message
                NotifyJSCompletion(levelIdx);
            }
            else
            {
                // Hide if not completed
                completionTextObj.SetActive(false);
            }
        }
    }

    private void NotifyJSCompletion(int levelIdx)
    {
        var env = new Envelope
        {
            type = "game_event",
            payload = new Payload
            {
                name = "level_complete",
                level = levelIdx,
                score = score
            }
        };
        string json = JsonUtility.ToJson(env);

#if UNITY_WEBGL && !UNITY_EDITOR
        SendGameEventMessage(json);
#else
        Debug.Log($"[Simulated JS postMessage envelope] {json}");
#endif
    }

    private int GetLevelIndexFromURL()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string url = Application.absoluteURL;
        if (!string.IsNullOrEmpty(url))
        {
            int idx = url.IndexOf("?levelTag=");
            if (idx >= 0)
            {
                int start = idx + "?levelTag=".Length;
                int end = url.IndexOf('&', start);
                string numStr = (end > start) ? url.Substring(start, end - start) : url.Substring(start);
                if (int.TryParse(numStr, out int val))
                {
                    if (val >= 0 && val < levelFiles.Count) return val;
                }
            }
        }
#endif
        return 0;
    }
    #endregion
}
