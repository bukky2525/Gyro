using UnityEngine;
using System;
using NativeWebSocket;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    [Header("AWS AppSync 設定")]
    [Tooltip("例: xxx.appsync-api.us-east-1.amazonaws.com")]
    [SerializeField] private string httpDomain;
    [Tooltip("例: xxx.appsync-realtime-api.us-east-1.amazonaws.com")]
    [SerializeField] private string realtimeDomain;
    [Tooltip("AppSync認可用APIキー")]
    [SerializeField] private string apiKey;
    [Tooltip("publish・subscribeするチャンネル")]
    [SerializeField] private string channel = "/default/test";

    [Header("方向データ設定")]
    [SerializeField] private bool logDirectionValues = true;

    private WebSocket _websocket;
    private bool _isConnected = false;
    private string _currentSubscriptionId;
    private DirectionData _latestDirectionData;
    private bool _hasDirectionData = false;

    private string RealtimeEndpoint => $"wss://{realtimeDomain}/event/realtime";

    public event Action<DirectionData> OnDirectionDataReceived;

    public bool HasDirectionData => _hasDirectionData;
    public DirectionData LatestDirectionData => _latestDirectionData;

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
                case "subscribe_error":
                    var errors = json["errors"];
                    if (errors != null)
                    {
                        Debug.LogError($"subscribeエラー: {errors.ToString()}");
                    }
                    else
                    {
                        Debug.LogError($"subscribeエラー: {json.ToString()}");
                    }
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
            // 方向データを処理
            if (eventObj["message"] != null)
            {
                string message = (string)eventObj["message"];
                Debug.Log($"イベントメッセージ: {message}");
                TryHandleDirectionPayload(message);
            }
            if (eventObj["data"] != null)
            {
                string dataPayload = eventObj["data"].ToString();
                TryHandleDirectionPayload(dataPayload);
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

    private bool TryHandleDirectionPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;

        if (TryParseDirectionData(payload, out var directionData))
        {
            _latestDirectionData = directionData;
            _hasDirectionData = true;

            if (logDirectionValues)
            {
                string timestampInfo = string.IsNullOrEmpty(directionData.Timestamp) ? "タイムスタンプ: なし" : $"タイムスタンプ: {directionData.Timestamp}";
                Debug.Log($"方向データ受信 => 方向: {directionData.Direction} | {timestampInfo}");
            }

            OnDirectionDataReceived?.Invoke(directionData);
            return true;
        }
        return false;
    }

    private bool TryParseDirectionData(string payload, out DirectionData directionData)
    {
        directionData = default;
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

            // 方向データの形式を確認
            string type = obj["type"]?.ToString();
            if (type != "direction")
            {
                return false;
            }

            string direction = obj["direction"]?.ToString();
            if (string.IsNullOrEmpty(direction))
            {
                return false;
            }

            // 有効な方向かチェック
            if (direction != "up" && direction != "down" && direction != "left" && direction != "right")
            {
                return false;
            }

            string timestamp = obj["timestamp"]?.ToString();
            directionData = new DirectionData(direction, timestamp);
            return true;
        }
        catch (JsonReaderException)
        {
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"方向データ解析中に例外が発生しました: {e.Message}");
            return false;
        }
    }
}