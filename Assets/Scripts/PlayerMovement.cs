using Game.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class PlayerMove : MonoBehaviour, IDamageable
{
    [Header("Vida")]
    [SerializeField] private float vidaMaxima = 100f;
    [Tooltip("Image do coração no Canvas, com Image Type = Filled (Vertical).")]
    [SerializeField] private Image coracao;
    [SerializeField] private Text textoVida;

    private float _vida;

    [Header("Tela de sair / vitória / derrota (Canvas)")]
    [SerializeField] private GameObject painelFim;
    [SerializeField] private Text textoFim;
    [SerializeField] private string nomeCenaMenu = "Menu";

    private enum Tela { Jogando, Pausa, Venceu, Perdeu }
    private Tela _tela = Tela.Jogando;

    [Header("Movimento")]
    [FormerlySerializedAs("speed")] [SerializeField] private float velocidade = 5f;
    [FormerlySerializedAs("rotationSpeed")] [SerializeField] private float velocidadeRotacao = 10f;
    [FormerlySerializedAs("cameraTransform")] [SerializeField] private Transform transformCamera;
    [Tooltip("Corpo sempre virado pra onde a câmera olha (hero shooter).")]
    [SerializeField] private bool olharParaCamera = true;

 
    [Header("Pulo")]
    [FormerlySerializedAs("jumpForce")] [SerializeField] private float forcaPulo = 7f;
    [FormerlySerializedAs("groundCheckDistance")] [SerializeField] private float distanciaChao = 1.1f;
 
    [Header("Dash")]
    [FormerlySerializedAs("dashSpeed")] [SerializeField] private float velocidadeDash = 20f;
    [FormerlySerializedAs("dashDuration")] [SerializeField] private float duracaoDash = 0.15f;
    [FormerlySerializedAs("dashCooldown")] [SerializeField] private float recargaDash = 0.8f;

    
 
    private Rigidbody rb;
 
    private Vector2 moveInput;
 
    private bool jumpPressed;
    private bool dashPressed;
 
    private bool isDashing;
    private float dashTimer;
    private float lastDashTime = -999f;
 
    private Vector3 dashDirection;
 
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // sem câmera atribuída (player gerado), usa a principal
        if (transformCamera == null && Camera.main != null)
            transformCamera = Camera.main.transform;

        _vida = vidaMaxima;
        AtualizarHudVida();
    }

    public void TakeDamage(float amount)
    {
        if (_vida <= 0f) return;

        _vida = Mathf.Max(0f, _vida - amount);
        AtualizarHudVida();

        // morreu: mostra a tela de derrota
        if (_vida <= 0f)
            MostrarTela(Tela.Perdeu, "VOCE PERDEU!\n\nENTER = jogar de novo\nESC = menu");
    }

    private void AtualizarHudVida()
    {
        if (coracao != null) coracao.fillAmount = _vida / vidaMaxima;
        if (textoVida != null) textoVida.text = Mathf.CeilToInt(_vida).ToString();
    }
 
    private void Update()
    {
        AtualizarTela();

        // pausado (sair, vitória ou derrota): não lê movimento
        if (_tela != Tela.Jogando) return;

        ReadInput();
    }

    // ---------- Telas: sair, vitória e derrota ----------

    // Sair do jogo pergunta antes (Esc, depois ENTER confirma). Vitória e derrota oferecem jogar de novo ou menu.
    private void AtualizarTela()
    {
        Keyboard teclado = Keyboard.current;
        if (teclado == null) return;

        bool enter = teclado.enterKey.wasPressedThisFrame || teclado.numpadEnterKey.wasPressedThisFrame;
        bool esc = teclado.escapeKey.wasPressedThisFrame;

        switch (_tela)
        {
            case Tela.Jogando:
                if (EnemyController.AllDefeated) MostrarTela(Tela.Venceu, "VOCE VENCEU!\n\nENTER = jogar de novo\nESC = menu");
                else if (esc) MostrarTela(Tela.Pausa, "VOLTAR AO MENU?\n\nENTER = sim\nESC = continuar");
                break;

            case Tela.Pausa:
                if (enter) VoltarAoMenu();
                else if (esc) Continuar();
                break;

            case Tela.Venceu:
            case Tela.Perdeu:
                if (enter) JogarDeNovo();
                else if (esc) VoltarAoMenu();
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

    private void VoltarAoMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(nomeCenaMenu);
    }

    private void JogarDeNovo()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
 
    private void FixedUpdate()
    {
        if (isDashing)
        {
            Dash();
            return;
        }
 
        Move();
 
        if (jumpPressed)
        {
            Jump();
            jumpPressed = false;
        }
 
        if (dashPressed)
        {
            StartDash();
            dashPressed = false;
        }
    }
    private void ReadInput()
    {
        moveInput = Vector2.zero;
 
        // Movimento
        if (Keyboard.current.wKey.isPressed)
            moveInput.y += 1;
 
        if (Keyboard.current.sKey.isPressed)
            moveInput.y -= 1;
 
        if (Keyboard.current.dKey.isPressed)
            moveInput.x += 1;
 
        if (Keyboard.current.aKey.isPressed)
            moveInput.x -= 1;
 
        moveInput = Vector2.ClampMagnitude(moveInput, 1f);
 
        // Pulo
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            jumpPressed = true;
        }
 
        // Dash
        if (Keyboard.current.leftShiftKey.wasPressedThisFrame)
        {
            dashPressed = true;
        }
    }
 

 private Vector3 GetCameraDirection()
{
    Vector3 cameraForward = transformCamera.forward;
    Vector3 cameraRight = transformCamera.right;

    cameraForward.y = 0f;
    cameraRight.y = 0f;

    cameraForward.Normalize();
    cameraRight.Normalize();

    Vector3 direction =
        cameraRight * moveInput.x +
        cameraForward * moveInput.y;

    return direction.normalized;
}

    private void Move()
{
    Vector3 direction = GetCameraDirection();

    Vector3 velocity = rb.linearVelocity;

    velocity.x = direction.x * velocidade;
    velocity.z = direction.z * velocidade;

    rb.linearVelocity = velocity;

    // corpo sempre virado pra onde a câmera olha
    bool seguirCamera = olharParaCamera && transformCamera != null;

    if (seguirCamera || direction.sqrMagnitude > 0.01f)
    {
        Quaternion targetRotation = seguirCamera
            ? Quaternion.Euler(0f, transformCamera.eulerAngles.y, 0f)
            : Quaternion.LookRotation(direction);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                targetRotation,
                velocidadeRotacao * Time.fixedDeltaTime
            )
        );
    }
}


 
 
    private void Jump()
    {
        if (!IsGrounded())
            return;
 
        // Zera velocidade vertical antes do pulo :D
        Vector3 velocity = rb.linearVelocity;
        velocity.y = 0f;
        rb.linearVelocity = velocity;
 
        rb.AddForce(
            Vector3.up * forcaPulo,
            ForceMode.Impulse
        );
    }
 
 
    private bool IsGrounded()
    {
        return Physics.Raycast(
            transform.position,
            Vector3.down,
            distanciaChao
        );
    }
 
  
 
    private void StartDash()
{
    if (Time.time < lastDashTime + recargaDash)
        return;

    Vector3 direction = GetCameraDirection();

    if (direction.sqrMagnitude < 0.01f)
    {
        direction = transform.forward;
    }

    dashDirection = direction;

    isDashing = true;
    dashTimer = duracaoDash;
    lastDashTime = Time.time;

    rb.linearVelocity = Vector3.zero;
}

 
    private void Dash()
    {
        rb.linearVelocity =
            dashDirection * velocidadeDash;
 
        dashTimer -= Time.fixedDeltaTime;
 
        if (dashTimer <= 0f)
        {
            isDashing = false;
 
            // Para o movimento horizontal
            Vector3 velocity = rb.linearVelocity;
 
            velocity.x = 0f;
            velocity.z = 0f;
 
            rb.linearVelocity = velocity;
        }
    }
}