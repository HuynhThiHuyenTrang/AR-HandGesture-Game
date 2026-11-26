using System.Collections;
using UnityEngine;

/// <summary>
/// Trái tim hồi máu 3D xuất hiện trong trận chiến AR Monster Hunter:
/// - Tự động xoay tròn và nhấp nhô nhẹ nhàng trong không gian 3D.
/// - Phát hiệu ứng nhịp tim đập (Pulsing Heartbeat) sống động.
/// - Khi người chơi chụm cả bàn tay lại (Cử chỉ Grab / Phím 3):
///   Trái tim sẽ bay nhanh về phía người chơi (Magnet Pull) và hồi 30 HP!
/// </summary>
public class CollectibleHeart : MonoBehaviour
{
    [Header("--- THÔNG SỐ HỒI MÁU ---")]
    [Tooltip("Lượng máu hồi phục cho người chơi")]
    [SerializeField] private int healAmount = 30;

    [Tooltip("Thời gian tồn tại tối đa trước khi tự biến mất (giây)")]
    [SerializeField] private float lifetime = 3.0f;

    [Header("--- HIỆU ỨNG CHUYỂN ĐỘNG ---")]
    [SerializeField] private float rotationSpeed = 90.0f;
    [SerializeField] private float bobbingSpeed = 3.5f;
    [SerializeField] private float bobbingHeight = 0.08f;

    private Vector3 startPosition;
    private float lifeTimer = 0f;
    private bool isBeingCollected = false;
    private Transform visualRoot;
    private Vector3 initialVisualScale = Vector3.one;

    void Awake()
    {
        startPosition = transform.position;
        visualRoot = transform.Find("Visual");
        if (visualRoot != null)
        {
            initialVisualScale = visualRoot.localScale;
        }
    }

    void Start()
    {
        startPosition = transform.position;
        if (visualRoot == null)
        {
            visualRoot = transform.Find("Visual");
            if (visualRoot != null)
            {
                initialVisualScale = visualRoot.localScale;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (isBeingCollected) return;
        // Chỉ ăn tim khi người chơi cầm điện thoại di chuyển lại gần chạm trực tiếp vào quả tim
        if (other.CompareTag("MainCamera") || other.GetComponentInParent<PlayerCombatController>() != null)
        {
            if (PlayerCombatController.Instance != null)
            {
                Collect(PlayerCombatController.Instance);
            }
        }
    }

    void Update()
    {
        if (isBeingCollected)
            return;

        lifeTimer += Time.deltaTime;
        if (lifeTimer >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        // 1. Tự động xoay tròn trong không gian
        transform.Rotate(0, rotationSpeed * Time.deltaTime, 0, Space.World);

        // 2. Nhấp nhô bồng bềnh
        float newY = startPosition.y + Mathf.Sin(Time.time * bobbingSpeed) * bobbingHeight;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);

        // 3. Hiệu ứng nhịp tim đập (Beating Heart pulse)
        if (visualRoot != null)
        {
            float pulse = 1.0f + 0.12f * Mathf.Sin(Time.time * 8.0f);
            visualRoot.localScale = initialVisualScale * pulse;
        }
    }

    /// <summary>
    /// Kích hoạt khi người chơi chụm cả bàn tay lại để hút/thu thập trái tim
    /// </summary>
    public void Collect(PlayerCombatController player)
    {
        if (isBeingCollected)
            return;

        isBeingCollected = true;
        StartCoroutine(FlyToPlayerAndHeal(player));
    }

    private IEnumerator FlyToPlayerAndHeal(PlayerCombatController player)
    {
        Camera cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        Vector3 targetPos = (cam != null) ? cam.transform.position : player.transform.position;
        // Điểm hút hơi chếch về trước mắt camera
        if (cam != null) targetPos += cam.transform.forward * 0.4f;

        Vector3 startFlyPos = transform.position;
        Vector3 startScale = transform.localScale;
        float duration = 0.25f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Bay tăng tốc mượt mà
            float smoothT = t * t;

            if (cam != null)
            {
                targetPos = cam.transform.position + cam.transform.forward * 0.35f;
            }

            transform.position = Vector3.Lerp(startFlyPos, targetPos, smoothT);
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, smoothT);

            yield return null;
        }

        // Hồi máu cho người chơi
        if (player != null)
        {
            player.HealPlayer(healAmount);
        }

        Destroy(gameObject);
    }

