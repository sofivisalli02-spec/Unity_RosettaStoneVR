using UnityEngine;
using TMPro;

// Shows the translation of the touched passage in the chosen language
// and highlights the same passage in all the scripts where it survives.
public class TranslationManager : MonoBehaviour
{
    public static TranslationManager Instance;

    [System.Serializable]
    public class Passage
    {
        public string[] titles;        // one per language
        [TextArea(3, 10)]
        public string[] translations;  // one per language
        [TextArea(1, 3)]
        public string[] scriptNotes;   // which scripts preserve it, one per language
    }

    [TextArea(2, 4)]
    public string[] introTexts;        // shown after choosing the language, one per language
    [TextArea(1, 3)]
    public string chooseText = "Scegli una lingua\nChoose a language";
    public Passage[] passages;         // index = passageID
    public TMP_Text translationText;   // text on the Translation Panel
    public AudioSource selectSound;    // optional

    const string Gold = "#F2C14E";
    TextZone[] zones;

    void Awake()
    {
        Instance = this;
        zones = FindObjectsByType<TextZone>(FindObjectsSortMode.None);
    }

    public void ShowIntro()
    {
        translationText.text = introTexts[LanguageManager.CurrentLanguage];
    }

    public void ShowPassage(int passageID)
    {
        Passage p = passages[passageID];
        int lang = LanguageManager.CurrentLanguage;

        translationText.text =
            "<color=" + Gold + "><b>" + p.titles[lang] + "</b></color>\n\n" +
            p.translations[lang] + "\n\n" +
            "<i><size=85%>" + p.scriptNotes[lang] + "</size></i>";

        foreach (TextZone zone in zones)
            zone.Highlight(zone.passageID == passageID);

        if (selectSound != null && selectSound.clip != null)
            selectSound.Play();
    }

    public void ClearSelection()
    {
        foreach (TextZone zone in zones)
            zone.Highlight(false);
        translationText.text = chooseText;
    }
}
