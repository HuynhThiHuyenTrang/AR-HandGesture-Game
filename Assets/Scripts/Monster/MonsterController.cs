using System.Collections;
using UnityEngine;

/// <summary>
/// Các trạng thái hành vi của quái vật (Finite State Machine - FSM)
/// </summary>
public enum MonsterState
{
    Idle,           // Đứng quan sát
    Chase,          // Di chuyển tiếp cận người chơi
    Telegraph,      // Báo hiệu chuẩn bị tấn công (1.5s để người chơi giơ khiên hoặc chém)
    Attack,         // Tung đòn gây sát thương
    Cooldown,       // Hồi chiêu sau đòn đánh
    Hit,            // Bị chém trúng, khựng lại
    Die             // Bị tiêu diệt, rơi vật phẩm
}

/// <summary>
/// Bộ điều khiển AI quái vật: Tiếp cận, tấn công, nhận đòn chém từ cử chỉ tay
/// </summary>
[RequireComponent(typeof(Collider))]
public class MonsterController : MonoBehaviour
{
    [Header("--- THÔNG SỐ MÁU & SÁT THƯƠNG ---")]
    [Tooltip("Lượng máu tối đa của quái vật")]
    [SerializeField] private int maxHealth = 100;
    private int currentHealth;

    [Tooltip("Sát thương gây ra cho người chơi mỗi đòn đánh")]
    [SerializeField] private int attackDamage = 15;

    [Header("--- THÔNG SỐ DI CHUYỂN & KHOẢNG CÁCH ---")]
    [Tooltip("Tốc độ di chuyển về phía người chơi (m/s)")]
    [SerializeField] private float moveSpeed = 0.85f;

    [Tooltip("Khoảng cách tối thiểu để bắt đầu tung đòn đánh (mét)")]
    [SerializeField] private float attackRange = 1.25f;

    [Tooltip("Tốc độ xoay mặt về phía người chơi")]
    [SerializeField] private float rotationSpeed = 5.0f;

    [Header("--- THÔNG SỐ THỜI GIAN & HỒI CHIÊU (TIMING) ---")]
    [Tooltip("Thời gian báo hiệu chuẩn bị đánh (giây)")]
    [SerializeField] private float telegraphDuration = 1.5f;

    [Tooltip("Thời gian chờ hồi chiêu giữa 2 lần tấn công (giây)")]
    [SerializeField] private float attackCooldown = 3.0f;

    [Tooltip("Thời gian chờ trước khi xóa xác sau khi chết (giây)")]
    [SerializeField] private float despawnDelay = 1.8f;

    [Header("--- THAM CHIẾU COMPONENT ---")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform playerTarget;

    [Header("--- DEBUG TRẠNG THÁI HIỆN TẠI ---")]
    [SerializeField] private MonsterState currentState = MonsterState.Idle;

    private float stateTimer = 0f;
    private Collider monsterCollider;
    private bool isDead = false;
    private bool isStunned = false;
    private float stunTimer = 0f;
    private bool isBoss = false;

    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimAttack = Animator.StringToHash("Attack");
    private static readonly int AnimHit = Animator.StringToHash("Hit");
    private static readonly int AnimDie = Animator.StringToHash("Die");

    void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        monsterCollider = GetComponent<Collider>();
    }

    void Start()
    {
        currentHealth = maxHealth;

        if (playerTarget == null && Camera.main != null)
        {
            playerTarget = Camera.main.transform;
        }

        SetState(MonsterState.Idle);
    }

