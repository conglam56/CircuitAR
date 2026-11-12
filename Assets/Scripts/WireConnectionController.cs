using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Quản lý tính năng nối dây điện giữa các điểm cực (Terminal) thông qua cử chỉ Pinch (Hand Tracking MediaPipe).
/// - Thân dây uốn lượn hình vòng cung mềm mại tự nhiên (Cubic Bézier Arch & Sag), thanh thoát chuẩn dây điện thí nghiệm.
/// - Đầu giắc cắm bắp chuối (Lab Banana Plug) sang trọng, ôm khít nắp cọc, có vòng đệm kim loại sáng bóng, không bị trồi lơ lửng.
/// - Tự động đồng bộ màu sắc dây và giắc cắm theo cực: Cực Đỏ -> Dây Đỏ, Cực Đen -> Dây Đen.
/// - Đồng bộ vị trí, độ cong và góc xoay theo thời gian thực khi kéo di chuyển linh kiện trên bàn AR.
/// </summary>
[DefaultExecutionOrder(150)]
public class WireConnectionController : MonoBehaviour
{
    [Header("--- THAM CHIẾU HỆ THỐNG ---")]
    [Tooltip("Kéo GestureManager (chứa ButtonInteractor) vào đây")]
    public ButtonInteractor buttonInteractor;

    [Tooltip("Kéo GestureManager (chứa MenuHUDController) vào đây")]
    public MenuHUDController menuHUD;

    [Tooltip("Tham chiếu tới TapToPlaceController để đồng bộ lịch sử Undo/Redo")]
    public TapToPlaceController tapToPlaceController;

    [Header("--- KIỂU DÁNG GIẮC CẮM ---")]
    [Tooltip("BẬT: Dùng giắc cắm bắp chuối chuẩn phòng thí nghiệm (rất đẹp, ôm khít cọc, không bị trồi vuốt). TẮT: Dùng kẹp cá sấu từ prefab.")]
    public bool useLabPlugStyle = true;

    [Header("--- CẤU HÌNH DÂY NỐI 3D MỀM MẠI ---")]
    [Tooltip("Material dùng cho dây. Mặc định tự động dùng Assets/Materials/Rope.mat")]
    public Material wireMaterial;

    [Tooltip("Màu sắc mặc định của dây nếu không tự nhận diện cực")]
    public Color wireColor = new Color(0.9f, 0.15f, 0.15f, 1f); // Đỏ tươi chuẩn dây điện

    [Tooltip("Tự động đổi màu dây và giắc theo cực (Cực Đỏ -> Dây Đỏ, Cực Đen -> Dây Đen)")]
    public bool autoColorByTerminal = true;

    [Tooltip("Bán kính của thân dây (mặc định 0.0022m = 2.2mm, tạo đường kính dây 4.4mm thanh thoát, sắc nét)")]
    public float wireRadius = 0.0022f;

    [Tooltip("Độ vồng uốn lượn tự nhiên của dây nhô lên từ cọc (mặc định 0.025m = 2.5cm)")]
    public float archHeight = 0.025f;

    [Tooltip("Hệ số vồng theo khoảng cách giữa 2 cực")]
    [Range(0.05f, 0.5f)]
    public float archFactor = 0.22f;

    [Tooltip("Số cạnh xung quanh để tạo hình ống trụ tròn 3D (8-12 cạnh là tròn đều mượt mà)")]
    [Range(6, 16)]
    public int radialSegments = 10;

    [Tooltip("Số điểm uốn cong dọc theo thân dây (18-28 điểm cho đường cong mượt)")]
    [Range(10, 36)]
    public int curveSegments = 22;

    [Tooltip("Độ lặp lại của texture dọc theo mét chiều dài thân dây")]
    public float textureTilingPerMeter = 25f;

    [Tooltip("Transform cha chứa tất cả các dây để giữ Hierarchy gọn gàng")]
    public Transform wiresContainer;

    [Header("--- ĐẦU GIẮC CẮM PREFAB (KHI TẮT LAB PLUG STYLE) ---")]
    [Tooltip("Prefab đầu cắm cực 1 (mặc định Cylinder015)")]
    public GameObject connectorPrefabA;

    [Tooltip("Prefab đầu cắm cực 2 (mặc định Cylinder016)")]
    public GameObject connectorPrefabB;

    [Tooltip("Tỉ lệ kích thước đầu giắc cắm prefab")]
    public float connectorScale = 0.012f;

    [Tooltip("Góc bù xoay cho đầu giắc cắm A")]
    public Vector3 connectorRotationOffsetA = new Vector3(0f, 0f, 0f);

    [Tooltip("Góc bù xoay cho đầu giắc cắm B")]
    public Vector3 connectorRotationOffsetB = new Vector3(0f, 180f, 0f);

    [Header("--- CẤU HÌNH HIGHLIGHT CỰC ---")]
    [Tooltip("Màu phát sáng/đổi màu khi cực được chọn làm điểm bắt đầu")]
    public Color highlightColor = Color.yellow;

    [Header("--- CẤU HÌNH RAYCAST ---")]
    public float maxRaycastDistance = 15f;

    // Chiều cao giắc cắm bắp chuối chuẩn phòng thí nghiệm
    private const float LAB_PLUG_HEIGHT = 0.011f; // 1.1cm

    // --- DỮ LIỆU KẾT NỐI ---
    [System.Serializable]
    public class WireRecord
    {
        public Transform terminalA;
        public Transform terminalB;
        public GameObject wireObject;
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public Mesh wireMesh;
        public GameObject pivotA;
        public GameObject pivotB;
        public GameObject connectorA;
        public GameObject connectorB;
        public Color actualWireColor;

        // Per-wire buffers are retained while dragging; idle frames do not upload
        // unchanged tube geometry or allocate fresh vertex/index arrays.
        [System.NonSerialized] internal WireGeometryInputs geometryInputs;
        [System.NonSerialized] internal bool hasGeometry;
        [System.NonSerialized] internal int geometryRevision;
        [System.NonSerialized] internal Vector3[] curvePoints, tangents, normals, binormals, vertices, meshNormals;
        [System.NonSerialized] internal Vector2[] uvs;
        [System.NonSerialized] internal int[] triangles;

        public WireRecord(Transform a, Transform b, GameObject obj, MeshFilter mf, MeshRenderer mr, Mesh mesh, GameObject pA, GameObject pB, GameObject cA, GameObject cB, Color col)
        {
            terminalA = a;
            terminalB = b;
            wireObject = obj;
            meshFilter = mf;
            meshRenderer = mr;
            wireMesh = mesh;
            pivotA = pA;
            pivotB = pB;
            connectorA = cA;
            connectorB = cB;
            actualWireColor = col;
        }
    }

    internal struct WireGeometryInputs : System.IEquatable<WireGeometryInputs>
    {
        public Vector3 baseA, baseB, junctionA, junctionB, rotationA, rotationB;
        public Matrix4x4 worldToLocal;
        public Vector4 shape;
        public Vector2Int segments;
        public int flags;
        public Mesh mesh;
        public bool Equals(WireGeometryInputs other) => baseA.Equals(other.baseA) && baseB.Equals(other.baseB) &&
            junctionA.Equals(other.junctionA) && junctionB.Equals(other.junctionB) &&
            rotationA.Equals(other.rotationA) && rotationB.Equals(other.rotationB) &&
            worldToLocal.Equals(other.worldToLocal) && shape.Equals(other.shape) &&
            segments.Equals(other.segments) && flags == other.flags && mesh == other.mesh;
    }

    [Header("--- DANH SÁCH DÂY ĐANG KẾT NỐI ---")]
    public List<WireRecord> activeWires = new List<WireRecord>();

    [Header("--- CHẾ ĐỘ NỐI DÂY (WIRE MODE) ---")]
    [Tooltip("BẬT: Chế độ nối dây riêng biệt (Pinch cực để nối, không kéo linh kiện). TẮT: Chế độ di chuyển linh kiện.")]
    public bool isWireMode = false;
    public bool IsWireMode
    {
        get => isWireMode;
        set
        {
            if (isWireMode != value)
            {
                isWireMode = value;
                if (!isWireMode) CancelSelection();
                OnWireModeChanged?.Invoke(isWireMode);
                Debug.Log($"<color=cyan>[WireConnectionController]</color> Chế độ nối dây: {(isWireMode ? "BẬT (WIRE MODE)" : "TẮT (MOVE MODE)")}");
            }
        }
    }
    public event System.Action<bool> OnWireModeChanged;

