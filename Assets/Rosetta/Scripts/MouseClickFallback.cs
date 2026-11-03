using UnityEngine;
using UnityEngine.InputSystem;

// Testing aid for the editor without a headset: a left mouse click on a language button
// or on a zone of the inscription does the same as selecting it with the VR controller.
public class MouseClickFallback : MonoBehaviour
{
    public LanguageManager languageManager;

    void Update()
    {
        var mouse = Mouse.current;
        var cam = Camera.main;
        if (mouse == null || cam == null || !mouse.leftButton.wasPressedThisFrame) return;

        bool menuOpen = languageManager.languageMenu != null && languageManager.languageMenu.activeSelf;
        var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        var hits = Physics.RaycastAll(ray, 20f, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            var button = hit.collider.GetComponent<LanguageButton>();
            if (button != null)
            {
                languageManager.SetLanguage(button.languageIndex);
                return;
            }
            var zone = hit.collider.GetComponent<TextZone>();
            if (zone != null && !menuOpen)
            {
                zone.OnSelected();
                return;
            }
        }
    }
}
