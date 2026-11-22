using System.Collections;
using UnityEngine;

/// <summary>
/// Quản lý chiến đấu của Người chơi (Combat Controller):
/// 1. CHÉM QUÁI (Slash): Vung tay hoặc click chuột để chém quái vật bằng nhát kiếm năng lượng.
/// 2. TƯỜNG CHẮN (Shield / OpenPalm): Thẳng bàn tay mở giơ cao để kích hoạt khiên chặn 100% sát thương.
/// 3. LẤY VẬT PHẨM (Grab): Chụm cả bàn tay lại để hút/thu thập vật phẩm rơi ra từ quái vật.
/// </summary>
public class PlayerCombatController : MonoBehaviour
{
    public static PlayerCombatController Instance { get; private set; }

    [Header("--- THÔNG SỐ MÁU NGƯỜI CHƠI ---")]
    [Tooltip("Lượng máu tối đa của người chơi")]
    [SerializeField] private int maxHealth = 100;
    private int currentHealth;

    [Header("--- TRẠNG THÁI PHÒNG THỦ & KHIÊN ---")]
    [Tooltip("Thời gian khiên duy trì hiệu lực sau khi kích hoạt (giây)")]
    [SerializeField] private float shieldDuration = 2.5f;
    private bool isShieldActive = false;
    private float shieldTimer = 0f;

    [Header("--- HIỆU QUẢ ĐÒN ĐÁNH ---")]
    [Tooltip("Sát thương đòn chém gây ra cho quái vật (250 HP để chạm nhẹ là 1-hit quái thường chết ngay)")]
    [SerializeField] private int slashDamage = 250;

    [Tooltip("Khoảng cách tối đa mà đòn chém có thể với tới (mét)")]
    [SerializeField] private float slashReachDistance = 8.0f;

    [Tooltip("Dung sai bán kính quét trúng đòn trên màn hình (pixel, cực rộng để không cần căn chuẩn xác)")]
    [SerializeField] private float slashHitPixelTolerance = 450f;

    [Header("--- TIẾN ĐỘ THU THẬP VẬT PHẨM ---")]
    [SerializeField] private int itemsCollected = 0;

