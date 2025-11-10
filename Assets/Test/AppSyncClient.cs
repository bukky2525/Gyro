using UnityEngine;
using System;
using System.Collections;
using NativeWebSocket;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#region DataModels
// JSON-RPC messages for AppSync
namespace AppSync.Messages
{
    // wss:// ヘッダー用
    public class WssHeader
    {
        [JsonProperty("host")]
        public string Host { get; set; }
        [JsonProperty("x-api-key")]
        public string ApiKey { get; set; }
    }

    // connection_init メッセージ用
    public class ConnectionInitRequest
    {
        [JsonProperty("type")]
        public string Type { get; } = "connection_init";
    }

    // subscribe メッセージ用
    public class SubscribeRequest
    {
        [JsonProperty("type")]
        public string Type { get; } = "subscribe";
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("channel")]
        public string Channel { get; set; }
        [JsonProperty("authorization")]
        public Dictionary<string, string> Authorization { get; set; }
    }

    // publish メッセージ用
    public class PublishRequest
    {
        [JsonProperty("type")]
        public string Type { get; } = "publish";
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("channel")]
        public string Channel { get; set; }
        [JsonProperty("events")]
        public List<string> Events { get; set; }
        [JsonProperty("authorization")]
        public Dictionary<string, string> Authorization { get; set; }
    }

    // イベントペイロード用
    public class EventPayload
    {
        [JsonProperty("message")]
        public string Message { get; set; }
        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }
    }

    // unsubscribe メッセージ用
    public class UnsubscribeRequest
    {
        [JsonProperty("type")]
        public string Type { get; } = "unsubscribe";
        [JsonProperty("id")]
        public string Id { get; set; }
    }
}
#endregion

[Serializable]
public struct GyroData
{
    public float Alpha;
    public float Beta;
    public float Gamma;
    public string Timestamp;

    public GyroData(float alpha, float beta, float gamma, string timestamp)
    {
        Alpha = alpha;
        Beta = beta;
        Gamma = gamma;
        Timestamp = timestamp;
    }

    public override string ToString()
    {
        return $"Alpha:{Alpha:F2}°, Beta:{Beta:F2}°, Gamma:{Gamma:F2}°, Timestamp:{Timestamp ?? "null"}";
    }
}

public class AppSyncClient : MonoBehaviour
{
    [Header("AWS AppSync 設定")]
    [Tooltip("例: xxx.appsync-api.us-east-1.amazonaws.com")]
    [SerializeField] private string httpDomain;
    [Tooltip("例: xxx.appsync-realtime-api.us-east-1.amazonaws.com")]
    [SerializeField] private string realtimeDomain;
    [Tooltip("AppSync認可用APIキー")]
    [SerializeField] private string apiKey;
    [Tooltip("publish・subscribeするチャンネル")]
    [SerializeField] private string channel = "/default/test";

    [Header("ジャイロ設定")]
    [SerializeField] private bool logGyroValues = true;
    [SerializeField] private Transform gyroTarget;
    [SerializeField] private bool applyGyroToTarget = false;
    [SerializeField] private float gyroRotationLerpSpeed = 10f;
    [SerializeField] private Vector3 gyroRotationOffset = Vector3.zero;

    private WebSocket _websocket;
    private bool _isConnected = false;
    private string _currentSubscriptionId;
    private GyroData _latestGyroData;
    private bool _hasGyroData = false;
    private Quaternion _gyroTargetRotation = Quaternion.identity;

    private string RealtimeEndpoint => $"wss://{realtimeDomain}/event/realtime";

    public event Action<GyroData> OnGyroDataReceived;

    public bool HasGyroData => _hasGyroData;
    public GyroData LatestGyroData => _latestGyroData;

    private string GetEncodedHeaderInfo()
    {
        var header = new AppSync.Messages.WssHeader
        {
            Host = httpDomain,
            ApiKey = apiKey
        };
        string jsonHeader = JsonConvert.SerializeObject(header);
        byte[] headerBytes = Encoding.UTF8.GetBytes(jsonHeader);
        string base64Header = Convert.ToBase64String(headerBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return $"header-{base64Header}";
    }

    async void Start()
    {
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(httpDomain) || string.IsNullOrEmpty(realtimeDomain))
        {
            Debug.LogError("AppSyncの設定が不足しています。インスペクターで設定してください。");
            return;
        }

        if (applyGyroToTarget && gyroTarget == null)
        {
            gyroTarget = transform;
        }

        await InitializeConnection();
    }

