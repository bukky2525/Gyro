# Firebase Realtime Database セットアップガイド

## 1. Firebaseプロジェクトの作成

### Step 1: プロジェクトの作成

1. [Firebaseコンソール](https://console.firebase.google.com/)にアクセス
2. Googleアカウントでサインイン
3. 「プロジェクトを作成」をクリック
4. プロジェクト名に `Gyro` と入力
5. 手順に従ってプロジェクトを作成

### Step 2: Realtime Databaseの有効化

1. プロジェクトのダッシュボード左側メニューから「Realtime Database」を選択
2. 「データベースを作成」をクリック
3. セキュリティルールの選択画面で「テストモードで開始」を選択
4. 有効にする

⚠️ **注意**: テストモードでは誰でもデータの読み書きが可能になります。本番環境で利用する場合は、必ずセキュリティルールを変更してください。

### Step 3: セキュリティルールの設定

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

### Step 4: Webアプリの設定情報取得

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

## 2. Unity側のFirebase SDK導入

### Step 1: Firebase SDK for Unityのダウンロード

1. [Firebase SDK for Unityのダウンロードページ](https://firebase.google.com/download/unity)にアクセス
2. Realtime Database SDKを含む`.unitypackage`をダウンロード

### Step 2: Unityプロジェクトへのインポート

1. Unityプロジェクトを開く
2. ダウンロードした`.unitypackage`ファイルをUnityエディターにドラッグ＆ドロップ
3. インポートダイアログで「Import」をクリック

### Step 3: Firebase設定ファイルの追加（オプション）

1. プロジェクトの設定画面から「アプリの追加」でUnityアイコンを選択
2. Unityアプリのパッケージ名（例：`com.yourcompany.gyro`）を入力
3. 設定ファイルをダウンロード
4. ダウンロードした`google-services.json`（Android/Editor用）および`GoogleService-Info.plist`（iOS用）を`Assets`フォルダ直下に配置

### Step 4: C#スクリプトの設定

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

3. UnityMainThreadDispatcherの追加（推奨）:
   - [UnityMainThreadDispatcher](https://github.com/PimDeWitte/UnityMainThreadDispatcher)を導入
   - Firebase SDKからのデータ受信はバックグラウンドスレッドで行われるため、UnityのGameObject操作はメインスレッドで実行する必要があります

## 3. 動作確認

### Webサイト側

1. ブラウザで`Web/index.html`を開く
2. 「ジャイロを有効化」ボタンをクリック
3. ジャイロセンサーの許可を与える
4. Firebaseコンソールでデータが保存されているか確認

### Unity側

1. Unityでゲームを実行
2. Firebase RTDBからデータを受信
3. ジャイロデータをゲーム操作に反映

## 4. トラブルシューティング

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

## 5. セキュリティ

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

