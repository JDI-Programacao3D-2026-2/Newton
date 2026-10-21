using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Combat
{
    /// Duas habilidades por personagem, nas teclas Q e E. Cada prefab escolhe no Inspector
    /// qual habilidade fica em cada tecla (Atirador: Rajada + Granada, Espadachim: Giro + Stun).
    public class Habilidades : MonoBehaviour
    {
        public enum Tipo { Rajada, Granada, Giro, Stun }

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
        [Tooltip("Esfera que cresce na explosão (sem collider)")]
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

        private WeaponPlayer _arma;
        private Collider[] _meusColliders;
        private readonly Queue<Rigidbody> _granadas = new Queue<Rigidbody>();
        private readonly Queue<Transform> _explosoes = new Queue<Transform>();
        private readonly Collider[] _area = new Collider[64];
        private readonly HashSet<IDamageable> _alvosDano = new HashSet<IDamageable>();
        private readonly HashSet<EnemyController> _alvosStun = new HashSet<EnemyController>();

        private float _proximoQ;
        private float _proximoE;
        private float _proximaAtualizacaoHud;

        private void Awake()
        {
            _arma = GetComponent<WeaponPlayer>();
            _meusColliders = GetComponentsInChildren<Collider>();

            // pools criados já no início, pra não dar Instantiate no meio da partida
            if (prefabGranada != null)
            {
                for (int i = 0; i < tamanhoPool; i++)
                {
                    Rigidbody g = Instantiate(prefabGranada);
                    g.gameObject.SetActive(false);
                    _granadas.Enqueue(g);
                }
            }

            if (prefabExplosao != null)
            {
                for (int i = 0; i < tamanhoPool; i++)
                {
                    Transform e = Instantiate(prefabExplosao);
                    e.gameObject.SetActive(false);
                    _explosoes.Enqueue(e);
                }
            }
        }

        private void Update()
        {
            // pausado (sair, vitória ou derrota): não usa habilidades
            if (Keyboard.current != null && Time.timeScale > 0f)
            {
                if (Keyboard.current.qKey.wasPressedThisFrame) Usar(habilidadeQ, ref _proximoQ, recargaQ);
                if (Keyboard.current.eKey.wasPressedThisFrame) Usar(habilidadeE, ref _proximoE, recargaE);
            }

            AtualizarHud();
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
                default: return false;
            }
        }

        // ---------- Rajada ----------

        private bool Rajada()
        {
            if (_arma == null || _arma.CorpoACorpo) return false;

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

        // ---------- Granada ----------

        private bool Granada()
        {
            if (_granadas.Count == 0) return false;

            Rigidbody granada = _granadas.Dequeue();
            granada.transform.position = transform.position + transform.forward + Vector3.up * 0.8f;
            granada.gameObject.SetActive(true);

            // não bate no próprio jogador que acabou de jogar
            Collider colliderGranada = granada.GetComponent<Collider>();
            foreach (Collider meu in _meusColliders)
                Physics.IgnoreCollision(colliderGranada, meu, true);

            Vector3 frente = Camera.main != null ? Camera.main.transform.forward : transform.forward;
            granada.linearVelocity = (frente + Vector3.up * 0.3f).normalized * forcaArremesso;
            granada.angularVelocity = Random.insideUnitSphere * 5f;

            StartCoroutine(GranadaRotina(granada));
            return true;
        }

        private IEnumerator GranadaRotina(Rigidbody granada)
        {
            yield return new WaitForSeconds(pavio);

            Vector3 posicao = granada.position;
            granada.linearVelocity = Vector3.zero;
            granada.gameObject.SetActive(false);
            _granadas.Enqueue(granada);

            DanoEmArea(posicao, raioGranada, danoGranada);
            yield return ExplosaoRotina(posicao);
        }

        // a esfera cresce até o raio da explosão e some
        private IEnumerator ExplosaoRotina(Vector3 posicao)
        {
            if (_explosoes.Count == 0) yield break;

            Transform esfera = _explosoes.Dequeue();
            esfera.position = posicao;
            esfera.gameObject.SetActive(true);

            const float duracao = 0.25f;
            for (float t = 0f; t < duracao; t += Time.deltaTime)
            {
                esfera.localScale = Vector3.one * Mathf.Lerp(0.2f, raioGranada * 2f, t / duracao);
                yield return null;
            }

            esfera.gameObject.SetActive(false);
            _explosoes.Enqueue(esfera);
        }

        // ---------- Giro ----------

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

        // ---------- Stun ----------

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

        // ---------- Comum ----------

        // Dano em todos os IDamageable no raio (menos o próprio jogador), uma vez por alvo
        private void DanoEmArea(Vector3 centro, float raio, float dano)
        {
            int count = Physics.OverlapSphereNonAlloc(centro, raio, _area,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            _alvosDano.Clear();
            for (int i = 0; i < count; i++)
            {
                if (_area[i].transform.IsChildOf(transform)) continue;

                IDamageable alvo = _area[i].GetComponentInParent<IDamageable>();
                if (alvo != null && _alvosDano.Add(alvo))
                    alvo.TakeDamage(dano);
            }
        }

        // O círculo escuro esvazia durante a recarga; o texto mostra os segundos que faltam
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
