using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Cầu nối thu thập hình ảnh từ Camera thật:
/// - Chạy đồng nhất trên cả MÁY TÍNH (PC / Unity Editor) và ĐIỆN THOẠI (Android / iOS).
/// - Luôn ưu tiên CAMERA TRƯỚC (Front-facing / Selfie).
/// - Chuẩn hóa hình học 3D Quad: Xoay và lật chuẩn xác theo không gian màn hình (Screen Space).
/// - Hỗ trợ nút bấm On-Screen và phím tắt (R, M, V) để người dùng tự do xoay / lật camera nếu thiết bị có cảm biến đặc thù.
/// </summary>
public class ARCameraStreamBridge : MonoBehaviour
{
    public static ARCameraStreamBridge Instance { get; private set; }

    [Header("--- THIẾT LẬP CAMERA & NỀN 3D ---")]
    [Tooltip("Bắt buộc bật Camera thực tế (trên cả PC và điện thoại)")]
    [SerializeField] private bool forceWebCamOnPC = true;

    [Tooltip("Tự động tạo Quad nền 3D phía sau quái vật để quái vật 3D đứng trước phòng thật")]
    [SerializeField] private bool create3DBackgroundQuadOnPC = true;

    [Tooltip("Lật gương ngang (Mirror Horizontal) để thuận tay như soi gương (Tay phải ở bên phải màn hình)")]
    [SerializeField] private bool mirrorHorizontalOnPC = true;

    [Tooltip("Lật dọc (Flip Vertical) nếu camera thiết bị bị lộn ngược đầu")]
    [SerializeField] private bool flipVertical = false;

    [Tooltip("Góc xoay bù trừ thêm (0, 90, 180, 270 độ)")]
    [SerializeField] private int extraRotationAngle = 0;

    [Tooltip("Khoảng cách Quad nền 3D tính từ Camera (mét, quái vật xuất hiện ở 2-4m sẽ đứng phía trước nền này)")]
    [SerializeField] private float backgroundQuadDistance = 12.0f;

    [Header("--- THAM CHIẾU AR CAMERA (FALLBACK) ---")]
    [Tooltip("Quản lý camera của AR Foundation")]
    [SerializeField] private ARCameraManager arCameraManager;

    [Header("--- CẤU HÌNH HIỆU NĂNG & ĐỘ PHÂN GIẢI ---")]
    [Tooltip("Tần suất xử lý AI (0.033s ~ 30 FPS để mượt mà nhất)")]
    [SerializeField] private float processInterval = 0.033f;

    [Tooltip("Độ phân giải khung hình AI (mặc định 320x240 ngang hoặc 240x320 dọc)")]
    [SerializeField] private int targetWidth = 320;
    [SerializeField] private int targetHeight = 240;

    // Sự kiện phát ra khung hình Texture2D sẵn sàng cho MediaPipe
    public static event Action<Texture2D> OnCameraFrameReady;
    // Sự kiện truyền trực tiếp buffer Color32 đã xoay/lật (không cấp phát rác, không upload GPU)
    public static event Action<Color32[], int, int> OnCameraPixelsReady;

    private Texture2D cameraTexture;
    private WebCamTexture pcWebCamTexture;
    private float lastProcessTime = 0f;
    private bool isUsingARCamera = false;
    private GameObject pcBackgroundQuad;
    private Material pcBackgroundMat;
    private Color32[] rawPixelsBuffer;
    private Color32[] flippedPixels;
    private Camera cachedMainCamera;
    private bool isCameraInitialized = false;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        // Đảm bảo chạy 60 FPS trên cả PC và Mobile thay vì mặc định 30 FPS trên điện thoại
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;

        // Nạp thiết lập xoay / lật camera đã lưu trên thiết bị
        extraRotationAngle = PlayerPrefs.GetInt("CamRotOffset", 0);
        mirrorHorizontalOnPC = PlayerPrefs.GetInt("CamMirror", 1) == 1;
        flipVertical = PlayerPrefs.GetInt("CamFlipV", 0) == 1;
    }

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            StartCoroutine(WaitForCameraPermissionRoutine());
            return;
        }
#endif
        InitCameraSystem();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private IEnumerator WaitForCameraPermissionRoutine()
    {
        float timer = 0f;
        while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) && timer < 5.0f)
        {
            yield return new WaitForSeconds(0.2f);
            timer += 0.2f;
        }

        InitCameraSystem();
    }
