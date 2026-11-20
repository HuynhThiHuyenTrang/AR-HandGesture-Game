using System.Collections;
using UnityEngine;

/// <summary>
/// Quản lý toàn bộ Giao diện Chiến đấu (HUD) chuẩn theo thiết kế đề tài:
/// - Góc trên bên trái: Tim đỏ + Thanh máu (Health Bar) + Điểm số (Score)
/// - Góc trên ở giữa: Huy hiệu Cấp độ phong cách Sci-Fi (Level 1 -> Level 5)
/// - Góc trên bên phải: Đầu lâu (Số quái diệt được: X/30) + Đồng hồ đếm ngược (5s -> 1s)
/// - Tâm màn hình: Tâm ngắm hồng ngoại (Crosshair Reticle)
/// - Góc dưới bên trái: 3 nút kỹ năng chuẩn:
///   1. ⚔️ Chém quái (Vung tay chém / Click chuột trái)
///   2. 🛡️ Tường chắn (Thẳng bàn tay mở giơ cao)
///   3. ✊ Lấy vật phẩm (Chụm cả bàn tay lại)
/// - Hiệu ứng Vệt Kiếm Chém (Slash Trail FX) lướt ngang màn hình khi chém quái.
/// - Hiệu ứng Tường Chắn năng lượng (Shield FX) vòng cung bên trái khi bật khiên.
/// - Hiệu ứng Thu thập vật phẩm (Item Pickup Aura FX).
/// </summary>
public class GameHUDController : MonoBehaviour
{
    public static GameHUDController Instance { get; private set; }

    [Header("--- THIẾT LẬP GIAO DIỆN ---")]
    [SerializeField] private bool fullScreenWebCamOnPC = true;
    [Tooltip("Hiển thị tâm ngắm Sci-Fi ở giữa màn hình (Mặc định tắt để tránh vướng khuôn mặt/tầm nhìn)")]
    [SerializeField] private bool showCrosshair = false;

    [Header("--- ĐIỂM SỐ & TIẾN ĐỘ LEVEL (30 QUÁI/LEVEL) ---")]
    [SerializeField] private int currentScore = 0;
    [SerializeField] private int monstersKilledInLevel = 0;
    [SerializeField] private int totalMonstersPerLevel = 30;

    // Hiệu ứng vệt kiếm chém (Slash VFX)
    private bool isSlashFxActive = false;
    private float slashFxTimer = 0f;
    private Vector2 slashStartPos;
    private Vector2 slashEndPos;
    private bool isSlashCrit = false;
    private int currentCombo = 1;
    private bool lastSlashHit = true;

    // Hiệu ứng phản đòn thành công (Parry Stun VFX)
    private bool isParryFxActive = false;
    private float parryFxTimer = 0f;

    // Hiệu ứng chớp đỏ khi bị quái đánh trúng
    private float damageFlashTimer = 0f;

    // Hiệu ứng nhặt vật phẩm (Item Pickup VFX)
    private bool isItemPickupFxActive = false;
    private float itemPickupFxTimer = 0f;

    // Hiệu ứng hồi máu từ Trái Tim (Heart Heal VFX)
    private bool isHeartHealFxActive = false;
    private float heartHealFxTimer = 0f;
    private int lastHealAmount = 30;

    // Quản lý chuyển đổi Level & Thời gian nghỉ 2 giây giữa các màn (Level Transition)
    private bool isLevelTransitionActive = false;
    private float levelTransitionTimer = 0f;
    private int completedLevel = 1;
    private int nextLevel = 2;
    private string nextLevelTitle = "";
    private bool isVictoryActive = false;

