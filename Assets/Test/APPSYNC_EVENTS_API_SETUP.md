# AWS AppSync Events API 設定ガイド

## 1. AppSyncコンソールで設定情報を確認

### 必要な情報

1. **API ID** または **DNSエンドポイント**
   - AppSyncコンソール → 作成したEvents API → 「設定」タブ
   - 「DNSエンドポイント - HTTP」を確認
   - 例: `xxxxxxxxxxxx.appsync-api.ap-northeast-1.amazonaws.com`
   
2. **Realtime DNSエンドポイント**
   - AppSyncコンソール → 作成したEvents API → 「設定」タブ
   - 「DNSエンドポイント - リアルタイム」を確認
   - 例: `xxxxxxxxxxxx.appsync-realtime-api.ap-northeast-1.amazonaws.com`

3. **API Key**
   - AppSyncコンソール → 作成したEvents API → 「設定」タブ
   - 「認可モード - APIキー」の値を確認
   - 例: `da2-xxxxxxxxxxxxxxxxxxxxx`

4. **リージョン**
   - 例: `ap-northeast-1`, `us-east-1` など

5. **名前空間（Namespace）**
   - デフォルト: `default`
   - カスタム名前空間を使用する場合はその値
   - **重要**: チャンネル名は事前に作成する必要はありません。送信時に指定する任意の文字列です。

6. **チャンネル名**
   - **チャンネル名は確認するものではなく、自分で指定します**
   - デフォルト名前空間を使用する場合、任意のチャンネル名を指定できます
   - 例: `gyro`, `dpad`, `test`, `game-control` など
   - 完全なチャンネル名の形式: `{名前空間}/{チャンネル名}`
   - 例: `default/gyro`, `default/dpad` など
   - **Unity側とスマホ側で同じチャンネル名を使用する必要があります**

## 2. Unity側の設定（Test.cs）

Unityエディタで、`Test.cs`スクリプトがアタッチされているGameObjectを選択し、Inspectorで以下を設定：

- **Http Domain**: HTTP DNSエンドポイント（例: `xxxxxxxxxxxx.appsync-api.ap-northeast-1.amazonaws.com`）
- **Realtime Domain**: Realtime DNSエンドポイント（例: `xxxxxxxxxxxx.appsync-realtime-api.ap-northeast-1.amazonaws.com`）
- **Api Key**: APIキー（例: `da2-xxxxxxxxxxxxxxxxxxxxx`）
- **Channel**: チャンネル名（例: `default/gyro` または `gyro`）

### チャンネル名の形式
- 名前空間を含める場合: `default/gyro`
- 名前空間を含めない場合: `gyro`（デフォルト名前空間が使用されます）

## 3. スマホ側の設定（index.html）

`index.html`の以下の部分を更新：

```javascript
const APP_SYNC_API_ID = "xxxxxxxxxxxx"; // API ID（DNSエンドポイントから取得）
const APP_SYNC_REGION = "ap-northeast-1"; // リージョン
const APP_SYNC_EVENTS_URL = `https://${APP_SYNC_API_ID}.appsync-api.${APP_SYNC_REGION}.amazonaws.com/event`;
const API_KEY = "da2-xxxxxxxxxxxxxxxxxxxxx"; // APIキー
const NAMESPACE = "default"; // 名前空間
const CHANNEL_NAME = "gyro"; // チャンネル名
const SUBSCRIPTION_CHANNEL_NAME = `${NAMESPACE}/${CHANNEL_NAME}`; // 完全なチャンネル名
```

## 4. AppSync Events APIの送信方法

### スマホ側からの送信（HTTP POST）

Events APIでは、GraphQL mutationではなく、HTTP POSTリクエストを使用します。

#### ジャイロデータの送信例
```javascript
const url = `https://${APP_SYNC_API_ID}.appsync-api.${APP_SYNC_REGION}.amazonaws.com/event/${NAMESPACE}/${CHANNEL_NAME}`;
const payload = {
    type: "gyro",
    alpha: 123.45,
    beta: 67.89,
    gamma: 10.11,
    timestamp: new Date().toISOString()
};

