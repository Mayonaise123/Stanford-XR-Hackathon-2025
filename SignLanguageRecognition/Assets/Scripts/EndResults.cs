using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class EndResults : MonoBehaviour
{
    [Header("UI")]
    public GameObject endResultsPanel;
    public Image displayImage; // The single big image shown on screen

    [System.Serializable]
    public class WordSprites
    {
        public string word;
        public Sprite improvedSprite;
        public Sprite notImprovedSprite;
    }

    [Header("Word Sprites")]
    public List<WordSprites> wordSprites;

    void Start()
    {
        endResultsPanel.SetActive(false);
        displayImage.gameObject.SetActive(false);
    }

    // Called with dictionary from Evaluation.Instance
    public void ShowAllResults(Dictionary<string, bool> results)
    {
        StartCoroutine(ShowImagesSequentially(results));
        endResultsPanel.SetActive(true);
    }

    private IEnumerator<WaitForSeconds> ShowImagesSequentially(Dictionary<string, bool> results)
    {
        displayImage.gameObject.SetActive(true);

        foreach (var pair in results)
        {
            string word = pair.Key;
            bool improved = pair.Value;

            // Find the sprites for this word
            var data = wordSprites.Find(w => w.word == word);

            if (data != null)
            {
                displayImage.sprite = improved ? data.improvedSprite : data.notImprovedSprite;
                yield return new WaitForSeconds(1.5f); // duration each is shown
            }
        }
    }
}