    private async Task InitializeConnection()
    {
        try
        {
            Debug.Log($"AppSyncに接続します: {RealtimeEndpoint}");

            var subprotocols = new List<string> 
            {  
                "aws-appsync-event-ws", 
                GetEncodedHeaderInfo() 
            };

            Debug.Log($"subprotocolsを使用: [{string.Join(", ", subprotocols)}]");

            _websocket = new WebSocket(RealtimeEndpoint, subprotocols);

            _websocket.OnOpen += () =>
            {
                _isConnected = true;
                Debug.Log("AppSyncへの接続が確立されました。");
                SendConnectionInit();
            };

            _websocket.OnMessage += OnMessageReceived;

            _websocket.OnError += (e) =>
            {
                Debug.LogError($"AppSyncエラー: {e}");
                _isConnected = false;
            };

            _websocket.OnClose += (e) =>
            {
                Debug.Log($"AppSync接続が閉じられました: {e}");
                _isConnected = false;
            };

            await _websocket.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"AppSync接続の初期化に失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    private void OnMessageReceived(byte[] bytes)
    {
        var message = Encoding.UTF8.GetString(bytes);
        Debug.Log($"メッセージ受信: {message}");
        
        try
        {
            var json = JObject.Parse(message);
            string type = (string)json["type"];

            switch (type)
            {
                case "connection_ack":
                    Debug.Log("接続が確認されました。チャンネルをsubscribeします...");
                    SubscribeToChannel();
                    break;
                case "subscribe_success":
                    Debug.Log("チャンネルをsubscribeしました。");
                    break;
                case "publish_success":
                    Debug.Log("イベントをpublishしました。");
                    break;
                case "data":
                    HandleDataMessage(json);
                    break;
                case "ka": // Keep-alive
                    Debug.Log("Keep-aliveメッセージを受信しました。");
                    break;
                case "unsubscribe_success":
                    Debug.Log("チャンネルをunsubscribeしました。");
                    break;
                default:
                    Debug.LogWarning($"未処理のメッセージタイプ '{type}': {json.ToString()}");
                    break;
            }
        }
        catch (JsonException e)
        {
            Debug.LogError($"JSONメッセージの解析に失敗: {e.Message}");
        }
    }

    private void HandleDataMessage(JObject json)
    {
        if (json["event"] == null) return;
        
        string eventStr = (string)json["event"];
        try
        {
            var eventObj = JObject.Parse(eventStr);
            Debug.Log($"イベントデータ受信: {eventObj.ToString()}");
            // ここで eventObj["message"] のようにプロパティにアクセスできる
            if (eventObj["message"] != null)
            {
                string message = (string)eventObj["message"];
                Debug.Log($"イベントメッセージ: {message}");
                TryHandleGyroPayload(message);
            }
            if (eventObj["data"] != null)
            {
                string dataPayload = eventObj["data"].ToString();
                TryHandleGyroPayload(dataPayload);
            }
        }
        catch (JsonException)
        {
             Debug.Log($"イベントデータがJSONオブジェクトではありません: {eventStr}");
        }
    }
    
    private async void SendConnectionInit()
    {
        try
        {
            var request = new AppSync.Messages.ConnectionInitRequest();
            string json = JsonConvert.SerializeObject(request);
            await _websocket.SendText(json);
            Debug.Log("connection_init メッセージを送信しました。");
        }
        catch (Exception e)
        {
            Debug.LogError($"connection_init の送信に失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    private async void SubscribeToChannel()
    {
        try
        {
            _currentSubscriptionId = Guid.NewGuid().ToString();
            var request = new AppSync.Messages.SubscribeRequest
            {
                Id = _currentSubscriptionId,
                Channel = channel,
                Authorization = new Dictionary<string, string>
                {
                    { "host", httpDomain },
                    { "x-api-key", apiKey }
                }
            };
            string json = JsonConvert.SerializeObject(request);
            await _websocket.SendText(json);
            Debug.Log($"subscribe リクエストを送信しました (ID: {_currentSubscriptionId})");
        }
        catch (Exception e)
        {
            Debug.LogError($"subscribeに失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    public async void PublishEvent(string message)
    {
        if (!_isConnected)
        {
            Debug.LogWarning("接続されていません。イベントを発行できません。");
            return;
        }

        try
        {
            var payload = new AppSync.Messages.EventPayload
            {
                Message = message,
                Timestamp = DateTime.UtcNow.ToString("o")
            };
            string innerJson = JsonConvert.SerializeObject(payload);

            var request = new AppSync.Messages.PublishRequest
            {
                Id = Guid.NewGuid().ToString(),
                Channel = channel,
                Events = new List<string> { innerJson },
                Authorization = new Dictionary<string, string> { { "x-api-key", apiKey } }
            };
            
            string json = JsonConvert.SerializeObject(request);
            await _websocket.SendText(json);
            Debug.Log($"イベントを発行しました: {json}");
        }
        catch (Exception e)
        {
            Debug.LogError($"イベントの発行に失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    public async Task Unsubscribe()
    {
        if (!_isConnected)
        {
            Debug.LogWarning("接続されていません。subscribe解除できません。");
            return;
        }

        if (string.IsNullOrEmpty(_currentSubscriptionId))
        {
            Debug.LogWarning("unsubscribeするアクティブなsubscriptionがありません。");
            return;
        }

        try
        {
            var request = new AppSync.Messages.UnsubscribeRequest { Id = _currentSubscriptionId };
            string json = JsonConvert.SerializeObject(request);
            await _websocket.SendText(json);
            Debug.Log($"unsubscribe リクエストを送信しました (ID: {_currentSubscriptionId})");
            _currentSubscriptionId = null;
        }
        catch (Exception e)
        {
            Debug.LogError($"unsubscribeに失敗: {e.Message}\n{e.StackTrace}");
        }
    }

    async void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
            _websocket?.DispatchMessageQueue();
        #endif

        if (applyGyroToTarget && gyroTarget != null && _hasGyroData)
        {
            if (gyroRotationLerpSpeed > 0f)
            {
                gyroTarget.rotation = Quaternion.Slerp(gyroTarget.rotation, _gyroTargetRotation, Time.deltaTime * gyroRotationLerpSpeed);
            }
            else
            {
                gyroTarget.rotation = _gyroTargetRotation;
            }
        }

        // テスト用
        if (Input.GetKeyDown(KeyCode.P))
        {
            PublishEvent("Hello from Unity!");
        }
        if (Input.GetKeyDown(KeyCode.U))
        {
            await Unsubscribe();
        }
    }

    private void TryHandleGyroPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return;

        if (TryParseGyroData(payload, out var gyroData))
        {
            _latestGyroData = gyroData;
            _hasGyroData = true;
            _gyroTargetRotation = ConvertGyroToUnityRotation(gyroData);

            if (logGyroValues)
            {
                string timestampInfo = string.IsNullOrEmpty(gyroData.Timestamp) ? "タイムスタンプ: なし" : $"タイムスタンプ: {gyroData.Timestamp}";
                Debug.Log($"ジャイロデータ受信 => Alpha(Z): {gyroData.Alpha:F2}°, Beta(X): {gyroData.Beta:F2}°, Gamma(Y): {gyroData.Gamma:F2}° | {timestampInfo}");
            }

            OnGyroDataReceived?.Invoke(gyroData);
        }
        else if (logGyroValues && payload.Contains("alpha"))
        {
            Debug.LogWarning($"ジャイロデータの解析に失敗しました: {payload}");
        }
    }

    private bool TryParseGyroData(string payload, out GyroData gyroData)
    {
        gyroData = default;
        try
        {
            JToken token = JToken.Parse(payload);

            if (token.Type == JTokenType.String)
            {
                string inner = token.Value<string>();
                if (string.IsNullOrWhiteSpace(inner))
                {
                    return false;
                }
                token = JToken.Parse(inner);
            }

            if (token.Type != JTokenType.Object)
            {
                return false;
            }

            var obj = (JObject)token;

            if (!TryReadFloat(obj["alpha"], out float alpha)) return false;
            if (!TryReadFloat(obj["beta"], out float beta)) return false;
            if (!TryReadFloat(obj["gamma"], out float gamma)) return false;

            string timestamp = obj["timestamp"]?.ToString();
            gyroData = new GyroData(alpha, beta, gamma, timestamp);
            return true;
        }
        catch (JsonReaderException)
        {
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"ジャイロデータ解析中に例外が発生しました: {e.Message}");
            return false;
        }
    }

    private bool TryReadFloat(JToken token, out float value)
    {
        value = 0f;
        if (token == null) return false;

        switch (token.Type)
        {
            case JTokenType.Float:
            case JTokenType.Integer:
                value = token.Value<float>();
                return true;
            case JTokenType.String:
                return float.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            default:
                return false;
        }
    }

    private Quaternion ConvertGyroToUnityRotation(GyroData gyroData)
    {
        Vector3 euler = new Vector3(gyroData.Beta, gyroData.Alpha, -gyroData.Gamma) + gyroRotationOffset;
        return Quaternion.Euler(euler);
    }
}