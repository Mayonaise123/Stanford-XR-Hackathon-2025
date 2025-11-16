using System;
using System.Collections;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;
using UnityEngine;
using TMPro;

public class YoloSignClientPC : MonoBehaviour
{
    [Header("Server")]
    public string serverIp = "127.0.0.1";
    public int serverPort = 5000;

    [Header("Capture")]
    public WebcamView webcamView;
    public float captureInterval = 0.2f;
    public int captureWidth = 320;
    public int captureHeight = 240;
    public int jpgQuality = 70;

    [Header("UI")]
    public TMP_Text labelText;

    private TcpClient client;
    private NetworkStream stream;
    private Texture2D captureTex;
    private RenderTexture captureRT;
    private volatile string latestLabel = "";
    private volatile float latestConfidence = 0f;
    private bool running = true;

    void Start()
    {
        if (webcamView == null)
        {
            Debug.LogError("YoloSignClientPC: webcamView not assigned");
            enabled = false;
            return;
        }

        captureTex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        captureRT = new RenderTexture(captureWidth, captureHeight, 0, RenderTextureFormat.ARGB32);

        try
        {
            client = new TcpClient();
            client.Connect(serverIp, serverPort);
            stream = client.GetStream();
            Debug.Log("Connected to YOLO server at " + serverIp + ":" + serverPort);

            Thread recvThread = new Thread(ReceiveLoop);
            recvThread.IsBackground = true;
            recvThread.Start();

            StartCoroutine(CaptureLoop());
        }
        catch (Exception e)
        {
            Debug.LogError("Could not connect to YOLO server: " + e);
        }
    }

    private IEnumerator CaptureLoop()
    {
        yield return new WaitForSeconds(1f);

        WebCamTexture camTex = webcamView.GetWebCamTexture();
        if (camTex == null)
        {
            Debug.LogError("WebCamTexture is null");
            yield break;
        }

        while (running)
        {
            yield return new WaitForSeconds(captureInterval);

            if (!camTex.isPlaying || !camTex.didUpdateThisFrame)
                continue;

            if (stream != null && stream.CanWrite)
            {
                SendFrame(camTex);
            }
        }
    }

    private void SendFrame(WebCamTexture camTex)
    {
        try
        {
            // Blit webcam texture directly to a smaller RenderTexture
            Graphics.Blit(camTex, captureRT);

            // Read from RenderTexture into Texture2D
            RenderTexture currentRT = RenderTexture.active;
            RenderTexture.active = captureRT;

            captureTex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
            captureTex.Apply();

            RenderTexture.active = currentRT;

            byte[] jpgBytes = ImageConversion.EncodeToJPG(captureTex, jpgQuality);

            byte[] lengthBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(jpgBytes.Length));
            stream.Write(lengthBytes, 0, lengthBytes.Length);
            stream.Write(jpgBytes, 0, jpgBytes.Length);

            // Debug.Log("Frame sent, size " + jpgBytes.Length);
        }
        catch (Exception e)
        {
            Debug.LogWarning("SendFrame error: " + e.Message);
        }
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[1024];
        StringBuilder sb = new StringBuilder();

        try
        {
            while (running && client != null && client.Connected)
            {
                int bytes = stream.Read(buffer, 0, buffer.Length);
                if (bytes <= 0) break;

                string chunk = Encoding.UTF8.GetString(buffer, 0, bytes);
                sb.Append(chunk);

                int newlineIndex;
                while ((newlineIndex = sb.ToString().IndexOf('\n')) >= 0)
                {
                    string line = sb.ToString().Substring(0, newlineIndex).Trim();
                    sb.Remove(0, newlineIndex + 1);
                    ParsePrediction(line);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("ReceiveLoop error: " + e.Message);
        }
    }

    private void ParsePrediction(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        Debug.Log("Server prediction: " + line);

        string[] parts = line.Split(':');
        if (parts.Length == 2)
        {
            latestLabel = parts[0];
            if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float conf))
                latestConfidence = conf;
        }
    }

    void Update()
    {
        if (labelText == null)
            return;

        if (!string.IsNullOrEmpty(latestLabel) && latestLabel != "none")
        {
            labelText.text = $"{latestLabel} {latestConfidence * 100f:0.0}%";
        }
        else
        {
            labelText.text = "";
        }
    }

    void OnDestroy()
    {
        running = false;
        try
        {
            stream?.Close();
            client?.Close();
        }
        catch { }

        if (captureRT != null)
            captureRT.Release();
    }
}
