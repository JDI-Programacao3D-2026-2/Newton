using UnityEngine;
using UnityEngine.AI;

namespace Game.Combat
{
    /// Gerador de inimigos (horda): de tempos em tempos solta um inimigo, até um limite de vivos no mapa.
    /// A cada rodada também manda inimigos pra defender as zonas de captura que ainda não foram capturadas,
    /// pra a horda não ficar parada onde nasceu.
    public class GeradorInimigos : MonoBehaviour
    {
        [Tooltip("Prefabs sorteados a cada geração (ex.: atirador e espadachim)")]
        [SerializeField] private GameObject[] inimigos;
        [SerializeField, Min(0.5f)] private float intervalo = 8f;
        [Tooltip("Não gera enquanto houver esse número de inimigos vivos")]
        [SerializeField, Min(1)] private int maximoVivos = 24;
        [Tooltip("A cada rodada, cada zona ainda não capturada precisa ter pelo menos esse número de defensores")]
        [SerializeField, Min(0)] private int defensoresPorZona = 5;

        private float _proximaGeracao;

        private void Update()
        {
            if (Time.time < _proximaGeracao) return;
            _proximaGeracao = Time.time + intervalo;

            EnemyFSM novo = Gerar();
            ReforcarZonas(novo);
        }

        // Solta um inimigo (se couber no limite) e devolve o FSM dele
        private EnemyFSM Gerar()
        {
            if (inimigos.Length == 0 || EnemyController.Vivos >= maximoVivos) return null;

            // só nasce em cima do NavMesh (senão o inimigo não anda)
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit ponto, 3f, NavMesh.AllAreas)) return null;

            GameObject prefab = inimigos[Random.Range(0, inimigos.Length)];
            GameObject novo = Instantiate(prefab, ponto.position + Vector3.up, Quaternion.identity);

            if (novo.TryGetComponent(out NavMeshAgent agente)) agente.Warp(ponto.position);

            return novo.GetComponent<EnemyFSM>();
        }

        // Cada zona não capturada completa seus defensores com os inimigos mais próximos que ainda não defendem nada.
        // O recém-nascido que sobrar vai pra zona com menos defensores.
        private void ReforcarZonas(EnemyFSM novo)
        {
            EnemyFSM[] todos = FindObjectsByType<EnemyFSM>();

            foreach (ZonaCaptura zona in ZonaCaptura.Ativas)
            {
                if (zona.Capturada) continue;

                int defensores = ContarDefensores(todos, zona);
                while (defensores < defensoresPorZona)
                {
                    EnemyFSM escolhido = MaisPertoSemZona(todos, zona);
                    if (escolhido == null) break;

                    escolhido.DefenderZona(zona);
                    defensores++;
                }
            }

            if (novo != null && novo.ZonaDefendida == null)
            {
                ZonaCaptura maisVazia = null;
                int menor = int.MaxValue;

                foreach (ZonaCaptura zona in ZonaCaptura.Ativas)
                {
                    if (zona.Capturada) continue;

                    int defensores = ContarDefensores(todos, zona);
                    if (defensores < menor)
                    {
                        menor = defensores;
                        maisVazia = zona;
                    }
                }

                if (maisVazia != null) novo.DefenderZona(maisVazia);
            }
        }

        private static int ContarDefensores(EnemyFSM[] todos, ZonaCaptura zona)
        {
            int total = 0;
            foreach (EnemyFSM e in todos)
                if (e.ZonaDefendida == zona) total++;

            return total;
        }

        private static EnemyFSM MaisPertoSemZona(EnemyFSM[] todos, ZonaCaptura zona)
        {
            EnemyFSM melhor = null;
            float menor = float.MaxValue;

            foreach (EnemyFSM e in todos)
            {
                if (e.ZonaDefendida != null) continue;

                float distancia = (e.transform.position - zona.transform.position).sqrMagnitude;
                if (distancia < menor)
                {
                    menor = distancia;
                    melhor = e;
                }
            }

            return melhor;
        }
    }
}
