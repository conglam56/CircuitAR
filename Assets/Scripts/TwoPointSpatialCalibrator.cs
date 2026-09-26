using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Quản lý căn chỉnh và cố định bàn mạch AR (TwoPointSpatialCalibrator):
/// 1. TỰ ĐỘNG ĐẶT CHÍNH DIỆN: Khi chạm, bàn mạch tự động đặt chính diện song song với tầm mắt người dùng.
/// 2. CỬ CHỈ XOAY TỰ NHIÊN NHƯ IKEA PLACE (Gesture Rotate - Không cần nút):
///    - Dùng 2 ngón tay vặn xoay (Twist) HOẶC vuốt 1 ngón tay ngang màn hình để xoay bàn mạch khớp khít 100% với cạnh bàn gỗ thật.
///    - Thao tác trực tiếp trên màn hình cực nhanh, mượt và tự nhiên.
/// 3. KHÓA CỨNG CAO ĐỘ THẾ GIỚI: Vị trí Y và XZ luôn giữ nguyên trên mặt bàn, không di chuyển theo camera.
/// 4. Thanh công cụ tinh gọn: Chỉ gồm thanh kéo Rộng, Dài, Presets và nút XÁC NHẬN CỐ ĐỊNH.
/// </summary>
public class TwoPointSpatialCalibrator : MonoBehaviour
{
    [Header("--- THAM CHIẾU AR ---")]
    public ARRaycastManager raycastManager;
    public ARPlaneManager planeManager;
    public ARAnchorManager anchorManager;
    public SinglePlaneLockController lockController;

    [Header("--- PREFAB BÀN MẠCH ---")]
    public GameObject tableBoardPrefab;

    [Header("--- KÍCH THƯỚC BÀN MẶC ĐỊNH (METERS) ---")]
    [Tooltip("Chiều rộng mặt bàn (ngang). Mặc định 1.20m (120cm)")]
    public float defaultBoardWidth = 1.20f;
    [Tooltip("Chiều sâu mặt bàn (dọc). Mặc định 0.80m (80cm)")]
    public float defaultBoardDepth = 0.80f;
    [Tooltip("Độ dày mặt bàn. Mặc định 1cm")]
    public float defaultBoardThickness = 0.01f;

    [Header("--- BOARD ORIENTATION LOCK ---")]
    [Tooltip("Use the user's horizontal viewing direction at placement, instead of the AR plane's changing axis.")]
    public bool alignBoardToUserOnPlacement = true;
    [Tooltip("Keep the board parallel to the user and disable rotation gestures after placement.")]
    public bool lockBoardOrientationToUser = true;

    [Header("--- ANCHOR STABILIZATION ---")]
    [Tooltip("Ignore tiny anchor position changes to prevent visible micro-jitter.")]
    [Min(0f)] public float anchorPositionDeadband = 0.002f;
    [Tooltip("Maximum world-space correction speed after AR relocalization.")]
    [Min(0.01f)] public float maxAnchorCorrectionSpeed = 0.20f;
    [Tooltip("Maximum yaw correction speed after AR relocalization.")]
    [Min(1f)] public float maxAnchorRotationSpeed = 30f;

    [Header("--- UI VÀ TÂM NGẮM ---")]
    public GameObject spatialCalibrationUI;
    public GameObject reticleUI;
    public TextMeshProUGUI btnLabelTMP;
    public Text btnLabelLegacy;

    [Header("--- TƯƠNG TÁC MEDIAPIPE ---")]
    public ButtonInteractor handInteractor;

    // Quản lý bàn mạch trong không gian
    public GameObject ActiveBoardAnchor { get; private set; }
    public GameObject ActiveBoard { get; private set; }

    private bool isPlaced = false;
    private bool isAdjusting = false;
    private TrackableId currentPlaneId = TrackableId.invalidId;
    private GameObject fineTuneUIRoot;
    private Slider widthSlider;
    private Slider depthSlider;

    // UI Hiện đại nâng cấp (Hologram Reticle & Floating HUD)
    private HolographicARReticle holographicReticle;
    private ARScanFloatingHUD floatingHUD;

    private TextMeshProUGUI sizeLabelTMP;
    private TextMeshProUGUI widthValueTMP;
    private TextMeshProUGUI depthValueTMP;
    private List<Image> presetButtonBgs = new List<Image>();
    private List<Outline> presetButtonOutlines = new List<Outline>();
    private List<Vector2> presetSizes = new List<Vector2>();
    private static Sprite fineTuneRoundedRectSprite;
    private static Sprite fineTuneCircleSprite;
    private static Sprite fineTuneCheckSprite;

    private static List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private Coroutine warningCoroutine;
    private bool isWarningActive = false;

    // Toạ độ thế giới bất biến
    private Vector3 lockedWorldPos = Vector3.zero;
    private Quaternion lockedWorldRot = Quaternion.identity;
    private bool isPermanentlyLocked = false;
    private ARAnchor nativeAnchor = null;
    private int anchorRequestVersion = 0;
    private Vector3 anchorToBoardLocalPosition = Vector3.zero;
    private Quaternion anchorToBoardLocalRotation = Quaternion.identity;
    private bool hasAnchorBinding = false;

    void Awake()
    {
        // 1. TẮT VĨNH VIỄN MỌI LINERENDERER (Nguyên nhân gây ra tấm mặt phẳng màu xanh nhạt)
        LineRenderer[] lrs = GetComponentsInChildren<LineRenderer>(true);
        foreach (var lr in lrs)
        {
            lr.enabled = false;
            lr.positionCount = 0;
        }

        // 2. Khôi phục kích thước chuẩn nếu Unity Inspector deserialize giá trị cũ quá nhỏ
        if (defaultBoardWidth < 0.3f) defaultBoardWidth = 1.20f;
        if (defaultBoardDepth < 0.3f) defaultBoardDepth = 0.80f;
        if (defaultBoardThickness < 0.005f) defaultBoardThickness = 0.01f;

        if (raycastManager == null) raycastManager = FindFirstObjectByType<ARRaycastManager>();
        if (raycastManager == null) raycastManager = FindObjectOfType<ARRaycastManager>();

        if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
        if (planeManager == null) planeManager = FindObjectOfType<ARPlaneManager>();

        if (anchorManager == null) anchorManager = FindFirstObjectByType<ARAnchorManager>();
        if (anchorManager == null) anchorManager = FindObjectOfType<ARAnchorManager>();

        if (lockController == null) lockController = FindFirstObjectByType<SinglePlaneLockController>();
        if (lockController == null) lockController = FindObjectOfType<SinglePlaneLockController>();

        if (handInteractor == null) handInteractor = FindFirstObjectByType<ButtonInteractor>();
        if (handInteractor == null) handInteractor = FindObjectOfType<ButtonInteractor>();

        // 3. Khởi tạo Hệ thống Holographic Reticle & Floating HUD hiện đại
        if (holographicReticle == null) holographicReticle = HolographicARReticle.Create(null);
        if (floatingHUD == null) floatingHUD = ARScanFloatingHUD.EnsureInstance();

        // Ẩn tâm ngắm đỏ vuông cũ trong scene nếu có
        if (reticleUI != null)
        {
            Image oldImg = reticleUI.GetComponent<Image>();
            if (oldImg != null && oldImg.color.r > 0.8f && oldImg.color.g < 0.2f && oldImg.sprite == null)
            {
                reticleUI.SetActive(false);
            }
        }

        // Ẩn visual của nút 3D lơ lửng cũ để không chắn camera view trong không gian 3D
        if (spatialCalibrationUI != null)
        {
            Button oldBtn = spatialCalibrationUI.GetComponentInChildren<Button>(true);
            if (oldBtn != null)
            {
                foreach (var g in oldBtn.GetComponentsInChildren<Graphic>(true))
                {
                    g.enabled = false;
                }
            }
        }
    }

