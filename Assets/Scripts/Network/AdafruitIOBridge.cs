using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// Cầu nối đồng bộ 2 chiều giữa Unity (PC + Mobile) <-> Docker Bridge (MQTT) <-> Adafruit IO:
/// - Nếu Docker Bridge (http://localhost:8000 trên PC hoặc IP LAN trên Mobile) đang bật:
///   đồng bộ siêu nhanh qua Docker MQTT (~0.8s).
/// - Nếu không kết nối được Docker (ví dụ Mobile dùng 4G hoặc chưa bật Docker Desktop):
///   tự động fallback sang kết nối trực tiếp Adafruit IO HTTPS để Mobile luôn chạy 100%!
/// </summary>
public class AdafruitIOBridge : MonoBehaviour
{
    public static AdafruitIOBridge Instance { get; private set; }

    [Header("--- TÀI KHOẢN ADAFRUIT IO ---")]
    [SerializeField] private string aioUsername = "HuyenTrang";
    [SerializeField] private string aioKey = "";

    [Header("--- CẤU HÌNH DOCKER BRIDGE (TÙY CHỌN) ---")]
    [Tooltip("Ưu tiên đồng bộ qua Docker Bridge nếu có (tự động fallback về Adafruit trực tiếp nếu không thấy Docker)")]
    [SerializeField] private bool useDockerBridge = true;

    [Tooltip("Địa chỉ Docker Bridge: trên PC dùng http://localhost:8000; trên điện thoại cùng Wi-Fi đổi thành http://IP_MAY_TINH:8000")]
    [SerializeField] private string dockerBridgeUrl = "http://localhost:8000";

    [Header("--- TẦN SUẤT ĐỒNG BỘ ---")]
    [SerializeField] private float publishInterval = 1.2f;
    [SerializeField] private float pollInterval = 1.0f;
    [SerializeField] private bool enableRemoteControl = true;

    [Header("--- TRẠNG THÁI KẾT NỐI ---")]
    [SerializeField] private bool isConnected = false;
    [SerializeField] private bool isUsingDocker = false;
    [SerializeField] private string lastStatusMessage = "Đang khởi tạo...";

    [Serializable]
    private class GroupResponse
    {
        public FeedInfo[] feeds;
    }

    [Serializable]
    private class FeedInfo
    {
        public string key;
        public string last_value;
        public string updated_at;
    }

    [Serializable]
    private class DockerControlResponse
    {
        public int set_level;
        public int pause_game;
        public string reset_ts;
        public bool mqtt_connected;
    }

    private int lastSentHp = -1;
    private int lastSentScore = -1;
    private int lastSentItems = -1;
    private int lastSentLevel = -1;
    private int lastSentKills = -1;
    private int lastSentMaxCombo = -1;
    private int lastSentHand = -1;

