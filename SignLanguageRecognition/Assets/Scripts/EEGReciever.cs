using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class EEGReceiver : MonoBehaviour
{
    public string serverIP = "127.0.0.1";
    public int serverPort = 5005;

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;

    [HideInInspector]
    public int latestPrediction = 0;   // 0 = not confused, 1 = confused

    void Start()
    {
        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    void ReceiveData()
    {
        try
        {
            client = new TcpClient(serverIP, serverPort);
            stream = client.GetStream();
            byte[] buffer = new byte[1024];

            while (true)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0) continue;

                string msg = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();

                // Expecting a single number: "0" or "1"
                if (int.TryParse(msg, out int pred))
                {
                    latestPrediction = pred;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("TCP receive error: " + e.Message);
        }
    }

    void OnApplicationQuit()
    {
        receiveThread?.Abort();
        stream?.Close();
        client?.Close();
    }
}
