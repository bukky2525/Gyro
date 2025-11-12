using UnityEngine;
using System;
using System.Collections;

// Firebase SDKが必要です
// Firebase Realtime Database SDK for Unityをプロジェクトに追加してください
// 詳細はREADME.mdを参照してください

// Firebase SDKを使用する場合のコード（コメントアウト）
/*
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
*/

[Serializable]
public struct GyroData
{
    public float alpha; // Z軸回転 (ヨー)
    public float beta;  // X軸回転 (ピッチ)
    public float gamma; // Y軸回転 (ロール)
    public long timestamp;
}

public class Test : MonoBehaviour
{
    [Header("Firebase Realtime Database 設定")]
    [Tooltip("Firebase Realtime Databaseのパス（デフォルト: gyro/data）")]
    [SerializeField] private string databasePath = "gyro/data";
    
    [Header("ジャイロデータ設定")]
    [Tooltip("ジャイロデータのログを表示するか")]
    [SerializeField] private bool logGyroValues = true;
    
    [Tooltip("受信したジャイロデータをUnityの入力として使用するか")]
    [SerializeField] private bool useGyroAsInput = true;

    [Header("回転対象オブジェクト")]
    [Tooltip("回転させる対象のGameObject（未設定の場合は自動検索）")]
    [SerializeField] private GameObject targetObject;

    [Header("回転設定")]
    [Tooltip("回転の補間速度")]
    [SerializeField] private float rotationSpeed = 10f;

    // Firebase SDKを使用する場合の変数（コメントアウト）
    // private DatabaseReference databaseReference;
    // private FirebaseApp firebaseApp;

    private GyroData _latestGyroData;
    private bool _hasGyroData = false;
    private bool _isFirebaseInitialized = false;

    public event Action<GyroData> OnGyroDataReceived;
    public event Action OnFirebaseInitialized;
    public event Action<string> OnFirebaseError;

    public bool IsFirebaseInitialized => _isFirebaseInitialized;
    public bool HasGyroData => _hasGyroData;
    public GyroData LatestGyroData => _latestGyroData;

    void Start()
    {
        // ターゲットオブジェクトが見つからない場合、自動検索
        if (targetObject == null)
        {
            targetObject = GameObject.Find("TargetCube");
            if (targetObject == null)
            {
                Debug.LogWarning("ターゲットオブジェクトが見つかりません。シーンに 'TargetCube' という名前のGameObjectを配置してください。");
            }
        }

        // Firebase初期化
        InitializeFirebase();
    }

    private void InitializeFirebase()
    {
        Debug.Log("Firebase Realtime Databaseの初期化を開始します...");

        // ⚠️ Firebase SDKが必要です
        // 以下のコードは、Firebase SDKをプロジェクトに追加した後に有効にしてください
        
        /*
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {
                firebaseApp = FirebaseApp.DefaultInstance;
                databaseReference = FirebaseDatabase.DefaultInstance.RootReference;
                
                _isFirebaseInitialized = true;
                Debug.Log("Firebase Realtime Databaseの初期化が完了しました。");
                OnFirebaseInitialized?.Invoke();
                
                StartListeningForData();
            }
            else
            {
                string errorMessage = $"Firebaseの依存関係エラー: {dependencyStatus}";
                Debug.LogError(errorMessage);
                OnFirebaseError?.Invoke(errorMessage);
            }
        });
        */

        // デモ用: Firebase SDKが導入されるまでのモックデータ
        Debug.LogWarning("⚠️ Firebase SDKが導入されていません。");
        Debug.LogWarning("⚠️ Firebase Realtime Database SDK for Unityをプロジェクトに追加してください。");
        Debug.LogWarning("⚠️ 詳細はREADME.mdを参照してください。");
        
        // モックデータで動作確認（実際のFirebase SDK導入後は削除）
        StartCoroutine(MockGyroData());
    }

    private void StartListeningForData()
    {
        Debug.Log($"Firebase Realtime Databaseの監視を開始しました。パス: {databasePath}");

        // ⚠️ Firebase SDKが必要です
        // 以下のコードは、Firebase SDKをプロジェクトに追加した後に有効にしてください
        
        /*
        DatabaseReference gyroRef = databaseReference.Child(databasePath);
        
        gyroRef.ValueChanged += HandleValueChanged;
        */
    }