    private int lastReceivedLevel = -1;
    private int lastReceivedPause = -1;
    private string lastResetTimestamp = null;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadLocalCredentials();
            string savedUrl = PlayerPrefs.GetString("DockerBridgeUrl", "");
            if (!string.IsNullOrEmpty(savedUrl))
            {
                dockerBridgeUrl = savedUrl;
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void LoadLocalCredentials()
    {
        TextAsset cfg = Resources.Load<TextAsset>("adafruit_config");
        if (cfg != null && !string.IsNullOrEmpty(cfg.text))
        {
            foreach (string rawLine in cfg.text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("username=")) aioUsername = line.Substring(9).Trim();
                else if (line.StartsWith("key=")) aioKey = line.Substring(4).Trim();
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        if (Instance == null && FindAnyObjectByType<AdafruitIOBridge>() == null)
        {
            GameObject go = new GameObject("AdafruitIOBridge");
            go.AddComponent<AdafruitIOBridge>();
            DontDestroyOnLoad(go);
        }
    }

    void Start()
    {
        StartCoroutine(PublishTelemetryRoutine());
        if (enableRemoteControl)
        {
            StartCoroutine(PollControlRoutine());
        }
    }

    public void SetDockerBridgeUrl(string url)
    {
        dockerBridgeUrl = url.TrimEnd('/');
        PlayerPrefs.SetString("DockerBridgeUrl", dockerBridgeUrl);
        PlayerPrefs.Save();
    }

    private IEnumerator PublishTelemetryRoutine()
    {
        yield return new WaitForSecondsRealtime(0.5f);

        List<string> batchEntries = new List<string>(8);

        while (true)
        {
            batchEntries.Clear();

            if (PlayerCombatController.Instance != null)
            {
                int hp = PlayerCombatController.Instance.GetCurrentHealth();
                if (hp != lastSentHp)
                {
                    lastSentHp = hp;
                    batchEntries.Add($"{{\"key\":\"player-hp\",\"value\":\"{hp}\"}}");
                }

                int items = PlayerCombatController.Instance.GetItemsCollected();
                if (items != lastSentItems)
                {
                    lastSentItems = items;
                    batchEntries.Add($"{{\"key\":\"items-collected\",\"value\":\"{items}\"}}");
                }

                int maxCombo = PlayerCombatController.Instance.GetMaxComboCount();
                if (maxCombo != lastSentMaxCombo)
                {
                    lastSentMaxCombo = maxCombo;
                    batchEntries.Add($"{{\"key\":\"max-combo\",\"value\":\"{maxCombo}\"}}");
                }
            }

            if (GameHUDController.Instance != null)
            {
                int score = GameHUDController.Instance.GetScore();
                if (score != lastSentScore)
                {
                    lastSentScore = score;
                    batchEntries.Add($"{{\"key\":\"player-score\",\"value\":\"{score}\"}}");
                }

                int kills = GameHUDController.Instance.GetTotalMonstersKilled();
                if (kills != lastSentKills)
                {
                    lastSentKills = kills;
                    batchEntries.Add($"{{\"key\":\"monsters-killed\",\"value\":\"{kills}\"}}");
                }
            }

            if (MonsterSpawner.Instance != null)
            {
                int level = MonsterSpawner.Instance.GetCurrentLevel();
                if (level != lastSentLevel)
                {
                    lastSentLevel = level;
                    batchEntries.Add($"{{\"key\":\"current-level\",\"value\":\"{level}\"}}");
                }
            }

            if (MediaPipeHandTracker.Instance != null)
            {
                int hand = MediaPipeHandTracker.Instance.IsHandVisible() ? 1 : 0;
                if (hand != lastSentHand)
                {
                    lastSentHand = hand;
                    batchEntries.Add($"{{\"key\":\"hand-detected\",\"value\":\"{hand}\"}}");
                }
            }

            if (batchEntries.Count > 0)
            {
                string jsonBody = $"{{\"feeds\":[{string.Join(",", batchEntries)}]}}";
                yield return StartCoroutine(SendTelemetryWithFallbackRoutine(jsonBody, batchEntries.Count));
            }

            yield return new WaitForSecondsRealtime(publishInterval);
        }
    }

    private IEnumerator SendTelemetryWithFallbackRoutine(string jsonBody, int count)
    {
        bool sentViaDocker = false;

        if (useDockerBridge && !string.IsNullOrEmpty(dockerBridgeUrl))
        {
            string dockerUrl = $"{dockerBridgeUrl.TrimEnd('/')}/telemetry";
            using (UnityWebRequest dReq = new UnityWebRequest(dockerUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                dReq.uploadHandler = new UploadHandlerRaw(bodyRaw);
                dReq.downloadHandler = new DownloadHandlerBuffer();
                dReq.SetRequestHeader("Content-Type", "application/json");
                dReq.timeout = 2;

                yield return dReq.SendWebRequest();

                if (dReq.result == UnityWebRequest.Result.Success)
                {
                    sentViaDocker = true;
                    isUsingDocker = true;
                    isConnected = true;
                    lastStatusMessage = $"Docker MQTT: đã gửi {count} thông số";
                }
            }
        }

        if (!sentViaDocker)
        {
            isUsingDocker = false;
            string url = $"https://io.adafruit.com/api/v2/{aioUsername}/groups/default/data";
            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("X-AIO-Key", aioKey);
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 8;

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    isConnected = true;
                    lastStatusMessage = $"Adafruit Direct: đã gửi {count} thông số";
                }
                else
                {
                    isConnected = false;
                    lastStatusMessage = $"Lỗi gửi: {req.error}";
                }
            }
        }
    }

    private IEnumerator PollControlRoutine()
    {
        yield return new WaitForSecondsRealtime(0.8f);

        while (true)
        {
            bool polledDocker = false;

            if (useDockerBridge && !string.IsNullOrEmpty(dockerBridgeUrl))
            {
                string dockerCtrlUrl = $"{dockerBridgeUrl.TrimEnd('/')}/control";
                using (UnityWebRequest dReq = UnityWebRequest.Get(dockerCtrlUrl))
                {
                    dReq.timeout = 2;
                    yield return dReq.SendWebRequest();

                    if (dReq.result == UnityWebRequest.Result.Success)
                    {
                        polledDocker = true;
                        isUsingDocker = true;
                        DockerControlResponse ctrl = JsonUtility.FromJson<DockerControlResponse>(dReq.downloadHandler.text);
                        if (ctrl != null)
                        {
                            ApplyLevelControl(ctrl.set_level);
                            ApplyPauseControl(ctrl.pause_game);
                            ApplyResetControl(ctrl.reset_ts);
                        }
                    }
                }
            }

            if (!polledDocker)
            {
                isUsingDocker = false;
                string url = $"https://io.adafruit.com/api/v2/{aioUsername}/groups/default";
                using (UnityWebRequest req = UnityWebRequest.Get(url))
                {
                    req.SetRequestHeader("X-AIO-Key", aioKey);
                    req.timeout = 8;
                    yield return req.SendWebRequest();

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        GroupResponse group = JsonUtility.FromJson<GroupResponse>(req.downloadHandler.text);
                        if (group != null && group.feeds != null)
                        {
                            foreach (var feed in group.feeds)
                            {
                                if (feed.key == "set-level" && int.TryParse(feed.last_value, out int newLevel))
                                    ApplyLevelControl(newLevel);
                                else if (feed.key == "pause-game" && int.TryParse(feed.last_value, out int pauseVal))
                                    ApplyPauseControl(pauseVal);
                                else if (feed.key == "reset-game")
                                    ApplyResetControl(feed.updated_at);
                            }
                        }
                    }
                }
            }

            yield return new WaitForSecondsRealtime(pollInterval);
        }
    }

