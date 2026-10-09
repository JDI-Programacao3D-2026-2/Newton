using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// Câmera de ombro (terceira pessoa) travada no jogador: o mouse gira a mira livremente, a câmera fica
/// atrás e ao lado do ombro e não atravessa paredes.
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
        if (jogador == null)
        {
            Debug.LogError($"{nameof(MouseOrbit)}: jogador não atribuído em '{name}'.", this);
            enabled = false;
            return;
        }

        // Cinemachine fora do caminho: quem posiciona a câmera é este script.
        CinemachineCamera cinemachineCam = GetComponent<CinemachineCamera>();
        if (cinemachineCam != null) cinemachineCam.enabled = false;

        _camera = Camera.main != null ? Camera.main.transform : transform;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Começa olhando na mesma direção do jogador.
        rotX = jogador.eulerAngles.y;
    }

    private void LateUpdate()
    {
        if (Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue() * sensibilidadeMouse;
            rotX += delta.x;
            rotY -= delta.y;
            rotY = Mathf.Clamp(rotY, -anguloOlharCima, anguloOlharBaixo);
        }

        Quaternion rotation = Quaternion.Euler(rotY, rotX, 0f);
        Vector3 pivot = jogador.position + Vector3.up * alturaPivo;

        // Começa no ombro (lateral) e recua pra trás a partir dali.
        Vector3 shoulder = pivot + rotation * Vector3.right * deslocamentoOmbro;
        Vector3 back = rotation * Vector3.back;
        float distance = distanciaCamera;

        // Se algo estiver entre o ombro e a câmera, aproxima a câmera pra não atravessar a parede.
        int mask = camadasColisao.value & ~(1 << jogador.gameObject.layer);
        if (Physics.SphereCast(shoulder, raioColisao, back, out RaycastHit hit, distanciaCamera, mask,
                QueryTriggerInteraction.Ignore))
        {
            distance = Mathf.Max(0.1f, hit.distance);
        }

        Vector3 position = shoulder + back * distance;

        _camera.SetPositionAndRotation(position, rotation);

        // O PlayerMovement pode ter este objeto como referência de câmera: mantém os dois iguais.
        if (_camera != transform) transform.SetPositionAndRotation(position, rotation);
    }
}
