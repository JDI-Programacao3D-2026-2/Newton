using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Game.Combat
{
    /// Inimigo simples: persegue o jogador via NavMeshAgent (igual ao EnemyNavMesh original viu?) e dispara
    /// Projectile quando dentro do alcance. Vida e IA num único script por simplicidade.
    /// 
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyController : MonoBehaviour, IDamageable
    {
        [Header("Vida")]
        [FormerlySerializedAs("maxHealth")] [SerializeField, Min(1f)] private float vidaMaxima = 50f;

        [Header("Alvo")]
        [FormerlySerializedAs("player")] [SerializeField] private Transform jogador;

        [Header("Disparo")]
        [FormerlySerializedAs("projectilePrefab")] [SerializeField] private Projectile prefabProjetil;
        [Tooltip("Ponto de saída do projétil. Usado só como POSIÇÃO de spawn — a direção do tiro é sempre calculada até o jogador.")]
        [FormerlySerializedAs("muzzle")] [SerializeField] private Transform bocaDoCano;
        [FormerlySerializedAs("attackRange")] [SerializeField, Min(0f)] private float alcanceAtaque = 10f;
        [FormerlySerializedAs("damage")] [SerializeField, Min(0f)] private float dano = 10f;
        [FormerlySerializedAs("projectileSpeed")] [SerializeField, Min(0.01f)] private float velocidadeProjetil = 30f;
        [FormerlySerializedAs("projectileLifetime")] [SerializeField, Min(0.01f)] private float tempoVidaProjetil = 4f;
        [FormerlySerializedAs("attackCooldown")] [SerializeField, Min(0.01f)] private float intervaloAtaque = 1.5f;

        [Header("Fuga (FSM)")]
        [Tooltip("Abaixo desta fração da vida máxima o inimigo foge (0.25 = 25%).")]
        [SerializeField, Range(0.05f, 0.9f)] private float percentualVidaFuga = 0.25f;
        [Tooltip("Fração da vida em que ele se considera curado e volta a perseguir.")]
        [SerializeField, Range(0.1f, 1f)] private float percentualVidaRecuperada = 0.7f;
        [SerializeField, Min(0f)] private float curaPorSegundo = 5f;
        [Tooltip("Distância dos pontos candidatos de esconderijo, medida a partir do inimigo.")]
        [SerializeField, Min(1f)] private float raioBuscaEscondijo = 15f;
        [SerializeField, Min(0.1f)] private float intervaloRecalculo = 0.5f;

        [Header("Tática (cobertura)")]
        [Tooltip("Distância máxima, a partir do inimigo, dos pontos candidatos de tiro com cobertura.")]
        [SerializeField, Min(1f)] private float raioBuscaCobertura = 12f;
        [Tooltip("Não escolhe posição mais perto que isso do jogador.")]
        [SerializeField, Min(0f)] private float distanciaMinimaJogador = 5f;
        [Tooltip("Tempo mínimo/máximo (s) numa posição antes de trocar de lugar.")]
        [SerializeField, Min(0.5f)] private float tempoMinNaPosicao = 1.5f;
        [SerializeField, Min(0.5f)] private float tempoMaxNaPosicao = 3.5f;

        public enum EnemyState
        {
            [InspectorName("Perseguir")] Pursuit, // Persegue e ataca o jogador
            [InspectorName("Fugir")] Flee,    // Foge para um ponto fora da visão do jogador
            [InspectorName("Cobertura")] Cover,  // Atira de uma posição tática (meio corpo na quina da parede)
            [InspectorName("Curar")] Heal     // Escondido, recuperando vida
        }

        [Header("Debug")]
        public EnemyState estadoAtual = EnemyState.Pursuit;

        private const string PlayerTag = "Player";

        // Contagem de inimigos vivos, pra detectar a vitória (todos derrotados).
        private static int _alive;
        private static int _killed;
        private bool _counted;

        /// Verdadeiro quando o último inimigo da fase foi derrotado.
        public static bool AllDefeated => _alive == 0 && _killed > 0;

        // Esconderijos já escolhidos por inimigos em fuga, pra dois não irem pro mesmo ponto.
        private static readonly Dictionary<EnemyController, Vector3> HideClaims = new Dictionary<EnemyController, Vector3>();
        private const float HideClaimSpacing = 2.5f;

        private const int HideCandidates = 24;
        private const int HideRings = 4;
        private const float BodyHeight = 1f;
        private readonly RaycastHit[] _sightHits = new RaycastHit[16];

        private NavMeshAgent _agent;
        private readonly Queue<Projectile> _pooledProjectiles = new Queue<Projectile>();
        private float _currentHealth;
        private float _nextAttackTime;
        private float _nextHidePathTime;
        private float _nextCoverRepathTime;
        private float _nextValidateTime;
        private float _blockedSince = -1f;
        private bool _hasCoverSpot;
        private Vector3 _coverSpot;
        private Vector3 _lastCoverSpot = new Vector3(float.MaxValue, 0f, 0f);

        public bool IsDead => _currentHealth <= 0f;
        private bool IsLowHealth => _currentHealth <= vidaMaxima * percentualVidaFuga;

        private void Awake()
        {
            if (_alive == 0) _killed = 0; // fase nova (ex: reiniciada): zera a contagem
            _alive++;
            _counted = true;

            _agent = GetComponent<NavMeshAgent>();

            // Quem move o inimigo é o NavMeshAgent. Com Rigidbody dinâmico, qualquer contato (player
            // esbarrando, projétil) vira impulso físico e o empurra pra longe, brigando com o agent.
            // Kinematic = imune a forças, e o player também para de ser arremessado ao bater nele.
            if (TryGetComponent(out Rigidbody body))
                body.isKinematic = true;
            _currentHealth = vidaMaxima;

            // Em prefab não dá pra referenciar objeto da cena: se não foi atribuído, acha pela tag "Player".
            if (jogador == null)
                jogador = FindPlayerByTag();

            if (jogador == null)
                Debug.LogWarning($"{nameof(EnemyController)}: jogador não atribuído e nenhum objeto com a tag \"Player\" em '{name}'.", this);

            if (prefabProjetil == null || bocaDoCano == null)
            {
                Debug.LogError($"{nameof(EnemyController)}: Projectile Prefab ou Muzzle não atribuídos em '{name}'.", this);
                enabled = false;
            }
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
                return null; // tag não existe no projeto
            }
        }

        private void Update()
        {
            if (IsDead || jogador == null) return;

            FiniteStateMachine();
        }

        private void FiniteStateMachine()
        {
            switch (estadoAtual)
            {
                case EnemyState.Pursuit:
                    Pursuit();
                    break;
                case EnemyState.Cover:
                    Cover();
                    break;
                case EnemyState.Flee:
                    Flee();
                    break;
                case EnemyState.Heal:
                    Heal();
                    break;
            }
        }

        public void ChangeState(EnemyState newState)
        {
            if (estadoAtual == newState) return;

            estadoAtual = newState;

            // Em Cobertura o corpo encara o jogador o tempo todo; nos outros estados o agent gira sozinho.
            _agent.updateRotation = newState != EnemyState.Cover;

            if (newState == EnemyState.Pursuit)
                HideClaims.Remove(this);

            if (newState == EnemyState.Cover)
            {
                _nextCoverRepathTime = 0f; // escolhe posição já no próximo frame
                _hasCoverSpot = false;
                _blockedSince = -1f;
            }

            if (newState == EnemyState.Flee)
                _nextHidePathTime = 0f; // força escolher um esconderijo já no próximo frame
        }

        private void Pursuit()
        {
            // Transição: vida baixa -> foge.
            if (IsLowHealth)
            {
                ChangeState(EnemyState.Flee);
                return;
            }

            // Transição: ao entrar no alcance, em vez de parar e atirar no meio do campo, busca uma
            // posição tática (estado Cover).
            float sqrDistance = (jogador.position - transform.position).sqrMagnitude;
            if (sqrDistance <= alcanceAtaque * alcanceAtaque)
            {
                ChangeState(EnemyState.Cover);
                return;
            }

            // Igual ao EnemyNavMesh original: sempre persegue. É o SetDestination todo frame que
            // faz o próprio NavMeshAgent girar o objeto na direção do movimento.
            _agent.isStopped = false;
            _agent.SetDestination(jogador.position);
        }

        /// Tático: vai até uma posição com cobertura parcial (a quina de uma parede, onde só parte do
        /// corpo fica exposta ao jogador), atira de lá e troca de posição de tempos em tempos ou quando
        /// perde a visão. Sem posição boa disponível, cai no comportamento antigo (parar e atirar).
        private void Cover()
        {
            if (IsLowHealth)
            {
                ChangeState(EnemyState.Flee);
                return;
            }

            float sqrDistance = (jogador.position - transform.position).sqrMagnitude;
            if (sqrDistance > alcanceAtaque * alcanceAtaque * 1.5f)
            {
                ChangeState(EnemyState.Pursuit); // jogador se afastou: volta a perseguir
                return;
            }

            if (Time.time >= _nextCoverRepathTime)
            {
                _hasCoverSpot = TryFindCoverSpot(out _coverSpot);
                _blockedSince = -1f;

                if (_hasCoverSpot)
                {
                    _lastCoverSpot = _coverSpot;
                    HideClaims[this] = _coverSpot;
                    _agent.isStopped = false;
                    _agent.SetDestination(_coverSpot);
                    _nextCoverRepathTime = Time.time + Random.Range(tempoMinNaPosicao, tempoMaxNaPosicao);
                }
                else
                {
                    _nextCoverRepathTime = Time.time + 0.3f; // tenta de novo logo
                }
            }

            if (!_hasCoverSpot)
            {
                // Fallback: sem cobertura, para se tiver visão e alcance; senão se aproxima.
                if (sqrDistance <= alcanceAtaque * alcanceAtaque && HasLineOfSightToPlayer())
                {
                    _agent.isStopped = true;
                    RotateTowardsPlayer();
                    TryFire();
                }
                else
                {
                    _agent.isStopped = false;
                    _agent.SetDestination(jogador.position);
                }

                return;
            }

            // Sempre de frente pro jogador, mesmo andando (o agent não gira o corpo nesse estado).
            RotateTowardsPlayer();

            // Se o jogador se moveu e o ponto escolhido deixou de ter visão, escolhe outro sem esperar.
            if (Time.time >= _nextValidateTime)
            {
                _nextValidateTime = Time.time + 0.25f;
                if (!HasLineOfSight(jogador.position, _coverSpot + Vector3.up * BodyHeight))
                    _nextCoverRepathTime = 0f;
            }

            bool arrived = !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f;

            // Atira também andando, se tiver visão livre (strafe), em vez de só quando chega.
            if (!arrived)
            {
                if (HasLineOfSightToPlayer()) TryFire();
                return;
            }

            _agent.isStopped = true;

            if (HasLineOfSightToPlayer())
            {
                _blockedSince = -1f;
                TryFire();
            }
            else
            {
                // Chegou mas não enxerga (o jogador se moveu): espera um instante e escolhe outra posição.
                if (_blockedSince < 0f) _blockedSince = Time.time;
                if (Time.time - _blockedSince > 0.35f) _nextCoverRepathTime = 0f;
            }
        }

        /// Procura um ponto de tiro com meio corpo exposto: o jogador é visível do ponto, mas um
        /// deslocamento lateral de ~0,7 m (um dos lados do corpo) já fica atrás de um obstáculo — ou
        /// seja, o inimigo está colado numa quina de parede. Entre os candidatos, escolhe o mais
        /// próximo dele, ignorando pontos reservados por outros inimigos e a posição anterior.
        private bool TryFindCoverSpot(out Vector3 spot)
        {
            spot = transform.position;
            float bestDist = float.MaxValue;
            bool found = false;

            for (int ring = 1; ring <= HideRings; ring++)
            {
                float radius = raioBuscaCobertura * ring / HideRings;

                for (int i = 0; i < HideCandidates; i++)
                {
                    float angle = i * Mathf.PI * 2f / HideCandidates;
                    Vector3 candidate = transform.position +
                                        new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                        continue;

                    Vector3 point = navHit.position;
                    Vector3 toPlayer = jogador.position - point;
                    toPlayer.y = 0f;
                    float distToPlayer = toPlayer.magnitude;

                    if (distToPlayer < distanciaMinimaJogador || distToPlayer > alcanceAtaque * 0.9f) continue;
                    if (IsHideSpotClaimed(point)) continue;
                    if ((point - _lastCoverSpot).sqrMagnitude < 4f) continue;

                    Vector3 body = point + Vector3.up * BodyHeight;
                    if (!HasLineOfSight(jogador.position, body)) continue; // precisa enxergar o jogador

                    Vector3 side = Vector3.Cross(Vector3.up, toPlayer).normalized * 0.7f;
                    bool leftCovered = !HasLineOfSight(jogador.position, body - side);
                    bool rightCovered = !HasLineOfSight(jogador.position, body + side);
                    if (!leftCovered && !rightCovered) continue; // totalmente a céu aberto

                    float distFromEnemy = (point - transform.position).sqrMagnitude;
                    if (distFromEnemy < bestDist)
                    {
                        bestDist = distFromEnemy;
                        spot = point;
                        found = true;
                    }
                }
            }

            return found;
        }
        private void Flee()
        {
            _agent.isStopped = false;

            if (Time.time >= _nextHidePathTime)
            {
                _nextHidePathTime = Time.time + intervaloRecalculo;
                _agent.SetDestination(FindHidingSpot());
            }

            bool arrived = !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.1f;
            if (arrived && !HasLineOfSight(jogador.position, transform.position))
            {
                _agent.isStopped = true;
                ChangeState(EnemyState.Heal);
            }
        }
        private void Heal()
        {
            _agent.isStopped = true;
            _currentHealth = Mathf.Min(vidaMaxima, _currentHealth + curaPorSegundo * Time.deltaTime);

            if (_currentHealth >= vidaMaxima * percentualVidaRecuperada)
            {
                ChangeState(EnemyState.Pursuit);
                return;
            }

            if (HasLineOfSight(jogador.position, transform.position))
                ChangeState(EnemyState.Flee);
        }

        private Vector3 FindHidingSpot()
        {
            Vector3 bestHidden = transform.position;
            float bestHiddenDist = float.MaxValue;
            bool foundHidden = false;

            Vector3 bestExposed = transform.position;
            float bestExposedDist = -1f;

            for (int ring = 1; ring <= HideRings; ring++)
            {
                float radius = raioBuscaEscondijo * ring / HideRings;

                for (int i = 0; i < HideCandidates; i++)
                {
                    float angle = i * Mathf.PI * 2f / HideCandidates;
                    Vector3 candidate = transform.position +
                                        new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                        continue;

                    Vector3 point = navHit.position;

                    if (IsHideSpotClaimed(point)) continue;

                    float distFromEnemy = (point - transform.position).sqrMagnitude;
                    float distFromPlayer = (point - jogador.position).sqrMagnitude;

        
                    if (!HasLineOfSight(jogador.position, point + Vector3.up * BodyHeight))
                    {
                        if (distFromEnemy < bestHiddenDist)
                        {
                            bestHiddenDist = distFromEnemy;
                            bestHidden = point;
                            foundHidden = true;
                        }
                    }
                    else if (distFromPlayer > bestExposedDist)
                    {
                        bestExposedDist = distFromPlayer;
                        bestExposed = point;
                    }
                }
            }

            Vector3 chosen = foundHidden ? bestHidden : bestExposed;
            HideClaims[this] = chosen;
            return chosen;
        }

        /// Raycast simples entre o inimigo e o jogador: se algum collider sólido estiver no caminho
        private bool HasLineOfSightToPlayer()
        {
            Vector3 origin = bocaDoCano.position;
            Vector3 offset = jogador.position - origin;
            int mask = Physics.DefaultRaycastLayers; // qualquer collider sólido bloqueia a visão
            int count = Physics.SphereCastNonAlloc(origin, 0.2f, offset.normalized, _sightHits, offset.magnitude, mask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform hitTransform = _sightHits[i].collider.transform;
                if (hitTransform.IsChildOf(transform) || hitTransform.IsChildOf(jogador)) continue;
                return false;
            }

            return true;
        }

        /// Há visão livre se nenhum collider sólido (fora o próprio inimigo e o jogador) cruzar o
        /// segmento. Não depende de máscara configurada: qualquer collider sólido (parede, pilar, caixa) bloqueia.
        private bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 offset = to - from;
            int mask = Physics.DefaultRaycastLayers; // qualquer collider sólido bloqueia a visão
            int count = Physics.RaycastNonAlloc(from, offset.normalized, _sightHits, offset.magnitude, mask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform hitTransform = _sightHits[i].collider.transform;
                if (hitTransform.IsChildOf(transform) || hitTransform.IsChildOf(jogador)) continue;
                if (hitTransform.GetComponentInParent<EnemyController>() != null) continue; // outro inimigo não é cobertura
                return false;
            }

            return true;
        }

        private bool IsHideSpotClaimed(Vector3 point)
        {
            foreach (var claim in HideClaims)
            {
                if (claim.Key != this && (claim.Value - point).sqrMagnitude < HideClaimSpacing * HideClaimSpacing)
                    return true;
            }

            return false;
        }

        private void OnDestroy()
        {
            HideClaims.Remove(this);

            if (_counted)
            {
                _counted = false;
                _alive--;
            }
        }

        /// Gira o corpo (e o muzzle, que é filho dele) pra encarar o jogador instantaneamente. Sem isso,
        /// o cano fica apontando pra onde o inimigo parou de se mover, não pra onde o jogador está — daí
        /// o tiro "saía de lado" mesmo acertando a direção certa matematicamente.
        private void RotateTowardsPlayer()
        {
            Vector3 direction = jogador.position - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(direction);
        }

        private void TryFire()
        {
            if (Time.time < _nextAttackTime) return;

            _nextAttackTime = Time.time + intervaloAtaque;
            Debug.Log($"{name} atirou | estado={estadoAtual} | distância={Vector3.Distance(transform.position, jogador.position):F1}", this);

            Vector3 direction = (jogador.position - bocaDoCano.position).normalized;

            Projectile projectile = GetPooledProjectile();
            projectile.transform.SetPositionAndRotation(bocaDoCano.position, Quaternion.LookRotation(direction));
            projectile.gameObject.SetActive(true);
            projectile.Launch(direction, velocidadeProjetil, dano, tempoVidaProjetil, ReturnProjectileToPool);
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
            _killed++;
            _agent.isStopped = true;
            // Troque por animação/drop/pool depois; por enquanto remove o objeto.
            Destroy(gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, alcanceAtaque);

            if (Application.isPlaying && jogador != null && bocaDoCano != null)
            {
                Gizmos.color = HasLineOfSightToPlayer() ? Color.green : Color.magenta;
                Gizmos.DrawLine(bocaDoCano.position, jogador.position);
            }
        }
    }
}