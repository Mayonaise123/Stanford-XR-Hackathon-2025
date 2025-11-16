using System;
using System.Collections;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using TMPro;   // add this

public class VRCameraStreamer : MonoBehaviour
{
    [Header("Server")]
    public string serverIp = "192.168.0.42"; // your PC IP here
    public int serverPort = 5000;

    [Header("Capture")]
    public Camera captureCamera;
    public RenderTexture captureRT;
    public float sendInterval = 0.15f; // seconds between frames
    [Range(1, 100)]
    public int jpgQuality = 70;

    [Header("UI")]
    [Tooltip("TextMeshPro object in the scene to show label and confidence")]
    public TextMeshPro labelText;

    [Header("Debug")]
    public bool logDebug = true;

    private TcpClient client;
    private NetworkStream stream;
    private Texture2D captureTex;
    private bool connected = false;
    private bool running = true;

    // values we get back from the server
    private Thread recvThread;
    private volatile string latestLabel = "";
    private volatile float latestConf = 0f;

    void Start()
    {
        if (captureCamera == null || captureRT == null)
        {
            Debug.LogError("VRCameraStreamer: assign captureCamera and captureRT");
            enabled = false;
            return;
        }

        // match texture size to RenderTexture
        captureTex = new Texture2D(
            captureRT.width,
            captureRT.height,
            TextureFormat.RGB24,
            false
        );

        try
        {
            client = new TcpClient();
            client.Connect(serverIp, serverPort);
            stream = client.GetStream();
            connected = true;
            if (logDebug)
                Debug.Log("Connected to YOLO server " + serverIp + ":" + serverPort);

            // start receive thread for labels
            recvThread = new Thread(ReceiveLoop);
            recvThread.IsBackground = true;
            recvThread.Start();

            StartCoroutine(CaptureLoop());
        }
        catch (Exception e)
        {
            Debug.LogError("VRCameraStreamer: could not connect to server: " + e);
        }
    }

    private IEnumerator CaptureLoop()
    {
        // small delay so XR is initialized
        yield return new WaitForSeconds(1f);

        while (running && connected)
        {
            yield return new WaitForSeconds(sendInterval);

            if (stream != null && stream.CanWrite)
            {
                SendFrame();
            }
        }
    }

    private void Update()
    {
        // show latest detection in the VR world
        if (labelText != null)
        {
            if (!string.IsNullOrEmpty(latestLabel) && latestLabel != "none")
            {
                // confidence is 0 to 1, format as percent
                float pct = latestConf * 100f;
                labelText.text = $"{latestLabel} {pct:0.0}%";
            }
            else
            {
                labelText.text = "No sign detected";
            }
        }
    }

    private void SendFrame()
    {
        try
        {
            RenderTexture currentRT = RenderTexture.active;
            RenderTexture.active = captureRT;

            captureTex.ReadPixels(new Rect(0, 0, captureRT.width, captureRT.height), 0, 0);
            captureTex.Apply();

            RenderTexture.active = currentRT;

            byte[] jpgBytes = ImageConversion.EncodeToJPG(captureTex, jpgQuality);

            // length header in network byte order, Python expects big endian via "!I"
            byte[] lengthBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(jpgBytes.Length));

            if (logDebug)
            {
                Debug.Log(
                    $"Sending frame of {jpgBytes.Length} bytes, length header: {BitConverter.ToString(lengthBytes)}"
                );
            }

            stream.Write(lengthBytes, 0, lengthBytes.Length);
            stream.Write(jpgBytes, 0, jpgBytes.Length);
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRCameraStreamer SendFrame error: " + e.Message);
            connected = false;
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
                // server ends each prediction with "\n"
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
            Debug.LogWarning("VRCameraStreamer ReceiveLoop error: " + e.Message);
        }
    }

    private void ParsePrediction(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        // expected format: label:0.945
        if (logDebug) Debug.Log("Server prediction: " + line);

        string[] parts = line.Split(':');
        if (parts.Length == 2)
        {
            latestLabel = parts[0];
            if (float.TryParse(
                    parts[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float c))
            {
                latestConf = c;
            }
            else
            {
                latestConf = 0f;
            }
        }
    }

    public string GetLatestLabel()
    {
        return latestLabel;
    }

    public float GetLatestConfidence()
    {
        return latestConf;
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
    }
}
