using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Combat
{
    // qualquer coisa que pode levar dano de um Projectile
    public interface IDamageable
    {
        void TakeDamage(float amount);
    }

    // projétil com Rigidbody que o pool do WeaponPlayer reaproveita. a arma chama Launch() depois de ligar a instância,
    // e o próprio projétil pede pra voltar pro pool quando expira ou acerta algo
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class Projectile : MonoBehaviour
    {
        [Header("Explosão (lança-granadas)")]
        [Tooltip("0 = só machuca quem foi atingido. Maior que 0 = explode e machuca todos nesse raio.")]
        [SerializeField, Min(0f)] private float raioExplosao;
        [Tooltip("Particle System filho (prefab Explosao), desligado até explodir. A escala é ajustada ao raio.")]
        [SerializeField] private Transform visualExplosao;

        private Rigidbody _rigidbody;
        private Renderer _corpo;
        private float _damage;
        private float _lifetime;
        private float _elapsedTime;
        private bool _explodindo;
        private Action<Projectile> _releaseToPool;
        private readonly Collider[] _atingidos = new Collider[32];
        private readonly HashSet<IDamageable> _jaMachucados = new HashSet<IDamageable>();

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.useGravity = false;
            GetComponent<Collider>().isTrigger = true;
            _corpo = GetComponent<Renderer>();
        }

        public void Launch(Vector3 direction, float speed, float damage, float lifetime, Action<Projectile> releaseToPool)
        {
            _damage = damage;
            _lifetime = lifetime;
            _elapsedTime = 0f;
            _explodindo = false;
            _releaseToPool = releaseToPool;

            // volta ao normal (pode ter explodido no uso anterior)
            if (_corpo != null) _corpo.enabled = true;
            if (visualExplosao != null) visualExplosao.gameObject.SetActive(false);

            transform.rotation = Quaternion.LookRotation(direction);
            _rigidbody.linearVelocity = direction.normalized * speed;
        }

        private void Update()
        {
            if (_explodindo) return;

            _elapsedTime += Time.deltaTime;
            if (_elapsedTime < _lifetime) return;

            // granada que não acertou nada explode no ar
            if (raioExplosao > 0f) StartCoroutine(Explodir());
            else Release();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_explodindo) return;

            if (raioExplosao > 0f)
            {
                StartCoroutine(Explodir());
                return;
            }

            if (other.TryGetComponent(out IDamageable damageable))
                damageable.TakeDamage(_damage);

            Release();
        }

        // dano em área (fogo amigo: machuca o próprio player também) e a explosão em partículas
        private IEnumerator Explodir()
        {
            _explodindo = true;
            _rigidbody.linearVelocity = Vector3.zero;

            int count = Physics.OverlapSphereNonAlloc(transform.position, raioExplosao, _atingidos,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            _jaMachucados.Clear();
            for (int i = 0; i < count; i++)
            {
                IDamageable alvo = _atingidos[i].GetComponentInParent<IDamageable>();
                if (alvo != null && _jaMachucados.Add(alvo))
                    alvo.TakeDamage(_damage);
            }

            if (_corpo != null) _corpo.enabled = false;
            if (visualExplosao != null)
            {
                // a escala do filho é relativa ao projétil: compensa pra explosão ter o diâmetro do raio
                visualExplosao.localScale = Vector3.one * (raioExplosao * 2f / transform.lossyScale.x);
                visualExplosao.gameObject.SetActive(true);

                yield return new WaitForSeconds(0.8f); // tempo das partículas
            }

            Release();
        }

        private void Release()
        {
            _rigidbody.linearVelocity = Vector3.zero;
            gameObject.SetActive(false);
            _releaseToPool?.Invoke(this);
        }
    }
}