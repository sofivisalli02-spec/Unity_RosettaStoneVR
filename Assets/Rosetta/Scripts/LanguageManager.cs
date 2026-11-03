using UnityEngine;

// Stores the language chosen by the visitor (0 Italiano, 1 English, 2 Español, 3 Français, 4 Deutsch).
// The menu buttons call SetLanguage; the "back" button on the translation panel calls ShowMenu.
public class LanguageManager : MonoBehaviour
{
    public static int CurrentLanguage = 0;

    public GameObject languageMenu; // menu hidden after the choice

    public void SetLanguage(int index)
    {
        CurrentLanguage = index;
        if (languageMenu != null)
            languageMenu.SetActive(false);

        TranslationManager.Instance.ShowIntro();
    }

    // Go back: reopen the menu to choose another language
    public void ShowMenu()
    {
        if (languageMenu != null)
            languageMenu.SetActive(true);

        TranslationManager.Instance.ClearSelection();
    }
}