    public bool IsBeingCollected() => isBeingCollected;
    public int GetHealAmount() => healAmount;

    /// <summary>
    /// Hàm tĩnh hỗ trợ sinh Trái Tim 3D tại bất kỳ vị trí nào trong Scene
    /// Tạo mô hình hình trái tim đỏ rực rỡ có ánh sáng phát quang URP
    /// </summary>
    public static GameObject SpawnHeart(Vector3 spawnPosition)
    {
        GameObject heartObj = new GameObject("HeartItem_HP");
        heartObj.transform.position = spawnPosition;
        heartObj.transform.localScale = Vector3.one * 1.5f;

        // Gắn script CollectibleHeart
        CollectibleHeart heartComp = heartObj.AddComponent<CollectibleHeart>();

        // Tạo collider trigger
        SphereCollider col = heartObj.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.35f;

        // Tạo GameObject Visual chứa các khối hình thành quả tim 3D
        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(heartObj.transform, false);

        // Tạo vật liệu đỏ phát quang URP
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Standard");

        Material heartMat = new Material(shader);
        Color heartColor = new Color(1.0f, 0.15f, 0.32f, 1.0f);
        if (heartMat.HasProperty("_BaseColor")) heartMat.SetColor("_BaseColor", heartColor);
        if (heartMat.HasProperty("_Color")) heartMat.SetColor("_Color", heartColor);
        if (heartMat.HasProperty("_EmissionColor"))
        {
            heartMat.EnableKeyword("_EMISSION");
            heartMat.SetColor("_EmissionColor", new Color(0.85f, 0.08f, 0.22f, 1.0f));
        }

        // 1. Lobe trái quả tim (Sphere)
        GameObject leftLobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        leftLobe.name = "LeftLobe";
        leftLobe.transform.SetParent(visual.transform, false);
        leftLobe.transform.localPosition = new Vector3(-0.08f, 0.08f, 0);
        leftLobe.transform.localScale = new Vector3(0.22f, 0.22f, 0.18f);
        DestroyCollider(leftLobe);
        leftLobe.GetComponent<MeshRenderer>().material = heartMat;

        // 2. Lobe phải quả tim (Sphere)
        GameObject rightLobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        rightLobe.name = "RightLobe";
        rightLobe.transform.SetParent(visual.transform, false);
        rightLobe.transform.localPosition = new Vector3(0.08f, 0.08f, 0);
        rightLobe.transform.localScale = new Vector3(0.22f, 0.22f, 0.18f);
        DestroyCollider(rightLobe);
        rightLobe.GetComponent<MeshRenderer>().material = heartMat;

        // 3. Đáy quả tim hình nón / khối tháp (Cube xoay 45 độ)
        GameObject bottomTip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bottomTip.name = "BottomTip";
        bottomTip.transform.SetParent(visual.transform, false);
        bottomTip.transform.localPosition = new Vector3(0, -0.04f, 0);
        bottomTip.transform.localRotation = Quaternion.Euler(0, 0, 45f);
        bottomTip.transform.localScale = new Vector3(0.19f, 0.19f, 0.18f);
        DestroyCollider(bottomTip);
        bottomTip.GetComponent<MeshRenderer>().material = heartMat;

        // Thêm ánh sáng dịu màu đỏ phát ra từ quả tim
        GameObject lightObj = new GameObject("HeartLight");
        lightObj.transform.SetParent(heartObj.transform, false);
        Light lightComp = lightObj.AddComponent<Light>();
        lightComp.type = LightType.Point;
        lightComp.color = new Color(1.0f, 0.2f, 0.4f);
        lightComp.range = 2.0f;
        lightComp.intensity = 1.2f;

        Debug.Log($"<color=magenta><b>[CollectibleHeart]</b> Đã sinh trái tim hồi máu tại {spawnPosition}!</color>");
        return heartObj;
    }

    private static void DestroyCollider(GameObject obj)
    {
        Collider c = obj.GetComponent<Collider>();
        if (c != null)
        {
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }
    }
}
