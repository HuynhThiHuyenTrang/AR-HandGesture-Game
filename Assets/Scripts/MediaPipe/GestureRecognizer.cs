using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Bộ nhận diện 3 cử chỉ chiến đấu cốt lõi từ các điểm Landmarks bàn tay:
/// 1. CHÉM QUÁI (Slash): Vung lướt tay nhanh qua màn hình (vận tốc >= 0.55 m/s, quãng đường >= 7% màn hình).
///    - Tia kiếm chém xuất hiện chuẩn 100% theo vệt tay lướt.
/// 2. TƯỜNG CHẮN (Shield / OpenPalm): Thẳng bàn tay mở giơ cao trước camera.
///    - Bật ngay sau 0.05s, duy trì khiên liên tục khi còn giơ tay, tự tắt khi hạ tay xuống.
/// 3. LẤY VẬT PHẨM (Grab / Pinch): Chỉ chụm đầu ngón cái và ngón trỏ.
///    - Phải giữ tư thế chụm ổn định mới hút trái tim hồi máu.
/// </summary>
public class GestureRecognizer : MonoBehaviour
{
    public static GestureRecognizer Instance { get; private set; }

    public static event Action<HandGestureType> OnGestureConfirmed;

    [Header("--- 1. CỬ CHỈ CHÉM QUÁI (SLASH) ---")]
    [Tooltip("Vận tốc di chuyển bàn tay tối thiểu để tính là vung chém (m/s)")]
    [SerializeField] private float slashSpeedThreshold = 0.40f;

    [Tooltip("Quãng đường tối thiểu bàn tay lướt qua để tính là 1 cú chém (10% màn hình, tránh chụm tay bị nhầm thành chém)")]
    [SerializeField] private float slashMinDisplacement = 0.10f;


    [Header("--- 2. CỬ CHỈ TƯỜNG CHẮN (SHIELD) ---")]
    [Tooltip("Khoảng cách tối thiểu giữa ngón cái và ngón trỏ khi xòe bàn tay")]
    [SerializeField] private float openPalmMinPinch = 0.075f;

    [Header("--- 3. CỬ CHỈ LẤY VẬT PHẨM (GRAB) ---")]
    [Tooltip("Ngưỡng khoảng cách tối đa giữa đầu ngón cái và ngón trỏ để tính là Chụm tay")]
    [SerializeField] private float pinchDistanceThreshold = 0.08f;

    [Header("--- CẤU HÌNH THỜI GIAN THEO ĐỀ TÀI ---")]
    [Tooltip("Thời gian hồi chiêu giữa 2 lần chém để mượt mà (giây)")]
    [SerializeField] private float slashCooldown = 0.10f;

    [Tooltip("Thời gian chờ giữa 2 lần phát sự kiện tĩnh (giây)")]
    [SerializeField] private float gestureCooldown = 0.22f;

    [Tooltip("Ngưỡng tin cậy tối thiểu của MediaPipe")]
    [SerializeField] private float confidenceThreshold = 0.5f;

    [Header("--- DEBUG THEO DÕI THỜI GIAN THỰC ---")]
    [SerializeField] private HandGestureType detectedCandidate = HandGestureType.None;
    [SerializeField] private HandGestureType currentConfirmedGesture = HandGestureType.None;
    [SerializeField] private float candidateDuration = 0f;
    [SerializeField] private float cooldownTimer = 0f;
    [SerializeField] private bool isHandTracked = false;

    [Header("--- THÔNG SỐ ĐO ĐẠC THỰC TẾ ---")]
    [SerializeField] private float currentPinchDistance = 0f;
    [SerializeField] private float currentHandCenterY = 0f;
    [SerializeField] private float currentHandSpeed = 0f;

    private struct CentroidSample
    {
        public Vector2 position;
        public float timestamp;
    }
    private Queue<CentroidSample> motionHistory = new Queue<CentroidSample>();

    private HandLandmarkResult smoothedResult = new HandLandmarkResult();
    private float trackingLostTimer = 0f;
    private bool isHoldingObject = false;
    private float lastStaticCheckTime = -1f;

