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
    [Tooltip("publish・subscribeするチャンネル（名前空間を含む場合: default/gyro、名前空間を含まない場合: gyro）")]
    [SerializeField] private string channel = "default/gyro";

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
        Debug.Log($"【方向データ受信】受信メッセージ全体: {json.ToString()}");
        
        // AppSyncのデータ構造を確認
        // パターン1: { "type": "data", "id": "...", "data": { "data": { "subscribe": { "data": "...", "name": "..." } } } }
        // パターン2: { "type": "data", "event": "..." }
        
        string rawMessage = json.ToString();
        
        // まず、dataフィールドを確認
        if (json["data"] != null)
        {
            Debug.Log($"【方向データ受信】dataフィールドが見つかりました");
            var dataObj = json["data"];
            
            // data.data.subscribe.data の構造を確認
            if (dataObj["data"] != null)
            {
                var innerData = dataObj["data"];
                if (innerData["subscribe"] != null)
                {
                    var subscribe = innerData["subscribe"];
                    Debug.Log($"【方向データ受信】subscribeフィールドが見つかりました: {subscribe.ToString()}");
                    
                    if (subscribe["data"] != null)
                    {
                        var payload = subscribe["data"];
                        Debug.Log($"【方向データ受信】subscribe.dataが見つかりました: {payload.ToString()}");
                        
                        // データが文字列の場合はパース
                        string payloadStr = payload.Type == JTokenType.String 
                            ? payload.Value<string>() 
                            : payload.ToString();
                            
                        Debug.Log($"【方向データ受信】ペイロード文字列: {payloadStr}");
                        TryHandleDirectionPayload(payloadStr);
                        return;
                    }
                }
            }
            
            // フォールバック: dataフィールドを直接処理
            string dataStr = dataObj.ToString();
            Debug.Log($"【方向データ受信】dataフィールドを直接処理: {dataStr}");
            TryHandleDirectionPayload(dataStr);
        }
        
        // パターン2: eventフィールドを確認
        if (json["event"] != null)
        {
            Debug.Log($"【方向データ受信】eventフィールドが見つかりました");
            string eventStr = json["event"].ToString();
            try
            {
                var eventObj = JObject.Parse(eventStr);
                Debug.Log($"【方向データ受信】イベントデータ: {eventObj.ToString()}");
                
                if (eventObj["message"] != null)
                {
                    string message = eventObj["message"].ToString();
                    Debug.Log($"【方向データ受信】イベントメッセージ: {message}");
                    TryHandleDirectionPayload(message);
                }
                if (eventObj["data"] != null)
                {
                    string dataPayload = eventObj["data"].ToString();
                    Debug.Log($"【方向データ受信】イベントデータ: {dataPayload}");
                    TryHandleDirectionPayload(dataPayload);
                }
            }
            catch (JsonException ex)
            {
                Debug.LogWarning($"【方向データ受信】イベントデータのパースに失敗: {ex.Message}");
                // パースに失敗した場合は、文字列として処理
                TryHandleDirectionPayload(eventStr);
            }
        }
        
        // フォールバック: メッセージ全体から方向データを検索
        if (rawMessage.Contains("direction") || rawMessage.Contains("\"type\":\"direction\""))
        {
            Debug.Log($"【方向データ受信】メッセージ全体から方向データを検索");
            // JSON文字列から方向データを抽出
            int directionIndex = rawMessage.IndexOf("\"direction\"");
            if (directionIndex > 0)
            {
                Debug.Log($"【方向データ受信】directionフィールドが見つかりました（位置: {directionIndex}）");
                // 周辺のJSONを抽出して処理
                TryHandleDirectionPayload(rawMessage);
            }
        }
        
        Debug.LogWarning($"【方向データ受信】データ構造を認識できませんでした。メッセージ全体: {rawMessage}");
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
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }
        
        try
        {
            Debug.Log($"【方向データ解析】ペイロードを解析中: {payload}");
            
            JToken token = null;
            
            // まず、JSONとしてパースを試みる
            try
            {
                token = JToken.Parse(payload);
            }
            catch
            {
                // パースに失敗した場合、エスケープされた文字列の可能性がある
                // エスケープを解除して再試行
                string unescaped = payload.Replace("\\\"", "\"").Replace("\\\\", "\\");
                try
                {
                    token = JToken.Parse(unescaped);
                    Debug.Log($"【方向データ解析】エスケープ解除後にパース成功");
                }
                catch
                {
                    Debug.LogWarning($"【方向データ解析】JSONパースに失敗: {payload}");
                    return false;
                }
            }

            // 文字列の場合は、さらにパース
            if (token.Type == JTokenType.String)
            {
                string inner = token.Value<string>();
                if (string.IsNullOrWhiteSpace(inner))
                {
                    return false;
                }
                try
                {
                    token = JToken.Parse(inner);
                    Debug.Log($"【方向データ解析】ネストされたJSON文字列をパース");
                }
                catch
                {
                    // エスケープされた文字列の可能性
                    string unescapedInner = inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
                    try
                    {
                        token = JToken.Parse(unescapedInner);
                    }
                    catch
                    {
                        Debug.LogWarning($"【方向データ解析】ネストされたJSONのパースに失敗");
                        return false;
                    }
                }
            }

            if (token.Type != JTokenType.Object)
            {
                Debug.LogWarning($"【方向データ解析】トークンがオブジェクトではありません: {token.Type}");
                return false;
            }

            var obj = (JObject)token;
            Debug.Log($"【方向データ解析】オブジェクトを取得: {obj.ToString()}");

            // 方向データの形式を確認
            string type = obj["type"]?.ToString();
            Debug.Log($"【方向データ解析】typeフィールド: {type}");
            
            // typeフィールドが"direction"でない場合でも、directionフィールドがあれば処理を続行
            if (type != "direction" && type != null && !string.IsNullOrEmpty(type))
            {
                Debug.LogWarning($"【方向データ解析】typeが'direction'ではありません: {type}。directionフィールドを確認します...");
            }
            
            // directionフィールドの存在を確認（必須）
            if (obj["direction"] == null)
            {
                Debug.LogWarning($"【方向データ解析】directionフィールドが見つかりません。type: {type}");
                return false;
            }

            string direction = obj["direction"]?.ToString();
            if (string.IsNullOrEmpty(direction))
            {
                Debug.LogWarning($"【方向データ解析】directionフィールドの値が空です");
                return false;
            }
            
            Debug.Log($"【方向データ解析】direction値: {direction}");

            // 有効な方向かチェック
            if (direction != "up" && direction != "down" && direction != "left" && direction != "right")
            {
                Debug.LogWarning($"【方向データ解析】無効な方向: {direction}");
                return false;
            }

            string timestamp = obj["timestamp"]?.ToString();
            directionData = new DirectionData(direction, timestamp);
            Debug.Log($"【方向データ解析】✅ 方向データを正常に解析: {directionData}");
            return true;
        }
        catch (JsonReaderException ex)
        {
            Debug.LogError($"【方向データ解析】JSON読み取りエラー: {ex.Message}");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"【方向データ解析】例外が発生しました: {e.Message}\n{e.StackTrace}");
            return false;
        }
    }
}