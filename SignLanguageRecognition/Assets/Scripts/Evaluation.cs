//call this function at the last scene/test to show the final results
//Evaluation.Instance.EvaluateAll();
using System.Collections.Generic;
using UnityEngine;

public class Evaluation : MonoBehaviour
{
    public static Evaluation Instance;

    //to update:
    // Evaluation.Instance.hasImproved["Egg"] = true;

    public Dictionary<string, bool> hasImproved = new Dictionary<string, bool>();

    private EndResults resultsUI;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Initialize dictionary (these values will get updated by your gameplay)
        hasImproved["Egg"] = true;
        hasImproved["Mushroom"] = true;
        hasImproved["Bread"] = true;
        hasImproved["Fish"] = true;
        hasImproved["Hamburger"] = true;
    }

    // Call this once when session ends
    public void EvaluateAll()
    {
        resultsUI.ShowAllResults(hasImproved);
    }
}
