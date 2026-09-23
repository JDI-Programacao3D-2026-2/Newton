using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Combat
{

    /// Arma do jogador. Atira na direção "forward" deste GameObject (o Player) ao clicar, respeitando
    /// o fire rate. A direção é deliberadamente independente da rotação do Muzzle/mesh da arma — o
    /// modelo visual pode estar rotacionado (ex: cilindro deitado) sem afetar pra onde o tiro vai.
    /// Faz pooling simples dos projéteis pra evitar Instantiate/Destroy a cada tiro.
  
    public class WeaponPlayer : MonoBehaviour
    {
        [SerializeField] private WeaponData weaponData;
        [Tooltip("Ponto de saída do projétil (ponta do cano). Usado só como POSIÇÃO de spawn — a direção do tiro é o forward do Player.")]
        [SerializeField] private Transform muzzle;

        private readonly Queue<Projectile> _pooledProjectiles = new Queue<Projectile>();
        private float _nextFireTime;

        private void Awake()
        {
            if (weaponData == null || weaponData.ProjectilePrefab == null || muzzle == null)
            {
                Debug.LogError($"{nameof(WeaponPlayer)}: WeaponData, Projectile Prefab ou Muzzle não atribuídos em '{name}'.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                TryFire();
        }

        public void TryFire()
        {
            if (Time.time < _nextFireTime) return;

            _nextFireTime = Time.time + weaponData.FireInterval;

            Vector3 direction = transform.forward;
            Projectile projectile = GetPooledProjectile();
            projectile.transform.SetPositionAndRotation(muzzle.position, Quaternion.LookRotation(direction));
            projectile.gameObject.SetActive(true);
            projectile.Launch(direction, weaponData.ProjectileSpeed, weaponData.Damage, weaponData.ProjectileLifetime, ReturnProjectileToPool);
        }

        private Projectile GetPooledProjectile() =>
            _pooledProjectiles.Count > 0 ? _pooledProjectiles.Dequeue() : CreateProjectile();

        private Projectile CreateProjectile()
        {
            Projectile instance = Instantiate(weaponData.ProjectilePrefab);
            instance.gameObject.SetActive(false);
            return instance;
        }

        private void ReturnProjectileToPool(Projectile projectile)
        {
            if (projectile == null) return; // pode ter sido destruído (ex: troca de cena)

            projectile.gameObject.SetActive(false);
            _pooledProjectiles.Enqueue(projectile);
        }
    }
}