    void OnEnable()
    {
        ARSession.stateChanged += OnARSessionStateChanged;
        if (!isPlaced)
        {
            if (floatingHUD == null) floatingHUD = ARScanFloatingHUD.EnsureInstance();
            if (floatingHUD != null) floatingHUD.ShowScanning();
        }
    }

    void OnDisable()
    {
        ARSession.stateChanged -= OnARSessionStateChanged;
        if (holographicReticle != null) holographicReticle.Hide();
        if (floatingHUD != null) floatingHUD.Hide();
    }

    void Start()
    {
        if (floatingHUD == null) floatingHUD = ARScanFloatingHUD.EnsureInstance();
        if (!isPlaced && floatingHUD != null)
        {
            floatingHUD.ShowScanning();
        }
        SetText("HÃY HƯỚNG CAMERA VÀO MẶT BÀN ĐỂ QUÉT...");
    }

    void Update()
    {
        // 1. KHI BÀN MẠCH ĐÃ ĐƯỢC ĐẶT:
        if (isPlaced && ActiveBoardAnchor != null)
        {
            // Follow a standalone world anchor. It survives plane subsumption and also
            // receives ARCore relocalization corrections after rapid camera movement.
            if (nativeAnchor != null && nativeAnchor.trackingState == TrackingState.Tracking)
            {
                UpdateLockedPoseFromAnchor();
            }

            // Luôn giữ vững toạ độ Y và XZ trên mặt bàn (chống tụt xuống theo camera khi cúi máy)
            ActiveBoardAnchor.transform.position = lockedWorldPos;

            // KHI ĐANG Ở GIAI ĐOẠN ĐIỀU CHỈNH (CHƯA BẤM XÁC NHẬN KHÓA):
            if (!isPermanentlyLocked && isAdjusting && !lockBoardOrientationToUser)
            {
                HandleScreenRotationGestures();
            }

            ActiveBoardAnchor.transform.rotation = lockedWorldRot;
            return;
        }

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        // 2. Raycast tìm mặt bàn nằm ngang thực tế dưới tâm ngắm
        bool hasCenterPlane = TryGetPlaneUnderPoint(screenCenter, out Pose centerHitPose, out ARPlane centerHitPlane, out _);

        // 3. Cập nhật tâm ngắm (Reticle) bám phẳng lì theo Vector3.up
        UpdateReticleAndUI(hasCenterPlane, centerHitPose, centerHitPlane);

        // 4. Bắt chạm ngón tay vào màn hình (Screen Touch)
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                TryExecutePlacement(touch.position);
                return;
            }
        }

        // 5. Bắt click chuột (Hỗ trợ thử nghiệm trong Unity Editor và PC)
        if (Input.GetMouseButtonDown(0))
        {
            TryExecutePlacement(Input.mousePosition);
            return;
        }

        // 6. Bắt cử chỉ chụm ngón tay MediaPipe (Hand Pinch)
        if (handInteractor != null && handInteractor.JustPinched)
        {
            TryExecutePlacement(screenCenter);
            return;
        }
    }

    /// <summary>
    /// Xử lý cử chỉ vặn xoay màn hình tự nhiên (Chuẩn IKEA Place / Apple RealityKit):
    /// - 2 ngón tay vặn xoay (Twist): Xoay bàn mạch theo góc vặn của ngón tay.
    /// - 1 ngón tay vuốt ngang (trên vùng trống màn hình): Xoay bàn mạch sang trái / phải mượt mà.
    /// - Hỗ trợ kéo chuột trên PC / Unity Editor.
    /// </summary>
    /// <summary>
    /// Xử lý cử chỉ vặn xoay màn hình tự nhiên:
    /// - Chỉ nhận cử chỉ 2 ngón tay vặn xoay (Two-Finger Twist) với ngưỡng chống rung (Deadband > 0.6°).
    /// - ĐÃ LOẠI BỎ vuốt 1 ngón tay và Mouse X vì trên Android gây ra xung đột spike làm bàn mạch bị giật văng sang phải.
    /// </summary>
    private void HandleScreenRotationGestures()
    {
        float toolbarHeightThreshold = Screen.height * 0.35f;

        // CHỈ NHẬN CỬ CHỈ 2 NGÓN TAY VẶN XOAY RÕ RÀNG (Two-Finger Twist)
        if (Input.touchCount == 2)
        {
            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);

            // Bỏ qua nếu bất kỳ ngón nào chạm vào vùng Toolbar mép dưới
            if (t0.position.y > toolbarHeightThreshold && t1.position.y > toolbarHeightThreshold)
            {
                Vector2 prevDir = (t1.position - t1.deltaPosition) - (t0.position - t0.deltaPosition);
                Vector2 currDir = t1.position - t0.position;

                // Khoảng cách 2 ngón phải đủ xa (> 40px) để không bị nhầm lẫn
                if (currDir.sqrMagnitude > 1600f && prevDir.sqrMagnitude > 1600f)
                {
                    float angleDelta = Vector2.SignedAngle(prevDir, currDir);
                    // Ngưỡng chống rung: Chỉ xoay khi góc vặn > 0.6 độ
                    if (Mathf.Abs(angleDelta) > 0.6f)
                    {
                        lockedWorldRot = Quaternion.AngleAxis(-angleDelta, Vector3.up) * lockedWorldRot;
                    }
                }
            }
            return;
        }

#if UNITY_EDITOR
        // CHỈ HỖ TRỢ TRONG UNITY EDITOR TRÊN PC (Chuột phải kéo để test)
        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X");
            if (Mathf.Abs(mouseX) > 0.05f)
            {
                lockedWorldRot = Quaternion.AngleAxis(-mouseX * 2f, Vector3.up) * lockedWorldRot;
            }
        }