const response = await fetch(url, {
    method: 'POST',
    headers: {
        'Content-Type': 'application/json',
        'x-api-key': API_KEY
    },
    body: JSON.stringify(payload)
});
```

#### 方向データの送信例
```javascript
const url = `https://${APP_SYNC_API_ID}.appsync-api.${APP_SYNC_REGION}.amazonaws.com/event/${NAMESPACE}/${CHANNEL_NAME}`;
const payload = {
    type: "direction",
    direction: "up", // "up", "down", "left", "right"
    timestamp: new Date().toISOString()
};

const response = await fetch(url, {
    method: 'POST',
    headers: {
        'Content-Type': 'application/json',
        'x-api-key': API_KEY
    },
    body: JSON.stringify(payload)
});
```

### Unity側からの受信（WebSocket）

Unity側は既にWebSocketで接続しているため、追加の設定は不要です。
チャンネル名が一致していれば、自動的にデータを受信します。

#### データ構造

Events APIから受信するデータは、以下のような構造になります：

```json
{
    "type": "data",
    "id": "...",
    "data": {
        "data": {
            "subscribe": {
                "data": "{\"type\":\"direction\",\"direction\":\"up\",\"timestamp\":\"...\"}",
                "name": "default/gyro"
            }
        }
    }
}
```

または、イベントベースの形式：

```json
{
    "type": "data",
    "event": "{\"type\":\"direction\",\"direction\":\"up\",\"timestamp\":\"...\"}"
}
```

Unity側の`Test.cs`は、これらの両方の形式に対応しています。

## 5. 新しいEvents APIの設定値の確認と入力

### ステップ1: AppSyncコンソールで情報を確認

1. AWS Management Consoleにサインイン
2. AppSyncコンソールを開く
3. 作成したEvents APIを選択
4. 「設定」タブを開く
5. 以下の情報を確認：
   - **DNSエンドポイント - HTTP**: 例: `xxxxxxxxxxxx.appsync-api.ap-northeast-1.amazonaws.com`
   - **DNSエンドポイント - リアルタイム**: 例: `xxxxxxxxxxxx.appsync-realtime-api.ap-northeast-1.amazonaws.com`
   - **認可モード - APIキー**: 例: `da2-xxxxxxxxxxxxxxxxxxxxx`

### チャンネル名について

**チャンネル名は確認するものではなく、自分で指定します。**

- Events APIでは、チャンネル名は事前に作成する必要はありません
- デフォルト名前空間（`default`）を使用する場合、任意のチャンネル名を指定できます
- 例: `gyro`, `dpad`, `test`, `game-control` など
- 完全なチャンネル名の形式: `{名前空間}/{チャンネル名}`
  - 例: `default/gyro`, `default/dpad` など
- **重要**: Unity側とスマホ側で**同じチャンネル名**を使用する必要があります
  - Unity側: `default/gyro`
  - スマホ側: `default/gyro`

### ステップ2: Unity側の設定を更新

1. Unityエディタで、`Test.cs`スクリプトがアタッチされているGameObjectを選択
2. Inspectorで以下の値を設定：
   - **Http Domain**: DNSエンドポイント - HTTP（ドメイン部分のみ）
     - 例: `k5fxcxsvbbathdhik52l5hegrq.appsync-api.ap-northeast-1.amazonaws.com`
   - **Realtime Domain**: DNSエンドポイント - リアルタイム（ドメイン部分のみ）
     - 例: `k5fxcxsvbbathdhik52l5hegrq.appsync-realtime-api.ap-northeast-1.amazonaws.com`
   - **Api Key**: APIキー
     - 例: `da2-jhqtdoi3qjdrph6atduxm254wq`
   - **Channel**: チャンネル名（名前空間を含む）
     - 例: `default/gyro`（ジャイロデータ用）
     - 例: `default/dpad`（方向データ用）
     - **注意**: Unity側とスマホ側で同じチャンネル名を使用してください

### ステップ3: スマホ側の設定を更新

`index.html`の以下の部分を更新：

```javascript
// 実際の設定例（提供された値を使用）
const APP_SYNC_API_ID = "k5fxcxsvbbathdhik52l5hegrq"; // DNSエンドポイントから取得
const APP_SYNC_REGION = "ap-northeast-1"; // リージョン
const APP_SYNC_EVENTS_URL = `https://${APP_SYNC_API_ID}.appsync-api.${APP_SYNC_REGION}.amazonaws.com/event`;
const API_KEY = "da2-jhqtdoi3qjdrph6atduxm254wq"; // APIキー
const NAMESPACE = "default"; // 名前空間（デフォルトは"default"）
const CHANNEL_NAME = "gyro"; // チャンネル名（任意の文字列、Unity側と一致させる）
const SUBSCRIPTION_CHANNEL_NAME = `${NAMESPACE}/${CHANNEL_NAME}`; // 完全なチャンネル名（例: "default/gyro"）
```

**注意**: 
- `CHANNEL_NAME`は任意の文字列を指定できます（例: `"gyro"`, `"dpad"`, `"test"`など）
- Unity側の`Channel`設定と一致させる必要があります
  - スマホ側: `CHANNEL_NAME = "gyro"` → 完全なチャンネル名: `"default/gyro"`
  - Unity側: `Channel = "default/gyro"`

## 6. 設定の確認方法

1. Unity側のコンソールログで「AppSyncへの接続が確立されました」が表示されることを確認
2. 「チャンネルをsubscribeしました」が表示されることを確認
3. スマホ側からデータを送信
4. Unity側のコンソールログで「【方向データ受信】」が表示されることを確認
5. スマホ側のデバッグログで「Gyro Publish HTTP status=200」または「Direction Publish HTTP status=200」が表示されることを確認

## 7. トラブルシューティング

### 404エラーが発生する場合
- Events APIのHTTPエンドポイントが正しいか確認
  - エンドポイント形式: `https://{API_ID}.appsync-api.{REGION}.amazonaws.com/event/{NAMESPACE}/{CHANNEL}`
  - 例: `https://xxxxxxxxxxxx.appsync-api.ap-northeast-1.amazonaws.com/event/default/gyro`
