using UnityEngine;
using Firebase;
using Firebase.Database;
using System;
using System.Collections;

/// <summary>
/// PlayerとFirebaseGyroReceiverの機能を統合したハイブリッドスクリプト
/// Firebaseからの移動方向データを受信し、Playerと同じ物理システムで移動します
/// Playerオブジェクトにアタッチして使用します
/// </summary>
public class Test : MonoBehaviour
{
    [Header("移動設定")]
    [SerializeField] private float _moveForce = 4000f;       // 移動力を大幅に上げる（素早い動き）
    [SerializeField] private float _maxSpeed = 50f;          // 最大速度を大幅に上げる
    
    [Header("重力・物理設定")]
    [SerializeField] private float _gravityMultiplier = 2f;  // 重力の倍率を下げる（軽やかな落下）
    [SerializeField] private float _mass = 3f;               // 質量を下げる（軽やかな動き）
    [SerializeField] private float _drag = 0.5f;            // 空気抵抗をさらに下げる
    [SerializeField] private float _angularDrag = 0.8f;      // 回転抵抗を下げる（軽やかな回転）
    
    [Header("バランス設定")]
    [SerializeField] private float _balanceForce = 800f;     // バランス力を上げる
    [SerializeField] private float _stabilizationForce = 150f; // 安定化力を下げる（より自然な動き）
    
    [Header("カメラ設定")]
    [Tooltip("カメラのTransformをセットしてください")]
    public Transform cameraTransform; // カメラのTransformをセットする
    
    [Header("リスポーン設定")]
    public KeyCode respawnKey = KeyCode.R; // 手動リスポーンキー
    
    [Header("カウントダウン設定")]
    public float countdownDuration = 3f; // カウントダウンの時間（秒）
    
    [Header("Firebase設定")]
    [Tooltip("Firebase Realtime Databaseのパス（デフォルト: control/movement）")]
    [SerializeField] private string movementPath = "control/movement";
    
    [Header("入力設定")]
    [Tooltip("Firebase入力を優先するか")]
    [SerializeField] private bool useFirebaseInput = true;
    
    [Tooltip("通常入力を許可するか（Firebase入力が優先されない場合のみ）")]
    [SerializeField] private bool allowNormalInput = true;
    
    [Tooltip("プレイヤーが移動可能かどうか（デフォルト: true）")]
    [SerializeField] private bool startCanMove = true;
    
    // 移動方向データ受け取り用の構造体
    [System.Serializable]
    public class MovementData
    {
        public float moveX; // -1: 左, 0: なし, 1: 右
        public float moveZ; // -1: 後ろ, 0: なし, 1: 前
        public long timestamp;
    }
    
    // 現在の移動方向（他のスクリプトからアクセス可能）
    public static MovementData CurrentMovement { get; private set; } = new MovementData();
    
    // プロパティでInspectorとScriptを同期
    public float moveForce { get => _moveForce; set => _moveForce = value; }
    public float maxSpeed { get => _maxSpeed; set => _maxSpeed = value; }
    public float gravityMultiplier { get => _gravityMultiplier; set => _gravityMultiplier = value; }
    public float mass { get => _mass; set => _mass = value; }
    public float drag { get => _drag; set => _drag = value; }
    public float angularDrag { get => _angularDrag; set => _angularDrag = value; }
    public float balanceForce { get => _balanceForce; set => _balanceForce = value; }
    public float stabilizationForce { get => _stabilizationForce; set => _stabilizationForce = value; }
    
    // Firebase関連
    private DatabaseReference reference;
    private bool _isFirebaseInitialized = false;
    
    // 物理関連（Player.csと同じ）
    private Rigidbody rb;
    private Vector3 lastVelocity;
    private RespawnManager respawnManager;
    private Vector3 moveDirection;
    private Vector3 customGravity;
    private bool canMove; // プレイヤーが動けるかどうか
    
    // Firebaseからの入力
    private float firebaseHorizontal = 0f;
    private float firebaseVertical = 0f;
    private bool hasFirebaseInput = false;
    
    // イベント
    public event Action<MovementData> OnMovementDataReceived;
    public event Action OnFirebaseInitialized;
    public event Action<string> OnFirebaseError;
    
    // プロパティ
    public bool IsFirebaseInitialized => _isFirebaseInitialized;
    public bool HasFirebaseInput => hasFirebaseInput;
    
    void Start()
    {
        // Rigidbodyを取得
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("Rigidbodyコンポーネントが見つかりません。このスクリプトはRigidbodyが必要です。");
            return;
        }

        // 物理特性の設定（鉄球らしく）
        rb.mass = _mass;
        rb.linearDamping = _drag;
        rb.angularDamping = _angularDrag;
        rb.useGravity = false; // カスタム重力を使用するため無効化
        
