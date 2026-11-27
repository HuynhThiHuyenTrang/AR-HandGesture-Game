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

    // Bộ đệm tái sử dụng hoàn toàn để không cấp phát rác GC mỗi frame
    private const int GRID_W = 40;
    private const int GRID_H = 30;
    private const int GRID_SIZE = GRID_W * GRID_H;

    private Color32[] fallbackPixelBuffer;
    private Color32[] prevPixelBuffer;
    private bool hasPrevPixelFrame = false;
    private readonly int[] gridSkinCount = new int[GRID_SIZE];
    private readonly int[] gridMovingCount = new int[GRID_SIZE];
    private readonly int[] gridBlobId = new int[GRID_SIZE];
    private readonly int[] bfsQueue = new int[GRID_SIZE];
    private readonly int[] bgR = new int[GRID_SIZE];
    private readonly int[] bgG = new int[GRID_SIZE];
    private readonly int[] bgB = new int[GRID_SIZE];
    private readonly int[] curCellSumR = new int[GRID_SIZE];
    private readonly int[] curCellSumG = new int[GRID_SIZE];
    private readonly int[] curCellSumB = new int[GRID_SIZE];
    private readonly int[] curCellCount = new int[GRID_SIZE];
    private int bgWarmupFrames = 0;
    private bool hasActiveHandBox = false;
    private int activeMinGx, activeMaxGx, activeMinGy, activeMaxGy;
    private float activeHandHoldDuration = 0f;
    private int[] topYInColumn;
    private float lastHandMotionTime = -10f;
    private bool hasKnownFace = false;
    private Vector2 knownFacePos = Vector2.zero;
    private float knownFaceW = 0f;
    private float knownFaceH = 0f;
    private float idleStaticBlobTimer = 0f;
    private Vector2 idleStaticBlobPos = Vector2.zero;
    private GUIStyle cachedStatusStyle;

    private struct SkinBlob2D
    {
        public int blobId;
        public int cellCount;
        public int skinPixels;
        public int movingPixels;
        public int minGx;
        public int maxGx;
        public int minGy;
        public int maxGy;
        public float centerX;
        public float centerY;
    }
    private readonly SkinBlob2D[] detectedBlobs = new SkinBlob2D[24];

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
        ARCameraStreamBridge.OnCameraPixelsReady += HandleCameraPixels;
        ARCameraStreamBridge.OnCameraFrameReady += HandleCameraFrame;
    }

    void OnDisable()
    {
        ARCameraStreamBridge.OnCameraPixelsReady -= HandleCameraPixels;
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

    private void HandleCameraPixels(Color32[] pixels, int width, int height)
    {
        if (pixels == null || width <= 0 || height <= 0 || pixels.Length < width * height)
            return;

        float startTime = Time.realtimeSinceStartup;

        bool foundFresh = ExtractHandLandmarksFromPixels(pixels, width, height);

        processingLatencyMs = (Time.realtimeSinceStartup - startTime) * 1000f;

        if (GestureRecognizer.Instance != null)
        {
            if (foundFresh)
                GestureRecognizer.Instance.ProcessLandmarks(currentLandmarks);
            else if (!isHandVisible)
                GestureRecognizer.Instance.NotifyHandLost();
        }
    }

    private void HandleCameraFrame(Texture2D frame)
    {
        if (frame == null)
            return;

        float startTime = Time.realtimeSinceStartup;
        currentFrameTexture = frame;

        int width = frame.width;
        int height = frame.height;
        int len = width * height;
        if (fallbackPixelBuffer == null || fallbackPixelBuffer.Length != len)
        {
            fallbackPixelBuffer = new Color32[len];
        }

        // Tránh cấp phát mảng Color32 mới: nếu là RGBA32 thì copy trực tiếp từ raw texture data
        if (frame.format == TextureFormat.RGBA32)
        {
            frame.GetRawTextureData<Color32>().CopyTo(fallbackPixelBuffer);
        }
        else
        {
            Color32[] tmp = frame.GetPixels32();
            Array.Copy(tmp, fallbackPixelBuffer, Mathf.Min(tmp.Length, len));
        }

        bool foundFresh = ExtractHandLandmarksFromPixels(fallbackPixelBuffer, width, height);

        processingLatencyMs = (Time.realtimeSinceStartup - startTime) * 1000f;

        if (GestureRecognizer.Instance != null)
        {
            if (foundFresh)
                GestureRecognizer.Instance.ProcessLandmarks(currentLandmarks);
            else if (!isHandVisible)
                GestureRecognizer.Instance.NotifyHandLost();
        }
    }

    /// <summary>
    /// Trích xuất bàn tay bằng lưới nền thích nghi 2D (40x30) + Connected-Components + nhận diện khuôn mặt:
    /// - Chỉ xét pixel da NỔI BẬT KHÁC NỀN (loại bỏ hoàn toàn tường, biển gỗ, áo đỏ, mặt ngồi yên).
    /// - Khi tay đứng yên (Tường chắn / Chụm tay), đóng băng nền vùng tay để không bị biến mất.
    /// - Khi camera rung mạnh toàn khung hình, tự động cập nhật lại nền để không kích hoạt cử chỉ giả.
    /// </summary>
    private bool ExtractHandLandmarksFromPixels(Color32[] pixels, int width, int height)
    {
        int step = 2;

        int scanMinY = Mathf.RoundToInt(height * 0.08f);
        int scanMaxY = Mathf.RoundToInt(height * 0.96f);
        int marginX = Mathf.RoundToInt(width * 0.02f);

        int len = width * height;
        if (prevPixelBuffer == null || prevPixelBuffer.Length != len)
        {
            prevPixelBuffer = new Color32[len];
            hasPrevPixelFrame = false;
            bgWarmupFrames = 0;
        }

        if (topYInColumn == null || topYInColumn.Length != width)
        {
            topYInColumn = new int[width];
        }

        Array.Clear(gridSkinCount, 0, GRID_SIZE);
        Array.Clear(gridMovingCount, 0, GRID_SIZE);
        Array.Clear(gridBlobId, 0, GRID_SIZE);
        Array.Clear(curCellSumR, 0, GRID_SIZE);
        Array.Clear(curCellSumG, 0, GRID_SIZE);
        Array.Clear(curCellSumB, 0, GRID_SIZE);
        Array.Clear(curCellCount, 0, GRID_SIZE);

        long moveSumX = 0;
        long moveSumY = 0;
        int moveSkinTotal = 0;
        bool bgReady = (bgWarmupFrames >= 22);

        for (int y = scanMinY; y < scanMaxY; y += step)
        {
            int row = y * width;
            int gy = (y * GRID_H) / height;
            if (gy >= GRID_H) gy = GRID_H - 1;
            int gRow = gy * GRID_W;

            for (int x = marginX; x < width - marginX; x += step)
            {
                int idx = row + x;
                Color32 cur = pixels[idx];
                Color32 prev = prevPixelBuffer[idx];
                prevPixelBuffer[idx] = cur;

                int gx = (x * GRID_W) / width;
                if (gx >= GRID_W) gx = GRID_W - 1;
                int cell = gRow + gx;

                curCellSumR[cell] += cur.r;
                curCellSumG[cell] += cur.g;
                curCellSumB[cell] += cur.b;
                curCellCount[cell]++;

                if (!bgReady)
                    continue;

                if (IsSkinColorYCrCb(cur.r, cur.g, cur.b))
                {
                    int bdr = cur.r - bgR[cell]; if (bdr < 0) bdr = -bdr;
                    int bdg = cur.g - bgG[cell]; if (bdg < 0) bdg = -bdg;
                    int bdb = cur.b - bgB[cell]; if (bdb < 0) bdb = -bdb;
                    int bgDiff = bdr + bdg + bdb;

                    int frameDiff = 0;
                    if (hasPrevPixelFrame)
                    {
                        int dr = cur.r - prev.r; if (dr < 0) dr = -dr;
                        int dg = cur.g - prev.g; if (dg < 0) dg = -dg;
                        int db = cur.b - prev.b; if (db < 0) db = -db;
                        frameDiff = dr + dg + db;
                    }

                    bool inActiveBox = hasActiveHandBox && gx >= activeMinGx && gx <= activeMaxGx && gy >= activeMinGy && gy <= activeMaxGy;

                    if (bgDiff > 32 || (inActiveBox && frameDiff > 24))
                    {
                        gridSkinCount[cell]++;

                        if (frameDiff > 34)
                        {
                            gridMovingCount[cell]++;
                            moveSumX += x;
                            moveSumY += y;
                            moveSkinTotal++;
                        }
                    }
                }
            }
        }
        hasPrevPixelFrame = true;

        if (!bgReady)
        {
            for (int c = 0; c < GRID_SIZE; c++)
            {
                int cnt = curCellCount[c];
                if (cnt > 0)
                {
                    int ar = curCellSumR[c] / cnt;
                    int ag = curCellSumG[c] / cnt;
                    int ab = curCellSumB[c] / cnt;
                    if (bgWarmupFrames == 0)
                    {
                        bgR[c] = ar;
                        bgG[c] = ag;
                        bgB[c] = ab;
                    }
                    else
                    {
                        bgR[c] += (ar - bgR[c]) >> 1;
                        bgG[c] += (ag - bgG[c]) >> 1;
                        bgB[c] += (ab - bgB[c]) >> 1;
                    }
                }
            }
            bgWarmupFrames++;
            isHandVisible = false;
            hasActiveHandBox = false;
            currentLandmarks.confidence = 0f;
            trackedHandCenter = Vector3.zero;
            return false;
        }

        // Kiểm tra rung/di chuyển toàn khung hình hoặc thay đổi phơi sáng (auto-exposure)
        int changedBgCells = 0;
        int changedNonSkinCells = 0;
        int validBgCells = 0;
        for (int c = 0; c < GRID_SIZE; c++)
        {
            int cnt = curCellCount[c];
            if (cnt <= 0) continue;
            validBgCells++;
            int ar = curCellSumR[c] / cnt;
            int ag = curCellSumG[c] / cnt;
            int ab = curCellSumB[c] / cnt;
            int d = Mathf.Abs(ar - bgR[c]) + Mathf.Abs(ag - bgG[c]) + Mathf.Abs(ab - bgB[c]);
            if (d > 40)
            {
                changedBgCells++;
                if (!IsSkinColorYCrCb((byte)ar, (byte)ag, (byte)ab))
                {
                    changedNonSkinCells++;
                }
            }
        }

        if (validBgCells > 0 && (changedBgCells > validBgCells * 0.36f || changedNonSkinCells > validBgCells * 0.22f))
        {
            for (int c = 0; c < GRID_SIZE; c++)
            {
                int cnt = curCellCount[c];
                if (cnt <= 0) continue;
                bgR[c] += ((curCellSumR[c] / cnt) - bgR[c]) >> 1;
                bgG[c] += ((curCellSumG[c] / cnt) - bgG[c]) >> 1;
                bgB[c] += ((curCellSumB[c] / cnt) - bgB[c]) >> 1;
            }
            isHandVisible = false;
            hasActiveHandBox = false;
            currentLandmarks.confidence = 0f;
            trackedHandCenter = Vector3.zero;
            return false;
        }

        // Phân cụm 2D (8-connected BFS) trên lưới 40x30
        int blobCount = 0;
        for (int startCell = 0; startCell < GRID_SIZE; startCell++)
        {
            if (gridSkinCount[startCell] < 2 || gridBlobId[startCell] != 0)
                continue;

            int newBlobId = blobCount + 1;
            int qHead = 0, qTail = 0;
            bfsQueue[qTail++] = startCell;
            gridBlobId[startCell] = newBlobId;

            int cellCount = 0;
            int skinSum = 0;
            int movingSum = 0;
            int minGx = GRID_W, maxGx = 0, minGy = GRID_H, maxGy = 0;
            long cellWeightedGx = 0, cellWeightedGy = 0;

            while (qHead < qTail)
            {
                int c = bfsQueue[qHead++];
                int cgx = c % GRID_W;
                int cgy = c / GRID_W;
                int sc = gridSkinCount[c];

                cellCount++;
                skinSum += sc;
                movingSum += gridMovingCount[c];
                cellWeightedGx += (long)cgx * sc;
                cellWeightedGy += (long)cgy * sc;

                if (cgx < minGx) minGx = cgx;
                if (cgx > maxGx) maxGx = cgx;
                if (cgy < minGy) minGy = cgy;
                if (cgy > maxGy) maxGy = cgy;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = cgy + dy;
                    if (ny < 0 || ny >= GRID_H) continue;
                    int nRow = ny * GRID_W;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cgx + dx;
                        if (nx < 0 || nx >= GRID_W) continue;
                        int nc = nRow + nx;
                        if (gridSkinCount[nc] >= 2 && gridBlobId[nc] == 0)
                        {
                            gridBlobId[nc] = newBlobId;
                            bfsQueue[qTail++] = nc;
                        }
                    }
                }
            }

            if (cellCount >= 6 && skinSum >= 50 && blobCount < detectedBlobs.Length)
            {
                detectedBlobs[blobCount++] = new SkinBlob2D
                {
                    blobId = newBlobId,
                    cellCount = cellCount,
                    skinPixels = skinSum,
                    movingPixels = movingSum,
                    minGx = minGx,
                    maxGx = maxGx,
                    minGy = minGy,
                    maxGy = maxGy,
                    centerX = ((float)cellWeightedGx / skinSum + 0.5f) / GRID_W,
                    centerY = ((float)cellWeightedGy / skinSum + 0.5f) / GRID_H
                };
            }
        }

        // Chọn cụm 2D là bàn tay tốt nhất
        int bestIdx = -1;
        float bestScore = float.MinValue;

        for (int i = 0; i < blobCount; i++)
        {
            SkinBlob2D b = detectedBlobs[i];
            float bw = (float)(b.maxGx - b.minGx + 1) / GRID_W;
            float bh = (float)(b.maxGy - b.minGy + 1) / GRID_H;
            if (bw > 0.88f || bh > 0.90f)
                continue;

            float motionRatio = (float)b.movingPixels / Mathf.Max(1, b.skinPixels);
            float score = motionRatio * 14.0f
                        + Mathf.Min(b.movingPixels, 150) * 0.06f
                        + Mathf.Min(b.skinPixels, 300) * 0.004f;

            // Nếu đã biết vị trí khuôn mặt đứng yên và có cụm khác (bàn tay), đẩy cụm khuôn mặt xuống cuối
            if (hasKnownFace && blobCount > 1)
            {
                float faceDist = Vector2.Distance(new Vector2(b.centerX, b.centerY), knownFacePos);
                if (faceDist < 0.11f && motionRatio < 0.25f)
                {
                    score -= 50.0f;
                }
            }

            if (isHandVisible && trackedHandCenter != Vector3.zero)
            {
                float dist = Vector2.Distance(new Vector2(b.centerX, b.centerY), new Vector2(trackedHandCenter.x, trackedHandCenter.y));
                score -= dist * 1.5f;
            }
            else if (blobCount > 1)
            {
                // Nếu có 2 cụm (mặt ở giữa trên + tay ở dưới/bên), ưu tiên cụm tay
                score += Mathf.Abs(b.centerX - 0.5f) * 2.0f + (1.0f - b.centerY) * 0.6f;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        if (bestIdx >= 0)
        {
            SkinBlob2D best = detectedBlobs[bestIdx];
            int bestId = best.blobId;

            int boundMinX = Mathf.Max(marginX, (best.minGx * width) / GRID_W);
            int boundMaxX = Mathf.Min(width - marginX - 1, ((best.maxGx + 1) * width) / GRID_W);
            int boundMinY = Mathf.Max(scanMinY, (best.minGy * height) / GRID_H);
            int boundMaxY = Mathf.Min(scanMaxY - 1, ((best.maxGy + 1) * height) / GRID_H);

            for (int x = boundMinX; x <= boundMaxX; x++)
            {
                topYInColumn[x] = -1;
            }

            int exactMinX = width, exactMaxX = 0, exactMinY = height, exactMaxY = 0;
            long exactSumX = 0, exactSumY = 0, exactSkinLumSum = 0;
            int exactSkinPixels = 0;

            for (int y = boundMinY; y <= boundMaxY; y += step)
            {
                int row = y * width;
                int gy = (y * GRID_H) / height;
                if (gy >= GRID_H) gy = GRID_H - 1;
                int gRow = gy * GRID_W;

                for (int x = boundMinX; x <= boundMaxX; x += step)
                {
                    int gx = (x * GRID_W) / width;
                    if (gx >= GRID_W) gx = GRID_W - 1;
                    if (gridBlobId[gRow + gx] != bestId)
                        continue;

                    Color32 cur = pixels[row + x];
                    if (IsSkinColorYCrCb(cur.r, cur.g, cur.b))
                    {
                        exactSkinPixels++;
                        exactSumX += x;
                        exactSumY += y;
                        exactSkinLumSum += cur.r + cur.g + cur.b;
                        if (x < exactMinX) exactMinX = x;
                        if (x > exactMaxX) exactMaxX = x;
                        if (y < exactMinY) exactMinY = y;
                        if (y > exactMaxY) exactMaxY = y;
                        if (y > topYInColumn[x]) topYInColumn[x] = y;
                    }
                }
            }

            if (exactSkinPixels >= 60 && exactMaxX > exactMinX && exactMaxY > exactMinY)
            {
                float invW = 1.0f / width;
                float invH = 1.0f / height;
                float cX = (float)exactSumX / exactSkinPixels * invW;
                float cY = (float)exactSumY / exactSkinPixels * invH;
                float handW = (exactMaxX - exactMinX) * invW;
                float handH = (exactMaxY - exactMinY) * invH;

                if (handW < 0.075f || handH < 0.09f)
                {
                    hasActiveHandBox = false;
                    AdaptBackgroundGrid(3, false);
                    isHandVisible = false;
                    handLostGraceTimer = 0f;
                    currentLandmarks.confidence = 0f;
                    trackedHandCenter = Vector3.zero;
                    return false;
                }

                // Kiểm tra xem cụm mới nổi lên có thực sự là rìa mặt/cổ vừa nhúc nhích cạnh vùng da nền hay không
                int bgSkinCells = 0;
                for (int gy = best.minGy; gy <= best.maxGy; gy++)
                {
                    int gRow = gy * GRID_W;
                    for (int gx = best.minGx; gx <= best.maxGx; gx++)
                    {
                        int c = gRow + gx;
                        if (gridBlobId[c] == bestId && IsSkinColorYCrCb((byte)bgR[c], (byte)bgG[c], (byte)bgB[c]))
                        {
                            bgSkinCells++;
                        }
                    }
                }
                float bgSkinOverlap = (float)bgSkinCells / Mathf.Max(1, best.cellCount);

                int boxW = exactMaxX - exactMinX;
                int boxH = exactMaxY - exactMinY;
                int colsInBox = Mathf.Max(1, boxW / step + 1);
                int rowsInBox = Mathf.Max(1, boxH / step + 1);
                float fillRatio = Mathf.Clamp01((float)exactSkinPixels / (colsInBox * rowsInBox));

                // Kiểm tra đặc trưng khuôn mặt (2 mắt / chân mày / đường rẽ tóc tối bên trong vùng trên)
                float avgSkinSum = (float)exactSkinLumSum / exactSkinPixels;
                float darkThresh = avgSkinSum * 0.72f;
                int inMinX = exactMinX + Mathf.RoundToInt(boxW * 0.20f);
                int inMaxX = exactMinX + Mathf.RoundToInt(boxW * 0.80f);
                int inMinY = exactMinY + Mathf.RoundToInt(boxH * 0.42f);
                int inMaxY = exactMinY + Mathf.RoundToInt(boxH * 0.82f);
                int interiorTotal = 0;
                int interiorDark = 0;

                for (int y = inMinY; y <= inMaxY; y += step)
                {
                    int row = y * width;
                    for (int x = inMinX; x <= inMaxX; x += step)
                    {
                        interiorTotal++;
                        Color32 cur = pixels[row + x];
                        if (!IsSkinColorYCrCb(cur.r, cur.g, cur.b) && (cur.r + cur.g + cur.b) < darkThresh)
                        {
                            interiorDark++;
                        }
                    }
                }
                float darkFeatureRatio = (interiorTotal > 0) ? (float)interiorDark / interiorTotal : 0f;
                float bestMotionRatio = (float)best.movingPixels / Mathf.Max(1, best.skinPixels);

                if (!isHandVisible && bestMotionRatio < 0.12f)
                {
                    Vector2 curCenter2D = new Vector2(cX, cY);
                    if (Vector2.Distance(curCenter2D, idleStaticBlobPos) < 0.05f)
                        idleStaticBlobTimer += Time.deltaTime;
                    else
                    {
                        idleStaticBlobPos = curCenter2D;
                        idleStaticBlobTimer = 0f;
                    }
                }
                else
                {
                    idleStaticBlobTimer = 0f;
                }

                bool matchesKnownFace = hasKnownFace &&
                                        Vector2.Distance(new Vector2(cX, cY), knownFacePos) < 0.10f &&
                                        Mathf.Abs(handW - knownFaceW) < 0.09f &&
                                        Mathf.Abs(handH - knownFaceH) < 0.11f;
                bool looksLikeFace = (darkFeatureRatio >= 0.050f && bestMotionRatio < 0.25f && handH >= 0.14f);
                bool isFaceShiftRim = (!isHandVisible && bgSkinOverlap >= 0.40f && bestMotionRatio < 0.35f);

                if (looksLikeFace || isFaceShiftRim || (matchesKnownFace && bestMotionRatio < 0.26f && (!isHandVisible || darkFeatureRatio >= 0.04f)) || (!isHandVisible && idleStaticBlobTimer >= 0.40f))
                {
                    Vector2 faceCenter2D = new Vector2(cX, cY);
                    knownFacePos = hasKnownFace ? Vector2.Lerp(knownFacePos, faceCenter2D, 0.25f) : faceCenter2D;
                    knownFaceW = handW;
                    knownFaceH = handH;
                    hasKnownFace = true;

                    hasActiveHandBox = false;
                    AdaptBackgroundGrid(2, false);

                    isHandVisible = false;
                    handLostGraceTimer = 0f;
                    currentLandmarks.confidence = 0f;
                    trackedHandCenter = Vector3.zero;
                    return false;
                }

                // Đếm số đỉnh ngón tay tách biệt ở mép trên (chỉ trên cụm 2D của bàn tay)
                int highThresh = exactMinY + Mathf.RoundToInt(boxH * 0.65f);
                int valleyThresh = exactMinY + Mathf.RoundToInt(boxH * 0.55f);
                int fingerPeaks = 0;
                bool inPeak = false;
                for (int x = exactMinX; x <= exactMaxX; x += step)
                {
                    int ty = topYInColumn[x];
                    if (!inPeak && ty >= highThresh)
                    {
                        inPeak = true;
                        fingerPeaks++;
                    }
                    else if (inPeak && ty < valleyThresh)
                    {
                        inPeak = false;
                    }
                }

                bool wasVisible = isHandVisible;
                // Luôn dùng tâm 2D ổn định của cụm bàn tay để tránh tâm bị giật giữa rìa chuyển động và toàn bàn tay
                Vector3 instantCenter = new Vector3(cX, cY, 0.5f);

                bool movedNow = best.movingPixels >= 16 ||
                                (wasVisible && Vector2.Distance(instantCenter, trackedHandCenter) >= 0.020f);
                if (movedNow)
                {
                    lastHandMotionTime = Time.time;
                    activeHandHoldDuration = 0f;
                }
                else
                {
                    activeHandHoldDuration += 0.033f;
                }

                hasActiveHandBox = true;
                activeMinGx = Mathf.Max(0, best.minGx - 1);
                activeMaxGx = Mathf.Min(GRID_W - 1, best.maxGx + 1);
                activeMinGy = Mathf.Max(0, best.minGy - 1);
                activeMaxGy = Mathf.Min(GRID_H - 1, best.maxGy + 1);

                // Cập nhật nền phía ngoài bàn tay (nếu giữ yên quá 8s thì hòa nền dần)
                AdaptBackgroundGrid(4, activeHandHoldDuration < 8.0f);

                isHandVisible = true;
                handLostGraceTimer = 0.25f;
                currentLandmarks.confidence = 0.95f;
                currentLandmarks.handWidth = handW;
                currentLandmarks.handHeight = handH;
                currentLandmarks.fillRatio = fillRatio;
                currentLandmarks.fingerPeaks = fingerPeaks;
                currentLandmarks.isMoving = (Time.time - lastHandMotionTime) <= 2.5f;

                trackedHandCenter = (!wasVisible || trackedHandCenter == Vector3.zero)
                    ? instantCenter
                    : Vector3.Lerp(trackedHandCenter, instantCenter, 0.82f);
                currentLandmarks.centroid = trackedHandCenter;

                Vector3 wrist = new Vector3(cX, (float)exactMinY * invH, 0.5f);
                currentLandmarks.landmarks[(int)HandJoint.Wrist] = wrist;

                BuildDynamicFingers(topYInColumn, exactMinX, exactMaxX, exactMinY, exactMaxY, width, height, wrist);
                return true;
            }
        }

        // Không có bàn tay nổi bật trên nền -> cập nhật nền và tắt nhận diện tay
        hasActiveHandBox = false;
        activeHandHoldDuration = 0f;
        AdaptBackgroundGrid(3, false);

        if (handLostGraceTimer <= 0f || blobCount == 0)
        {
            isHandVisible = false;
            handLostGraceTimer = 0f;
            currentLandmarks.confidence = 0f;
            trackedHandCenter = Vector3.zero;
        }
        return false;
    }

    private void AdaptBackgroundGrid(int shift, bool skipActiveBox)
    {
        for (int c = 0; c < GRID_SIZE; c++)
        {
            int cnt = curCellCount[c];
            if (cnt <= 0) continue;

            if (skipActiveBox && hasActiveHandBox)
            {
                int gx = c % GRID_W;
                int gy = c / GRID_W;
                if (gx >= activeMinGx && gx <= activeMaxGx && gy >= activeMinGy && gy <= activeMaxGy)
                    continue;
            }

            bgR[c] += ((curCellSumR[c] / cnt) - bgR[c]) >> shift;
            bgG[c] += ((curCellSumG[c] / cnt) - bgG[c]) >> shift;
            bgB[c] += ((curCellSumB[c] / cnt) - bgB[c]) >> shift;
        }
    }

    /// <summary>
    /// Bộ lọc màu da tối ưu bằng số nguyên (YCrCb + RGB):
    /// - Loại bỏ áo đỏ/đỏ đô (r - g > 58), tường xám (r - g < 6), màu tím và vật tối.
    /// </summary>
    private bool IsSkinColorYCrCb(byte r, byte g, byte b)
    {
        if (r < 55) return false;
        int sum = r + g + b;
        if (sum < 130 || sum > 740) return false;

        int rg = r - g;
        int rb = r - b;
        if (rg < 6 || rg > 58 || rb < 8) return false;
        if (b - g > 6) return false;

        int cr = 128 + ((128 * r - 107 * g - 21 * b) >> 8);
        int cb = 128 + ((-43 * r - 85 * g + 128 * b) >> 8);

        return (cr >= 131 && cr <= 168 && cb >= 76 && cb <= 130);
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
        if (cachedStatusStyle == null)
        {
            cachedStatusStyle = new GUIStyle();
            cachedStatusStyle.richText = true;
            cachedStatusStyle.fontStyle = FontStyle.Bold;
            cachedStatusStyle.normal.textColor = Color.white;
            cachedStatusStyle.alignment = TextAnchor.MiddleCenter;
        }
        cachedStatusStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(sw * 0.015f), 11, 13);
        GUIStyle style = cachedStatusStyle;

        string handStatus = isHandVisible ? "<color=#00ff88>● ĐÃ THẤY TAY</color>" : "<color=#ff5555>○ CHƯA THẤY TAY</color>";
        float speed = GestureRecognizer.Instance != null ? GestureRecognizer.Instance.GetCurrentHandSpeed() : 0f;
        string speedColor = (speed >= 0.18f) ? "#ff3333" : (speed > 0.10f ? "#ffaa00" : "#ffffff");
        string speedText = isHandVisible ? $" | Vận tốc: <color={speedColor}>{speed:F2} m/s</color>" : "";

        string gestureText = "";
        if (GestureRecognizer.Instance != null && isHandVisible)
        {
            HandGestureType g = GestureRecognizer.Instance.GetCurrentConfirmedGesture();
            if (g == HandGestureType.Slash) gestureText = " | <color=#ff3333><b>CHÉM QUÁI!</b></color>";
            else if (g == HandGestureType.OpenPalm) gestureText = " | <color=#00e5ff><b>TƯỜNG CHẮN</b></color>";
            else if (g == HandGestureType.Grab) gestureText = " | <color=#ffaa00><b>NHẶT TIM</b></color>";
        }
        else if (!isHandVisible)
        {
            gestureText = " | Giơ tay: Khiên | Chụm: Nhặt | Vung: Chém";
        }

        string text = $"[MediaPipe AI] {handStatus}{speedText}{gestureText}";

        Vector2 textSz = style.CalcSize(new GUIContent(text));
        float bannerW = Mathf.Clamp(textSz.x + 36f, 380f, sw * 0.95f);
        float bannerH = 28;
        float bannerX = (sw - bannerW) / 2f;
        float bannerY = Screen.height - 35;

        DrawColorRect(new Rect(bannerX, bannerY, bannerW, bannerH), new Color(0.04f, 0.12f, 0.22f, 0.90f));
        GUI.Label(new Rect(bannerX, bannerY, bannerW, bannerH), text, style);
    }

    private void DrawScreenLine(Vector2 p1, Vector2 p2, Color col, float width)
    {
        Vector2 d = p2 - p1;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        float length = d.magnitude;

        GUIUtility.RotateAroundPivot(angle, p1);
        DrawColorRect(new Rect(p1.x, p1.y - width / 2f, length, width), col);
        GUIUtility.RotateAroundPivot(-angle, p1);
    }

    private void DrawCircle(Vector2 center, float radius, Color col)
    {
        DrawColorRect(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), col);
    }

    private Texture2D cachedWhiteTex;

    private void DrawColorRect(Rect rect, Color c)
    {
        if (cachedWhiteTex == null)
        {
            cachedWhiteTex = new Texture2D(1, 1);
            cachedWhiteTex.SetPixel(0, 0, Color.white);
            cachedWhiteTex.Apply();
        }
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(rect, cachedWhiteTex);
        GUI.color = prev;
    }

    public HandLandmarkResult GetCurrentLandmarks() => currentLandmarks;
    public bool IsHandVisible() => isHandVisible;
}
