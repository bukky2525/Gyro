# Firebase Realtime Database ジャイロコントローラーシステム

Unityとスマートフォン間でジャイロデータを低遅延でやり取りするためのFirebase Realtime Databaseシステムです。

## 📋 システム構成

```
スマートフォン (Web) → Firebase Realtime Database → Unity (C#)
```

1. **スマートフォン (Web)**: ジャイロデータを取得し、Firebase RTDBに送信
2. **Firebase Realtime Database**: データをリアルタイムで保存・同期
3. **Unity (C#)**: Firebase RTDBからデータを受信してゲーム操作に反映

## ✨ 特徴

- **サーバーコード不要**: Firebase RTDBを使用するため、サーバーコードを書く必要がありません
- **リアルタイム同期**: データがリアルタイムで自動的に同期されます
- **低遅延**: Firebase RTDBは低遅延でデータを同期します
- **認証不要**: テストモードでは認証なしで使用できます（本番環境ではセキュリティルールを設定してください）

## 🚀 セットアップ

### 1. Firebaseプロジェクトの準備

#### Step 1-1: プロジェクトの作成

1. [Firebaseコンソール](https://console.firebase.google.com/)にアクセス
2. Googleアカウントでサインイン
3. 「プロジェクトを作成」をクリック
4. プロジェクト名に `Gyro` と入力
5. 手順に従ってプロジェクトを作成

#### Step 1-2: Realtime Databaseの有効化

1. プロジェクトのダッシュボード左側メニューから「Realtime Database」を選択
2. 「データベースを作成」をクリック
3. セキュリティルールの選択画面で「テストモードで開始」を選択
4. 有効にする

⚠️ **注意**: テストモードでは誰でもデータの読み書きが可能になります。本番環境で利用する場合は、必ずセキュリティルールを変更してください。

#### Step 1-3: セキュリティルールの設定

1. データベースが作成されたら、上部のタブから「ルール」を選択
2. 以下のようになっているか確認:

```json
{
  "rules": {
    ".read": "true",
    ".write": "true"
  }
}
```

3. **「公開」**ボタンを押して、設定を反映

#### Step 1-4: Webアプリの設定情報取得

1. プロジェクトのダッシュボードに戻る
2. 「プロジェクトの設定」（左上の歯車アイコン）をクリック
3. 下部にある「アプリの追加」セクションで `< />` (ウェブ) アイコンをクリック
4. アプリ名を入力し、「アプリを登録」を押す
5. 表示された`firebaseConfig`を控えておく

**現在の設定情報**（既に設定済み）:

```javascript
const firebaseConfig = {
    apiKey: "AIzaSyDDbQ1fClyVJgXeYlRPr9KVmpN8Z0L-ceY",
    authDomain: "gyro-67262.firebaseapp.com",
    databaseURL: "https://gyro-67262-default-rtdb.asia-southeast1.firebasedatabase.app",
    projectId: "gyro-67262",
    storageBucket: "gyro-67262.firebasestorage.app",
    messagingSenderId: "334654487209",
    appId: "1:334654487209:web:6bdbb65a93f107db1bc7b8",
    measurementId: "G-CXFVW3T368"
};
```

### 2. Webサイト側のセットアップ

1. `Web/index.html`を開く
2. ブラウザで開く（ローカルサーバーまたはGitHub Pages）
3. 「ジャイロを有効化」ボタンをクリック
4. ジャイロセンサーの許可を与える
5. 自動的にジャイロデータがFirebase RTDBに送信されます

### 3. Unity側のセットアップ

#### Step 3-1: Firebase SDK for Unityの導入

1. **Unityのバージョン確認**: Unity 2018.4 以降を使用していることを確認

2. **Firebase SDK for Unityのダウンロード**:
   - [Firebase SDK for Unityのダウンロードページ](https://firebase.google.com/download/unity)にアクセス
   - Realtime Database SDKを含む`.unitypackage`をダウンロード

3. **Unityプロジェクトへのインポート**:
   - Unityプロジェクトを開く
   - ダウンロードした`.unitypackage`ファイルをUnityエディターにドラッグ＆ドロップ
   - インポートダイアログで「Import」をクリック

4. **Firebase設定ファイルの追加**（オプション）:
   - プロジェクトの設定画面から「アプリの追加」でUnityアイコンを選択
   - Unityアプリのパッケージ名（例：`com.yourcompany.gyro`）を入力
   - 設定ファイルをダウンロード
   - ダウンロードした`google-services.json`（Android/Editor用）および`GoogleService-Info.plist`（iOS用）を`Assets`フォルダ直下に配置

#### Step 3-2: C#スクリプトの設定

1. `Assets/Test/Test.cs`を開く
2. Firebase SDKのコメントアウトを解除:

```csharp
// 以下のusingを追加
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

// InitializeFirebase()メソッド内のコメントアウトを解除
// StartListeningForData()メソッド内のコメントアウトを解除
// HandleValueChanged()メソッド内のコメントアウトを解除
// OnDestroy()メソッド内のコメントアウトを解除
// MockGyroData()コルーチンを削除
```

3. UnityMainThreadDispatcherの追加（オプション）:
   - Firebase SDKからのデータ受信はバックグラウンドスレッドで行われます
   - UnityのGameObject操作はメインスレッドで実行する必要があります
   - [UnityMainThreadDispatcher](https://github.com/PimDeWitte/UnityMainThreadDispatcher)を導入することを推奨します

#### Step 3-3: シーンの設定

1. シーンに`TargetCube`という名前のGameObjectを配置（または任意の名前のGameObject）
2. 空のGameObject（例：`GyroManager`）を作成
3. `GyroManager`に`Test.cs`をアタッチ
4. Inspectorで`Target Object`を設定（またはシーンに`TargetCube`という名前のGameObjectを配置）
5. 再生ボタンをクリックして動作確認

## 📱 使い方

### Webサイト側

1. ブラウザで`Web/index.html`を開く
2. 「ジャイロを有効化」ボタンをクリック
3. ジャイロセンサーの許可を与える
4. 自動的にジャイロデータがFirebase RTDBに送信されます

### Unity側

1. Unityでゲームを実行
2. Firebase RTDBからデータを受信
3. ジャイロデータをゲーム操作に反映

## 🔧 設定

### データベースパスの変更

デフォルトのパスは`gyro/data`です。変更する場合:

- **Web側**: `Web/index.html`の`gyroRef`を変更
- **Unity側**: Inspectorで`Database Path`を変更

### 送信頻度の変更

`Web/index.html`の`SEND_INTERVAL`を変更:

```javascript
const SEND_INTERVAL = 33; // ミリ秒（デフォルト: 33ms = 30fps）
```

### 回転速度の変更

Unity側のInspectorで`Rotation Speed`を変更（デフォルト: 10）

## 🐛 トラブルシューティング

### Firebase SDKが動作しない場合

1. **依存関係の確認**:
   - Firebase SDKが正しくインポートされているか確認
   - 必要な依存関係がインストールされているか確認

2. **エラーメッセージの確認**:
   - Unityコンソールでエラーメッセージを確認
   - Firebaseコンソールでデータベースの状態を確認

### ジャイロデータが送信されない場合

1. **ブラウザの許可確認**:
   - ジャイロセンサーへのアクセス許可を確認
   - iOSの場合は設定から許可が必要な場合があります

2. **HTTPS接続**:
   - 一部のブラウザでは、HTTPS接続が必要な場合があります
   - ローカル開発の場合は`localhost`を使用

3. **Firebase設定の確認**:
   - `firebaseConfig`が正しく設定されているか確認
   - データベースURLが正しいか確認

### Unity側でデータが受信されない場合

1. **Firebase SDKの確認**:
   - Firebase SDKが正しくインポートされているか確認
   - コメントアウトが解除されているか確認

2. **データベースパスの確認**:
   - Web側とUnity側で同じパスを使用しているか確認
   - Firebaseコンソールでデータが保存されているか確認

3. **メインスレッド処理**:
   - UnityMainThreadDispatcherが正しく設定されているか確認
   - バックグラウンドスレッドからのUnity操作を避ける

## 📝 ファイル構成

```
.
├── README.md              # このファイル
├── Assets/
│   └── Test/
│       ├── Test.cs        # Unity Firebase RTDBクライアント
│       └── Web/
│           └── index.html # スマートフォン用Webクライアント
```

## 🔒 セキュリティ

⚠️ **重要**: テストモードでは誰でもデータの読み書きが可能になります。本番環境で利用する場合は、必ずセキュリティルールを変更してください。

### セキュリティルールの例

```json
{
  "rules": {
    "gyro": {
      "data": {
        ".read": true,
        ".write": true
      }
    }
  }
}
```

より安全な設定:

```json
{
  "rules": {
    "gyro": {
      "data": {
        ".read": "auth != null",
        ".write": "auth != null"
      }
    }
  }
}
```

## 📄 ライセンス

MIT License

## 🔗 参考リンク

- [Firebase Realtime Database公式ドキュメント](https://firebase.google.com/docs/database)
- [Firebase SDK for Unity公式ドキュメント](https://firebase.google.com/docs/unity/setup)
- [UnityMainThreadDispatcher](https://github.com/PimDeWitte/UnityMainThreadDispatcher)

