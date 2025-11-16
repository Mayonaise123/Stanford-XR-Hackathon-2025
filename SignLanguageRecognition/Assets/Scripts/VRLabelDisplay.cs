using UnityEngine;
using TMPro;

public class VRLabelDisplay : MonoBehaviour
{
    public VRCameraStreamer streamer;
    public TMP_Text labelText;

    void Update()
    {
        if (streamer == null || labelText == null) return;

        string label = streamer.GetLatestLabel();
        float conf = streamer.GetLatestConfidence();

        if (!string.IsNullOrEmpty(label) && label != "none")
        {
            labelText.text = $"{label} {conf * 100f:0.0}%";
        }
        else
        {
            labelText.text = "";
        }
    }
}
