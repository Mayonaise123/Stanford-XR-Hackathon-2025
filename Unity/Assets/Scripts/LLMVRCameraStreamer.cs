using System;
using System.Collections;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using TMPro;   // TextMeshPro

[Serializable]
public class ServerResponse
{
    public string mode;   // "yolo" or "gemini" or "error"
    public string label;
    public float conf;
    public string gemini;
    public string error;

    // new: EEG confusion flag from server (0 or 1)
    public int eeg_confused;
}

public class LLMVRCameraStreamer : MonoBehaviour
{
    [Header("Server Settings")]
    [Tooltip("IP address of the PC running the Python YOLO + Gemini + EEG server")]
    public string serverIp = "192.168.0.42";

    [Tooltip("Port of the Python server")]
    public int serverPort = 5000;


    [Header("Capture Settings")]
    [Tooltip("Camera whose view will be captured and sent to the server")]
    public Camera captureCamera;

    [Tooltip("RenderTexture that the captureCamera renders into")]
    public RenderTexture captureRT;

    [Tooltip("Seconds between normal YOLO frames")]
    public float sendInterval = 0.15f;

    [Range(1, 100)]
    [Tooltip("JPEG quality for captured frames")]
    public int jpgQuality = 70;


    [Header("Gemini Settings")]
    [Tooltip("Minimum seconds between Gemini calls")]
    public float geminiInterval = 15f;

    [Tooltip("TextMeshPro used to display Gemini responses in VR")]
    public TextMeshPro geminiTextUI;


    [Header("Debug Settings")]
    [Tooltip("Enable extra debug logs in the console")]
    public bool logDebug = true;


    // internal state

    private TcpClient client;
    private NetworkStream stream;
    private Texture2D captureTex;
    private bool connected = false;
    private bool running = true;

    // latest YOLO prediction
    private volatile string latestLabel = "";
    private volatile float latestConf = 0f;

    // latest Gemini text
    private volatile string latestGeminiText = "";
    private volatile bool geminiDirty = false;

    // EEG confusion flag from server (0 or 1)
    private volatile int latestEegConfused = 0;

    // mark that a Gemini response just arrived so we can reset cooldown
    private volatile bool geminiResponseJustArrived = false;

    // if true, do not send any more Gemini frames until reply
    private volatile bool waitingForGemini = false;

    // pending help request from SignLessonManager
    private volatile bool helpRequestPending = false;
    private string pendingSignId = "";

    // networking thread
    private Thread recvThread;
    private readonly object sendLock = new object();

    // timers
    private float nextSendTime;
    private float nextGeminiTime;


    void Start()
    {
        if (captureCamera == null)
        {
            Debug.LogError("LLMVRCameraStreamer: captureCamera is not assigned.");
            enabled = false;
            return;
        }

        if (captureRT == null)
        {
            Debug.LogError("LLMVRCameraStreamer: captureRT is not assigned.");
            enabled = false;
            return;
        }

        captureTex = new Texture2D(captureRT.width, captureRT.height, TextureFormat.RGB24, false);

        try
        {
            client = new TcpClient();
            client.Connect(serverIp, serverPort);
            stream = client.GetStream();
            connected = true;
            if (logDebug) Debug.Log($"LLMVRCameraStreamer: Connected to server {serverIp}:{serverPort}");

            recvThread = new Thread(ReceiveLoop);
            recvThread.IsBackground = true;
            recvThread.Start();

            nextSendTime = Time.time + sendInterval;
            nextGeminiTime = Time.time;

            StartCoroutine(StartDelayedCapture());
        }
        catch (Exception e)
        {
            Debug.LogError("LLMVRCameraStreamer: could not connect to server: " + e);
        }
    }

    private IEnumerator StartDelayedCapture()
    {
        yield return new WaitForSeconds(1f);
        // nothing else, Update will handle sending
    }

    void Update()
    {
        if (!running || !connected || stream == null || !stream.CanWrite) return;

        float t = Time.time;

        // apply latest Gemini text on main thread
        if (geminiDirty)
        {
            geminiDirty = false;
            if (geminiTextUI != null)
            {
                geminiTextUI.text = latestGeminiText;
            }

            if (logDebug && !string.IsNullOrEmpty(latestGeminiText))
            {
                Debug.Log("Gemini: " + latestGeminiText);
            }
        }

        // when a Gemini response arrives, reset its cooldown
        if (geminiResponseJustArrived)
        {
            geminiResponseJustArrived = false;
            nextGeminiTime = t + geminiInterval;
            if (logDebug)
            {
                Debug.Log($"LLMVRCameraStreamer: Gemini response received. Next Gemini allowed after {geminiInterval} seconds.");
            }
        }

        // send frames at a fixed interval
        if (t >= nextSendTime)
        {
            bool sendGemini = false;
            string signIdForGemini = null;

            // only send Gemini when there is a pending help request and cooldown passed
            if (!waitingForGemini &&
                helpRequestPending &&
                t >= nextGeminiTime)
            {
                sendGemini = true;
                signIdForGemini = pendingSignId;
                waitingForGemini = true;
                helpRequestPending = false;
            }

            byte modeFlag = (byte)(sendGemini ? 1 : 0);
            SendFrame(modeFlag, signIdForGemini);

            nextSendTime = t + sendInterval;
        }
    }

