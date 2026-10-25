using Game.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// Jogador: movimento (WASD, pulo, dash), vida e as telas de sair, vitória e derrota.
public class PlayerMove : MonoBehaviour, IDamageable
{
    [Header("Movimento")]
    [SerializeField] private float velocidade = 5f;
    [SerializeField] private float velocidadeRotacao = 10f;
    [SerializeField] private Transform transformCamera;
    [Tooltip("Corpo sempre virado pra onde a câmera olha (hero shooter).")]
    [SerializeField] private bool olharParaCamera = true;

    [Header("Pulo")]
    [SerializeField] private float forcaPulo = 7f;
    [SerializeField] private float distanciaChao = 1.1f;

    [Header("Dash")]
    [SerializeField] private float velocidadeDash = 20f;
    [SerializeField] private float duracaoDash = 0.15f;
    [SerializeField] private float recargaDash = 0.8f;

    [Header("Vida (Canvas)")]
    [SerializeField] private float vidaMaxima = 100f;
    [Tooltip("Image do coração, com Image Type = Filled (Vertical).")]
    [SerializeField] private Image coracao;
    [SerializeField] private Text textoVida;

    [Header("Zonas de captura (Canvas)")]
    [Tooltip("Texto do topo da tela: zonas capturadas e progresso da zona atual")]
    [SerializeField] private Text textoZonas;

    [Header("Tela de sair / vitória / derrota (Canvas)")]
    [SerializeField] private GameObject painelFim;
    [SerializeField] private Text textoFim;
    [SerializeField] private string nomeCenaMenu = "Menu";

    private enum Tela { Jogando, Pausa, Venceu, Perdeu }
    private Tela _tela = Tela.Jogando;

    private Rigidbody rb;
    private Vector2 moveInput;
    private bool jumpPressed;
    private bool dashPressed;
    private bool isDashing;
    private float dashTimer;
    private float lastDashTime = -999f;
    private Vector3 dashDirection;
    private float _vida;
    private string _ultimoTextoZonas;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // sem câmera atribuída (player gerado), usa a principal
        if (transformCamera == null) transformCamera = Camera.main.transform;