    void Update()
    {
        if (isDead)
            return;

        if (isStunned)
        {
            stunTimer -= Time.deltaTime;
            if (stunTimer <= 0f)
            {
                isStunned = false;
                SetState(MonsterState.Chase);
            }
            return;
        }

        if (playerTarget == null)
        {
            if (Camera.main != null)
                playerTarget = Camera.main.transform;
            return;
        }

        switch (currentState)
        {
            case MonsterState.Idle:
                HandleIdleState();
                break;

            case MonsterState.Chase:
                HandleChaseState();
                break;

            case MonsterState.Telegraph:
                HandleTelegraphState();
                break;

            case MonsterState.Attack:
                HandleAttackState();
                break;

            case MonsterState.Cooldown:
                HandleCooldownState();
                break;

            case MonsterState.Hit:
                HandleHitState();
                break;

            case MonsterState.Die:
                break;
        }
    }

    public void SetState(MonsterState newState)
    {
        currentState = newState;
        stateTimer = 0f;

        if (animator != null)
        {
            switch (newState)
            {
                case MonsterState.Idle:
                    animator.SetFloat(AnimSpeed, 0f);
                    break;

                case MonsterState.Chase:
                    animator.SetFloat(AnimSpeed, 1f);
                    break;

                case MonsterState.Telegraph:
                    animator.SetFloat(AnimSpeed, 0f);
                    break;

                case MonsterState.Attack:
                    animator.SetFloat(AnimSpeed, 0f);
                    animator.SetTrigger(AnimAttack);
                    break;

                case MonsterState.Hit:
                    animator.SetTrigger(AnimHit);
                    break;

                case MonsterState.Die:
                    animator.SetTrigger(AnimDie);
                    break;
            }
        }
    }

    private void HandleIdleState()
    {
        stateTimer += Time.deltaTime;
        LookAtPlayer();

        if (stateTimer >= 0.8f)
        {
            SetState(MonsterState.Chase);
        }
    }

