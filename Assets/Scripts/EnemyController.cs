using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Game.Combat
{
    /// Vida, disparo e visão do inimigo. Os estados (patrulha, perseguição, cobertura, fuga e cura)
    /// ficam no EnemyFSM.
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(EnemyFSM))]
    public class EnemyController : MonoBehaviour, IDamageable
    {
        [Header("Vida")]
        [FormerlySerializedAs("maxHealth")] [SerializeField, Min(1f)] private float vidaMaxima = 50f;

        [Header("Disparo")]
        [FormerlySerializedAs("projectilePrefab")] [SerializeField] private Projectile prefabProjetil;
        [Tooltip("Ponto de saída do projétil")]
        [FormerlySerializedAs("muzzle")] [SerializeField] private Transform bocaDoCano;
        [FormerlySerializedAs("attackRange")] [SerializeField, Min(0f)] private float alcanceAtaque = 10f;
        [FormerlySerializedAs("damage")] [SerializeField, Min(0f)] private float dano = 10f;
        [FormerlySerializedAs("projectileSpeed")] [SerializeField, Min(0.01f)] private float velocidadeProjetil = 30f;
        [FormerlySerializedAs("projectileLifetime")] [SerializeField, Min(0.01f)] private float tempoVidaProjetil = 4f;
        [FormerlySerializedAs("attackCooldown")] [SerializeField, Min(0.01f)] private float intervaloAtaque = 1.5f;

        [Header("Corpo a corpo")]
        [Tooltip("Ataca de perto com a espada, sem projétil. O alcance é o 'Alcance Ataque' acima.")]
        [SerializeField] private bool corpoACorpo;
        [Tooltip("Animação da espada descendo (componente Animation no objeto Espada)")]
        [SerializeField] private Animation animacaoEspada;
        [Tooltip("Tempo entre começar o golpe e o dano valer (dá chance de desviar)")]
        [SerializeField, Min(0f)] private float atrasoGolpe = 0.2f;

        [Header("Pool de projéteis")]
        [Tooltip("Projéteis criados já no início, pra não dar Instantiate no meio da partida")]
        [SerializeField, Min(0)] private int tamanhoPool = 5;

        [Header("Visão")]
        [Tooltip("Ângulo total do cone de visão, em graus")]
        [SerializeField, Range(10f, 360f)] private float campoDeVisao = 110f;
        [Tooltip("Distância máxima de visão")]
        [SerializeField, Min(1f)] private float distanciaVisao = 10f;
        [Tooltip("Dentro desse raio ele percebe o jogador mesmo fora do cone")]
        [SerializeField, Min(0f)] private float raioPercepcao = 3f;

        private const string PlayerTag = "Player";

        private Transform jogador; // o Player da cena, achado pela tag

        // inimigos vivos e derrotados, pra detectar a vitória
        private static int _alive;
        private static int _killed;
        private bool _counted;

        // todos os inimigos foram derrotados
        public static bool AllDefeated => _alive == 0 && _killed > 0;

        // placar (tecla Tab)
        public static int Abates => _killed;
        public static int Vivos => _alive;

        public bool CorpoACorpo => corpoACorpo;

        // atordoado: não anda, não ataca (o FSM e o disparo conferem isso)
        private float _atordoadoAte;
        private Renderer _corpo;
        private MaterialPropertyBlock _tinta;
        public bool Atordoado => Time.time < _atordoadoAte;

        public void Atordoar(float segundos)
        {
            if (IsDead) return;

            _atordoadoAte = Mathf.Max(_atordoadoAte, Time.time + segundos);
            _agent.isStopped = true;

            // fica amarelo enquanto dura
            _corpo ??= GetComponent<Renderer>();
            if (_corpo != null)
            {
                _tinta ??= new MaterialPropertyBlock();
                _tinta.SetColor("_BaseColor", new Color(1f, 0.9f, 0.1f));
                _corpo.SetPropertyBlock(_tinta);
            }

            CancelInvoke(nameof(FimAtordoamento));
            Invoke(nameof(FimAtordoamento), _atordoadoAte - Time.time);
        }

        private void FimAtordoamento()
        {
            if (_corpo != null) _corpo.SetPropertyBlock(null);
        }

        private NavMeshAgent _agent;
        private readonly Queue<Projectile> _pooledProjectiles = new Queue<Projectile>();
        private readonly RaycastHit[] _sightHits = new RaycastHit[16];
        private float _currentHealth;
        private float _nextAttackTime;

        /// Disparado quando o inimigo leva dano (o FSM usa pra investigar de onde veio o tiro).
        public event System.Action Damaged;

        // Se o jogador ainda não existia (ou foi destruído e recriado), procura de novo pela tag
        public Transform Jogador
        {
            get
            {
                if (jogador == null) jogador = FindPlayerByTag();
                return jogador;
            }
        }
        public bool IsDead => _currentHealth <= 0f;
        public float VidaFracao => _currentHealth / vidaMaxima;

        // O ataque nunca passa da distância de visão
        public float AlcanceEfetivo => Mathf.Min(alcanceAtaque, distanciaVisao);

        private void Awake()
        {
            if (_alive == 0) _killed = 0; // fase nova: zera a contagem
            _alive++;
            _counted = true;

            _agent = GetComponent<NavMeshAgent>();

            // Kinematic: o NavMeshAgent move o inimigo, a física não deve empurrá-lo
            if (TryGetComponent(out Rigidbody body))
                body.isKinematic = true;
            _currentHealth = vidaMaxima;

            jogador = FindPlayerByTag();

            if (jogador == null)
                Debug.LogWarning($"{nameof(EnemyController)}: nenhum objeto com a tag \"Player\" na cena ('{name}').", this);

            if (!corpoACorpo && (prefabProjetil == null || bocaDoCano == null))
            {
                Debug.LogError($"{nameof(EnemyController)}: Projectile Prefab ou Muzzle não atribuídos em '{name}'.", this);
                enabled = false;
            }
            else if (!corpoACorpo)
            {
                for (int i = 0; i < tamanhoPool; i++)
                    ReturnProjectileToPool(CreateProjectile());
            }

            // Prefab antigo sem o FSM: adiciona
            if (!TryGetComponent<EnemyFSM>(out _))
                gameObject.AddComponent<EnemyFSM>();
        }

        private static Transform FindPlayerByTag()
        {
            try
            {
                GameObject found = GameObject.FindGameObjectWithTag(PlayerTag);
                return found != null ? found.transform : null;
            }
            catch (UnityException)
            {
                return null; // tag inexistente
            }
        }

        private void OnDestroy()
        {
            if (_counted)
            {
                _counted = false;
                _alive--;
            }

            // os projéteis do pool não são filhos do inimigo: limpa pra não ficarem soltos na cena
            foreach (Projectile projectile in _pooledProjectiles)
                if (projectile != null) Destroy(projectile.gameObject);
            _pooledProjectiles.Clear();
        }

        public void Curar(float quantidade)
        {
            _currentHealth = Mathf.Min(vidaMaxima, _currentHealth + quantidade);
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;

            _currentHealth = Mathf.Max(0f, _currentHealth - amount);
            Damaged?.Invoke();

            if (_currentHealth <= 0f)
                Die();
        }

        private void Die()
        {
            _killed++;
            _agent.isStopped = true;
            Destroy(gameObject);
        }

        // Vê o jogador se ele está no alcance, dentro do cone (ou muito perto) e sem parede no caminho
        public bool CanSeePlayer()
        {
            Vector3 toPlayer = jogador.position - transform.position;
            if (toPlayer.sqrMagnitude > distanciaVisao * distanciaVisao) return false;

            Vector3 flat = toPlayer;
            flat.y = 0f;
            bool muitoPerto = flat.sqrMagnitude <= raioPercepcao * raioPercepcao;
            if (!muitoPerto && Vector3.Angle(transform.forward, flat) > campoDeVisao * 0.5f) return false;

            return HasLineOfSight(transform.position, jogador.position) ||
                   HasLineOfSight(transform.position, jogador.position + Vector3.up * 0.8f);
        }

        // Verifica se há linha livre da boca do cano até o jogador
        public bool HasLineOfSightToPlayer()
        {
            Vector3 origin = bocaDoCano.position;
            Vector3 offset = jogador.position - origin;
            int count = Physics.SphereCastNonAlloc(origin, 0.2f, offset.normalized, _sightHits, offset.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform hitTransform = _sightHits[i].collider.transform;
                if (hitTransform.IsChildOf(transform) || hitTransform.IsChildOf(jogador)) continue;
                return false;
            }

            return true;
        }

        // Linha de visão livre entre dois pontos (ignora o próprio inimigo, o jogador e outros inimigos)
        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 offset = to - from;
            int count = Physics.RaycastNonAlloc(from, offset.normalized, _sightHits, offset.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform hitTransform = _sightHits[i].collider.transform;
                if (hitTransform.IsChildOf(transform) || hitTransform.IsChildOf(jogador)) continue;
                if (hitTransform.GetComponentInParent<EnemyController>() != null) continue;
                return false;
            }

            return true;
        }

        // Gira o corpo para o alvo (o cano é filho dele)
        public void RotateTowards(Vector3 alvo)
        {
            Vector3 direction = alvo - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(direction);
        }

        public void TryFire()
        {
            if (Atordoado || Time.time < _nextAttackTime) return;

            _nextAttackTime = Time.time + intervaloAtaque;

            // espada: a lâmina desce e, no meio do golpe, o dano vale se o jogador ainda estiver perto
            if (corpoACorpo)
            {
                if (animacaoEspada != null) animacaoEspada.Play();
                Invoke(nameof(AplicarGolpe), atrasoGolpe);
                return;
            }

            Vector3 direction = (jogador.position - bocaDoCano.position).normalized;

            Projectile projectile = GetPooledProjectile();
            projectile.transform.SetPositionAndRotation(bocaDoCano.position, Quaternion.LookRotation(direction));
            projectile.gameObject.SetActive(true);
            projectile.Launch(direction, velocidadeProjetil, dano, tempoVidaProjetil, ReturnProjectileToPool);
        }

        private void AplicarGolpe()
        {
            if (IsDead || Atordoado || jogador == null) return;

            // um pouco de folga: o jogador pode ter andado durante o golpe
            float alcance = AlcanceEfetivo * 1.3f;
            if ((jogador.position - transform.position).sqrMagnitude > alcance * alcance) return;

            if (jogador.TryGetComponent(out IDamageable alvo))
                alvo.TakeDamage(dano);
        }

        private Projectile GetPooledProjectile() =>
            _pooledProjectiles.Count > 0 ? _pooledProjectiles.Dequeue() : CreateProjectile();

        private Projectile CreateProjectile()
        {
            Projectile instance = Instantiate(prefabProjetil);
            instance.gameObject.SetActive(false);
            return instance;
        }

        private void ReturnProjectileToPool(Projectile projectile)
        {
            if (projectile == null) return;

            // tiro que ainda voava quando o dono morreu: não tem pool pra voltar
            if (this == null)
            {
                Destroy(projectile.gameObject);
                return;
            }

            projectile.gameObject.SetActive(false);
            _pooledProjectiles.Enqueue(projectile);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, AlcanceEfetivo);

            // cone de visão e raio de percepção
            Gizmos.color = Color.yellow;
            Vector3 esq = Quaternion.Euler(0f, -campoDeVisao * 0.5f, 0f) * transform.forward;
            Vector3 dir = Quaternion.Euler(0f, campoDeVisao * 0.5f, 0f) * transform.forward;
            Gizmos.DrawLine(transform.position, transform.position + esq * distanciaVisao);
            Gizmos.DrawLine(transform.position, transform.position + dir * distanciaVisao);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, raioPercepcao);
        }
    }
}
