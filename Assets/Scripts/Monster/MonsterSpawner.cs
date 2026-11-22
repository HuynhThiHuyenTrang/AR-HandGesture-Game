using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bộ quản lý sinh quái vật tự động theo Cấp độ (Level 1 đến Level 5)
/// Quái vật được sinh ra đúng vị trí trước mắt người chơi (khoảng cách 2.4m, scale 50x tương đương ~1.3m)
/// Dễ dàng nhìn thấy, chém trúng và tương tác trực quan.
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    public static MonsterSpawner Instance { get; private set; }

    [Header("--- CẤU HÌNH CẤP ĐỘ & THỜI GIAN SPAWN ---")]
    [Tooltip("Cấp độ hiện tại của ván chơi (1 đến 5)")]
    [Range(1, 5)]
    [SerializeField] private int currentLevel = 1;

    [Tooltip("Khoảng cách giữa các lần sinh quái (giây) - Level 1: 5s, Level 2: 4s, Level 3: 3s, Level 4: 2s, Level 5: 1s")]
    [SerializeField] private float spawnInterval = 5.0f;

    [Tooltip("Số lượng quái vật tối đa được phép tồn tại đồng thời trên màn hình")]
    [SerializeField] private int maxMonstersAlive = 3;

    [Tooltip("Bật / Tắt chế độ tự động sinh quái vật")]
    [SerializeField] private bool autoSpawn = true;

    [Header("--- THIẾT LẬP VỊ TRÍ XUẤT HIỆN ---")]
    [Tooltip("Khoảng cách quái xuất hiện cách người chơi (mét)")]
    [SerializeField] private float spawnDistance = 2.4f;

    [Tooltip("Góc quét ngẫu nhiên trước mặt người chơi (độ)")]
    [SerializeField] private float spawnAngleSpread = 25.0f;

    [Header("--- THAM CHIẾU PREFAB & MODEL ---")]
    [Tooltip("Prefab quái vật Slime (Mặt đất)")]
    [SerializeField] private GameObject monsterPrefab;

    [Tooltip("Prefab quái vật Dơi Tím Bat (Bay trên không từ Level 2+)")]
    [SerializeField] private GameObject batPrefab;

    [Tooltip("Tỷ lệ kích thước của quái vật (Scale: 50x tương đương ~1.3m người thật)")]
    [SerializeField] private float monsterScaleMultiplier = 50.0f;

    [Header("--- THIẾT LẬP TRÁI TIM HỒI MÁU ---")]
    [Tooltip("Khoảng thời gian tự động sinh Trái tim hồi máu trước mặt người chơi (giây)")]
    [SerializeField] private float heartSpawnInterval = 8.0f;
    private float heartSpawnTimer = 0f;

    private List<GameObject> activeMonsters = new List<GameObject>();
    private float spawnTimer = 0f;
    private int monstersSpawnedInCurrentLevel = 0;
    private bool hasSpawnedBossInLevel5 = false;
    private float pauseSpawnTimer = 0f;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    void Start()
    {
        UpdateSpawnIntervalForLevel(currentLevel);

        // Sinh ngay quái vật đầu tiên sau 0.2s để người chơi thấy quái vật ngay lập tức khi bắt đầu game
        StartCoroutine(InitialSpawnDelay());

        // Sinh trái tim hồi máu đầu tiên ngay sau 0.5s để người chơi có thể test hút tim lập tức
        StartCoroutine(InitialHeartDelay());
    }

    private IEnumerator InitialSpawnDelay()
    {
        yield return new WaitForSeconds(0.2f);
        if (autoSpawn && activeMonsters.Count < maxMonstersAlive)
        {
            SpawnOneMonster();
        }
    }

    private IEnumerator InitialHeartDelay()
    {
        yield return new WaitForSeconds(0.5f);
        SpawnCollectibleHeart();
    }

    void Update()
    {
        activeMonsters.RemoveAll(m => m == null || (m.TryGetComponent<MonsterController>(out var mc) && mc.IsDead()));

        // Đang trong thời gian nghỉ ngơi 2 giây giữa các màn (Level Transition Pause)
        if (pauseSpawnTimer > 0f)
        {
            pauseSpawnTimer -= Time.deltaTime;
            return;
        }

        if (!autoSpawn)
            return;

        // 1. Quản lý sinh quái vật theo chu kỳ cấp độ
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;
            if (activeMonsters.Count < maxMonstersAlive)
            {
                SpawnOneMonster();
            }
        }

        // 2. Quản lý sinh trái tim hồi máu định kỳ trong lúc chiến đấu
        heartSpawnTimer += Time.deltaTime;
        if (heartSpawnTimer >= heartSpawnInterval)
        {
            heartSpawnTimer = 0f;
            SpawnCollectibleHeart();
        }
    }

    /// <summary>
    /// Tạm dừng sinh quái vật trong khoảng thời gian nghỉ giữa 2 màn (2s - 2.5s)
    /// </summary>
    public void PauseSpawningForSeconds(float duration)
    {
        pauseSpawnTimer = duration;
        // Dọn dẹp quái vật cũ của màn trước để người chơi nghỉ ngơi
        foreach (var m in activeMonsters)
        {
            if (m != null)
            {
                Destroy(m);
            }
        }
        activeMonsters.Clear();
    }

    /// <summary>
    /// Sinh ra 1 Trái Tim Hồi Máu 3D lơ lửng ngay trước mặt người chơi
    /// </summary>
    public GameObject SpawnCollectibleHeart()
    {
        Transform camTransform = Camera.main != null ? Camera.main.transform : transform;
        Vector3 forward = camTransform.forward;
        forward.y = 0;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = camTransform.right;
        right.y = 0;
        right.Normalize();

        // Tọa độ xuất hiện lơ lửng trước mặt 2.0m, lệch nhẹ sang trái/phải ngẫu nhiên
        Vector3 heartPos = camTransform.position + forward * 2.0f + right * Random.Range(-0.65f, 0.65f);
        heartPos.y = camTransform.position.y - 0.05f + Random.Range(-0.08f, 0.15f);

        GameObject heart = CollectibleHeart.SpawnHeart(heartPos);
        Debug.Log($"<color=magenta><b>[MonsterSpawner]</b> ❤️ Đã sinh Trái Tim Hồi Máu tại {heartPos}!</color>");
        return heart;
    }

    /// <summary>
    /// Sinh ra 1 quái vật đứng ngay trước mặt người chơi ở tầm ngắm chuẩn
    /// </summary>
    public GameObject SpawnOneMonster()
    {
        if (monsterPrefab == null)
        {
            Debug.LogError("[MonsterSpawner] Chưa gán monsterPrefab trong Inspector!");
            return null;
        }

        Transform camTransform = Camera.main != null ? Camera.main.transform : transform;

        GameObject chosenPrefab = monsterPrefab;
        bool isFlying = false;
        if (currentLevel >= 2 && batPrefab != null && Random.value > 0.45f)
        {
            chosenPrefab = batPrefab;
            isFlying = true;
        }

        // Góc ngẫu nhiên phía trước mặt
        float randomAngle = Random.Range(-spawnAngleSpread, spawnAngleSpread);
        Quaternion rotation = Quaternion.AngleAxis(randomAngle, Vector3.up);
        Vector3 forwardDirection = rotation * camTransform.forward;
        forwardDirection.y = 0;
        forwardDirection.Normalize();

        // Tọa độ xuất hiện: cách người chơi spawnDistance mét
        Vector3 spawnPosition = camTransform.position + forwardDirection * spawnDistance;

        // Slime đứng trước mặt ở tầm ngực/bụng; Bat bay ở tầm mắt/đầu
        float spawnY = camTransform.position.y - 0.20f;
        if (isFlying)
        {
            spawnY = camTransform.position.y + 0.35f;
        }
        spawnPosition.y = spawnY;

        // Quái vật quay mặt thẳng về phía người chơi
        Quaternion lookRotation = Quaternion.LookRotation(camTransform.position - spawnPosition);
        lookRotation.x = 0;
        lookRotation.z = 0;

        GameObject monster = Instantiate(chosenPrefab, spawnPosition, lookRotation);
        string monsterType = isFlying ? "Bat" : "Slime";
        monster.name = $"Monster_{monsterType}_{++monstersSpawnedInCurrentLevel}";

        // Phóng to kích thước quái vật để to lớn, rõ nét và dễ chém
        monster.transform.localScale = Vector3.one * monsterScaleMultiplier;

        // Thiết lập sức mạnh và độ khó quái vật theo cấp độ
        MonsterController mc = monster.GetComponent<MonsterController>();
        if (mc != null)
        {
            bool isBossSpawn = (currentLevel == 5 && !hasSpawnedBossInLevel5);
            if (isBossSpawn)
            {
                hasSpawnedBossInLevel5 = true;
                monster.name = "👑_BOSS_SLIME_CHÚA";
                monster.transform.localScale = Vector3.one * 75.0f; // Boss khổng lồ 75x
                mc.SetupMonsterLevel(5, true);
            }
            else
            {
                mc.SetupMonsterLevel(currentLevel, false);
            }
        }

        activeMonsters.Add(monster);
        Debug.Log($"<color=green><b>[MonsterSpawner]</b> Đã sinh quái vật {monster.name} tại {spawnPosition} (Level: {currentLevel}, Hiện có: {activeMonsters.Count}/{maxMonstersAlive} quái)!</color>");
        return monster;
    }

    public void SetLevel(int level)
    {
        currentLevel = Mathf.Clamp(level, 1, 5);
        hasSpawnedBossInLevel5 = false;
        UpdateSpawnIntervalForLevel(currentLevel);
        Debug.Log($"<color=cyan><b>[MonsterSpawner]</b> Chuyển sang Level {currentLevel}! Tần suất spawn: {spawnInterval}s/quái</color>");
    }

    private void UpdateSpawnIntervalForLevel(int level)
    {
        switch (level)
        {
            case 1: 
                spawnInterval = 5.0f; 
                maxMonstersAlive = 2;
                break;
            case 2: 
                spawnInterval = 4.0f; 
                maxMonstersAlive = 3;
                break;
            case 3: 
                spawnInterval = 3.0f; 
                maxMonstersAlive = 3;
                break;
            case 4: 
                spawnInterval = 2.0f; 
                maxMonstersAlive = 4;
                break;
            case 5: 
                spawnInterval = 2.5f; 
                maxMonstersAlive = 3;
                break;
        }
    }

    public int GetAliveMonsterCount()
    {
        activeMonsters.RemoveAll(m => m == null || (m.TryGetComponent<MonsterController>(out var mc) && mc.IsDead()));
        return activeMonsters.Count;
    }

    public int GetCurrentLevel() => currentLevel;
}
