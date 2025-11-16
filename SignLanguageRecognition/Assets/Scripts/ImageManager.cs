using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement; // add this

public class SpriteSlideshowVR : MonoBehaviour
{
    [Header("UI Target")]
    public Image targetImage;

    [Header("Sprites To Show")]
    public List<Sprite> sprites = new List<Sprite>();

    [Header("Timing")]
    public float intervalSeconds = 3f;

    [Header("Next Scene")]
    public string nextSceneName = "LearningSceneFinal";

    private int currentIndex = 0;
    private float timer = 0f;

    private void Start()
    {
        if (sprites.Count > 0 && targetImage != null)
        {
            // show first image
            currentIndex = 0;
            targetImage.sprite = sprites[currentIndex];
        }
    }

    private void Update()
    {
        if (sprites.Count == 0 || targetImage == null)
        {
            return;
        }

        timer += Time.deltaTime;

        if (timer >= intervalSeconds)
        {
            timer = 0f;
            ShowNextSpriteOrLoadScene();
        }
    }

    private void ShowNextSpriteOrLoadScene()
    {
        // move to next index
        currentIndex++;

        // if we have shown all sprites, load the next scene
        if (currentIndex >= sprites.Count)
        {
            LoadNextScene();
            return;
        }

        // otherwise show the next sprite
        targetImage.sprite = sprites[currentIndex];
    }

    private void LoadNextScene()
    {
        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogWarning("SpriteSlideshowVR: nextSceneName is empty, cannot load scene.");
            return;
        }

        // make sure LearningSceneFinal is added to Build Settings > Scenes In Build
        SceneManager.LoadScene(1);
    }
}
