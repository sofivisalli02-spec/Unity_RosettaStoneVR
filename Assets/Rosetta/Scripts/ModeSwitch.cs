using UnityEngine;

// Chooses how the scene is visited: desktop (mouse + WASD, for the video) or VR (headset / XR simulator).
// Tick or untick "Desktop Mode" in the Inspector: the objects are switched on/off immediately.
public class ModeSwitch : MonoBehaviour
{
    public bool desktopMode = true;
    public GameObject desktopPlayer;
    public GameObject[] vrObjects;      // XR Origin, XR Interaction Simulator
    public GameObject[] desktopOnly;    // e.g. the invisible wall around the glass case (it would block the VR ray)

    void Awake() => Apply();
    void OnValidate() => Apply();

    void Apply()
    {
        if (desktopPlayer != null) desktopPlayer.SetActive(desktopMode);
        if (vrObjects != null)
            foreach (var go in vrObjects)
                if (go != null) go.SetActive(!desktopMode);
        if (desktopOnly != null)
            foreach (var go in desktopOnly)
                if (go != null) go.SetActive(desktopMode);
    }
}
