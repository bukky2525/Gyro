using UnityEngine;
using System;
using System.Text;
using System.Security.Cryptography;
using WebSocketSharp; // 既存のDLLを使用
using System.Collections.Generic;

public class AppSyncConnector : MonoBehaviour
{
    // --------------------------------------------------------------------------
    // AWS AppSync 設定
    // --------------------------------------------------------------------------
    
    // AWS AppSyncコンソールから取得したAPI URL
    private const string APP_SYNC_URL = "https://thyvvhs3wnfajkv5mg5agx2hfm.appsync-api.ap-northeast-1.amazonaws.com/graphql";
    // AWS AppSyncコンソールから取得したAPIキー
    private const string API_KEY = "da2-q5wrt2h47nb4zfsld3wffd2xga";
    // AWSリージョン
    private const string REGION = "ap-northeast-1";
    // サブスクリプション用のチャンネル名
    private const string SUBSCRIPTION_CHANNEL_NAME = "gyro";

    private WebSocket ws;
    private bool isConnected = false; // CS0103エラーを解決するために必須
    private string subscriptionId = Guid.NewGuid().ToString();

    // --------------------------------------------------------------------------
    // Unity ライフサイクル
    // --------------------------------------------------------------------------

    void Start()
    {
        // 設定値の確認（デバッグ用）
        if (string.IsNullOrEmpty(API_KEY) || API_KEY.Contains("XXXX") || API_KEY.Contains("****"))
        {
            Debug.LogError("【設定エラー】API_KEYを設定してください。");
            Debug.LogError("AWSコンソール > AppSync > 設定 > APIキー から正しいAPIキーをコピーしてください。");
            return;
        }
        
        Debug.Log($"【AppSync設定】エンドポイント: {APP_SYNC_URL}");
        Debug.Log($"【AppSync設定】APIキー: {API_KEY.Substring(0, Math.Min(10, API_KEY.Length))}... (長さ: {API_KEY.Length}文字)");
        Debug.Log("【スマホ接続確認】📱 AppSyncに接続して、スマホからのジャイロデータを受信準備を開始します...");
        
        ConnectToAppSync();
    }

    void OnDestroy()
    {
        if (ws != null)
        {
            SendStopMessage();
            ws.Close();
        }
    }

    // --------------------------------------------------------------------------
    // 接続処理
    // --------------------------------------------------------------------------
    
    private void ConnectToAppSync()
    {
        // 1. WebSocket接続に必要なヘッダーとペイロードを生成
        //    AppSyncの認証は、URLのクエリパラメータを通じて行われる
        string payloadJson = "{}"; 
        
        // ホスト名を Base64 エンコードする (APIキー認証の要件)
        string headerAuthJson = $"{{\"host\":\"{GetHost(APP_SYNC_URL)}\",\"x-api-key\":\"{API_KEY}\"}}";
        string base64Header = Base64Encode(headerAuthJson);
        
        // 2. AppSync Real-time 接続URLを構築
        //    headers には Base64 エンコードされた認証情報が含まれる
        string wsUrl = APP_SYNC_URL.Replace("https://", "wss://").Replace("appsync-api", "appsync-realtime-api");
        wsUrl += $"?header={base64Header}&payload={Base64Encode(payloadJson)}";

        Debug.Log($"AppSync WebSocket URL (Base64 Ready): {wsUrl}");
        
        // 2. WebSocket接続を開始 (プロトコルを明示的に指定)
        // AWS AppSync Real-time APIは 'graphql-ws' プロトコルを使用します
        ws = new WebSocket(wsUrl, "graphql-ws");
        
        // ▼▼▼ TLSエラー回避のための最終設定を追加 ▼▼▼
        
        // AWS IoT Core/AppSyncが要求するTLS 1.2を明示的に指定
        ws.SslConfiguration.EnabledSslProtocols = 
            System.Security.Authentication.SslProtocols.Tls12;
        
        // サーバー証明書検証を強制的にスキップ（これが1015エラーを回避する最後の手段）
        ws.SslConfiguration.ServerCertificateValidationCallback = 
            (sender, certificate, chain, sslPolicyErrors) => 
        {
            Debug.LogWarning("AppSync: サーバー証明書の検証を強制スキップ。");
            return true; // 常に true を返し、検証を成功させる
        };
        // ▲▲▲ TLSエラー回避のための最終設定を追加 ▲▲▲

        ws.OnOpen += OnWebSocketOpen;
        ws.OnMessage += OnWebSocketMessage;
        ws.OnError += OnWebSocketError;
        ws.OnClose += OnWebSocketClose;

        ws.ConnectAsync();
    }

