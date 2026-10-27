using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace Game.Combat
{
    // duas habilidades por personagem, nas teclas Q e E. cada prefab escolhe no Inspector qual fica em cada tecla
    public class Habilidades : MonoBehaviour
    {
        public enum Tipo { Rajada, Granada, Giro, Stun, Fumaca, Bombardeio }

        [Header("Tecla Q")]
        [SerializeField] private Tipo habilidadeQ = Tipo.Rajada;
        [SerializeField, Min(0.1f)] private float recargaQ = 5f;

        [Header("Tecla E")]
        [SerializeField] private Tipo habilidadeE = Tipo.Granada;
        [SerializeField, Min(0.1f)] private float recargaE = 8f;

        [Header("HUD Q (Canvas)")]
        [Tooltip("Ícone da habilidade. Sem sprite, o slot mostra o nome dela.")]
        [SerializeField] private Image iconeQ;
        [Tooltip("Círculo escuro (Image Filled, Radial 360) que esvazia durante a recarga")]
        [SerializeField] private Image coberturaQ;
        [Tooltip("Segundos que faltam (ou o nome, se não tiver ícone)")]
        [SerializeField] private Text textoQ;

        [Header("HUD E (Canvas)")]
        [SerializeField] private Image iconeE;
        [SerializeField] private Image coberturaE;
        [SerializeField] private Text textoE;

        [Header("Rajada")]
        [SerializeField, Min(1)] private int tirosRajada = 3;
        [SerializeField, Min(0.02f)] private float intervaloRajada = 0.1f;

        [Header("Granada")]
        [Tooltip("Prefab com Rigidbody e Collider. As granadas são criadas no início e reaproveitadas (pool).")]
        [SerializeField] private Rigidbody prefabGranada;
        [Tooltip("Particle System da explosão (pool). A escala da raiz vira o diâmetro da explosão.")]
        [SerializeField] private Transform prefabExplosao;
        [SerializeField, Min(1)] private int tamanhoPool = 4;
        [SerializeField, Min(1f)] private float forcaArremesso = 16f;
        [SerializeField, Min(0.1f)] private float pavio = 1.8f;
        [SerializeField, Min(1f)] private float raioGranada = 5f;
        [SerializeField, Min(0f)] private float danoGranada = 40f;

        [Header("Giro (espada)")]
        [Tooltip("Componente Animation do objeto Espada, com o clipe Espada_Giro")]
        [SerializeField] private Animation animacaoEspada;
        [SerializeField] private string clipeGiro = "Espada_Giro";
        [SerializeField, Min(0.5f)] private float raioGiro = 3.5f;
        [SerializeField, Min(0f)] private float danoGiro = 30f;
        [Tooltip("Tempo entre começar o giro e o dano valer")]
        [SerializeField, Min(0f)] private float atrasoGiro = 0.2f;

        [Header("Stun")]
        [SerializeField, Min(0.5f)] private float raioStun = 6f;
        [SerializeField, Min(0.1f)] private float duracaoStun = 3f;

        [Header("Fumaça")]
        [Tooltip("Lata de fumaça (Rigidbody, pool): voa em arco até o ponto mirado e, ao pousar, solta a nuvem.")]
        [SerializeField] private Rigidbody prefabLataFumaca;
        [Tooltip("Particle System de fumaça (pool). A escala da raiz vira o diâmetro da nuvem; dentro dela os inimigos não enxergam o jogador.")]
        [SerializeField] private Transform prefabFumaca;
        [SerializeField, Min(1f)] private float raioFumaca = 5f;
        [SerializeField, Min(1f)] private float duracaoFumaca = 7f;
        [SerializeField, Min(1f)] private float alcanceFumaca = 18f;
        [Tooltip("Tempo de voo da lata até o ponto mirado")]
        [SerializeField, Min(0.3f)] private float tempoDeVooLata = 0.8f;

        [Header("Bombardeio")]
        [Tooltip("Míssil (modelo ProBuilder, pool): cai do céu de ponta pra baixo e explode no chão.")]
        [SerializeField] private Transform prefabMissil;
        [Tooltip("Círculo no chão: aparece e segue a mira enquanto a tecla está apertada (solte pra disparar); fica fixo, em vermelho, enquanto os mísseis caem.")]
        [SerializeField] private Transform prefabAreaBombardeio;
        [SerializeField, Min(1)] private int explosoesBombardeio = 8;
        [Tooltip("Tempo entre apertar a tecla e cair a primeira granada (dá pra ver a área)")]
        [SerializeField, Min(0f)] private float avisoBombardeio = 0.8f;
        [SerializeField, Min(0.3f)] private float intervaloBombardeio = 0.35f;
        [SerializeField, Min(1f)] private float raioAreaBombardeio = 6f;
        [SerializeField, Min(1f)] private float raioExplosaoBombardeio = 3f;
        [SerializeField, Min(0f)] private float danoBombardeio = 30f;
        [SerializeField, Min(1f)] private float alcanceBombardeio = 25f;

        // fumaças que existem agora (o EnemyController olha aqui pra saber se o player tá escondido)
        private static readonly List<Transform> FumacasAtivas = new List<Transform>();

        private WeaponPlayer _arma;
        private Collider[] _meusColliders;
        private Queue<Rigidbody> _granadas;
        private Queue<Rigidbody> _latas;
        private Queue<Transform> _misseis;
        private Queue<Transform> _explosoes;
        private Queue<Transform> _fumacas;
        private Transform _circulo;          // círculo do bombardeio
        private Renderer _circuloRenderer;
        private MaterialPropertyBlock _tintaCirculo;
        private bool _bombardeando;
        private bool _mirandoBombardeio; // tecla do bombardeio apertada (mostrando a área)
        private readonly Collider[] _area = new Collider[64];
        private readonly HashSet<IDamageable> _alvosDano = new HashSet<IDamageable>();
        private readonly HashSet<EnemyController> _alvosStun = new HashSet<EnemyController>();

        private float _proximoQ;
        private float _proximoE;
        private float _proximaAtualizacaoHud;

        // a fumaça bloqueia a visão se a linha entre os dois pontos passa por alguma nuvem
        // (ou se um deles tá dentro dela). serve pro inimigo cego dentro da fumaça e pro player escondido atrás dela
        public static bool BloqueiaVisao(Vector3 de, Vector3 ate)
        {
            Vector3 linha = ate - de;
            float tamanho = Mathf.Max(linha.sqrMagnitude, 0.0001f);

            foreach (Transform fumaca in FumacasAtivas)
            {
                if (fumaca == null || !fumaca.gameObject.activeSelf) continue;

                // ponto da linha mais perto do centro da nuvem
                float t = Mathf.Clamp01(Vector3.Dot(fumaca.position - de, linha) / tamanho);
                float raio = fumaca.localScale.x * 0.5f;
                if ((fumaca.position - (de + linha * t)).sqrMagnitude <= raio * raio) return true;
            }

            return false;
        }

        private void Awake()
        {
            _arma = GetComponent<WeaponPlayer>();
            _meusColliders = GetComponentsInChildren<Collider>();

            // cria os pools já no começo pra não dar Instantiate no meio da partida
            _granadas = CriarPool(prefabGranada, tamanhoPool);
            _latas = CriarPool(prefabLataFumaca, 2);
            _misseis = CriarPool(prefabMissil, 4);
            _explosoes = CriarPool(prefabExplosao, tamanhoPool);
            _fumacas = CriarPool(prefabFumaca, 2);

            if (prefabAreaBombardeio != null)
            {
                _circulo = CriarPool(prefabAreaBombardeio, 1).Dequeue();
                _circuloRenderer = _circulo.GetComponentInChildren<Renderer>();
                _tintaCirculo = new MaterialPropertyBlock();
            }
        }

        // cria 'quantidade' cópias desligadas do prefab (fila vazia se o personagem não usa essa habilidade)
        private static Queue<T> CriarPool<T>(T prefab, int quantidade) where T : Component
        {
            var fila = new Queue<T>();
            if (prefab == null) return fila;

            for (int i = 0; i < quantidade; i++)
            {
                T copia = Instantiate(prefab);
                copia.gameObject.SetActive(false);
                fila.Enqueue(copia);
            }

            return fila;
        }

        private void Update()
        {
            // pausado (sair, vitória ou derrota): não usa habilidade
            if (Time.timeScale > 0f)
            {
                Entrada(Keyboard.current.qKey, habilidadeQ, ref _proximoQ, recargaQ);
                Entrada(Keyboard.current.eKey, habilidadeE, ref _proximoE, recargaE);
            }

            AtualizarMiraBombardeio();
            AtualizarHud();
        }

        // a habilidade dispara quando aperta a tecla. o bombardeio é "segura pra mirar, solta pra disparar"
        private void Entrada(ButtonControl tecla, Tipo tipo, ref float proximo, float recarga)
        {
            if (tipo != Tipo.Bombardeio)
            {
                if (tecla.wasPressedThisFrame) Usar(tipo, ref proximo, recarga);
                return;
            }

            if (tecla.wasPressedThisFrame && Time.time >= proximo && !_bombardeando)
                _mirandoBombardeio = true;

            if (tecla.wasReleasedThisFrame && _mirandoBombardeio)
            {
                _mirandoBombardeio = false;
                Usar(tipo, ref proximo, recarga);
            }
        }

        private void Usar(Tipo tipo, ref float proximo, float recarga)
        {
            if (Time.time < proximo) return;

            if (Executar(tipo))
                proximo = Time.time + recarga;
        }

        private bool Executar(Tipo tipo)
        {
            switch (tipo)
            {
                case Tipo.Rajada: return Rajada();
                case Tipo.Granada: return Granada();
                case Tipo.Giro: return Giro();
                case Tipo.Stun: return Stun();
                case Tipo.Fumaca: return Fumaca();
                case Tipo.Bombardeio: return Bombardeio();
                default: return false;
            }
        }

        private bool Rajada()
        {
            if (_arma.CorpoACorpo) return false;

            StartCoroutine(RajadaRotina());
            return true;
        }

        private IEnumerator RajadaRotina()
        {
            var espera = new WaitForSeconds(intervaloRajada);
            for (int i = 0; i < tirosRajada; i++)
            {
                _arma.Disparar();
                yield return espera;
            }
        }

        private bool Granada()
        {
            if (_granadas.Count == 0) return false;

            Vector3 frente = Camera.main != null ? Camera.main.transform.forward : transform.forward;
            Rigidbody granada = _granadas.Dequeue();
            Arremessar(granada, (frente + Vector3.up * 0.3f).normalized * forcaArremesso);

            StartCoroutine(GranadaRotina(granada));
            return true;
        }

        // põe o objeto na frente do player e joga com essa velocidade (granada e lata de fumaça)
        private Vector3 OrigemDoArremesso => transform.position + transform.forward + Vector3.up * 0.8f;

        private void Arremessar(Rigidbody objeto, Vector3 velocidade)
        {
            objeto.isKinematic = false;
            objeto.transform.position = OrigemDoArremesso;
            objeto.gameObject.SetActive(true);

            // não bate no próprio player que acabou de jogar
            Collider colisor = objeto.GetComponent<Collider>();
            foreach (Collider meu in _meusColliders)
                Physics.IgnoreCollision(colisor, meu, true);

            objeto.linearVelocity = velocidade;
            objeto.angularVelocity = Random.insideUnitSphere * 5f;
        }

        private IEnumerator GranadaRotina(Rigidbody granada)
        {
            yield return new WaitForSeconds(pavio);

            Vector3 posicao = granada.position;
            granada.linearVelocity = Vector3.zero;
            granada.gameObject.SetActive(false);
            _granadas.Enqueue(granada);

            DanoEmArea(posicao, raioGranada, danoGranada, true); // fogo amigo: a granada machuca o player também
            yield return ExplosaoRotina(posicao, raioGranada);
        }

        // o Particle System da explosão toca sozinho quando liga, e a escala dele é o diâmetro da explosão
        private IEnumerator ExplosaoRotina(Vector3 posicao, float raio)
        {
            if (_explosoes.Count == 0) yield break;

            Transform explosao = _explosoes.Dequeue();
            explosao.position = posicao;
            explosao.localScale = Vector3.one * (raio * 2f);
            explosao.gameObject.SetActive(true);

            yield return new WaitForSeconds(1f); // tempo das partículas

            explosao.gameObject.SetActive(false);
            _explosoes.Enqueue(explosao);
        }

        private bool Giro()
        {
            if (animacaoEspada != null) animacaoEspada.Play(clipeGiro);

            StartCoroutine(DanoDepois(atrasoGiro));
            return true;
        }

        private IEnumerator DanoDepois(float atraso)
        {
            yield return new WaitForSeconds(atraso);
            DanoEmArea(transform.position, raioGiro, danoGiro);
        }

        private bool Stun()
        {
            // a espada desce no chão (mesmo golpe do ataque normal)
            if (animacaoEspada != null) animacaoEspada.Play();

            int count = Physics.OverlapSphereNonAlloc(transform.position, raioStun, _area,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            _alvosStun.Clear();
            for (int i = 0; i < count; i++)
            {
                EnemyController inimigo = _area[i].GetComponentInParent<EnemyController>();
                if (inimigo != null && _alvosStun.Add(inimigo))
                    inimigo.Atordoar(duracaoStun);
            }

            return true;
        }

        // joga uma lata de fumaça em arco até o ponto mirado, quando pousa ela solta a nuvem.
        // enquanto o player tiver dentro da nuvem os inimigos não enxergam ele
        private bool Fumaca()
        {
            if (_latas.Count == 0 || _fumacas.Count == 0) return false;

            // velocidade pra lata chegar no ponto mirado em 'tempoDeVooLata' segundos (com a gravidade)
            Vector3 alvo = _arma.PontoDaMira(alcanceFumaca);
            Vector3 velocidade = (alvo - OrigemDoArremesso) / tempoDeVooLata - 0.5f * Physics.gravity * tempoDeVooLata;

            Rigidbody lata = _latas.Dequeue();
            Arremessar(lata, velocidade);

            StartCoroutine(FumacaRotina(lata));
            return true;
        }

        private IEnumerator FumacaRotina(Rigidbody lata)
        {
            yield return new WaitForSeconds(tempoDeVooLata + 0.15f); // voo + um instante pra assentar

            // a lata fica parada onde pousou, no centro da nuvem
            lata.linearVelocity = Vector3.zero;
            lata.angularVelocity = Vector3.zero;
            lata.isKinematic = true;

            Transform fumaca = _fumacas.Dequeue();
            fumaca.position = lata.position + Vector3.up * (raioFumaca * 0.5f);
            fumaca.localScale = Vector3.one * (raioFumaca * 2f);
            fumaca.gameObject.SetActive(true);
            FumacasAtivas.Add(fumaca);

            yield return new WaitForSeconds(duracaoFumaca);

            fumaca.gameObject.SetActive(false);
            FumacasAtivas.Remove(fumaca);
            _fumacas.Enqueue(fumaca);

            lata.gameObject.SetActive(false);
            _latas.Enqueue(lata);
        }

        // chuva de mísseis numa área em volta do ponto mirado
        private bool Bombardeio()
        {
            if (_bombardeando) return false;

            StartCoroutine(BombardeioRotina(_arma.PontoDaMira(alcanceBombardeio)));
            return true;
        }

        private IEnumerator BombardeioRotina(Vector3 centro)
        {
            _bombardeando = true;
            MostrarArea(centro, true);                           // círculo vermelho fixo no lugar mirado
            yield return new WaitForSeconds(avisoBombardeio);    // aviso: dá pra ver onde vai cair

            var espera = new WaitForSeconds(intervaloBombardeio);
            for (int i = 0; i < explosoesBombardeio; i++)
            {
                Vector2 sorteio = Random.insideUnitCircle * raioAreaBombardeio;
                StartCoroutine(MissilRotina(centro + new Vector3(sorteio.x, 0f, sorteio.y)));
                yield return espera;
            }

            yield return new WaitForSeconds(0.6f);               // o último míssil ainda tá caindo
            _bombardeando = false;
        }

        // um míssil cai do céu de ponta pra baixo no ponto sorteado e explode (machuca o player também)
        private IEnumerator MissilRotina(Vector3 ponto)
        {
            // acha o chão embaixo do ponto sorteado
            if (Physics.Raycast(ponto + Vector3.up * 30f, Vector3.down, out RaycastHit chao, 60f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                ponto = chao.point;

            const float alturaDeQueda = 22f;
            const float tempoDeQueda = 0.55f;
            const float metadeDoMissil = 1.7f; // a ponta toca o chão no fim da queda

            Transform missil = _misseis.Count > 0 ? _misseis.Dequeue() : null;
            if (missil != null)
            {
                Vector3 topo = ponto + Vector3.up * alturaDeQueda;
                Vector3 fim = ponto + Vector3.up * metadeDoMissil;

                missil.rotation = Quaternion.Euler(180f, Random.Range(0f, 360f), 0f); // ponta pra baixo
                missil.position = topo;
                missil.gameObject.SetActive(true);

                for (float t = 0f; t < tempoDeQueda; t += Time.deltaTime)
                {
                    missil.position = Vector3.Lerp(topo, fim, t / tempoDeQueda);
                    yield return null;
                }

                missil.gameObject.SetActive(false);
                _misseis.Enqueue(missil);
            }
            else
            {
                yield return new WaitForSeconds(tempoDeQueda);
            }

            DanoEmArea(ponto, raioExplosaoBombardeio, danoBombardeio, true);
            yield return ExplosaoRotina(ponto, raioExplosaoBombardeio);
        }

        // o círculo só aparece (e segue a mira, branco) enquanto a tecla do bombardeio tá apertada.
        // ao soltar ele dispara e o círculo fica fixo e vermelho (MostrarArea no começo do bombardeio)
        private void AtualizarMiraBombardeio()
        {
            if (_circulo == null || _bombardeando) return;

            _circulo.gameObject.SetActive(_mirandoBombardeio);
            if (_mirandoBombardeio) MostrarArea(_arma.PontoDaMira(alcanceBombardeio), false);
        }

        private void MostrarArea(Vector3 centro, bool disparado)
        {
            if (_circulo == null) return;

            _circulo.position = centro + Vector3.up * 0.05f;
            _circulo.localScale = Vector3.one * (raioAreaBombardeio * 2f);
            _circulo.gameObject.SetActive(true);

            _tintaCirculo.SetColor("_BaseColor", disparado ? new Color(0.95f, 0.1f, 0.1f, 0.45f) : new Color(1f, 1f, 1f, 0.3f));
            _circuloRenderer.SetPropertyBlock(_tintaCirculo);
        }

        // dano em todo mundo que pode levar dano (IDamageable) no raio, uma vez por alvo.
        // 'fogoAmigo': explosão (granada e míssil) machuca o próprio player, o giro da espada não
        private void DanoEmArea(Vector3 centro, float raio, float dano, bool fogoAmigo = false)
        {
            int count = Physics.OverlapSphereNonAlloc(centro, raio, _area,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            _alvosDano.Clear();
            for (int i = 0; i < count; i++)
            {
                if (!fogoAmigo && _area[i].transform.IsChildOf(transform)) continue;

                IDamageable alvo = _area[i].GetComponentInParent<IDamageable>();
                if (alvo != null && _alvosDano.Add(alvo))
                    alvo.TakeDamage(dano);
            }
        }

        // o círculo escuro esvazia durante a recarga e o texto mostra os segundos que faltam
        private void AtualizarHud()
        {
            bool atualizarTexto = Time.time >= _proximaAtualizacaoHud;
            if (atualizarTexto) _proximaAtualizacaoHud = Time.time + 0.1f;

            AtualizarSlot(iconeQ, coberturaQ, textoQ, habilidadeQ, _proximoQ, recargaQ, atualizarTexto);
            AtualizarSlot(iconeE, coberturaE, textoE, habilidadeE, _proximoE, recargaE, atualizarTexto);
        }

        private static void AtualizarSlot(Image icone, Image cobertura, Text texto, Tipo tipo, float proximo, float recarga, bool atualizarTexto)
        {
            float falta = proximo - Time.time;

            if (cobertura != null)
                cobertura.fillAmount = falta > 0f ? Mathf.Clamp01(falta / recarga) : 0f;

            if (texto == null || !atualizarTexto) return;

            bool temIcone = icone != null && icone.sprite != null;
            if (icone != null) icone.enabled = temIcone;

            if (falta > 0f) texto.text = falta.ToString("0.0");
            else texto.text = temIcone ? "" : tipo.ToString();
        }
    }
}
