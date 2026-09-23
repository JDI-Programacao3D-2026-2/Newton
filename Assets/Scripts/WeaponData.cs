using UnityEngine;

namespace Game.Combat
{
    /// Dados de configuração de uma arma, editáveis no Inspector sem tocar em código.
    [CreateAssetMenu(fileName = "NewWeaponData", menuName = "Game/Combat/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Tooltip("Tiros por segundo.")]
        [SerializeField, Min(0.01f)] private float fireRate = 5f;
        [SerializeField, Min(0f)] private float damage = 10f;

        [SerializeField] private Projectile projectilePrefab;
        [SerializeField, Min(0.01f)] private float projectileSpeed = 60f;
        [SerializeField, Min(0.01f)] private float projectileLifetime = 4f;

        public float Damage => damage;
        public Projectile ProjectilePrefab => projectilePrefab;
        public float ProjectileSpeed => projectileSpeed;
        public float ProjectileLifetime => projectileLifetime;

        /// <summary>Tempo entre disparos (segundos), derivado do fire rate.</summary>
        public float FireInterval => 1f / fireRate;
    }
}