        _vida = vidaMaxima;
        AtualizarHudVida();
    }

    private void Update()
    {
        AtualizarHudZonas();
        AtualizarTela();

        // pausado (sair, vitória ou derrota): não lê movimento
        if (_tela == Tela.Jogando) LerTeclas();
    }

    private void FixedUpdate()
    {
        if (isDashing)
        {
            Dash();
            return;
        }

        Mover();

        if (jumpPressed) Pular();
        if (dashPressed) IniciarDash();
        jumpPressed = false;
        dashPressed = false;
    }

    // ---------- Vida ----------

    public void TakeDamage(float amount)
    {
        if (_vida <= 0f) return;

        _vida = Mathf.Max(0f, _vida - amount);
        AtualizarHudVida();

        if (_vida <= 0f) MostrarTela(Tela.Perdeu, "VOCE PERDEU!\n\nENTER = jogar de novo\nESC = menu");
    }

    private void AtualizarHudVida()
    {
        coracao.fillAmount = _vida / vidaMaxima;
        textoVida.text = Mathf.CeilToInt(_vida).ToString();
    }

    // "ZONAS 1/3" no topo da tela, com a porcentagem enquanto o jogador captura uma zona
    private void AtualizarHudZonas()
    {
        string texto = "";
        if (ZonaCaptura.Total > 0)
        {
            texto = $"ZONAS {ZonaCaptura.Capturadas}/{ZonaCaptura.Total}";

            float progresso = ZonaCaptura.ProgressoAtual;
            if (progresso >= 0f) texto += $"  -  capturando {Mathf.FloorToInt(progresso * 100f)}%";
        }

        // só reescreve quando muda (evita criar texto novo todo frame)
        if (texto == _ultimoTextoZonas) return;
        _ultimoTextoZonas = texto;
        textoZonas.text = texto;
    }

    // ---------- Telas: sair, vitória e derrota ----------

    // Com zonas no mapa vence quem captura todas; sem zonas (tutorial) vence quem derrota todos os inimigos
    private static bool Venceu()
    {
        return ZonaCaptura.Total > 0 ? ZonaCaptura.TodasCapturadas : EnemyController.AllDefeated;
    }

    // Sair do jogo pergunta antes (Esc, depois ENTER confirma). Vitória e derrota oferecem jogar de novo ou menu.
    private void AtualizarTela()
    {
        Keyboard teclado = Keyboard.current;
        bool enter = teclado.enterKey.wasPressedThisFrame || teclado.numpadEnterKey.wasPressedThisFrame;
        bool esc = teclado.escapeKey.wasPressedThisFrame;

        switch (_tela)
        {
            case Tela.Jogando:
                if (Venceu()) MostrarTela(Tela.Venceu, "VOCE VENCEU!\n\nENTER = jogar de novo\nESC = menu");
                else if (esc) MostrarTela(Tela.Pausa, "VOLTAR AO MENU?\n\nENTER = sim\nESC = continuar");
                break;

            case Tela.Pausa:
                if (enter) Carregar(nomeCenaMenu);
                else if (esc) Continuar();
                break;

            default: // Venceu ou Perdeu
                if (enter) Carregar(SceneManager.GetActiveScene().name);
                else if (esc) Carregar(nomeCenaMenu);
                break;
        }
    }

    private void MostrarTela(Tela tela, string texto)
    {
        _tela = tela;
        Time.timeScale = 0f; // congela o jogo enquanto a tela está aberta
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        painelFim.SetActive(true);
        textoFim.text = texto;
    }

    private void Continuar()
    {
        _tela = Tela.Jogando;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        painelFim.SetActive(false);
    }

    private void Carregar(string cena)
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(cena);
    }

    // ---------- Movimento ----------

    private void LerTeclas()
    {
        Keyboard teclado = Keyboard.current;

        moveInput = new Vector2(
            (teclado.dKey.isPressed ? 1 : 0) - (teclado.aKey.isPressed ? 1 : 0),
            (teclado.wKey.isPressed ? 1 : 0) - (teclado.sKey.isPressed ? 1 : 0));
        moveInput = Vector2.ClampMagnitude(moveInput, 1f);

        jumpPressed |= teclado.spaceKey.wasPressedThisFrame;
        dashPressed |= teclado.leftShiftKey.wasPressedThisFrame;
    }

    // Direção do movimento no plano, relativa à câmera
    private Vector3 DirecaoDaCamera()
    {
        Vector3 frente = transformCamera.forward;
        Vector3 direita = transformCamera.right;
        frente.y = 0f;
        direita.y = 0f;

        return (direita.normalized * moveInput.x + frente.normalized * moveInput.y).normalized;
    }

    private void Mover()
    {
        Vector3 direcao = DirecaoDaCamera();

        Vector3 velocity = rb.linearVelocity;
        velocity.x = direcao.x * velocidade;
        velocity.z = direcao.z * velocidade;
        rb.linearVelocity = velocity;

        // corpo sempre virado pra onde a câmera olha
        if (olharParaCamera || direcao.sqrMagnitude > 0.01f)
        {
            Quaternion alvo = olharParaCamera
                ? Quaternion.Euler(0f, transformCamera.eulerAngles.y, 0f)
                : Quaternion.LookRotation(direcao);

            rb.MoveRotation(Quaternion.Slerp(rb.rotation, alvo, velocidadeRotacao * Time.fixedDeltaTime));
        }
    }

    private void Pular()
    {
        if (!Physics.Raycast(transform.position, Vector3.down, distanciaChao)) return;

        // zera a velocidade vertical antes do pulo
        Vector3 velocity = rb.linearVelocity;
        velocity.y = 0f;
        rb.linearVelocity = velocity;

        rb.AddForce(Vector3.up * forcaPulo, ForceMode.Impulse);
    }

    private void IniciarDash()
    {
        if (Time.time < lastDashTime + recargaDash) return;

        Vector3 direcao = DirecaoDaCamera();
        dashDirection = direcao.sqrMagnitude < 0.01f ? transform.forward : direcao;

        isDashing = true;
        dashTimer = duracaoDash;
        lastDashTime = Time.time;
        rb.linearVelocity = Vector3.zero;
    }

    private void Dash()
    {
        rb.linearVelocity = dashDirection * velocidadeDash;

        dashTimer -= Time.fixedDeltaTime;
        if (dashTimer > 0f) return;

        // acabou o dash: para o movimento horizontal
        isDashing = false;
        Vector3 velocity = rb.linearVelocity;
        velocity.x = 0f;
        velocity.z = 0f;
        rb.linearVelocity = velocity;
    }
}
