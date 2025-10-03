using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GenericSceneManager : MonoBehaviour
{
    [Header("Configuration")]
    public SceneConfig config;
    
    [Header("UI References (Optional - will auto-find if not set)")]
    // public Button[] navigationButtons;
    // public Button backButton;
    // public Button exitButton;
    
    [Header("Audio")]
    public AudioSource audioSource;
    
    private Dictionary<string, Button> buttonMapping;

    void Start()
    {
        InitializeSceneManager();
    }

    void InitializeSceneManager()
    {
        if (config == null)
        {
            Debug.LogError("SceneConfig is not assigned!");
            return;
        }

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        buttonMapping = new Dictionary<string, Button>();

        SetupButtons();
    }

    void SetupButtons()
    {
        if (config.buttonActions == null) return;

        foreach (var actionData in config.buttonActions)
        {
            Button button = FindButtonByName(actionData.buttonName);
            if (button != null)
            {
                Text buttonText = button.GetComponentInChildren<Text>();
                if (buttonText != null && !string.IsNullOrEmpty(actionData.buttonText))
                {
                    buttonText.text = actionData.buttonText;
                }

                button.onClick.RemoveAllListeners();
                switch (actionData.buttonType)
                {
                    case ButtonType.LoadScene:
                        button.onClick.AddListener(() => LoadScene(actionData.targetValue));
                        break;
                    case ButtonType.OpenLink:
                        button.onClick.AddListener(() => OpenExternalLink(actionData.targetValue));
                        break;
                    case ButtonType.GoBack:
                        button.onClick.AddListener(GoBack);
                        break;
                    case ButtonType.Exit:
                        button.onClick.AddListener(ExitApplication);
                        break;
                    // Tambahkan case untuk tipe baru di sini
                }
                
                buttonMapping[actionData.buttonName] = button;
            }
            else
            {
                Debug.LogWarning($"Button '{actionData.buttonName}' not found in scene!");
            }
        }
    }

    public void OpenExternalLink(string url)
    {
        PlayButtonSound();
        Application.OpenURL(url);
    }

    Button FindButtonByName(string buttonName)
    {
        // Perbaikan: Cari di dictionary dulu biar efisien
        if (buttonMapping.ContainsKey(buttonName))
        {
            return buttonMapping[buttonName];
        }

        GameObject buttonObj = GameObject.Find(buttonName);
        if (buttonObj != null)
        {
            Button button = buttonObj.GetComponent<Button>();
            if (button != null) return button;
        }
        
        // FindObjectsOfType sekarang jadi fallback terakhir
        Button[] allButtons = FindObjectsOfType<Button>();
        foreach (var button in allButtons)
        {
            if (button.name.Contains(buttonName) || button.name.Equals(buttonName, System.StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }
        }

        return null;
    }

    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("Scene name is null or empty!");
            return;
        }

        PlayButtonSound();
        Debug.Log($"Loading scene '{sceneName}' via Addressables...");
        StartCoroutine(LoadSceneFromAddressables(sceneName));
    }
    
    private IEnumerator LoadSceneFromAddressables(string sceneName)
    {
        Debug.Log($"Attempting to load scene '{sceneName}' via Addressables");
        
        // Load scene via Addressables
        var handle = Addressables.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        
        // Wait sampai selesai
        yield return handle;
        
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            Debug.Log($"✅ Scene '{sceneName}' loaded successfully via Addressables");
        }
        else
        {
            Debug.LogError($"❌ Failed to load scene '{sceneName}' via Addressables: {handle.OperationException}");
            
            // Fallback - coba traditional loading
            Debug.LogWarning($"🔄 Attempting fallback traditional loading for '{sceneName}'");
            
            if (DoesSceneExist(sceneName))
            {
                Debug.Log($"Scene '{sceneName}' found in build settings, loading traditionally");
                SceneManager.LoadScene(sceneName);
            }
            else
            {
                Debug.LogError($"Scene '{sceneName}' not found anywhere!");
            }
        }
    }

    public void GoBack()
    {
        PlayButtonSound();
        if (!string.IsNullOrEmpty(config.backSceneName))
        {
            Debug.Log($"GoBack to {config.backSceneName}");
            LoadScene(config.backSceneName);
            return;
        }

        Debug.LogWarning("No back scene configured in SceneConfig!");
    }

    public void ExitApplication()
    {
        PlayButtonSound();
        Debug.Log("Exiting application...");
        
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    public void RestartCurrentScene()
    {
        PlayButtonSound();
        Scene currentScene = SceneManager.GetActiveScene();
        LoadScene(currentScene.name);
    }

    bool DoesSceneExist(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (name == sceneName)
                return true;
        }
        return false;
    }

    void PlayButtonSound()
    {
        if (config.buttonClickSound && audioSource)
        {
            audioSource.PlayOneShot(config.buttonClickSound);
        }
    }

    IEnumerator LoadSceneWithDelay(string sceneName, float delay = 0.1f)
    {
        yield return new WaitForSeconds(delay);
        
        if (DoesSceneExist(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError($"Scene '{sceneName}' not found in build settings!");
        }
    }

    public void LoadConfigFromAssetBundle(SceneConfig newConfig)
    {
        config = newConfig;
        InitializeSceneManager();
    }

    public void EnableButton(string buttonName, bool enable)
    {
        if (buttonMapping.ContainsKey(buttonName))
        {
            buttonMapping[buttonName].interactable = enable;
        }
    }
}
