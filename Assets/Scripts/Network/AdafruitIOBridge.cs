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
        public bool sync_started;
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
        if (!dockerBridgeUrl.StartsWith("http://") && !dockerBridgeUrl.StartsWith("https://"))
        {
            dockerBridgeUrl = "http://" + dockerBridgeUrl;
        }
        PlayerPrefs.SetString("DockerBridgeUrl", dockerBridgeUrl);
        PlayerPrefs.Save();
        StartCoroutine(TestDockerHealthRoutine());
    }

    private IEnumerator TestDockerHealthRoutine()
    {
        if (string.IsNullOrEmpty(dockerBridgeUrl)) yield break;
        string healthUrl = $"{dockerBridgeUrl.TrimEnd('/')}/health";
        lastStatusMessage = $"Đang thử kết nối {dockerBridgeUrl}...";
        using (UnityWebRequest req = UnityWebRequest.Get(healthUrl))
        {
            req.timeout = 3;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                isUsingDocker = true;
                isConnected = true;
                lastStatusMessage = "Docker Bridge: Đã kết nối!";
            }
            else
            {
                lastStatusMessage = $"Lỗi Docker: {req.error}";
            }
        }
    }

    public string GetDockerBridgeUrl() => dockerBridgeUrl;
    public bool IsConnected() => isConnected;
    public bool IsUsingDocker() => isUsingDocker;
    public string GetStatusMessage() => lastStatusMessage;
    public bool IsServerConfigOpen() => showServerConfig;
    public void ToggleServerConfig()
    {
        showServerConfig = !showServerConfig;
        tempDockerUrl = dockerBridgeUrl;
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
                        isConnected = true;
                        DockerControlResponse ctrl = JsonUtility.FromJson<DockerControlResponse>(dReq.downloadHandler.text);
                        if (ctrl != null)
                        {
                            ApplyLevelControl(ctrl.set_level);
                            ApplyPauseControl(ctrl.pause_game);
                            ApplyResetControl(ctrl.reset_ts);
                            if (ctrl.sync_started && GameHUDController.Instance != null && !GameHUDController.Instance.IsGameStarted())
                            {
                                GameHUDController.Instance.StartGame(false);
                            }
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
                        isConnected = true;
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

    public void NotifyGameStartedToDocker()
    {
        if (useDockerBridge && !string.IsNullOrEmpty(dockerBridgeUrl))
        {
            StartCoroutine(SendSyncStartRoutine(true));
        }
    }

    private IEnumerator SendSyncStartRoutine(bool started)
    {
        string url = $"{dockerBridgeUrl.TrimEnd('/')}/sync_start?started={(started ? "true" : "false")}";
        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 2;
            yield return req.SendWebRequest();
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
        GUI.depth = showServerConfig ? -100 : -10;

        if (cachedConnStyle == null)
        {
            cachedConnStyle = new GUIStyle();
            cachedConnStyle.fontSize = 12;
            cachedConnStyle.fontStyle = FontStyle.Bold;
        }
        cachedConnStyle.normal.textColor = isConnected ? new Color(0.2f, 1f, 0.4f, 0.85f) : new Color(1f, 0.6f, 0.2f, 0.85f);
        GUIStyle style = cachedConnStyle;

        string mode = isUsingDocker ? "Docker + Adafruit IO" : "Adafruit IO";
        string connText = isConnected ? $"● {mode}: ĐÃ KẾT NỐI (Chạm sửa IP)" : $"○ {mode}: ĐANG KẾT NỐI... (Chạm sửa IP)";
        if (GUI.Button(new Rect(28, 4, 300, 24), connText, style))
        {
            showServerConfig = !showServerConfig;
            tempDockerUrl = dockerBridgeUrl;
        }

        if (showServerConfig)
        {
            float boxW = Mathf.Min(Screen.width - 32, 420);
            float boxH = 210f;
            float boxX = (Screen.width - boxW) / 2f;
            float boxY = Mathf.Min(Screen.height * 0.16f, 120f);
            Rect boxRect = new Rect(boxX, boxY, boxW, boxH);

            GUI.Box(boxRect, "");
            GUI.Box(boxRect, "Cấu hình Docker Bridge (IP Máy tính)");

            GUIStyle lblStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(boxRect.x + 14, boxRect.y + 34, boxW - 28, 26), "Nhập URL Docker (VD: http://192.168.10.237:8000):", lblStyle);

            GUIStyle fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            tempDockerUrl = GUI.TextField(new Rect(boxRect.x + 14, boxRect.y + 66, boxW - 28, 44), tempDockerUrl, fieldStyle);

            GUIStyle btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            float btnW = (boxW - 44) / 3f;

            if (GUI.Button(new Rect(boxRect.x + 14, boxRect.y + 130, btnW, 48), "Lưu & Kết nối", btnStyle))
            {
                useDockerBridge = true;
                SetDockerBridgeUrl(tempDockerUrl);
                showServerConfig = false;
            }

            if (GUI.Button(new Rect(boxRect.x + 22 + btnW, boxRect.y + 130, btnW, 48), "Chạy Direct", btnStyle))
            {
                useDockerBridge = false;
                isUsingDocker = false;
                showServerConfig = false;
            }

            if (GUI.Button(new Rect(boxRect.x + 30 + btnW * 2, boxRect.y + 130, btnW, 48), "Đóng", btnStyle))
            {
                showServerConfig = false;
            }
        }
    }
}