    public enum WireSubMode
    {
        Connect, // Chế độ Nối Dây
        Delete   // Chế độ Xóa Dây
    }

    [Header("--- PHÂN LOẠI CHẾ ĐỘ DÂY (NỐI DÂY / XÓA DÂY) ---")]
    public WireSubMode currentSubMode = WireSubMode.Connect;
    public WireSubMode CurrentSubMode
    {
        get => currentSubMode;
        set
        {
            if (currentSubMode != value)
            {
                currentSubMode = value;
                CancelSelection();
                OnWireSubModeChanged?.Invoke(currentSubMode);
                Debug.Log($"<color=cyan>[WireConnectionController]</color> Đã chuyển chế độ dây: {(currentSubMode == WireSubMode.Connect ? "NỐI DÂY" : "XÓA DÂY")}");
            }
        }
    }
    public event System.Action<WireSubMode> OnWireSubModeChanged;

    [Tooltip("Tham chiếu tới ARFloatingBubbleMenu để hiển thị thông báo toast")]
    public ARFloatingBubbleMenu floatingMenu;

    public void ToggleWireMode()
    {
        IsWireMode = !IsWireMode;
    }

    public void SetWireSubMode(WireSubMode mode)
    {
        CurrentSubMode = mode;
        if (!IsWireMode) IsWireMode = true;
    }

    // Trạng thái nội bộ
    private Camera mainCamera;
    private Transform firstSelectedTerminal = null;
    public bool HasSelectedTerminal => firstSelectedTerminal != null;
    private Renderer firstSelectedRenderer = null;
    private Material[] originalTerminalMaterials;
    private Material terminalHighlightMaterial;
    private Material runtimeDefaultMaterial = null;

#if UNITY_EDITOR
    void Reset()
    {
        AutoAssignAssets();
    }

    void OnValidate()
    {
        AutoAssignAssets();
    }

    private void AutoAssignAssets()
    {
        if (wireMaterial == null)
        {
            wireMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Rope.mat");
        }
        if (connectorPrefabA == null)
        {
            connectorPrefabA = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RefinedModels/Cylinder015.prefab");
        }
        if (connectorPrefabB == null)
        {
            connectorPrefabB = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RefinedModels/Cylinder016.prefab");
        }
    }
#endif

    void Start()
    {
        mainCamera = Camera.main;

        // Cơ chế phòng ngừa lỗi quên gán Inspector
        AutoResolveReferences();

        if (wiresContainer == null)
        {
            GameObject containerObj = new GameObject("Wires_Container");
            containerObj.transform.SetParent(this.transform);
            wiresContainer = containerObj.transform;
        }
    }

    private sealed class WirePinchRequest
    {
        public ButtonInteractor owner;
        public bool pending;
        public float expiresAt;
    }
    private readonly WirePinchRequest primaryWirePinch = new();
    private readonly WirePinchRequest secondaryWirePinch = new();
    private readonly RaycastHit[] terminalHits = new RaycastHit[64];
    private readonly List<Terminal> nearbyTerminals = new();
    private Transform lastGestureTerminal;
    private int lastGestureFrame = -1;

    private void ProcessWirePinch(ButtonInteractor input, WirePinchRequest request)
    {
        if (request.owner != input) { request.owner = input; request.pending = false; }
        if (input == null) return;
        if (input.JustPinched)
        {
            request.pending = input.IsHandVisible && !input.PinchConsumedByUI && !input.IsPointerOverUI;
            request.expiresAt = Time.unscaledTime + 0.18f;
        }
        if (!request.pending) return;
        if (Time.unscaledTime > request.expiresAt || input.PinchConsumedByUI || input.IsPointerOverUI || !input.isPinching)
        {
            request.pending = false;
            return;
        }
        if (!input.IsHandVisible) return; // Never cast from a missing/stale hand.
        Transform terminal = FindTerminalAt(input.CurrentScreenPos);
        if (terminal == null) return;
        request.pending = false; // One successful selection per physical pinch.
        if (lastGestureFrame == Time.frameCount && lastGestureTerminal == terminal) return;
        lastGestureTerminal = terminal;
        lastGestureFrame = Time.frameCount;
        OnTerminalPinched(terminal);
    }

    void Update()
    {
        if ((firstSelectedTerminal == null && originalTerminalMaterials != null) ||
            (firstSelectedTerminal != null && !firstSelectedTerminal.gameObject.activeInHierarchy))
            CancelSelection();

        // Kiểm tra an toàn tham chiếu
        if (buttonInteractor == null)
        {
            AutoResolveReferences();
            if (buttonInteractor == null) return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        // 1. NẾU CHẾ ĐỘ NỐI DÂY ĐANG TẮT (NORMAL / MOVE MODE) -> KHÔNG THỰC HIỆN NỐI DÂY!
        if (!isWireMode)
        {
            primaryWirePinch.pending = secondaryWirePinch.pending = false;
            if (firstSelectedTerminal != null)
            {
                CancelSelection();
            }
            return;
        }

        // 2. Nếu đang mở menu đặt linh kiện mới, huỷ thao tác nối dây dở dang
        if (menuHUD != null && menuHUD.HasSelection)
        {
            primaryWirePinch.pending = secondaryWirePinch.pending = false;
            if (firstSelectedTerminal != null)
            {
                CancelSelection();
            }
            return;
        }

        // Keep a short intent window while the fingers finish closing. A miss
        // on the first sample must not force another open/close cycle.
        ProcessWirePinch(buttonInteractor, primaryWirePinch);
        ProcessWirePinch(buttonInteractor.SecondHand, secondaryWirePinch);
    }

    void LateUpdate()
    {
        // 4. Đồng bộ tọa độ, đường cong vồng mềm mại và 2 đầu giắc cắm theo thời gian thực
        UpdateActiveWirePositions();
    }

    /// <summary>
    /// Bắn raycast 3D từ con trỏ ngón tay vào không gian để tìm Collider mang Tag "Terminal"
    /// </summary>
    private Transform FindTerminalAt(Vector2 screenPos)
    {
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        Physics.SyncTransforms();
        int count = Physics.RaycastNonAlloc(ray, terminalHits, maxRaycastDistance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);
        // Saturation must not silently lose a closer terminal in a dense circuit.
        RaycastHit[] hits = count == terminalHits.Length
            ? Physics.RaycastAll(ray, maxRaycastDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
            : terminalHits;
        if (hits != terminalHits) count = hits.Length;
        Transform hitTerminal = null;
        float minDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.distance >= minDistance) continue;
            var terminal = hit.collider.GetComponentInParent<Terminal>();
            if (terminal == null && !hit.collider.CompareTag("Terminal")) continue;
            minDistance = hit.distance;
            hitTerminal = terminal != null ? terminal.transform : hit.collider.transform;
        }
        return hitTerminal != null ? hitTerminal : FindTerminalNear(screenPos);
    }

