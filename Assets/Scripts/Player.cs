using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
public class Player : MonoBehaviour
{
    // ---------- Gameplay ----------
    public float speed = 4f;

    private Rigidbody2D rb2d;
    private Animator animator;
    private int knockbackCount = 0;
    private int swingCount = 0;

    [HideInInspector] public int health = 100;
    [HideInInspector] public int keyCount = 0;

    public bool IsSwinging() => swingCount > 0;

    // ---------- WebGL interop ----------
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void SendGameEventMessage(string jsonPayload);
#endif

    private static void SendLogOut(string json, string suggestedName = "session_log")
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SendGameEventMessage(json);
#else
        Debug.Log($"[Simulated SendGameEventMessage] {json}");
#endif
    }

    // ---------- Replay Logger ----------
    [Serializable]
    private class PlayerReplayLog
    {

        [Serializable]
        public class InputEvent
        {
            public float t;
            public string type; // "move", "button"
            public string name; // axis/button name
            public float value; // axis value (for movement)
            public bool down;   // true for press, false for release (for buttons)
        }

        [Serializable]
        public class CollisionEvent
        {
            public float t;
            public string otherTag;
            public string otherName;
            public float cx, cy;   // first contact point (if available)
            public float px, py;   // player position at impact
        }

        [Serializable]
        public class SwordEvent
        {
            public float t;
            public string type = "sword"; // event discriminator
            public bool down;             // true for edge press
        }

        [Serializable]
        public class Upload
        {
            public string name = "session_log";
            public string sessionId;
            public string sceneName;
            public int sceneBuildIndex;
            public float duration;


            // Input event stream
            public List<InputEvent> inputs = new List<InputEvent>(2048);
            public List<CollisionEvent> collisions = new List<CollisionEvent>(256);

            public int finalHealth;
            public int finalScore;
        }

        public Upload data = new Upload();
        public float t0;

        public PlayerReplayLog()
        {
            data.sessionId = DateTime.UtcNow.Ticks.ToString("X");
        }


        public void LogInputEvent(string type, string name, float value = 0f, bool down = false)
        {
            data.inputs.Add(new InputEvent
            {
                t = Time.realtimeSinceStartup - t0,
                type = type,
                name = name,
                value = value,
                down = down
            });
        }

        public void LogCollision(string otherTag, string otherName, Vector2 playerPos, Vector2 contactPoint)
        {
            data.collisions.Add(new CollisionEvent
            {
                t = Time.realtimeSinceStartup - t0,
                otherTag = otherTag,
                otherName = otherName,
                px = playerPos.x,
                py = playerPos.y,
                cx = contactPoint.x,
                cy = contactPoint.y
            });
        }

        public string FinalizeAndSerialize(int finalHealth, int finalScore)
        {
            data.finalHealth = finalHealth;
            data.finalScore = finalScore;
            data.duration = Time.realtimeSinceStartup - t0;
            return JsonUtility.ToJson(data);
        }
    }

    private PlayerReplayLog logger;


    // Track previous input state for movement axes
    private float prevHorizontal = 0f;
    private float prevVertical = 0f;

    // ---------- Unity lifecycle ----------
    private void Start()
    {
        rb2d = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();

        logger = new PlayerReplayLog();
        var scn = SceneManager.GetActiveScene();
        logger.data.sceneName = scn.name;
        logger.data.sceneBuildIndex = scn.buildIndex;
        logger.t0 = Time.realtimeSinceStartup;
    }
    

    private void Update()
    {
        var gm = DemoGameManager.instance;
        if (gm != null && !gm.doingSetup)
        {
            // --- Log movement input changes ---
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(h - prevHorizontal) > 0.01f)
            {
                logger.LogInputEvent("move", "Horizontal", h);
                prevHorizontal = h;
            }
            if (Mathf.Abs(v - prevVertical) > 0.01f)
            {
                logger.LogInputEvent("move", "Vertical", v);
                prevVertical = v;
            }

            // --- Log button presses/releases ---
            if (Input.GetButtonDown("Sword"))
            {
                animator.SetTrigger("playerChop");
                swingCount = 10;
                logger.LogInputEvent("button", "Sword", down: true);
            }
            if (Input.GetButtonUp("Sword"))
            {
                logger.LogInputEvent("button", "Sword", down: false);
            }
            // Add more buttons as needed (e.g., Interact)

            if (swingCount > 0) swingCount--;
        }

        // Death check — send log before reset
        if (health <= 0)
        {
            int score = (DemoGameManager.instance != null) ? DemoGameManager.instance.score : 0;
            string json = logger.FinalizeAndSerialize(finalHealth: health, finalScore: score);
            SendLogOut(json, logger.data.name);

            if (DemoGameManager.instance != null)
                DemoGameManager.instance.OnPlayerDeath();
        }
    }

    private void FixedUpdate()
    {
        // Keep physics/movement here, but NO LOGGING here anymore.
        var gm = DemoGameManager.instance;
        if (gm != null && !gm.doingSetup)
        {
            if (knockbackCount == 0)
            {
                Vector2 targetVelocity = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                rb2d.velocity = targetVelocity * speed;

                if (targetVelocity.x > 0) GetComponent<SpriteRenderer>().flipX = false;
                else if (targetVelocity.x < 0) GetComponent<SpriteRenderer>().flipX = true;
            }
            else
            {
                knockbackCount--;
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // Event: collision
        Vector2 cp = (collision.contactCount > 0) ? collision.GetContact(0).point : (Vector2)transform.position;
        logger.LogCollision(
            otherTag: collision.gameObject.tag,
            otherName: collision.gameObject.name,
            playerPos: transform.position,
            contactPoint: cp
        );

        // ---- original gameplay ----
        if (collision.gameObject.CompareTag("Enemy"))
        {
            collision.gameObject.GetComponent<Animator>()?.SetTrigger("enemyAttack");
            animator.SetTrigger("playerHit");
            knockbackCount = 10;

            var direction = -(collision.gameObject.transform.position - transform.position).normalized;
            rb2d.AddForce(direction * 500);
            health -= 10;
        }
        else if (collision.gameObject.CompareTag("Key"))
        {
            keyCount++;
            Destroy(collision.gameObject);
        }
        else if (collision.gameObject.CompareTag("Door"))
        {
            if (keyCount > 0)
            {
                keyCount--;
                Destroy(collision.gameObject);
            }
        }
        else if (collision.gameObject.CompareTag("Treasure"))
        {
            if (DemoGameManager.instance != null)
                DemoGameManager.instance.score += 100;
            Destroy(collision.gameObject);
        }
        else if (collision.gameObject.CompareTag("Exit"))
        {
            // LEVEL COMPLETE — send log before scene change
            int score = (DemoGameManager.instance != null) ? DemoGameManager.instance.score : 0;
            string json = logger.FinalizeAndSerialize(finalHealth: health, finalScore: score);
            SendLogOut(json, logger.data.name);

            if (DemoGameManager.instance != null)
                DemoGameManager.instance.OnLevelComplete();
        }
    }
}