#endif

    private void InitCameraSystem()
    {
        if (isCameraInitialized) return;
        isCameraInitialized = true;

        if (arCameraManager != null)
        {
            arCameraManager.enabled = false;
        }

        ARCameraBackground bg = FindAnyObjectByType<ARCameraBackground>();
        if (bg != null)
        {
            bg.enabled = false;
        }

        ARSession arSession = FindAnyObjectByType<ARSession>();
        if (arSession != null)
        {
            arSession.enabled = false;
        }

        InitFrontWebCam();

        if (pcWebCamTexture != null && pcWebCamTexture.isPlaying)
        {
            isUsingARCamera = false;
            if (create3DBackgroundQuadOnPC)
            {
                SetupPCBackgroundQuad();
            }
            Debug.Log("<color=green><b>[ARCameraStreamBridge] ĐÃ BẬT THÀNH CÔNG CAMERA TRƯỚC VÀ KHỞI TẠO NỀN 3D!</b></color>");
            return;
        }

        if (arCameraManager != null)
        {
            arCameraManager.enabled = true;
            arCameraManager.frameReceived += OnARCameraFrameReceived;
            isUsingARCamera = true;
            Debug.Log("[ARCameraStreamBridge] Dùng luồng fallback CPU từ AR Foundation.");
        }
    }

    void OnDestroy()
    {
        if (arCameraManager != null)
        {
            arCameraManager.frameReceived -= OnARCameraFrameReceived;
        }

        if (pcWebCamTexture != null && pcWebCamTexture.isPlaying)
        {
            pcWebCamTexture.Stop();
        }

        if (cameraTexture != null)
        {
            Destroy(cameraTexture);
        }

        if (pcBackgroundQuad != null)
        {
            Destroy(pcBackgroundQuad);
        }

        if (pcBackgroundMat != null)
        {
            Destroy(pcBackgroundMat);
        }
    }

    void Update()
    {
        // Phím tắt nhanh trên máy tính để tinh chỉnh góc quay nếu cần
        if (Input.GetKeyDown(KeyCode.R))
        {
            RotateCameraClockwise();
        }
        if (Input.GetKeyDown(KeyCode.M))
        {
            ToggleMirror();
        }
        if (Input.GetKeyDown(KeyCode.V))
        {
            ToggleFlipVertical();
        }

        // Cập nhật Quad nền 3D theo góc nhìn camera
        if (pcBackgroundQuad != null)
        {
            UpdatePCBackgroundQuad();
        }

        // Xử lý khung hình từ Camera trước gửi sang AI
        if (!isUsingARCamera && pcWebCamTexture != null && pcWebCamTexture.isPlaying)
        {
            if (Time.time - lastProcessTime >= processInterval && (OnCameraPixelsReady != null || OnCameraFrameReady != null))
            {
                lastProcessTime = Time.time;

                int w = pcWebCamTexture.width;
                int h = pcWebCamTexture.height;

                if (w > 16 && h > 16)
                {
                    int tw = 320;
                    int th = 240;
                    if (Screen.height > Screen.width)
                    {
                        tw = 240;
                        th = 320;
                    }

                    int rawLen = w * h;
                    if (rawPixelsBuffer == null || rawPixelsBuffer.Length != rawLen)
                    {
                        rawPixelsBuffer = new Color32[rawLen];
                    }
                    pcWebCamTexture.GetPixels32(rawPixelsBuffer);

                    if (flippedPixels == null || flippedPixels.Length != tw * th)
                    {
                        flippedPixels = new Color32[tw * th];
                    }

                    int totalRot = (pcWebCamTexture.videoRotationAngle + extraRotationAngle) % 360;
                    if (totalRot < 0) totalRot += 360;

                    float screenSignX = mirrorHorizontalOnPC ? -1f : 1f;
                    bool shouldFlipY = pcWebCamTexture.videoVerticallyMirrored ^ flipVertical;
                    float screenSignY = shouldFlipY ? -1f : 1f;

                    float rad = totalRot * Mathf.Deg2Rad;
                    int cosTheta = Mathf.RoundToInt(Mathf.Cos(rad));
                    int sinTheta = Mathf.RoundToInt(Mathf.Sin(rad));

                    float invTw = 1.0f / tw;
                    float invTh = 1.0f / th;
                    int wMax = w - 1;
                    int hMax = h - 1;

                    for (int ty = 0; ty < th; ty++)
                    {
                        float v = (ty + 0.5f) * invTh;
                        float vm = (screenSignY < 0f) ? (1.0f - v) : v;
                        float cv = vm - 0.5f;
                        int dstRow = ty * tw;

                        for (int tx = 0; tx < tw; tx++)
                        {
                            float u = (tx + 0.5f) * invTw;
                            float um = (screenSignX < 0f) ? (1.0f - u) : u;
                            float cu = um - 0.5f;

                            float uSrc = (cu * cosTheta - cv * sinTheta) + 0.5f;
                            float vSrc = (cu * sinTheta + cv * cosTheta) + 0.5f;

                            int sx = (int)(uSrc * w);
                            if (sx < 0) sx = 0;
                            else if (sx > wMax) sx = wMax;

                            int sy = (int)(vSrc * h);
                            if (sy < 0) sy = 0;
                            else if (sy > hMax) sy = hMax;

                            flippedPixels[dstRow + tx] = rawPixelsBuffer[sy * w + sx];
                        }
                    }

                    OnCameraPixelsReady?.Invoke(flippedPixels, tw, th);

                    if (OnCameraFrameReady != null)
                    {
                        if (cameraTexture == null || cameraTexture.width != tw || cameraTexture.height != th)
                        {
                            cameraTexture = new Texture2D(tw, th, TextureFormat.RGBA32, false);
                        }
                        cameraTexture.SetPixels32(flippedPixels);
                        cameraTexture.Apply(false);
                        OnCameraFrameReady.Invoke(cameraTexture);
                    }
                }
            }
        }
    }

    private void SetupPCBackgroundQuad()
    {
        if (cachedMainCamera == null)
        {
            cachedMainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        }
        Camera cam = cachedMainCamera;

        if (cam == null)
        {
            Debug.LogWarning("[ARCameraStreamBridge] Chưa tìm thấy Camera để gắn Quad nền!");
            return;
        }

        if (pcBackgroundQuad == null)
        {
            pcBackgroundQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pcBackgroundQuad.name = "FrontCamera_Background_Quad";
            pcBackgroundQuad.transform.SetParent(cam.transform, false);

            Collider col = pcBackgroundQuad.GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
                unlitShader = Shader.Find("Unlit/Texture");
            if (unlitShader == null)
                unlitShader = Shader.Find("Universal Render Pipeline/Lit");
            if (unlitShader == null)
                unlitShader = Shader.Find("Sprites/Default");

            pcBackgroundMat = new Material(unlitShader);
            if (pcBackgroundMat.HasProperty("_BaseMap"))
                pcBackgroundMat.SetTexture("_BaseMap", pcWebCamTexture);
            if (pcBackgroundMat.HasProperty("_MainTex"))
                pcBackgroundMat.SetTexture("_MainTex", pcWebCamTexture);

            // Đặt ma trận UV chuẩn 1:1, toàn bộ phép lật/xoay xử lý qua Transform
            if (pcBackgroundMat.HasProperty("_BaseMap"))
            {
                pcBackgroundMat.SetTextureScale("_BaseMap", Vector2.one);
                pcBackgroundMat.SetTextureOffset("_BaseMap", Vector2.zero);
            }
            if (pcBackgroundMat.HasProperty("_MainTex"))
            {
                pcBackgroundMat.SetTextureScale("_MainTex", Vector2.one);
                pcBackgroundMat.SetTextureOffset("_MainTex", Vector2.zero);
            }

            MeshRenderer mr = pcBackgroundQuad.GetComponent<MeshRenderer>();
            mr.material = pcBackgroundMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        UpdatePCBackgroundQuad();
    }

    /// <summary>
    /// Đồng bộ kích thước và góc quay Quad nền khớp chính xác góc nhìn (FOV) và tỷ lệ màn hình của Camera.
    /// Khắc phục 100% tình trạng lộn ngược đầu hoặc nằm ngang trên thiết bị di động.
    /// </summary>
    private void UpdatePCBackgroundQuad()
    {
        if (cachedMainCamera == null)
        {
            cachedMainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        }
        Camera cam = cachedMainCamera;

        if (pcBackgroundQuad == null || cam == null || pcWebCamTexture == null)
            return;

        float distance = backgroundQuadDistance;
        float height = 2.0f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
        float width = height * cam.aspect;

        pcBackgroundQuad.transform.localPosition = new Vector3(0, 0, distance);

        int totalRot = (pcWebCamTexture.videoRotationAngle + extraRotationAngle) % 360;
        if (totalRot < 0) totalRot += 360;

        bool is90or270 = (totalRot == 90 || totalRot == 270);

        // 1. Lật ngang (Mirror Horizontal): Người chơi giơ tay phải thật thì hình hiển thị bên phải
        float screenSignX = mirrorHorizontalOnPC ? -1f : 1f;

        // 2. Lật dọc (Vertical Flip): Xử lý phần cứng camera điện thoại không bị ngược đầu
        bool shouldFlipY = pcWebCamTexture.videoVerticallyMirrored ^ flipVertical;
        float screenSignY = shouldFlipY ? -1f : 1f;

        // Xoay Quad theo góc cảm biến
        pcBackgroundQuad.transform.localRotation = Quaternion.Euler(0, 0, -totalRot);

        // Scale theo đúng trục sau khi xoay để tránh tình trạng lật nhầm trục X thành trục Y
        if (is90or270)
        {
            pcBackgroundQuad.transform.localScale = new Vector3(screenSignY * height, screenSignX * width, 1.0f);
        }
        else
        {
            pcBackgroundQuad.transform.localScale = new Vector3(screenSignX * width, screenSignY * height, 1.0f);
        }
    }

    private void InitFrontWebCam()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        Debug.Log($"<color=cyan>[ARCameraStreamBridge] Đang dò tìm Camera... Số lượng thiết bị tìm thấy: {devices.Length}</color>");

        if (devices.Length > 0)
        {
            string chosenDevice = devices[0].name;
            bool foundFront = false;

            for (int i = 0; i < devices.Length; i++)
            {
                Debug.Log($"[ARCameraStreamBridge] Camera #{i}: {devices[i].name} (isFrontFacing: {devices[i].isFrontFacing})");
                if (devices[i].isFrontFacing && !foundFront)
                {
                    chosenDevice = devices[i].name;
                    foundFront = true;
                }
            }

            int reqW = 640;
            int reqH = 480;
            if (Screen.height > Screen.width)
            {
                reqW = 480;
                reqH = 640;
            }

            pcWebCamTexture = new WebCamTexture(chosenDevice, reqW, reqH, 30);
            pcWebCamTexture.Play();
            Debug.Log($"<color=green>[ARCameraStreamBridge] Đang phát Camera: {chosenDevice} (Camera trước: {foundFront})</color>");
        }
        else
        {
            Debug.LogError("<color=red>[ARCameraStreamBridge] Không tìm thấy Camera nào! Vui lòng kiểm tra quyền Camera trong cài đặt thiết bị.</color>");
        }
    }

    public void RotateCameraClockwise()
    {
        extraRotationAngle = (extraRotationAngle + 90) % 360;
        PlayerPrefs.SetInt("CamRotOffset", extraRotationAngle);
        PlayerPrefs.Save();
        Debug.Log($"[ARCameraStreamBridge] Đã xoay Camera bù thêm: {extraRotationAngle}°");
    }

    public void ToggleMirror()
    {
        mirrorHorizontalOnPC = !mirrorHorizontalOnPC;
        PlayerPrefs.SetInt("CamMirror", mirrorHorizontalOnPC ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[ARCameraStreamBridge] Lật gương ngang: {mirrorHorizontalOnPC}");
    }

    public void ToggleFlipVertical()
    {
        flipVertical = !flipVertical;
        PlayerPrefs.SetInt("CamFlipV", flipVertical ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[ARCameraStreamBridge] Lật dọc: {flipVertical}");
    }

    private void OnARCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if (Time.time - lastProcessTime < processInterval)
            return;

        lastProcessTime = Time.time;

        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
            return;

        using (cpuImage)
        {
            int tw = (Screen.height > Screen.width) ? 240 : 320;
            int th = (Screen.height > Screen.width) ? 320 : 240;

            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = new Vector2Int(tw, th),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            if (cameraTexture == null || cameraTexture.width != tw || cameraTexture.height != th)
            {
                cameraTexture = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            }

            var rawTextureData = cameraTexture.GetRawTextureData<byte>();
            cpuImage.Convert(conversionParams, rawTextureData);

            if (OnCameraPixelsReady != null)
            {
                if (flippedPixels == null || flippedPixels.Length != tw * th)
                {
                    flippedPixels = new Color32[tw * th];
                }
                cameraTexture.GetRawTextureData<Color32>().CopyTo(flippedPixels);
                OnCameraPixelsReady.Invoke(flippedPixels, tw, th);
            }

            if (OnCameraFrameReady != null)
            {
                cameraTexture.Apply(false);
                OnCameraFrameReady.Invoke(cameraTexture);
            }
        }
    }

    public WebCamTexture GetWebCamTexture() => pcWebCamTexture;
    public bool IsWebCamActive() => pcWebCamTexture != null && pcWebCamTexture.isPlaying;
}