    private Transform FindTerminalNear(Vector2 screenPoint)
    {
        if (tapToPlaceController == null) return null;
        bool headset = buttonInteractor != null && buttonInteractor.IsHeadsetMode;
        if (headset && !PhoneHeadsetStereo.ContainsSourcePoint(mainCamera.ScreenToViewportPoint(screenPoint))) return null;
        // Only terminal targets get this small allowance. Body grabs retain their
        // strict visible-silhouette hit test.
        float padding = Mathf.Clamp(Mathf.Min(mainCamera.pixelWidth, mainCamera.pixelHeight) * 0.028f, 18f, 32f);
        float closest = float.PositiveInfinity;
        Transform result = null;
        foreach (var placed in tapToPlaceController.placedObjectsHistory)
        {
            if (placed == null || !placed.activeInHierarchy) continue;
            nearbyTerminals.Clear();
            placed.GetComponentsInChildren(false, nearbyTerminals);
            foreach (var terminal in nearbyTerminals)
            {
                if (!terminal.isActiveAndEnabled ||
                    !TryGetTerminalBounds(terminal.transform, out Bounds bounds) ||
                    (bounds.center - mainCamera.transform.position).sqrMagnitude > maxRaycastDistance * maxRaycastDistance) continue;
                int views = headset ? 2 : 1;
                for (int eye = 0; eye < views; eye++)
                {
                    if (!TryProjectTerminalBounds(bounds, headset, eye == 1, out Rect rect, out Vector2 center)) continue;
                    Vector2 nearest = new Vector2(Mathf.Clamp(screenPoint.x, rect.xMin, rect.xMax),
                        Mathf.Clamp(screenPoint.y, rect.yMin, rect.yMax));
                    if ((nearest - screenPoint).sqrMagnitude > padding * padding) continue;
                    float score = (center - screenPoint).sqrMagnitude;
                    if (score >= closest) continue;
                    closest = score;
                    result = terminal.transform;
                }
            }
        }
        return result;
    }

    private bool TryProjectTerminalBounds(Bounds bounds, bool headset, bool right, out Rect rect, out Vector2 center)
    {
        rect = default;
        center = default;
        if (headset)
        {
            if (!PhoneHeadsetStereo.TryProjectIntoEye(mainCamera, bounds.center, right, out center)) return false;
        }
        else
        {
            Vector3 point = mainCamera.WorldToScreenPoint(bounds.center);
            if (point.z <= mainCamera.nearClipPlane) return false;
            center = point;
        }
        Vector2 min = center, max = center;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            Vector2 projected;
            if (headset)
            {
                if (!PhoneHeadsetStereo.TryProjectIntoEye(mainCamera, corner, right, out projected, false)) continue;
            }
            else
            {
                Vector3 point = mainCamera.WorldToScreenPoint(corner);
                if (point.z <= mainCamera.nearClipPlane) continue;
                projected = point;
            }
            min = Vector2.Min(min, projected);
            max = Vector2.Max(max, projected);
        }
        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private static bool TryGetTerminalBounds(Transform terminal, out Bounds bounds)
    {
        var collider = terminal.GetComponentInChildren<Collider>();
        bounds = collider != null ? collider.bounds : default;
        return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
    }

    /// <summary>
    /// Xử lý logic máy trạng thái nối dây / xóa dây giữa 2 cực
    /// </summary>
    private void OnTerminalPinched(Transform terminal)
    {
        // Trường hợp 1: Chưa chọn cực nào -> Chọn cực 1 và Highlight
        if (firstSelectedTerminal == null)
        {
            firstSelectedTerminal = terminal;
            HighlightTerminal(terminal);
            Debug.Log($"<color=yellow>[WireConnection]</color> Đã chọn cực 1: [{terminal.name}] thuộc [{GetRootPlacedName(terminal)}]");
            return;
        }

        // Trường hợp 2: Bấm lại đúng cực 1 -> Huỷ chọn
        if (firstSelectedTerminal == terminal)
        {
            Debug.Log($"<color=yellow>[WireConnection]</color> Bấm lại cực 1 -> Huỷ chọn [{terminal.name}]");
            CancelSelection();
            return;
        }

        // Trường hợp 3: Bấm vào 2 cực của cùng 1 linh kiện (so sánh Transform thay cho tên chuỗi)
        if (GetRootPlacedTransform(firstSelectedTerminal) == GetRootPlacedTransform(terminal))
        {
            Debug.LogWarning($"<color=orange>[WireConnection]</color> Hai cực thuộc cùng linh kiện [{GetRootPlacedName(terminal)}]. Thao tác bị từ chối.");
            if (currentSubMode == WireSubMode.Delete)
            {
                CancelSelection();
                ShowNoWireNotification();
            }
            return;
        }

        Transform terminalA = firstSelectedTerminal;
        Transform terminalB = terminal;

        if (currentSubMode == WireSubMode.Connect)
        {
            // === CHẾ ĐỘ NỐI DÂY (GIỮ NGUYÊN 100% NHƯ CŨ) ===
            // Trường hợp 4: Kiểm tra xem 2 cực này đã có dây nối chưa
            if (IsAlreadyConnected(terminalA, terminalB))
            {
                Debug.LogWarning($"<color=orange>[WireConnection]</color> Hai cực [{terminalA.name}] và [{terminalB.name}] đã được nối dây từ trước!");
                CancelSelection();
                return;
            }

            // Trường hợp 5: Hợp lệ -> Tiến hành nối dây trực tiếp (Multi-connection cho phép nối nhiều dây vào cùng 1 cực)
            CancelSelection();

            CreateWire(terminalA, terminalB);

            // Ghi nhận vào lịch sử Undo/Redo độc lập cho dây vừa tạo (Requirement 2 & 3)
            if (tapToPlaceController == null) AutoResolveReferences();
            if (tapToPlaceController != null && tapToPlaceController.History != null)
            {
                tapToPlaceController.History.RecordAction(new ConnectWireAction(terminalA, terminalB, this));
            }
        }
        else // currentSubMode == WireSubMode.Delete
        {
            // === CHẾ ĐỘ XÓA DÂY ===
            // Reset selection ngay để người dùng sẵn sàng cho thao tác tiếp theo
            CancelSelection();

            // Tìm và xóa đúng duy nhất connection A-B (các dây khác như A-C, A-D giữ nguyên)
            bool wireFound = RemoveWire(terminalA, terminalB);
            if (wireFound)
            {
                // Nếu connection A-B tồn tại:
                // -> Xóa đúng connection A-B
                // -> Không hỏi xác nhận
                // -> Không hiện thông báo thành công
                // -> Ghi nhận vào lịch sử Undo/Redo (DisconnectWireAction)
                if (tapToPlaceController == null) AutoResolveReferences();
                if (tapToPlaceController != null && tapToPlaceController.History != null)
                {
                    tapToPlaceController.History.RecordAction(new DisconnectWireAction(terminalA, terminalB, this));
                }
                Debug.Log($"<color=green>[WireConnection]</color> Đã xóa kết nối dây giữa [{terminalA.name}] và [{terminalB.name}].");
            }
            else
            {
                // Nếu connection A-B KHÔNG tồn tại:
                // -> Không thay đổi gì
                // -> Hiện thông báo: "Không có dây, vui lòng chọn lại"
                ShowNoWireNotification();
            }
        }
    }

    /// <summary>
    /// Hiển thị thông báo khi không tìm thấy dây nối giữa 2 cực được chọn trong Chế độ Xóa Dây
    /// </summary>
    public void ShowNoWireNotification()
    {
        if (floatingMenu == null) AutoResolveReferences();
        if (floatingMenu != null)
        {
            floatingMenu.ShowToastNotification("Không có dây, vui lòng chọn lại");
            floatingMenu.FlashWireModeBadgeMessage("KHÔNG CÓ DÂY, VUI LÒNG CHỌN LẠI");
        }
        else
        {
            Debug.LogWarning("<color=orange>[WireConnection]</color> Không có dây, vui lòng chọn lại");
        }
    }

    /// <summary>
    /// Nhận diện màu sắc của cực linh kiện để tô màu dây thông minh (Đỏ / Đen)
    /// </summary>
    private Color DetectTerminalColor(Transform terminal)
    {
        if (!autoColorByTerminal) return wireColor;

        Renderer rend = terminal.GetComponent<Renderer>();
        if (rend == null) rend = terminal.GetComponentInChildren<Renderer>();

        if (rend != null && rend.sharedMaterial != null)
        {
            Color matCol = rend.sharedMaterial.HasProperty("_BaseColor") ? rend.sharedMaterial.GetColor("_BaseColor") : rend.sharedMaterial.color;
            string matName = rend.sharedMaterial.name.ToLower();
            string objName = terminal.name.ToLower();

            // Nếu là cực đỏ hoặc tên chứa red/plus/+
            if ((matCol.r > 0.55f && matCol.g < 0.35f && matCol.b < 0.35f) || matName.Contains("red") || objName.Contains("red"))
            {
                return new Color(0.92f, 0.15f, 0.15f, 1f); // Đỏ tươi
            }

            // Nếu là cực đen hoặc tên chứa black/minus/-
            if ((matCol.r < 0.3f && matCol.g < 0.3f && matCol.b < 0.3f) || matName.Contains("black") || objName.Contains("black"))
            {
                return new Color(0.12f, 0.12f, 0.12f, 1f); // Đen bóng
            }
        }

        return wireColor;
    }

