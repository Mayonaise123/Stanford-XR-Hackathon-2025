using UnityEngine;
using System.IO;

public class DebugSaveCapture : MonoBehaviour
{
    public Camera captureCamera;
    public RenderTexture captureRT;

    Texture2D tex;

    void Start()
    {
        if (captureRT == null || captureCamera == null)
        {
            Debug.LogError("Assign captureCamera and captureRT on DebugSaveCapture");
            enabled = false;
            return;
        }

        tex = new Texture2D(captureRT.width, captureRT.height, TextureFormat.RGB24, false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            SaveOne();
        }
    }

    void SaveOne()
    {
        RenderTexture currentRT = RenderTexture.active;
        RenderTexture.active = captureRT;

        tex.ReadPixels(new Rect(0, 0, captureRT.width, captureRT.height), 0, 0);
        tex.Apply();

        RenderTexture.active = currentRT;

        byte[] pngBytes = tex.EncodeToPNG();
        string path = Path.Combine(Application.dataPath, "debug_capture.png");
        File.WriteAllBytes(path, pngBytes);

        Debug.Log("Saved debug capture to " + path);
    }
}
