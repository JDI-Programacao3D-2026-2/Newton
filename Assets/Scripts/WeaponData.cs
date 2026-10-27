using UnityEngine;

namespace Game.Combat
{
    // dados de uma arma, dá pra mexer no Inspector sem mexer no código
    [CreateAssetMenu(fileName = "NewWeaponData", menuName = "Game/Combat/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Tooltip("Tiros por segundo.")]
        [SerializeField, Min(0.01f)] private float cadenciaTiro = 5f;
        [SerializeField, Min(0f)] private float dano = 10f;

        [SerializeField] private Projectile prefabProjetil;
        [SerializeField, Min(0.01f)] private float velocidadeProjetil = 60f;
        [SerializeField, Min(0.01f)] private float tempoVidaProjetil = 4f;

        [Tooltip("Balas por pente. Ao zerar (ou apertar R) é preciso recarregar.")]
        [SerializeField, Min(1)] private int tamanhoPente = 8;
        [Tooltip("Tempo de recarga em segundos.")]
        [SerializeField, Min(0f)] private float tempoRecarga = 1.5f;

        public int MagazineSize => tamanhoPente;
        public float ReloadTime => tempoRecarga;
        public float Damage => dano;
        public Projectile ProjectilePrefab => prefabProjetil;
        public float ProjectileSpeed => velocidadeProjetil;
        public float ProjectileLifetime => tempoVidaProjetil;

        public float FireInterval => 1f / cadenciaTiro;
    }
}