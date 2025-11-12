using UnityEngine;
using System;
using NativeWebSocket;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

[Serializable]
public struct GyroData
{
    public float alpha;
    public float beta;
    public float gamma;
    public string timestamp;
}

public class Test : MonoBehaviour
{
    [Header("WebSocket サーバー設定")]
    [Tooltip("中継サーバーのWebSocket URL (例: ws://localhost:8080 または ws://192.168.x.x:8080)")]
    [SerializeField] private string serverUrl = "ws://localhost:8080";
    
    [Header("ジャイロデータ設定")]
    [Tooltip("ジャイロデータのログを表示するか")]
    [SerializeField] private bool logGyroValues = true;
    
    [Tooltip("受信したジャイロデータをUnityの入力として使用するか")]
    [SerializeField] private bool useGyroAsInput = true;

    private WebSocket _websocket;
    private bool _isConnected = false;
    private GyroData _latestGyroData;
    private bool _hasGyroData = false;

    public event Action<GyroData> OnGyroDataReceived;
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<string> OnError;

    public bool IsConnected => _isConnected;
    public bool HasGyroData => _hasGyroData;
    public GyroData LatestGyroData => _latestGyroData;

    async void Start()
    {
        await ConnectToServer();
    }

    private async Task ConnectToServer()
    {
        if (string.IsNullOrEmpty(serverUrl))
        {
            Debug.LogError("サーバーURLが設定されていません。インスペクターで設定してください。");
            return;
        }

        try
        {
            Debug.Log($"WebSocketサーバーに接続します: {serverUrl}");

            _websocket = new WebSocket(serverUrl);

            _websocket.OnOpen += () =>
            {
                _isConnected = true;
                Debug.Log("WebSocketサーバーに接続しました！");
                
                // Unityクライアントであることをサーバーに通知
                SendUnityInit();
                
                OnConnected?.Invoke();
            };

            _websocket.OnMessage += OnMessageReceived;

            _websocket.OnError += (e) =>
            {
                Debug.LogError($"WebSocketエラー: {e}");
                _isConnected = false;
                OnError?.Invoke(e);
            };

            _websocket.OnClose += (e) =>
            {
                Debug.Log($"WebSocket接続が閉じられました: {e}");
                _isConnected = false;
                OnDisconnected?.Invoke();
            };

            await _websocket.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"WebSocket接続の初期化に失敗: {e.Message}\n{e.StackTrace}");
            OnError?.Invoke(e.Message);
        }
    }

    private void OnMessageReceived(byte[] bytes)
    {
        try
        {
            string message = Encoding.UTF8.GetString(bytes);
            
            // UNITY_INITメッセージは無視
            if (message.Contains("UNITY_INIT"))
            {
                return;
            }

            Debug.Log($"データ受信: {message}");
            
            // JSONとしてジャイロデータをパース
            GyroData gyroData = JsonConvert.DeserializeObject<GyroData>(message);
            
            _latestGyroData = gyroData;
            _hasGyroData = true;

            if (logGyroValues)
            {
                Debug.Log($"ジャイロデータ受信 => Alpha: {gyroData.alpha}, Beta: {gyroData.beta}, Gamma: {gyroData.gamma}");
            }

            OnGyroDataReceived?.Invoke(gyroData);
        }
        catch (JsonException e)
        {
            Debug.LogWarning($"JSONのパースに失敗: {e.Message}");
        }
        catch (Exception e)
        {
            Debug.LogError($"メッセージの処理に失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    private async void SendUnityInit()
    {
        try
        {
            string initMessage = "UNITY_INIT: Ready for Gyro Data";
            await _websocket.SendText(initMessage);
            Debug.Log("Unityクライアントとしてサーバーに登録しました");
        }
        catch (Exception e)
        {
            Debug.LogError($"Unity初期化メッセージの送信に失敗: {e.Message}");
        }
    }

    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
            if (_websocket != null)
            {
                _websocket.DispatchMessageQueue();
            }
        #endif

        // ジャイロデータを使用してUnityの入力として処理
        if (useGyroAsInput && _hasGyroData)
        {
            ProcessGyroInput(_latestGyroData);
        }
    }

    private void ProcessGyroInput(GyroData gyroData)
    {
        // ジャイロデータをUnityの入力として処理
        // この部分は、実際のゲームの要件に応じて実装してください
        // 例: オブジェクトの回転、移動など
        
        // 例: Transformを回転させる
        // transform.Rotate(new Vector3(gyroData.beta, gyroData.alpha, gyroData.gamma) * Time.deltaTime);
    }

    public async Task Disconnect()
    {
        if (_websocket != null && _isConnected)
        {
            await _websocket.Close();
            _websocket = null;
            _isConnected = false;
            _hasGyroData = false;
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

    // デバッグ用: サーバーの状態を表示
    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = Color.white;
        style.wordWrap = true;

        int yPos = 10;
        GUI.Label(new Rect(10, yPos, 800, 30), $"WebSocket接続: {(_isConnected ? "接続済み ✅" : "未接続 ❌")}", style);
        yPos += 35;
        
        GUI.Label(new Rect(10, yPos, 800, 30), $"サーバーURL: {serverUrl}", style);
        yPos += 35;
        
        if (_hasGyroData)
        {
            GUI.Label(new Rect(10, yPos, 800, 30), $"Alpha: {_latestGyroData.alpha:F2}", style);
            yPos += 35;
            GUI.Label(new Rect(10, yPos, 800, 30), $"Beta: {_latestGyroData.beta:F2}", style);
            yPos += 35;
            GUI.Label(new Rect(10, yPos, 800, 30), $"Gamma: {_latestGyroData.gamma:F2}", style);
        }
    }
}