    /// <summary>
    /// Tính toán vị trí nắp trên cùng của cọc linh kiện
    /// </summary>
    public Vector3 GetTerminalBaseAnchor(Transform terminal)
    {
        if (terminal == null) return Vector3.zero;

        Collider col = terminal.GetComponent<Collider>();
        if (col == null) col = terminal.GetComponentInChildren<Collider>();

        if (col != null)
        {
            Vector3 center = col.bounds.center;
            // Vị trí nắp trên cùng của cọc
            center.y = col.bounds.max.y;
            return center;
        }

        Renderer rend = terminal.GetComponent<Renderer>();
        if (rend != null)
        {
            Vector3 center = rend.bounds.center;
            center.y = rend.bounds.max.y;
            return center;
        }

        return terminal.position + Vector3.up * 0.012f;
    }

    /// <summary>
    /// Tạo đối tượng dây thân tròn 3D hoàn chỉnh nối giữa 2 Transform cực và cắm 2 đầu giắc
    /// </summary>
    private void CreateWire(Transform startTerminal, Transform endTerminal)
    {
        string wireName = $"Wire3D_{GetRootPlacedName(startTerminal)}_{startTerminal.name}__to__{GetRootPlacedName(endTerminal)}_{endTerminal.name}";
        GameObject wireObj = new GameObject(wireName);

        if (wiresContainer != null)
        {
            wireObj.transform.SetParent(wiresContainer, true);
        }

        MeshFilter mf = wireObj.AddComponent<MeshFilter>();
        MeshRenderer mr = wireObj.AddComponent<MeshRenderer>();

        // Xác định màu sắc thông minh cho sợi dây theo cực khởi đầu
        Color effectiveColor = DetectTerminalColor(startTerminal);

        // Cấu hình Material với độ bóng cách điện chân thực
        Material mat = wireMaterial != null ? new Material(wireMaterial) : GetOrCreateRuntimeMaterial();
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", effectiveColor);
        else if (mat.HasProperty("_Color")) mat.color = effectiveColor;
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.7f);

        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;

        Mesh wireMesh = new Mesh();
        wireMesh.name = "Dynamic_Wire_Tube";
        mf.mesh = wireMesh;

        // Sinh 2 Pivot và 2 đầu giắc cắm bắp chuối ôm khít cọc
        Color colA = DetectTerminalColor(startTerminal);
        Color colB = DetectTerminalColor(endTerminal);
        GameObject connA = InstantiateConnector(connectorPrefabA, "Connector_A", wireObj.transform, colA, out GameObject pivotA);
        GameObject connB = InstantiateConnector(connectorPrefabB, "Connector_B", wireObj.transform, colB, out GameObject pivotB);

        // Lưu vào danh sách quản lý
        WireRecord record = new WireRecord(startTerminal, endTerminal, wireObj, mf, mr, wireMesh, pivotA, pivotB, connA, connB, effectiveColor);
        activeWires.Add(record);

        Terminal termA = startTerminal.GetComponent<Terminal>() ?? startTerminal.GetComponentInChildren<Terminal>();
        Terminal termB = endTerminal.GetComponent<Terminal>() ?? endTerminal.GetComponentInChildren<Terminal>();
        if (termA != null && termB != null)
        {
            termA.ConnectTo(termB);
        }

        // Cập nhật vị trí toàn bộ dây và các nút ngã rẽ phân nhánh ngay lập tức
        UpdateActiveWirePositions();

