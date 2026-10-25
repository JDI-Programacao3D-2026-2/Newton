using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Combat
{
    /// Arma do jogador: o clique esquerdo atira (pool de projéteis, munição e recarga com R).
    /// Com 'Corpo A Corpo' ligado (Espadachim) o clique golpeia com a espada. Também mostra o placar (Tab).
    public class WeaponPlayer : MonoBehaviour
    {
        [SerializeField] private WeaponData dadosArma;
        [Tooltip("Ponto de saída do projétil (ponta do cano). Só a posição: a direção vem da mira.")]
        [SerializeField] private Transform bocaDoCano;
        [Tooltip("Projéteis criados já no início, pra não dar Instantiate no meio da partida")]
        [SerializeField, Min(0)] private int tamanhoPool = 20;

        [Header("HUD (Canvas)")]
        [SerializeField] private Text textoMunicao;
        [Tooltip("Painel do placar, mostrado enquanto a tecla Tab está apertada")]
        [SerializeField] private GameObject painelFrags;
        [SerializeField] private Text textoFrags;

        [Header("Corpo a corpo (Espadachim)")]
        [Tooltip("Liga: o clique esquerdo golpeia com a espada em vez de atirar")]
        [SerializeField] private bool corpoACorpo;
        [Tooltip("Animação da espada descendo (componente Animation no objeto Espada)")]
        [SerializeField] private Animation animacaoEspada;
        [SerializeField, Min(0.5f)] private float alcanceEspada = 2.5f;
        [SerializeField, Range(10f, 180f)] private float anguloEspada = 110f;
        [SerializeField, Min(0f)] private float danoEspada = 25f;
        [SerializeField, Min(0.1f)] private float intervaloEspada = 0.6f;
        [Tooltip("Tempo entre começar o golpe e o dano valer")]
        [SerializeField, Min(0f)] private float atrasoGolpe = 0.2f;

        private const float AlcanceDaMira = 200f;

        private readonly Queue<Projectile> _pool = new Queue<Projectile>();
        private readonly RaycastHit[] _aimHits = new RaycastHit[16];
        private readonly Collider[] _golpeColliders = new Collider[32];
        private readonly HashSet<IDamageable> _golpeAlvos = new HashSet<IDamageable>();

        private int _municao;
        private bool _recarregando;
        private float _fimDaRecarga;
        private float _proximoTiro;

        /// Usado pelas habilidades (a rajada precisa saber se este personagem atira).
        public bool CorpoACorpo => corpoACorpo;

        private void Awake()
        {
            if (corpoACorpo) return; // espada: sem arma, munição nem pool

            if (dadosArma == null || dadosArma.ProjectilePrefab == null || bocaDoCano == null)
            {
                Debug.LogError($"{nameof(WeaponPlayer)}: WeaponData, Projectile Prefab ou Muzzle não atribuídos em '{name}'.", this);
                enabled = false;
                return;
            }

            _municao = dadosArma.MagazineSize;

            for (int i = 0; i < tamanhoPool; i++)
                Devolver(CriarProjetil());
        }

        private void Start() => AtualizarTextoMunicao();

        private void Update()
        {
            AtualizarFrags();

            // jogo pausado (confirmação de saída, vitória ou derrota): não atira nem recarrega
            if (Time.timeScale == 0f) return;

            if (_recarregando && Time.time >= _fimDaRecarga) TerminarRecarga();

            if (Keyboard.current.rKey.wasPressedThisFrame) IniciarRecarga();
            if (Mouse.current.leftButton.wasPressedThisFrame) Atirar();
        }

        // ---------- HUD ----------

        // Placar enquanto Tab está apertada
        private void AtualizarFrags()
        {
            bool ver = Keyboard.current.tabKey.isPressed;
            if (painelFrags.activeSelf != ver) painelFrags.SetActive(ver);

            if (ver) textoFrags.text = $"Abates: {EnemyController.Abates}\nRestantes: {EnemyController.Vivos}";
        }

        private void AtualizarTextoMunicao()
        {
            if (corpoACorpo)
            {
                textoMunicao.text = "Espada";
                return;
            }

            textoMunicao.text = _recarregando ? "Recarregando..." : $"Munição: {_municao}/{dadosArma.MagazineSize}" + (_municao == 0 ? "  (R)" : "");
            textoMunicao.color = _municao == 0 ? Color.red : Color.white;
        }

        // ---------- Tiro ----------

        private void IniciarRecarga()
        {
            if (corpoACorpo || _recarregando || _municao >= dadosArma.MagazineSize) return;

            _recarregando = true;
            _fimDaRecarga = Time.time + dadosArma.ReloadTime;
            AtualizarTextoMunicao();
        }

        private void TerminarRecarga()
        {
            _recarregando = false;
            _municao = dadosArma.MagazineSize;
            AtualizarTextoMunicao();
        }

        private void Atirar()
        {
            if (corpoACorpo)
            {
                Golpear();
                return;
            }

            // pente vazio: não atira (a recarga é manual, tecla R)
            if (_recarregando || _municao <= 0 || Time.time < _proximoTiro) return;

            _municao--;
            _proximoTiro = Time.time + dadosArma.FireInterval;
            AtualizarTextoMunicao();

            Disparar();
        }

        // Lança um projétil do pool agora, sem gastar munição nem respeitar intervalo (a rajada usa isso)
        public void Disparar()
        {
            Vector3 direcao = DirecaoDaMira();
            Projectile projetil = _pool.Count > 0 ? _pool.Dequeue() : CriarProjetil();

            projetil.transform.SetPositionAndRotation(bocaDoCano.position, Quaternion.LookRotation(direcao));
            projetil.gameObject.SetActive(true);
            projetil.Launch(direcao, dadosArma.ProjectileSpeed, dadosArma.Damage, dadosArma.ProjectileLifetime, Devolver);
        }

        /// Ponto que o centro da tela acerta (até 'alcance'); sem nada na frente, o ponto mais longe no ar.
        /// As habilidades (fumaça e bombardeio) usam a mesma mira.
        public Vector3 PontoDaMira(float alcance)
        {
            Camera cam = Camera.main;
            if (cam == null) return transform.position + transform.forward * alcance;

            Ray raio = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 ponto = raio.GetPoint(alcance);
            float maisPerto = alcance;

            int count = Physics.RaycastNonAlloc(raio, _aimHits, alcance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (_aimHits[i].transform.IsChildOf(transform) || _aimHits[i].distance >= maisPerto) continue;

                maisPerto = _aimHits[i].distance;
                ponto = _aimHits[i].point;
            }

            return ponto;
        }

        // Do cano até o ponto mirado
        private Vector3 DirecaoDaMira()
        {
            Vector3 direcao = (PontoDaMira(AlcanceDaMira) - bocaDoCano.position).normalized;

            // alvo atrás do cano: usa a frente do corpo
            return Vector3.Dot(direcao, transform.forward) > 0.1f ? direcao : transform.forward;
        }

        // ---------- Pool ----------

        private Projectile CriarProjetil()
        {
            Projectile novo = Instantiate(dadosArma.ProjectilePrefab);
            novo.gameObject.SetActive(false);
            return novo;
        }

        private void Devolver(Projectile projetil)
        {
            if (projetil == null) return;

            projetil.gameObject.SetActive(false);
            _pool.Enqueue(projetil);
        }

        // ---------- Espada ----------

        // A lâmina desce e, no meio do golpe, o dano vale pra quem estiver na frente
        private void Golpear()
        {
            if (Time.time < _proximoTiro) return;

            _proximoTiro = Time.time + intervaloEspada;
            animacaoEspada.Play();
            Invoke(nameof(AplicarGolpe), atrasoGolpe);
        }

        private void AplicarGolpe()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, alcanceEspada, _golpeColliders,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            _golpeAlvos.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider colisor = _golpeColliders[i];
                if (colisor.transform.IsChildOf(transform)) continue;

                IDamageable alvo = colisor.GetComponentInParent<IDamageable>();
                if (alvo == null) continue;

                // só quem está na frente (cone) e uma vez por golpe (o inimigo tem mais de um collider)
                Vector3 direcao = colisor.transform.position - transform.position;
                direcao.y = 0f;
                if (Vector3.Angle(transform.forward, direcao) > anguloEspada * 0.5f) continue;

                if (_golpeAlvos.Add(alvo)) alvo.TakeDamage(danoEspada);
            }
        }
    }
}