    [Header("--- HỆ THỐNG COMBO & CHÍ MẠNG ---")]
    [SerializeField] private int comboCount = 0;
    [SerializeField] private float comboWindow = 2.5f;
    private float lastSlashHitTime = -10f;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        currentHealth = maxHealth;
    }

    void OnEnable()
    {
        GestureRecognizer.OnGestureConfirmed += HandleGestureAction;
    }

    void OnDisable()
    {
        GestureRecognizer.OnGestureConfirmed -= HandleGestureAction;
    }

    void Update()
    {
        if (isShieldActive)
        {
            // Nếu người chơi đã hạ tay xuống (không còn thấy bàn tay giơ lên), ngắt khiên nhanh chóng trong 0.20s
            if (MediaPipeHandTracker.Instance != null && !MediaPipeHandTracker.Instance.IsHandVisible())
            {
                shieldTimer = Mathf.Min(shieldTimer, 0.20f);
            }

            shieldTimer -= Time.deltaTime;
            if (shieldTimer <= 0f)
            {
                isShieldActive = false;
                Debug.Log("<color=yellow>[Combat] Khiên bảo vệ đã hết hiệu lực!</color>");
            }
        }
    }

    private void HandleGestureAction(HandGestureType gesture)
    {
        switch (gesture)
        {
            case HandGestureType.Slash:
                ExecuteSlashAttack();
                break;

            case HandGestureType.OpenPalm:
                // Tường chắn cử chỉ duy trì 0.60s:
                // Khi người chơi còn giơ tay xòe thì liên tục gia hạn; khi hạ tay xuống thì tự tắt ngay!
                ActivateShield(0.60f);
                break;

            case HandGestureType.Grab:
                ExecuteGrabItem();
                break;

            case HandGestureType.Push:
                ExecutePushForce();
                break;

            case HandGestureType.Release:
                break;
        }
    }

    /// <summary>
    /// ĐÒN CHÉM QUÁI: Mặc định lấy toạ độ vệt chém mới nhất của bàn tay
    /// </summary>
    public void ExecuteSlashAttack()
    {
        Vector2 startNorm = (GestureRecognizer.Instance != null) ? GestureRecognizer.Instance.GetLastSlashStart() : new Vector2(0.28f, 0.70f);
        Vector2 endNorm = (GestureRecognizer.Instance != null) ? GestureRecognizer.Instance.GetLastSlashEnd() : new Vector2(0.72f, 0.30f);
        ExecuteSlashAttack(startNorm, endNorm);
    }

    /// <summary>
    /// ĐÒN CHÉM QUÁI THEO ĐÚNG QUỸ ĐẠO BÀN TAY (HAND-TRACKED DYNAMIC SLASH):
    /// Tia chém năng lượng xuất hiện chính xác 100% tại vị trí bàn tay vung chém.
    /// Quét chính xác quái vật nào nằm trên vệt chém trên màn hình và không gian 3D.
    /// </summary>
    public void ExecuteSlashAttack(Vector2 startNorm, Vector2 endNorm)
    {
        Camera cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        int sw = Screen.width;
        int sh = Screen.height;

        // Chuyển toạ độ chuẩn hóa sang Screen Pixel (hệ trục OnGUI: gốc (0,0) ở góc trên bên trái)
        Vector2 p1 = new Vector2(startNorm.x * sw, (1.0f - startNorm.y) * sh);
        Vector2 p2 = new Vector2(endNorm.x * sw, (1.0f - endNorm.y) * sh);

        // Tính toán Combo đòn đánh
        if (Time.time - lastSlashHitTime <= comboWindow)
        {
            comboCount = Mathf.Min(comboCount + 1, 10);
        }
        else
        {
            comboCount = 1;
        }

        // Tính tỉ lệ Chí Mạng (Critical Hit): Cơ bản 15% + 5% mỗi nấc combo (tối đa 60%)
        float critChance = 0.15f + comboCount * 0.05f;
        bool isCrit = (Random.value < critChance);
        int finalDamage = isCrit ? Mathf.RoundToInt(slashDamage * 1.8f) : slashDamage;

        bool hitAnyMonster = false;

        MonsterController[] allMonsters = Object.FindObjectsByType<MonsterController>(FindObjectsSortMode.None);
        foreach (var monster in allMonsters)
        {
            if (monster == null || monster.IsDead()) continue;

            Vector3 worldPos = monster.transform.position;
            Vector3 camPos = cam != null ? cam.transform.position : transform.position;
            float worldDist = Vector3.Distance(camPos, worldPos);

            if (worldDist > slashReachDistance) continue;

            bool isHit = false;

            if (cam != null)
            {
                Vector3 screenPoint = cam.WorldToScreenPoint(worldPos);
                if (screenPoint.z > 0) // Nằm trước mặt camera
                {
                    Vector2 monsterGuiPos = new Vector2(screenPoint.x, sh - screenPoint.y);
                    float distToSlashLine = DistancePointToSegment(monsterGuiPos, p1, p2);

                    // Cơ chế chạm nhẹ là trúng (Super Lenient Hitbox - Cực kỳ dễ chơi):
                    // 1. Vệt kiếm chém đi gần quái trong phạm vi rộng (<= 450px)
                    // 2. HOẶC quái đang ở trước mặt trong cự ly gần (<= 4.2m)
                    // 3. HOẶC quái nằm trong khung nhìn màn hình camera (<= 6.5m)
                    if (distToSlashLine <= slashHitPixelTolerance || worldDist <= 4.2f || (screenPoint.x >= -80 && screenPoint.x <= sw + 80 && screenPoint.y >= -80 && screenPoint.y <= sh + 80 && worldDist <= 6.5f))
                    {
                        isHit = true;
                    }
                }
            }
            else if (worldDist <= slashReachDistance)
            {
                isHit = true;
            }

            if (isHit)
            {
                monster.TakeDamage(finalDamage);
                hitAnyMonster = true;
                lastSlashHitTime = Time.time;
                Debug.Log($"<color=green>[Combat] ⚔️ CHÉM TRÚNG QUÁI {monster.name}! Combo: x{comboCount} | Sát thương: {finalDamage}{(isCrit ? " (CHÍ MẠNG!)" : "")}</color>");
            }
        }

        if (!hitAnyMonster)
        {
            comboCount = 0; // Chém trượt thì mất chuỗi Combo
            Debug.Log("<color=yellow>[Combat] Vung chém hụt! Hãy nhắm vệt tay chính xác vào vị trí quái vật!</color>");
        }

        // Hiển thị tia kiếm chém xuất hiện CHÍNH XÁC theo quỹ đạo bàn tay người chơi
        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.TriggerSlashFx(p1, p2, isCrit, comboCount, hitAnyMonster);
        }
    }

    /// <summary>
    /// Tính khoảng cách vuông góc từ điểm p đến đoạn thẳng v-w (tính bằng pixel màn hình)
    /// </summary>
    private float DistancePointToSegment(Vector2 p, Vector2 v, Vector2 w)
    {
        float l2 = (v - w).sqrMagnitude;
        if (l2 < 0.0001f) return Vector2.Distance(p, v);

        float t = Mathf.Clamp01(Vector2.Dot(p - v, w - v) / l2);
        Vector2 projection = v + t * (w - v);
        return Vector2.Distance(p, projection);
    }

    public void ActivateShield(float duration)
    {
        isShieldActive = true;
        shieldTimer = duration;
        Debug.Log($"<color=cyan><b>[Combat] 🛡️ KÍCH HOẠT TƯỜNG CHẮN BẢO VỆ! (Duy trì {duration:F2}s)</b></color>");
    }

    public void ExecuteGrabItem()
    {
        Camera cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        Vector3 origin = cam != null ? cam.transform.position : transform.position;

        // Tìm trái tim hồi máu gần nhất trong không gian xung quanh
        CollectibleHeart[] allHearts = Object.FindObjectsByType<CollectibleHeart>(FindObjectsSortMode.None);
        CollectibleHeart nearestHeart = null;
        float minDistance = float.MaxValue;

        foreach (var heart in allHearts)
        {
            if (heart == null || heart.IsBeingCollected()) continue;
            float dist = Vector3.Distance(origin, heart.transform.position);
            if (dist < minDistance && dist <= 15.0f) // Trong cự ly hút 15 mét (bao quát toàn bộ chiến trường)
            {
                minDistance = dist;
                nearestHeart = heart;
            }
        }

        if (nearestHeart != null)
        {
            itemsCollected++;
            Debug.Log($"<color=magenta><b>[Combat] ✊ ĐÃ HÚT TRÁI TIM HỒI MÁU! (Cự ly {minDistance:F2}m)</b></color>");
            if (GameHUDController.Instance != null)
            {
                GameHUDController.Instance.TriggerItemPickupFx();
            }
            nearestHeart.Collect(this);
        }
        else
        {
            itemsCollected++;
            Debug.Log($"<color=orange><b>[Combat] ✊ ĐÃ CHỤM TAY LẤY VẬT PHẨM! Tổng vật phẩm: {itemsCollected}</b></color>");

            if (GameHUDController.Instance != null)
            {
                GameHUDController.Instance.AddScore(50);
                GameHUDController.Instance.TriggerItemPickupFx();
            }
        }
    }

    /// <summary>
    /// Hồi phục máu cho người chơi khi nhặt được Trái Tim Hồi Máu (CollectibleHeart):
    /// - Tăng lượng máu lên tối đa maxHealth (100 HP)
    /// - Kích hoạt hiệu ứng HUD phát sáng và cộng điểm
    /// </summary>
    public void HealPlayer(int amount)
    {
        int oldHp = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        int healed = currentHealth - oldHp;
        Debug.Log($"<color=green><b>[Combat] ❤️ HỒI PHỤC +{healed} HP! Máu hiện tại: {currentHealth}/{maxHealth}</b></color>");

        if (GameHUDController.Instance != null)
        {
            // Hiển thị lượng máu hồi phục (nếu đã đầy 100 HP thì hiển thị thông báo +30 HP đầy bình)
            int displayHeal = (healed > 0) ? healed : amount;
            GameHUDController.Instance.TriggerHeartHealFx(displayHeal);
        }
    }

    public void ExecutePushForce()
    {
        Debug.Log("<color=yellow><b>[Combat] 🌀 KÍCH HOẠT SÓNG TRƯỜNG LỰC ĐẨY LÙI!</b></color>");
        MonsterController[] allMonsters = Object.FindObjectsByType<MonsterController>(FindObjectsSortMode.None);
        foreach (var monster in allMonsters)
        {
            if (monster == null || monster.IsDead()) continue;
            float distance = Vector3.Distance(transform.position, monster.transform.position);
            if (distance <= 4.0f)
            {
                Vector3 pushDirection = (monster.transform.position - transform.position).normalized;
                monster.transform.position += pushDirection * 2.0f;
                monster.TakeDamage(15);
            }
        }
    }

    public void TakePlayerDamage(int damage, MonsterController attacker = null)
    {
        if (isShieldActive)
        {
            Debug.Log("<color=green><b>[Combat] 🛡️ TƯỜNG CHẮN ĐÃ CHẶN ĐỨNG ĐÒN ĐÁNH CỦA QUÁI VẬT! Không mất máu!</b></color>");
            if (attacker != null)
            {
                attacker.Stun(2.5f); // Làm choáng quái vật trong 2.5s khi bị phản đòn!
            }
            if (GameHUDController.Instance != null)
            {
                GameHUDController.Instance.TriggerParryFx();
            }
            return;
        }

        comboCount = 0; // Bị quái đánh trúng thì mất Combo
        currentHealth -= damage;
        Debug.Log($"<color=red>[Player] Người chơi bị đánh trúng! Mất {damage} HP. Còn lại: {currentHealth}/{maxHealth}</color>");

        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.TriggerDamageFlash();
        }

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Debug.LogError("[Player] GAME OVER! Người chơi đã bị hạ gục!");
        }
    }

    public int GetComboCount() => comboCount;

    public bool IsShieldActive() => isShieldActive;
    public int GetCurrentHealth() => currentHealth;
    public int GetItemsCollected() => itemsCollected;
}
