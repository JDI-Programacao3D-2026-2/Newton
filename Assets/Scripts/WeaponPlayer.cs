using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Game.Combat
{

  
    public class WeaponPlayer : MonoBehaviour
    {
        [FormerlySerializedAs("weaponData")] [SerializeField] private WeaponData dadosArma;
        [Tooltip("Ponto de saída do projétil (ponta do cano). Usado só como POSIÇÃO de spawn — a direção do tiro é o forward do Player.")]
        [FormerlySerializedAs("muzzle")] [SerializeField] private Transform bocaDoCano;

        [Header("HUD (Canvas)")]
        [SerializeField] private Text textoMunicao;
        [Tooltip("Painel do placar, mostrado enquanto a tecla Tab está apertada")]
        [SerializeField] private GameObject painelFrags;
        [SerializeField] private Text textoFrags;

        [Header("Pool de projéteis")]
        [Tooltip("Projéteis criados já no início, pra não dar Instantiate no meio da partida")]
        [SerializeField, Min(0)] private int tamanhoPool = 20;

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

        private readonly Collider[] _golpeColliders = new Collider[32];
        private readonly HashSet<IDamageable> _golpeAlvos = new HashSet<IDamageable>();

        private readonly Queue<Projectile> _pooledProjectiles = new Queue<Projectile>();
        private float _nextFireTime;

        private int _currentAmmo;
        private float _reloadEndTime;

        public int CurrentAmmo => _currentAmmo;
        public int MagazineSize => dadosArma != null ? dadosArma.MagazineSize : 0;
        public bool IsReloading { get; private set; }
        public bool CorpoACorpo => corpoACorpo;

        public event System.Action<int, int> AmmoChanged;
        public event System.Action<float> ReloadStarted;
        public event System.Action ReloadFinished;

        private void Awake()
        {
            if (corpoACorpo) return; // espada: sem arma, munição nem pool

            if (dadosArma == null || dadosArma.ProjectilePrefab == null || bocaDoCano == null)
            {
                Debug.LogError($"{nameof(WeaponPlayer)}: WeaponData, Projectile Prefab ou Muzzle não atribuídos em '{name}'.", this);
                enabled = false;
                return;
            }

            _currentAmmo = dadosArma.MagazineSize;

            for (int i = 0; i < tamanhoPool; i++)
                ReturnProjectileToPool(CreateProjectile());
        }

        private void Start()
        {
            AmmoChanged?.Invoke(_currentAmmo, MagazineSize);
            AtualizarTextoMunicao();
        }

        private void Update()
        {
            AtualizarFrags();

            // jogo pausado (confirmação de saída, vitória ou derrota): não atira nem recarrega
            if (Time.timeScale == 0f) return;

            if (IsReloading && Time.time >= _reloadEndTime)
                FinishReload();

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                StartReload();

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                TryFire();
        }

        // HUD: mira, munição e recarga
        private void OnGUI()
        {
            if (dadosArma == null || Time.timeScale == 0f) return;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.Box(new Rect(cx - 2f, cy - 2f, 4f, 4f), GUIContent.none);
        }

        // Placar enquanto Tab está apertada
        private void AtualizarFrags()
        {
            if (painelFrags == null) return;

            bool ver = Keyboard.current != null && Keyboard.current.tabKey.isPressed;
            if (painelFrags.activeSelf != ver) painelFrags.SetActive(ver);

            if (ver && textoFrags != null)
                textoFrags.text = $"Abates: {EnemyController.Abates}\nRestantes: {EnemyController.Vivos}";
        }

        // HUD de munição no Canvas (o texto é atribuído no Inspector)
        private void AtualizarTextoMunicao()
        {
            if (textoMunicao == null) return;

            if (corpoACorpo)
            {
                textoMunicao.text = "Espada";
                return;
            }

            string texto = IsReloading ? "Recarregando..." : $"Munição: {_currentAmmo}/{dadosArma.MagazineSize}";
            if (!IsReloading && _currentAmmo == 0) texto += "  (R)";

            textoMunicao.text = texto;
            textoMunicao.color = _currentAmmo == 0 ? Color.red : Color.white;
        }

        public void StartReload()
        {
            if (corpoACorpo || IsReloading || _currentAmmo >= dadosArma.MagazineSize) return;

            IsReloading = true;
            _reloadEndTime = Time.time + dadosArma.ReloadTime;
            ReloadStarted?.Invoke(dadosArma.ReloadTime);
            AtualizarTextoMunicao();
        }

        private void FinishReload()
        {
            IsReloading = false;
            _currentAmmo = dadosArma.MagazineSize;
            AmmoChanged?.Invoke(_currentAmmo, MagazineSize);
            ReloadFinished?.Invoke();
            AtualizarTextoMunicao();
        }

        // Espada: a lâmina desce e, no meio do golpe, o dano vale pra quem estiver na frente
        private void Golpear()
        {
            if (Time.time < _nextFireTime) return;

            _nextFireTime = Time.time + intervaloEspada;
            if (animacaoEspada != null) animacaoEspada.Play();
            Invoke(nameof(AplicarGolpe), atrasoGolpe);
        }

        private void AplicarGolpe()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, alcanceEspada, _golpeColliders,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            _golpeAlvos.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider alvoCollider = _golpeColliders[i];
                if (alvoCollider.transform.IsChildOf(transform)) continue;

                IDamageable alvo = alvoCollider.GetComponentInParent<IDamageable>();
                if (alvo == null) continue;

                // só quem está na frente (cone) e uma vez por golpe (o inimigo tem mais de um collider)
                Vector3 direcao = alvoCollider.transform.position - transform.position;
                direcao.y = 0f;
                if (Vector3.Angle(transform.forward, direcao) > anguloEspada * 0.5f) continue;

                if (_golpeAlvos.Add(alvo))
                    alvo.TakeDamage(danoEspada);
            }
        }

        public void TryFire()
        {
            if (corpoACorpo)
            {
                Golpear();
                return;
            }

            if (IsReloading || Time.time < _nextFireTime) return;

            // Pente vazio: não atira. A recarga é manual (tecla R).
            if (_currentAmmo <= 0) return;

            _currentAmmo--;
            AmmoChanged?.Invoke(_currentAmmo, MagazineSize);
            AtualizarTextoMunicao();
            _nextFireTime = Time.time + dadosArma.FireInterval;

            Disparar();
        }

        // Lança um projétil do pool agora, sem gastar munição nem respeitar intervalo (a rajada usa isso)
        public void Disparar()
        {
            Vector3 direction = GetAimDirection();
            Projectile projectile = GetPooledProjectile();
            projectile.transform.SetPositionAndRotation(bocaDoCano.position, Quaternion.LookRotation(direction));
            projectile.gameObject.SetActive(true);
            projectile.Launch(direction, dadosArma.ProjectileSpeed, dadosArma.Damage, dadosArma.ProjectileLifetime, ReturnProjectileToPool);
        }

        private readonly RaycastHit[] _aimHits = new RaycastHit[16];

       
        private Vector3 GetAimDirection()
        {
            Camera cam = Camera.main;
            if (cam == null) return transform.forward;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 target = ray.GetPoint(AimMaxDistance);
            float nearest = AimMaxDistance;

            int count = Physics.RaycastNonAlloc(ray, _aimHits, AimMaxDistance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (_aimHits[i].transform.IsChildOf(transform)) continue;
                if (_aimHits[i].distance < nearest)
                {
                    nearest = _aimHits[i].distance;
                    target = _aimHits[i].point;
                }
            }

            Vector3 direction = (target - bocaDoCano.position).normalized;

            // alvo atrás do cano: usa a frente do corpo
            return Vector3.Dot(direction, transform.forward) > 0.1f ? direction : transform.forward;
        }

        private const float AimMaxDistance = 200f;

        private Projectile GetPooledProjectile() =>
            _pooledProjectiles.Count > 0 ? _pooledProjectiles.Dequeue() : CreateProjectile();

        private Projectile CreateProjectile()
        {
            Projectile instance = Instantiate(dadosArma.ProjectilePrefab);
            instance.gameObject.SetActive(false);
            return instance;
        }

        private void ReturnProjectileToPool(Projectile projectile)
        {
            if (projectile == null) return;

            projectile.gameObject.SetActive(false);
            _pooledProjectiles.Enqueue(projectile);
        }
    }
}