        // ボールの物理マテリアルを設定（軽やかな鉄球）
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            PhysicsMaterial ballMaterial = new PhysicsMaterial("FastIronBallMaterial");
            ballMaterial.dynamicFriction = 0.4f;      // 摩擦を下げる（滑らかな動き）
            ballMaterial.staticFriction = 0.4f;       // 静止摩擦を下げる
            ballMaterial.bounciness = 0.15f;          // 反発を上げる（軽やかな動き）
            ballMaterial.frictionCombine = PhysicsMaterialCombine.Average;
            ballMaterial.bounceCombine = PhysicsMaterialCombine.Average;
            
            col.material = ballMaterial;
        }
        
        // カスタム重力を設定
        customGravity = Physics.gravity * _gravityMultiplier;
        
        // リスポーンマネージャーを検索
        respawnManager = FindFirstObjectByType<RespawnManager>();
        if (respawnManager == null)
        {
            Debug.LogWarning("RespawnManagerが見つかりません。リスポーン機能が使用できません。");
        }
        
        // Firebase初期化
        InitializeFirebase();
        
        // 移動可能状態を設定
        canMove = startCanMove;
        
        Debug.Log($"プレイヤーが初期化されました。移動可能: {canMove}, useFirebaseInput: {useFirebaseInput}, allowNormalInput: {allowNormalInput}");
    }

    private void InitializeFirebase()
    {
        Debug.Log("Firebase Realtime Databaseの初期化を開始します...");
        
        // Firebaseの依存関係をチェックし、修復
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
        {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {
                // データベース参照を取得
                reference = FirebaseDatabase.DefaultInstance.RootReference;
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
    }

    private void StartListeningForData()
    {
        Debug.Log($"Firebase Realtime Databaseの監視を開始しました。パス: {movementPath}");

        // 指定パスの値が変更されるたびに呼ばれるイベントリスナーを設定
        reference.Child(movementPath).ValueChanged += HandleValueChanged;
    }

    // ★ データベースの値が更新されるたびに呼ばれる
    private void HandleValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null)
        {
            Debug.LogError($"データベースエラー: {args.DatabaseError.Message}");
            OnFirebaseError?.Invoke(args.DatabaseError.Message);
            return;
        }

        // 受信したデータを解析
        DataSnapshot snapshot = args.Snapshot;
        if (snapshot.Exists)
        {
            string json = snapshot.GetRawJsonValue();
            
            // UnityのJsonUtilityを使用してJSONをデシリアライズ
            MovementData data = JsonUtility.FromJson<MovementData>(json);

            // メインスレッドでの操作をキューに入れる
            MainThreadDispatcher.Instance().Enqueue(() => {
                // 移動方向データを更新
                CurrentMovement = data;
                hasFirebaseInput = true;
                
                // Firebaseからの入力を設定
                firebaseHorizontal = data.moveX;
                firebaseVertical = data.moveZ;
                
                OnMovementDataReceived?.Invoke(data);
                
                // デバッグログは最初の数回だけ表示（ログが多すぎるのを防ぐ）
                // Debug.Log($"移動方向受信: moveX={data.moveX}, moveZ={data.moveZ}");
            });
        }
    }

    // Update is called once per frame
    void Update()
    {
        HandleRespawn();
        
        // 移動入力を処理
        if (canMove)
        {
            HandleInput();
        }
        else
        {
            // canMoveがfalseの場合、moveDirectionをリセット
            moveDirection = Vector3.zero;
        }
    }
    
    // 物理演算のタイミングで呼ばれる（Rigidbodyの操作に適している）
    void FixedUpdate()
    {
        // カスタム重力を適用（鉄球の重い落下）
        rb.AddForce(customGravity, ForceMode.Acceleration);
        
        // カウントダウン中は移動しない
        if (!canMove)
        {
            // プレイヤーを静止状態に保つ
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        // 現在の速度を取得
        Vector3 currentVelocity = rb.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);
        
        // 移動方向が設定されている場合のみ力を加える
        if (moveDirection.magnitude > 0.01f)
        {
            // 水平方向の最大速度制限
            if (horizontalVelocity.magnitude < _maxSpeed)
            {
                // Rigidbodyに力を加えてPlayerを移動させる
                rb.AddForce(moveDirection.normalized * _moveForce * Time.fixedDeltaTime, ForceMode.Force);
            }
        }
        
        StabilizeMovement();
    }
    
    void HandleInput()
    {
        float horizontal = 0f;
        float vertical = 0f;
        
        // Firebase入力を優先
        if (useFirebaseInput && hasFirebaseInput)
        {
            horizontal = firebaseHorizontal;
            vertical = firebaseVertical; // moveZ=1で前、moveZ=-1で後ろ（Web側で正しい方向に設定済み）
        }
        // 通常入力（WASDキーまたは左スティック）
        else if (allowNormalInput)
        {
            horizontal = Input.GetAxis("Horizontal"); // A, Dキー / ←, →キー / 左スティック左右
            vertical = Input.GetAxis("Vertical");     // W, Sキー / ↑, ↓キー / 左スティック上下
        }
        
        // カメラの向きを基準にした移動方向を計算
        CalculateMoveDirection(horizontal, vertical);
        
        // デバッグ: 入力値と移動方向を確認（最初の数回だけ）
        if (Mathf.Abs(horizontal) > 0.1f || Mathf.Abs(vertical) > 0.1f)
        {
            // Debug.Log($"入力: horizontal={horizontal}, vertical={vertical}, moveDirection={moveDirection}");
        }
        
        // 微細なバランス調整
        if (Mathf.Abs(horizontal) < 0.1f && Mathf.Abs(vertical) < 0.1f)
        {
            ApplyBalanceCorrection();
        }
    }
    
    void CalculateMoveDirection(float horizontal, float vertical)
    {
        // カメラの向きを基準にした移動方向を計算
        if (cameraTransform != null)
        {
            // カメラの前方向ベクトルを取得
            Vector3 camForward = cameraTransform.forward;
            // カメラの右方向ベクトルを取得
            Vector3 camRight = cameraTransform.right;

            // カメラが上下を向いていても水平に移動するように、Y成分を0にして正規化する
            camForward.y = 0;
            camRight.y = 0;
            camForward.Normalize();
            camRight.Normalize();

            // 入力とカメラの向きから、最終的な移動方向を決定
            moveDirection = camForward * vertical + camRight * horizontal;
        }
        else
        {
            // カメラが設定されていない場合は、ワールド座標系で移動
            moveDirection = new Vector3(horizontal, 0, vertical);
        }
    }
    
    void StabilizeMovement()
    {
        // 水平方向の速度制限（垂直方向は重力に任せる）
        Vector3 currentVelocity = rb.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);
        
        if (horizontalVelocity.magnitude > _maxSpeed)
        {
            Vector3 limitedVelocity = horizontalVelocity.normalized * _maxSpeed;
            rb.linearVelocity = new Vector3(limitedVelocity.x, currentVelocity.y, limitedVelocity.z);
        }
        
        // 回転の安定化（軽やかな回転）
        if (rb.angularVelocity.magnitude > 15f)
        {
            rb.angularVelocity = rb.angularVelocity.normalized * 15f;
        }
    }
    
    void ApplyBalanceCorrection()
    {
        // 静止時の微細な調整
        Vector3 currentVelocity = rb.linearVelocity;
        currentVelocity.y = 0;
        
        if (currentVelocity.magnitude > 0.1f)
        {
            Vector3 correction = -currentVelocity * _stabilizationForce * Time.deltaTime;
            rb.AddForce(correction, ForceMode.Force);
        }
    }
    
    void HandleRespawn()
    {
        // 手動リスポーン（Rキー）
        if (Input.GetKeyDown(respawnKey) && respawnManager != null)
        {
            respawnManager.ManualRespawn();
        }
    }
    
    // 外部から移動可能状態を設定するメソッド
    public void SetCanMove(bool canMoveState)
    {
        canMove = canMoveState;
        Debug.Log($"プレイヤーの移動状態: {(canMove ? "可能" : "不可能")}");
    }
    
    // 外部から移動可能状態を取得するメソッド
    public bool GetCanMove()
    {
        return canMove;
    }
    
    // Firebase入力を有効/無効にする
    public void SetUseFirebaseInput(bool use)
    {
        useFirebaseInput = use;
    }
    
    // 通常入力を有効/無効にする
    public void SetAllowNormalInput(bool allow)
    {
        allowNormalInput = allow;
    }
    
    void OnDisable()
    {
        // Play停止時やオブジェクトが無効化されたときにイベントリスナーを解除
        UnsubscribeFromFirebase();
    }
    
    void OnApplicationPause(bool pauseStatus)
    {
        // アプリがバックグラウンドに移ったときも解除
        if (pauseStatus)
        {
            UnsubscribeFromFirebase();
        }
    }
    
    void OnDestroy()
    {
        // アプリ終了時にイベントリスナーを解除
        UnsubscribeFromFirebase();
    }
    
    private void UnsubscribeFromFirebase()
    {
        if (reference != null)
        {
            try
            {
                reference.Child(movementPath).ValueChanged -= HandleValueChanged;
                Debug.Log("Firebase Realtime Databaseの監視を停止しました。");
            }
            catch (System.Exception e)
            {
                // 既に解除されている場合はエラーを無視
                Debug.LogWarning($"Firebaseリスナーの解除中にエラーが発生しました（無視可能）: {e.Message}");
            }
        }
    }
}
