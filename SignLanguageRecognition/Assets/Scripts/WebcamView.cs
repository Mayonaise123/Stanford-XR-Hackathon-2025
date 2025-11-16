using UnityEngine;
using UnityEngine.UI;

public class WebcamView : MonoBehaviour
{
    public RawImage rawImage;
    public AspectRatioFitter fitter;

    private WebCamTexture webCamTexture;

    void Start()
    {
        if (rawImage == null)
            rawImage = GetComponent<RawImage>();

        // pick the first camera
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("No webcam found");
            return;
        }

        string camName = devices[0].name;
        Debug.Log("Using camera: " + camName);

        webCamTexture = new WebCamTexture(camName, 640, 480, 30);
        rawImage.texture = webCamTexture;
        rawImage.material.mainTexture = webCamTexture;

        webCamTexture.Play();
    }

    void Update()
    {
        if (webCamTexture == null || !webCamTexture.isPlaying)
            return;

        if (fitter != null && webCamTexture.width > 16)
        {
            float ratio = (float)webCamTexture.width / webCamTexture.height;
            fitter.aspectRatio = ratio;
        }
    }

    public WebCamTexture GetWebCamTexture()
    {
        return webCamTexture;
    }
}
