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
    private Text sizeLabelText;

    private Slider widthSlider;
    private Slider depthSlider;
    private Text widthValLabel;
    private Text depthValLabel;

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
    }

    void OnEnable()
    {
        ARSession.stateChanged += OnARSessionStateChanged;
    }

    void OnDisable()
    {
        ARSession.stateChanged -= OnARSessionStateChanged;
    }

    void Start()
    {
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
            if (reticleUI != null)
            {
                if (!reticleUI.activeSelf) reticleUI.SetActive(true);

                // Khóa phẳng lì theo phương thẳng đứng chuẩn trọng lực
                reticleUI.transform.position = hitPose.position + Vector3.up * 0.002f;
                reticleUI.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            if (!isWarningActive)
            {
                SetText("ĐÃ THẤY MẶT BÀN!\nCHẠM ĐỂ ĐẶT BÀN MẠCH PHẲNG");
            }
        }
        else
        {
            if (reticleUI != null && reticleUI.activeSelf)
            {
                reticleUI.SetActive(false);
            }

            if (!isWarningActive)
            {
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
    /// Tạo thanh công cụ tinh chỉnh kích thước cực kỳ tinh gọn và sang trọng
    /// Kèm dòng nhắc nhở cử chỉ xoay tự nhiên: "Vuốt hoặc xoay 2 ngón tay trên màn hình để xoay bàn"
    /// </summary>
    private void ShowFineTuneToolbar()
    {
        if (fineTuneUIRoot != null) Destroy(fineTuneUIRoot);

        fineTuneUIRoot = new GameObject("FineTune_Canvas");
        Canvas canvas = fineTuneUIRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        CanvasScaler scaler = fineTuneUIRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        fineTuneUIRoot.AddComponent<GraphicRaycaster>();

        // Khung nền chính ở mép dưới màn hình (gọn gàng, chỉ chiếm 29% màn hình)
        GameObject panelObj = new GameObject("FineTune_Panel");
        panelObj.transform.SetParent(fineTuneUIRoot.transform, false);

        RectTransform panelRt = panelObj.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.03f, 0.01f);
        panelRt.anchorMax = new Vector2(0.97f, 0.29f);
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;

        Image panelImg = panelObj.AddComponent<Image>();
        panelImg.color = new Color(0.06f, 0.09f, 0.15f, 0.96f);

        VerticalLayoutGroup vlg = panelObj.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 10, 10);
        vlg.spacing = 6;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        Font customFont = (btnLabelLegacy != null) ? btnLabelLegacy.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Hàng 0: Tiêu đề + Hướng dẫn cử chỉ xoay
        GameObject headerObj = new GameObject("Header");
        headerObj.transform.SetParent(panelObj.transform, false);
        sizeLabelText = headerObj.AddComponent<Text>();
        sizeLabelText.text = lockBoardOrientationToUser
            ? "📏 KÍCH THƯỚC BÀN MẠCH (ĐÃ KHÓA HƯỚNG)"
            : "📏 KÍCH THƯỚC BÀN MẠCH (XOAY 2 NGÓN TAY ĐỂ CĂN)";
        sizeLabelText.fontSize = 20;
        sizeLabelText.fontStyle = FontStyle.Bold;
        sizeLabelText.alignment = TextAnchor.MiddleCenter;
        sizeLabelText.color = new Color(0.0f, 0.9f, 1f);
        if (customFont != null) sizeLabelText.font = customFont;

        // Hàng 1: Thanh kéo Chiều Rộng (Width Slider)
        widthSlider = CreateSliderRow(panelObj.transform, "↔ Chiều Rộng", 0.30f, 2.00f, defaultBoardWidth, customFont, new Color(0.0f, 0.8f, 0.95f), (val) =>
        {
            defaultBoardWidth = val;
            UpdateBoardScale();
        }, out widthValLabel);

        // Hàng 2: Thanh kéo Chiều Dài / Sâu (Depth Slider)
        depthSlider = CreateSliderRow(panelObj.transform, "↕ Chiều Dài", 0.30f, 1.50f, defaultBoardDepth, customFont, new Color(0.2f, 0.85f, 0.6f), (val) =>
        {
            defaultBoardDepth = val;
            UpdateBoardScale();
        }, out depthValLabel);

        // Hàng 3: Kích thước mẫu nhanh (Presets)
        GameObject rowPresets = CreateRow(panelObj.transform, 6);
        CreateButtonInRow(rowPresets.transform, "40×30cm", new Color(0.20f, 0.20f, 0.32f), customFont, () => ApplySizePreset(0.40f, 0.30f));
        CreateButtonInRow(rowPresets.transform, "60×40cm", new Color(0.20f, 0.20f, 0.32f), customFont, () => ApplySizePreset(0.60f, 0.40f));
        CreateButtonInRow(rowPresets.transform, "80×60cm", new Color(0.20f, 0.20f, 0.32f), customFont, () => ApplySizePreset(0.80f, 0.60f));
        CreateButtonInRow(rowPresets.transform, "100×70cm", new Color(0.20f, 0.20f, 0.32f), customFont, () => ApplySizePreset(1.00f, 0.70f));
        CreateButtonInRow(rowPresets.transform, "120×80cm", new Color(0.20f, 0.20f, 0.32f), customFont, () => ApplySizePreset(1.20f, 0.80f));

        // Hàng 4: NÚT XÁC NHẬN CỐ ĐỊNH HOÀN TOÀN
        GameObject rowConfirm = CreateRow(panelObj.transform, 0);
        Button confirmBtn = CreateButtonInRow(rowConfirm.transform, "✔ XÁC NHẬN CỐ ĐỊNH BÀN MẠCH", new Color(0.00f, 0.76f, 0.38f), customFont, ConfirmAndLockPlacement);
        LayoutElement confirmLe = confirmBtn.gameObject.AddComponent<LayoutElement>();
        confirmLe.minHeight = 48;
    }

    private Slider CreateSliderRow(Transform parent, string title, float min, float max, float currentVal, Font font, Color fillColor, System.Action<float> onValueChange, out Text valueLabel)
    {
        GameObject row = new GameObject("SliderRow_" + title);
        row.transform.SetParent(parent, false);
        VerticalLayoutGroup vlg = row.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        valueLabel = labelObj.AddComponent<Text>();
        valueLabel.fontSize = 18;
        valueLabel.fontStyle = FontStyle.Bold;
        valueLabel.alignment = TextAnchor.MiddleLeft;
        valueLabel.color = Color.white;
        if (font != null) valueLabel.font = font;
        valueLabel.text = $"{title}: {currentVal:F2}m ({Mathf.RoundToInt(currentVal * 100)}cm)";

        GameObject sliderObj = new GameObject("Slider");
        sliderObj.transform.SetParent(row.transform, false);
        RectTransform sliderRt = sliderObj.AddComponent<RectTransform>();
        sliderRt.sizeDelta = new Vector2(0, 26);
        LayoutElement le = sliderObj.AddComponent<LayoutElement>();
        le.minHeight = 26;
        le.preferredHeight = 26;

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.transition = Selectable.Transition.ColorTint;

        // Background
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRt = bgObj.AddComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.30f);
        bgRt.anchorMax = new Vector2(1f, 0.70f);
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0.12f, 0.16f, 0.22f, 1f);

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
        fillAreaRt.anchorMin = new Vector2(0f, 0.30f);
        fillAreaRt.anchorMax = new Vector2(1f, 0.70f);
        fillAreaRt.offsetMin = new Vector2(5, 0);
        fillAreaRt.offsetMax = new Vector2(-5, 0);

        // Fill
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRt = fill.AddComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = fillColor;

        // Handle Area
        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRt = handleArea.AddComponent<RectTransform>();
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = new Vector2(14, 0);
        handleAreaRt.offsetMax = new Vector2(-14, 0);

        // Handle
        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform handleRt = handle.AddComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(26, 26);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = Color.white;

        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = currentVal;

        Text localLabel = valueLabel;
        slider.onValueChanged.AddListener(val =>
        {
            if (localLabel != null)
                localLabel.text = $"{title}: {val:F2}m ({Mathf.RoundToInt(val * 100)}cm)";
            onValueChange?.Invoke(val);
        });

        return slider;
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
        if (widthValLabel != null)
            widthValLabel.text = $"↔ Chiều Rộng: {w:F2}m ({Mathf.RoundToInt(w * 100)}cm)";
        if (depthValLabel != null)
            depthValLabel.text = $"↕ Chiều Dài: {d:F2}m ({Mathf.RoundToInt(d * 100)}cm)";
        UpdateBoardScale();
    }

    private void UpdateBoardScale()
    {
        if (ActiveBoard != null)
        {
            ActiveBoard.transform.localScale = new Vector3(defaultBoardWidth, defaultBoardThickness, defaultBoardDepth);
        }
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
        yield return new WaitForSeconds(2.0f);
        isWarningActive = false;
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
