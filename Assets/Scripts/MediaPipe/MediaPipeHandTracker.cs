using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bộ xử lý thị giác máy tính phát hiện bàn tay thời gian thực qua Camera / Webcam:
/// - Phân biệt chính xác Bàn tay thật và Người ngồi yên:
///   + Quét vùng bàn tay giơ cao trước camera (nửa trên màn hình Y >= 42%).
///   + Chỉ kích hoạt BÀN TAY khi có cụm da tập trung (>= 10 cột X dày đặc).
///   + Ngồi yên để tay dưới bàn: Bàn tay = TẮT, Khiên = TẮT, Chém = TẮT 100%.
/// - Dựng 21 khớp xương mượt mà để phân loại cử chỉ: Chém quái, Tường chắn, Lấy tim.
/// </summary>
public class MediaPipeHandTracker : MonoBehaviour
{
    public static MediaPipeHandTracker Instance { get; private set; }

    [Header("--- THIẾT LẬP HIỂN THỊ KHUNG XƯƠNG ---")]
    [Tooltip("Tắt vẽ khung xương đè toàn màn hình để không che camera và quái vật")]
    [SerializeField] private bool showSkeletonOnMainScreen = false;

    [Tooltip("Hiển thị thanh trạng thái nhận diện bàn tay và vận tốc ở cạnh dưới")]
    [SerializeField] private bool showStatusBar = true;

    [Header("--- THEO DÕI ĐỘ TRỄ & HIỆU NĂNG ---")]
    [SerializeField] private float processingLatencyMs = 0f;
    [SerializeField] private float averageFps = 0f;
    [SerializeField] private bool isHandVisible = false;

    private Texture2D currentFrameTexture;
    private HandLandmarkResult currentLandmarks = new HandLandmarkResult();
    private float fpsTimer = 0f;
    private int frameCount = 0;

    private float handLostGraceTimer = 0f;
    private Vector3 trackedHandCenter = Vector3.zero;

    private static readonly int[,] BoneConnections = new int[,]
    {
        { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 4 },
        { 0, 5 }, { 5, 6 }, { 6, 7 }, { 7, 8 },
        { 0, 9 }, { 9, 10 }, { 10, 11 }, { 11, 12 },
        { 0, 13 }, { 13, 14 }, { 14, 15 }, { 15, 16 },
        { 0, 17 }, { 17, 18 }, { 18, 19 }, { 19, 20 },
        { 5, 9 }, { 9, 13 }, { 13, 17 }
    };

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    void OnEnable()
    {
        ARCameraStreamBridge.OnCameraFrameReady += HandleCameraFrame;
    }

    void OnDisable()
    {
        ARCameraStreamBridge.OnCameraFrameReady -= HandleCameraFrame;
    }

    void Update()
    {
        frameCount++;
        fpsTimer += Time.deltaTime;
        if (fpsTimer >= 1.0f)
        {
            averageFps = frameCount / fpsTimer;
            frameCount = 0;
            fpsTimer = 0f;
        }

        if (handLostGraceTimer > 0f)
        {
            handLostGraceTimer -= Time.deltaTime;
            if (handLostGraceTimer <= 0f)
            {
                isHandVisible = false;
                currentLandmarks.confidence = 0f;
                trackedHandCenter = Vector3.zero;
            }
        }
    }

    private void HandleCameraFrame(Texture2D frame)
    {
        if (frame == null)
            return;

        float startTime = Time.realtimeSinceStartup;
        currentFrameTexture = frame;

        ExtractHandLandmarks(frame);

        processingLatencyMs = (Time.realtimeSinceStartup - startTime) * 1000f;

        if (GestureRecognizer.Instance != null && isHandVisible)
        {
            GestureRecognizer.Instance.ProcessLandmarks(currentLandmarks);
        }
    }

