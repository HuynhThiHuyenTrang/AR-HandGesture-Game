using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Cầu nối thu thập hình ảnh từ Camera thật:
/// - Trên PC / Unity Editor: Tự động kích hoạt Webcam thực tế của máy tính để quay cử chỉ bàn tay thật.
/// - Trên Android (Mobile): Lấy luồng ảnh CPU từ AR Foundation ARCore để tránh xung đột phần cứng camera.
/// </summary>
public class ARCameraStreamBridge : MonoBehaviour
{
    public static ARCameraStreamBridge Instance { get; private set; }

    [Header("--- THIẾT LẬP MÁY TÍNH (PC WEBCAM) ---")]
    [Tooltip("Bắt buộc bật Webcam thật của máy tính khi chạy trên Unity Editor để quay bàn tay thật của bạn")]
    [SerializeField] private bool forceWebCamOnPC = true;

    [Tooltip("Tự động tạo Quad nền 3D phía sau quái vật trên PC để quái vật 3D đứng trước phòng thật")]
    [SerializeField] private bool create3DBackgroundQuadOnPC = true;

    [Tooltip("Khoảng cách Quad nền 3D tính từ Camera (mét, quái vật xuất hiện ở 2-4m sẽ đứng phía trước nền này)")]
    [SerializeField] private float backgroundQuadDistance = 12.0f;

    [Header("--- THAM CHIẾU AR CAMERA (MOBILE) ---")]
    [Tooltip("Quản lý camera của AR Foundation (dành cho Android)")]
    [SerializeField] private ARCameraManager arCameraManager;

    [Header("--- CẤU HÌNH HIỆU NĂNG & ĐỘ PHÂN GIẢI ---")]
    [Tooltip("Tần suất xử lý AI (0.033s ~ 30 FPS để mượt mà nhất)")]
    [SerializeField] private float processInterval = 0.033f;

    [Tooltip("Độ phân giải khung hình AI (320 x 240 pixel để xử lý cực nhanh, không lag)")]
    [SerializeField] private int targetWidth = 320;
    [SerializeField] private int targetHeight = 240;

    // Sự kiện phát ra khung hình Texture2D sẵn sàng cho MediaPipe
    public static event Action<Texture2D> OnCameraFrameReady;

