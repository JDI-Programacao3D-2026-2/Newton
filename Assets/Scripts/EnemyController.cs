using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Combat
{
    // vida, tiro e visão do inimigo. o comportamento (patrulha, perseguição etc.) fica no EnemyFSM
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(EnemyFSM))]
    public class EnemyController : MonoBehaviour, IDamageable
    {
        [Header("Vida")]
        [SerializeField, Min(1f)] private float vidaMaxima = 50f;

        [Header("Disparo")]
        [SerializeField] private Projectile prefabProjetil;
        [Tooltip("Ponto de saída do projétil")]
        [SerializeField] private Transform bocaDoCano;
        [SerializeField, Min(0f)] private float alcanceAtaque = 10f;
        [SerializeField, Min(0f)] private float dano = 10f;
        [SerializeField, Min(0.01f)] private float velocidadeProjetil = 30f;
        [SerializeField, Min(0.01f)] private float tempoVidaProjetil = 4f;
        [SerializeField, Min(0.01f)] private float intervaloAtaque = 1.5f;

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

        private Transform jogador; // o player da cena (achado pela tag)

        // quantos inimigos vivos e quantos já morreram (placar do Tab e vitória do tutorial)
        private static int _alive;
        private static int _killed;
        public static int Abates => _killed;
        public static int Vivos => _alive;
        public static bool AllDefeated => _alive == 0 && _killed > 0;

        private NavMeshAgent _agent;
        private readonly Queue<Projectile> _pooledProjectiles = new Queue<Projectile>();
        private readonly RaycastHit[] _sightHits = new RaycastHit[16];
        private float _currentHealth;
        private float _nextAttackTime;

        // atordoado não anda nem ataca
        private float _atordoadoAte;
        private Renderer _corpo;
        private MaterialPropertyBlock _tinta;

        // avisa quando leva dano (o FSM usa pra ir ver de onde veio o tiro)
        public event System.Action Damaged;

        public bool CorpoACorpo => corpoACorpo;
        public bool Atordoado => Time.time < _atordoadoAte;
        public bool IsDead => _currentHealth <= 0f;
        public float VidaFracao => _currentHealth / vidaMaxima;

        // o ataque nunca passa da distância de visão
        public float AlcanceEfetivo => Mathf.Min(alcanceAtaque, distanciaVisao);

        // se o player ainda não existia (ou foi recriado), procura de novo pela tag
        public Transform Jogador
        {
            get
            {
                if (jogador == null)
                {
                    GameObject achado = GameObject.FindGameObjectWithTag("Player");
                    jogador = achado != null ? achado.transform : null;
                }

                return jogador;
            }
        }

        private void Awake()
        {
            if (_alive == 0) _killed = 0; // fase nova, zera a contagem
            _alive++;

            _agent = GetComponent<NavMeshAgent>();
            _currentHealth = vidaMaxima;

            // quem move o inimigo é o NavMeshAgent, a física não pode empurrar
            if (TryGetComponent(out Rigidbody body))
                body.isKinematic = true;

            if (corpoACorpo) return; // espadachim não usa projétil

            if (prefabProjetil == null || bocaDoCano == null)
            {
                Debug.LogError($"{nameof(EnemyController)}: Projectile Prefab ou Muzzle não atribuídos em '{name}'.", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < tamanhoPool; i++)
                ReturnProjectileToPool(CreateProjectile());
        }

        private void OnDestroy()
        {
            _alive--;

            // os projéteis do pool não são filhos do inimigo, então apaga junto
            foreach (Projectile projectile in _pooledProjectiles)
                if (projectile != null) Destroy(projectile.gameObject);
        }

        public void Atordoar(float segundos)
        {
            if (IsDead) return;

            _atordoadoAte = Mathf.Max(_atordoadoAte, Time.time + segundos);
            if (_agent.isOnNavMesh) _agent.isStopped = true;

            Pintar(new Color(1f, 0.9f, 0.1f)); // fica amarelo enquanto durar

            CancelInvoke(nameof(RestaurarCor));
            Invoke(nameof(RestaurarCor), _atordoadoAte - Time.time);
        }

        // troca a cor sem mexer no material, cada inimigo tem a sua
        private void Pintar(Color cor)
        {
            _corpo ??= GetComponent<Renderer>();
            _tinta ??= new MaterialPropertyBlock();
            _tinta.SetColor("_BaseColor", cor);
            _corpo.SetPropertyBlock(_tinta);
        }

        // volta pra cor normal (se ainda tiver atordoado, volta pro amarelo)
        private void RestaurarCor()
        {
            if (!Atordoado)
            {
                _corpo.SetPropertyBlock(null);
                return;
            }

            Pintar(new Color(1f, 0.9f, 0.1f));
            CancelInvoke(nameof(RestaurarCor));
            Invoke(nameof(RestaurarCor), _atordoadoAte - Time.time);
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

            // pisca branco rapidinho pra mostrar que acertou
            Pintar(Color.white);
            CancelInvoke(nameof(RestaurarCor));
            Invoke(nameof(RestaurarCor), 0.1f);

            if (_currentHealth <= 0f)
                Die();
        }

        private void Die()
        {
            _killed++;
            if (_agent.isOnNavMesh) _agent.isStopped = true;   // na corda do helicóptero o agente ainda tá desligado
            Destroy(gameObject);
        }

        // vê o player se ele tá no alcance, dentro do cone (ou bem perto) e sem parede no meio
        public bool CanSeePlayer()
        {
            // fumaça no meio (ou em cima de um dos dois): não vê
            if (Habilidades.BloqueiaVisao(transform.position, jogador.position)) return false;

            Vector3 toPlayer = jogador.position - transform.position;
            if (toPlayer.sqrMagnitude > distanciaVisao * distanciaVisao) return false;

            Vector3 flat = toPlayer;
            flat.y = 0f;
            bool muitoPerto = flat.sqrMagnitude <= raioPercepcao * raioPercepcao;
            if (!muitoPerto && Vector3.Angle(transform.forward, flat) > campoDeVisao * 0.5f) return false;

            return HasLineOfSight(transform.position, jogador.position) ||
                   HasLineOfSight(transform.position, jogador.position + Vector3.up * 0.8f);
        }

        // confere se tem linha livre do cano até o player
        public bool HasLineOfSightToPlayer()
        {
            Vector3 origin = bocaDoCano.position;
            if (Habilidades.BloqueiaVisao(origin, jogador.position)) return false;

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

        // linha livre entre dois pontos (ignora o próprio inimigo, o player e os outros inimigos)
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

        // gira o corpo pro alvo (o cano é filho dele)
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

            // espada: desce a lâmina e no meio do golpe o dano vale, se o player ainda estiver perto
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

            // uma folguinha, o player pode ter andado durante o golpe
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

            // tiro que ainda tava voando quando o dono morreu, não tem pool pra voltar
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
