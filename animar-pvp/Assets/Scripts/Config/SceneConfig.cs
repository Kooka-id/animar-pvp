using UnityEngine;

[CreateAssetMenu(fileName = "SceneConfig", menuName = "Portal AR/Scene Config")]
public class SceneConfig : ScriptableObject
{
    [Header("Scene Information")]
    public string sceneName;
    public string sceneDisplayName;
    public string backSceneName;

    [Header("Button Actions")]
    public ButtonActionData[] buttonActions;

    [Header("Audio (Optional)")]
    public AudioClip buttonClickSound;
}

[System.Serializable]
public class ButtonActionData
{
    public string buttonName;
    public string buttonText;
    public ButtonType buttonType;
    public string targetValue;
}

public enum ButtonType
{
    LoadScene,
    OpenLink,
    StartARSession,
    GoBack,
    Exit,
}

public enum LinkType
{
    WebURL,
    Instagram,
    Email,
    WhatsApp
}