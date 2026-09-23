using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Quản lý tính năng nối dây điện giữa các điểm cực (Terminal) thông qua cử chỉ Pinch (Hand Tracking MediaPipe).
/// - Thân dây uốn lượn hình vòng cung mềm mại tự nhiên (Cubic Bézier Arch & Sag), thanh thoát chuẩn dây điện thí nghiệm.
/// - Đầu giắc cắm bắp chuối (Lab Banana Plug) sang trọng, ôm khít nắp cọc, có vòng đệm kim loại sáng bóng, không bị trồi lơ lửng.
/// - Tự động đồng bộ màu sắc dây và giắc cắm theo cực: Cực Đỏ -> Dây Đỏ, Cực Đen -> Dây Đen.
/// - Đồng bộ vị trí, độ cong và góc xoay theo thời gian thực khi kéo di chuyển linh kiện trên bàn AR.
/// </summary>
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

    public void ToggleWireMode()
    {
        IsWireMode = !IsWireMode;
    }

    // Trạng thái nội bộ
    private Camera mainCamera;
    private Transform firstSelectedTerminal = null;
    private Renderer firstSelectedRenderer = null;
    private Color originalTerminalColor;
    private bool hasOriginalColor = false;
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

    void Update()
    {
        // Kiểm tra an toàn tham chiếu
        if (buttonInteractor == null || menuHUD == null)
        {
            AutoResolveReferences();
            if (buttonInteractor == null || menuHUD == null) return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        // 1. NẾU CHẾ ĐỘ NỐI DÂY ĐANG TẮT (NORMAL / MOVE MODE) -> KHÔNG THỰC HIỆN NỐI DÂY!
        if (!isWireMode)
        {
            if (firstSelectedTerminal != null)
            {
                CancelSelection();
            }
            return;
        }

        // 2. Nếu đang mở menu đặt linh kiện mới, huỷ thao tác nối dây dở dang
        if (menuHUD.HasSelection)
        {
            if (firstSelectedTerminal != null)
            {
                CancelSelection();
            }
            return;
        }

        // 3. Lắng nghe cử chỉ Pinch trong Wire Mode
        if (buttonInteractor.JustPinched)
        {
            HandlePinchRaycast();
        }
    }

    void LateUpdate()
    {
        // 4. Đồng bộ tọa độ, đường cong vồng mềm mại và 2 đầu giắc cắm theo thời gian thực
        UpdateActiveWirePositions();
    }

    /// <summary>
    /// Tính toán Screen Position từ debugPoint của ngón tay
    /// </summary>
    private Vector2 GetCursorScreenPosition()
    {
        if (buttonInteractor.debugPoint == null)
        {
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        Canvas canvas = buttonInteractor.debugPoint.GetComponentInParent<Canvas>();
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
        return RectTransformUtility.WorldToScreenPoint(uiCam, buttonInteractor.debugPoint.position);
    }

    /// <summary>
    /// Bắn raycast 3D từ con trỏ ngón tay vào không gian để tìm Collider mang Tag "Terminal"
    /// </summary>
    private void HandlePinchRaycast()
    {
        Vector2 screenPos = GetCursorScreenPosition();
        Ray ray = mainCamera.ScreenPointToRay(screenPos);

        RaycastHit[] hits = Physics.RaycastAll(ray, maxRaycastDistance);
        Transform hitTerminal = null;
        float minDistance = float.MaxValue;

        foreach (var hit in hits)
        {
            if (hit.collider.CompareTag("Terminal"))
            {
                if (hit.distance < minDistance)
                {
                    minDistance = hit.distance;
                    hitTerminal = hit.collider.transform;
                }
            }
        }

        if (hitTerminal != null)
        {
            OnTerminalPinched(hitTerminal);
        }
    }

    /// <summary>
    /// Xử lý logic máy trạng thái nối dây giữa 2 cực
    /// </summary>
    private void OnTerminalPinched(Transform terminal)
    {
        // Trường hợp 1: Chưa chọn cực nào -> Chọn cực 1 và Highlight
        if (firstSelectedTerminal == null)
        {
            firstSelectedTerminal = terminal;
            HighlightTerminal(firstSelectedTerminal, true);
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

        // Trường hợp 3: Bấm vào 2 cực của cùng 1 linh kiện
        if (GetRootPlacedName(firstSelectedTerminal) == GetRootPlacedName(terminal))
        {
            Debug.LogWarning($"<color=orange>[WireConnection]</color> Hai cực thuộc cùng linh kiện [{GetRootPlacedName(terminal)}]. Thao tác bị từ chối.");
            return;
        }

        // Trường hợp 4: Kiểm tra xem 2 cực này đã có dây nối chưa
        if (IsAlreadyConnected(firstSelectedTerminal, terminal))
        {
            Debug.LogWarning($"<color=orange>[WireConnection]</color> Hai cực [{firstSelectedTerminal.name}] và [{terminal.name}] đã được nối dây từ trước!");
            CancelSelection();
            return;
        }

        // Trường hợp 5: Hợp lệ -> Tiến hành nối dây trực tiếp (Multi-connection cho phép nối nhiều dây vào cùng 1 cực)
        Transform terminalA = firstSelectedTerminal;
        Transform terminalB = terminal;

        CancelSelection();

        CreateWire(terminalA, terminalB);

        // Ghi nhận vào lịch sử Undo/Redo độc lập cho dây vừa tạo (Requirement 2 & 3)
        if (tapToPlaceController == null) AutoResolveReferences();
        if (tapToPlaceController != null && tapToPlaceController.History != null)
        {
            tapToPlaceController.History.RecordAction(new ConnectWireAction(terminalA, terminalB, this));
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

        // Tạo hình học uốn vồng ngay lập tức
        UpdateWireGeometry(record);

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

    /// <summary>
    /// Tính toán điểm nút ngã ba (Y-Split Junction) khi một cực có từ 2 nhánh dây nối trở lên
    /// </summary>
    private Vector3 GetSharedJunctionPoint(Transform terminal, Transform destinationTerminal, Vector3 plugTopPos, out bool isBranched)
    {
        isBranched = false;
        if (terminal == null) return plugTopPos;

        List<Transform> otherTerminals = new List<Transform>();
        for (int i = 0; i < activeWires.Count; i++)
        {
            var w = activeWires[i];
            if (w.terminalA == terminal && w.terminalB != null && w.terminalB != terminal)
            {
                if (!otherTerminals.Contains(w.terminalB)) otherTerminals.Add(w.terminalB);
            }
            else if (w.terminalB == terminal && w.terminalA != null && w.terminalA != terminal)
            {
                if (!otherTerminals.Contains(w.terminalA)) otherTerminals.Add(w.terminalA);
            }
        }

        // Chỉ tạo phân nhánh chữ Y khi cực này có từ 2 kết nối trở lên
        if (otherTerminals.Count < 2)
        {
            return plugTopPos;
        }

        isBranched = true;
        Vector3 avgDir = Vector3.zero;
        for (int i = 0; i < otherTerminals.Count; i++)
        {
            Vector3 d = otherTerminals[i].position - terminal.position;
            d.y = 0;
            if (d.sqrMagnitude > 0.0001f) avgDir += d.normalized;
        }

        if (avgDir.sqrMagnitude < 0.001f)
        {
            Vector3 fallback = (destinationTerminal != null ? destinationTerminal.position : terminal.position) - terminal.position;
            fallback.y = 0;
            avgDir = fallback.sqrMagnitude > 0.001f ? fallback.normalized : Vector3.forward;
        }
        else
        {
            avgDir = avgDir.normalized;
        }

        float stalkDistance = 0.042f; // Đoạn thân chung 4.2cm nhô ra từ cọc
        float stalkHeight = 0.022f;   // Độ cao nhô lên từ cọc
        return plugTopPos + Vector3.up * stalkHeight + avgDir * stalkDistance;
    }

    /// <summary>
    /// Cập nhật hình học thân ống tròn 3D uốn cong mềm mại (Cubic Bézier Arch & Sag) theo thời gian thực.
    /// Hỗ trợ cả dây đơn trực tiếp lẫn phân nhánh chữ Y tự nhiên khi cực nối nhiều nhánh.
    /// </summary>
    private void UpdateWireGeometry(WireRecord wire)
    {
        if (wire.terminalA == null || wire.terminalB == null || wire.wireMesh == null) return;

        // Tọa độ nắp cọc linh kiện
        Vector3 basePosA = GetTerminalBaseAnchor(wire.terminalA);
        Vector3 basePosB = GetTerminalBaseAnchor(wire.terminalB);

        // Điểm xuất phát của sợi dây: nhô ra từ đỉnh của giắc cắm
        float plugTopOffset = useLabPlugStyle ? LAB_PLUG_HEIGHT : 0.005f;
        Vector3 p0 = basePosA + Vector3.up * plugTopOffset;
        Vector3 p3 = basePosB + Vector3.up * plugTopOffset;

        float dist = Vector3.Distance(p0, p3);
        if (dist < 0.005f) return;

        // Tính toán các điểm nút phân nhánh nếu có nhiều nhánh dây cùng cắm vào 1 cực
        Vector3 jA = GetSharedJunctionPoint(wire.terminalA, wire.terminalB, p0, out bool isBranchA);
        Vector3 jB = GetSharedJunctionPoint(wire.terminalB, wire.terminalA, p3, out bool isBranchB);

        int N = Mathf.Max(12, curveSegments);
        int K = Mathf.Max(6, radialSegments);

        Vector3[] curvePoints = new Vector3[N + 1];
        Vector3[] tangents = new Vector3[N + 1];

        // 1. TÍNH TOÁN CÁC ĐIỂM DỌC THÂN DÂY
        if (!isBranchA && !isBranchB)
        {
            // Trường hợp A: Dây đơn thông thường giữa 2 cực (Direct Bézier)
            float dynamicArch = Mathf.Clamp(dist * archFactor, archHeight * 0.6f, archHeight * 2.5f);
            Vector3 dir = (p3 - p0).normalized;
            Vector3 p1 = p0 + Vector3.up * dynamicArch + dir * (dist * 0.25f);
            Vector3 p2 = p3 + Vector3.up * dynamicArch - dir * (dist * 0.25f);

            for (int i = 0; i <= N; i++)
            {
                float t = (float)i / N;
                float u = 1f - t;
                curvePoints[i] = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;

                Vector3 tan = 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
                if (tan.sqrMagnitude < 0.0001f) tan = dir;
                tangents[i] = tan.normalized;
            }
        }
        else
        {
            // Trường hợp B: Dây phân nhánh chữ Y (Y-Branch Split)
            // Đoạn thân chung nhô ra từ cọc A -> ngã ba jA -> uốn cong sang B
            int splitIndexA = isBranchA ? Mathf.Max(3, N / 4) : 0;
            int splitIndexB = isBranchB ? Mathf.Min(N - 3, N - (N / 4)) : N;

            // Tính điểm kiểm soát cho phần cung ở giữa
            Vector3 midStart = isBranchA ? jA : p0;
            Vector3 midEnd = isBranchB ? jB : p3;
            float midDist = Vector3.Distance(midStart, midEnd);
            float midArch = Mathf.Clamp(midDist * archFactor, archHeight * 0.5f, archHeight * 2.2f);
            Vector3 midDir = (midEnd - midStart).normalized;
            Vector3 mp1 = midStart + Vector3.up * midArch + midDir * (midDist * 0.25f);
            Vector3 mp2 = midEnd + Vector3.up * midArch - midDir * (midDist * 0.25f);

            for (int i = 0; i <= N; i++)
            {
                if (isBranchA && i <= splitIndexA)
                {
                    // Thân chung từ cọc A đến nút ngã ba jA
                    float tLocal = (float)i / splitIndexA;
                    float smoothT = Mathf.SmoothStep(0f, 1f, tLocal);
                    curvePoints[i] = Vector3.Lerp(p0, jA, smoothT) + Vector3.up * (0.005f * Mathf.Sin(tLocal * Mathf.PI));
                    tangents[i] = (jA - p0).normalized;
                }
                else if (isBranchB && i >= splitIndexB)
                {
                    // Thân chung từ nút ngã ba jB đến cọc B
                    float tLocal = (float)(i - splitIndexB) / (N - splitIndexB);
                    float smoothT = Mathf.SmoothStep(0f, 1f, tLocal);
                    curvePoints[i] = Vector3.Lerp(jB, p3, smoothT) + Vector3.up * (0.005f * Mathf.Sin(tLocal * Mathf.PI));
                    tangents[i] = (p3 - jB).normalized;
                }
                else
                {
                    // Nhánh vòng cung uốn lượn ở giữa
                    int midStartIdx = isBranchA ? splitIndexA : 0;
                    int midEndIdx = isBranchB ? splitIndexB : N;
                    float tLocal = (float)(i - midStartIdx) / (midEndIdx - midStartIdx);
                    float uLocal = 1f - tLocal;

                    curvePoints[i] = uLocal * uLocal * uLocal * midStart 
                                   + 3f * uLocal * uLocal * tLocal * mp1 
                                   + 3f * uLocal * tLocal * tLocal * mp2 
                                   + tLocal * tLocal * tLocal * midEnd;

                    Vector3 tan = 3f * uLocal * uLocal * (mp1 - midStart) 
                                + 6f * uLocal * tLocal * (mp2 - mp1) 
                                + 3f * tLocal * tLocal * (midEnd - mp2);
                    if (tan.sqrMagnitude < 0.0001f) tan = midDir;
                    tangents[i] = tan.normalized;
                }
            }
        }

        // 2. HỆ TRỤC TỌA ĐỘ QUAY (Parallel Transport Frame) chống xoắn vặn
        Vector3[] normals = new Vector3[N + 1];
        Vector3[] binormals = new Vector3[N + 1];

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
        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] meshNormals = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];

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
        int[] triangles = new int[triCount];
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
    }

    /// <summary>
    /// Cập nhật vị trí và hình dáng của toàn bộ các dây đang kết nối theo thời gian thực.
    /// Tự động khử trùng lặp đầu giắc cắm khi nhiều dây cắm vào cùng 1 cọc.
    /// </summary>
    private void UpdateActiveWirePositions()
    {
        HashSet<Transform> seenTerminals = new HashSet<Transform>();

        for (int i = activeWires.Count - 1; i >= 0; i--)
        {
            var wire = activeWires[i];

            if (wire.terminalA == null || wire.terminalB == null || wire.wireObject == null)
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
                continue;
            }

            // Quản lý đầu giắc cắm: Cực nào đã có giắc cắm từ dây trước thì dây sau cắm vào cùng cực đó
            // sẽ ẩn đầu giắc cắm thừa để ôm khít cọc và không bị lồng đè lên nhau
            bool isFirstOnA = seenTerminals.Add(wire.terminalA);
            if (wire.pivotA != null) wire.pivotA.SetActive(isFirstOnA);

            bool isFirstOnB = seenTerminals.Add(wire.terminalB);
            if (wire.pivotB != null) wire.pivotB.SetActive(isFirstOnB);

            UpdateWireGeometry(wire);
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

            if (match)
            {
                if (wire.wireMesh != null) Destroy(wire.wireMesh);
                if (wire.wireObject != null) Destroy(wire.wireObject);
                activeWires.RemoveAt(i);
                UpdateActiveWirePositions();
                return true;
            }
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
    private void HighlightTerminal(Transform terminal, bool highlight)
    {
        if (highlight)
        {
            firstSelectedRenderer = terminal.GetComponent<Renderer>();
            if (firstSelectedRenderer != null && firstSelectedRenderer.material != null)
            {
                Material mat = firstSelectedRenderer.material;
                if (mat.HasProperty("_BaseColor"))
                {
                    originalTerminalColor = mat.GetColor("_BaseColor");
                    hasOriginalColor = true;
                    mat.SetColor("_BaseColor", highlightColor);
                }
                else if (mat.HasProperty("_Color"))
                {
                    originalTerminalColor = mat.color;
                    hasOriginalColor = true;
                    mat.color = highlightColor;
                }
            }
        }
        else
        {
            if (firstSelectedRenderer != null && hasOriginalColor && firstSelectedRenderer.material != null)
            {
                Material mat = firstSelectedRenderer.material;
                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", originalTerminalColor);
                }
                else if (mat.HasProperty("_Color"))
                {
                    mat.color = originalTerminalColor;
                }
            }
            firstSelectedRenderer = null;
            hasOriginalColor = false;
        }
    }

    /// <summary>
    /// Huỷ trạng thái đang chọn cực
    /// </summary>
    public void CancelSelection()
    {
        if (firstSelectedTerminal != null)
        {
            HighlightTerminal(firstSelectedTerminal, false);
            firstSelectedTerminal = null;
        }
    }

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
        activeWires.Clear();
        CancelSelection();
        Debug.Log("[WireConnection] Đã xoá toàn bộ dây nối.");
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