    // Firebase SDKを使用する場合のハンドラー（コメントアウト）
    /*
    private void HandleValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null)
        {
            string errorMessage = $"データベースエラー: {args.DatabaseError.Message}";
            Debug.LogError(errorMessage);
            OnFirebaseError?.Invoke(errorMessage);
            return;
        }

        DataSnapshot snapshot = args.Snapshot;
        if (snapshot.Exists)
        {
            string json = snapshot.GetRawJsonValue();
            Debug.Log($"Firebaseからデータを受信: {json}");

            try
            {
                // JSONをパース
                GyroData data = JsonUtility.FromJson<GyroData>(json);
                
                _latestGyroData = data;
                _hasGyroData = true;

                if (logGyroValues)
                {
                    Debug.Log($"ジャイロデータ受信 => Alpha: {data.alpha}, Beta: {data.beta}, Gamma: {data.gamma}");
                }

                // メインスレッドで処理
                UnityMainThreadDispatcher.Instance.Enqueue(() => {
                    ProcessGyroData(data);
                    OnGyroDataReceived?.Invoke(data);
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"JSONのパースに失敗: {e.Message}");
            }
        }
    }
    */

    private void ProcessGyroData(GyroData gyroData)
    {
        if (!useGyroAsInput)
        {
            return;
        }

        // ジャイロデータを使用してUnityの入力として処理
        if (targetObject != null)
        {
            // ジャイロの値をそのまま回転角度として利用
            // Quaternion.Euler(X軸, Y軸, Z軸)
            Quaternion targetRotation = Quaternion.Euler(gyroData.beta, gyroData.alpha, gyroData.gamma);
            
            // 滑らかにするためLerpで補間
            targetObject.transform.rotation = Quaternion.Lerp(
                targetObject.transform.rotation,
                targetRotation,
                Time.deltaTime * rotationSpeed
            );
        }
    }

    void Update()
    {
        // ジャイロデータを使用してUnityの入力として処理
        if (useGyroAsInput && _hasGyroData)
        {
            ProcessGyroData(_latestGyroData);
        }
    }

    void OnDestroy()
    {
        // Firebase SDKを使用する場合のクリーンアップ（コメントアウト）
        /*
        if (databaseReference != null)
        {
            DatabaseReference gyroRef = databaseReference.Child(databasePath);
            gyroRef.ValueChanged -= HandleValueChanged;
        }
        */
    }

    // デモ用: Firebase SDKが導入されるまでのモックデータ（実際のFirebase SDK導入後は削除）
    private IEnumerator MockGyroData()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.1f);
            
            // モックデータを生成
            _latestGyroData = new GyroData
            {
                alpha = Mathf.Sin(Time.time) * 180f,
                beta = Mathf.Cos(Time.time) * 90f,
                gamma = Mathf.Sin(Time.time * 0.5f) * 45f,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            _hasGyroData = true;

            if (logGyroValues)
            {
                Debug.Log($"モックジャイロデータ => Alpha: {_latestGyroData.alpha:F2}, Beta: {_latestGyroData.beta:F2}, Gamma: {_latestGyroData.gamma:F2}");
            }

            OnGyroDataReceived?.Invoke(_latestGyroData);
        }
    }

    // デバッグ用: サーバーの状態を表示
    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = Color.white;
        style.wordWrap = true;

        int yPos = 10;
        GUI.Label(new Rect(10, yPos, 800, 30), $"Firebase接続: {(_isFirebaseInitialized ? "接続済み ✅" : "未接続 ❌")}", style);
        yPos += 35;
        
        GUI.Label(new Rect(10, yPos, 800, 30), $"データベースパス: {databasePath}", style);
        yPos += 35;
        
        if (!_isFirebaseInitialized)
        {
            GUI.Label(new Rect(10, yPos, 800, 60), $"⚠️ Firebase SDKが必要です。README.mdを参照してください。", style);
            yPos += 65;
        }
        
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

