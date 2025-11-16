using UnityEngine;

public class ConfusionDebugger : MonoBehaviour
{
    public EEGReceiver receiver;

    void Update()
    {
        if (receiver == null)
        {
            Debug.LogWarning("ConfusionDebugger has no EEGReceiver assigned.");
            return;
        }

        if (receiver.latestPrediction == 1)
            Debug.Log("CONFUSION detected");
        else
            Debug.Log("Not confused");
    }
}