- チャンネル名の形式が正しいか確認（`default/gyro` など）
- APIキーが有効か確認
- 名前空間が存在するか確認（デフォルトは`default`）

### 接続できない場合
- Realtime DNSエンドポイントが正しいか確認
  - エンドポイント形式: `wss://{REALTIME_DOMAIN}/event/realtime`
  - 例: `wss://xxxxxxxxxxxx.appsync-realtime-api.ap-northeast-1.amazonaws.com/event/realtime`
- APIキーが正しいか確認
- 名前空間とチャンネル名が一致しているか確認
- Unity側とスマホ側の設定が一致しているか確認

### データが受信できない場合
- Unity側とスマホ側のチャンネル名が一致しているか確認
  - Unity側: `default/gyro`
  - スマホ側: `default/gyro`
- 名前空間を含む完全なチャンネル名を使用しているか確認
- Unity側のコンソールログで「【方向データ受信】」が表示されるか確認
- スマホ側のデバッグログで「Gyro Publish HTTP status=200」が表示されるか確認

### よくある間違い
1. **DNSエンドポイントの形式を間違える**
   - ❌ 間違い: `https://xxxxxxxxxxxx.appsync-api.ap-northeast-1.amazonaws.com/graphql`
   - ✅ 正しい: `https://xxxxxxxxxxxx.appsync-api.ap-northeast-1.amazonaws.com/event/default/gyro`

2. **チャンネル名の形式を間違える**
   - ❌ 間違い: `gyro`（名前空間がない）
   - ✅ 正しい: `default/gyro`（名前空間を含む）

3. **APIキーが間違っている**
   - APIキーは `da2-` で始まる文字列
   - コピー&ペースト時にスペースや改行が含まれていないか確認

