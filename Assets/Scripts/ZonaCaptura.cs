using System.Collections.Generic;
using UnityEngine;

namespace Game.Combat
{
    /// Zona de captura: ficar dentro do círculo enche a barra; ao chegar em 100% a zona é capturada.
    /// Capturadas todas as zonas do mapa, o jogador vence (quem confere isso é o PlayerMove).
    public class ZonaCaptura : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float raio = 5f;
        [Tooltip("Segundos dentro da zona pra capturar")]
        [SerializeField, Min(1f)] private float tempoCaptura = 8f;
        [Tooltip("Disco colorido: vermelho (vazia) até verde (capturada)")]
        [SerializeField] private Renderer disco;

        // placar de todas as zonas do mapa
        public static int Total { get; private set; }
        public static int Capturadas { get; private set; }
        public static bool TodasCapturadas => Total > 0 && Capturadas >= Total;

        // todas as zonas do mapa (o gerador de inimigos usa pra mandar defensores)
        public static readonly List<ZonaCaptura> Ativas = new List<ZonaCaptura>();
        public bool Capturada => _capturada;

        // progresso da zona onde o jogador está agora (-1 se ele não está em nenhuma)
        public static float ProgressoAtual => Time.frameCount - _frameDentro <= 1 ? _progressoDentro : -1f;
        private static int _frameDentro = -10;
        private static float _progressoDentro;

        private Transform _jogador;
        private MaterialPropertyBlock _tinta;
        private float _progresso;
        private bool _capturada;

        private void Awake()
        {
            Total++;
            Ativas.Add(this);
        }

        private void OnDestroy()
        {
            Total--;
            Ativas.Remove(this);
            if (_capturada) Capturadas--;
        }

        private void Update()
        {
            if (_capturada) return;

            if (_jogador == null)
            {
                GameObject achado = GameObject.FindGameObjectWithTag("Player");
                if (achado == null) return;
                _jogador = achado.transform;
            }

            // distância no plano (ignora a altura)
            Vector3 diferenca = _jogador.position - transform.position;
            diferenca.y = 0f;
            bool dentro = diferenca.magnitude <= raio;

            if (dentro)
            {
                _progresso += Time.deltaTime / tempoCaptura;
                _progressoDentro = _progresso;
                _frameDentro = Time.frameCount;
            }
            else
            {
                // fora da zona o progresso escorre devagar
                _progresso -= Time.deltaTime / tempoCaptura * 0.5f;
            }

            _progresso = Mathf.Clamp01(_progresso);
            Pintar();

            if (_progresso >= 1f)
            {
                _capturada = true;
                Capturadas++;
            }
        }

        private void Pintar()
        {
            if (disco == null) return;

            _tinta ??= new MaterialPropertyBlock();
            _tinta.SetColor("_BaseColor", Color.Lerp(new Color(0.9f, 0.15f, 0.15f), new Color(0.2f, 0.9f, 0.3f), _progresso));
            disco.SetPropertyBlock(_tinta);
        }
    }
}