    private void ApplyLevelControl(int newLevel)
    {
        if (newLevel < 1 || newLevel > 5) return;
        if (lastReceivedLevel == -1) lastReceivedLevel = newLevel;
        else if (newLevel != lastReceivedLevel)
        {
            lastReceivedLevel = newLevel;
            Debug.Log($"<color=green><b>[Adafruit IO] 🕹️ Đổi sang Level {newLevel} từ Dashboard!</b></color>");
            if (MonsterSpawner.Instance != null) MonsterSpawner.Instance.SetLevel(newLevel);
        }
    }

    private void ApplyPauseControl(int pauseVal)
    {
        int normalized = (pauseVal == 1) ? 1 : 0;
        if (lastReceivedPause == -1) lastReceivedPause = normalized;
        else if (normalized != lastReceivedPause)
        {
            lastReceivedPause = normalized;
            Time.timeScale = (normalized == 1) ? 0f : 1f;
            Debug.Log($"<color=yellow><b>[Adafruit IO] ⏸️ {(normalized == 1 ? "TẠM DỪNG" : "TIẾP TỤC")} GAME!</b></color>");
        }
    }

    private void ApplyResetControl(string timestamp)
    {
        if (string.IsNullOrEmpty(timestamp)) return;
        if (lastResetTimestamp == null) lastResetTimestamp = timestamp;
        else if (timestamp != lastResetTimestamp)
        {
            lastResetTimestamp = timestamp;
            Time.timeScale = 1f;
            lastSentHp = -1;
            lastSentScore = -1;
            lastSentItems = -1;
            lastSentLevel = -1;
            lastSentKills = -1;
            lastSentMaxCombo = -1;
            Debug.Log("<color=cyan><b>[Adafruit IO] 🔄 RESET GAME từ Dashboard!</b></color>");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    private bool showServerConfig = false;
    private string tempDockerUrl = "";
    private GUIStyle cachedConnStyle;

    void OnGUI()
    {
        if (cachedConnStyle == null)
        {
            cachedConnStyle = new GUIStyle();
            cachedConnStyle.fontSize = 10;
            cachedConnStyle.fontStyle = FontStyle.Bold;
        }
        cachedConnStyle.normal.textColor = isConnected ? new Color(0.2f, 1f, 0.4f, 0.85f) : new Color(1f, 0.6f, 0.2f, 0.85f);
        GUIStyle style = cachedConnStyle;

        string mode = isUsingDocker ? "Docker + Adafruit IO" : "Adafruit IO";
        string connText = isConnected ? $"● {mode}: ĐÃ KẾT NỐI (Chạm sửa IP)" : $"○ {mode}: ĐANG KẾT NỐI... (Chạm sửa IP)";
        if (GUI.Button(new Rect(28, 4, 280, 18), connText, style))
        {
            showServerConfig = !showServerConfig;
            tempDockerUrl = dockerBridgeUrl;
        }

        if (showServerConfig)
        {
            float boxW = Mathf.Min(Screen.width - 40, 320);
            Rect boxRect = new Rect(25, 105, boxW, 125);
            GUI.Box(boxRect, "Cấu hình Docker Bridge (Máy khác)");

            GUI.Label(new Rect(boxRect.x + 10, boxRect.y + 24, boxW - 20, 20), "URL Docker (VD: http://192.168.1.10:8000):");
            tempDockerUrl = GUI.TextField(new Rect(boxRect.x + 10, boxRect.y + 46, boxW - 20, 26), tempDockerUrl);

            if (GUI.Button(new Rect(boxRect.x + 10, boxRect.y + 82, 110, 30), "Lưu & Kết nối"))
            {
                useDockerBridge = true;
                SetDockerBridgeUrl(tempDockerUrl);
                showServerConfig = false;
            }

            if (GUI.Button(new Rect(boxRect.x + 128, boxRect.y + 82, 110, 30), "Chạy Direct"))
            {
                useDockerBridge = false;
                isUsingDocker = false;
                showServerConfig = false;
            }

            if (GUI.Button(new Rect(boxRect.x + 246, boxRect.y + 82, 60, 30), "Đóng"))
            {
                showServerConfig = false;
            }
        }
    }
}
