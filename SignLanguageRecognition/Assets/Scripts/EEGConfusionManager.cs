using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class EegConfusionClient : MonoBehaviour
{
    [Header("EEG server (readToUnity.py)")]
    public string serverIp = "127.0.0.1"; // same machine as readToUnity.py
    public int serverPort = 5005;
    public bool logDebug = true;

    private TcpClient client;
    private NetworkStream stream;
    private Thread recvThread;
    private volatile int latestPred = 0; // 0 = not confused, 1 = confused
    private bool running = true;

    void Start()
    {
        try
        {
            client = new TcpClient();
            client.Connect(serverIp, serverPort);
            stream = client.GetStream();

            if (logDebug)
                Debug.Log($"EegConfusionClient: connected to {serverIp}:{serverPort}");

            recvThread = new Thread(ReceiveLoop);
            recvThread.IsBackground = true;
            recvThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError("EegConfusionClient: could not connect to EEG server: " + e);
            enabled = false;
        }
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[256];
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
                    ParseLine(line);
                }
            }
        }
        catch (Exception e)
        {
            if (logDebug)
                Debug.LogWarning("EegConfusionClient ReceiveLoop error: " + e.Message);
        }
    }

    private void ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        if (logDebug) Debug.Log("EEG prediction: " + line);

        if (int.TryParse(line, out int val))
        {
            latestPred = (val == 1) ? 1 : 0;
        }
    }

    public bool IsConfused()
    {
        return latestPred == 1;
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