    /// <summary>
    /// Called by SignLessonManager when the user seems confused on a sign.
    /// signId is like "egg", "mushroom", etc.
    /// </summary>
    public void RequestGeminiHelp(string signId)
    {
        if (!connected) return;

        pendingSignId = signId ?? "";
        helpRequestPending = true;

        if (logDebug)
        {
            Debug.Log("LLMVRCameraStreamer: Gemini help requested for sign " + pendingSignId);
        }
    }

    private void SendFrame(byte modeFlag, string signId)
    {
        try
        {
            RenderTexture currentRT = RenderTexture.active;
            RenderTexture.active = captureRT;

            captureTex.ReadPixels(new Rect(0, 0, captureRT.width, captureRT.height), 0, 0);
            captureTex.Apply();

            RenderTexture.active = currentRT;

            byte[] jpgBytes = ImageConversion.EncodeToJPG(captureTex, jpgQuality);

            byte[] payload;

            if (modeFlag == 1)
            {
                // Gemini frame: [1 byte labelLen][label bytes][jpg bytes]
                string s = signId ?? "";
                byte[] labelBytes = Encoding.UTF8.GetBytes(s);
                int labelLen = Mathf.Min(labelBytes.Length, 255);

                payload = new byte[1 + labelLen + jpgBytes.Length];
                payload[0] = (byte)labelLen;
                Buffer.BlockCopy(labelBytes, 0, payload, 1, labelLen);
                Buffer.BlockCopy(jpgBytes, 0, payload, 1 + labelLen, jpgBytes.Length);
            }
            else
            {
                // YOLO only: payload is just the image
                payload = jpgBytes;
            }

            int length = payload.Length;
            byte[] header = new byte[5];

            header[0] = modeFlag;
            header[1] = (byte)((length >> 24) & 0xFF);
            header[2] = (byte)((length >> 16) & 0xFF);
            header[3] = (byte)((length >> 8) & 0xFF);
            header[4] = (byte)(length & 0xFF);

            if (logDebug)
            {
                Debug.Log(
                    $"LLMVRCameraStreamer: sending {(modeFlag == 1 ? "GEMINI" : "YOLO")} frame, " +
                    $"{payload.Length} bytes, header: {BitConverter.ToString(header)}"
                );
            }

            lock (sendLock)
            {
                stream.Write(header, 0, header.Length);
                stream.Write(payload, 0, payload.Length);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("LLMVRCameraStreamer SendFrame error: " + e.Message);
            connected = false;
        }
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[4096];
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
                    ParseServerJson(line);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("LLMVRCameraStreamer ReceiveLoop error: " + e.Message);
        }

        connected = false;
    }

    private void ParseServerJson(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        if (logDebug) Debug.Log("LLMVRCameraStreamer server line: " + line);

        ServerResponse resp = null;
        try
        {
            resp = JsonUtility.FromJson<ServerResponse>(line);
        }
        catch (Exception e)
        {
            Debug.LogWarning("LLMVRCameraStreamer JSON parse error: " + e.Message);
            return;
        }

        if (resp == null) return;

        // YOLO prediction
        if (!string.IsNullOrEmpty(resp.label))
        {
            latestLabel = resp.label;
            latestConf = resp.conf;
        }

        // EEG confusion
        latestEegConfused = resp.eeg_confused;

        if (resp.mode == "gemini")
        {
            waitingForGemini = false;
            latestGeminiText = resp.gemini ?? "";
            geminiDirty = true;
            geminiResponseJustArrived = true;
        }
        else if (resp.mode == "yolo")
        {
            // normal YOLO, nothing extra
        }
        else if (resp.mode == "error")
        {
            waitingForGemini = false;
            latestGeminiText = "[Server error] " + (resp.error ?? "");
            geminiDirty = true;
            geminiResponseJustArrived = true;
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

    public int GetLatestEegConfused()
    {
        return latestEegConfused;   // 0 or 1
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

        try
        {
            if (recvThread != null && recvThread.IsAlive)
            {
                recvThread.Abort();
            }
        }
        catch { }
    }
}
