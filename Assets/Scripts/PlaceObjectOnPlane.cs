
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Script dùng để đặt vật thể (Cube hoặc Quái vật) lên mặt phẳng AR
/// Hỗ trợ cả New Input System và Legacy Input:
/// - Click chuột trái
/// - Chạm ngón tay (Touch)
/// - Phím SPACEBAR trên máy tính để sinh quái vật ngay lập tức!
/// </summary>
public class PlaceObjectOnPlane : MonoBehaviour
{
    [Header("Cấu hình AR")]
    [Tooltip("Quản lý việc bắn tia Raycast tìm mặt phẳng AR")]
    [SerializeField] private ARRaycastManager raycastManager;

    [Tooltip("Khuôn mẫu vật thể / quái vật sẽ sinh ra khi chạm hoặc click")]
    [SerializeField] private GameObject cubePrefab;

    // Danh sách lưu kết quả các điểm chạm mặt phẳng
    private static List<ARRaycastHit> hits = new List<ARRaycastHit>();

    void Awake()
    {
        // Tự động tìm ARRaycastManager nếu chưa kéo thả
        if (raycastManager == null)
        {
            raycastManager = GetComponent<ARRaycastManager>();
            if (raycastManager == null)
                raycastManager = Object.FindAnyObjectByType<ARRaycastManager>();
        }
    }

    void Update()
    {
        if (GameHUDController.Instance != null && !GameHUDController.Instance.IsGameStarted())
            return;

        // --- 1. XỬ LÝ THEO NEW INPUT SYSTEM (CHUẨN UNITY 6) ---
        #if ENABLE_INPUT_SYSTEM
        // Phím SPACEBAR để sinh quái vật nhanh
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            SpawnMonsterAtFallbackPosition();
            return;
        }

        // Cảm ứng trên màn hình điện thoại
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            Vector2 touchPos = Touchscreen.current.primaryTouch.position.ReadValue();
            TryPlaceObject(touchPos);
            return;
        }

        // Click chuột trái trên máy tính
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            TryPlaceObject(mousePos);
            return;
        }
        #endif

        // --- 2. FALLBACK CHO LEGACY INPUT NẾU ĐƯỢC BẬT ---
        #if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SpawnMonsterAtFallbackPosition();
            return;
        }

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == UnityEngine.TouchPhase.Began)
            {
                TryPlaceObject(touch.position);
                return;
            }
        }

        #if UNITY_EDITOR || UNITY_STANDALONE
        if (Input.GetMouseButtonDown(0))
        {
            TryPlaceObject(Input.mousePosition);
        }
        #endif
        #endif
    }

    /// <summary>
    /// Hàm dùng chung để bắn tia Raycast và đặt quái vật tại vị trí chạm
    /// Cơ chế 3 tầng bảo đảm 100% quái vật luôn xuất hiện khi người chơi thao tác
    /// </summary>
    /// <param name="screenPosition">Tọa độ 2D trên màn hình</param>
    private void TryPlaceObject(Vector2 screenPosition)
    {
        if (cubePrefab == null)
        {
            Debug.LogError("<color=red>[PlaceObjectOnPlane] Chưa gán prefab quái vật vào ô Cube Prefab!</color>");
            return;
        }

        // TẦNG 1: Thử bắn tia AR Raycast xuống mặt phẳng AR (Tất cả loại mặt phẳng Planes)
        if (raycastManager != null && raycastManager.Raycast(screenPosition, hits, TrackableType.Planes))
        {
            Pose hitPose = hits[0].pose;
            Camera camMain = Camera.main;
            Quaternion rot = hitPose.rotation;
            if (camMain != null)
            {
                Vector3 dir = camMain.transform.position - hitPose.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f) rot = Quaternion.LookRotation(dir);
            }
            GameObject spawned = Instantiate(cubePrefab, hitPose.position, rot);
            spawned.transform.localScale = Vector3.one * 50.0f;
            if (spawned.TryGetComponent<MonsterController>(out var mc) && MonsterSpawner.Instance != null)
            {
                mc.SetupMonsterLevel(MonsterSpawner.Instance.GetCurrentLevel(), false);
            }
            Debug.Log($"<color=green>[PlaceObjectOnPlane] Đã sinh quái vật trên mặt phẳng AR tại: {hitPose.position} (Scale: 50x ~ 1.3m)</color>");
            return;
        }

        // TẦNG 2: Nếu AR Raycast chưa bắt kịp, dùng Physics Raycast vào không gian 3D của môi trường mô phỏng
        Camera cam = Camera.main;
        if (cam != null)
        {
            Ray ray = cam.ScreenPointToRay(screenPosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 50f))
            {
                GameObject spawned = Instantiate(cubePrefab, hit.point, Quaternion.identity);
                spawned.transform.localScale = Vector3.one * 50.0f;
                Debug.Log($"<color=green>[PlaceObjectOnPlane] Đã sinh quái vật qua Physics Raycast tại: {hit.point} (Scale: 50x ~ 1.3m)</color>");
                return;
            }
        }

        // TẦNG 3: Fallback bảo đảm 100% sinh ra quái vật ngay trước mặt người chơi 2 mét
        SpawnMonsterAtFallbackPosition();
    }

    /// <summary>
    /// Sinh quái vật trực tiếp phía trước người chơi 2 mét trên mặt sàn
    /// </summary>
    public void SpawnMonsterAtFallbackPosition()
    {
        if (cubePrefab == null)
        {
            Debug.LogError("<color=red>[PlaceObjectOnPlane] Chưa gán prefab quái vật!</color>");
            return;
        }

        Camera cam = Camera.main;
        Vector3 camPos = (cam != null ? cam.transform.position : transform.position);
        Vector3 camForward = (cam != null ? cam.transform.forward : transform.forward);
        camForward.y = 0;
        camForward.Normalize();

        Vector3 spawnPos = camPos + camForward * 2.0f;
        spawnPos.y = (camPos.y > 0.5f) ? 0f : (camPos.y - 0.65f);

        GameObject spawned = Instantiate(cubePrefab, spawnPos, Quaternion.LookRotation(camPos - spawnPos));
        spawned.transform.localScale = Vector3.one * 50.0f;
        Debug.Log($"<color=green>[PlaceObjectOnPlane] Đã sinh quái vật trước mặt người chơi tại: {spawnPos} (Scale: 50x ~ 1.3m)</color>");
    }
}
