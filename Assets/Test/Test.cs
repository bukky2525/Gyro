using UnityEngine;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using System.Security.Cryptography;

[Serializable]
public struct DirectionData
{
    public string Direction; // "up", "down", "left", "right", "stop"
    public string Timestamp;

    public DirectionData(string direction, string timestamp)
    {
        Direction = direction;
        Timestamp = timestamp;
    }
}

public class Test : MonoBehaviour
{
    [Header("WebSocket サーバー設定")]
    [Tooltip("WebSocketサーバーのポート番号")]
    [SerializeField] private int port = 8080;
    
    [Header("方向入力設定")]
    [Tooltip("方向データのログを表示するか")]
    [SerializeField] private bool logDirectionValues = true;

    [Tooltip("受信した方向データをUnityの入力として使用するか")]
    [SerializeField] private bool useDirectionAsInput = true;

    private TcpListener _tcpListener;
    private ConcurrentDictionary<TcpClient, NetworkStream> _clients = new ConcurrentDictionary<TcpClient, NetworkStream>();
    private bool _isServerRunning = false;
    private DirectionData _latestDirectionData;
    private bool _hasDirectionData = false;
    private string _currentDirection = "";
    private string _localIPAddress = "";

    public event Action<DirectionData> OnDirectionDataReceived;
    public event Action<string> OnClientConnected;
    public event Action<string> OnClientDisconnected;

    public bool IsServerRunning => _isServerRunning;
    public bool HasDirectionData => _hasDirectionData;
    public DirectionData LatestDirectionData => _latestDirectionData;
    public string CurrentDirection => _currentDirection;
    public string LocalIPAddress => _localIPAddress;
    public int ConnectedClientsCount => _clients.Count;

    async void Start()
    {
        // ローカルIPアドレスを取得
        _localIPAddress = GetLocalIPAddress();
        await StartWebSocketServer();
    }

