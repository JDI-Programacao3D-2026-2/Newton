using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

// câmera de terceira pessoa por cima do ombro do player
public class MouseOrbit : MonoBehaviour
{
    [FormerlySerializedAs("player")] public Transform jogador;
    [FormerlySerializedAs("MouseSensitivity")] public float sensibilidadeMouse = 0.2f;

    [Header("Terceira pessoa (ombro)")]
    [Tooltip("Altura do ponto que a câmera mira, a partir do pivô do jogador (cabeça/ombros).")]
    public float alturaPivo = 1.6f;
    [Tooltip("Distância da câmera atrás do jogador.")]
    public float distanciaCamera = 3f;
    [Tooltip("Deslocamento lateral (over-the-shoulder). Positivo = ombro direito, negativo = esquerdo.")]
    public float deslocamentoOmbro = 0.8f;
    [Tooltip("Até onde dá pra olhar pra CIMA (graus).")]
    public float anguloOlharCima = 55f;
    [Tooltip("Até onde dá pra olhar pra BAIXO (graus).")]
    public float anguloOlharBaixo = 60f;

    [Header("Colisão da câmera")]
    [Tooltip("Camadas que a câmera não atravessa (paredes, chão). A layer do jogador é ignorada sempre.")]
    public LayerMask camadasColisao = ~0;
    public float raioColisao = 0.25f;

    private Transform _camera;
    private float rotX;
    private float rotY;

    private void Awake()
    {
        // desliga o Cinemachine, quem controla essa câmera é esse script
        CinemachineCamera cinemachineCam = GetComponent<CinemachineCamera>();
        if (cinemachineCam != null) cinemachineCam.enabled = false;

        _camera = Camera.main != null ? Camera.main.transform : transform;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (jogador != null) rotX = jogador.eulerAngles.y;
    }

    private void LateUpdate()
    {
        // player gerado pelo MapaGerador: procura pela tag
        if (jogador == null)
        {
            GameObject achado = GameObject.FindGameObjectWithTag("Player");
            if (achado == null) return;

            jogador = achado.transform;
            rotX = jogador.eulerAngles.y;
        }

        if (Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue() * sensibilidadeMouse;
            rotX += delta.x;
            rotY -= delta.y;
            rotY = Mathf.Clamp(rotY, -anguloOlharCima, anguloOlharBaixo);
        }

        Quaternion rotation = Quaternion.Euler(rotY, rotX, 0f);
        Vector3 pivot = jogador.position + Vector3.up * alturaPivo;

        Vector3 shoulder = pivot + rotation * Vector3.right * deslocamentoOmbro;
        Vector3 back = rotation * Vector3.back;
        float distance = distanciaCamera;

        // aproxima a câmera se tiver parede no caminho
        int mask = camadasColisao.value & ~(1 << jogador.gameObject.layer);
        if (Physics.SphereCast(shoulder, raioColisao, back, out RaycastHit hit, distanciaCamera, mask,
                QueryTriggerInteraction.Ignore))
        {
            distance = Mathf.Max(0.1f, hit.distance);
        }

        Vector3 position = shoulder + back * distance;

        _camera.SetPositionAndRotation(position, rotation);

        // mantém esse objeto na mesma posição da câmera
        if (_camera != transform) transform.SetPositionAndRotation(position, rotation);
    }
}