        Debug.Log($"<color=green>[WireConnection]</color> <b>NỐI DÂY 3D THÀNH CÔNG!</b> Giữa [{startTerminal.name}] và [{endTerminal.name}]");
    }

    /// <summary>
    /// Khởi tạo đầu giắc cắm (ưu tiên giắc bắp chuối chuẩn phòng thí nghiệm ôm khít cọc)
    /// </summary>
    private GameObject InstantiateConnector(GameObject prefab, string defaultName, Transform parent, Color plugColor, out GameObject pivotObj)
    {
        pivotObj = new GameObject(defaultName + "_Pivot");
        pivotObj.transform.SetParent(parent, false);

        if (useLabPlugStyle)
        {
            // Sinh giắc cắm bắp chuối chuẩn phòng thí nghiệm (rất đẹp và ôm khít cọc)
            return CreateLabBananaPlug(defaultName + "_Model", pivotObj.transform, plugColor);
        }

        // Tùy chọn 2: Dùng model prefab nếu người dùng tắt useLabPlugStyle
        GameObject conn;
        if (prefab != null)
        {
            conn = Instantiate(prefab, pivotObj.transform);
            conn.name = defaultName + "_Model";
            conn.transform.localPosition = Vector3.zero;
            conn.transform.localScale = Vector3.one * connectorScale;

            Collider[] colliders = conn.GetComponentsInChildren<Collider>();
            foreach (var c in colliders) Destroy(c);
        }
        else
        {
            conn = CreateLabBananaPlug(defaultName + "_Model", pivotObj.transform, plugColor);
        }
        return conn;
    }

    /// <summary>
    /// Sinh đầu giắc cắm bắp chuối (Banana Plug) tinh xảo: thân bọc nhựa bóng ôm cọc + vòng khuyên kim loại mạ vàng
    /// </summary>
    private GameObject CreateLabBananaPlug(string name, Transform parent, Color plugColor)
    {
        GameObject plugRoot = new GameObject(name);
        plugRoot.transform.SetParent(parent, false);

        // 1. Thân giắc bọc nhựa cách điện (Insulated Sleeve)
        GameObject sleeve = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        sleeve.name = "Sleeve";
        sleeve.transform.SetParent(plugRoot.transform, false);
        Collider col1 = sleeve.GetComponent<Collider>();
        if (col1 != null) Destroy(col1);

        float sleeveRadius = wireRadius * 2.2f; // ~4.8mm đường kính
        float sleeveHeight = LAB_PLUG_HEIGHT; // 1.1cm
        sleeve.transform.localScale = new Vector3(sleeveRadius * 2f, sleeveHeight * 0.5f, sleeveRadius * 2f);
        sleeve.transform.localPosition = new Vector3(0, sleeveHeight * 0.5f, 0);

        Material sleeveMat = new Material(wireMaterial != null ? wireMaterial : GetOrCreateRuntimeMaterial());
        if (sleeveMat.HasProperty("_BaseColor")) sleeveMat.SetColor("_BaseColor", plugColor);
        else if (sleeveMat.HasProperty("_Color")) sleeveMat.color = plugColor;
        if (sleeveMat.HasProperty("_Smoothness")) sleeveMat.SetFloat("_Smoothness", 0.75f);
        sleeve.GetComponent<Renderer>().material = sleeveMat;

        // 2. Vòng khuyên kim loại mạ vàng ở chân cắm (Gold Collar Ring)
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Metal_Collar";
        ring.transform.SetParent(plugRoot.transform, false);
        Collider col2 = ring.GetComponent<Collider>();
        if (col2 != null) Destroy(col2);

        float ringRadius = sleeveRadius * 1.15f;
        float ringHeight = 0.002f; // 2mm
        ring.transform.localScale = new Vector3(ringRadius * 2f, ringHeight * 0.5f, ringRadius * 2f);
        ring.transform.localPosition = new Vector3(0, ringHeight * 0.5f, 0);

        Material ringMat = new Material(sleeveMat);
        Color goldColor = new Color(0.85f, 0.72f, 0.25f, 1f);
        if (ringMat.HasProperty("_BaseColor")) ringMat.SetColor("_BaseColor", goldColor);
        else if (ringMat.HasProperty("_Color")) ringMat.color = goldColor;
        if (ringMat.HasProperty("_Metallic")) ringMat.SetFloat("_Metallic", 0.95f);
        if (ringMat.HasProperty("_Smoothness")) ringMat.SetFloat("_Smoothness", 0.85f);
        ring.GetComponent<Renderer>().material = ringMat;

        return plugRoot;
    }

    // Quản lý các Node ngã rẽ trực quan (Junction Nodes) khi một cọc có từ 2 kết nối trở lên
    private readonly Dictionary<Transform, GameObject> activeJunctionNodes = new Dictionary<Transform, GameObject>();
    private readonly Dictionary<Transform, List<WireRecord>> terminalWires = new();
    private readonly Stack<List<WireRecord>> terminalWireLists = new();
    private readonly Dictionary<Transform, TerminalJunctionInfo> junctionMap = new();
    private readonly HashSet<Transform> currentBranchedTerminals = new();
    private readonly List<Transform> otherTerms = new();
    private readonly List<Transform> toRemoveNodes = new();


    private struct TerminalJunctionInfo
    {
        public Vector3 junctionPos;
        public WireRecord primaryWire;
    }

    /// <summary>
    /// Tính toán vị trí ngã rẽ Junction deterministic từ hướng trung bình các cọc đích
    /// </summary>
    private Vector3 CalculateJunctionPosition(Transform term, List<Transform> otherTerms, Vector3 termPlugPos)
    {
        Vector3 avgDir = Vector3.zero;
        float minDist = float.MaxValue;
        for (int i = 0; i < otherTerms.Count; i++)
        {
            if (otherTerms[i] == null) continue;
            Vector3 delta = otherTerms[i].position - term.position;
            float d = delta.magnitude;
            if (d < minDist) minDist = d;
            delta.y = 0;
            if (delta.sqrMagnitude > 0.0001f)
            {
                avgDir += delta.normalized;
            }
        }

        if (minDist > 100f) minDist = 0.2f;

        // Tránh triệt tiêu vector khi các cọc đối diện nằm đối xứng 2 bên:
        if (avgDir.sqrMagnitude < 0.01f)
        {
            Vector3 fwd = term.forward;
            fwd.y = 0;
            if (fwd.sqrMagnitude < 0.001f && term.parent != null)
            {
                fwd = term.parent.forward;
                fwd.y = 0;
            }
            avgDir = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
        }
        else
        {
            avgDir = avgDir.normalized;
        }

        float stalkDist = Mathf.Clamp(minDist * 0.35f, 0.035f, 0.10f);
        float stalkHeight = 0.015f;
        return termPlugPos + Vector3.up * stalkHeight + avgDir * stalkDist;
    }

    /// <summary>
    /// Cập nhật hình học thân ống tròn 3D uốn cong mềm mại (Cubic Bézier Arch & Sag) theo thời gian thực.
    /// Hỗ trợ cả dây đơn trực tiếp lẫn phân nhánh ngã rẽ (Junction) không chồng chéo mesh.
    /// </summary>
    private void UpdateWireGeometry(
        WireRecord wire,
        bool isBranchedA = false,
        bool isPrimaryA = false,
        Vector3 jPosA = default,
        bool isBranchedB = false,
        bool isPrimaryB = false,
        Vector3 jPosB = default)
    {
        if (wire.terminalA == null || wire.terminalB == null || wire.wireMesh == null) return;

        // Tọa độ nắp cọc linh kiện
        Vector3 basePosA = GetTerminalBaseAnchor(wire.terminalA);
        Vector3 basePosB = GetTerminalBaseAnchor(wire.terminalB);

        var inputs = new WireGeometryInputs
        {
            baseA = basePosA, baseB = basePosB, junctionA = jPosA, junctionB = jPosB,
            rotationA = connectorRotationOffsetA, rotationB = connectorRotationOffsetB,
            worldToLocal = wire.wireObject.transform.worldToLocalMatrix,
            shape = new Vector4(wireRadius, archHeight, archFactor, textureTilingPerMeter),
            segments = new Vector2Int(Mathf.Max(16, curveSegments), Mathf.Max(6, radialSegments)),
            flags = (isBranchedA ? 1 : 0) | (isPrimaryA ? 2 : 0) | (isBranchedB ? 4 : 0) |
                (isPrimaryB ? 8 : 0) | (useLabPlugStyle ? 16 : 0) |
                (wire.pivotA != null && wire.pivotA.activeSelf ? 32 : 0) |
                (wire.pivotB != null && wire.pivotB.activeSelf ? 64 : 0),
            mesh = wire.wireMesh
        };
        if (wire.hasGeometry && wire.geometryInputs.Equals(inputs)) return;

        // Điểm xuất phát của sợi dây: nhô ra từ đỉnh của giắc cắm
        float plugTopOffset = useLabPlugStyle ? LAB_PLUG_HEIGHT : 0.005f;
        Vector3 p0 = basePosA + Vector3.up * plugTopOffset;
        Vector3 p3 = basePosB + Vector3.up * plugTopOffset;

        int N = Mathf.Max(16, curveSegments);
        int K = Mathf.Max(6, radialSegments);

        if (wire.curvePoints == null || wire.curvePoints.Length != N + 1)
        {
            wire.curvePoints = new Vector3[N + 1];
            wire.tangents = new Vector3[N + 1];
            wire.normals = new Vector3[N + 1];
            wire.binormals = new Vector3[N + 1];
        }
        Vector3[] curvePoints = wire.curvePoints;
        Vector3[] tangents = wire.tangents;

        // Xác định điểm thực sự bắt đầu và kết thúc của đoạn uốn lượn chính
        // Nếu là nhánh phụ (không phải Primary), điểm bắt đầu/kết thúc chính là Junction Node
        Vector3 startPos = (isBranchedA && !isPrimaryA) ? jPosA : p0;
        Vector3 endPos = (isBranchedB && !isPrimaryB) ? jPosB : p3;

        float dist = Vector3.Distance(startPos, endPos);
        if (dist < 0.002f) return;

        // Điểm phân chia đoạn thân thẳng (Trunk) nhô ra từ cọc tới Junction nếu là Primary Wire
        int splitA = isPrimaryA ? Mathf.Clamp(N / 4, 3, N / 3) : 0;
        int splitB = isPrimaryB ? Mathf.Clamp(N - (N / 4), N * 2 / 3, N - 3) : N;

        Vector3 midStart = isPrimaryA ? jPosA : startPos;
        Vector3 midEnd = isPrimaryB ? jPosB : endPos;
        float midDist = Vector3.Distance(midStart, midEnd);
        float midArch = Mathf.Clamp(midDist * archFactor, archHeight * 0.5f, archHeight * 2.2f);
        Vector3 midDir = midDist > 0.001f ? (midEnd - midStart).normalized : Vector3.forward;
        Vector3 mp1 = midStart + Vector3.up * midArch + midDir * (midDist * 0.25f);
        Vector3 mp2 = midEnd + Vector3.up * midArch - midDir * (midDist * 0.25f);

        for (int i = 0; i <= N; i++)
        {
            if (isPrimaryA && i <= splitA)
            {
                // Đoạn thân chung từ cọc A đến nút Junction jPosA (CHỈ VẼ TRÊN PRIMARY WIRE)
                float tLocal = (float)i / splitA;
                float smoothT = Mathf.SmoothStep(0f, 1f, tLocal);
                curvePoints[i] = Vector3.Lerp(p0, jPosA, smoothT);
            }
            else if (isPrimaryB && i >= splitB)
            {
                // Đoạn thân chung từ nút Junction jPosB đến cọc B (CHỈ VẼ TRÊN PRIMARY WIRE)
                float tLocal = (float)(i - splitB) / (N - splitB);
                float smoothT = Mathf.SmoothStep(0f, 1f, tLocal);
                curvePoints[i] = Vector3.Lerp(jPosB, p3, smoothT);
            }
            else
            {
                // Đoạn vòng cung uốn lượn tự do ở giữa (hoặc toàn bộ dây nếu không phải đoạn trunk)
                int midStartIdx = isPrimaryA ? splitA : 0;
                int midEndIdx = isPrimaryB ? splitB : N;
                float tLocal = (float)(i - midStartIdx) / Mathf.Max(1, midEndIdx - midStartIdx);
                float uLocal = 1f - tLocal;

                curvePoints[i] = uLocal * uLocal * uLocal * midStart
                               + 3f * uLocal * uLocal * tLocal * mp1
                               + 3f * uLocal * tLocal * tLocal * mp2
                               + tLocal * tLocal * tLocal * midEnd;
            }
        }

        // Tính tiếp tuyến mượt mà dọc theo tất cả các điểm
        for (int i = 0; i <= N; i++)
        {
            Vector3 tan;
            if (i == 0) tan = curvePoints[1] - curvePoints[0];
            else if (i == N) tan = curvePoints[N] - curvePoints[N - 1];
            else tan = curvePoints[i + 1] - curvePoints[i - 1];

            if (tan.sqrMagnitude < 0.00001f) tan = midDir;
            tangents[i] = tan.normalized;
        }


        // 2. HỆ TRỤC TỌA ĐỘ QUAY (Parallel Transport Frame) chống xoắn vặn
        Vector3[] normals = wire.normals;
        Vector3[] binormals = wire.binormals;

        Vector3 initialUp = Mathf.Abs(Vector3.Dot(tangents[0], Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
        normals[0] = Vector3.Cross(tangents[0], initialUp).normalized;
        binormals[0] = Vector3.Cross(tangents[0], normals[0]);

        for (int i = 1; i <= N; i++)
        {
            Quaternion rot = Quaternion.FromToRotation(tangents[i - 1], tangents[i]);
            normals[i] = (rot * normals[i - 1]).normalized;
            binormals[i] = Vector3.Cross(tangents[i], normals[i]);
        }

        // 3. TẠO ĐỈNH, PHÁP TUYẾN VÀ UV
        int vertCount = (N + 1) * (K + 1);
        if (wire.vertices == null || wire.vertices.Length != vertCount)
        {
            wire.vertices = new Vector3[vertCount];
            wire.meshNormals = new Vector3[vertCount];
            wire.uvs = new Vector2[vertCount];
        }
        Vector3[] vertices = wire.vertices;
        Vector3[] meshNormals = wire.meshNormals;
        Vector2[] uvs = wire.uvs;

        float cumulativeDist = 0f;
        int vIdx = 0;
        Transform wireTransform = wire.wireObject.transform;

        for (int i = 0; i <= N; i++)
        {
            if (i > 0) cumulativeDist += Vector3.Distance(curvePoints[i], curvePoints[i - 1]);

            for (int j = 0; j <= K; j++)
            {
                float angle = (float)j / K * Mathf.PI * 2f;
                Vector3 radialDir = Mathf.Cos(angle) * normals[i] + Mathf.Sin(angle) * binormals[i];
                Vector3 worldPos = curvePoints[i] + radialDir * wireRadius;

                vertices[vIdx] = wireTransform.InverseTransformPoint(worldPos);
                meshNormals[vIdx] = wireTransform.InverseTransformDirection(radialDir);
                uvs[vIdx] = new Vector2((float)j / K, cumulativeDist * textureTilingPerMeter);
                vIdx++;
            }
        }

        // 4. TẠO TAM GIÁC MESH
        int triCount = N * K * 6;
        if (wire.triangles == null || wire.triangles.Length != triCount) wire.triangles = new int[triCount];
        int[] triangles = wire.triangles;
        int tIdx = 0;

        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < K; j++)
            {
                int cur = i * (K + 1) + j;
                int next = cur + (K + 1);

                triangles[tIdx++] = cur;
                triangles[tIdx++] = next;
                triangles[tIdx++] = cur + 1;

                triangles[tIdx++] = next;
                triangles[tIdx++] = next + 1;
                triangles[tIdx++] = cur + 1;
            }
        }

        // 5. CẬP NHẬT DỮ LIỆU MESH
        wire.wireMesh.Clear();
        wire.wireMesh.vertices = vertices;
        wire.wireMesh.normals = meshNormals;
        wire.wireMesh.uv = uvs;
        wire.wireMesh.triangles = triangles;
        wire.wireMesh.RecalculateBounds();

        // 6. CẬP NHẬT VỊ TRÍ 2 GIẮC CẮM
        if (wire.pivotA != null && wire.pivotA.activeSelf)
        {
            wire.pivotA.transform.position = basePosA;
            if (useLabPlugStyle)
            {
                wire.pivotA.transform.rotation = Quaternion.identity;
            }
            else
            {
                Vector3 flatDir = new Vector3(tangents[0].x, 0, tangents[0].z);
                if (flatDir.sqrMagnitude > 0.001f)
                {
                    wire.pivotA.transform.rotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
                }
                if (wire.connectorA != null)
                {
                    wire.connectorA.transform.localRotation = Quaternion.Euler(connectorRotationOffsetA);
                }
            }
        }

        if (wire.pivotB != null && wire.pivotB.activeSelf)
        {
            wire.pivotB.transform.position = basePosB;
            if (useLabPlugStyle)
            {
                wire.pivotB.transform.rotation = Quaternion.identity;
            }
            else
            {
                Vector3 flatDir = new Vector3(-tangents[N].x, 0, -tangents[N].z);
                if (flatDir.sqrMagnitude > 0.001f)
                {
                    wire.pivotB.transform.rotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
                }
                if (wire.connectorB != null)
                {
                    wire.connectorB.transform.localRotation = Quaternion.Euler(connectorRotationOffsetB);
                }
            }
        }
        wire.geometryInputs = inputs;
        wire.hasGeometry = true;
        wire.geometryRevision++;
    }

    /// <summary>
    /// Cập nhật vị trí và hình dáng của toàn bộ các dây đang kết nối theo thời gian thực.
    /// Quản lý phân nhánh ngã rẽ (Junction) một cách deterministic ở tầng hiển thị, không chồng lấn mesh.
    /// </summary>
    private void UpdateActiveWirePositions()
    {
        // 1. Dọn dẹp các dây không hợp lệ
        for (int i = activeWires.Count - 1; i >= 0; i--)
        {
            var wire = activeWires[i];

            if (wire.terminalA == null || wire.terminalB == null || wire.wireObject == null)
            {
                if (wire.wireMesh != null) Destroy(wire.wireMesh);
                if (wire.wireObject != null) Destroy(wire.wireObject);
                activeWires.RemoveAt(i);
                continue;
            }

            // Wires live outside Board_Anchor. Hide them along with their endpoints
            // during tracking recovery, without deleting connections or undo history.
            bool endpointsVisible = wire.terminalA.gameObject.activeInHierarchy
                && wire.terminalB.gameObject.activeInHierarchy;
            wire.wireObject.SetActive(endpointsVisible);
        }

        // 2. Gom nhóm các dây theo từng terminal
        foreach (var list in terminalWires.Values)
        {
            list.Clear();
            terminalWireLists.Push(list);
        }
        terminalWires.Clear();
        for (int i = 0; i < activeWires.Count; i++)
        {
            var wire = activeWires[i];
            if (!wire.wireObject.activeSelf) continue;

            if (!terminalWires.TryGetValue(wire.terminalA, out var listA))
            {
                listA = terminalWireLists.Count > 0 ? terminalWireLists.Pop() : new List<WireRecord>();
                terminalWires[wire.terminalA] = listA;
            }
            listA.Add(wire);

            if (!terminalWires.TryGetValue(wire.terminalB, out var listB))
            {
                listB = terminalWireLists.Count > 0 ? terminalWireLists.Pop() : new List<WireRecord>();
                terminalWires[wire.terminalB] = listB;
            }
            listB.Add(wire);
        }

        // 3. Tính toán Junction Data cho các terminal có >= 2 dây kết nối
        junctionMap.Clear();
        currentBranchedTerminals.Clear();
        float plugTopOffset = useLabPlugStyle ? LAB_PLUG_HEIGHT : 0.005f;

        foreach (var kvp in terminalWires)
        {
            Transform term = kvp.Key;
            List<WireRecord> wiresOnTerm = kvp.Value;

            if (wiresOnTerm.Count < 2) continue;

            currentBranchedTerminals.Add(term);
            Vector3 termPlugPos = GetTerminalBaseAnchor(term) + Vector3.up * plugTopOffset;

            // Thu thập các terminal đối diện kết nối với cọc term (loại trừ trùng lặp và sắp xếp deterministic)
            otherTerms.Clear();
            for (int i = 0; i < wiresOnTerm.Count; i++)
            {
                Transform other = (wiresOnTerm[i].terminalA == term) ? wiresOnTerm[i].terminalB : wiresOnTerm[i].terminalA;
                if (other != null && !otherTerms.Contains(other)) otherTerms.Add(other);
            }
            otherTerms.Sort((x, y) => x.GetInstanceID().CompareTo(y.GetInstanceID()));

            // Tính vị trí Junction J_T một cách deterministic
            Vector3 junctionPos = CalculateJunctionPosition(term, otherTerms, termPlugPos);

            Vector3 trunkDir = (junctionPos - termPlugPos);
            trunkDir.y = 0;
            if (trunkDir.sqrMagnitude > 0.0001f) trunkDir.Normalize();
            else trunkDir = term.forward;

            // Xác định Primary Wire deterministically: dây có cọc đích thẳng hàng nhất với hướng trunk
            WireRecord bestWire = wiresOnTerm[0];
            float bestDot = -999f;
            int bestInstanceId = int.MinValue;

            for (int i = 0; i < wiresOnTerm.Count; i++)
            {
                WireRecord w = wiresOnTerm[i];
                Transform other = (w.terminalA == term) ? w.terminalB : w.terminalA;
                Vector3 toDest = (other != null ? other.position : term.position) - term.position;
                toDest.y = 0;
                float dot = toDest.sqrMagnitude > 0.0001f ? Vector3.Dot(trunkDir, toDest.normalized) : 0f;
                int instId = other != null ? other.GetInstanceID() : 0;

                if (dot > bestDot || (Mathf.Approximately(dot, bestDot) && instId > bestInstanceId))
                {
                    bestDot = dot;
                    bestInstanceId = instId;
                    bestWire = w;
                }
            }

            junctionMap[term] = new TerminalJunctionInfo
            {
                junctionPos = junctionPos,
                primaryWire = bestWire
            };

            // Cập nhật hoặc tạo mới visual Junction Node (viên bi tròn che khớp ngã rẽ)
            if (!activeJunctionNodes.TryGetValue(term, out GameObject nodeObj) || nodeObj == null)
            {
                nodeObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                nodeObj.name = $"JunctionNode_{term.name}";
                Collider col = nodeObj.GetComponent<Collider>();
                if (col != null) Destroy(col);
                if (wiresContainer != null) nodeObj.transform.SetParent(wiresContainer, true);

                Material nodeMat = wireMaterial != null ? new Material(wireMaterial) : GetOrCreateRuntimeMaterial();
                nodeObj.GetComponent<Renderer>().material = nodeMat;
                activeJunctionNodes[term] = nodeObj;
            }

            nodeObj.transform.position = junctionPos;
            nodeObj.transform.localScale = Vector3.one * (wireRadius * 2.8f);
            nodeObj.SetActive(term.gameObject.activeInHierarchy);

            Color termColor = DetectTerminalColor(term);
            Material rendMat = nodeObj.GetComponent<Renderer>().material;
            if (rendMat.HasProperty("_BaseColor")) rendMat.SetColor("_BaseColor", termColor);
            else if (rendMat.HasProperty("_Color")) rendMat.color = termColor;
            if (rendMat.HasProperty("_Smoothness")) rendMat.SetFloat("_Smoothness", 0.75f);
        }

        // Dọn dẹp các Junction Node không còn phân nhánh (< 2 dây)
        toRemoveNodes.Clear();
        foreach (var kvp in activeJunctionNodes)
        {
            if (!currentBranchedTerminals.Contains(kvp.Key))
            {
                if (kvp.Value != null) Destroy(kvp.Value);
                toRemoveNodes.Add(kvp.Key);
            }
        }
        for (int i = 0; i < toRemoveNodes.Count; i++) activeJunctionNodes.Remove(toRemoveNodes[i]);

        // 4. Render từng dây dựa trên Junction Data
        for (int i = 0; i < activeWires.Count; i++)
        {
            var wire = activeWires[i];
            if (!wire.wireObject.activeSelf) continue;

            bool isBranchedA = junctionMap.TryGetValue(wire.terminalA, out var dataA);
            bool isPrimaryA = isBranchedA && dataA.primaryWire == wire;
            Vector3 jPosA = isBranchedA ? dataA.junctionPos : Vector3.zero;

            bool isBranchedB = junctionMap.TryGetValue(wire.terminalB, out var dataB);
            bool isPrimaryB = isBranchedB && dataB.primaryWire == wire;
            Vector3 jPosB = isBranchedB ? dataB.junctionPos : Vector3.zero;

            // Quản lý đầu giắc cắm:
            // Chỉ hiển thị giắc cắm trên cọc A nếu không phân nhánh, HOẶC là dây chính (Primary) của cọc A
            bool showPlugA = !isBranchedA || isPrimaryA;
            if (wire.pivotA != null) wire.pivotA.SetActive(showPlugA);

            bool showPlugB = !isBranchedB || isPrimaryB;
            if (wire.pivotB != null) wire.pivotB.SetActive(showPlugB);

            // Dựng mesh thân dây uốn lượn
            UpdateWireGeometry(wire, isBranchedA, isPrimaryA, jPosA, isBranchedB, isPrimaryB, jPosB);
        }
    }

    /// <summary>
    /// Thu hồi / Xóa toàn bộ dây điện đang kết nối vào các cực của linh kiện này (Requirement 1 & 4)
    /// </summary>
    public void RemoveWiresConnectedTo(Transform componentRoot)
    {
        if (componentRoot == null) return;

        if (firstSelectedTerminal != null && firstSelectedTerminal.IsChildOf(componentRoot))
        {
            CancelSelection();
        }

        for (int i = activeWires.Count - 1; i >= 0; i--)
        {
            var wire = activeWires[i];
            bool matchA = (wire.terminalA != null && wire.terminalA.IsChildOf(componentRoot));
            bool matchB = (wire.terminalB != null && wire.terminalB.IsChildOf(componentRoot));

            if (matchA || matchB || wire.terminalA == null || wire.terminalB == null)
            {
                if (wire.wireMesh != null)
                {
                    Destroy(wire.wireMesh);
                }
                if (wire.wireObject != null)
                {
                    Destroy(wire.wireObject);
                }
                activeWires.RemoveAt(i);
            }
        }

        UpdateActiveWirePositions();
        Debug.Log($"<color=green>[WireConnection]</color> Đã xóa toàn bộ dây kết nối của [{componentRoot.name}].");
    }

    /// <summary>
    /// Tạo dây nối trực tiếp cho cơ chế Undo/Redo (không ghi lại lịch sử mới)
    /// </summary>
    public void ConnectWireDirectly(Transform terminalA, Transform terminalB)
    {
        if (terminalA == null || terminalB == null) return;
        if (IsAlreadyConnected(terminalA, terminalB)) return;
        CreateWire(terminalA, terminalB);
    }

    /// <summary>
    /// Gỡ bỏ một dây nối cụ thể giữa 2 cực (dùng cho Undo/Redo)
    /// </summary>
    public bool RemoveWire(Transform terminalA, Transform terminalB)
    {
        if (terminalA == null || terminalB == null) return false;

        for (int i = activeWires.Count - 1; i >= 0; i--)
        {
            var wire = activeWires[i];
            bool match = (wire.terminalA == terminalA && wire.terminalB == terminalB) ||
                         (wire.terminalA == terminalB && wire.terminalB == terminalA);
            if (!match) continue;

            activeWires.RemoveAt(i);
            Terminal termA = terminalA.GetComponent<Terminal>() ?? terminalA.GetComponentInChildren<Terminal>();
            Terminal termB = terminalB.GetComponent<Terminal>() ?? terminalB.GetComponentInChildren<Terminal>();
            if (termA != null && termB != null) termA.DisconnectFrom(termB);
            UnityEngine.Rendering.CoreUtils.Destroy(wire.wireMesh);
            UnityEngine.Rendering.CoreUtils.Destroy(wire.wireObject);
            UpdateActiveWirePositions();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Lấy danh sách các cặp cực có dây kết nối tới linh kiện này (dùng để lưu vết Undo khi xóa linh kiện)
    /// </summary>
    public List<(Transform, Transform)> GetConnectedWiresForComponent(Transform componentRoot)
    {
        var list = new List<(Transform, Transform)>();
        if (componentRoot == null) return list;

        foreach (var wire in activeWires)
        {
            if (wire.terminalA != null && wire.terminalB != null)
            {
                if (wire.terminalA.IsChildOf(componentRoot) || wire.terminalB.IsChildOf(componentRoot))
                {
                    list.Add((wire.terminalA, wire.terminalB));
                }
            }
        }
        return list;
    }

    /// <summary>
    /// Cho phép các script bên ngoài (Undo/Redo di chuyển) yêu cầu làm mới vị trí các dây
    /// </summary>
    public void UpdateActiveWirePositionsExternal()
    {
        UpdateActiveWirePositions();
    }

    /// <summary>
    /// Đổi màu Highlight khi cực được chọn / Khôi phục màu gốc khi huỷ chọn
    /// </summary>
    private void HighlightTerminal(Transform terminal)
    {
        var renderer = terminal.GetComponentInChildren<Renderer>();
        if (renderer == null) return;
        var materials = renderer.sharedMaterials;
        if (materials.Length == 0) return;
        if (terminalHighlightMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            terminalHighlightMaterial = new Material(shader != null ? shader : GetOrCreateRuntimeMaterial().shader);
        }
        terminalHighlightMaterial.color = highlightColor;
        firstSelectedRenderer = renderer;
        originalTerminalMaterials = materials;
        var highlighted = new Material[materials.Length];
        for (int i = 0; i < highlighted.Length; i++) highlighted[i] = terminalHighlightMaterial;
        renderer.sharedMaterials = highlighted;
    }

    /// <summary>
    /// Huỷ trạng thái đang chọn cực
    /// </summary>
    public void CancelSelection()
    {
        if (firstSelectedRenderer != null && originalTerminalMaterials != null)
            firstSelectedRenderer.sharedMaterials = originalTerminalMaterials;
        firstSelectedRenderer = null;
        originalTerminalMaterials = null;
        firstSelectedTerminal = null;
    }

    private void OnDisable() => CancelSelection();

    /// <summary>
    /// Kiểm tra xem 2 cực đã có dây nối giữa chúng hay chưa
    /// </summary>
    public bool IsAlreadyConnected(Transform a, Transform b)
    {
        for (int i = 0; i < activeWires.Count; i++)
        {
            var w = activeWires[i];
            if ((w.terminalA == a && w.terminalB == b) || (w.terminalA == b && w.terminalB == a))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Lấy danh sách các cặp cực đang nối dây dưới dạng Tuple
    /// </summary>
    public List<(Transform terminalA, Transform terminalB)> GetConnections()
    {
        List<(Transform, Transform)> list = new List<(Transform, Transform)>();
        for (int i = 0; i < activeWires.Count; i++)
        {
            if (activeWires[i].terminalA != null && activeWires[i].terminalB != null)
            {
                list.Add((activeWires[i].terminalA, activeWires[i].terminalB));
            }
        }
        return list;
    }

    /// <summary>
    /// Xoá toàn bộ dây đã nối
    /// </summary>
    public void ClearAllWires()
    {
        for (int i = activeWires.Count - 1; i >= 0; i--)
        {
            if (activeWires[i].wireMesh != null)
            {
                Destroy(activeWires[i].wireMesh);
            }
            if (activeWires[i].wireObject != null)
            {
                Destroy(activeWires[i].wireObject);
            }
        }
        foreach (var w in activeWires)
        {
            if (w.terminalA != null && w.terminalB != null)
            {
                Terminal tA = w.terminalA.GetComponent<Terminal>() ?? w.terminalA.GetComponentInChildren<Terminal>();
                Terminal tB = w.terminalB.GetComponent<Terminal>() ?? w.terminalB.GetComponentInChildren<Terminal>();
                if (tA != null && tB != null) tA.DisconnectFrom(tB);
            }
        }
        activeWires.Clear();
        ClearJunctionNodes();
        CancelSelection();
        Debug.Log("[WireConnection] Đã xoá toàn bộ dây nối.");
    }

    void OnDestroy()
    {
        ClearJunctionNodes();
        UnityEngine.Rendering.CoreUtils.Destroy(terminalHighlightMaterial);
    }

    private void ClearJunctionNodes()
    {
        foreach (var kvp in activeJunctionNodes)
        {
            if (kvp.Value != null) Destroy(kvp.Value);
        }
        activeJunctionNodes.Clear();
    }

    /// <summary>
    /// Tự động tìm kiếm tham chiếu trong Scene nếu người dùng quên kéo vào Inspector
    /// </summary>
    private void AutoResolveReferences()
    {
        if (buttonInteractor == null)
        {
#if UNITY_2023_1_OR_NEWER
            buttonInteractor = FindFirstObjectByType<ButtonInteractor>();
#else
            buttonInteractor = FindObjectOfType<ButtonInteractor>();
#endif
            if (buttonInteractor != null)
            {
                Debug.Log("<color=orange>[WireConnectionController]</color> Đã tự động tìm thấy ButtonInteractor.");
            }
        }

        if (menuHUD == null)
        {
#if UNITY_2023_1_OR_NEWER
            menuHUD = FindFirstObjectByType<MenuHUDController>();
#else
            menuHUD = FindObjectOfType<MenuHUDController>();
#endif
            if (menuHUD != null)
            {
                Debug.Log("<color=orange>[WireConnectionController]</color> Đã tự động tìm thấy MenuHUDController.");
            }
        }

        if (buttonInteractor == null || menuHUD == null)
        {
            GameObject gm = GameObject.Find("GestureManager");
            if (gm != null)
            {
                if (buttonInteractor == null) buttonInteractor = gm.GetComponent<ButtonInteractor>();
                if (menuHUD == null) menuHUD = gm.GetComponent<MenuHUDController>();
            }
        }

        if (tapToPlaceController == null)
        {
#if UNITY_2023_1_OR_NEWER
            tapToPlaceController = FindFirstObjectByType<TapToPlaceController>();
#else
            tapToPlaceController = FindObjectOfType<TapToPlaceController>();
#endif
        }

        if (floatingMenu == null)
        {
#if UNITY_2023_1_OR_NEWER
            floatingMenu = FindFirstObjectByType<ARFloatingBubbleMenu>();
#else
            floatingMenu = FindObjectOfType<ARFloatingBubbleMenu>();
#endif
        }
    }

    /// <summary>
    /// Tìm tên đối tượng gốc "Placed_..." chứa điểm cực
    /// </summary>
    private string GetRootPlacedName(Transform terminal)
    {
        Transform cur = terminal;
        while (cur != null)
        {
            if (cur.name.StartsWith("Placed_"))
            {
                return cur.name;
            }
            cur = cur.parent;
        }
        return terminal.parent != null ? terminal.parent.name : terminal.name;
    }

    /// <summary>
    /// Tìm Transform gốc "Placed_..." chứa điểm cực để so sánh instance object thay vì tên chuỗi
    /// </summary>
    private Transform GetRootPlacedTransform(Transform terminal)
    {
        if (terminal == null) return null;
        Transform cur = terminal;
        while (cur != null)
        {
            if (cur.name.StartsWith("Placed_"))
            {
                return cur;
            }
            cur = cur.parent;
        }
        Terminal t = terminal.GetComponent<Terminal>();
        if (t != null && t.parentComponent != null)
        {
            return t.parentComponent.transform;
        }
        CircuitComponent cc = terminal.GetComponentInParent<CircuitComponent>();
        if (cc != null)
        {
            return cc.transform;
        }
        return terminal.parent != null ? terminal.parent : terminal;
    }

    /// <summary>
    /// Tạo Material runtime nếu người dùng không gán wireMaterial
    /// </summary>
    private Material GetOrCreateRuntimeMaterial()
    {
        if (runtimeDefaultMaterial != null) return runtimeDefaultMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Mobile/Diffuse");

        runtimeDefaultMaterial = new Material(shader);
        return runtimeDefaultMaterial;
    }
}