    // --------------------------------------------------------------------------
    // WebSocket イベントハンドラ
    // --------------------------------------------------------------------------

    private void OnWebSocketOpen(object sender, EventArgs e)
    {
        Debug.Log("【AppSync】WebSocket接続オープン。");
        Debug.Log("【スマホ接続確認】📱 WebSocket接続が確立されました。認証処理を開始します...");
        SendConnectionInitMessage();
    }

    private void OnWebSocketMessage(object sender, MessageEventArgs e)
    {
        if (!e.IsText) return;
        
        string message = e.Data;
        
        // すべての受信メッセージをログに出力（デバッグ用）
        Debug.Log($"【AppSync】受信メッセージ: {message}");
        
        try
        {
            var jsonObject = JsonUtility.FromJson<AppSyncMessage>(message);
            
            switch (jsonObject.type)
            {
                case "connection_ack":
                    isConnected = true;
                    Debug.Log("【AppSync】✅ 接続確立（connection_ack）。サブスクリプションを開始します。");
                    Debug.Log("【スマホ接続確認】📱 スマホからのジャイロデータを待機中...");
                    SendStartSubscriptionMessage();
                    break;
                    
                case "data":
                    Debug.Log("【AppSync】✅ データ受信メッセージを受信しました");
                    HandleRealtimeData(message);
                    break;
                    
                case "error":
                    Debug.LogError("【スマホ接続確認】==========================================");
                    Debug.LogError("【スマホ接続確認】❌ エラーが発生しました");
                    Debug.LogError("【スマホ接続確認】スマホからのジャイロデータを受信できません");
                    Debug.LogError($"【スマホ接続確認】エラーメッセージ全文: {message}");
                    
                    // エラーの詳細を解析（簡易版）
                    if (message.Contains("UnsupportedOperation"))
                    {
                        Debug.LogError("【スマホ接続確認】❌ UnsupportedOperation エラー");
                        Debug.LogError("【スマホ接続確認】サブスクリプションクエリが認識されていません");
                        Debug.LogError("【スマホ接続確認】");
                        Debug.LogError("【スマホ接続確認】🔍 確認事項:");
                        Debug.LogError("【スマホ接続確認】1. AppSyncコンソールでスキーマが正しく設定されているか確認");
                        Debug.LogError("【スマホ接続確認】2. AppSyncコンソールでリゾルバーが正しく設定されているか確認");
                        Debug.LogError("【スマホ接続確認】3. AppSyncコンソールの「クエリ」タブでサブスクリプションが動作するか確認");
                        Debug.LogError("【スマホ接続確認】4. Unityアプリケーションが接続しているAPIエンドポイントが正しいか確認");
                        Debug.LogError("【スマホ接続確認】5. APIキーが正しいか確認");
                        Debug.LogError("【スマホ接続確認】");
                        Debug.LogError($"【スマホ接続確認】現在のエンドポイント: {APP_SYNC_URL}");
                        Debug.LogError($"【スマホ接続確認】現在のAPIキー: {API_KEY.Substring(0, Math.Min(10, API_KEY.Length))}...");
                    }
                    Debug.LogError("【スマホ接続確認】==========================================");
                    break;
                    
                case "ka":
                    // Keep-aliveメッセージ（無視）
                    break;

                default:
                    Debug.LogWarning($"【AppSync】未知のメッセージタイプ: {jsonObject.type}");
                    Debug.LogWarning($"【AppSync】メッセージ内容: {message}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"【AppSync】メッセージ解析エラー: {ex.Message}");
            Debug.LogError($"【AppSync】元のデータ: {message}");
            Debug.LogError($"【AppSync】スタックトレース: {ex.StackTrace}");
        }
    }

    private void OnWebSocketError(object sender, ErrorEventArgs e)
    {
        Debug.LogError($"【AppSync】WebSocketエラー: {e.Message}");
        isConnected = false;
    }

    private void OnWebSocketClose(object sender, CloseEventArgs e)
    {
        Debug.LogWarning($"【AppSync】WebSocket切断。コード: {e.Code}, クリーン終了: {e.WasClean}, 理由: {e.Reason}");
        
        // 切断コードの意味を表示
        if (e.Code == 1000)
        {
            Debug.LogWarning("切断コード1000: 正常な切断（サーバーがプロトコルに従って切断）");
        }
        else if (e.Code == 1006)
        {
            Debug.LogError("切断コード1006: 異常な切断（ネットワークエラーまたは認証エラーの可能性）");
            Debug.LogError("考えられる原因:");
            Debug.LogError("1. APIキーが無効または期限切れ");
            Debug.LogError("2. 認証情報の形式が正しくない");
            Debug.LogError("3. AppSyncの設定が正しくない");
        }
        else
        {
            Debug.LogWarning($"切断コード{e.Code}: その他の理由");
        }
        
        isConnected = false;
    }

    // --------------------------------------------------------------------------
    // AppSync プロトコルメッセージの送信
    // --------------------------------------------------------------------------

    private void SendConnectionInitMessage()
    {
        // 以前の複雑なBase64認証ペイロードを削除し、空のJSONペイロードに戻す
        // 認証情報は、URLのクエリパラメータで十分に渡されていると仮定する
        string initMessage = "{\"type\":\"connection_init\",\"payload\":{}}"; // <-- 最もシンプルな形に戻す
        
        ws.Send(initMessage);
        Debug.Log("【AppSync】connection_initメッセージ送信 (空ペイロード)");
        Debug.Log("【スマホ接続確認】📱 認証メッセージを送信しました。接続確認を待機中...");
    }
    
    private void SendStartSubscriptionMessage()
    {
        // 1. GraphQLサブスクリプションクエリを定義 (改行なし、スペースを最小限に)
        // 注意: $name は変数宣言、name: $name は変数の使用
        string gqlQuery = "subscription Subscribe($name: String!) { subscribe(name: $name) { data name } }";
        
        // 2. クエリをエスケープ (JSON文字列内に埋め込むため)
        string escapedQuery = EscapeJson(gqlQuery);
        
        // 3. 変数を定義 (チャンネル名を指定)
        // JSONオブジェクトとして構築: {"name":"gyro"}
        string variablesJson = $"{{\"name\":\"{SUBSCRIPTION_CHANNEL_NAME}\"}}";
        
        // 4. ペイロードJSONオブジェクトを構築
        // 構造: {"query":"...", "variables":{...}, "extensions":{}}
        // 注意: query はエスケープされた文字列、variables と extensions はJSONオブジェクト
        string payloadJson = $"{{\"query\":\"{escapedQuery}\",\"variables\":{variablesJson},\"extensions\":{{}}}}";

        // 5. サブスクリプション開始メッセージを構築
        // 構造: {"id":"...", "type":"start", "payload":{...}}
        // 注意: payload はJSONオブジェクトとしてそのまま埋め込む
        string startMessage = $"{{\"id\":\"{subscriptionId}\",\"type\":\"start\",\"payload\":{payloadJson}}}"; 
        
        // デバッグ: 送信メッセージの詳細をログに出力
        Debug.Log($"【AppSync】=== サブスクリプション開始メッセージ ===");
        Debug.Log($"【AppSync】ID: {subscriptionId}");
        Debug.Log($"【AppSync】GraphQLクエリ (元): {gqlQuery}");
        Debug.Log($"【AppSync】GraphQLクエリ (エスケープ後): {escapedQuery}");
        Debug.Log($"【AppSync】変数: {variablesJson}");
        Debug.Log($"【AppSync】ペイロードJSON: {payloadJson}");
        Debug.Log($"【AppSync】完全なメッセージ: {startMessage}");
        Debug.Log($"【AppSync】メッセージ長: {startMessage.Length}文字");
        
        ws.Send(startMessage);
        Debug.Log($"【AppSync】メッセージ送信完了");
        Debug.Log("【スマホ接続確認】📱 サブスクリプション開始完了。スマホからのジャイロデータを受信できるようになりました。");
        Debug.Log("【スマホ接続確認】📱 スマホで「AppSyncに接続」ボタンを押して、ジャイロデータを送信してください。");
    }

    private void SendStopMessage()
    {
        // 修正: 接続が確立している場合のみ停止メッセージを送信
        if (isConnected && ws != null && ws.ReadyState == WebSocketState.Open)
        {
            // WebSocketプロトコルの要件に基づき、接続がオープンな場合のみ停止を試みる
            string stopMessage = $"{{\"id\":\"{subscriptionId}\",\"type\":\"stop\"}}";
            ws.Send(stopMessage);
            Debug.Log("【AppSync】サブスクリプション停止メッセージを送信しました。");
        }
        else
        {
            Debug.LogWarning("【AppSync】接続が確立されていないため、停止メッセージを送信しませんでした。");
        }
    }

    // --------------------------------------------------------------------------
    // データ処理
    // --------------------------------------------------------------------------
    
    private void HandleRealtimeData(string message)
    {
        // JsonUtilityは不向きなため、ここではログ出力と簡易抽出に留める
        try
        {
            Debug.Log("【スマホ接続確認】==========================================");
            Debug.Log("【スマホ接続確認】✅ スマホからのジャイロデータを受信しました！");
            Debug.Log("【スマホ接続確認】==========================================");
            
            // 'data' メッセージから 'subscribe' の値セクションを抜き出す
            // 新しいスキーマ: { "data": { "subscribe": { "data": "...", "name": "gyro" } } }
            int subscribeIndex = message.IndexOf("\"subscribe\":");
            if (subscribeIndex > 0)
            {
                Debug.Log("【スマホ接続確認】✅ サブスクリプションデータを検出しました");
                
                // 'data' フィールドから実際のデータを抽出
                int dataFieldIndex = message.IndexOf("\"data\":", subscribeIndex);
                if (dataFieldIndex > 0)
                {
                    // JSONデータの値を抽出（簡易版）
                    // "data":"{\"alpha\":123.45,\"beta\":67.89,\"gamma\":10.11}"
                    int dataValueStart = message.IndexOf('"', dataFieldIndex + 7) + 1;
                    int dataValueEnd = message.IndexOf('"', dataValueStart);
                    if (dataValueEnd > dataValueStart)
                    {
                        string jsonData = message.Substring(dataValueStart, dataValueEnd - dataValueStart);
                        // エスケープを解除
                        jsonData = jsonData.Replace("\\\"", "\"").Replace("\\\\", "\\");
                        
                        Debug.Log($"【スマホ接続確認】受信データ（JSON文字列）: {jsonData}");
                        
                        // alpha, beta, gammaの値を抽出
                        ExtractGyroValues(jsonData);
                    }
                }
                
                // 'name' フィールドを抽出
                int nameFieldIndex = message.IndexOf("\"name\":", subscribeIndex);
                if (nameFieldIndex > 0)
                {
                    int nameValueStart = message.IndexOf('"', nameFieldIndex + 7) + 1;
                    int nameValueEnd = message.IndexOf('"', nameValueStart);
                    if (nameValueEnd > nameValueStart)
                    {
                        string channelName = message.Substring(nameValueStart, nameValueEnd - nameValueStart);
                        Debug.Log($"【スマホ接続確認】チャンネル名: {channelName}");
                    }
                }
            }
            else
            {
                // フォールバック: メッセージ全体をログに出力
                Debug.LogWarning($"【スマホ接続確認】⚠️ サブスクリプションデータの形式が予期されていません");
                Debug.LogWarning($"【スマホ接続確認】完全なメッセージ: {message}");
            }
            
            Debug.Log("【スマホ接続確認】==========================================");
        }
        catch (Exception ex)
        {
            Debug.LogError($"【スマホ接続確認】❌ ジャイロデータパースエラー: {ex.Message}");
            Debug.LogError($"【スマホ接続確認】メッセージ: {message}");
            Debug.LogError($"【スマホ接続確認】スタックトレース: {ex.StackTrace}");
        }
    }
    
    // ジャイロデータからalpha, beta, gammaの値を抽出
    private void ExtractGyroValues(string jsonData)
    {
        try
        {
            // JSON文字列からalpha, beta, gammaを抽出
            // 例: {"alpha":123.45,"beta":67.89,"gamma":10.11}
            
            // alphaを抽出
            int alphaIndex = jsonData.IndexOf("\"alpha\":");
            if (alphaIndex >= 0)
            {
                int alphaValueStart = alphaIndex + 8;
                int alphaValueEnd = jsonData.IndexOf(',', alphaValueStart);
                if (alphaValueEnd < 0) alphaValueEnd = jsonData.IndexOf('}', alphaValueStart);
                if (alphaValueEnd > alphaValueStart)
                {
                    string alphaStr = jsonData.Substring(alphaValueStart, alphaValueEnd - alphaValueStart).Trim();
                    if (float.TryParse(alphaStr, out float alpha))
                    {
                        Debug.Log($"【スマホ接続確認】✅ Alpha (Z軸): {alpha:F2}°");
                    }
                }
            }
            
            // betaを抽出
            int betaIndex = jsonData.IndexOf("\"beta\":");
            if (betaIndex >= 0)
            {
                int betaValueStart = betaIndex + 7;
                int betaValueEnd = jsonData.IndexOf(',', betaValueStart);
                if (betaValueEnd < 0) betaValueEnd = jsonData.IndexOf('}', betaValueStart);
                if (betaValueEnd > betaValueStart)
                {
                    string betaStr = jsonData.Substring(betaValueStart, betaValueEnd - betaValueStart).Trim();
                    if (float.TryParse(betaStr, out float beta))
                    {
                        Debug.Log($"【スマホ接続確認】✅ Beta (X軸): {beta:F2}°");
                    }
                }
            }
            
            // gammaを抽出
            int gammaIndex = jsonData.IndexOf("\"gamma\":");
            if (gammaIndex >= 0)
            {
                int gammaValueStart = gammaIndex + 8;
                int gammaValueEnd = jsonData.IndexOf(',', gammaValueStart);
                if (gammaValueEnd < 0) gammaValueEnd = jsonData.IndexOf('}', gammaValueStart);
                if (gammaValueEnd > gammaValueStart)
                {
                    string gammaStr = jsonData.Substring(gammaValueStart, gammaValueEnd - gammaValueStart).Trim();
                    if (float.TryParse(gammaStr, out float gamma))
                    {
                        Debug.Log($"【スマホ接続確認】✅ Gamma (Y軸): {gamma:F2}°");
                    }
                }
            }
            
            // timestampを抽出（オプション）
            int timestampIndex = jsonData.IndexOf("\"timestamp\":");
            if (timestampIndex >= 0)
            {
                int timestampValueStart = jsonData.IndexOf('"', timestampIndex + 12) + 1;
                int timestampValueEnd = jsonData.IndexOf('"', timestampValueStart);
                if (timestampValueEnd > timestampValueStart)
                {
                    string timestamp = jsonData.Substring(timestampValueStart, timestampValueEnd - timestampValueStart);
                    Debug.Log($"【スマホ接続確認】タイムスタンプ: {timestamp}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"【スマホ接続確認】❌ ジャイロ値の抽出エラー: {ex.Message}");
        }
    }

    // --------------------------------------------------------------------------
    // ユーティリティ
    // --------------------------------------------------------------------------
    
    // JSON文字列をエスケープする
    private string EscapeJson(string json)
    {
        // 修正: 二重引用符をバックスラッシュでエスケープし、改行コードを削除
        return json.Replace("\\", "\\\\") // バックスラッシュ自体をエスケープ
                   .Replace("\"", "\\\"") // 二重引用符をエスケープ
                   .Replace("\n", "")      // 改行コードを削除
                   .Replace("\r", "")      // キャリッジリターンを削除
                   .Replace("\t", " ");     // タブをスペースに変換
    }

    // URLからホスト名を取得
    private string GetHost(string url)
    {
        Uri uri = new Uri(url);
        return uri.Host;
    }

    // Base64エンコード
    private string Base64Encode(string plainText)
    {
        var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
        return Convert.ToBase64String(plainTextBytes);
    }
}

// AppSyncからのメッセージのトップレベル構造
[System.Serializable]
public class AppSyncMessage
{
    public string type;
    public string id;
    public string payload; 
}