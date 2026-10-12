using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace Game.Combat
{

  
    public class WeaponPlayer : MonoBehaviour
    {
        [FormerlySerializedAs("weaponData")] [SerializeField] private WeaponData dadosArma;
        [Tooltip("Ponto de saída do projétil (ponta do cano). Usado só como POSIÇÃO de spawn — a direção do tiro é o forward do Player.")]
        [FormerlySerializedAs("muzzle")] [SerializeField] private Transform bocaDoCano;

        private readonly Queue<Projectile> _pooledProjectiles = new Queue<Projectile>();
        private float _nextFireTime;

        private int _currentAmmo;
        private float _reloadEndTime;

        public int CurrentAmmo => _currentAmmo;
        public int MagazineSize => dadosArma != null ? dadosArma.MagazineSize : 0;
        public bool IsReloading { get; private set; }

        public event System.Action<int, int> AmmoChanged;
        public event System.Action<float> ReloadStarted;
        public event System.Action ReloadFinished;

        private void Awake()
        {
            if (dadosArma == null || dadosArma.ProjectilePrefab == null || bocaDoCano == null)
            {
                Debug.LogError($"{nameof(WeaponPlayer)}: WeaponData, Projectile Prefab ou Muzzle não atribuídos em '{name}'.", this);
                enabled = false;
                return;
            }

            _currentAmmo = dadosArma.MagazineSize;
        }

        private void Start() => AmmoChanged?.Invoke(_currentAmmo, MagazineSize);

        private void Update()
        {
            // vitória: ENTER reinicia a fase
            if (EnemyController.AllDefeated)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                if (Keyboard.current != null &&
                    (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

                return;
            }

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
            if (dadosArma == null) return;

            if (EnemyController.AllDefeated)
            {
                var titulo = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                titulo.normal.textColor = Color.green;
                var subtitulo = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
                subtitulo.normal.textColor = Color.white;

                GUI.Label(new Rect(0f, Screen.height * 0.35f, Screen.width, 90f), "Você venceu!", titulo);
                GUI.Label(new Rect(0f, Screen.height * 0.35f + 90f, Screen.width, 50f), "Aperte ENTER para jogar de novo", subtitulo);
                return;
            }

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.Box(new Rect(cx - 2f, cy - 2f, 4f, 4f), GUIContent.none);

            var estilo = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            estilo.normal.textColor = _currentAmmo == 0 ? Color.red : Color.white;

            string texto = IsReloading ? "Recarregando..." : $"Munição: {_currentAmmo}/{dadosArma.MagazineSize}";
            if (!IsReloading && _currentAmmo == 0) texto += "  (R)";

            GUI.Label(new Rect(Screen.width - 420f, Screen.height - 70f, 400f, 50f), texto, estilo);
        }

        public void StartReload()
        {
            if (IsReloading || _currentAmmo >= dadosArma.MagazineSize) return;

            IsReloading = true;
            _reloadEndTime = Time.time + dadosArma.ReloadTime;
            ReloadStarted?.Invoke(dadosArma.ReloadTime);
        }

        private void FinishReload()
        {
            IsReloading = false;
            _currentAmmo = dadosArma.MagazineSize;
            AmmoChanged?.Invoke(_currentAmmo, MagazineSize);
            ReloadFinished?.Invoke();
        }

        public void TryFire()
        {
            if (IsReloading || Time.time < _nextFireTime) return;

            // Pente vazio: não atira. A recarga é manual (tecla R).
            if (_currentAmmo <= 0) return;

            _currentAmmo--;
            AmmoChanged?.Invoke(_currentAmmo, MagazineSize);
            _nextFireTime = Time.time + dadosArma.FireInterval;

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