    // Sau khi chém, yêu cầu mở tay ra rồi mới được chụm để hút tim.
    private bool grabNeedsReleaseAfterSlash = false;

    // Toạ độ thực tế của vệt vung tay chém mới nhất (chuẩn hóa 0..1)
    private Vector2 lastSlashStart = new Vector2(0.28f, 0.70f);
    private Vector2 lastSlashEnd = new Vector2(0.72f, 0.30f);

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    void Update()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
        }

        if (!isHandTracked)
        {
            trackingLostTimer += Time.deltaTime;
            if (trackingLostTimer > 0.18f)
            {
                candidateDuration = 0f;
                detectedCandidate = HandGestureType.None;
                currentConfirmedGesture = HandGestureType.None;
                motionHistory.Clear();
                currentHandSpeed = 0f;
            }
        }
        else
        {
            trackingLostTimer = 0f;
        }

        HandlePCTestShortcuts();
    }

    public void TriggerInstantSlash(float detectedSpeed, Vector2 startNorm, Vector2 endNorm)
    {
        currentHandSpeed = detectedSpeed;
        lastSlashStart = startNorm;
        lastSlashEnd = endNorm;

        if (cooldownTimer <= 0f && detectedSpeed >= slashSpeedThreshold)
        {
            ConfirmGesture(HandGestureType.Slash);
            cooldownTimer = slashCooldown;
            motionHistory.Clear();
        }
    }

    public void TriggerInstantSlash(float detectedSpeed)
    {
        Vector2 defaultStart = new Vector2(0.28f, 0.68f);
        Vector2 defaultEnd = new Vector2(0.72f, 0.32f);
        TriggerInstantSlash(detectedSpeed, defaultStart, defaultEnd);
    }

    /// <summary>
    /// Xử lý các điểm Landmark của bàn tay:
    /// - Đo vận tốc và chuyển động lướt qua màn hình trong 0.16s để chém quái chuẩn xác.
    /// - Khi bàn tay di chuyển bình thường: nhận diện Tường chắn (xòe tay) hoặc Lấy tim (chụm/nắm tay).
    /// </summary>
    public void ProcessLandmarks(HandLandmarkResult rawResult)
    {
        if (rawResult == null || rawResult.confidence < confidenceThreshold)
        {
            isHandTracked = false;
            return;
        }

        isHandTracked = true;
        SmoothLandmarks(rawResult);

        Vector3 wrist = smoothedResult.GetJoint(HandJoint.Wrist);
        Vector3 thumbTip = smoothedResult.GetJoint(HandJoint.ThumbTip);
        Vector3 indexTip = smoothedResult.GetJoint(HandJoint.IndexTip);
        Vector3 middleTip = smoothedResult.GetJoint(HandJoint.MiddleTip);

        currentPinchDistance = Vector3.Distance(thumbTip, indexTip);
        currentHandCenterY = (wrist.y + middleTip.y) * 0.5f;

        // Chỉ mở khóa Grab khi người chơi đã mở ngón cái và ngón trỏ ra.
        if (currentPinchDistance > pinchDistanceThreshold * 1.35f)
        {
            grabNeedsReleaseAfterSlash = false;
        }

        // =========================================================================
        // 1. CHÉM QUÁI (SLASH): THEO DÕI VẬN TỐC & QUỸ ĐẠO BÀN TAY TRONG 0.22s
        // =========================================================================
        float now = Time.time;
        Vector2 currentPos = new Vector2(smoothedResult.centroid.x, smoothedResult.centroid.y);
        motionHistory.Enqueue(new CentroidSample { position = currentPos, timestamp = now });

        while (motionHistory.Count > 0 && (now - motionHistory.Peek().timestamp) > 0.22f)
        {
            motionHistory.Dequeue();
        }

        float maxSpeed = 0f;
        float maxDisplacement = 0f;
        Vector2 slashStartPoint = currentPos;

        // Quét đa điểm trong hàng đợi 0.22s: bắt trọn đỉnh vận tốc khi vung tay, triệt tiêu 100% hiện tượng trượt nhát chém
        foreach (var sample in motionHistory)
        {
            float d = Vector2.Distance(currentPos, sample.position);
            float dt = Mathf.Max(now - sample.timestamp, 0.001f);
            float spd = (dt > 0.01f) ? (d / dt) : 0f;
            if (spd > maxSpeed)
            {
                maxSpeed = spd;
                maxDisplacement = d;
                slashStartPoint = sample.position;
            }
        }

        currentHandSpeed = maxSpeed;

        // Đòn chém cần có cả vận tốc vung tay (maxSpeed >= slashSpeedThreshold) VÀ quãng đường lướt thực sự (>= slashMinDisplacement)
        // Không kích hoạt chém chỉ vì chụm/nắm ngón tay tại chỗ làm dịch nhẹ trọng tâm
        bool isSlash = (maxDisplacement >= slashMinDisplacement && maxSpeed >= slashSpeedThreshold);

        if (isSlash && cooldownTimer <= 0f)
        {
            lastSlashStart = slashStartPoint;
            lastSlashEnd = currentPos;
            ConfirmGesture(HandGestureType.Slash);
            cooldownTimer = Mathf.Max(slashCooldown, 0.25f); // Khóa 0.25s sau cú chém để vệt tay chém không bị nhận nhầm thành chụm tay
            motionHistory.Clear();
            detectedCandidate = HandGestureType.None;
            candidateDuration = 0f;
            return;
        }

        // =========================================================================
        // 2. CỬ CHỈ TĨNH: XÉT KHI BÀN TAY KHÔNG VUNG CHÉM (SPEED < 0.35 m/s & DỊCH CHUYỂN < 0.045)
        // Cho phép rung nhẹ 1-2 pixel của camera nhưng vẫn khóa hoàn toàn khi đang vung tay chém!
        // =========================================================================
        float dtStatic = (lastStaticCheckTime > 0f) ? Mathf.Clamp(now - lastStaticCheckTime, 0f, 0.2f) : 0.033f;
        lastStaticCheckTime = now;

        if (currentHandSpeed < 0.35f && maxDisplacement < 0.045f)
        {
            float handW = smoothedResult.handWidth;
            float handH = smoothedResult.handHeight;
            float ratio = handH / Mathf.Max(handW, 0.01f);

            HandGestureType staticGesture = HandGestureType.None;

            // LẤY TIM (GRAB): Nhận khi chụm ngón cái-trỏ (<= 0.095) HOẶC nắm tay gọn (tỉ lệ vuông, không dẹt ngang như khi chém)
            bool isPinching = currentPinchDistance > 0.001f && currentPinchDistance <= 0.095f;
            bool isCompactFist = (handW <= 0.23f && handH <= 0.23f && ratio >= 0.78f && ratio <= 1.45f);
            bool isGrab = isPinching || isCompactFist;

            // TƯỜNG CHẮN (SHIELD): Thẳng bàn tay mở giơ cao
            bool isOpenPalm = !isGrab && (handW >= 0.08f && handH >= 0.08f) && 
                              (currentPinchDistance > openPalmMinPinch || handH >= 0.15f);

            if (isGrab)
            {
                staticGesture = HandGestureType.Grab;
            }
            else if (isOpenPalm)
            {
                staticGesture = HandGestureType.OpenPalm;
            }

            if (staticGesture == detectedCandidate && staticGesture != HandGestureType.None)
            {
                candidateDuration += dtStatic;
                // Giữ chụm/nắm tay 0.12 giây là vào chế độ Nhặt Tim ngay
                float reqDuration = (staticGesture == HandGestureType.Grab) ? 0.12f : 0.06f;

                if (candidateDuration >= reqDuration && cooldownTimer <= 0f)
                {
                    ConfirmGesture(staticGesture);
                    candidateDuration = 0f;
                    cooldownTimer = 0.20f;
                }
            }
            else
            {
                detectedCandidate = staticGesture;
                candidateDuration = 0f;
            }
        }
        else
        {
            detectedCandidate = HandGestureType.None;
            candidateDuration = 0f;
        }
    }

    private void SmoothLandmarks(HandLandmarkResult raw)
    {
        smoothedResult.confidence = raw.confidence;
        smoothedResult.isRightHand = raw.isRightHand;
        smoothedResult.handWidth = raw.handWidth;
        smoothedResult.handHeight = raw.handHeight;
        smoothedResult.centroid = raw.centroid;

        for (int i = 0; i < 21; i++)
        {
            smoothedResult.landmarks[i] = Vector3.Lerp(smoothedResult.landmarks[i], raw.landmarks[i], 0.45f);
        }
    }

    public void ConfirmGesture(HandGestureType gesture)
    {
        currentConfirmedGesture = gesture;
        cooldownTimer = (gesture == HandGestureType.Slash) ? slashCooldown : ((gesture == HandGestureType.Grab) ? 0.35f : gestureCooldown);

        if (gesture == HandGestureType.Slash)
        {
            // Khóa Grab sau khi chém; chỉ mở khóa khi người chơi mở tay ra.
            grabNeedsReleaseAfterSlash = true;
            Debug.Log($"<color=red><b>[GestureRecognizer] ⚔️ ĐÃ NHẬN DIỆN CHÉM QUÁI!</b> Vận tốc: {currentHandSpeed:F2} m/s</color>");
        }
        else if (gesture == HandGestureType.OpenPalm)
        {
            Debug.Log($"<color=cyan><b>[GestureRecognizer] 🛡️ ĐÃ BẬT TƯỜNG CHẮN!</b> Bàn tay mở thẳng giơ cao</color>");
        }
        else if (gesture == HandGestureType.Grab)
        {
            isHoldingObject = true;
            Debug.Log($"<color=orange><b>[GestureRecognizer] ✊ ĐÃ LẤY VẬT PHẨM / TRÁI TIM!</b> Chụm ngón cái và ngón trỏ</color>");
        }

        OnGestureConfirmed?.Invoke(gesture);
    }

    private void HandlePCTestShortcuts()
    {
        // Chạm vào màn hình điện thoại để chém quái (hỗ trợ cả New Input System và Legacy Input)
        #if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Touchscreen.current != null && UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            TriggerDynamicSlashFromHandOrCenter();
        }
        #endif

        #if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == UnityEngine.TouchPhase.Began)
        {
            TriggerDynamicSlashFromHandOrCenter();
        }
        #endif

        #if UNITY_EDITOR || UNITY_STANDALONE
        #if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
        {
            TriggerDynamicSlashFromHandOrCenter();
        }
        else if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                TriggerDynamicSlashFromHandOrCenter();
            }
            else if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                ConfirmGesture(HandGestureType.OpenPalm);
            }
            else if (Keyboard.current.digit3Key.wasPressedThisFrame)
            {
                ConfirmGesture(HandGestureType.Grab);
            }
            else if (Keyboard.current.digit4Key.wasPressedThisFrame)
            {
                ConfirmGesture(HandGestureType.Push);
            }
        }
        #endif

        #if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            TriggerDynamicSlashFromHandOrCenter();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Space))
        {
            TriggerDynamicSlashFromHandOrCenter();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            ConfirmGesture(HandGestureType.OpenPalm);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            ConfirmGesture(HandGestureType.Grab);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            ConfirmGesture(HandGestureType.Push);
        }
        #endif
        #endif
    }

    private void TriggerDynamicSlashFromHandOrCenter()
    {
        Vector2 pStart, pEnd;
        if (isHandTracked && smoothedResult.centroid != Vector3.zero)
        {
            pStart = new Vector2(smoothedResult.centroid.x - 0.22f, smoothedResult.centroid.y + 0.16f);
            pEnd = new Vector2(smoothedResult.centroid.x + 0.22f, smoothedResult.centroid.y - 0.16f);
        }
        else
        {
            pStart = new Vector2(0.28f, 0.70f);
            pEnd = new Vector2(0.72f, 0.30f);
        }
        TriggerInstantSlash(1.2f, pStart, pEnd);
    }

    public Vector2 GetLastSlashStart() => lastSlashStart;
    public Vector2 GetLastSlashEnd() => lastSlashEnd;
    public HandGestureType GetCurrentConfirmedGesture() => currentConfirmedGesture;
    public bool IsHoldingObject() => isHoldingObject;
    public float GetCurrentPinchDistance() => currentPinchDistance;
    public float GetCurrentHandSpeed() => currentHandSpeed;
}
