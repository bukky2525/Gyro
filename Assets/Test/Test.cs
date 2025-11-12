using UnityEngine;
using System;
using NativeWebSocket;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

[Serializable]
public struct DirectionData
{
    public string Direction; // "up", "down", "left", "right"
    public string Timestamp;

    public DirectionData(string direction, string timestamp)
    {
        Direction = direction;
        Timestamp = timestamp;
    }

    public override string ToString()
    {
        return $"Direction:{Direction}, Timestamp:{Timestamp ?? "null"}";
    }
}

public class Test : MonoBehaviour
{
    [Header("WebSocket 設定")]
    [Tooltip("WebSocketサーバーURL (例: ws://localhost:8080 または wss://example.com)")]
    [SerializeField] private string websocketUrl = "ws://localhost:8080";

    [Header("方向データ設定")]
    [SerializeField] private bool logDirectionValues = true;
    [SerializeField] private bool autoReconnect = true;
    [SerializeField] private float reconnectDelay = 3f;

    private WebSocket _websocket;
    private bool _isConnected = false;
    private bool _isConnecting = false;
    private DirectionData _latestDirectionData;
    private bool _hasDirectionData = false;
    private float _reconnectTimer = 0f;

    public event Action<DirectionData> OnDirectionDataReceived;
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<string> OnError;

    public bool IsConnected => _isConnected;
    public bool HasDirectionData => _hasDirectionData;
    public DirectionData LatestDirectionData => _latestDirectionData;

    async void Start()
    {
        if (string.IsNullOrEmpty(websocketUrl))
        {
            Debug.LogError("WebSocket URLが設定されていません。インスペクターで設定してください。");
            return;
        }

        await Connect();
    }

    public async Task Connect()
    {
        if (_isConnecting || _isConnected)
        {
            Debug.LogWarning("既に接続中または接続済みです。");
            return;
        }

        if (string.IsNullOrEmpty(websocketUrl))
        {
            Debug.LogError("WebSocket URLが設定されていません。");
            return;
        }

        _isConnecting = true;

        try
        {
            Debug.Log($"WebSocketに接続します: {websocketUrl}");

            _websocket = new WebSocket(websocketUrl);

            _websocket.OnOpen += () =>
            {
                _isConnected = true;
                _isConnecting = false;
                _reconnectTimer = 0f;
                Debug.Log("WebSocketへの接続が確立されました。");
                OnConnected?.Invoke();
            };

            _websocket.OnMessage += OnMessageReceived;

            _websocket.OnError += (e) =>
            {
                Debug.LogError($"WebSocketエラー: {e}");
                _isConnected = false;
                _isConnecting = false;
                OnError?.Invoke(e);
            };

            _websocket.OnClose += (e) =>
            {
                Debug.Log($"WebSocket接続が閉じられました: {e}");
                _isConnected = false;
                _isConnecting = false;
                OnDisconnected?.Invoke();
            };

            await _websocket.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"WebSocket接続の初期化に失敗: {e.Message}\n{e.StackTrace}");
            _isConnecting = false;
            OnError?.Invoke(e.Message);
        }
    }

    public async Task Disconnect()
    {
        if (_websocket != null && _isConnected)
        {
            await _websocket.Close();
            _websocket = null;
            _isConnected = false;
            _hasDirectionData = false;
        }
    }

    private void OnMessageReceived(byte[] bytes)
    {
        try
        {
            string message = Encoding.UTF8.GetString(bytes);
            Debug.Log($"メッセージ受信: {message}");

            // JSONとしてDirectionDataをパース
            if (TryParseDirectionData(message, out var directionData))
            {
                _latestDirectionData = directionData;
                _hasDirectionData = true;

                if (logDirectionValues)
                {
                    string timestampInfo = string.IsNullOrEmpty(directionData.Timestamp) 
                        ? "タイムスタンプ: なし" 
                        : $"タイムスタンプ: {directionData.Timestamp}";
                    Debug.Log($"方向データ受信 => 方向: {directionData.Direction} | {timestampInfo}");
                }

                OnDirectionDataReceived?.Invoke(directionData);
            }
            else
            {
                Debug.LogWarning($"方向データとして認識できないメッセージ: {message}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"メッセージの処理に失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    public async void SendDirectionData(string direction)
    {
        if (!_isConnected)
        {
            Debug.LogWarning("接続されていません。データを送信できません。");
            return;
        }

        if (string.IsNullOrEmpty(direction))
        {
            Debug.LogWarning("方向が指定されていません。");
            return;
        }

        // 有効な方向かチェック
        if (direction != "up" && direction != "down" && direction != "left" && direction != "right")
        {
            Debug.LogWarning($"無効な方向: {direction}");
            return;
        }

        try
        {
            var directionData = new DirectionData(direction, DateTime.UtcNow.ToString("o"));
            string json = JsonConvert.SerializeObject(directionData);
            
            await _websocket.SendText(json);
            Debug.Log($"方向データを送信しました: {json}");
        }
        catch (Exception e)
        {
            Debug.LogError($"データの送信に失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    public async void SendDirectionData(DirectionData directionData)
    {
        if (!_isConnected)
        {
            Debug.LogWarning("接続されていません。データを送信できません。");
            return;
        }

        try
        {
            string json = JsonConvert.SerializeObject(directionData);
            await _websocket.SendText(json);
            Debug.Log($"方向データを送信しました: {json}");
        }
        catch (Exception e)
        {
            Debug.LogError($"データの送信に失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    public async void SendRawMessage(string message)
    {
        if (!_isConnected)
        {
            Debug.LogWarning("接続されていません。メッセージを送信できません。");
            return;
        }

        try
        {
            await _websocket.SendText(message);
            Debug.Log($"メッセージを送信しました: {message}");
        }
        catch (Exception e)
        {
            Debug.LogError($"メッセージの送信に失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    async void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
            if (_websocket != null)
            {
                _websocket.DispatchMessageQueue();
            }
        #endif

        // 自動再接続
        if (autoReconnect && !_isConnected && !_isConnecting)
        {
            _reconnectTimer += Time.deltaTime;
            if (_reconnectTimer >= reconnectDelay)
            {
                _reconnectTimer = 0f;
                Debug.Log("自動再接続を試みます...");
                await Connect();
            }
        }

        // テスト用キー入力
        if (Input.GetKeyDown(KeyCode.P))
        {
            SendDirectionData("up");
        }
        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            SendDirectionData("down");
        }
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            SendDirectionData("left");
        }
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            SendDirectionData("right");
        }
        if (Input.GetKeyDown(KeyCode.C))
        {
            await Connect();
        }
        if (Input.GetKeyDown(KeyCode.X))
        {
            await Disconnect();
        }
    }

    private bool TryParseDirectionData(string json, out DirectionData directionData)
    {
        directionData = default;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            // JSONを直接パース
            directionData = JsonConvert.DeserializeObject<DirectionData>(json);

            // directionフィールドの存在を確認
            if (string.IsNullOrEmpty(directionData.Direction))
            {
                return false;
            }

            // 有効な方向かチェック
            if (directionData.Direction != "up" && 
                directionData.Direction != "down" && 
                directionData.Direction != "left" && 
                directionData.Direction != "right")
            {
                return false;
            }

            return true;
        }
        catch (JsonException e)
        {
            Debug.LogWarning($"JSONのパースに失敗: {e.Message}");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"方向データの解析に失敗: {e.Message}\n{e.StackTrace}");
            return false;
        }
    }

    async void OnDestroy()
    {
        await Disconnect();
    }

    async void OnApplicationQuit()
    {
        await Disconnect();
    }
}
