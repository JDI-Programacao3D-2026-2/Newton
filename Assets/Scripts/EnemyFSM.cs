using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Combat
{
    // cérebro do inimigo: patrulha, perseguição, cobertura, fuga e cura. vida, tiro e visão ficam no EnemyController
    [RequireComponent(typeof(EnemyController))]
    public class EnemyFSM : MonoBehaviour
    {
        [Header("Fuga")]
        [Tooltip("Foge abaixo desta fração da vida (0.25 = 25%)")]
        [SerializeField, Range(0.05f, 0.9f)] private float percentualVidaFuga = 0.25f;
        [Tooltip("Vida (fração) em que ele volta a perseguir")]
        [SerializeField, Range(0.1f, 1f)] private float percentualVidaRecuperada = 0.7f;
        [SerializeField, Min(0f)] private float curaPorSegundo = 5f;
        [Tooltip("Raio de busca de esconderijo")]
        [SerializeField, Min(1f)] private float raioBuscaEscondijo = 15f;
        [SerializeField, Min(0.1f)] private float intervaloRecalculo = 0.5f;

        [Header("Cobertura")]
        [Tooltip("Raio de busca de posição de cobertura")]
        [SerializeField, Min(1f)] private float raioBuscaCobertura = 12f;
        [Tooltip("Distância mínima do jogador ao escolher posição")]
        [SerializeField, Min(0f)] private float distanciaMinimaJogador = 2f;
        [Tooltip("Tempo (s) numa posição antes de trocar de lugar")]
        [SerializeField, Min(0.5f)] private float tempoMinNaPosicao = 1.5f;
        [SerializeField, Min(0.5f)] private float tempoMaxNaPosicao = 3.5f;

        [Tooltip("Sem linha de tiro por esse tempo (jogador atrás de um muro), o atirador avança pra achar um ângulo")]
        [SerializeField, Min(0.5f)] private float tempoParaAvancar = 2f;

        [Header("Memória")]
        [Tooltip("Segundos indo ao último lugar onde viu o jogador antes de patrulhar")]
        [SerializeField, Min(0f)] private float tempoMemoria = 5f;

        [Header("Alerta")]
        [Tooltip("Ao ver o jogador ou levar dano, chama os amigos que estão patrulhando dentro deste raio")]
        [SerializeField, Min(0f)] private float raioAlerta = 20f;
        [Tooltip("Quantas vezes o chamado passa de um inimigo pro outro (amigos dos amigos)")]
        [SerializeField, Min(0)] private int saltosDoAlerta = 2;

        [Header("Defesa de zona")]
        [Tooltip("Raio em volta da zona onde o defensor patrulha")]
        [SerializeField, Min(1f)] private float raioDefesaZona = 10f;

        [Header("Patrulha")]
        [SerializeField, Min(1f)] private float raioPatrulha = 15f;
        [SerializeField, Min(0f)] private float esperaPatrulhaMin = 1f;
        [SerializeField, Min(0f)] private float esperaPatrulhaMax = 3f;

        public enum EnemyState
        {
            [InspectorName("Perseguir")] Pursuit, // persegue o player
            [InspectorName("Fugir")] Flee,        // foge pra um lugar onde o player não vê
            [InspectorName("Cobertura")] Cover,   // atira de uma posição com meio corpo de fora
            [InspectorName("Curar")] Heal,        // escondido, recuperando vida
            [InspectorName("Patrulhar")] Patrol   // não viu o player: anda pelo mapa procurando
        }

        [Header("Debug")]
        public EnemyState estadoAtual = EnemyState.Patrol;

        private const int HideCandidates = 24;
        private const int HideRings = 4;
        private const float BodyHeight = 1f;
        private const float HideClaimSpacing = 2.5f;

        // lugares que outros inimigos já escolheram, pra não irem todos pro mesmo
        private static readonly Dictionary<EnemyFSM, Vector3> HideClaims = new Dictionary<EnemyFSM, Vector3>();

        // todos os inimigos ativos, pra chamar os amigos
        private static readonly List<EnemyFSM> Ativos = new List<EnemyFSM>();

        private EnemyController _ctrl;
        private NavMeshAgent _agent;
        private bool _estavaAtordoado;
        private float _proximoChamado;

        // zona que ele recebeu pra defender (fica guardada mesmo depois de capturada, aí ele vai atrás do player)
        private ZonaCaptura _zona;

        // zona que ele defende (nula = patrulha solto, ou a zona já foi capturada)
        public ZonaCaptura ZonaDefendida => _zona != null && !_zona.Capturada ? _zona : null;

        // memória do player
        private Vector3 _lastKnownPlayerPos;
        private float _lastSeenTime = -999f;
        private bool _hasLastKnown;
        private bool _seesPlayerNow = true;
        private float _lostSightSince = -1f;

        // patrulha
        private bool _patrolHasDestination;
        private float _nextPatrolTime;
        private NavMeshPath _patrolPath;

        // fuga
        private float _nextHidePathTime;

        // cobertura
        private float _nextCoverRepathTime;
        private float _nextValidateTime;
        private float _blockedSince = -1f;
        private float _semLinhaDeTiroDesde = -1f;
        private bool _hasCoverSpot;
        private Vector3 _coverSpot;
        private Vector3 _lastCoverSpot = new Vector3(float.MaxValue, 0f, 0f);

        private Transform Jogador => _ctrl.Jogador;
        private bool IsLowHealth => _ctrl.VidaFracao <= percentualVidaFuga;

        private void Awake()
        {
            _ctrl = GetComponent<EnemyController>();
            _agent = GetComponent<NavMeshAgent>();
            estadoAtual = EnemyState.Patrol; // começa sem saber onde o player está
        }

        private void OnEnable()
        {
            _ctrl.Damaged += OnDamaged;
            Ativos.Add(this);
        }

        private void OnDisable()
        {
            _ctrl.Damaged -= OnDamaged;
            Ativos.Remove(this);
        }
        private void OnDestroy() => HideClaims.Remove(this);

        private void Update()
        {
            if (!_ctrl.enabled || _ctrl.IsDead || Jogador == null) return;

            // atordoado fica parado até acabar, depois escolhe de novo o que fazer
            if (_ctrl.Atordoado)
            {
                _agent.isStopped = true;
                _estavaAtordoado = true;
                return;
            }

            if (_estavaAtordoado)
            {
                _estavaAtordoado = false;
                _agent.isStopped = false;
                _nextCoverRepathTime = 0f;
            }

            switch (estadoAtual)
            {
                case EnemyState.Patrol:
                    Patrol();
                    break;
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

            // na cobertura o corpo sempre olha pro alvo, nos outros estados o agent gira sozinho
            _agent.updateRotation = newState != EnemyState.Cover;

            if (newState == EnemyState.Pursuit)
                HideClaims.Remove(this);

            if (newState == EnemyState.Patrol)
            {
                _patrolHasDestination = false;
                _nextPatrolTime = 0f;
            }

            if (newState == EnemyState.Cover)
            {
                _lostSightSince = -1f;
                _nextCoverRepathTime = 0f; // escolhe posição no próximo frame
                _semLinhaDeTiroDesde = -1f;
                _hasCoverSpot = false;
                _blockedSince = -1f;
            }

            if (newState == EnemyState.Flee)
                _nextHidePathTime = 0f; // escolhe esconderijo no próximo frame
        }

        // levou tiro: vai ver o que foi e chama ajuda (no máximo a cada 3 s)
        private void OnDamaged()
        {
            if (Jogador == null) return;

            NotePlayerSeen();
            if (estadoAtual == EnemyState.Patrol) ChangeState(EnemyState.Pursuit);

            if (Time.time >= _proximoChamado)
            {
                _proximoChamado = Time.time + 3f;
                ChamarAmigos(Jogador.position, 0);
            }
        }

        // chama os amigos que tão patrulhando perto, eles vão até onde o player está
        // o chamado passa de amigo pra amigo (amigos dos amigos) até 'saltosDoAlerta' vezes
        private void ChamarAmigos(Vector3 posicaoDoJogador, int salto)
        {
            foreach (EnemyFSM amigo in Ativos.ToArray())
            {
                if (amigo == this || amigo._ctrl.IsDead || amigo.estadoAtual != EnemyState.Patrol) continue;
                if ((amigo.transform.position - transform.position).sqrMagnitude > raioAlerta * raioAlerta) continue;

                amigo.ReceberAlerta(posicaoDoJogador, salto + 1);
            }
        }

        public void ReceberAlerta(Vector3 posicaoDoJogador, int salto = 0)
        {
            _lastKnownPlayerPos = posicaoDoJogador;
            _lastSeenTime = Time.time;
            _hasLastKnown = true;
            ChangeState(EnemyState.Pursuit);

            // quem foi chamado também chama os vizinhos
            if (salto < saltosDoAlerta) ChamarAmigos(posicaoDoJogador, salto);
        }

        // passa a patrulhar em volta dessa zona (em vez de onde nasceu)
        public void DefenderZona(ZonaCaptura zona)
        {
            _zona = zona;
            _patrolHasDestination = false; // escolhe um ponto novo perto da zona na próxima patrulha
            _nextPatrolTime = 0f;
        }

        private void NotePlayerSeen()
        {
            _lastKnownPlayerPos = Jogador.position;
            _lastSeenTime = Time.time;
            _hasLastKnown = true;
        }

        // olha pro player, ou pro último lugar onde viu ele se tiver perdido de vista
        private void FacePlayer()
        {
            _ctrl.RotateTowards(_seesPlayerNow || !_hasLastKnown ? Jogador.position : _lastKnownPlayerPos);
        }

        // anda por pontos aleatórios do NavMesh até ver o player
        private void Patrol()
        {
            if (_ctrl.CanSeePlayer())
            {
                NotePlayerSeen();
                ChamarAmigos(Jogador.position, 0);
                ChangeState(EnemyState.Pursuit);
                return;
            }

            // a zona que ele defendia foi capturada: vai atrás do player
            if (_zona != null && _zona.Capturada)
            {
                _zona = null;
                ReceberAlerta(Jogador.position);
                return;
            }

            _agent.isStopped = false;

            if (!_patrolHasDestination)
            {
                // parado: olha em volta
                transform.Rotate(0f, 40f * Time.deltaTime, 0f);

                if (Time.time >= _nextPatrolTime && TryPickPatrolPoint(out Vector3 ponto))
                {
                    _agent.SetDestination(ponto);
                    _patrolHasDestination = true;
                }
                return;
            }

            bool chegou = !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.5f;
            if (chegou)
            {
                _patrolHasDestination = false;
                _nextPatrolTime = Time.time + Random.Range(esperaPatrulhaMin, esperaPatrulhaMax);
            }
        }

        private bool TryPickPatrolPoint(out Vector3 ponto)
        {
            _patrolPath ??= new NavMeshPath();

            // defensor patrulha em volta da zona, os outros em volta de onde tão
            bool defendendo = ZonaDefendida != null;
            Vector3 centro = defendendo ? ZonaDefendida.transform.position : transform.position;
            float raio = defendendo ? raioDefesaZona : raioPatrulha;

            for (int i = 0; i < 8; i++)
            {
                Vector2 c = Random.insideUnitCircle * raio;
                Vector3 candidato = centro + new Vector3(c.x, 0f, c.y);

                if (!NavMesh.SamplePosition(candidato, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
                if (!_agent.CalculatePath(hit.position, _patrolPath) || _patrolPath.status != NavMeshPathStatus.PathComplete) continue;

                ponto = hit.position;
                return true;
            }

            ponto = transform.position;
            _nextPatrolTime = Time.time + 1f; // sem ponto alcançável: tenta de novo
            return false;
        }

        private void Pursuit()
        {
            // vida baixa: foge
            if (IsLowHealth)
            {
                ChangeState(EnemyState.Flee);
                return;
            }

            if (_ctrl.CanSeePlayer())
            {
                NotePlayerSeen();

                // espadachim não usa cobertura: chega perto e bate
                if (_ctrl.CorpoACorpo)
                {
                    AtacarCorpoACorpo();
                    return;
                }

                // no alcance: procura uma cobertura
                float sqrDistance = (Jogador.position - transform.position).sqrMagnitude;
                if (sqrDistance <= _ctrl.AlcanceEfetivo * _ctrl.AlcanceEfetivo)
                {
                    ChangeState(EnemyState.Cover);
                    return;
                }

                _agent.isStopped = false;
                _agent.SetDestination(Jogador.position);
                return;
            }

            // perdeu de vista: vai até o último lugar onde viu o player e depois volta a patrulhar
            if (!_hasLastKnown || Time.time - _lastSeenTime > tempoMemoria)
            {
                ChangeState(EnemyState.Patrol);
                return;
            }

            _agent.isStopped = false;
            _agent.SetDestination(_lastKnownPlayerPos);

            bool chegou = !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.8f;
            if (chegou) ChangeState(EnemyState.Patrol);
        }

        // dentro do alcance da espada para e ataca, fora dele corre até o player
        private void AtacarCorpoACorpo()
        {
            float sqrDistance = (Jogador.position - transform.position).sqrMagnitude;
            if (sqrDistance <= _ctrl.AlcanceEfetivo * _ctrl.AlcanceEfetivo)
            {
                _agent.isStopped = true;
                _ctrl.RotateTowards(Jogador.position);
                _ctrl.TryFire();
                return;
            }

            _agent.isStopped = false;
            _agent.SetDestination(Jogador.position);
        }

        // atira de um lugar com cobertura parcial e troca de lugar de vez em quando
        private void Cover()
        {
            if (IsLowHealth)
            {
                ChangeState(EnemyState.Flee);
                return;
            }

            float alcance = _ctrl.AlcanceEfetivo;
            float sqrDistance = (Jogador.position - transform.position).sqrMagnitude;
            if (sqrDistance > alcance * alcance * 1.5f)
            {
                ChangeState(EnemyState.Pursuit); // o player se afastou
                return;
            }

            // sem ver o player por um tempo, volta a perseguir (indo pro último ponto que viu)
            _seesPlayerNow = _ctrl.CanSeePlayer();
            if (_seesPlayerNow)
            {
                NotePlayerSeen();
                _lostSightSince = -1f;
            }
            else
            {
                if (_lostSightSince < 0f) _lostSightSince = Time.time;
                if (Time.time - _lostSightSince > 2.5f)
                {
                    ChangeState(EnemyState.Pursuit);
                    return;
                }
            }

            // player atrás de um muro (sem linha de tiro por um tempo): avança até achar um ângulo,
            // em vez de ficar trocando de cobertura sem atirar
            if (_ctrl.HasLineOfSightToPlayer())
            {
                _semLinhaDeTiroDesde = -1f;
            }
            else
            {
                if (_semLinhaDeTiroDesde < 0f) _semLinhaDeTiroDesde = Time.time;

                if (Time.time - _semLinhaDeTiroDesde > tempoParaAvancar)
                {
                    _agent.isStopped = false;
                    _agent.SetDestination(Jogador.position);
                    FacePlayer();
                    return;
                }
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
                    _nextCoverRepathTime = Time.time + 0.3f; // tenta de novo
                }
            }

            if (!_hasCoverSpot)
            {
                // sem cobertura: atira parado se vê o player, senão chega mais perto
                if (sqrDistance <= alcance * alcance && _ctrl.HasLineOfSightToPlayer())
                {
                    _agent.isStopped = true;
                    FacePlayer();
                    _ctrl.TryFire();
                }
                else
                {
                    _agent.isStopped = false;
                    _agent.SetDestination(Jogador.position);
                }

                return;
            }

            // olha pro player mesmo andando
            FacePlayer();

            // o ponto escolhido perdeu a visão: escolhe outro
            if (Time.time >= _nextValidateTime)
            {
                _nextValidateTime = Time.time + 0.25f;
                if (!_ctrl.HasLineOfSight(Jogador.position, _coverSpot + Vector3.up * BodyHeight))
                    _nextCoverRepathTime = 0f;
            }

            bool arrived = !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f;

            // atira andando, se tiver visão livre
            if (!arrived)
            {
                if (_ctrl.HasLineOfSightToPlayer()) _ctrl.TryFire();
                return;
            }

            _agent.isStopped = true;

            if (_ctrl.HasLineOfSightToPlayer())
            {
                _blockedSince = -1f;
                _ctrl.TryFire();
            }
            else
            {
                // chegou mas não vê o player: troca de lugar
                if (_blockedSince < 0f) _blockedSince = Time.time;
                if (Time.time - _blockedSince > 0.35f) _nextCoverRepathTime = 0f;
            }
        }

        // procura um ponto onde o player aparece mas um lado do corpo fica atrás de um obstáculo
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
                    Vector3 toPlayer = Jogador.position - point;
                    toPlayer.y = 0f;
                    float distToPlayer = toPlayer.magnitude;

                    if (distToPlayer < distanciaMinimaJogador || distToPlayer > _ctrl.AlcanceEfetivo * 0.9f) continue;
                    if (IsHideSpotClaimed(point)) continue;
                    if ((point - _lastCoverSpot).sqrMagnitude < 4f) continue;

                    Vector3 body = point + Vector3.up * BodyHeight;
                    if (!_ctrl.HasLineOfSight(Jogador.position, body)) continue;

                    Vector3 side = Vector3.Cross(Vector3.up, toPlayer).normalized * 0.7f;
                    bool leftCovered = !_ctrl.HasLineOfSight(Jogador.position, body - side);
                    bool rightCovered = !_ctrl.HasLineOfSight(Jogador.position, body + side);
                    if (!leftCovered && !rightCovered) continue; // sem cobertura

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
            if (arrived && !_ctrl.HasLineOfSight(Jogador.position, transform.position))
            {
                _agent.isStopped = true;
                ChangeState(EnemyState.Heal);
            }
        }

        private void Heal()
        {
            _agent.isStopped = true;
            _ctrl.Curar(curaPorSegundo * Time.deltaTime);

            if (_ctrl.VidaFracao >= percentualVidaRecuperada)
            {
                ChangeState(EnemyState.Pursuit);
                return;
            }

            if (_ctrl.HasLineOfSight(Jogador.position, transform.position))
                ChangeState(EnemyState.Flee);
        }

        // ponto mais perto que o player não vê (se não tiver, o mais longe dele)
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
                    float distFromPlayer = (point - Jogador.position).sqrMagnitude;

                    if (!_ctrl.HasLineOfSight(Jogador.position, point + Vector3.up * BodyHeight))
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

        private bool IsHideSpotClaimed(Vector3 point)
        {
            foreach (var claim in HideClaims)
            {
                if (claim.Key != this && (claim.Value - point).sqrMagnitude < HideClaimSpacing * HideClaimSpacing)
                    return true;
            }

            return false;
        }

        // na Scene (inimigo selecionado): círculo laranja = raio do chamado de ajuda
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(transform.position, raioAlerta);
        }
    }
}
