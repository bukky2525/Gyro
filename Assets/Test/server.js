// server.js
// Node.js WebSocket中継サーバー
// Unityとスマートフォン間でジャイロデータを中継します

const WebSocket = require('ws');

const PORT = 8080;
const wss = new WebSocket.Server({ port: PORT });

console.log(`=== WebSocket中継サーバーが起動しました ===`);
console.log(`ポート: ${PORT}`);
console.log(`接続URL: ws://localhost:${PORT}`);
console.log(`スマホから接続: ws://YOUR_IP:${PORT}`);
console.log(`=========================================`);

let unityClient = null; // Unityクライアントを識別するための変数
let webClients = new Set(); // Webクライアント（スマホ）のセット

wss.on('connection', (ws, req) => {
    const clientIp = req.socket.remoteAddress;
    console.log(`クライアントが接続しました: ${clientIp}`);

    ws.on('message', (message) => {
        const data = message.toString();

        // Unityクライアントの初期化メッセージを確認
        if (data.includes("UNITY_INIT")) {
            // Unityクライアントを登録
            unityClient = ws;
            console.log(`Unity Clientが登録されました: ${clientIp}`);
            
            // 接続確認メッセージを送信
            ws.send(JSON.stringify({
                type: "connected",
                message: "Unity Client registered successfully"
            }));
            return;
        }

        // JSONデータをパース
        try {
            const jsonData = JSON.parse(data);
            
            // ジャイロデータの場合（alpha, beta, gammaが含まれている）
            if (jsonData.alpha !== undefined || jsonData.beta !== undefined || jsonData.gamma !== undefined) {
                console.log(`Webからジャイロデータを受信: alpha=${jsonData.alpha?.toFixed(2)}, beta=${jsonData.beta?.toFixed(2)}, gamma=${jsonData.gamma?.toFixed(2)}`);
                
                // Webクライアントとして登録（まだ登録されていない場合）
                if (!webClients.has(ws)) {
                    webClients.add(ws);
                    console.log(`Web Clientが登録されました: ${clientIp}`);
                }
                
                // Unityクライアントが接続されていれば、そのままデータを転送
                if (unityClient && unityClient.readyState === WebSocket.OPEN) {
                    unityClient.send(data);
                    console.log(`ジャイロデータをUnityに転送しました`);
                } else {
                    console.log(`Unity Clientが接続されていないため、データを転送できません`);
                }
            } else {
                console.log(`受信データ: ${data}`);
            }
        } catch (e) {
            // JSONパースエラー（テキストメッセージなど）
            console.log(`テキストメッセージを受信: ${data}`);
        }
    });

    ws.on('close', () => {
        console.log(`クライアントが切断しました: ${clientIp}`);
        
        // Unityクライアントが切断された場合
        if (ws === unityClient) {
            unityClient = null;
            console.log('Unity Clientが切断されました');
        }
        
        // Webクライアントが切断された場合
        if (webClients.has(ws)) {
            webClients.delete(ws);
            console.log('Web Clientが切断されました');
        }
    });

    ws.on('error', (error) => {
        console.error(`WebSocketエラー: ${error.message}`);
    });
});

// サーバー起動時のメッセージ
wss.on('listening', () => {
    console.log(`WebSocketサーバーはポート ${PORT} でリッスンしています`);
    console.log(`Unity側: ws://localhost:${PORT} で接続`);
    console.log(`スマホ側: ws://YOUR_LOCAL_IP:${PORT} で接続`);
    console.log(`（YOUR_LOCAL_IPは、同じWi-Fiネットワーク上のPCのIPアドレスです）`);
});

// エラーハンドリング
wss.on('error', (error) => {
    console.error(`サーバーエラー: ${error.message}`);
});

// グレースフルシャットダウン
process.on('SIGINT', () => {
    console.log('\nサーバーをシャットダウンしています...');
    wss.close(() => {
        console.log('WebSocketサーバーが停止しました');
        process.exit(0);
    });
});