#endif
    }

    /// <summary>
    /// Thử raycast tìm mặt bàn thực tế nằm ngang (HorizontalUp).
    /// Có bộ lọc loại trừ mặt sàn nhà bên dưới gầm bàn để không bao giờ bị bắt nhầm độ cao quá thấp.
    /// </summary>
    private bool TryGetPlaneUnderPoint(Vector2 screenPoint, out Pose hitPose, out ARPlane hitPlane, out TrackableId planeId)
    {
        hitPose = Pose.identity;
        hitPlane = null;
        planeId = TrackableId.invalidId;

        if (raycastManager == null) return false;

        TrackableType planeTypes = TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds;
        if (raycastManager.Raycast(screenPoint, hits, planeTypes))
        {
            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;

            for (int i = 0; i < hits.Count; i++)
            {
                TrackableId tId = hits[i].trackableId;
                ARPlane plane = planeManager != null ? planeManager.GetPlane(tId) : null;

                if (plane != null)
                {
                    if (plane.alignment != PlaneAlignment.HorizontalUp)
                    {
                        continue;
                    }
                }
                else
                {
                    if (hits[i].pose.up.y < 0.6f)
                    {
                        continue;
                    }
                }

                // BỘ LỌC CHIỀU CAO: Tránh bắt nhầm mặt sàn nhà dưới gầm bàn
                if (cam != null)
                {
                    float heightDiff = camPos.y - hits[i].pose.position.y;
                    // Mặt bàn học thực tế cách camera từ 0.2m đến 0.85m.
                    // Nếu chênh lệch > 1.05m so với camera trong khi còn hits khác, đó là mặt sàn nhà!
                    if (heightDiff > 1.05f && (i < hits.Count - 1))
                    {
                        continue;
                    }
                }

                hitPose = hits[i].pose;
                planeId = tId;
                hitPlane = plane;
                return true;
            }

            if (hits.Count > 0)
            {
                hitPose = hits[0].pose;
                planeId = hits[0].trackableId;
                hitPlane = planeManager != null ? planeManager.GetPlane(planeId) : null;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cập nhật vị trí tâm ngắm (Reticle):
    /// Bám sát cao độ mặt bàn thật nhưng XOAY PHẲNG TUYỆT ĐỐI theo phương trọng lực chuẩn Vector3.up
    /// </summary>
    private void UpdateReticleAndUI(bool hasPlane, Pose hitPose, ARPlane hitPlane)
    {
        if (hasPlane)
        {
            if (holographicReticle != null)
            {
                holographicReticle.SetTargetPose(hitPose.position + Vector3.up * 0.002f, Quaternion.Euler(90f, 0f, 0f));
                holographicReticle.Show();
            }

            if (reticleUI != null && reticleUI.activeSelf)
            {
                reticleUI.transform.position = hitPose.position + Vector3.up * 0.002f;
                reticleUI.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            if (!isWarningActive)
            {
                if (floatingHUD != null)
                {
                    floatingHUD.ShowDetected("ĐÃ TÌM THẤY MẶT BÀN!", "Chạm vào màn hình để đặt mạch");
                }
                SetText("ĐÃ THẤY MẶT BÀN!\nCHẠM ĐỂ ĐẶT BÀN MẠCH PHẲNG");
            }
        }
        else
        {
            if (holographicReticle != null)
            {
                holographicReticle.Hide();
            }

            if (reticleUI != null && reticleUI.activeSelf)
            {
                reticleUI.SetActive(false);
            }

            if (!isWarningActive)
            {
                if (floatingHUD != null)
                {
                    floatingHUD.ShowScanning("Đang quét tìm mặt bàn...", "Lia nhẹ camera quanh bề mặt phẳng");
                }
                SetText("HÃY HƯỚNG CAMERA VÀO MẶT BÀN ĐỂ QUÉT...");
            }
        }
    }

    /// <summary>
    /// Xử lý yêu cầu đặt bàn 1 chạm tức thì
    /// </summary>
    private void TryExecutePlacement(Vector2 screenPoint)
    {
        if (isPlaced) return;

        // Ưu tiên 1: Raycast tại chính xác điểm ngón tay chạm
        if (!TryGetPlaneUnderPoint(screenPoint, out Pose hitPose, out ARPlane hitPlane, out TrackableId planeId))
        {
            // Ưu tiên 2: Nếu điểm chạm trượt, thử tâm màn hình
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (!TryGetPlaneUnderPoint(screenCenter, out hitPose, out hitPlane, out planeId))
            {
                if (warningCoroutine != null) StopCoroutine(warningCoroutine);
                warningCoroutine = StartCoroutine(FlashWarningRoutine("CHƯA BẮT ĐƯỢC MẶT BÀN!\nHÃY LIA CAMERA VÀO MẶT BÀN CÓ ÁNH SÁNG"));
                return;
            }
        }

        DoPlaceBoard(hitPose, hitPlane, planeId);
    }

    /// <summary>
    /// Khởi tạo bàn mạch:
    /// - TỰ ĐỘNG CĂN CHÍNH DIỆN THEO HƯỚNG NGƯỜI DÙNG: Căn vuông vắn chính diện với góc nhìn camera tại thời điểm đặt.
    /// - HỖ TRỢ CỬ CHỈ XOAY 2 NGÓN TAY (IKEA Place): Người dùng có thể vặn xoay nhẹ trên màn hình để khớp mép bàn gỗ thật.
    /// - PHÁP TUYẾN CHUẨN VECTOR3.UP: Phẳng lì tuyệt đối theo trọng lực Trái Đất.
    /// </summary>
    private void DoPlaceBoard(Pose hitPose, ARPlane hitPlane, TrackableId planeId)
    {
        isPlaced = true;
        isAdjusting = true;
        currentPlaneId = planeId;

        // 1. TỰ ĐỘNG CĂN CHÍNH DIỆN VÀ HÍT KHỚP VUÔNG GÓC VỚI CẠNH BÀN THẬT:
        // Use the line from the user to the placement point. This stays front-facing
        // even when the user taps away from the exact screen center.
        Vector3 userForwardFlat = GetHorizontalDirectionFromUser(hitPose.position);

        // Tự động tìm cạnh bàn từ ARPlane và "hít" vuông góc, triệt tiêu 100% tình trạng bàn bị chéo xiên
        Vector3 alignedForward = alignBoardToUserOnPlacement
            ? userForwardFlat
            : GetTableAlignedForward(userForwardFlat, hitPlane);

        // Góc xoay PHẲNG TUYỆT ĐỐI (Pitch=0, Roll=0) & SONG SONG CẠNH BÀN GỖ THẬT:
        Quaternion rotation = Quaternion.LookRotation(alignedForward, Vector3.up);

        // 2. Cao độ Y: Lấy chính xác từ mặt bàn thực tế (hitPose.position.y) và nâng nhẹ theo độ dày thảm
        Vector3 center = new Vector3(
            hitPose.position.x,
            hitPose.position.y + (defaultBoardThickness * 0.5f),
            hitPose.position.z
        );

        // 3. TẠO NEO KHÔNG GIAN ĐỘC LẬP TẠI SCENE ROOT:
        GameObject anchorObj = new GameObject("Board_Anchor");
        anchorObj.transform.position = center;
        anchorObj.transform.rotation = rotation;
        anchorObj.transform.localScale = Vector3.one;
        anchorObj.transform.SetParent(null, true);

        ActiveBoardAnchor = anchorObj;
        lockedWorldPos = center;
        lockedWorldRot = rotation;

        // Tạo neo vật lý ARCore để triệt tiêu 100% trôi / xê dịch khi camera di chuyển
        CreateARAnchor(new Pose(center, rotation));

        // 4. Khởi tạo Prefab bàn mạch
        if (tableBoardPrefab != null)
        {
            GameObject board = Instantiate(tableBoardPrefab, center, rotation);
            board.transform.SetParent(anchorObj.transform, true);
            board.transform.localPosition = Vector3.zero;
            board.transform.localRotation = Quaternion.identity;
            board.transform.localScale = new Vector3(defaultBoardWidth, defaultBoardThickness, defaultBoardDepth);
            board.name = "ActiveCircuitBoard";
            ActiveBoard = board;

            BoxCollider col = board.GetComponent<BoxCollider>();
            if (col == null) col = board.AddComponent<BoxCollider>();
        }
        else
        {
            GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "ActiveCircuitBoard";
            board.transform.position = center;
            board.transform.rotation = rotation;
            board.transform.localScale = new Vector3(defaultBoardWidth, defaultBoardThickness, defaultBoardDepth);
            board.transform.SetParent(anchorObj.transform, true);
            board.transform.localPosition = Vector3.zero;
            board.transform.localRotation = Quaternion.identity;

            Renderer rend = board.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                rend.material.color = new Color(0.12f, 0.15f, 0.20f, 0.95f);
            }
            ActiveBoard = board;
        }

        try { ActiveBoard.tag = "CircuitBoard"; } catch { }

        // 5. Ẩn UI hướng dẫn ban đầu và tâm ngắm
        if (spatialCalibrationUI != null) spatialCalibrationUI.SetActive(false);
        if (reticleUI != null) reticleUI.SetActive(false);
        if (holographicReticle != null) holographicReticle.Hide();
        if (floatingHUD != null) floatingHUD.Hide();

        // 6. Hiển thị thanh công cụ điều chỉnh kích thước tinh gọn
        ShowFineTuneToolbar();
        Debug.Log("<color=green>[TwoPointSpatialCalibrator]</color> Đã đặt bàn mạch chính diện. Có thể vuốt 1 hoặc 2 ngón tay trên màn hình để xoay khớp mép bàn.");
    }

    /// <summary>
    /// Giám sát sự kiện trạng thái của ARSession:
    /// Nếu camera cúi xuống gầm bàn tối (TrackingState.Limited / Lost), tự động đóng băng toạ độ thế giới
    /// </summary>
    private void OnARSessionStateChanged(ARSessionStateChangedEventArgs args)
    {
        if (args.state == ARSessionState.SessionTracking)
        {
            // SLAM tracking bình thường
        }
        else if (args.state == ARSessionState.Ready || args.state == ARSessionState.SessionInitializing)
        {
            // Đang tái định vị hoặc mất tracking nhẹ: Giữ vững vị trí đã khóa
            if (isPlaced && ActiveBoardAnchor != null)
            {
                ActiveBoardAnchor.transform.position = lockedWorldPos;
                ActiveBoardAnchor.transform.rotation = lockedWorldRot;
            }
        }
    }

    /// <summary>
    /// Tự động căn chỉnh hướng bàn mạch:
    /// - Tìm cạnh thực tế của mặt bàn (thông qua hệ trục chính và đa giác biên ARPlane của ARCore).
    /// - Tự động "hít" (snap) góc xoay khớp vuông vắn 100% với cạnh bàn thật,
    ///   đồng thời giữ hướng chính diện quay về phía người dùng (triệt tiêu hoàn toàn góc chéo xiên).
    /// </summary>
    private Vector3 GetTableAlignedForward(Vector3 userForwardFlat, ARPlane hitPlane)
    {
        if (hitPlane == null) return userForwardFlat;

        Vector3 bestEdge = Vector3.zero;
        float maxEdgeLen = 0f;

        // 1. Phân tích đa giác biên thực tế của ARPlane (convex hull do ARCore quét)
        if (hitPlane.boundary.IsCreated && hitPlane.boundary.Length >= 3)
        {
            var boundary = hitPlane.boundary;
            for (int i = 0; i < boundary.Length; i++)
            {
                Vector2 p1 = boundary[i];
                Vector2 p2 = boundary[(i + 1) % boundary.Length];
                Vector3 worldP1 = hitPlane.transform.TransformPoint(new Vector3(p1.x, 0, p1.y));
                Vector3 worldP2 = hitPlane.transform.TransformPoint(new Vector3(p2.x, 0, p2.y));

                Vector3 edge = new Vector3(worldP2.x - worldP1.x, 0f, worldP2.z - worldP1.z);
                float len = edge.magnitude;
                // Cạnh bàn thật thường là cạnh thẳng dài nhất (> 25cm)
                if (len > maxEdgeLen && len > 0.25f)
                {
                    maxEdgeLen = len;
                    bestEdge = edge.normalized;
                }
            }
        }

        // 2. Nếu đa giác chưa đủ rõ, sử dụng hệ trục chính của ARPlane (ARCore tự căn trục theo bề mặt bàn)
        if (maxEdgeLen < 0.25f)
        {
            Vector3 planeFwd = new Vector3(hitPlane.transform.forward.x, 0f, hitPlane.transform.forward.z).normalized;
            if (planeFwd.sqrMagnitude > 0.001f)
            {
                bestEdge = planeFwd;
            }
            else
            {
                bestEdge = new Vector3(hitPlane.transform.right.x, 0f, hitPlane.transform.right.z).normalized;
            }
        }

        if (bestEdge.sqrMagnitude < 0.001f) return userForwardFlat;

        // Vector vuông góc với cạnh bàn trên mặt phẳng nằm ngang
        Vector3 edgePerp = new Vector3(-bestEdge.z, 0f, bestEdge.x).normalized;

        // 4 hướng trực giao chuẩn của bàn (2 hướng song song mép bàn, 2 hướng vuông góc mép bàn)
        Vector3[] candidates = new Vector3[]
        {
            bestEdge,
            -bestEdge,
            edgePerp,
            -edgePerp
        };

        // Tìm hướng gần nhất với góc nhìn của người dùng
        Vector3 bestForward = userForwardFlat;
        float maxDot = -1f;

        foreach (var cand in candidates)
        {
            float dot = Vector3.Dot(cand, userForwardFlat);
            if (dot > maxDot)
            {
                maxDot = dot;
                bestForward = cand;
            }
        }

        // Nếu góc lệch trong phạm vi 45 độ (dot >= 0.65), tự động "hít" chuẩn 100% khớp cạnh bàn
        if (maxDot >= 0.65f)
        {
            return bestForward.normalized;
        }

        return userForwardFlat;
    }

    /// <summary>
    /// Tự động hít lại góc xoay bàn mạch cho khớp cạnh bàn gỗ
    /// </summary>
    public void AutoSnapToTableEdge()
    {
        if (ActiveBoardAnchor == null) return;
        ARPlane plane = planeManager != null ? planeManager.GetPlane(currentPlaneId) : null;
        Vector3 userForwardFlat = GetHorizontalDirectionFromUser(ActiveBoardAnchor.transform.position);

        Vector3 alignedForward = lockBoardOrientationToUser
            ? userForwardFlat
            : GetTableAlignedForward(userForwardFlat, plane);
        lockedWorldRot = Quaternion.LookRotation(alignedForward, Vector3.up);
        ActiveBoardAnchor.transform.rotation = lockedWorldRot;
        Debug.Log("<color=green>[TwoPointSpatialCalibrator]</color> Đã tự động hít bàn mạch song song với cạnh bàn.");
    }

    /// <summary>
    /// Tạo neo AR vật lý với ARCore:
    /// - Ưu tiên đính trực tiếp vào ARPlane của mặt bàn (AttachAnchor) để khóa chặt với mặt bàn.
    /// - Nếu không đính được, tạo neo tự do trong không gian (TryAddAnchorAsync).
    /// - Nhờ ARAnchor, ARCore sẽ tự động bù trừ sai số trôi dạt (Drift correction) khi người dùng di chuyển quanh bàn.
    /// </summary>
    private async void CreateARAnchor(Pose pose)
    {
        if (anchorManager == null) return;

        int requestVersion = ++anchorRequestVersion;

        // Always create an independent world anchor. A plane-attached anchor inherits
        // the detected plane's lifecycle and can become unstable when that plane is
        // subsumed or no longer observed.
        try
        {
            var result = await anchorManager.TryAddAnchorAsync(pose);
            if (result.status.IsSuccess() && result.value != null)
            {
                if (requestVersion != anchorRequestVersion || !isPlaced)
                {
                    Destroy(result.value.gameObject);
                    return;
                }

                nativeAnchor = result.value;
                BindBoardToAnchor();
                Debug.Log("<color=green>[TwoPointSpatialCalibrator]</color> Đã tạo ARAnchor tự do trong không gian thành công!");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[TwoPointSpatialCalibrator] TryAddAnchorAsync không khả dụng: " + ex.Message);
        }
    }

    private void BindBoardToAnchor()
    {
        if (nativeAnchor == null || ActiveBoardAnchor == null) return;

        anchorToBoardLocalPosition = nativeAnchor.transform.InverseTransformPoint(lockedWorldPos);
        anchorToBoardLocalRotation = Quaternion.Inverse(nativeAnchor.transform.rotation) * lockedWorldRot;
        hasAnchorBinding = true;
    }

    private void UpdateLockedPoseFromAnchor()
    {
        if (nativeAnchor == null || !hasAnchorBinding) return;

        Vector3 targetPosition = nativeAnchor.transform.TransformPoint(anchorToBoardLocalPosition);
        Quaternion targetRotation = FlattenRotation(nativeAnchor.transform.rotation * anchorToBoardLocalRotation);

        float distance = Vector3.Distance(lockedWorldPos, targetPosition);
        if (distance > Mathf.Max(0f, anchorPositionDeadband))
        {
            float positionStep = Mathf.Max(0.01f, maxAnchorCorrectionSpeed) * Time.deltaTime;
            lockedWorldPos = Vector3.MoveTowards(lockedWorldPos, targetPosition, positionStep);
        }

        float angle = Quaternion.Angle(lockedWorldRot, targetRotation);
        if (angle > 0.2f)
        {
            float rotationStep = Mathf.Max(1f, maxAnchorRotationSpeed) * Time.deltaTime;
            lockedWorldRot = Quaternion.RotateTowards(lockedWorldRot, targetRotation, rotationStep);
        }
    }

    /// <summary>
    /// Tạo thanh công cụ tinh chỉnh kích thước cực kỳ tinh gọn và sang trọng (Cyber Glassmorphism Bottom Sheet)
    /// </summary>
    private void ShowFineTuneToolbar()
    {
        if (fineTuneUIRoot != null) Destroy(fineTuneUIRoot);
        EnsureFineTuneSprites();

        fineTuneUIRoot = new GameObject("FineTune_Canvas");
        Canvas canvas = fineTuneUIRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2400;

        CanvasScaler scaler = fineTuneUIRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0f;

        fineTuneUIRoot.AddComponent<GraphicRaycaster>();

        // Khung nền chính ở mép dưới màn hình (Card kính mờ bo góc lớn 32px)
        GameObject panelObj = new GameObject("FineTune_Panel", typeof(RectTransform), typeof(Image));
        panelObj.transform.SetParent(fineTuneUIRoot.transform, false);

        RectTransform panelRt = panelObj.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.04f, 0.015f);
        panelRt.anchorMax = new Vector2(0.96f, 0.355f);
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;

        Image panelImg = panelObj.GetComponent<Image>();
        panelImg.sprite = fineTuneRoundedRectSprite;
        panelImg.type = Image.Type.Sliced;
        panelImg.color = new Color(0.045f, 0.08f, 0.15f, 0.95f); // Dark Slate Glass
        panelImg.raycastTarget = true;

        Outline panelOutline = panelObj.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0f, 0.88f, 1f, 0.65f); // Neon Cyan Outline
        panelOutline.effectDistance = new Vector2(2f, -2f);

        // Thanh gạt Handle ở đỉnh Card (như iOS Bottom Sheet)
        GameObject handleBarObj = new GameObject("Handle_Bar", typeof(RectTransform), typeof(Image));
        handleBarObj.transform.SetParent(panelObj.transform, false);
        RectTransform hbRT = handleBarObj.GetComponent<RectTransform>();
        hbRT.anchorMin = new Vector2(0.5f, 1f);
        hbRT.anchorMax = new Vector2(0.5f, 1f);
        hbRT.pivot = new Vector2(0.5f, 1f);
        hbRT.sizeDelta = new Vector2(50, 5);
        hbRT.anchoredPosition = new Vector2(0, -8f);
        Image hbImg = handleBarObj.GetComponent<Image>();
        hbImg.sprite = fineTuneRoundedRectSprite;
        hbImg.type = Image.Type.Sliced;
        hbImg.color = new Color(1f, 1f, 1f, 0.25f);

        VerticalLayoutGroup vlg = panelObj.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 16, 14);
        vlg.spacing = 8;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // Hàng 0: Tiêu đề + Hướng dẫn cử chỉ xoay
        GameObject headerObj = new GameObject("Header", typeof(RectTransform));
        headerObj.transform.SetParent(panelObj.transform, false);
        sizeLabelTMP = headerObj.AddComponent<TextMeshProUGUI>();
        sizeLabelTMP.font = WelcomeScreenController.GetSafeFont();
        string rotInstruction = lockBoardOrientationToUser
            ? "<size=14><color=#94A3B8>Đã khóa hướng chính diện</color></size>"
            : "<size=14><color=#38BDF8>Xoay 2 ngón tay trên màn hình để căn góc bàn</color></size>";
        sizeLabelTMP.text = $"<b>THIẾT LẬP KÍCH THƯỚC BÀN MẠCH</b>   {rotInstruction}";
        sizeLabelTMP.fontSize = 17;
        sizeLabelTMP.alignment = TextAlignmentOptions.Center;
        sizeLabelTMP.color = Color.white;

        // Hàng 1: Thanh kéo Chiều Rộng (Width Slider)
        widthSlider = CreateModernSliderRow(panelObj.transform, "Chiều Rộng (Ngang)", 0.30f, 2.00f, defaultBoardWidth, new Color(0.0f, 0.85f, 1f), (val) =>
        {
            defaultBoardWidth = val;
            UpdateWidthLabel(val);
            UpdateBoardScale();
            UpdatePresetHighlights(defaultBoardWidth, defaultBoardDepth);
        }, out widthValueTMP);

        // Hàng 2: Thanh kéo Chiều Dài / Sâu (Depth Slider)
        depthSlider = CreateModernSliderRow(panelObj.transform, "Chiều Dài (Dọc)", 0.30f, 1.50f, defaultBoardDepth, new Color(0.15f, 0.90f, 0.60f), (val) =>
        {
            defaultBoardDepth = val;
            UpdateDepthLabel(val);
            UpdateBoardScale();
            UpdatePresetHighlights(defaultBoardWidth, defaultBoardDepth);
        }, out depthValueTMP);

        // Vùng đệm cách biệt an toàn giữa thanh trượt Chiều Dài và hàng nút mẫu (ngăn chạm nhầm)
        GameObject spacerObj = new GameObject("Spacer_Depth_Presets", typeof(RectTransform));
        spacerObj.transform.SetParent(panelObj.transform, false);
        LayoutElement spacerLe = spacerObj.AddComponent<LayoutElement>();
        spacerLe.minHeight = 10;
        spacerLe.preferredHeight = 10;

        // Hàng 3: Kích thước mẫu nhanh (Presets Chips)
        presetButtonBgs.Clear();
        presetButtonOutlines.Clear();
        presetSizes.Clear();

        GameObject rowPresets = CreateRow(panelObj.transform, 8);
        CreatePresetChip(rowPresets.transform, "40×30", 0.40f, 0.30f);
        CreatePresetChip(rowPresets.transform, "60×40", 0.60f, 0.40f);
        CreatePresetChip(rowPresets.transform, "80×60", 0.80f, 0.60f);
        CreatePresetChip(rowPresets.transform, "100×70", 1.00f, 0.70f);
        CreatePresetChip(rowPresets.transform, "120×80", 1.20f, 0.80f);
        UpdatePresetHighlights(defaultBoardWidth, defaultBoardDepth);

        // Hàng 4: NÚT XÁC NHẬN CỐ ĐỊNH HOÀN TOÀN
        GameObject rowConfirm = CreateRow(panelObj.transform, 0);
        Button confirmBtn = CreateConfirmButton(rowConfirm.transform, "XÁC NHẬN CỐ ĐỊNH BÀN MẠCH", ConfirmAndLockPlacement);
        LayoutElement confirmLe = confirmBtn.gameObject.AddComponent<LayoutElement>();
        confirmLe.minHeight = 50;

        // Hiệu ứng trượt slide-up êm ái khi vừa xuất hiện
        StartCoroutine(AnimateToolbarSlideUp(panelRt));
    }

    private Slider CreateModernSliderRow(Transform parent, string title, float min, float max, float currentVal, Color fillColor, System.Action<float> onValueChange, out TextMeshProUGUI valueLabel)
    {
        GameObject rowObj = new GameObject("SliderRow_" + title, typeof(RectTransform));
        rowObj.transform.SetParent(parent, false);
        VerticalLayoutGroup vlg = rowObj.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 3;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // Top info subrow
        GameObject infoRow = new GameObject("InfoRow", typeof(RectTransform));
        infoRow.transform.SetParent(rowObj.transform, false);
        HorizontalLayoutGroup hlg = infoRow.AddComponent<HorizontalLayoutGroup>();
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        // Title
        GameObject titleObj = new GameObject("Title", typeof(RectTransform));
        titleObj.transform.SetParent(infoRow.transform, false);
        LayoutElement titleLe = titleObj.AddComponent<LayoutElement>();
        titleLe.minWidth = 140;
        titleLe.flexibleWidth = 1;
        TextMeshProUGUI titleTMP = titleObj.AddComponent<TextMeshProUGUI>();
        titleTMP.font = WelcomeScreenController.GetSafeFont();
        titleTMP.text = title;
        titleTMP.fontSize = 16;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.color = new Color(0.92f, 0.96f, 1f);

        // Value Badge (Micro Pill Badge thanh thoát)
        GameObject badgeObj = new GameObject("Badge", typeof(RectTransform), typeof(Image));
        badgeObj.transform.SetParent(infoRow.transform, false);
        LayoutElement badgeLe = badgeObj.AddComponent<LayoutElement>();
        badgeLe.minWidth = 125;
        badgeLe.preferredWidth = 125;
        badgeLe.minHeight = 22;
        badgeLe.preferredHeight = 22;

        Image badgeImg = badgeObj.GetComponent<Image>();
        badgeImg.sprite = fineTuneRoundedRectSprite;
        badgeImg.type = Image.Type.Sliced;
        badgeImg.color = new Color(0.08f, 0.13f, 0.22f, 0.9f);

        Outline badgeOutline = badgeObj.AddComponent<Outline>();
        badgeOutline.effectColor = new Color(fillColor.r, fillColor.g, fillColor.b, 0.45f);
        badgeOutline.effectDistance = new Vector2(1f, -1f);

        GameObject badgeTxtObj = new GameObject("Txt", typeof(RectTransform));
        badgeTxtObj.transform.SetParent(badgeObj.transform, false);
        RectTransform btRT = badgeTxtObj.GetComponent<RectTransform>();
        btRT.anchorMin = Vector2.zero;
        btRT.anchorMax = Vector2.one;
        btRT.offsetMin = Vector2.zero;
        btRT.offsetMax = Vector2.zero;

        valueLabel = badgeTxtObj.AddComponent<TextMeshProUGUI>();
        valueLabel.font = WelcomeScreenController.GetSafeFont();
        valueLabel.text = $"{currentVal:F2}m <size=11.5><color=#94A3B8>({Mathf.RoundToInt(currentVal * 100)}cm)</color></size>";
        valueLabel.fontSize = 13.5f;
        valueLabel.fontStyle = FontStyles.Bold;
        valueLabel.alignment = TextAlignmentOptions.Center;
        valueLabel.color = fillColor;

        // Slider component
        GameObject sliderObj = new GameObject("Slider", typeof(RectTransform));
        sliderObj.transform.SetParent(rowObj.transform, false);
        RectTransform sliderRt = sliderObj.GetComponent<RectTransform>();
        sliderRt.sizeDelta = new Vector2(0, 30);
        LayoutElement sliderLe = sliderObj.AddComponent<LayoutElement>();
        sliderLe.minHeight = 30;
        sliderLe.preferredHeight = 30;

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.transition = Selectable.Transition.ColorTint;

        // Background Track (lùi vào 16px mỗi bên để nút kéo không chạm rìa ngoài)
        GameObject bgObj = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bgObj.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRt = bgObj.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.40f);
        bgRt.anchorMax = new Vector2(1f, 0.60f);
        bgRt.offsetMin = new Vector2(16, 0);
        bgRt.offsetMax = new Vector2(-16, 0);
        Image bgImg = bgObj.GetComponent<Image>();
        bgImg.sprite = fineTuneRoundedRectSprite;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color(0.08f, 0.12f, 0.20f, 1f);

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRt = fillArea.GetComponent<RectTransform>();
        fillAreaRt.anchorMin = new Vector2(0f, 0.40f);
        fillAreaRt.anchorMax = new Vector2(1f, 0.60f);
        fillAreaRt.offsetMin = new Vector2(20, 0);
        fillAreaRt.offsetMax = new Vector2(-20, 0);

        // Fill
        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        Image fillImg = fill.GetComponent<Image>();
        fillImg.sprite = fineTuneRoundedRectSprite;
        fillImg.type = Image.Type.Sliced;
        fillImg.color = fillColor;

        // Handle Area: lùi vào 26px mỗi bên để con trượt 30px dừng thoải mái trong lề an toàn
        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRt = handleArea.GetComponent<RectTransform>();
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = new Vector2(26, 0);
        handleAreaRt.offsetMax = new Vector2(-26, 0);

        // Handle: kích thước 30x30px chuẩn công thái học ngón tay, viền neon
        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform handleRt = handle.GetComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(30, 30);
        Image handleImg = handle.GetComponent<Image>();
        handleImg.sprite = fineTuneCircleSprite;
        handleImg.color = Color.white;

        Outline handleOutline = handle.AddComponent<Outline>();
        handleOutline.effectColor = fillColor;
        handleOutline.effectDistance = new Vector2(2f, -2f);

        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = currentVal;

        slider.onValueChanged.AddListener(val =>
        {
            onValueChange?.Invoke(val);
        });

        return slider;
    }

    private void CreatePresetChip(Transform parent, string label, float w, float d)
    {
        GameObject chipObj = new GameObject("Chip_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        chipObj.transform.SetParent(parent, false);

        LayoutElement le = chipObj.AddComponent<LayoutElement>();
        le.minHeight = 36;
        le.preferredHeight = 36;

        Image img = chipObj.GetComponent<Image>();
        img.sprite = fineTuneRoundedRectSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(0.09f, 0.14f, 0.24f, 0.95f);

        Outline outline = chipObj.AddComponent<Outline>();
        outline.effectColor = new Color(0.2f, 0.3f, 0.45f, 0.5f);
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        Button btn = chipObj.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => ApplySizePreset(w, d));

        GameObject txtObj = new GameObject("Txt", typeof(RectTransform));
        txtObj.transform.SetParent(chipObj.transform, false);
        RectTransform txtRt = txtObj.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;

        TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
        txt.font = WelcomeScreenController.GetSafeFont();
        txt.text = label + "<size=11><color=#94A3B8>cm</color></size>";
        txt.fontSize = 15;
        txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = Color.white;

        presetButtonBgs.Add(img);
        presetButtonOutlines.Add(outline);
        presetSizes.Add(new Vector2(w, d));
    }

    private Button CreateConfirmButton(Transform parent, string label, System.Action onClick)
    {
        GameObject btnObj = new GameObject("Btn_ConfirmPlacement", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent, false);

        Image img = btnObj.GetComponent<Image>();
        img.sprite = fineTuneRoundedRectSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(0.02f, 0.68f, 0.46f, 0.95f); // Emerald/Mint gradient base

        Outline outline = btnObj.AddComponent<Outline>();
        outline.effectColor = new Color(0.35f, 1f, 0.78f, 0.90f); // Bright Mint glow
        outline.effectDistance = new Vector2(2f, -2f);

        Button btn = btnObj.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick?.Invoke());

        HorizontalLayoutGroup hlg = btnObj.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 10;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;

        if (fineTuneCheckSprite != null)
        {
            GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(btnObj.transform, false);
            RectTransform iconRt = iconObj.GetComponent<RectTransform>();
            iconRt.sizeDelta = new Vector2(24, 24);
            Image iconImg = iconObj.GetComponent<Image>();
            iconImg.sprite = fineTuneCheckSprite;
            iconImg.color = Color.white;
            iconImg.raycastTarget = false;
        }

        GameObject txtObj = new GameObject("Txt", typeof(RectTransform));
        txtObj.transform.SetParent(btnObj.transform, false);
        ContentSizeFitter csf = txtObj.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
        txt.font = WelcomeScreenController.GetSafeFont();
        txt.text = label;
        txt.fontSize = 18;
        txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = Color.white;
        txt.characterSpacing = 1.2f;

        return btn;
    }

    private IEnumerator AnimateToolbarSlideUp(RectTransform panelRt)
    {
        if (panelRt == null) yield break;

        float duration = 0.28f;
        float elapsed = 0f;
        Vector2 startPos = new Vector2(0f, -650f);
        Vector2 endPos = Vector2.zero;
        panelRt.anchoredPosition = startPos;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f + 0.8f * Mathf.Pow(t - 1f, 3) + 0.8f * Mathf.Pow(t - 1f, 2);
            panelRt.anchoredPosition = Vector2.LerpUnclamped(startPos, endPos, ease);
            yield return null;
        }
        panelRt.anchoredPosition = endPos;
    }

    private void UpdateWidthLabel(float w)
    {
        if (widthValueTMP != null)
        {
            widthValueTMP.text = $"{w:F2}m <size=11.5><color=#94A3B8>({Mathf.RoundToInt(w * 100)}cm)</color></size>";
        }
    }

    private void UpdateDepthLabel(float d)
    {
        if (depthValueTMP != null)
        {
            depthValueTMP.text = $"{d:F2}m <size=11.5><color=#94A3B8>({Mathf.RoundToInt(d * 100)}cm)</color></size>";
        }
    }

    private void UpdatePresetHighlights(float w, float d)
    {
        for (int i = 0; i < presetSizes.Count; i++)
        {
            if (i >= presetButtonBgs.Count || i >= presetButtonOutlines.Count) break;

            bool isMatch = Mathf.Abs(presetSizes[i].x - w) < 0.02f && Mathf.Abs(presetSizes[i].y - d) < 0.02f;
            if (isMatch)
            {
                presetButtonBgs[i].color = new Color(0.12f, 0.24f, 0.40f, 0.98f);
                presetButtonOutlines[i].effectColor = new Color(0f, 0.95f, 1f, 0.95f);
                presetButtonOutlines[i].effectDistance = new Vector2(2f, -2f);
            }
            else
            {
                presetButtonBgs[i].color = new Color(0.08f, 0.13f, 0.22f, 0.92f);
                presetButtonOutlines[i].effectColor = new Color(0.2f, 0.3f, 0.42f, 0.45f);
                presetButtonOutlines[i].effectDistance = new Vector2(1f, -1f);
            }
        }
    }

    private GameObject CreateRow(Transform parent, int spacing)
    {
        GameObject row = new GameObject("Row");
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = spacing;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        return row;
    }

    private Button CreateButtonInRow(Transform parent, string label, Color bgColor, Font font, System.Action onClick)
    {
        GameObject btnObj = new GameObject("Btn_" + label);
        btnObj.transform.SetParent(parent, false);

        Image img = btnObj.AddComponent<Image>();
        img.color = bgColor;

        Button btn = btnObj.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick?.Invoke());

        GameObject txtObj = new GameObject("Txt");
        txtObj.transform.SetParent(btnObj.transform, false);
        RectTransform txtRt = txtObj.AddComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;

        Text txt = txtObj.AddComponent<Text>();
        txt.text = label;
        txt.fontSize = 18;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        if (font != null) txt.font = font;

        return btn;
    }

    public void ApplySizePreset(float w, float d)
    {
        defaultBoardWidth = w;
        defaultBoardDepth = d;
        if (widthSlider != null) widthSlider.value = w;
        if (depthSlider != null) depthSlider.value = d;
        UpdateWidthLabel(w);
        UpdateDepthLabel(d);
        UpdateBoardScale();
        UpdatePresetHighlights(w, d);
    }

    private void UpdateBoardScale()
    {
        if (ActiveBoard != null)
        {
            ActiveBoard.transform.localScale = new Vector3(defaultBoardWidth, defaultBoardThickness, defaultBoardDepth);
        }
    }

    private static void EnsureFineTuneSprites()
    {
        if (fineTuneCircleSprite == null) fineTuneCircleSprite = GenerateCircleSprite(64);
        if (fineTuneRoundedRectSprite == null) fineTuneRoundedRectSprite = GenerateRoundedRectSprite(128, 128, 32);
        if (fineTuneCheckSprite == null) fineTuneCheckSprite = GenerateCheckIconSprite(64);
    }

    private static Sprite GenerateCircleSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.48f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                if (d <= radius)
                {
                    float a = Mathf.Clamp01((radius - d) * 1.5f);
                    colors[y * size + x] = new Color(1f, 1f, 1f, a);
                }
                else
                {
                    colors[y * size + x] = Color.clear;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static Sprite GenerateRoundedRectSprite(int width, int height, int radius)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] colors = new Color[width * height];
        float r = radius;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x + 0.5f - width * 0.5f) - (width * 0.5f - r));
                float dy = Mathf.Max(0, Mathf.Abs(y + 0.5f - height * 0.5f) - (height * 0.5f - r));
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > r)
                {
                    colors[y * width + x] = Color.clear;
                }
                else if (dist > r - 1.5f)
                {
                    colors[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - dist));
                }
                else
                {
                    colors[y * width + x] = Color.white;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        Vector4 border = new Vector4(radius, radius, radius, radius);
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }

    private static Sprite GenerateCheckIconSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.clear;

        Vector2 p1 = new Vector2(16, 32);
        Vector2 p2 = new Vector2(27, 18);
        Vector2 p3 = new Vector2(48, 44);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 pt = new Vector2(x, y);
                float dist1 = DistanceToLineSegment(pt, p1, p2);
                float dist2 = DistanceToLineSegment(pt, p2, p3);
                float d = Mathf.Min(dist1, dist2);

                if (d <= 3.8f)
                {
                    float a = Mathf.Clamp01((3.8f - d) * 1.5f);
                    colors[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static float DistanceToLineSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 pa = p - a;
        Vector2 ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude;
    }

    // Các hàm tương thích ngược giữ nguyên interface
    public void AdjustPitch(float deltaDegrees) { }
    public void AdjustRoll(float deltaDegrees) { }
    public void AdjustYaw(float deltaDegrees)
    {
        if (lockBoardOrientationToUser) return;

        if (ActiveBoardAnchor != null)
        {
            lockedWorldRot = Quaternion.AngleAxis(deltaDegrees, Vector3.up) * lockedWorldRot;
            ActiveBoardAnchor.transform.rotation = lockedWorldRot;
        }
    }

    /// <summary>
    /// Xác nhận khóa vĩnh viễn bàn mạch trong không gian thực:
    /// - Chốt World Pose cố định.
    /// - Dừng nhận cử chỉ xoay.
    /// - Tắt phát hiện mặt phẳng mới để dồn 100% CPU cho MediaPipe.
    /// </summary>
    public void ConfirmAndLockPlacement()
    {
        isPermanentlyLocked = true;
        isAdjusting = false;

        if (ActiveBoardAnchor != null)
        {
            lockedWorldPos = ActiveBoardAnchor.transform.position;
            lockedWorldRot = FlattenRotation(ActiveBoardAnchor.transform.rotation);

            // Keep content outside the AR trackable hierarchy. A detected plane can be
            // subsumed or removed while it is outside the camera's field of view.
            ActiveBoardAnchor.transform.SetParent(null, true);
            ActiveBoardAnchor.transform.SetPositionAndRotation(lockedWorldPos, lockedWorldRot);
        }

        // Keep the standalone anchor alive after confirmation. ARCore can then apply
        // the same relocalization correction to the board when tracking recovers.
        if (nativeAnchor != null && !hasAnchorBinding) BindBoardToAnchor();

        if (lockController != null)
        {
            lockController.SetManualLocked(currentPlaneId);
        }

        if (fineTuneUIRoot != null)
        {
            Destroy(fineTuneUIRoot);
            fineTuneUIRoot = null;
        }

        Debug.Log("<color=green>[TwoPointSpatialCalibrator]</color> ĐÃ XÁC NHẬN VÀ KHÓA VĨNH VIỄN BÀN MẠCH Ở TOẠ ĐỘ THẾ GIỚI!");
    }

    public void OnTriggerPinch()
    {
        if (isAdjusting) return;
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        TryExecutePlacement(screenCenter);
    }

    private void SetText(string content)
    {
        if (btnLabelTMP != null) btnLabelTMP.text = content;
        if (btnLabelLegacy != null) btnLabelLegacy.text = content;
    }

    private IEnumerator FlashWarningRoutine(string message)
    {
        isWarningActive = true;
        SetText(message);
        if (floatingHUD != null)
        {
            floatingHUD.ShowWarning("Chưa bắt được mặt bàn", "Hãy lia camera vào vùng có ánh sáng");
        }
        yield return new WaitForSeconds(2.0f);
        isWarningActive = false;
        if (!isPlaced && floatingHUD != null)
        {
            floatingHUD.ShowScanning();
        }
    }

    /// <summary>
    /// Khởi động lại toàn bộ quá trình quét và đặt bàn mạch
    /// </summary>
    public void RestartCalibration()
    {
        isPermanentlyLocked = false;
        anchorRequestVersion++;
        if (nativeAnchor != null) Destroy(nativeAnchor.gameObject);
        nativeAnchor = null;
        hasAnchorBinding = false;
        if (ActiveBoard != null) Destroy(ActiveBoard);
        if (ActiveBoardAnchor != null) Destroy(ActiveBoardAnchor);
        if (fineTuneUIRoot != null) Destroy(fineTuneUIRoot);

        GameObject existingAnchor = GameObject.Find("Board_Anchor");
        if (existingAnchor != null) Destroy(existingAnchor);
        GameObject existingBoard = GameObject.Find("ActiveCircuitBoard");
        if (existingBoard != null) Destroy(existingBoard);

        isPlaced = false;
        isAdjusting = false;
        isWarningActive = false;
        enabled = true;

        if (lockController != null)
        {
            lockController.UnlockAndRescan();
        }

        if (planeManager != null)
        {
            planeManager.enabled = true;
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        }

        if (spatialCalibrationUI != null) spatialCalibrationUI.SetActive(true);
        if (reticleUI != null) reticleUI.SetActive(true);
        if (holographicReticle != null) holographicReticle.Hide();
        if (floatingHUD != null) floatingHUD.ShowScanning();

        SetText("HÃY HƯỚNG CAMERA VÀO MẶT BÀN ĐỂ QUÉT...");
        Debug.Log("<color=cyan>[TwoPointSpatialCalibrator]</color> Đã khởi động lại chế độ quét mặt bàn.");
    }

    private static Quaternion FlattenRotation(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private static Vector3 GetHorizontalDirectionFromUser(Vector3 targetPosition)
    {
        Camera camera = Camera.main;
        Vector3 direction = camera != null
            ? targetPosition - camera.transform.position
            : Vector3.forward;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f && camera != null)
        {
            direction = camera.transform.forward;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
        return direction.normalized;
    }
}
