using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Combat
{
    // de tempos em tempos um helicóptero chega de longe, para num lugar aleatório do mapa, solta a corda e os soldados descem de rapel (atirando).
    // depois ele vai embora e volta em outro lugar. quem desce vai defender a zona (ainda não capturada) que tiver menos defensores.
    public class GeradorInimigos : MonoBehaviour
    {
        [Header("Helicóptero")]
        [SerializeField] private Transform prefabHelicoptero;
        [SerializeField, Min(5f)] private float altura = 13f;
        [Tooltip("Distância do ponto de descida de onde ele chega e pra onde ele vai embora (sempre pro lado oposto ao jogador, então só some bem longe)")]
        [SerializeField, Min(20f)] private float distancia = 200f;
        [SerializeField, Min(1f)] private float velocidade = 22f;
        [SerializeField] private float velocidadeRotor = 1400f;

        [Header("Soldados")]
        [Tooltip("Prefabs sorteados a cada soldado (ex.: atirador e espadachim)")]
        [SerializeField] private GameObject[] inimigos;
        [SerializeField, Min(1)] private int soldadosPorVisita = 3;
        [SerializeField, Min(0.5f)] private float intervaloDeSaida = 1.4f;
        [SerializeField, Min(1f)] private float velocidadeRapel = 5f;

        [Header("Fluxo")]
        [Tooltip("Segundos até a primeira chegada")]
        [SerializeField, Min(0f)] private float primeiraVisita = 8f;
        [Tooltip("Segundos entre uma partida e a próxima chegada")]
        [SerializeField, Min(1f)] private float intervalo = 25f;
        [Tooltip("Não chegam mais soldados enquanto houver esse número de inimigos vivos")]
        [SerializeField, Min(1)] private int maximoVivos = 20;

        [Tooltip("Cada visita escolhe um lugar aleatório do mapa, com pelo menos essa folga livre em volta e longe do jogador")]
        [SerializeField, Min(1f)] private float folga = 3.5f;
        [SerializeField, Min(0f)] private float distanciaMinimaDoJogador = 15f;

        private Transform _heli;
        private Transform _rotor, _rotorCauda, _corda, _pontoDaCorda;
        private Bounds? _limitesDoMapa;

        private void Start()
        {
            // o helicóptero é um objeto solto, o Ciclo esconde e posiciona antes de cada chegada
            _heli = Instantiate(prefabHelicoptero);
            _rotor = _heli.Find("RotorPrincipal");
            _rotorCauda = _heli.Find("RotorTraseiro");
            _pontoDaCorda = _heli.Find("PontoDaCorda");
            _corda = _pontoDaCorda.Find("Corda");

            StartCoroutine(Ciclo());
        }

        private void OnDestroy()
        {
            if (_heli != null) Destroy(_heli.gameObject);
        }

        private void Update()
        {
            // as hélices giram o tempo todo
            _rotor.Rotate(0f, velocidadeRotor * Time.deltaTime, 0f);
            _rotorCauda.Rotate(velocidadeRotor * 1.5f * Time.deltaTime, 0f, 0f);
        }

        private IEnumerator Ciclo()
        {
            // cada helicóptero chega numa hora, senão os soldados caem todos juntos
            _heli.gameObject.SetActive(false);
            yield return new WaitForSeconds(Random.Range(primeiraVisita, primeiraVisita + intervalo * 0.6f));

            while (true)
            {
                // sem espaço no mapa: espera, nem sai de longe
                while (EnemyController.Vivos >= maximoVivos) yield return new WaitForSeconds(1f);

                // cada visita é num lugar do mapa; chega de uma direção qualquer com o nariz virado pro ponto
                Vector3 ponto = PontoAleatorio();
                Vector3 chegada = DirecaoDeLonge(ponto);
                Quaternion giro = Quaternion.LookRotation(-chegada);

                // a corda sai da porta, não do centro: põe a porta (e não o helicóptero) em cima do ponto
                Vector3 portaNoCorpo = Vector3.Scale(_pontoDaCorda.localPosition, _heli.localScale);
                portaNoCorpo.y = 0f;
                Vector3 parado = ponto + Vector3.up * altura - giro * portaNoCorpo;

                _heli.SetPositionAndRotation(parado + chegada * distancia, giro);
                _heli.gameObject.SetActive(true);
                yield return Voar(parado);

                yield return Descer();

                // vai embora por outro lado
                Vector3 saida = DirecaoDeLonge(ponto);
                yield return Voar(parado + saida * distancia + Vector3.up * 10f);
                _heli.gameObject.SetActive(false);
                yield return new WaitForSeconds(intervalo);
            }
        }

        // vai até o destino: acelera de longe e freia suave perto do ponto
        private IEnumerator Voar(Vector3 destino)
        {
            while ((_heli.position - destino).sqrMagnitude > 0.01f)
            {
                float falta = (_heli.position - destino).magnitude;
                float passo = Mathf.Min(velocidade, 2f + falta * 0.6f) * Time.deltaTime;
                _heli.position = Vector3.MoveTowards(_heli.position, destino, passo);

                // indo embora, vira o nariz pra onde tá indo
                Vector3 rumo = destino - _heli.position;
                rumo.y = 0f;
                if (rumo.sqrMagnitude > 25f) _heli.rotation = Quaternion.Slerp(_heli.rotation, Quaternion.LookRotation(rumo), 2f * Time.deltaTime);

                yield return null;
            }
        }

        // a corda desce, os soldados descem um a um e a corda sobe
        private IEnumerator Descer()
        {
            Vector3 topo = _pontoDaCorda.position;
            Vector3 chao = ChaoEmBaixo(topo);
            float comprimento = topo.y - chao.y;

            yield return EsticarCorda(comprimento, 1.2f);

            for (int i = 0; i < soldadosPorVisita; i++)
            {
                if (EnemyController.Vivos >= maximoVivos) break;

                StartCoroutine(Rapel(chao, topo));
                yield return new WaitForSeconds(intervaloDeSaida);
            }

            // espera o último chegar no chão
            yield return new WaitForSeconds(comprimento / velocidadeRapel);
            yield return EsticarCorda(0f, 0.8f);
        }

        private IEnumerator EsticarCorda(float comprimento, float duracao)
        {
            float de = _corda.localScale.y;
            // a escala da corda é em unidades do helicóptero (que tá reduzido), então converte de metros
            float alvo = comprimento / _heli.localScale.y;

            for (float t = 0f; t < 1f; t += Time.deltaTime / duracao)
            {
                _corda.localScale = new Vector3(1f, Mathf.Lerp(de, alvo, t), 1f);
                yield return null;
            }

            _corda.localScale = new Vector3(1f, alvo, 1f);
        }

        // o soldado nasce no chão (pro NavMesh aceitar), sobe pra porta e desce deslizando na corda
        private IEnumerator Rapel(Vector3 chao, Vector3 topo)
        {
            GameObject prefab = inimigos[Random.Range(0, inimigos.Length)];
            GameObject soldado = Instantiate(prefab, chao, Quaternion.identity);

            NavMeshAgent agente = soldado.GetComponent<NavMeshAgent>();
            EnemyFSM fsm = soldado.GetComponent<EnemyFSM>();
            agente.enabled = false;
            fsm.enabled = false;

            // sobe pra porta e desce reto, atirando. o player pode matar o soldado ainda na corda (aí ele some)
            EnemyController controle = soldado.GetComponent<EnemyController>();
            soldado.transform.position = topo;
            while (soldado != null && soldado.transform.position.y > chao.y)
            {
                soldado.transform.position += Vector3.down * (velocidadeRapel * Time.deltaTime);
                AtirarNaDescida(controle);
                yield return null;
            }

            if (soldado == null) yield break;

            soldado.transform.position = chao;
            agente.enabled = true;
            agente.Warp(chao);
            fsm.enabled = true;

            ZonaCaptura zona = ZonaComMenosDefensores();
            if (zona != null) fsm.DefenderZona(zona);
        }

        // na corda o FSM tá desligado: quem tem arma ainda mira e atira se o player tá no alcance e visível
        private static void AtirarNaDescida(EnemyController soldado)
        {
            if (soldado.CorpoACorpo || soldado.Jogador == null) return;

            Vector3 alvo = soldado.Jogador.position;
            if ((alvo - soldado.transform.position).sqrMagnitude > soldado.AlcanceEfetivo * soldado.AlcanceEfetivo) return;

            soldado.RotateTowards(alvo);
            if (soldado.HasLineOfSightToPlayer()) soldado.TryFire();
        }

        // direção que se afasta do player, com um desvio: assim o helicóptero só aparece e só some bem longe dele
        private static Vector3 DirecaoDeLonge(Vector3 de)
        {
            GameObject jogador = GameObject.FindGameObjectWithTag("Player");
            Vector3 longe = jogador != null ? de - jogador.transform.position : Random.insideUnitSphere;
            longe.y = 0f;
            if (longe.sqrMagnitude < 1f) longe = Vector3.forward;

            return Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * longe.normalized;
        }

        // sorteia um ponto do NavMesh com folga em volta (pra corda e os soldados) e longe do player
        private Vector3 PontoAleatorio()
        {
            if (!_limitesDoMapa.HasValue)
            {
                Vector3[] vertices = NavMesh.CalculateTriangulation().vertices;
                if (vertices.Length == 0) return transform.position;

                Bounds limites = new Bounds(vertices[0], Vector3.zero);
                foreach (Vector3 v in vertices) limites.Encapsulate(v);
                _limitesDoMapa = limites;
            }

            Bounds mapa = _limitesDoMapa.Value;
            GameObject jogador = GameObject.FindGameObjectWithTag("Player");

            for (int i = 0; i < 40; i++)
            {
                Vector3 sorteio = new Vector3(Random.Range(mapa.min.x, mapa.max.x), 0.5f, Random.Range(mapa.min.z, mapa.max.z));
                if (!NavMesh.SamplePosition(sorteio, out NavMeshHit ponto, 2f, NavMesh.AllAreas)) continue;
                if (!NavMesh.FindClosestEdge(ponto.position, out NavMeshHit borda, NavMesh.AllAreas) || borda.distance < folga) continue;
                if (jogador != null && (jogador.transform.position - ponto.position).sqrMagnitude < distanciaMinimaDoJogador * distanciaMinimaDoJogador) continue;

                return ponto.position;
            }

            return transform.position;
        }

        // ponto do NavMesh mais perto do chão embaixo da corda
        private static Vector3 ChaoEmBaixo(Vector3 topo)
        {
            Vector3 baixo = new Vector3(topo.x, 0f, topo.z);
            return NavMesh.SamplePosition(baixo, out NavMeshHit ponto, 6f, NavMesh.AllAreas) ? ponto.position : baixo;
        }

        private static ZonaCaptura ZonaComMenosDefensores()
        {
            EnemyFSM[] todos = FindObjectsByType<EnemyFSM>();
            ZonaCaptura escolhida = null;
            int menor = int.MaxValue;

            foreach (ZonaCaptura zona in ZonaCaptura.Ativas)
            {
                if (zona.Capturada) continue;

                int defensores = 0;
                foreach (EnemyFSM e in todos)
                    if (e.ZonaDefendida == zona) defensores++;

                if (defensores < menor)
                {
                    menor = defensores;
                    escolhida = zona;
                }
            }

            return escolhida;
        }
    }
}
