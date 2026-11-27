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
    [SerializeField] private float slashSpeedThreshold = 0.48f;

    [Tooltip("Quãng đường tối thiểu bàn tay lướt qua để tính là 1 cú chém")]
    [SerializeField] private float slashMinDisplacement = 0.14f;


    [Header("--- 2. CỬ CHỈ TƯỜNG CHẮN (SHIELD) ---")]
    [Tooltip("Khoảng cách tối thiểu giữa ngón cái và ngón trỏ khi xòe bàn tay")]
    [SerializeField] private float openPalmMinPinch = 0.075f;

    [Header("--- 3. CỬ CHỈ LẤY VẬT PHẨM (GRAB) ---")]
    [Tooltip("Ngưỡng khoảng cách tối đa giữa đầu ngón cái và ngón trỏ để tính là Chụm tay")]
    [SerializeField] private float pinchDistanceThreshold = 0.085f;

    [Header("--- CẤU HÌNH THỜI GIAN THEO ĐỀ TÀI ---")]
    [Tooltip("Thời gian hồi chiêu giữa 2 lần chém để mượt mà (giây)")]
    [SerializeField] private float slashCooldown = 0.14f;

    [Tooltip("Thời gian chờ giữa 2 lần phát sự kiện tĩnh (giây)")]
    [SerializeField] private float gestureCooldown = 0.18f;

    [Tooltip("Ngưỡng tin cậy tối thiểu của MediaPipe")]
    [SerializeField] private float confidenceThreshold = 0.5f;

    [Header("--- DEBUG THEO DÕI THỜI GIAN THỰC ---")]
    [SerializeField] private HandGestureType detectedCandidate = HandGestureType.None;
    [SerializeField] private HandGestureType currentConfirmedGesture = HandGestureType.None;
    [SerializeField] private float candidateDuration = 0f;
    [SerializeField] private float cooldownTimer = 0f;
    [SerializeField] private float slashCooldownTimer = 0f;
    private float postSlashGrabLockTimer = 0f;
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
    private Vector2 prevCentroid = Vector2.zero;
    private float prevCentroidTime = -1f;

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
        if (slashCooldownTimer > 0f)
        {
            slashCooldownTimer -= Time.deltaTime;
        }
        if (postSlashGrabLockTimer > 0f)
        {
            postSlashGrabLockTimer -= Time.deltaTime;
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
                prevCentroidTime = -1f;
            }
        }
        else
        {
            trackingLostTimer = 0f;
        }

        if (GameHUDController.Instance != null && !GameHUDController.Instance.IsGameStarted())
            return;

        HandlePCTestShortcuts();
    }

    public void NotifyHandLost()
    {
        isHandTracked = false;
        candidateDuration = 0f;
        detectedCandidate = HandGestureType.None;
        currentConfirmedGesture = HandGestureType.None;
        motionHistory.Clear();
        currentHandSpeed = 0f;
        prevCentroidTime = -1f;
    }

    public void TriggerInstantSlash(float detectedSpeed, Vector2 startNorm, Vector2 endNorm)
    {
        currentHandSpeed = detectedSpeed;
        lastSlashStart = startNorm;
        lastSlashEnd = endNorm;

        if (slashCooldownTimer <= 0f && detectedSpeed >= slashSpeedThreshold)
        {
            ConfirmGesture(HandGestureType.Slash);
            slashCooldownTimer = slashCooldown;
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
    /// - Khi bàn tay ổn định: nhận diện Tường chắn (xòe tay) hoặc Lấy tim (chụm/nắm tay) ngay lập tức.
    /// </summary>
    public void ProcessLandmarks(HandLandmarkResult rawResult)
    {
        if (GameHUDController.Instance != null && !GameHUDController.Instance.IsGameStarted())
            return;

        if (rawResult == null || rawResult.confidence < confidenceThreshold)
        {
            isHandTracked = false;
            return;
        }

        bool wasTracked = isHandTracked;
        isHandTracked = true;
        SmoothLandmarks(rawResult, !wasTracked);

        Vector3 wrist = smoothedResult.GetJoint(HandJoint.Wrist);
        Vector3 thumbTip = smoothedResult.GetJoint(HandJoint.ThumbTip);
        Vector3 indexTip = smoothedResult.GetJoint(HandJoint.IndexTip);
        Vector3 middleTip = smoothedResult.GetJoint(HandJoint.MiddleTip);

        currentPinchDistance = Vector3.Distance(thumbTip, indexTip);
        currentHandCenterY = (wrist.y + middleTip.y) * 0.5f;

        // =========================================================================
        // 1. CHÉM QUÁI (SLASH): THEO DÕI VẬN TỐC & QUỸ ĐẠO BÀN TAY TRONG 0.16s
        // =========================================================================
        float now = Time.time;
        Vector2 currentPos = new Vector2(smoothedResult.centroid.x, smoothedResult.centroid.y);

        if (!wasTracked)
        {
            motionHistory.Clear();
            prevCentroid = currentPos;
            prevCentroidTime = now;
        }

        float instantSpeed = 0f;
        if (prevCentroidTime > 0f && (now - prevCentroidTime) > 0.005f)
        {
            instantSpeed = Vector2.Distance(currentPos, prevCentroid) / (now - prevCentroidTime);
        }
        prevCentroid = currentPos;
        prevCentroidTime = now;

        motionHistory.Enqueue(new CentroidSample { position = currentPos, timestamp = now });

        while (motionHistory.Count > 0 && (now - motionHistory.Peek().timestamp) > 0.18f)
        {
            motionHistory.Dequeue();
        }

        float maxSpeed = 0f;
        float maxDisplacement = 0f;
        Vector2 slashStartPoint = currentPos;

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

        bool isSlash = (motionHistory.Count >= 3 && maxDisplacement >= slashMinDisplacement && maxSpeed >= slashSpeedThreshold);

        if (isSlash && slashCooldownTimer <= 0f)
        {
            lastSlashStart = slashStartPoint;
            lastSlashEnd = currentPos;
            ConfirmGesture(HandGestureType.Slash);
            motionHistory.Clear();
            detectedCandidate = HandGestureType.None;
            candidateDuration = 0f;
            return;
        }

        // =========================================================================
        // 2. CỬ CHỈ TĨNH: XÉT KHI TỐC ĐỘ TỨC THỜI THẤP (< 0.55 m/s)
        // =========================================================================
        float dtStatic = (lastStaticCheckTime > 0f) ? Mathf.Clamp(now - lastStaticCheckTime, 0f, 0.2f) : 0.033f;
        lastStaticCheckTime = now;

        if (instantSpeed < 0.55f)
        {
            float handW = smoothedResult.handWidth;
            float handH = smoothedResult.handHeight;
            float ratio = handH / Mathf.Max(handW, 0.01f);

            HandGestureType staticGesture = HandGestureType.None;

            // LẤY TIM (GRAB): Chụm ngón rõ hoặc nắm/chụm tay gọn (kể cả khi dính cẳng tay phía dưới làm handH cao)
            bool canGrabByActivity = smoothedResult.isMoving && postSlashGrabLockTimer <= 0f;
            bool isPinching = currentPinchDistance > 0.001f && currentPinchDistance <= pinchDistanceThreshold;
            bool isCompactFist = (handW <= 0.28f && handH <= 0.34f && ratio >= 0.65f && ratio <= 1.65f &&
                                  smoothedResult.fillRatio >= 0.50f);
            bool isPinchCluster = (smoothedResult.fingerPeaks <= 1 && smoothedResult.fillRatio >= 0.48f && handW <= 0.32f && handH <= 0.65f);
            bool isGrab = canGrabByActivity && (isPinching || isCompactFist || isPinchCluster);

            // TƯỜNG CHẮN (SHIELD): Phải có >= 2 đỉnh ngón tay xòe ra HOẶC bàn tay đang giơ có khe hở giữa ngón
            // Tránh khuôn mặt tĩnh tròn (fingerPeaks <= 1, fillRatio cao) tự kích hoạt tường chắn!
            bool hasOpenHandShape = (smoothedResult.fingerPeaks >= 2) || (smoothedResult.isMoving && smoothedResult.fillRatio <= 0.54f);
            bool isOpenPalm = !isGrab && hasOpenHandShape && (handW >= 0.08f && handH >= 0.10f) &&
                              (currentPinchDistance > openPalmMinPinch || handH >= 0.13f);

            if (isGrab)
            {
                staticGesture = HandGestureType.Grab;
            }
            else if (isOpenPalm)
            {
                staticGesture = HandGestureType.OpenPalm;
            }

            if (staticGesture != HandGestureType.None)
            {
                bool isNewGesture = (staticGesture != currentConfirmedGesture);
                if (staticGesture == detectedCandidate)
                {
                    candidateDuration += dtStatic;
                }
                else
                {
                    detectedCandidate = staticGesture;
                    candidateDuration = 0f;
                }

                // Yêu cầu giữ chụm tay 0.10s và Tường chắn 0.08s
                float reqDuration = (staticGesture == HandGestureType.Grab) ? 0.10f : 0.08f;

                if (candidateDuration >= reqDuration && (isNewGesture || cooldownTimer <= 0f))
                {
                    ConfirmGesture(staticGesture);
                    candidateDuration = 0f;
                }
            }
            else
            {
                detectedCandidate = HandGestureType.None;
                candidateDuration = 0f;
            }
        }
        else
        {
            detectedCandidate = HandGestureType.None;
            candidateDuration = 0f;
        }
    }

    private void SmoothLandmarks(HandLandmarkResult raw, bool snapImmediate = false)
    {
        smoothedResult.confidence = raw.confidence;
        smoothedResult.isRightHand = raw.isRightHand;
        smoothedResult.handWidth = raw.handWidth;
        smoothedResult.handHeight = raw.handHeight;
        smoothedResult.fillRatio = raw.fillRatio;
        smoothedResult.fingerPeaks = raw.fingerPeaks;
        smoothedResult.isMoving = raw.isMoving;
        smoothedResult.centroid = raw.centroid;

        float t = snapImmediate ? 1.0f : 0.72f;
        for (int i = 0; i < 21; i++)
        {
            smoothedResult.landmarks[i] = snapImmediate
                ? raw.landmarks[i]
                : Vector3.Lerp(smoothedResult.landmarks[i], raw.landmarks[i], t);
        }
    }

    public void ConfirmGesture(HandGestureType gesture)
    {
        currentConfirmedGesture = gesture;
        if (gesture == HandGestureType.Slash)
        {
            slashCooldownTimer = Mathf.Max(slashCooldown, 0.22f);
            postSlashGrabLockTimer = 0.30f;
            Debug.Log($"<color=red><b>[GestureRecognizer] ⚔️ ĐÃ NHẬN DIỆN CHÉM QUÁI!</b> Vận tốc: {currentHandSpeed:F2} m/s</color>");
        }
        else if (gesture == HandGestureType.OpenPalm)
        {
            cooldownTimer = 0.15f;
            Debug.Log($"<color=cyan><b>[GestureRecognizer] 🛡️ ĐÃ BẬT TƯỜNG CHẮN!</b> Bàn tay mở thẳng giơ cao</color>");
        }
        else if (gesture == HandGestureType.Grab)
        {
            cooldownTimer = 0.18f;
            isHoldingObject = true;
            Debug.Log($"<color=orange><b>[GestureRecognizer] ✊ ĐÃ LẤY VẬT PHẨM / TRÁI TIM!</b> Chụm tay</color>");
        }
        else
        {
            cooldownTimer = gestureCooldown;
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
