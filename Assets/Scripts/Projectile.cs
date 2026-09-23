using System;
using UnityEngine;

namespace Game.Combat
{
   
   /// Contrato para qualquer alvo que possa receber dano de um Projectile.
  
    public interface IDamageable
    {
        void TakeDamage(float amount);
    }


    /// Projétil físico (Rigidbody) reaproveitável pelo pool do WeaponPlayer. A arma chama Launch()
    /// logo após reativar a instância; o próprio projétil pede pra ser devolvido ao expirar ou acertar algo.

    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class Projectile : MonoBehaviour
    {
        private Rigidbody _rigidbody;
        private float _damage;
        private float _lifetime;
        private float _elapsedTime;
        private Action<Projectile> _releaseToPool;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.useGravity = false;
            GetComponent<Collider>().isTrigger = true;
        }

        public void Launch(Vector3 direction, float speed, float damage, float lifetime, Action<Projectile> releaseToPool)
        {
            _damage = damage;
            _lifetime = lifetime;
            _elapsedTime = 0f;
            _releaseToPool = releaseToPool ?? throw new ArgumentNullException(nameof(releaseToPool));

            transform.rotation = Quaternion.LookRotation(direction);
            _rigidbody.linearVelocity = direction.normalized * speed;
        }

        private void Update()
        {
            _elapsedTime += Time.deltaTime;
            if (_elapsedTime >= _lifetime)
                Release();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out IDamageable damageable))
                damageable.TakeDamage(_damage);

            Release();
        }

        private void Release()
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _releaseToPool?.Invoke(this);
        }
    }
}