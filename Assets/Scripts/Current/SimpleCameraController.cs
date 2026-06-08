using UnityEngine;

public class SimpleCameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float fastMoveMultiplier = 3f; // Hold Shift to go faster

    [Header("Look Settings")]
    public float lookSensitivity = 2f;
    
    private float pitch = 0f; // Up/Down rotation
    private float yaw = 0f;   // Left/Right rotation

    void Start()
    {
        // Lock the mouse cursor to the center of the screen and hide it
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Grab the camera's initial rotation so it doesn't snap weirdly on start
        Vector3 angles = transform.eulerAngles;
        pitch = angles.x;
        yaw = angles.y;
    }

    void Update()
    {
        // --- 1. LOOK AROUND (Mouse) ---
        yaw += Input.GetAxis("Mouse X") * lookSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * lookSensitivity;
        
        // Clamp pitch so you can't flip upside down
        pitch = Mathf.Clamp(pitch, -90f, 90f); 

        transform.eulerAngles = new Vector3(pitch, yaw, 0f);

        // --- 2. MOVEMENT (WASD) ---
        float currentSpeed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift))
        {
            currentSpeed *= fastMoveMultiplier;
        }

        Vector3 moveDirection = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        
        // Add vertical movement (E to go up, Q to go down)
        if (Input.GetKey(KeyCode.E)) moveDirection.y += 1f;
        if (Input.GetKey(KeyCode.Q)) moveDirection.y -= 1f;

        // Apply movement relative to where the camera is looking
        transform.Translate(moveDirection * currentSpeed * Time.deltaTime, Space.Self);

        // --- 3. UNLOCK CURSOR (Escape) ---
        // Press Escape to get your mouse back if you need to click the Unity Editor
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        
        // Click back in the game view to lock the mouse again
        if (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
