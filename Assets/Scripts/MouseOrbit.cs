using UnityEngine;
using UnityEngine.InputSystem;

public class MouseOrbit : MonoBehaviour
{
    public Transform player;
    public float distance = 5f;
    public float MouseSensitivity = 0.2f;
    private float rotX;
    private float rotY;

    void Start() {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate() {
        if (player != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue() * MouseSensitivity;
            rotX += delta.x;
            rotY -= delta.y;
            rotY = Mathf.Clamp(rotY, -9, 60);

            Quaternion rotation = Quaternion.Euler(rotY, rotX, 0);
            transform.position = player.position - (rotation * Vector3.forward * distance);
            transform.rotation = rotation;
        }
    }
}