    // Textures thủ công để vẽ đồ họa siêu nét không cần kéo asset ngoài
    private Texture2D whiteTex;
    private Texture2D redTex;
    private Texture2D blueTex;
    private Texture2D goldTex;
    private Texture2D darkBgTex;
    private Texture2D greenTex;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        showCrosshair = false; // Luôn luôn tắt tâm ngắm tròn trên mặt người chơi
        InitTextures();
    }

    private void InitTextures()
    {
        whiteTex = MakeColorTex(Color.white);
        redTex = MakeColorTex(new Color(0.95f, 0.2f, 0.2f, 1f));
        blueTex = MakeColorTex(new Color(0.1f, 0.7f, 1f, 0.85f));
        goldTex = MakeColorTex(new Color(1f, 0.85f, 0.2f, 0.9f));
        darkBgTex = MakeColorTex(new Color(0.05f, 0.15f, 0.25f, 0.75f));
        greenTex = MakeColorTex(new Color(0.15f, 0.95f, 0.35f, 1f));
    }

    private Texture2D MakeColorTex(Color col)
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, col);
        tex.Apply();
        return tex;
    }

    public void TriggerSlashFx(Vector2 p1, Vector2 p2, bool isCrit, int combo, bool hit)
    {
        isSlashFxActive = true;
        slashFxTimer = 0.45f;
        slashStartPos = p1;
        slashEndPos = p2;
        isSlashCrit = isCrit;
        currentCombo = combo;
        lastSlashHit = hit;
    }

    public void TriggerSlashFx()
    {
        int sw = Screen.width; int sh = Screen.height;
        TriggerSlashFx(new Vector2(sw * 0.28f, sh * 0.70f), new Vector2(sw * 0.72f, sh * 0.30f), false, 1, true);
    }

    public void TriggerHandTrackedSlash(Vector2 p1, Vector2 p2, bool isCrit)
    {
        TriggerSlashFx(p1, p2, isCrit, 1, true);
    }

    public void TriggerParryFx()
    {
        isParryFxActive = true;
        parryFxTimer = 1.3f;
        AddScore(150);
    }

    public void TriggerDamageFlash()
    {
        damageFlashTimer = 0.35f;
    }

    public void TriggerItemPickupFx()
    {
        isItemPickupFxActive = true;
        itemPickupFxTimer = 0.75f;
    }

    public void TriggerHeartHealFx(int healAmount)
    {
        isHeartHealFxActive = true;
        heartHealFxTimer = 1.2f;
        lastHealAmount = healAmount;
        AddScore(30);
    }

    public void AddScore(int points)
    {
        currentScore += points;
        Debug.Log($"<color=yellow>[HUD] +{points} Điểm! Tổng điểm: {currentScore}</color>");
    }

    /// <summary>
    /// Ghi nhận 1 quái vật đã thực sự bị tiêu diệt:
    /// - Chỉ tăng số quái bị hạ gục khi quái vật thực sự chết (HP về 0)
    /// - Cộng 100 điểm thưởng diệt quái
    /// - Khi đủ 30 quái thì kích hoạt thời gian nghỉ 2.5s và chuyển sang Level mới
    /// </summary>
    public void OnMonsterKilled(MonsterController monster = null)
    {
        monstersKilledInLevel++;
        AddScore(100);

        Debug.Log($"<color=green><b>[HUD] 💀 ĐÃ TIÊU DIỆT QUÁI VẬT! Tiến độ màn chơi: {monstersKilledInLevel}/{totalMonstersPerLevel}</b></color>");

        if (monstersKilledInLevel >= totalMonstersPerLevel)
        {
            monstersKilledInLevel = 0;
            TriggerLevelCompleteTransition();
        }
    }

    /// <summary>
    /// Kích hoạt chuỗi thông báo vượt màn và cho người chơi nghỉ 2.5s trước khi vào Level mới
    /// </summary>
    public void TriggerLevelCompleteTransition()
    {
        int currentLvl = MonsterSpawner.Instance != null ? MonsterSpawner.Instance.GetCurrentLevel() : 1;
        if (currentLvl >= 5)
        {
            isVictoryActive = true;
            Debug.Log("<color=yellow><b>🏆 CHIẾN THẮNG TOÀN DIỆN CHIẾN DỊCH AR MONSTER HUNTER!</b></color>");
            return;
        }

        int targetLvl = currentLvl + 1;
        StartLevelTransition(currentLvl, targetLvl);
    }

    public void StartLevelTransition(int fromLevel, int toLevel)
    {
        isLevelTransitionActive = true;
        levelTransitionTimer = 2.5f; // Nghỉ 2.5 giây giữa 2 màn
        completedLevel = fromLevel;
        nextLevel = toLevel;
        nextLevelTitle = GetLevelTitle(toLevel);

        if (MonsterSpawner.Instance != null)
        {
            MonsterSpawner.Instance.PauseSpawningForSeconds(2.5f);
        }

        // Tặng quà vượt màn: Hồi 50 HP cho người chơi
        if (PlayerCombatController.Instance != null)
        {
            PlayerCombatController.Instance.HealPlayer(50);
        }

        currentScore += 500; // Thưởng 500 điểm vượt màn
        Debug.Log($"<color=cyan><b>[HUD] 🎉 HOÀN THÀNH LEVEL {fromLevel}! NGHỈ 2.5 GIÂY CHUẨN BỊ BƯỚC VÀO LEVEL {toLevel}...</b></color>");
    }

    public void ForceAdvanceLevelForTest()
    {
        monstersKilledInLevel = 0;
        TriggerLevelCompleteTransition();
    }

    private string GetLevelTitle(int level)
    {
        switch (level)
        {
            case 1: return "MÀN 1: TẬP SỰ KHỞI ĐẦU";
            case 2: return "MÀN 2: HANG DƠI BÓNG ĐÊM (DƠI BAY CAO)";
            case 3: return "MÀN 3: CƠN THỊNH NỘ BERSERK (TỐC ĐỘ CAO)";
            case 4: return "MÀN 4: QUÂN ĐOÀN ĐỘT KÍCH (BAO VÂY DỒN DẬP)";
            case 5: return "MÀN 5: ĐẠI CHIẾN TRÙM CUỐI (SLIME CHÚA KHỔNG LỒ)";
            default: return $"LEVEL {level}";
        }
    }

    void Update()
    {
        if (isSlashFxActive)
        {
            slashFxTimer -= Time.deltaTime;
            if (slashFxTimer <= 0f)
                isSlashFxActive = false;
        }

        if (isItemPickupFxActive)
        {
            itemPickupFxTimer -= Time.deltaTime;
            if (itemPickupFxTimer <= 0f)
                isItemPickupFxActive = false;
        }

        if (isHeartHealFxActive)
        {
            heartHealFxTimer -= Time.deltaTime;
            if (heartHealFxTimer <= 0f)
                isHeartHealFxActive = false;
        }

        if (isParryFxActive)
        {
            parryFxTimer -= Time.deltaTime;
            if (parryFxTimer <= 0f)
                isParryFxActive = false;
        }

        if (damageFlashTimer > 0f)
        {
            damageFlashTimer -= Time.deltaTime;
        }

        // Đếm ngược thời gian nghỉ 2.5 giây giữa 2 màn
        if (isLevelTransitionActive)
        {
            levelTransitionTimer -= Time.deltaTime;
            if (levelTransitionTimer <= 0f)
            {
                isLevelTransitionActive = false;
                if (MonsterSpawner.Instance != null)
                {
                    MonsterSpawner.Instance.SetLevel(nextLevel);
                }
            }
        }

        // Phím L trên máy tính để test nhanh tính năng chuyển màn mà không cần đánh đủ 30 quái
        #if UNITY_EDITOR || UNITY_STANDALONE
        if (Input.GetKeyDown(KeyCode.L))
        {
            ForceAdvanceLevelForTest();
        }
        #endif
    }

    void OnGUI()
    {
        int sw = Screen.width;
        int sh = Screen.height;

        // Hiệu ứng chớp đỏ khi bị quái đánh trúng
        if (damageFlashTimer > 0f)
        {
            float alpha = Mathf.Clamp01((damageFlashTimer / 0.35f) * 0.40f);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), MakeColorTex(new Color(1f, 0.1f, 0.1f, alpha)));
        }

        int maxHp = 100;
        int currentHp = PlayerCombatController.Instance != null ? PlayerCombatController.Instance.GetCurrentHealth() : 100;
        float hpPercent = Mathf.Clamp01((float)currentHp / maxHp);

        // Hiệu ứng viền nhấp nháy cảnh báo Máu Nguy Cấp (dưới 35 HP)
        if (currentHp <= 35 && currentHp > 0)
        {
            float pulse = (Mathf.Sin(Time.time * 6.5f) + 1f) * 0.5f;
            Color dangerCol = new Color(0.9f, 0.08f, 0.08f, 0.25f + pulse * 0.35f);
            DrawBorder(new Rect(0, 0, sw, sh), dangerCol, 12);
        }

        // =========================================================================
        // 1. GÓC TRÊN BÊN TRÁI: TIM ĐỎ + THANH MÁU + SCORE + COMBO
        // =========================================================================
        GUI.DrawTexture(new Rect(25, 20, 220, 48), darkBgTex);
        DrawBorder(new Rect(25, 20, 220, 48), new Color(0.2f, 0.7f, 1f, 0.8f), 2);

        GUIStyle heartStyle = new GUIStyle();
        heartStyle.fontSize = 24;
        heartStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(32, 22, 35, 42), "❤️", heartStyle);

        GUI.DrawTexture(new Rect(72, 34, 160, 18), MakeColorTex(new Color(0.1f, 0.1f, 0.1f, 0.9f)));
        if (hpPercent > 0)
        {
            Texture2D barTex = isHeartHealFxActive ? greenTex : redTex;
            GUI.DrawTexture(new Rect(74, 36, 156 * hpPercent, 14), barTex);
        }

        GUIStyle scoreStyle = new GUIStyle();
        scoreStyle.fontSize = 18;
        scoreStyle.fontStyle = FontStyle.Bold;
        scoreStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(28, 72, 200, 25), $"Score: {currentScore}", scoreStyle);

        // Hiển thị hệ thống chuỗi Combo chiến đấu
        int combo = PlayerCombatController.Instance != null ? PlayerCombatController.Instance.GetComboCount() : 0;
        if (combo >= 2)
        {
            GUIStyle comboStyle = new GUIStyle();
            comboStyle.fontSize = 17;
            comboStyle.fontStyle = FontStyle.Bold;
            comboStyle.normal.textColor = (combo >= 3) ? new Color(1f, 0.88f, 0.2f, 1f) : new Color(0.2f, 0.9f, 1f, 1f);
            string comboText = (combo >= 3) ? $"⚡ COMBO x{combo} (CHÍ MẠNG!)" : $"🔥 COMBO x{combo}";
            GUI.Label(new Rect(28, 96, 260, 25), comboText, comboStyle);
        }

        // =========================================================================
        // 2. GÓC TRÊN Ở GIỮA: HUY HIỆU CẤP ĐỘ SCI-FI (LEVEL 1 -> LEVEL 5)
        // =========================================================================
        int level = MonsterSpawner.Instance != null ? MonsterSpawner.Instance.GetCurrentLevel() : 1;
        float badgeWidth = 180;
        float badgeX = (sw - badgeWidth) / 2f;

        GUI.DrawTexture(new Rect(badgeX, 15, badgeWidth, 42), blueTex);
        DrawBorder(new Rect(badgeX, 15, badgeWidth, 42), Color.white, 2);

        GUIStyle levelStyle = new GUIStyle();
        levelStyle.fontSize = 22;
        levelStyle.fontStyle = FontStyle.Bold;
        levelStyle.normal.textColor = Color.white;
        levelStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(badgeX, 15, badgeWidth, 42), $"Level {level}", levelStyle);

        // =========================================================================
        // 3. GÓC TRÊN BÊN PHẢI: ĐẦU LÂU (DIỆT QUÁI X/30) + QUÁI HIỆN TẠI (ALIVE)
        // =========================================================================
        float rightPanelW = 160;
        float rightPanelX = sw - rightPanelW - 25;

        // Ô 1: Tiến độ diệt quái (chỉ tăng khi quái thực sự bị chém chết)
        GUI.DrawTexture(new Rect(rightPanelX, 18, rightPanelW, 36), darkBgTex);
        DrawBorder(new Rect(rightPanelX, 18, rightPanelW, 36), new Color(0.2f, 0.7f, 1f, 0.8f), 2);

        GUIStyle skullStyle = new GUIStyle();
        skullStyle.fontSize = 18;
        skullStyle.fontStyle = FontStyle.Bold;
        skullStyle.normal.textColor = Color.white;
        skullStyle.alignment = TextAnchor.MiddleLeft;
        GUI.Label(new Rect(rightPanelX + 12, 18, 30, 36), "💀", skullStyle);
        GUI.Label(new Rect(rightPanelX + 48, 18, 105, 36), $"{monstersKilledInLevel} / {totalMonstersPerLevel}", skullStyle);

        // Ô 2: Số quái vật đang xuất hiện trước mắt người chơi
        int aliveCount = MonsterSpawner.Instance != null ? MonsterSpawner.Instance.GetAliveMonsterCount() : 0;
        GUI.DrawTexture(new Rect(rightPanelX, 60, rightPanelW, 36), darkBgTex);
        DrawBorder(new Rect(rightPanelX, 60, rightPanelW, 36), new Color(0.2f, 0.7f, 1f, 0.8f), 2);

        GUI.Label(new Rect(rightPanelX + 12, 60, 30, 36), "👾", skullStyle);
        GUI.Label(new Rect(rightPanelX + 48, 60, 105, 36), $"{aliveCount} Quái", skullStyle);

        // =========================================================================
        // 4. TÂM MÀN HÌNH: TÂM NGẮM (HOÀN TOÀN TẮT ĐỂ KHÔNG VƯỚNG MẶT NGƯỜI CHƠI)
        // =========================================================================
        if (showCrosshair && false)
        {
            DrawSciFiCrosshair(sw / 2f, sh / 2f);
        }

        // =========================================================================
        // 5. GÓC DƯỚI BÊN TRÁI: 3 NÚT KỸ NĂNG CHUẨN ĐỀ TÀI:
        //    [Lấy tim/Đồ] (❤️ Chụm tay) | [Tường chắn] (🛡️ Mở tay) | [Chém quái] (⚔️ Vung tay)
        // =========================================================================
        float skillY = sh - 110;
        float skillStartX = 30;
        float skillSize = 78;
        float skillSpacing = 98;

        bool isShieldOn = PlayerCombatController.Instance != null && PlayerCombatController.Instance.IsShieldActive();

        // Nút 1: Lấy trái tim / vật phẩm (Chụm cả bàn tay lại)
        DrawSkillButton(new Rect(skillStartX, skillY, skillSize, skillSize), "❤️", "Lấy tim/Đồ", isItemPickupFxActive || isHeartHealFxActive, () => {
            if (PlayerCombatController.Instance != null) PlayerCombatController.Instance.ExecuteGrabItem();
        });

        // Nút 2: Tường chắn (Thẳng bàn tay mở)
        DrawSkillButton(new Rect(skillStartX + skillSpacing, skillY, skillSize, skillSize), "🛡️", "Tường chắn", isShieldOn, () => {
            if (PlayerCombatController.Instance != null) PlayerCombatController.Instance.ActivateShield(2.5f);
        });

        // Nút 3: Chém quái (Vung tay chém)
        DrawSkillButton(new Rect(skillStartX + skillSpacing * 2, skillY, skillSize, skillSize), "⚔️", "Chém quái", isSlashFxActive, () => {
            if (PlayerCombatController.Instance != null) PlayerCombatController.Instance.ExecuteSlashAttack();
        });

        // =========================================================================
        // 6. HIỆU ỨNG TƯỜNG CHẮN NĂNG LƯỢNG (FULL-SCREEN SHIELD FX)
        // =========================================================================
        if (isShieldOn)
        {
            DrawShieldArcEffect(sw, sh);
        }

        // =========================================================================
        // 7. HIỆU ỨNG VỆT KIẾM CHÉM QUÁI VẬT (SLASH TRAIL FX)
        // =========================================================================
        if (isSlashFxActive)
        {
            DrawSlashBladeEffect(sw, sh);
        }

        // =========================================================================
        // 8. HIỆU ỨNG THU THẬP VẬT PHẨM (ITEM PICKUP FX)
        // =========================================================================
        if (isItemPickupFxActive)
        {
            DrawItemPickupBanner(sw, sh);
        }

        // =========================================================================
        // 9. HIỆU ỨNG HỒI MÁU TRÁI TIM (HEART HEAL BANNER FX)
        // =========================================================================
        if (isHeartHealFxActive)
        {
            DrawHeartHealBanner(sw, sh);
        }

        // =========================================================================
        // 10. THANH MÁU TRÙM CUỐI (BOSS HEALTH BAR TRONG LEVEL 5)
        // =========================================================================
        DrawBossHealthBarIfPresent(sw, level);

        // =========================================================================
        // 11. BIỂU NGỮ PHẢN ĐÒN PARRY THÀNH CÔNG
        // =========================================================================
        if (isParryFxActive)
        {
            DrawParryBanner(sw, sh);
        }

        // =========================================================================
        // 12. THÔNG BÁO VƯỢT MÀN & THỜI GIAN NGHỈ 2 GIÂY GIỮA CÁC LEVEL
        // =========================================================================
        if (isLevelTransitionActive)
        {
            DrawLevelTransitionBanner(sw, sh);
        }

        if (isVictoryActive)
        {
            DrawVictoryBanner(sw, sh);
        }
    }

    private void DrawSkillButton(Rect rect, string icon, string label, bool isActive, System.Action onClick)
    {
        Color bgColor = isActive ? new Color(0.1f, 0.45f, 0.85f, 0.95f) : new Color(0.05f, 0.15f, 0.25f, 0.75f);
        GUI.DrawTexture(rect, MakeColorTex(bgColor));

        Color borderColor = isActive ? new Color(1f, 0.9f, 0.2f, 1f) : new Color(0.2f, 0.7f, 1f, 0.8f);
        int borderWidth = isActive ? 3 : 2;
        DrawBorder(rect, borderColor, borderWidth);

        GUIStyle iconStyle = new GUIStyle();
        iconStyle.fontSize = 24;
        iconStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(rect.x, rect.y + 6, rect.width, 32), icon, iconStyle);

        GUIStyle labelStyle = new GUIStyle();
        labelStyle.fontSize = 11;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.normal.textColor = isActive ? new Color(1f, 0.95f, 0.3f, 1f) : Color.white;
        labelStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(rect.x, rect.y + 44, rect.width, 24), label, labelStyle);

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            onClick?.Invoke();
        }
    }

    private void DrawShieldArcEffect(int sw, int sh)
    {
        // 1. LỚP PHỦ NĂNG LƯỢNG TOÀN MÀN HÌNH (Sci-Fi Hex Forcefield Aura)
        GUI.DrawTexture(new Rect(0, 0, sw, sh), MakeColorTex(new Color(0.05f, 0.55f, 1f, 0.10f)));

        // 2. KHUNG VIỀN NĂNG LƯỢNG KHIÊN BẢO VỆ PHÁT SÁNG TOÀN MÀN HÌNH
        DrawBorder(new Rect(0, 0, sw, sh), new Color(0.2f, 0.90f, 1f, 0.85f), 3);

        // 3. VÒNG CUNG NĂNG LƯỢNG BÊN TRÁI MÀN HÌNH: DỪNG Ở 60% CHIỀU CAO, KHÔNG ĐÈ NÚT KỸ NĂNG
        float barWidth = 48f;
        float barHeight = sh * 0.38f;
        float barY = sh * 0.22f; // từ 22% đến 60% chiều cao màn hình
        Color shieldColor = new Color(0.1f, 0.75f, 1f, 0.40f);
        GUI.DrawTexture(new Rect(0, barY, barWidth, barHeight), MakeColorTex(shieldColor));
        Color lineGlow = new Color(0.3f, 0.95f, 1f, 0.95f);
        GUI.DrawTexture(new Rect(barWidth - 3, barY, 3, barHeight), MakeColorTex(lineGlow));

        GUIStyle shieldTextStyle = new GUIStyle();
        shieldTextStyle.fontSize = 13;
        shieldTextStyle.fontStyle = FontStyle.Bold;
        shieldTextStyle.normal.textColor = new Color(0.2f, 0.95f, 1f, 1f);
        shieldTextStyle.alignment = TextAnchor.MiddleCenter;
        string text = "T\nƯ\nỜ\nN\nG\n\nC\nH\nẮ\nN";
        GUI.Label(new Rect(4, barY + (barHeight - 160) / 2f, 40, 160), text, shieldTextStyle);

        // 4. BANNER TRUNG TÂM NỔI BẬT: TỰ ĐỘNG CO GIÃN THEO ĐỘ DÀI CHỮ, TUYỆT ĐỐI KHÔNG TRÀN KHUNG
        GUIStyle centerShieldStyle = new GUIStyle();
        centerShieldStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(sw * 0.016f), 13, 16);
        centerShieldStyle.fontStyle = FontStyle.Bold;
        centerShieldStyle.normal.textColor = new Color(0.2f, 0.95f, 1f, 1f);
        centerShieldStyle.alignment = TextAnchor.MiddleCenter;
        centerShieldStyle.clipping = TextClipping.Clip;
        centerShieldStyle.wordWrap = false;

        string shieldText = "🛡️ TƯỜNG CHẮN BẢO VỆ (CHẶN 100% SÁT THƯƠNG)";
        Vector2 textSz = centerShieldStyle.CalcSize(new GUIContent(shieldText));

        float bannerW = Mathf.Clamp(textSz.x + 36f, 360f, sw * 0.92f);
        float bannerH = 40f;
        float bannerX = (sw - bannerW) / 2f;
        float bannerY = 66f;

        Rect bannerRect = new Rect(bannerX, bannerY, bannerW, bannerH);
        GUI.DrawTexture(bannerRect, MakeColorTex(new Color(0.02f, 0.18f, 0.36f, 0.92f)));
        DrawBorder(bannerRect, new Color(0.2f, 0.95f, 1f, 1f), 2);

        GUI.Label(bannerRect, shieldText, centerShieldStyle);
    }

    private void DrawSlashBladeEffect(int sw, int sh)
    {
        float alpha = Mathf.Clamp01(slashFxTimer / 0.40f);

        // Toạ độ thực tế của vệt vung tay người chơi
        Vector2 p1 = slashStartPos;
        Vector2 p2 = slashEndPos;

        if (p1 == Vector2.zero && p2 == Vector2.zero)
        {
            p1 = new Vector2(sw * 0.28f, sh * 0.70f);
            p2 = new Vector2(sw * 0.72f, sh * 0.30f);
        }

        Vector2 dir = (p2 - p1).normalized;
        float len = Vector2.Distance(p1, p2);
        if (len < 50f) len = sw * 0.35f;

        // Kéo dài vệt kiếm khí nhẹ về 2 đầu để tia chém sắc nhọn và trải rộng
        Vector2 start = p1 - dir * (len * 0.20f);
        Vector2 end = p2 + dir * (len * 0.35f);

        // Màu sắc kiếm khí: Vàng lửa nếu Chí Mạng, Xanh Neon Cyberpunk nếu đòn thường
        Color coreCol = isSlashCrit ? new Color(1f, 0.95f, 0.4f, alpha) : new Color(1f, 1f, 1f, alpha);
        Color glowCol = isSlashCrit ? new Color(1f, 0.45f, 0.1f, alpha * 0.85f) : new Color(0.1f, 0.85f, 1f, alpha * 0.85f);
        Color outerCol = isSlashCrit ? new Color(1f, 0.2f, 0.05f, alpha * 0.45f) : new Color(0.0f, 0.45f, 1f, alpha * 0.45f);

        // Vẽ 3 tầng ánh sáng tia chém năng lượng
        DrawThickScreenLine(start, end, outerCol, 24f);
        DrawThickScreenLine(start, end, glowCol, 12f);
        DrawThickScreenLine(start, end, coreCol, 4f);

        // Hiệu ứng loé sáng (Impact Spark) ở 2 đầu vệt chém
        DrawCircleHollow(start.x, start.y, 8f * alpha, glowCol, 2f);
        DrawCircleHollow(end.x, end.y, 12f * alpha, coreCol, 3f);

        // Banner hiển thị kết quả nhát chém
        GUIStyle slashTextStyle = new GUIStyle();
        slashTextStyle.fontSize = 26;
        slashTextStyle.fontStyle = FontStyle.Bold;
        slashTextStyle.alignment = TextAnchor.MiddleCenter;

        if (lastSlashHit)
        {
            if (isSlashCrit)
            {
                slashTextStyle.normal.textColor = new Color(1f, 0.88f, 0.2f, alpha);
                GUI.Label(new Rect((sw - 420) / 2f, sh * 0.40f, 420, 44), $"⚡ CHÉM CHÍ MẠNG x2! (-90 HP) [COMBO x{currentCombo}]", slashTextStyle);
            }
            else
            {
                slashTextStyle.normal.textColor = new Color(0.2f, 1f, 0.6f, alpha);
                GUI.Label(new Rect((sw - 360) / 2f, sh * 0.40f, 360, 44), $"⚔️ CHÉM TRÚNG! (-45 HP){(currentCombo >= 2 ? $" [x{currentCombo}]" : "")}", slashTextStyle);
            }
        }
        else
        {
            slashTextStyle.fontSize = 20;
            slashTextStyle.normal.textColor = new Color(1f, 0.4f, 0.4f, alpha * 0.8f);
            GUI.Label(new Rect((sw - 300) / 2f, sh * 0.40f, 300, 40), "❌ CHÉM TRƯỢT!", slashTextStyle);
        }
    }

    private void DrawParryBanner(int sw, int sh)
    {
        float alpha = Mathf.Clamp01(parryFxTimer / 1.0f);

        GUIStyle parryStyle = new GUIStyle();
        parryStyle.fontSize = 24;
        parryStyle.fontStyle = FontStyle.Bold;
        parryStyle.normal.textColor = new Color(0.2f, 0.95f, 1f, alpha);
        parryStyle.alignment = TextAnchor.MiddleCenter;

        Rect bannerRect = new Rect((sw - 460) / 2f, sh * 0.28f, 460, 50);
        GUI.DrawTexture(bannerRect, MakeColorTex(new Color(0.02f, 0.15f, 0.30f, alpha * 0.9f)));
        DrawBorder(bannerRect, new Color(0.2f, 0.9f, 1f, alpha), 3);

        GUI.Label(bannerRect, "🛡️ PARRY THÀNH CÔNG! QUÁI BỊ CHOÁNG (+50% DMG)!", parryStyle);
    }

    private void DrawBossHealthBarIfPresent(int sw, int level)
    {
        if (level < 5) return;

        MonsterController boss = null;
        MonsterController[] allMonsters = Object.FindObjectsByType<MonsterController>(FindObjectsSortMode.None);
        foreach (var m in allMonsters)
        {
            if (m != null && m.IsBoss())
            {
                boss = m;
                break;
            }
        }

        if (boss == null) return;

        float barW = 440f;
        float barH = 24f;
        float barX = (sw - barW) / 2f;
        float barY = 64f;

        GUI.DrawTexture(new Rect(barX - 10, barY - 26, barW + 20, 56), darkBgTex);
        DrawBorder(new Rect(barX - 10, barY - 26, barW + 20, 56), new Color(1f, 0.85f, 0.2f, 0.9f), 2);

        GUIStyle bossNameStyle = new GUIStyle();
        bossNameStyle.fontSize = 15;
        bossNameStyle.fontStyle = FontStyle.Bold;
        bossNameStyle.normal.textColor = new Color(1f, 0.9f, 0.3f, 1f);
        bossNameStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(barX, barY - 24, barW, 20), "👑 TRÙM CUỐI: ĐẠI CHIẾN SLIME CHÚA", bossNameStyle);

        float hpPct = Mathf.Clamp01((float)boss.GetCurrentHealth() / boss.GetMaxHealth());
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), MakeColorTex(new Color(0.15f, 0.15f, 0.15f, 0.9f)));
        if (hpPct > 0)
        {
            GUI.DrawTexture(new Rect(barX + 2, barY + 2, (barW - 4) * hpPct, barH - 4), goldTex);
        }

        GUIStyle hpTextStyle = new GUIStyle();
        hpTextStyle.fontSize = 12;
        hpTextStyle.fontStyle = FontStyle.Bold;
        hpTextStyle.normal.textColor = Color.black;
        hpTextStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(barX, barY + 2, barW, barH - 4), $"{boss.GetCurrentHealth()} / {boss.GetMaxHealth()} HP", hpTextStyle);
    }

    private void DrawItemPickupBanner(int sw, int sh)
    {
        float alpha = Mathf.Clamp01(itemPickupFxTimer / 0.75f);

        GUIStyle itemStyle = new GUIStyle();
        itemStyle.fontSize = 22;
        itemStyle.fontStyle = FontStyle.Bold;
        itemStyle.normal.textColor = new Color(1f, 0.88f, 0.2f, alpha);
        itemStyle.alignment = TextAnchor.MiddleCenter;

        Rect bannerRect = new Rect((sw - 360) / 2f, sh * 0.58f, 360, 42);
        GUI.DrawTexture(bannerRect, MakeColorTex(new Color(0.05f, 0.15f, 0.25f, alpha * 0.8f)));
        DrawBorder(bannerRect, new Color(1f, 0.85f, 0.2f, alpha), 2);

        GUI.Label(bannerRect, "💎 ĐÃ LẤY VẬT PHẨM (+50 ĐIỂM)!", itemStyle);
    }

    private void DrawHeartHealBanner(int sw, int sh)
    {
        float alpha = Mathf.Clamp01(heartHealFxTimer / 1.0f);

        GUIStyle healStyle = new GUIStyle();
        healStyle.fontSize = 24;
        healStyle.fontStyle = FontStyle.Bold;
        healStyle.normal.textColor = new Color(0.2f, 1f, 0.45f, alpha);
        healStyle.alignment = TextAnchor.MiddleCenter;

        Rect bannerRect = new Rect((sw - 420) / 2f, sh * 0.50f, 420, 48);
        GUI.DrawTexture(bannerRect, MakeColorTex(new Color(0.04f, 0.22f, 0.12f, alpha * 0.9f)));
        DrawBorder(bannerRect, new Color(0.2f, 1f, 0.45f, alpha), 3);

        GUI.Label(bannerRect, $"❤️ +{lastHealAmount} HP (HỒI MÁU THÀNH CÔNG!)", healStyle);
    }

    private void DrawSciFiCrosshair(float cx, float cy)
    {
        float ringRadius = 24f;
        DrawCircleHollow(cx, cy, ringRadius, new Color(1f, 1f, 1f, 0.65f), 1.5f);

        GUI.DrawTexture(new Rect(cx - 2, cy - 2, 4, 4), MakeColorTex(new Color(1f, 0.3f, 0.3f, 0.9f)));

        float crossLength = 10f;
        float gap = 28f;
        Color crossCol = new Color(1f, 1f, 1f, 0.75f);

        GUI.DrawTexture(new Rect(cx, cy - gap - crossLength, 2, crossLength), MakeColorTex(crossCol));
        GUI.DrawTexture(new Rect(cx, cy + gap, 2, crossLength), MakeColorTex(crossCol));
        GUI.DrawTexture(new Rect(cx - gap - crossLength, cy, crossLength, 2), MakeColorTex(crossCol));
        GUI.DrawTexture(new Rect(cx + gap, cy, crossLength, 2), MakeColorTex(crossCol));
    }

    private void DrawBorder(Rect r, Color col, int thickness)
    {
        Texture2D t = MakeColorTex(col);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), t);
        GUI.DrawTexture(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), t);
        GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), t);
        GUI.DrawTexture(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), t);
    }

    private void DrawCircleHollow(float cx, float cy, float radius, Color col, float thickness)
    {
        int segments = 24;
        float angleStep = 360f / segments;
        Vector2 prevPoint = new Vector2(cx + radius, cy);

        for (int i = 1; i <= segments; i++)
        {
            float rad = (i * angleStep) * Mathf.Deg2Rad;
            Vector2 nextPoint = new Vector2(cx + Mathf.Cos(rad) * radius, cy + Mathf.Sin(rad) * radius);
            DrawThickScreenLine(prevPoint, nextPoint, col, thickness);
            prevPoint = nextPoint;
        }
    }

    private void DrawThickScreenLine(Vector2 p1, Vector2 p2, Color col, float width)
    {
        Vector2 d = p2 - p1;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        float length = d.magnitude;

        GUIUtility.RotateAroundPivot(angle, p1);
        GUI.DrawTexture(new Rect(p1.x, p1.y - width / 2f, length, width), MakeColorTex(col));
        GUIUtility.RotateAroundPivot(-angle, p1);
    }

    private void DrawLevelTransitionBanner(int sw, int sh)
    {
        float bannerW = Mathf.Min(sw * 0.88f, 640f);
        float bannerH = 220f;
        float bannerX = (sw - bannerW) / 2f;
        float bannerY = (sh - bannerH) / 2f;

        // Khung nền đen Sci-Fi viền xanh vàng
        GUI.DrawTexture(new Rect(bannerX, bannerY, bannerW, bannerH), MakeColorTex(new Color(0.02f, 0.08f, 0.18f, 0.95f)));
        DrawBorder(new Rect(bannerX, bannerY, bannerW, bannerH), new Color(0.2f, 0.9f, 1f, 1f), 3);
        DrawBorder(new Rect(bannerX + 4, bannerY + 4, bannerW - 8, bannerH - 8), new Color(1f, 0.85f, 0.2f, 0.7f), 1);

        // 1. Tiêu đề Vượt Màn
        GUIStyle titleStyle = new GUIStyle();
        titleStyle.fontSize = 28;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = new Color(1f, 0.9f, 0.2f, 1f);
        titleStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(bannerX, bannerY + 18, bannerW, 36), "🎉 VƯỢT MÀN THÀNH CÔNG! (STAGE CLEAR)", titleStyle);

        // 2. Thông báo hoàn thành
        GUIStyle infoStyle = new GUIStyle();
        infoStyle.fontSize = 17;
        infoStyle.fontStyle = FontStyle.Bold;
        infoStyle.normal.textColor = Color.white;
        infoStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(bannerX, bannerY + 60, bannerW, 26), $"ĐÃ TIÊU DIỆT {totalMonstersPerLevel}/{totalMonstersPerLevel} QUÁI VẬT LEVEL {completedLevel}!", infoStyle);

        // 3. Chuẩn bị vào màn tiếp theo
        GUIStyle nextStyle = new GUIStyle();
        nextStyle.fontSize = 19;
        nextStyle.fontStyle = FontStyle.Bold;
        nextStyle.normal.textColor = new Color(0.2f, 1f, 0.85f, 1f);
        nextStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(bannerX, bannerY + 98, bannerW, 30), $"⚔️ CHUẨN BỊ: {nextLevelTitle}", nextStyle);

        // 4. Đồng hồ đếm ngược 2s
        GUIStyle timerStyle = new GUIStyle();
        timerStyle.fontSize = 22;
        timerStyle.fontStyle = FontStyle.Bold;
        timerStyle.normal.textColor = new Color(1f, 0.45f, 0.2f, 1f);
        timerStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(bannerX, bannerY + 138, bannerW, 30), $"⏱️ NGHỈ NGƠI VÀ BẮT ĐẦU TRONG: {Mathf.CeilToInt(levelTransitionTimer)}s...", timerStyle);

        // 5. Thưởng vượt màn
        GUIStyle rewardStyle = new GUIStyle();
        rewardStyle.fontSize = 14;
        rewardStyle.fontStyle = FontStyle.Bold;
        rewardStyle.normal.textColor = new Color(0.2f, 1f, 0.4f, 0.95f);
        rewardStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(bannerX, bannerY + 178, bannerW, 24), "❤️ ĐÃ HỒI PHỤC +50 HP & THƯỞNG +500 ĐIỂM!", rewardStyle);
    }

    private void DrawVictoryBanner(int sw, int sh)
    {
        float bannerW = Mathf.Min(sw * 0.90f, 680f);
        float bannerH = 220f;
        float bannerX = (sw - bannerW) / 2f;
        float bannerY = (sh - bannerH) / 2f;

        GUI.DrawTexture(new Rect(bannerX, bannerY, bannerW, bannerH), MakeColorTex(new Color(0.08f, 0.02f, 0.15f, 0.96f)));
        DrawBorder(new Rect(bannerX, bannerY, bannerW, bannerH), new Color(1f, 0.85f, 0.2f, 1f), 4);

        GUIStyle vicTitle = new GUIStyle();
        vicTitle.fontSize = 32;
        vicTitle.fontStyle = FontStyle.Bold;
        vicTitle.normal.textColor = new Color(1f, 0.88f, 0.2f, 1f);
        vicTitle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(bannerX, bannerY + 24, bannerW, 40), "🏆 CHIẾN THẮNG TOÀN DIỆN! 🏆", vicTitle);

        GUIStyle vicSub = new GUIStyle();
        vicSub.fontSize = 20;
        vicSub.fontStyle = FontStyle.Bold;
        vicSub.normal.textColor = Color.white;
        vicSub.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(bannerX, bannerY + 80, bannerW, 30), "BẠN ĐÃ TIÊU DIỆT TRÙM CUỐI VÀ HOÀN THÀNH 5 LEVEL!", vicSub);

        GUIStyle vicScore = new GUIStyle();
        vicScore.fontSize = 22;
        vicScore.fontStyle = FontStyle.Bold;
        vicScore.normal.textColor = new Color(0.2f, 1f, 0.5f, 1f);
        vicScore.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(bannerX, bannerY + 128, bannerW, 30), $"TỔNG ĐIỂM CHIẾN DỊCH: {currentScore} ĐIỂM", vicScore);
    }

    private float GetSpawnIntervalForLevel(int lvl)
    {
        switch (lvl)
        {
            case 1: return 5.0f;
            case 2: return 4.0f;
            case 3: return 3.0f;
            case 4: return 2.0f;
            case 5: return 1.0f;
            default: return 5.0f;
        }
    }
}
