using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMove : MonoBehaviour
{
    [Header("Movimento")]
    [SerializeField] private float speed = 5f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private Transform cameraTransform;

 
    [Header("Pulo")]
    [SerializeField] private float jumpForce = 7f;
    [SerializeField] private float groundCheckDistance = 1.1f;
 
    [Header("Dash")]
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashDuration = 0.15f;
    [SerializeField] private float dashCooldown = 0.8f;

    
 
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
    Vector3 cameraForward = cameraTransform.forward;
    Vector3 cameraRight = cameraTransform.right;

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

    velocity.x = direction.x * speed;
    velocity.z = direction.z * speed;

    rb.linearVelocity = velocity;

    if (direction.sqrMagnitude > 0.01f)
    {
        Quaternion targetRotation =
            Quaternion.LookRotation(direction);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                targetRotation,
                rotationSpeed * Time.fixedDeltaTime
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
            Vector3.up * jumpForce,
            ForceMode.Impulse
        );
    }
 
 
    private bool IsGrounded()
    {
        return Physics.Raycast(
            transform.position,
            Vector3.down,
            groundCheckDistance
        );
    }
 
  
 
    private void StartDash()
{
    if (Time.time < lastDashTime + dashCooldown)
        return;

    Vector3 direction = GetCameraDirection();

    if (direction.sqrMagnitude < 0.01f)
    {
        direction = transform.forward;
    }

    dashDirection = direction;

    isDashing = true;
    dashTimer = dashDuration;
    lastDashTime = Time.time;

    rb.linearVelocity = Vector3.zero;
}

 
    private void Dash()
    {
        rb.linearVelocity =
            dashDirection * dashSpeed;
 
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