    private Texture2D cameraTexture;
    private WebCamTexture pcWebCamTexture;
    private float lastProcessTime = 0f;
    private bool isUsingARCamera = false;
    private GameObject pcBackgroundQuad;
    private Material pcBackgroundMat;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    void Start()
    {
        // 1. NẾU CHẠY TRÊN MÁY TÍNH (UNITY EDITOR / PC STANDALONE):
        // Ưu tiên bật ngay WEBCAM THẬT của máy tính để người chơi quay bàn tay thật của mình!
        #if UNITY_EDITOR || UNITY_STANDALONE
        if (forceWebCamOnPC)
        {
            InitPCWebCam();
            if (pcWebCamTexture != null && pcWebCamTexture.isPlaying)
            {
                isUsingARCamera = false;
                if (create3DBackgroundQuadOnPC)
                {
                    SetupPCBackgroundQuad();
                }
                Debug.Log("<color=green><b>[ARCameraStreamBridge] ĐÃ BẬT THÀNH CÔNG WEBCAM THẬT CỦA MÁY TÍNH VÀ KHỞI TẠO NỀN 3D!</b></color>");
                return;
            }
        }
        #endif

        // 2. KHI BUILD RA ĐIỆN THOẠI ANDROID THẬT:
        // Sử dụng luồng hình ảnh CPU của AR Foundation để tránh xung đột camera với ARCore
        if (arCameraManager != null)
        {
            arCameraManager.frameReceived += OnARCameraFrameReceived;
            isUsingARCamera = true;
            Debug.Log("[ARCameraStreamBridge] Đang sử dụng luồng hình ảnh CPU từ AR Foundation trên thiết bị di động.");
        }
        else
        {
            Debug.LogWarning("[ARCameraStreamBridge] Chưa tìm thấy Webcam PC hoặc ARCameraManager!");
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
        // Cập nhật Quad nền 3D theo góc nhìn camera
        if (pcBackgroundQuad != null)
        {
            UpdatePCBackgroundQuad();
        }

        // Xử lý khung hình từ Webcam thật trên máy tính
        if (!isUsingARCamera && pcWebCamTexture != null && pcWebCamTexture.isPlaying)
        {
            if (Time.time - lastProcessTime >= processInterval)
            {
                lastProcessTime = Time.time;

                // Khởi tạo Texture2D nếu kích thước thay đổi
                if (cameraTexture == null || cameraTexture.width != pcWebCamTexture.width || cameraTexture.height != pcWebCamTexture.height)
                {
                    cameraTexture = new Texture2D(pcWebCamTexture.width, pcWebCamTexture.height, TextureFormat.RGBA32, false);
                }

                // Sao chép hình ảnh từ Webcam vào Texture2D
                cameraTexture.SetPixels32(pcWebCamTexture.GetPixels32());
                cameraTexture.Apply();

                // Phát sự kiện khung hình mới cho MediaPipeHandTracker
                OnCameraFrameReady?.Invoke(cameraTexture);
            }
        }
    }

    /// <summary>
    /// Tạo một tấm Quad 3D gắn vào Camera để làm nền hiển thị Webcam phòng thật
    /// Nhờ đặt ở khoảng cách 12m, mọi quái vật 3D (ở 2-4m) sẽ đứng NỔI BẬT PHÍA TRƯỚC hình ảnh thật!
    /// </summary>
    private void SetupPCBackgroundQuad()
    {
        Camera cam = Camera.main;
        if (cam == null)
            cam = FindAnyObjectByType<Camera>();

        if (cam == null)
        {
            Debug.LogWarning("[ARCameraStreamBridge] Chưa tìm thấy Camera để gắn Quad nền!");
            return;
        }

        if (pcBackgroundQuad == null)
        {
            pcBackgroundQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pcBackgroundQuad.name = "PC_WebCam_Background_Quad";
            pcBackgroundQuad.transform.SetParent(cam.transform, false);

            // Xóa Collider để không chặn các tia Raycast tấn công quái vật
            Collider col = pcBackgroundQuad.GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            // Tìm Shader Unlit của Universal Render Pipeline (URP)
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
                unlitShader = Shader.Find("Unlit/Texture");
            if (unlitShader == null)
                unlitShader = Shader.Find("Universal Render Pipeline/Lit");

            pcBackgroundMat = new Material(unlitShader);
            if (pcBackgroundMat.HasProperty("_BaseMap"))
                pcBackgroundMat.SetTexture("_BaseMap", pcWebCamTexture);
            if (pcBackgroundMat.HasProperty("_MainTex"))
                pcBackgroundMat.SetTexture("_MainTex", pcWebCamTexture);

            // Tự động xử lý lật ảnh dọc nếu thiết bị yêu cầu
            if (pcWebCamTexture != null && pcWebCamTexture.videoVerticallyMirrored)
            {
                if (pcBackgroundMat.HasProperty("_BaseMap"))
                    pcBackgroundMat.SetTextureScale("_BaseMap", new Vector2(1, -1));
                if (pcBackgroundMat.HasProperty("_MainTex"))
                    pcBackgroundMat.SetTextureScale("_MainTex", new Vector2(1, -1));
            }

            MeshRenderer mr = pcBackgroundQuad.GetComponent<MeshRenderer>();
            mr.material = pcBackgroundMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        UpdatePCBackgroundQuad();
        Debug.Log("<color=cyan><b>[ARCameraStreamBridge] Đã thiết lập thành công Quad nền 3D chiếu Webcam phòng thật!</b></color>");
    }

    /// <summary>
    /// Đồng bộ kích thước Quad nền khớp chính xác góc nhìn (FOV) và tỷ lệ màn hình của Camera
    /// </summary>
    private void UpdatePCBackgroundQuad()
    {
        Camera cam = Camera.main;
        if (cam == null)
            cam = FindAnyObjectByType<Camera>();

        if (pcBackgroundQuad == null || cam == null)
            return;

        float distance = backgroundQuadDistance;
        float height = 2.0f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
        float width = height * cam.aspect;

        pcBackgroundQuad.transform.localPosition = new Vector3(0, 0, distance);
        pcBackgroundQuad.transform.localRotation = Quaternion.identity;
        pcBackgroundQuad.transform.localScale = new Vector3(width, height, 1.0f);
    }

    /// <summary>
    /// Xử lý từng frame từ camera AR Foundation trên điện thoại di động
    /// </summary>
    private void OnARCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if (Time.time - lastProcessTime < processInterval)
            return;

        lastProcessTime = Time.time;

        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
            return;

        using (cpuImage)
        {
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = new Vector2Int(targetWidth, targetHeight),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            if (cameraTexture == null || cameraTexture.width != targetWidth || cameraTexture.height != targetHeight)
            {
                cameraTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            }

            var rawTextureData = cameraTexture.GetRawTextureData<byte>();
            cpuImage.Convert(conversionParams, rawTextureData);
            cameraTexture.Apply();

            OnCameraFrameReady?.Invoke(cameraTexture);
        }
    }

    /// <summary>
    /// Khởi động Webcam thực tế trên máy tính PC/Laptop
    /// </summary>
    private void InitPCWebCam()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        Debug.Log($"<color=cyan>[ARCameraStreamBridge] Đang dò tìm Webcam... Số lượng thiết bị tìm thấy: {devices.Length}</color>");

        if (devices.Length > 0)
        {
            for (int i = 0; i < devices.Length; i++)
            {
                Debug.Log($"[ARCameraStreamBridge] Webcam #{i}: {devices[i].name}");
            }

            // Chọn webcam đầu tiên và phát video trực tiếp
            pcWebCamTexture = new WebCamTexture(devices[0].name, targetWidth, targetHeight, 30);
            pcWebCamTexture.Play();
            Debug.Log($"<color=green>[ARCameraStreamBridge] Đang bật Webcam: {devices[0].name} ({targetWidth}x{targetHeight} @30fps)</color>");
        }
        else
        {
            Debug.LogError("<color=red>[ARCameraStreamBridge] Không tìm thấy Webcam nào trên máy tính! Vui lòng kiểm tra quyền Camera trong Windows Settings (Cài đặt Windows -> Quyền riêng tư -> Camera).</color>");
        }
    }

    /// <summary>
    /// Lấy trực tiếp luồng WebCamTexture đang phát để hiển thị lên màn hình
    /// </summary>
    public WebCamTexture GetWebCamTexture() => pcWebCamTexture;

    public bool IsWebCamActive() => pcWebCamTexture != null && pcWebCamTexture.isPlaying;
}