    private void HandleChaseState()
    {
        LookAtPlayer();

        Vector3 targetPosition = new Vector3(playerTarget.position.x, transform.position.y, playerTarget.position.z);
        float distance = Vector3.Distance(transform.position, targetPosition);

        if (distance <= attackRange)
        {
            SetState(MonsterState.Telegraph);
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
    }

    private void HandleTelegraphState()
    {
        LookAtPlayer();
        stateTimer += Time.deltaTime;

        if (stateTimer >= telegraphDuration)
        {
            SetState(MonsterState.Attack);
        }
    }

    private void HandleAttackState()
    {
        stateTimer += Time.deltaTime;

        if (stateTimer >= 0.45f && stateTimer - Time.deltaTime < 0.45f)
        {
            ExecuteAttackDamage();
        }

        if (stateTimer >= 0.9f)
        {
            SetState(MonsterState.Cooldown);
        }
    }

    private void HandleCooldownState()
    {
        LookAtPlayer();
        stateTimer += Time.deltaTime;

        if (stateTimer >= attackCooldown)
        {
            Vector3 targetPosition = new Vector3(playerTarget.position.x, transform.position.y, playerTarget.position.z);
            float distance = Vector3.Distance(transform.position, targetPosition);

            if (distance <= attackRange)
                SetState(MonsterState.Telegraph);
            else
                SetState(MonsterState.Chase);
        }
    }

    private void HandleHitState()
    {
        stateTimer += Time.deltaTime;

        // Bị giật lùi nhẹ và khựng lại 0.4s
        if (stateTimer >= 0.4f)
        {
            if (currentHealth > 0)
                SetState(MonsterState.Chase);
        }
    }

    private void ExecuteAttackDamage()
    {
        Debug.Log($"[Monster] Quái {gameObject.name} tấn công gây {attackDamage} sát thương cho Người chơi!");
        if (PlayerCombatController.Instance != null)
        {
            PlayerCombatController.Instance.TakePlayerDamage(attackDamage, this);
        }
    }

    /// <summary>
    /// Làm choáng quái vật khi người chơi giơ khiên phản đòn (Parry) thành công
    /// </summary>
    public void Stun(float duration)
    {
        if (isDead) return;
        isStunned = true;
        stunTimer = duration;
        if (animator != null) animator.SetFloat(AnimSpeed, 0f);
        Debug.Log($"<color=cyan><b>[Monster] ⚡ Quái vật {gameObject.name} BỊ CHOÁNG trong {duration}s! Nhận thêm +50% sát thương!</b></color>");
    }

    /// <summary>
    /// Thiết lập chỉ số sức mạnh của quái vật tương ứng theo từng Cấp độ (Level 1 - 5)
    /// </summary>
    public void SetupMonsterLevel(int level, bool isBoss = false)
    {
        this.isBoss = isBoss;
        if (isBoss)
        {
            maxHealth = 350;
            currentHealth = maxHealth;
            attackDamage = 35;
            moveSpeed = 1.05f;
            telegraphDuration = 1.3f;
            attackCooldown = 2.2f;
            return;
        }

        switch (level)
        {
            case 1:
                maxHealth = 80;
                attackDamage = 12;
                moveSpeed = 0.75f;
                telegraphDuration = 1.6f;
                break;
            case 2:
                maxHealth = 100;
                attackDamage = 18;
                moveSpeed = 1.15f;
                telegraphDuration = 1.4f;
                break;
            case 3:
                maxHealth = 140;
                attackDamage = 25;
                moveSpeed = 1.45f;
                telegraphDuration = 1.1f;
                break;
            case 4:
                maxHealth = 160;
                attackDamage = 30;
                moveSpeed = 1.60f;
                telegraphDuration = 0.95f;
                break;
            case 5:
                maxHealth = 180;
                attackDamage = 35;
                moveSpeed = 1.70f;
                telegraphDuration = 0.85f;
                break;
        }
        currentHealth = maxHealth;
    }

    /// <summary>
    /// Nhận sát thương khi người chơi vung tay chém (Slash)
    /// </summary>
    public void TakeDamage(int damage)
    {
        if (isDead)
            return;

        int finalDamage = isStunned ? Mathf.RoundToInt(damage * 1.5f) : damage;
        currentHealth -= finalDamage;
        Debug.Log($"<color=orange>[Monster] Quái vật {gameObject.name} bị chém trúng! -{finalDamage} HP{(isStunned ? " (CHOÁNG +50%!)" : "")} (Còn: {currentHealth}/{maxHealth})</color>");

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Die();
        }
        else
        {
            // Bị giật lùi ra sau một đoạn ngắn khi bị chém
            if (playerTarget != null)
            {
                Vector3 knockbackDir = (transform.position - playerTarget.position).normalized;
                knockbackDir.y = 0;
                transform.position += knockbackDir * 0.35f;
            }

            SetState(MonsterState.Hit);
        }
    }

    private void Die()
    {
        isDead = true;
        SetState(MonsterState.Die);

        if (monsterCollider != null)
            monsterCollider.enabled = false;

        Debug.Log($"<color=red><b>[Monster] QUÁI VẬT {gameObject.name} ĐÃ BỊ HẠ GỤC!</b></color>");

        // Rơi ra Trái Tim Hồi Máu 3D với tỷ lệ 35% hoặc khi người chơi thấp máu
        bool playerNeedsHealth = PlayerCombatController.Instance != null && PlayerCombatController.Instance.GetCurrentHealth() < 70;
        if (Random.value < 0.35f || playerNeedsHealth)
        {
            CollectibleHeart.SpawnHeart(transform.position + Vector3.up * 0.45f);
        }

        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.OnMonsterKilled(this);
        }

        StartCoroutine(DespawnCoroutine());
    }

    private IEnumerator DespawnCoroutine()
    {
        yield return new WaitForSeconds(despawnDelay);
        Destroy(gameObject);
    }

    private void LookAtPlayer()
    {
        if (playerTarget == null) return;
        Vector3 direction = playerTarget.position - transform.position;
        direction.y = 0;
        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    public int GetCurrentHealth() => currentHealth;
    public int GetMaxHealth() => maxHealth;
    public bool IsBoss() => isBoss;
    public bool IsStunned() => isStunned;
    public bool IsDead() => isDead;
}
