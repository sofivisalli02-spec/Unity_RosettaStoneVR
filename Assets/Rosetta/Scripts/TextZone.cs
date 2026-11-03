using UnityEngine;

// One interactive area of the inscription (one passage in one script).
// Needs a Collider and an XR Simple Interactable on the same object.
// XR Simple Interactable events: Select Entered -> OnSelected, Hover Entered/Exited -> OnHoverEnter/OnHoverExit
public class TextZone : MonoBehaviour
{
    public enum Script { Hieroglyphic, Demotic, Greek }

    public int passageID;              // same number = same passage in the three scripts
    public Script script;
    public Material normalMaterial;    // almost invisible
    public Material hoverMaterial;     // light veil while pointing at it
    public Material highlightMaterial; // golden veil when its passage is selected
    public GameObject outline;         // thin golden frame, shown on hover and when selected

    Renderer rend;
    bool highlighted;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        Highlight(false);
    }

    public void OnSelected()
    {
        TranslationManager.Instance.ShowPassage(passageID);
    }

    public void OnHoverEnter()
    {
        if (highlighted) return;
        rend.sharedMaterial = hoverMaterial;
        if (outline != null) outline.SetActive(true);
    }

    public void OnHoverExit()
    {
        if (highlighted) return;
        rend.sharedMaterial = normalMaterial;
        if (outline != null) outline.SetActive(false);
    }

    public void Highlight(bool on)
    {
        highlighted = on;
        rend.sharedMaterial = on ? highlightMaterial : normalMaterial;
        if (outline != null) outline.SetActive(on);
    }
}