    private string GetLocalIPAddress()
    {
        try
        {
            string hostName = Dns.GetHostName();
            IPHostEntry hostEntry = Dns.GetHostEntry(hostName);
            
            // IPv4アドレスを取得（ローカルホストを除く）
            foreach (IPAddress ip in hostEntry.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
            
            // フォールバック: 127.0.0.1
            return "127.0.0.1";
        }
        catch (Exception e)
        {
            Debug.LogWarning($"IPアドレスの取得に失敗: {e.Message}");
            return "127.0.0.1";
        }
    }

    private async Task StartWebSocketServer()
    {
        try
        {
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _tcpListener.Start();
            _isServerRunning = true;
            
            Debug.Log($"=== WebSocketサーバーを開始しました ===");
            Debug.Log($"ポート: {port}");
            Debug.Log($"ローカル接続: ws://localhost:{port}");
            Debug.Log($"スマホから接続: ws://{_localIPAddress}:{port}");
            Debug.Log($"同じWi-Fiネットワークに接続してください");
            Debug.Log($"=====================================");

            // クライアント接続を待機
            _ = AcceptClients();
        }
        catch (Exception e)
        {
            Debug.LogError($"WebSocketサーバーの起動に失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    private async Task AcceptClients()
    {
        while (_isServerRunning)
        {
            try
            {
                TcpClient client = await _tcpListener.AcceptTcpClientAsync();
                NetworkStream stream = client.GetStream();
                _clients.TryAdd(client, stream);
                Debug.Log($"クライアントが接続しました: {client.Client.RemoteEndPoint}");
                OnClientConnected?.Invoke(client.Client.RemoteEndPoint.ToString());
                
                // 各クライアントからのメッセージを処理
                _ = HandleClient(client, stream);
            }
            catch (Exception e)
            {
                if (_isServerRunning)
                {
                    Debug.LogError($"クライアント接続の受け入れに失敗: {e.Message}");
                }
            }
        }
    }

    private async Task HandleClient(TcpClient client, NetworkStream stream)
    {
        byte[] buffer = new byte[4096];
        List<byte> frameBuffer = new List<byte>();

        try
        {
            // WebSocketハンドシェイクを処理
            bool handshakeCompleted = await PerformWebSocketHandshake(stream);
            if (!handshakeCompleted)
            {
                Debug.LogWarning("WebSocketハンドシェイクに失敗しました");
                client.Close();
                _clients.TryRemove(client, out _);
                return;
            }

            Debug.Log($"WebSocketハンドシェイクが完了しました: {client.Client.RemoteEndPoint}");

            // 接続確認メッセージを送信
            await SendWebSocketMessage(stream, "{\"type\":\"connected\",\"message\":\"Unityに接続しました\"}");

            // メッセージを受信
            while (client.Connected && _isServerRunning)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break;
                }

                frameBuffer.AddRange(buffer.Take(bytesRead));

                // WebSocketフレームを解析
                while (frameBuffer.Count >= 2)
                {
                    int frameLength = GetWebSocketFrameLength(frameBuffer.ToArray());
                    if (frameLength > 0 && frameBuffer.Count >= frameLength)
                    {
                        byte[] frame = frameBuffer.Take(frameLength).ToArray();
                        frameBuffer.RemoveRange(0, frameLength);

                        string message = DecodeWebSocketFrame(frame, frame.Length);
                        if (!string.IsNullOrEmpty(message))
                        {
                            Debug.Log($"メッセージ受信: {message}");
                            ProcessMessage(message, client, stream);
                        }
                    }
                    else if (frameLength == 0)
                    {
                        break;
                    }
                    else
                    {
                        break; // フレームが完全に受信されるまで待機
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"クライアント処理エラー: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            Debug.Log($"クライアントが切断しました: {client.Client.RemoteEndPoint}");
            OnClientDisconnected?.Invoke(client.Client.RemoteEndPoint.ToString());
            try
            {
                client.Close();
            }
            catch { }
            _clients.TryRemove(client, out _);
        }
    }

    private async Task<bool> PerformWebSocketHandshake(NetworkStream stream)
    {
        try
        {
            byte[] buffer = new byte[4096];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // WebSocketキーを取得
            string key = ExtractWebSocketKey(request);
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("WebSocketキーが見つかりませんでした");
                return false;
            }

            // WebSocket Acceptキーを生成
            string acceptKey = GenerateWebSocketAcceptKey(key);

            // ハンドシェイクレスポンスを送信
            string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                             "Upgrade: websocket\r\n" +
                             "Connection: Upgrade\r\n" +
                             $"Sec-WebSocket-Accept: {acceptKey}\r\n" +
                             "Sec-WebSocket-Protocol: \r\n\r\n";

            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
            await stream.FlushAsync();

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"ハンドシェイク処理エラー: {e.Message}");
            return false;
        }
    }

    private string ExtractWebSocketKey(string request)
    {
        string[] lines = request.Split('\n');
        foreach (string line in lines)
        {
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            {
                return line.Substring(line.IndexOf(':') + 1).Trim();
            }
        }
        return null;
    }

    private string GenerateWebSocketAcceptKey(string key)
    {
        const string magicString = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        string combined = key + magicString;
        byte[] hash = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToBase64String(hash);
    }

    private int GetWebSocketFrameLength(byte[] buffer)
    {
        if (buffer.Length < 2)
        {
            return 0;
        }

        int payloadLength = buffer[1] & 0x7F;
        int offset = 2;

        if (payloadLength == 126)
        {
            if (buffer.Length < 4)
            {
                return 0;
            }
            payloadLength = (buffer[2] << 8) | buffer[3];
            offset = 4;
        }
        else if (payloadLength == 127)
        {
            if (buffer.Length < 10)
            {
                return 0;
            }
            offset = 10;
            payloadLength = (int)((buffer[2] << 56) | (buffer[3] << 48) | (buffer[4] << 40) | (buffer[5] << 32) |
                                 (buffer[6] << 24) | (buffer[7] << 16) | (buffer[8] << 8) | buffer[9]);
        }

        bool masked = (buffer[1] & 0x80) != 0;
        if (masked)
        {
            offset += 4;
        }

        int totalLength = offset + payloadLength;
        if (buffer.Length < totalLength)
        {
            return 0; // フレームが完全に受信されていない
        }

        return totalLength;
    }

    private string DecodeWebSocketFrame(byte[] buffer, int length)
    {
        if (length < 2)
        {
            return null;
        }

        bool masked = (buffer[1] & 0x80) != 0;
        int payloadLength = buffer[1] & 0x7F;

        int offset = 2;
        if (payloadLength == 126)
        {
            if (length < 4)
            {
                return null;
            }
            payloadLength = (buffer[2] << 8) | buffer[3];
            offset = 4;
        }
        else if (payloadLength == 127)
        {
            if (length < 10)
            {
                return null;
            }
            offset = 10;
            payloadLength = (int)((buffer[2] << 56) | (buffer[3] << 48) | (buffer[4] << 40) | (buffer[5] << 32) |
                                 (buffer[6] << 24) | (buffer[7] << 16) | (buffer[8] << 8) | buffer[9]);
        }

        byte[] maskingKey = null;
        if (masked)
        {
            if (length < offset + 4)
            {
                return null;
            }
            maskingKey = new byte[4];
            Array.Copy(buffer, offset, maskingKey, 0, 4);
            offset += 4;
        }

        if (length < offset + payloadLength)
        {
            return null;
        }

        byte[] payload = new byte[payloadLength];
        Array.Copy(buffer, offset, payload, 0, payloadLength);

        if (masked && maskingKey != null)
        {
            for (int i = 0; i < payloadLength; i++)
            {
                payload[i] = (byte)(payload[i] ^ maskingKey[i % 4]);
            }
        }

        try
        {
            return Encoding.UTF8.GetString(payload);
        }
        catch
        {
            return null;
        }
    }

    private async Task SendWebSocketMessage(NetworkStream stream, string message)
    {
        try
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            byte[] frame = EncodeWebSocketFrame(messageBytes);
            await stream.WriteAsync(frame, 0, frame.Length);
            await stream.FlushAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"メッセージ送信エラー: {e.Message}");
        }
    }

    private byte[] EncodeWebSocketFrame(byte[] payload)
    {
        List<byte> frame = new List<byte>();
        
        // FINフラグとオペコード（テキストフレーム）
        frame.Add(0x81);
        
        // ペイロード長
        if (payload.Length < 126)
        {
            frame.Add((byte)payload.Length);
        }
        else if (payload.Length < 65536)
        {
            frame.Add(126);
            frame.Add((byte)(payload.Length >> 8));
            frame.Add((byte)(payload.Length & 0xFF));
        }
        else
        {
            frame.Add(127);
            for (int i = 7; i >= 0; i--)
            {
                frame.Add((byte)((payload.Length >> (i * 8)) & 0xFF));
            }
        }
        
        // ペイロード（サーバーから送信するのでマスキングなし）
        frame.AddRange(payload);
        
        return frame.ToArray();
    }

    private async void ProcessMessage(string message, TcpClient client, NetworkStream stream)
    {
        try
        {
            DirectionData directionData = JsonConvert.DeserializeObject<DirectionData>(message);
            
            if (IsValidDirection(directionData.Direction))
            {
                _latestDirectionData = directionData;
                _hasDirectionData = true;
                _currentDirection = directionData.Direction;

                if (logDirectionValues)
                {
                    Debug.Log($"方向データ受信: {directionData.Direction} (タイムスタンプ: {directionData.Timestamp ?? "なし"})");
                }

                // 受信確認メッセージを送信
                string ackMessage = $"{{\"type\":\"ack\",\"direction\":\"{directionData.Direction}\"}}";
                await SendWebSocketMessage(stream, ackMessage);

                OnDirectionDataReceived?.Invoke(directionData);
            }
            else
            {
                Debug.LogWarning($"無効な方向データ: {directionData.Direction}");
            }
        }
        catch (JsonException e)
        {
            Debug.LogWarning($"JSONのパースに失敗: {e.Message} - メッセージ: {message}");
        }
        catch (Exception e)
        {
            Debug.LogError($"メッセージの処理に失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    private bool IsValidDirection(string direction)
    {
        return direction == "up" || direction == "down" || 
               direction == "left" || direction == "right" || 
               direction == "stop";
    }

    void Update()
    {
        // 方向データを使用してUnityの入力として処理
        if (useDirectionAsInput && _hasDirectionData)
        {
            // ここで方向データをUnityの入力として使用
            // 例: オブジェクトの移動など
            ProcessDirectionInput(_currentDirection);
        }
    }

    private void ProcessDirectionInput(string direction)
    {
        // 方向データをUnityの入力として処理
        // この部分は、実際のゲームの要件に応じて実装してください
        switch (direction)
        {
            case "up":
                // 上方向の処理
                break;
            case "down":
                // 下方向の処理
                break;
            case "left":
                // 左方向の処理
                break;
            case "right":
                // 右方向の処理
                break;
            case "stop":
                // 停止の処理
                break;
        }
    }

    void OnDestroy()
    {
        StopWebSocketServer();
    }

    void OnApplicationQuit()
    {
        StopWebSocketServer();
    }

    private void StopWebSocketServer()
    {
        _isServerRunning = false;
        
        if (_tcpListener != null)
        {
            try
            {
                _tcpListener.Stop();
            }
            catch { }
            _tcpListener = null;
        }

        foreach (var kvp in _clients)
        {
            try
            {
                if (kvp.Key.Connected)
                {
                    kvp.Key.Close();
                }
            }
            catch { }
        }
        _clients.Clear();

        Debug.Log("WebSocketサーバーを停止しました");
    }

    // デバッグ用: サーバーの状態を表示
    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = Color.white;
        style.wordWrap = true;

        int yPos = 10;
        GUI.Label(new Rect(10, yPos, 800, 30), $"WebSocketサーバー: {(_isServerRunning ? "実行中 ✅" : "停止中 ❌")}", style);
        yPos += 35;
        
        GUI.Label(new Rect(10, yPos, 800, 30), $"ポート: {port}", style);
        yPos += 35;
        
        if (!string.IsNullOrEmpty(_localIPAddress))
        {
            GUI.Label(new Rect(10, yPos, 800, 30), $"ローカルIP: {_localIPAddress}", style);
            yPos += 35;
            GUI.Label(new Rect(10, yPos, 800, 60), $"スマホから接続: ws://{_localIPAddress}:{port}", style);
            yPos += 65;
        }
        
        GUI.Label(new Rect(10, yPos, 800, 30), $"接続クライアント数: {_clients.Count}", style);
        yPos += 35;
        
        if (_hasDirectionData)
        {
            GUI.Label(new Rect(10, yPos, 800, 30), $"現在の方向: {_currentDirection}", style);
        }
    }
}
