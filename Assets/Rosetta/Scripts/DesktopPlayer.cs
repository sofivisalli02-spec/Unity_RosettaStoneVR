using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Desktop visitor for testing and recording the video without a headset:
// WASD to walk (Shift to walk faster), mouse or arrow keys to look around,
// left click / Enter / Space on what is under the crosshair, Backspace to go back to the language menu.
// Esc frees the mouse cursor, a click in the Game view captures it again.
[RequireComponent(typeof(CharacterController))]
public class DesktopPlayer : MonoBehaviour
{
    public Camera playerCamera;
    public LanguageManager languageManager;
    public float walkSpeed = 2.2f;
    public float runMultiplier = 2f;
    public float mouseSensitivity = 0.12f;
    public float keyLookSpeed = 60f;   // degrees per second with the arrow keys
    public float interactDistance = 6f;

    CharacterController controller;
    float pitch;
    float verticalSpeed;
    TextZone hoveredZone;
    Button hoveredButton;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        LockCursor(true);
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null || mouse == null) return;

        if (keyboard.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (keyboard.backspaceKey.wasPressedThisFrame) languageManager.ShowMenu();
        bool locked = Cursor.lockState == CursorLockMode.Locked;

        // Look around: mouse (when captured) or arrow keys (always, handy when recording the screen)
        Vector2 look = locked ? mouse.delta.ReadValue() * mouseSensitivity : Vector2.zero;
        float keyStep = keyLookSpeed * Time.deltaTime;
        if (keyboard.leftArrowKey.isPressed) look.x -= keyStep;
        if (keyboard.rightArrowKey.isPressed) look.x += keyStep;
        if (keyboard.upArrowKey.isPressed) look.y += keyStep;
        if (keyboard.downArrowKey.isPressed) look.y -= keyStep;
        transform.Rotate(0f, look.x, 0f);
        pitch = Mathf.Clamp(pitch - look.y, -70f, 70f);
        playerCamera.transform.localEulerAngles = new Vector3(pitch, 0f, 0f);

        // Walk, with gravity so the visitor follows the steps of the museum
        Vector2 input = Vector2.zero;
        if (keyboard.wKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;
        float speed = walkSpeed * (keyboard.leftShiftKey.isPressed ? runMultiplier : 1f);
        Vector3 move = (transform.forward * input.y + transform.right * input.x).normalized * speed;
        verticalSpeed = controller.isGrounded ? -1f : verticalSpeed - 9.81f * Time.deltaTime;
        move.y = verticalSpeed;
        controller.Move(move * Time.deltaTime);

        // What is under the crosshair: a UI button (language, back) or a zone of the inscription
        TextZone zone = null;
        Button button = null;
        var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        var hits = Physics.RaycastAll(ray, interactDistance, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        bool menuOpen = languageManager.languageMenu != null && languageManager.languageMenu.activeSelf;
        foreach (var hit in hits)
        {
            button = hit.collider.GetComponent<Button>();
            if (button != null) break;
            zone = hit.collider.GetComponent<TextZone>();
            if (zone != null) { if (menuOpen) zone = null; break; }
        }
        hoveredButton = button;

        // Hover feedback on the zones, as with the VR controller
        if (zone != hoveredZone)
        {
            if (hoveredZone != null) hoveredZone.OnHoverExit();
            if (zone != null) zone.OnHoverEnter();
            hoveredZone = zone;
        }

        // Select: left click, or Enter / Space without touching the mouse
        bool keySelect = keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame;
        if (mouse.leftButton.wasPressedThisFrame && !locked) { LockCursor(true); return; }
        if (mouse.leftButton.wasPressedThisFrame || keySelect)
        {
            if (button != null) button.onClick.Invoke();
            else if (zone != null) zone.OnSelected();
        }
    }

    void LockCursor(bool on)
    {
        Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !on;
    }

    // Small crosshair in the centre of the screen, golden when something can be clicked
    void OnGUI()
    {
        bool active = hoveredZone != null || hoveredButton != null;
        float s = active ? 10f : 6f;
        var r = new Rect(Screen.width / 2f - s / 2f, Screen.height / 2f - s / 2f, s, s);
        GUI.color = active ? new Color(0.95f, 0.76f, 0.3f) : Color.white;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
    }
}