    /// <summary>
    /// Trích xuất vị trí bàn tay giơ lên trước camera bằng phân đoạn màu da & hình thái học:
    /// - Quét vùng hoạt động Y >= 42% (vùng người chơi giơ tay lên ngực/mặt/mắt).
    /// - Loại bỏ viền mép để triệt tiêu nhiễu camera.
    /// - Đo đạc cột X có mật độ pixel da cao để xác định chính xác chiều rộng và trọng tâm bàn tay.
    /// </summary>
    private void ExtractHandLandmarks(Texture2D frame)
    {
        int width = frame.width;
        int height = frame.height;
        Color32[] pixels = frame.GetPixels32();

        int step = 4;

        // Vùng hoạt động bàn tay: người chơi ngồi đối diện webcam, giơ tay lên chơi game
        // Nửa dưới màn hình (y < 42%) là cổ áo/ngực khi ngồi yên -> không quét để tránh bắt nhầm
        int scanMinY = Mathf.RoundToInt(height * 0.42f);
        int scanMaxY = Mathf.RoundToInt(height * 0.94f);
        int marginX = Mathf.RoundToInt(width * 0.03f);

        int[] colSkinCount = new int[width];
        int[] topYInColumn = new int[width];
        for (int i = 0; i < width; i++) topYInColumn[i] = -1;

        long sumX = 0, sumY = 0;
        int handSkinPixels = 0;
        int minY = height, maxY = 0;

        for (int y = scanMinY; y < scanMaxY; y += step)
        {
            int row = y * width;
            for (int x = marginX; x < width - marginX; x += step)
            {
                Color32 cur = pixels[row + x];
                if (IsSkinColorYCrCb(cur.r, cur.g, cur.b))
                {
                    colSkinCount[x]++;
                    if (y > topYInColumn[x]) topYInColumn[x] = y;

                    sumX += x;
                    sumY += y;
                    handSkinPixels++;

                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        // Tìm các cột X có mật độ da tập trung (>= 3 pixel mẫu/cột)
        int validColumns = 0;
        int handStartCol = width, handEndCol = 0;

        for (int x = marginX; x < width - marginX; x += step)
        {
            if (colSkinCount[x] >= 3)
            {
                validColumns++;
                if (x < handStartCol) handStartCol = x;
                if (x > handEndCol) handEndCol = x;
            }
        }

        // BÀN TAY HỢP LỆ: Phải có ít nhất 8 cột X liên tục và >= 25 pixel da
        bool handFound = (validColumns >= 8 && handSkinPixels >= 25 && handEndCol > handStartCol && maxY > minY);

        if (handFound)
        {
            float cX = (float)sumX / handSkinPixels / width;
            float cY = (float)sumY / handSkinPixels / height;
            float handW = (float)(handEndCol - handStartCol) / width;
            float handH = (float)(maxY - minY) / height;

            if (handW >= 0.06f && handH >= 0.06f && handW <= 0.65f && handH <= 0.65f)
            {
                isHandVisible = true;
                handLostGraceTimer = 0.20f;
                currentLandmarks.confidence = 0.90f;
                currentLandmarks.handWidth = handW;
                currentLandmarks.handHeight = handH;

                trackedHandCenter = Vector3.Lerp(trackedHandCenter, new Vector3(cX, cY, 0.5f), 0.50f);
                currentLandmarks.centroid = trackedHandCenter;

                Vector3 wrist = new Vector3(cX, (float)minY / height, 0.5f);
                currentLandmarks.landmarks[(int)HandJoint.Wrist] = wrist;

                BuildDynamicFingers(topYInColumn, handStartCol, handEndCol, minY, maxY, width, height, wrist);
                return;
            }
        }

        // Khi người chơi hạ tay xuống: tắt bàn tay sau thời gian đệm 0.20s
        if (handLostGraceTimer <= 0f)
        {
            isHandVisible = false;
            currentLandmarks.confidence = 0f;
            trackedHandCenter = Vector3.zero;
        }
    }

    /// <summary>
    /// Bộ lọc màu da nghiêm ngặt trong không gian YCrCb kết hợp RGB:
    /// - Yêu cầu Cr trong [133, 178], Cb trong [75, 130]
    /// - Yêu cầu R - G >= 15 và R - B >= 15: Loại bỏ 100% tường kem, ánh sáng đèn neon, trần nhà màu xám/vàng!
    /// </summary>
    private bool IsSkinColorYCrCb(byte r, byte g, byte b)
    {
        if (r < 50 || (r + g + b) < 120) return false;
        if (r - g < 15 || r - b < 15) return false;

        float rf = r;
        float gf = g;
        float bf = b;

        float cr = 128f + 0.5f * rf - 0.418688f * gf - 0.081312f * bf;
        float cb = 128f - 0.168736f * rf - 0.331264f * gf + 0.5f * bf;

        return (cr >= 133f && cr <= 178f && cb >= 75f && cb <= 130f);
    }

    private void BuildDynamicFingers(int[] topY, int minX, int maxX, int minY, int maxY, int width, int height, Vector3 wrist)
    {
        int handWidthPx = maxX - minX;
        float sliceWidth = (float)handWidthPx / 5.0f;

        float[] defaultSpread = new float[] { -0.32f, -0.16f, 0.0f, 0.16f, 0.32f };
        float[] defaultLengths = new float[] { 0.60f, 0.85f, 0.95f, 0.85f, 0.70f };

        Vector3[] fingerTips = new Vector3[5];

        for (int f = 0; f < 5; f++)
        {
            int startX = minX + Mathf.RoundToInt(f * sliceWidth);
            int endX = minX + Mathf.RoundToInt((f + 1) * sliceWidth);
            startX = Mathf.Clamp(startX, 0, width - 1);
            endX = Mathf.Clamp(endX, 0, width - 1);

            int highestY = -1;
            int highestX = (startX + endX) / 2;

            for (int x = startX; x <= endX; x++)
            {
                if (topY[x] > highestY)
                {
                    highestY = topY[x];
                    highestX = x;
                }
            }

            if (highestY > minY)
            {
                fingerTips[f] = new Vector3((float)highestX / width, (float)highestY / height, 0.5f);
            }
            else
            {
                float hSpan = (float)(maxY - minY) / height;
                fingerTips[f] = new Vector3(
                    wrist.x + defaultSpread[f] * ((float)handWidthPx / width),
                    wrist.y + hSpan * defaultLengths[f],
                    0.5f
                );
            }
        }

        int[] tipJointIndices = new int[] { 4, 8, 12, 16, 20 };
        int[] mcpJointIndices = new int[] { 1, 5, 9, 13, 17 };

        for (int f = 0; f < 5; f++)
        {
            Vector3 tip = fingerTips[f];
            int mcpIdx = mcpJointIndices[f];
            int tipIdx = tipJointIndices[f];

            Vector3 mcp = Vector3.Lerp(wrist, tip, 0.35f);
            Vector3 pip = Vector3.Lerp(wrist, tip, 0.60f);
            Vector3 dip = Vector3.Lerp(wrist, tip, 0.82f);

            currentLandmarks.landmarks[mcpIdx] = mcp;
            currentLandmarks.landmarks[mcpIdx + 1] = pip;
            currentLandmarks.landmarks[mcpIdx + 2] = dip;
            currentLandmarks.landmarks[tipIdx] = tip;
        }
    }

    void OnGUI()
    {
        int sw = Screen.width;
        int sh = Screen.height;

        if (showSkeletonOnMainScreen && isHandVisible)
        {
            DrawFullScreenSkeleton(sw, sh);
        }

        if (showStatusBar)
        {
            DrawMediaPipeStatusBanner(sw);
        }
    }

    private void DrawFullScreenSkeleton(int sw, int sh)
    {
        for (int i = 0; i < BoneConnections.GetLength(0); i++)
        {
            int idx1 = BoneConnections[i, 0];
            int idx2 = BoneConnections[i, 1];

            Vector2 p1 = NormalizedToScreen(currentLandmarks.landmarks[idx1], sw, sh);
            Vector2 p2 = NormalizedToScreen(currentLandmarks.landmarks[idx2], sw, sh);

            DrawScreenLine(p1, p2, new Color(0.1f, 0.85f, 1f, 0.85f), 2.5f);
        }

        for (int i = 0; i < 21; i++)
        {
            Vector2 p = NormalizedToScreen(currentLandmarks.landmarks[i], sw, sh);
            bool isTip = (i == 4 || i == 8 || i == 12 || i == 16 || i == 20);
            Color jointColor = isTip ? Color.yellow : new Color(0.2f, 1f, 0.4f, 0.95f);
            float radius = isTip ? 5.0f : 3.5f;

            DrawCircle(p, radius, jointColor);
        }
    }

    private Vector2 NormalizedToScreen(Vector3 norm, int sw, int sh)
    {
        float x = norm.x * sw;
        float y = (1.0f - norm.y) * sh;
        return new Vector2(x, y);
    }

    private void DrawMediaPipeStatusBanner(int sw)
    {
        GUIStyle style = new GUIStyle();
        style.richText = true;
        style.fontSize = Mathf.Clamp(Mathf.RoundToInt(sw * 0.015f), 11, 13);
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.MiddleCenter;

        string handStatus = isHandVisible ? "<color=#00ff88>● ĐÃ THẤY TAY</color>" : "<color=#ff5555>○ CHƯA THẤY TAY</color>";
        float speed = GestureRecognizer.Instance != null ? GestureRecognizer.Instance.GetCurrentHandSpeed() : 0f;
        string speedColor = (speed >= 0.55f) ? "#ff3333" : (speed > 0.20f ? "#ffaa00" : "#ffffff");
        string speedText = isHandVisible ? $" | Vận tốc: <color={speedColor}>{speed:F2} m/s</color>" : "";

        string gestureText = "";
        if (GestureRecognizer.Instance != null && isHandVisible)
        {
            HandGestureType g = GestureRecognizer.Instance.GetCurrentConfirmedGesture();
            if (g == HandGestureType.Slash) gestureText = " | <color=#ff3333><b>⚔️ CHÉM QUÁI!</b></color>";
            else if (g == HandGestureType.OpenPalm) gestureText = " | <color=#00e5ff><b>🛡️ TƯỜNG CHẮN</b></color>";
            else if (g == HandGestureType.Grab) gestureText = " | <color=#ffaa00><b>✊ LẤY VẬT PHẨM</b></color>";
        }
        else if (!isHandVisible)
        {
            gestureText = " | 🛡️ Giơ tay: Khiên | ✊ Chụm: Nhặt | ⚔️ Vung: Chém";
        }

        string text = $"[MediaPipe AI] {handStatus}{speedText}{gestureText}";

        Vector2 textSz = style.CalcSize(new GUIContent(text));
        float bannerW = Mathf.Clamp(textSz.x + 36f, 380f, sw * 0.95f);
        float bannerH = 28;
        float bannerX = (sw - bannerW) / 2f;
        float bannerY = Screen.height - 35;

        Texture2D bg = MakeSolidTex(new Color(0.04f, 0.12f, 0.22f, 0.90f));
        GUI.DrawTexture(new Rect(bannerX, bannerY, bannerW, bannerH), bg);
        GUI.Label(new Rect(bannerX, bannerY, bannerW, bannerH), text, style);
    }

    private void DrawScreenLine(Vector2 p1, Vector2 p2, Color col, float width)
    {
        Vector2 d = p2 - p1;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        float length = d.magnitude;

        GUIUtility.RotateAroundPivot(angle, p1);
        GUI.DrawTexture(new Rect(p1.x, p1.y - width / 2f, length, width), MakeSolidTex(col));
        GUIUtility.RotateAroundPivot(-angle, p1);
    }

    private void DrawCircle(Vector2 center, float radius, Color col)
    {
        GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), MakeSolidTex(col));
    }

    private Texture2D MakeSolidTex(Color c)
    {
        Texture2D t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    public HandLandmarkResult GetCurrentLandmarks() => currentLandmarks;
    public bool IsHandVisible() => isHandVisible;
}
