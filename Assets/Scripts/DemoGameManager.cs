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
    private struct Payload
    {
        public int level;
        public int score;
        public float completeTime;
    }
    #endregion

    #region Fields
    private List<ManagerEvent> managerEvents = new List<ManagerEvent>();
    private float sessionStartTime;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void SendGameEventMessage(string message);
    [DllImport("__Internal")]
    private static extern void SendLevelCompleteMessage(string message);
#endif

    [Header("Debug/Test")]
    public bool forceWebGLMode = false;

    public List<TextAsset> levelFiles;
    public static DemoGameManager instance = null;

    private LevelManager levelManager;
    private int levelIndex = 0;
    bool isLevelComplete = false;

    [HideInInspector] public List<GameObject> enemies;
    [HideInInspector] public int score = 0;
    [HideInInspector] public bool doingSetup = false;
    [HideInInspector] public bool playerDied = false;

    // Cached UI references
    private GameObject scoreTextObj;
    private GameObject completionTextObj;

    private float spacebarPressedTime = -1f;
    float timeFromSpacebarToFinish;
    #endregion

    #region Lifecycle
    private void Awake()
    {
        if (instance == null) instance = this;
        else if (instance != this) { Destroy(gameObject); return; }

        DontDestroyOnLoad(gameObject);
        levelManager = GetComponent<LevelManager>();
        int urlLevelIndex = GetLevelIndexFromURL();
        Debug.Log("URL Level:" + urlLevelIndex);
        if (urlLevelIndex > 0 && urlLevelIndex < levelFiles.Count)
            levelIndex = urlLevelIndex;
        else
            levelIndex = 0; // Tutorial only

        // Register scene loaded callback
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InitGame();
    }
    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void GetGameObjects()
    {
        // Cache references after instance assignment
        scoreTextObj = GameObject.FindGameObjectWithTag("ScoreText");
        completionTextObj = GameObject.FindGameObjectWithTag("CompletionCode");
        if (completionTextObj != null)
        {
            completionTextObj.gameObject.SetActive(false);
        }
    }
    #endregion

    #region Public API
    public void OnPlayerDeath()
    {
        playerDied = true;
        // Reload current scene
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex, LoadSceneMode.Single);
    }

    public void OnLevelComplete()
    {
        isLevelComplete = true;
        // Calculate time spent from spacebar to completion
        timeFromSpacebarToFinish = (spacebarPressedTime > 0f) ? (Time.realtimeSinceStartup - spacebarPressedTime) : -1f;
        Debug.Log($"[LevelComplete] Time from spacebar to finish: {timeFromSpacebarToFinish:F2} seconds");

        // You can extend Payload struct to include this if needed
        RevealCompletionCode(levelIndex);
    }
    #endregion

    #region Game Setup

    private void InitGame()
    {
        GetGameObjects();
        int urlLevelIndex = GetLevelIndexFromURL();
        if (urlLevelIndex >= 0 && urlLevelIndex < levelFiles.Count)
        {
            levelIndex = urlLevelIndex;
        }
        // If the current level is already complete, don't init again.
        if (isLevelComplete)
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
        // Only reveal if the level is completed
        if (levelIdx >= 0 && levelIdx < levelFiles.Count && isLevelComplete)
        {
            if (completionTextObj != null)
            {
                completionTextObj.gameObject.SetActive(true);
            }
            // Notify JS plugin with human readable message
            NotifyJSCompletion(levelIdx);
        }
    }

    private void NotifyJSCompletion(int levelIdx)
    {
        Payload payload = new Payload
        {
            level = levelIdx,
            score = score,
            completeTime = timeFromSpacebarToFinish
        };
        string json = JsonUtility.ToJson(payload);

#if UNITY_WEBGL && !UNITY_EDITOR
        SendLevelCompleteMessage(json);
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
