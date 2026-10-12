using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class PlayerMove : MonoBehaviour
{
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
    }
 
    private void Update()
    {
        ReadInput();
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