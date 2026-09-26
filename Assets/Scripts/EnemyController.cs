using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Combat
{
    /// Inimigo simples: persegue o player via NavMeshAgent (igual ao EnemyNavMesh original) e dispara
    /// Projectile quando dentro do alcance. Vida e IA num único script por simplicidade — separe em
    /// EnemyHealth/EnemyAI se este componente crescer muito ou se a vida precisar ser reutilizada fora
    /// de um inimigo com IA.
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyController : MonoBehaviour, IDamageable
    {
        [Header("Vida")]
        [SerializeField, Min(1f)] private float maxHealth = 50f;

        [Header("Alvo")]
        [SerializeField] private Transform player;

        [Header("Disparo")]
        [SerializeField] private Projectile projectilePrefab;
        [Tooltip("Ponto de saída do projétil. Usado só como POSIÇÃO de spawn — a direção do tiro é sempre calculada até o player.")]
        [SerializeField] private Transform muzzle;
        [SerializeField, Min(0f)] private float attackRange = 10f;
        [Tooltip("Camadas que bloqueiam a visão (paredes, obstáculos). Sem isso, o inimigo atira mesmo sem enxergar o player.")]
        [SerializeField] private LayerMask obstacleMask;
        [SerializeField, Min(0f)] private float damage = 10f;
        [SerializeField, Min(0.01f)] private float projectileSpeed = 30f;
        [SerializeField, Min(0.01f)] private float projectileLifetime = 4f;
        [SerializeField, Min(0.01f)] private float attackCooldown = 1.5f;

        private NavMeshAgent _agent;
        private readonly Queue<Projectile> _pooledProjectiles = new Queue<Projectile>();
        private float _currentHealth;
        private float _nextAttackTime;

        public bool IsDead => _currentHealth <= 0f;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _currentHealth = maxHealth;

            if (player == null)
                Debug.LogWarning($"{nameof(EnemyController)}: player não atribuído em '{name}'.", this);

            if (projectilePrefab == null || muzzle == null)
            {
                Debug.LogError($"{nameof(EnemyController)}: Projectile Prefab ou Muzzle não atribuídos em '{name}'.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (IsDead || player == null) return;

            float sqrDistance = (player.position - transform.position).sqrMagnitude;
            bool inRange = sqrDistance <= attackRange * attackRange;

            if (inRange && HasLineOfSightToPlayer())
            {
                // Dentro do alcance E com visão livre: para de perseguir e vira o corpo pro player, já
                // que o NavMeshAgent só rotaciona automaticamente enquanto está seguindo um destino.
                _agent.isStopped = true;
                RotateTowardsPlayer();
                TryFire();
            }
            else
            {
                // Igual ao EnemyNavMesh original: sempre persegue. É o SetDestination todo frame que
                // faz o próprio NavMeshAgent girar o objeto na direção do movimento.
                _agent.isStopped = false;
                _agent.SetDestination(player.position);
            }
        }

        /// Raycast simples entre o inimigo e o player: se algo do obstacleMask estiver no caminho
        /// (uma parede, por exemplo), não há visão livre e ele não deve atirar.
        private bool HasLineOfSightToPlayer()
        {
            Vector3 toPlayer = player.position - transform.position;
            return !Physics.Raycast(transform.position, toPlayer.normalized, toPlayer.magnitude, obstacleMask);
        }

        /// Gira o corpo (e o muzzle, que é filho dele) pra encarar o player instantaneamente. Sem isso,
        /// o cano fica apontando pra onde o inimigo parou de se mover, não pra onde o player está — daí
        /// o tiro "saía de lado" mesmo acertando a direção certa matematicamente.
        private void RotateTowardsPlayer()
        {
            Vector3 direction = player.position - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(direction);
        }

        private void TryFire()
        {
            if (Time.time < _nextAttackTime) return;

            _nextAttackTime = Time.time + attackCooldown;

            // Igual ao WeaponPlayer: direção = forward do CORPO (transform), não do muzzle. O muzzle
            // é só posição de spawn — seu mesh/rotação pode estar torto (ex: cilindro deitado) sem
            // afetar pra onde o tiro vai. RotateTowardsPlayer() já girou o corpo pro alvo antes disso.
            Vector3 direction = transform.forward;

            Projectile projectile = GetPooledProjectile();
            projectile.transform.SetPositionAndRotation(muzzle.position, Quaternion.LookRotation(direction));
            projectile.gameObject.SetActive(true);
            projectile.Launch(direction, projectileSpeed, damage, projectileLifetime, ReturnProjectileToPool);
        }

        private Projectile GetPooledProjectile() =>
            _pooledProjectiles.Count > 0 ? _pooledProjectiles.Dequeue() : CreateProjectile();

        private Projectile CreateProjectile()
        {
            Projectile instance = Instantiate(projectilePrefab);
            instance.gameObject.SetActive(false);
            return instance;
        }

        private void ReturnProjectileToPool(Projectile projectile)
        {
            if (projectile == null) return; // pode ter sido destruído (ex: troca de cena)

            projectile.gameObject.SetActive(false);
            _pooledProjectiles.Enqueue(projectile);
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;

            _currentHealth = Mathf.Max(0f, _currentHealth - amount);

            if (_currentHealth <= 0f)
                Die();
        }

        private void Die()
        {
            _agent.isStopped = true;
            // Troque por animação/drop/pool depois; por enquanto remove o objeto.
            Destroy(gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}