using UnityEngine;
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;

public class TapToPlaceController : MonoBehaviour
{
    [Header("Tham chiếu Hệ thống")]
    public ButtonInteractor buttonInteractor;
    public MenuHUDController menuHUD;
    public SinglePlaneLockController planeLock;
    public WireConnectionController wireConnectionController;

    [Header("Prefab linh kiện (Khớp tên với Menu HUD)")]
    public GameObject[] componentPrefabs;
    public string[] componentNames;

    [Header("Cấu hình Preview & Di chuyển")]
    [Range(0.1f, 1f)] public float previewAlpha = 0.6f;
    public float scaleMultiplier = 0.035f;

    [Header("--- LỊCH SỬ LINH KIỆN (HỖ TRỢ UNDO / THU HỒI) ---")]
    public List<GameObject> placedObjectsHistory = new List<GameObject>();

    // Hệ thống quản lý lịch sử thao tác chuyên sâu (Requirement 2 & 3: Command History)
    public CircuitHistoryManager History { get; } = new CircuitHistoryManager();

    private GameObject previewAnchor;
    private GameObject previewVisual;
    private GameObject currentPrefabToPlace;
    public GameObject CurrentPrefabToPlace => currentPrefabToPlace;

    private bool canPlace = false;
    private float cooldownTimer = 0f;

    // --- DI CHUYỂN VẬT THỂ (DRAG & DROP) ---
    private GameObject draggedObject = null;
    public GameObject DraggedObject => draggedObject;
    private bool isDragging = false;
    public bool IsDragging => isDragging;
    private bool isHoveringTrash = false;
    private ARFloatingBubbleMenu floatingMenu = null;
    private float unpinchGraceTimer = 0f;
    private Vector3 lastValidDragPos = Vector3.zero;
    private Quaternion lastValidDragRot = Quaternion.identity;
    private Vector3 dragStartPos = Vector3.zero;
    private Quaternion dragStartRot = Quaternion.identity;
    private Vector3 currentSmoothedDragPos = Vector3.zero;
    private bool isDragBlocked = false;
    private Camera mainCamera;

    [Header("--- QUẢN LÝ CHỌN LINH KIỆN ĐỂ XÓA (REQUIREMENT 3) ---")]
    public GameObject SelectedComponent { get; private set; }
    public event System.Action<GameObject> OnSelectedComponentChanged;
    private Dictionary<Renderer, Material[]> originalSelectedMaterials = new Dictionary<Renderer, Material[]>();
    private Material selectionHighlightMat = null;

    /// <summary>
    /// Chọn một linh kiện trên bàn và hiển thị visual feedback
    /// </summary>
    public void SelectComponent(GameObject obj)
    {
        if (SelectedComponent == obj) return;

        ClearSelectionHighlight();
        SelectedComponent = obj;

        if (SelectedComponent != null)
        {
            ApplySelectionHighlight(SelectedComponent);
        }

        OnSelectedComponentChanged?.Invoke(SelectedComponent);
    }

    /// <summary>
    /// Bỏ chọn linh kiện hiện tại và khôi phục material
    /// </summary>
    public void ClearSelectedComponent()
    {
        if (SelectedComponent == null) return;
        ClearSelectionHighlight();
        SelectedComponent = null;
        OnSelectedComponentChanged?.Invoke(null);
    }

    /// <summary>
    /// Xóa linh kiện đang được chọn
    /// </summary>
    public void DeleteSelectedComponent()
    {
        if (SelectedComponent != null)
        {
            GameObject target = SelectedComponent;
            ClearSelectedComponent();
            DeletePlacedComponent(target);
        }
    }

    private void ApplySelectionHighlight(GameObject obj)
    {
        if (obj == null) return;
        Renderer[] rens = obj.GetComponentsInChildren<Renderer>(true);
        originalSelectedMaterials.Clear();

        if (selectionHighlightMat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            selectionHighlightMat = new Material(shader);
            if (selectionHighlightMat.HasProperty("_BaseColor")) selectionHighlightMat.SetColor("_BaseColor", new Color(0.2f, 0.85f, 1f, 0.9f));
            else if (selectionHighlightMat.HasProperty("_Color")) selectionHighlightMat.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            if (selectionHighlightMat.HasProperty("_EmissionColor"))
            {
                selectionHighlightMat.EnableKeyword("_EMISSION");
                selectionHighlightMat.SetColor("_EmissionColor", new Color(0f, 0.5f, 0.9f) * 0.8f);
            }
        }

        foreach (var r in rens)
        {
            if (r is LineRenderer) continue;
            originalSelectedMaterials[r] = r.sharedMaterials;
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = selectionHighlightMat;
            r.materials = mats;
        }
    }

    private void ClearSelectionHighlight()
    {
        foreach (var kvp in originalSelectedMaterials)
        {
            if (kvp.Key != null && kvp.Value != null)
            {
                kvp.Key.sharedMaterials = kvp.Value;
            }
        }
        originalSelectedMaterials.Clear();
    }

    /// <summary>
    /// Đồng bộ trạng thái kích hoạt của linh kiện từ Undo/Redo
    /// </summary>
    public void NotifyComponentToggled(GameObject obj, bool active)
    {
        if (obj == null) return;
        if (active)
        {
            if (!placedObjectsHistory.Contains(obj)) placedObjectsHistory.Add(obj);
        }
        else
        {
            if (SelectedComponent == obj)
            {
                ClearSelectedComponent();
            }
            placedObjectsHistory.Remove(obj);
        }
    }

    void Start()
    {
        mainCamera = Camera.main;
        if (buttonInteractor == null) buttonInteractor = FindObjectOfType<ButtonInteractor>();
        if (menuHUD == null) menuHUD = FindObjectOfType<MenuHUDController>();
        if (planeLock == null) planeLock = FindObjectOfType<SinglePlaneLockController>();
        if (wireConnectionController == null) wireConnectionController = FindObjectOfType<WireConnectionController>();
    }

    void Update()
    {
        if (planeLock == null) planeLock = FindObjectOfType<SinglePlaneLockController>();
        if (planeLock == null || !planeLock.HasLockedPlane)
        {
            if (previewAnchor != null) Destroy(previewAnchor);
            return;
        }

        if (buttonInteractor == null) buttonInteractor = FindObjectOfType<ButtonInteractor>();
        if (buttonInteractor == null) return;

        if (wireConnectionController == null) wireConnectionController = FindObjectOfType<WireConnectionController>();

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        if (cooldownTimer > 0) cooldownTimer -= Time.deltaTime;

        // 1. CHẾ ĐỘ ĐẶT VẬT MỚI (Từ MenuHUD hoặc ARFloatingBubbleMenu gọi trực tiếp)
        bool hasSelection = (menuHUD != null && menuHUD.HasSelection);
        bool hasDirectPreview = (currentPrefabToPlace != null && previewAnchor != null);

        if (hasSelection || hasDirectPreview)
        {
            if (isDragging) ReleaseDraggedObject();

            if (hasSelection && currentPrefabToPlace == null)
            {
                StartPreview(menuHUD.SelectedComponent);
            }

            UpdatePreviewAndPlacement();
        }
        // 2. CHẾ ĐỘ DI CHUYỂN VẬT CŨ (DRAG & DROP)
        else
        {
            if (previewAnchor != null)
            {
                Destroy(previewAnchor);
                currentPrefabToPlace = null;
            }

            HandleDragAndDrop();
        }
    }

    /// <summary>
    /// Cập nhật vị trí bóng xem trước (Preview), kiểm tra chống chồng lấn và đặt vật thể
    /// </summary>
    private void UpdatePreviewAndPlacement()
    {
        if (currentPrefabToPlace == null || previewAnchor == null) return;

        Vector2 screenPos = GetCursorScreenPosition();
        Ray ray = mainCamera.ScreenPointToRay(screenPos);

        // Dùng RaycastAll để tia nhìn xuyên qua preview và không bao giờ tự chặn chính nó
        RaycastHit[] hits = Physics.RaycastAll(ray, 20f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        bool foundBoard = false;
        RaycastHit boardHit = default;

        foreach (var h in hits)
        {
            if (previewAnchor != null && h.transform.IsChildOf(previewAnchor.transform)) continue;

            if (h.collider.CompareTag("CircuitBoard") || h.collider.name.Contains("Board"))
            {
                boardHit = h;
                foundBoard = true;
                break;
            }
        }

        if (foundBoard)
        {
            // Cố định vị trí và xoay áp sát mặt bảng mạch TRƯỚC KHI kiểm tra va chạm
            previewAnchor.transform.position = boardHit.point;
            previewAnchor.transform.rotation = boardHit.collider.transform.rotation;

            // Kiểm tra chống chồng lấn linh kiện (Anti-Overlap Requirement 2)
            bool isOverlapping = CheckOverlap(boardHit.point, null, out GameObject overlapObj);

            if (!previewAnchor.activeSelf) previewAnchor.SetActive(true);

            if (isOverlapping)
            {
                // Cảnh báo thị giác trực quan: Đổi màu bóng Preview sang ĐỎ CẢNH BÁO
                SetPreviewColor(previewVisual, new Color(1f, 0.22f, 0.22f, 0.75f));
                canPlace = false; // TỪ CHỐI ĐẶT
            }
            else
            {
                // Vị trí hợp lệ: Khôi phục màu sắc gốc từ prefab và điều chỉnh độ trong suốt (Requirement 5)
                RestoreOriginalMaterials(previewVisual, currentPrefabToPlace);
                SetPreviewTransparent(previewVisual, previewAlpha);
                if (!buttonInteractor.isPinching && cooldownTimer <= 0)
                {
                    canPlace = true;
                }
            }

            if (canPlace && !isOverlapping && buttonInteractor.isPinching && cooldownTimer <= 0)
            {
                PlaceObject(boardHit.point, boardHit.collider.transform.rotation, boardHit.collider.transform);
                canPlace = false;
                cooldownTimer = 0.8f;
            }
            return;
        }

        // Khi con trỏ chưa trúng mặt bàn: hiển thị linh kiện lơ lửng trước camera theo hướng nhìn/con trỏ
        Vector3 floatingPos = ray.GetPoint(0.55f);
        previewAnchor.transform.position = floatingPos;
        previewAnchor.transform.rotation = Quaternion.LookRotation(mainCamera.transform.forward, Vector3.up);
        RestoreOriginalMaterials(previewVisual, currentPrefabToPlace);
        SetPreviewTransparent(previewVisual, previewAlpha);
        if (!previewAnchor.activeSelf) previewAnchor.SetActive(true);
        canPlace = false;
    }

    /// <summary>
    /// Đặt linh kiện thật xuống mặt bảng mạch và lưu vào lịch sử Undo
    /// </summary>
    private void PlaceObject(Vector3 position, Quaternion rotation, Transform boardParent)
    {
        if (!previewAnchor.activeSelf) return;

        // Bật lại toàn bộ collider cho vật thể khi đặt xuống
        Collider[] colliders = previewVisual.GetComponentsInChildren<Collider>();
        foreach (var c in colliders)
        {
            c.enabled = true;
        }

        // 1. Khôi phục độ đục và khôi phục 100% material ban đầu của prefab (Requirement 5)
        SetPreviewTransparent(previewVisual, 1f);
        RestoreOriginalMaterials(previewVisual, currentPrefabToPlace);
        previewAnchor.name = "Placed_" + currentPrefabToPlace.name;

        // 2. KHẮC PHỤC TRIỆT ĐỂ LỖI HÌNH BỊ MÉO:
        Transform rootAnchor = boardParent.parent != null ? boardParent.parent : boardParent;
        previewAnchor.transform.SetParent(rootAnchor, true);

        // Ép Scale chuẩn xác tuyệt đối
        previewAnchor.transform.localScale = Vector3.one;
        if (previewVisual != null)
        {
            previewVisual.transform.localScale = Vector3.one * scaleMultiplier;
        }

        // Đảm bảo luôn có Body Collider cho linh kiện để có thể kéo thả (Move Mode)
        // và độc lập hoàn toàn với Terminal Collider (Guardrail 6)
        bool hasBodyCollider = false;
        Collider[] allCols = previewAnchor.GetComponentsInChildren<Collider>();
        foreach (var c in allCols)
        {
            if (!c.CompareTag("Terminal"))
            {
                hasBodyCollider = true;
                break;
            }
        }

        if (!hasBodyCollider)
        {
            BoxCollider box = previewAnchor.AddComponent<BoxCollider>();
            Renderer[] allRens = previewAnchor.GetComponentsInChildren<Renderer>();
            if (allRens != null && allRens.Length > 0)
            {
                Bounds b = allRens[0].bounds;
                for (int i = 1; i < allRens.Length; i++)
                {
                    if (allRens[i] is LineRenderer) continue;
                    b.Encapsulate(allRens[i].bounds);
                }
                box.center = previewAnchor.transform.InverseTransformPoint(b.center);
                box.size = b.size;
            }
        }

        // Lưu vào danh sách linh kiện và ghi nhận vào Lịch Sử Thao Tác (Requirement 1 & 2)
        placedObjectsHistory.Add(previewAnchor);
        History.RecordAction(new PlaceComponentAction(previewAnchor, this));
        Debug.Log($"<color=green>[TapToPlace]</color> Đã đặt thành công: [{previewAnchor.name}]. Tổng số linh kiện: {placedObjectsHistory.Count}");

        previewAnchor = null;
        previewVisual = null;
        currentPrefabToPlace = null;
        menuHUD.ClearSelection();
    }

    /// <summary>
    /// Xử lý kéo thả linh kiện (Drag & Drop) mượt mà có kiểm tra chống chồng lấn và Wire Mode (Requirement 2 & 3)
    /// </summary>
    private void HandleDragAndDrop()
    {
        // QUY TẮC RÀNG BUỘC REQUIREMENT 3:
        // Trong Chế Độ Nối Dây (Wire Mode ON), KHÔNG được xử lý kéo di chuyển linh kiện!
        if (wireConnectionController != null && wireConnectionController.IsWireMode)
        {
            if (isDragging) ReleaseDraggedObject();
            return;
        }

        Vector2 screenPos = GetCursorScreenPosition();
        Ray ray = mainCamera.ScreenPointToRay(screenPos);

        // 1. BẮT ĐẦU KÉO / CHỌN LINH KIỆN: Khi người dùng bấm chụm ngón tay vào vật thể
        if (buttonInteractor.JustPinched && !isDragging)
        {
            RaycastHit[] allHits = Physics.RaycastAll(ray, 20f);
            System.Array.Sort(allHits, (a, b) => a.distance.CompareTo(b.distance));

            // Nếu người dùng pinch trúng trực tiếp vào cực (Terminal) đầu tiên -> nhường cho WireConnectionController
            if (allHits.Length > 0 && allHits[0].collider.CompareTag("Terminal"))
            {
                return;
            }

            bool hitPlaced = false;
            for (int i = 0; i < allHits.Length; i++)
            {
                if (allHits[i].collider.CompareTag("Terminal")) continue;

                Transform rootObj = allHits[i].collider.transform;
                while (rootObj != null)
                {
                    if (rootObj.name.StartsWith("Placed_"))
                    {
                        draggedObject = rootObj.gameObject;
                        isDragging = true;
                        dragStartPos = draggedObject.transform.position;
                        dragStartRot = draggedObject.transform.rotation;
                        lastValidDragPos = dragStartPos;
                        lastValidDragRot = dragStartRot;
                        currentSmoothedDragPos = dragStartPos;
                        isDragBlocked = false;
                        unpinchGraceTimer = 0f;

                        // Kích hoạt Drag-to-Trash UI trên màn hình
                        if (floatingMenu == null) floatingMenu = FindObjectOfType<ARFloatingBubbleMenu>();
                        if (floatingMenu != null) floatingMenu.SetTrashZoneVisible(true);

                        SelectComponent(draggedObject);
                        hitPlaced = true;
                        break;
                    }
                    rootObj = rootObj.parent;
                }
                if (isDragging) break;
            }

            // Nếu pinch vào mặt bàn trống không trúng linh kiện nào -> Bỏ chọn linh kiện hiện tại
            if (!hitPlaced)
            {
                for (int i = 0; i < allHits.Length; i++)
                {
                    if (allHits[i].collider.CompareTag("CircuitBoard") || allHits[i].collider.name.Contains("Board"))
                    {
                        ClearSelectedComponent();
                        break;
                    }
                }
            }
        }

        // 2. ĐANG KÉO: Cập nhật vị trí vật thể trượt mượt mà kèm kiểm tra CHỐNG CHỒNG LẤN
        if (isDragging && draggedObject != null)
        {
            if (buttonInteractor.isPinching)
            {
                unpinchGraceTimer = 0f;

                RaycastHit[] hits = Physics.RaycastAll(ray, 20f);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                bool foundBoard = false;
                RaycastHit boardHit = default;

                foreach (var h in hits)
                {
                    if (h.transform.IsChildOf(draggedObject.transform)) continue;
                    if (h.collider.CompareTag("Terminal")) continue;

                    if (h.collider.CompareTag("CircuitBoard") || h.collider.name.Contains("Board"))
                    {
                        boardHit = h;
                        foundBoard = true;
                        break;
                    }
                }

                // Kiểm tra xem con trỏ kéo có đang nằm trên Thùng Rác (Drag-to-Trash) không
                if (floatingMenu == null) floatingMenu = FindObjectOfType<ARFloatingBubbleMenu>();
                bool overTrash = (floatingMenu != null && floatingMenu.IsPointerOverTrashZone(screenPos));
                isHoveringTrash = overTrash;
                if (floatingMenu != null) floatingMenu.SetTrashZoneHovered(overTrash);

                if (foundBoard && !overTrash)
                {
                    // Bộ lọc làm mượt di chuyển:
                    // Bỏ qua micro-jitter (< 1.5mm) từ landmark MediaPipe để tránh rung vật
                    Vector3 targetPoint = boardHit.point;
                    if (Vector3.Distance(targetPoint, currentSmoothedDragPos) > 0.0015f)
                    {
                        float followSpeed = 26f; // Tốc độ phản hồi cực nhanh ~38ms, không gây cảm giác trễ
                        currentSmoothedDragPos = Vector3.Lerp(currentSmoothedDragPos, targetPoint, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
                    }

                    // Kiểm tra chống chồng lấn với dải trễ (hysteresis) trong lúc drag để tránh snap giật mép va chạm
                    float margin = isDragBlocked ? 0.008f : 0f;
                    bool isOverlapping = CheckOverlap(currentSmoothedDragPos, draggedObject, out GameObject overlapObj, margin);

                    if (!isOverlapping)
                    {
                        isDragBlocked = false;
                        draggedObject.transform.position = currentSmoothedDragPos;
                        draggedObject.transform.rotation = boardHit.collider.transform.rotation;
                        lastValidDragPos = currentSmoothedDragPos;
                        lastValidDragRot = boardHit.collider.transform.rotation;
                    }
                    else
                    {
                        isDragBlocked = true;
                        // Giữ nguyên ở điểm hợp lệ gần nhất, ngăn không cho đâm xuyên và không snap giật
                        draggedObject.transform.position = lastValidDragPos;
                        draggedObject.transform.rotation = lastValidDragRot;
                    }

                    // Cập nhật tọa độ dây nối theo thời gian thực khi di chuyển linh kiện
                    if (wireConnectionController != null)
                    {
                        wireConnectionController.UpdateActiveWirePositionsExternal();
                    }
                }
                else if (overTrash)
                {
                    // Khi đang rê trên thùng rác: giữ nguyên vị trí trước đó
                    draggedObject.transform.position = lastValidDragPos;
                }
            }
            else
            {
                // Thêm độ trễ nhỏ (0.12s) chống rung tay nhả pinch vô tình
                unpinchGraceTimer += Time.deltaTime;
                if (unpinchGraceTimer > 0.12f)
                {
                    ReleaseDraggedObject();
                }
            }
        }
    }

    private void ReleaseDraggedObject()
    {
        if (draggedObject != null)
        {
            // Trường hợp 1: Nhả tay trên Thùng Rác -> XÓA LINH KIỆN (Drag-to-Trash)
            if (isHoveringTrash)
            {
                GameObject objToDelete = draggedObject;
                isDragging = false;
                draggedObject = null;
                isHoveringTrash = false;
                if (floatingMenu != null) floatingMenu.SetTrashZoneVisible(false);

                DeletePlacedComponent(objToDelete);
                ClearSelectedComponent();
                Debug.Log($"<color=red>[TapToPlace]</color> Đã kéo linh kiện [{objToDelete.name}] vào thùng rác để xóa thành công.");
                return;
            }

            if (floatingMenu != null) floatingMenu.SetTrashZoneVisible(false);

            // Trường hợp 2: Thả tay bình thường trên bàn -> Đảm bảo không bị chồng lấn
            bool isOverlapping = CheckOverlap(draggedObject.transform.position, draggedObject, out _);
            if (isOverlapping && lastValidDragPos != Vector3.zero)
            {
                draggedObject.transform.position = lastValidDragPos;
                draggedObject.transform.rotation = lastValidDragRot;
            }

            // Ghi nhận vào lịch sử thao tác nếu có dịch chuyển (Requirement 2 & 3)
            float moveDist = Vector3.Distance(dragStartPos, draggedObject.transform.position);
            if (moveDist > 0.006f)
            {
                History.RecordAction(new MoveComponentAction(
                    draggedObject,
                    dragStartPos,
                    dragStartRot,
                    draggedObject.transform.position,
                    draggedObject.transform.rotation,
                    wireConnectionController
                ));
            }
        }

        if (floatingMenu != null) floatingMenu.SetTrashZoneVisible(false);
        isDragging = false;
        draggedObject = null;
        isHoveringTrash = false;
        unpinchGraceTimer = 0f;
        isDragBlocked = false;
    }

    private Vector2 GetCursorScreenPosition()
    {
        if (buttonInteractor.debugPoint == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Canvas canvas = buttonInteractor.debugPoint.GetComponentInParent<Canvas>();
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
        return RectTransformUtility.WorldToScreenPoint(uiCam, buttonInteractor.debugPoint.position);
    }

    /// <summary>
    /// Bắt đầu hiển thị bóng xem trước (Preview) cho linh kiện được chọn
    /// </summary>
    public void StartPreview(string componentName)
    {
        ClearPreview();

        GameObject prefab = GetPrefabByName(componentName);
        if (prefab == null)
        {
            Debug.LogWarning("<color=orange>[TapToPlace]</color> Không tìm thấy prefab phù hợp cho: " + componentName);
            return;
        }

        currentPrefabToPlace = prefab;
        previewAnchor = new GameObject("PreviewAnchor_" + prefab.name);
        previewVisual = Instantiate(prefab);
        previewVisual.transform.SetParent(previewAnchor.transform, false);
        previewVisual.transform.localScale = Vector3.one * scaleMultiplier;

        // VÔ HIỆU HOÁ TOÀN BỘ COLLIDER KHI PREVIEW ĐỂ KHÔNG TỰ CHẶN RAYCAST (KHẮC PHỤC TRIỆT ĐỂ LỖI NHÁP NHÁY)
        Collider[] colliders = previewVisual.GetComponentsInChildren<Collider>();
        foreach (var c in colliders)
        {
            c.enabled = false;
        }

        AlignVisualBaseToAnchor(previewAnchor, previewVisual);
        SetPreviewTransparent(previewVisual, previewAlpha);
        previewAnchor.SetActive(true); // Hiển thị ngay lập tức để người dùng nhìn thấy!

        canPlace = false;
        cooldownTimer = 0.4f;
    }

    /// <summary>
    /// Huỷ bóng xem trước và xoá trạng thái chọn
    /// </summary>
    public void ClearPreview()
    {
        if (previewAnchor != null)
        {
            Destroy(previewAnchor);
            previewAnchor = null;
            previewVisual = null;
            currentPrefabToPlace = null;
        }
        canPlace = false;
        if (menuHUD != null) menuHUD.ClearSelection();
    }

    /// <summary>
    /// Hủy hoàn toàn chế độ đặt linh kiện (Cancel Placement Preview - Guardrail 5)
    /// </summary>
    public void CancelPlacement()
    {
        ClearPreview();
        canPlace = false;
        isDragging = false;
        draggedObject = null;
        isHoveringTrash = false;
        if (floatingMenu != null) floatingMenu.SetTrashZoneVisible(false);
        Debug.Log("<color=yellow>[TapToPlace]</color> Đã hủy chế độ đặt linh kiện (Placement Cancelled).");
    }

    private void AlignVisualBaseToAnchor(GameObject anchor, GameObject visual)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }

        float bottomOffsetWorld = combined.min.y - anchor.transform.position.y;
        Vector3 worldCorrection = new Vector3(0, -bottomOffsetWorld, 0);
        visual.transform.position += worldCorrection;
    }

    private GameObject GetPrefabByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string search = name.Trim().ToLowerInvariant();

        // 1. Tìm chính xác hoặc tương đối theo mảng componentNames
        if (componentNames != null && componentPrefabs != null)
        {
            for (int i = 0; i < componentNames.Length; i++)
            {
                if (i < componentPrefabs.Length && componentPrefabs[i] != null)
                {
                    string configuredName = componentNames[i].Trim().ToLowerInvariant();
                    if (configuredName == search || configuredName.Contains(search) || search.Contains(configuredName))
                    {
                        return componentPrefabs[i];
                    }
                }
            }
        }

        // 2. Tìm theo tên Prefab trong componentPrefabs
        if (componentPrefabs != null)
        {
            for (int i = 0; i < componentPrefabs.Length; i++)
            {
                if (componentPrefabs[i] == null) continue;
                string pName = componentPrefabs[i].name.ToLowerInvariant();
                if (pName == search || pName.Contains(search) || search.Contains(pName))
                {
                    return componentPrefabs[i];
                }

                // Nhận diện theo từ khóa thông dụng
                if ((search.Contains("pin") || search.Contains("battery") || search.Contains("nguon")) && pName.Contains("battery")) return componentPrefabs[i];
                if ((search.Contains("den") || search.Contains("lamp") || search.Contains("bong")) && pName.Contains("lamp")) return componentPrefabs[i];
                if ((search.Contains("cong") || search.Contains("tac") || search.Contains("key") || search.Contains("switch")) && pName.Contains("key")) return componentPrefabs[i];
                if ((search.Contains("ampe") || search.Contains("ammetr") || search.Contains("ammeter")) && pName.Contains("ammetr")) return componentPrefabs[i];
                if ((search.Contains("von") || search.Contains("volt") || search.Contains("voltmeter")) && pName.Contains("volt")) return componentPrefabs[i];
            }
        }

        return null;
    }

    private void SetPreviewTransparent(GameObject obj, float alpha)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (r is LineRenderer) continue;
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = alpha;
                    mat.SetColor("_BaseColor", c);
                }
                else if (mat.HasProperty("_Color"))
                {
                    Color c = mat.color;
                    c.a = alpha;
                    mat.color = c;
                }
            }
        }
    }

    /// <summary>
    /// Đổi màu hiển thị của bóng xem trước (đỏ khi vị trí bị trùng/chồng lấn, trắng mờ khi hợp lệ)
    /// </summary>
    private void SetPreviewColor(GameObject obj, Color tintColor)
    {
        if (obj == null) return;
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (r is LineRenderer) continue;
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", tintColor);
                }
                else if (mat.HasProperty("_Color"))
                {
                    mat.color = tintColor;
                }
            }
        }
    }

    /// <summary>
    /// Thu hồi / Hoàn tác thao tác gần nhất trong lịch sử (Requirement 2)
    /// </summary>
    public bool UndoLastPlacedComponent()
    {
        return History.Undo();
    }

    /// <summary>
    /// Làm lại thao tác vừa hoàn tác trong lịch sử (Requirement 3: Redo)
    /// </summary>
    public bool RedoLastAction()
    {
        return History.Redo();
    }

    /// <summary>
    /// Xóa linh kiện và lưu vết vào lịch sử Undo (Requirement 1 & 2)
    /// </summary>
    public void DeletePlacedComponent(GameObject targetObj)
    {
        if (targetObj == null) return;

        // Nếu đối tượng đang được chọn, dọn dẹp highlight và selection trước khi ẩn
        if (SelectedComponent == targetObj)
        {
            ClearSelectionHighlight();
            SelectedComponent = null;
            OnSelectedComponentChanged?.Invoke(null);
        }

        string objName = targetObj.name;

        // Lưu danh sách dây kết nối trước khi ngắt để phục vụ Undo
        List<(Transform, Transform)> attachedWires = null;
        if (wireConnectionController != null)
        {
            attachedWires = wireConnectionController.GetConnectedWiresForComponent(targetObj.transform);
            wireConnectionController.RemoveWiresConnectedTo(targetObj.transform);
        }

        placedObjectsHistory.Remove(targetObj);
        targetObj.SetActive(false);

        // Lưu vào History (không Destroy để bảo toàn nguyên vẹn tham chiếu và Terminal)
        History.RecordAction(new DeleteComponentAction(targetObj, this, wireConnectionController, attachedWires));
        Debug.Log($"<color=green>[TapToPlace]</color> Đã chuyển linh kiện [{objName}] sang trạng thái ẩn và lưu vết vào History.");
    }

    /// <summary>
    /// Khôi phục chính xác toàn bộ material gốc của prefab (Requirement 5: không làm đổi sang màu trắng)
    /// </summary>
    private void RestoreOriginalMaterials(GameObject visualInstance, GameObject sourcePrefab)
    {
        if (visualInstance == null || sourcePrefab == null) return;

        Renderer[] instanceRenderers = visualInstance.GetComponentsInChildren<Renderer>(true);
        Renderer[] prefabRenderers = sourcePrefab.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < instanceRenderers.Length && i < prefabRenderers.Length; i++)
        {
            if (instanceRenderers[i] is LineRenderer) continue;
            if (prefabRenderers[i] != null && prefabRenderers[i].sharedMaterials != null)
            {
                instanceRenderers[i].sharedMaterials = prefabRenderers[i].sharedMaterials;
            }
        }
    }

    /// <summary>
    public struct FootprintOBB2D
    {
        public Vector2 center;      // Tâm OBB trong mặt phẳng 2D (x, z)
        public Vector2 halfExtents; // Bán kính nửa kích thước (width/2, depth/2) tính bằng mét
        public Vector2[] corners;   // 4 đỉnh OBB trong mặt phẳng 2D (x, z)
        public Vector2 axis1;       // Trục pháp tuyến cạnh 1 (normalized)
        public Vector2 axis2;       // Trục pháp tuyến cạnh 2 (normalized)
    }

    /// <summary>
    /// Tính toán hộp bao hướng (Oriented Bounding Box - OBB) 2D trên mặt phẳng X-Z cho một linh kiện.
    /// Tính toán chính xác kích thước thực tế theo mét (đã áp dụng scaleMultiplier = 0.035).
    /// </summary>
    public bool GetFootprintOBB(GameObject obj, Vector3 worldPos, Quaternion worldRot, out FootprintOBB2D obb, float margin = 0f)
    {
        obb = default;
        if (obj == null) return false;

        Vector2 localCenter = Vector2.zero;
        Vector2 localHalfExtents = new Vector2(0.045f, 0.045f); // Giá trị mặc định an toàn 4.5cm x 4.5cm

        Transform rootTransform = obj.transform;

        // Ưu tiên 1: Tìm BoxCollider thân đế (không phải cực Terminal)
        BoxCollider[] boxColliders = obj.GetComponentsInChildren<BoxCollider>();
        BoxCollider bodyBox = null;
        for (int i = 0; i < boxColliders.Length; i++)
        {
            if (boxColliders[i] != null && !boxColliders[i].CompareTag("Terminal"))
            {
                bodyBox = boxColliders[i];
                break;
            }
        }

        if (bodyBox != null)
        {
            Vector3 bc = bodyBox.center;
            Vector3 bh = bodyBox.size * 0.5f;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            // Biến đổi 8 góc của BoxCollider sang toạ độ thế giới rồi đưa về toạ độ của rootTransform (tỉ lệ 1:1 mét)
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 ptLocalToBox = bc + new Vector3(sx * bh.x, sy * bh.y, sz * bh.z);
                        Vector3 ptWorld = bodyBox.transform.TransformPoint(ptLocalToBox);
                        Vector3 ptInRoot = rootTransform.InverseTransformPoint(ptWorld);

                        if (ptInRoot.x < minX) minX = ptInRoot.x;
                        if (ptInRoot.x > maxX) maxX = ptInRoot.x;
                        if (ptInRoot.z < minZ) minZ = ptInRoot.z;
                        if (ptInRoot.z > maxZ) maxZ = ptInRoot.z;
                    }
                }
            }

            if (maxX > minX && maxZ > minZ)
            {
                localCenter = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
                localHalfExtents = new Vector2((maxX - minX) * 0.5f, (maxZ - minZ) * 0.5f);
            }
        }
        else
        {
            // Ưu tiên 2: Thu thập Renderers phần chân đế tiếp xúc mặt bàn
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                float minY = float.MaxValue;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] is LineRenderer) continue;
                    if (renderers[i].bounds.min.y < minY) minY = renderers[i].bounds.min.y;
                }

                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                bool hasValid = false;

                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    if (r is LineRenderer) continue;
                    // Bỏ qua chi tiết nhô cao hơn chân đế 3.5cm (như cần gạt mở khóa K)
                    if (r.bounds.min.y > minY + 0.035f) continue;

                    Bounds b = r.bounds;
                    Vector3 bc = b.center;
                    Vector3 bh = b.extents;

                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        for (int sy = -1; sy <= 1; sy += 2)
                        {
                            for (int sz = -1; sz <= 1; sz += 2)
                            {
                                Vector3 ptWorld = bc + new Vector3(sx * bh.x, sy * bh.y, sz * bh.z);
                                Vector3 ptInRoot = rootTransform.InverseTransformPoint(ptWorld);

                                if (ptInRoot.x < minX) minX = ptInRoot.x;
                                if (ptInRoot.x > maxX) maxX = ptInRoot.x;
                                if (ptInRoot.z < minZ) minZ = ptInRoot.z;
                                if (ptInRoot.z > maxZ) maxZ = ptInRoot.z;
                            }
                        }
                    }
                    hasValid = true;
                }

                if (hasValid && maxX > minX && maxZ > minZ)
                {
                    localCenter = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
                    localHalfExtents = new Vector2((maxX - minX) * 0.5f, (maxZ - minZ) * 0.5f);
                }
            }
        }

        // Giới hạn an toàn (Clamp): Bán kính nửa footprint linh kiện từ 2cm đến 18cm (tính bằng mét)
        localHalfExtents.x = Mathf.Clamp(localHalfExtents.x, 0.02f, 0.18f);
        localHalfExtents.y = Mathf.Clamp(localHalfExtents.y, 0.02f, 0.18f);

        // Áp dụng lề margin
        float hx = localHalfExtents.x + margin;
        float hz = localHalfExtents.y + margin;

        // Vector hướng phẳng trên mặt bàn X-Z
        Vector3 right3 = worldRot * Vector3.right;
        Vector3 fwd3 = worldRot * Vector3.forward;

        Vector2 right2 = new Vector2(right3.x, right3.z);
        if (right2.sqrMagnitude < 0.0001f) right2 = Vector2.right;
        else right2.Normalize();

        Vector2 fwd2 = new Vector2(fwd3.x, fwd3.z);
        if (fwd2.sqrMagnitude < 0.0001f) fwd2 = Vector2.up;
        else fwd2.Normalize();

        // Tâm OBB trong mặt phẳng 2D thế giới
        Vector2 center2D = new Vector2(worldPos.x, worldPos.z) + right2 * localCenter.x + fwd2 * localCenter.y;

        // 4 góc của OBB trong mặt phẳng 2D thế giới
        Vector2[] corners = new Vector2[4];
        corners[0] = center2D - right2 * hx - fwd2 * hz;
        corners[1] = center2D + right2 * hx - fwd2 * hz;
        corners[2] = center2D + right2 * hx + fwd2 * hz;
        corners[3] = center2D - right2 * hx + fwd2 * hz;

        obb.center = center2D;
        obb.halfExtents = new Vector2(hx, hz);
        obb.corners = corners;
        obb.axis1 = right2;
        obb.axis2 = fwd2;

        return true;
    }

    /// <summary>
    /// Kiểm tra va chạm giao nhau giữa 2 OBB 2D bằng định lý trục phân tách (Separating Axis Theorem - SAT).
    /// Bắt buộc cấm overlap dù chỉ giao nhau một phần hoặc chạm góc.
    /// Trả về false nếu có trục phân tách (không overlap), true nếu overlap trên tất cả 4 trục.
    /// </summary>
    public static bool CheckOBBOverlapSAT(FootprintOBB2D a, FootprintOBB2D b, out int separatingAxisIndex)
    {
        separatingAxisIndex = -1;
        Vector2[] axes = new Vector2[] { a.axis1, a.axis2, b.axis1, b.axis2 };

        for (int i = 0; i < 4; i++)
        {
            Vector2 axis = axes[i];
            if (axis.sqrMagnitude < 0.0001f) continue;

            // Chiếu 4 góc của A lên trục
            float minA = float.MaxValue, maxA = float.MinValue;
            for (int j = 0; j < 4; j++)
            {
                float proj = Vector2.Dot(a.corners[j], axis);
                if (proj < minA) minA = proj;
                if (proj > maxA) maxA = proj;
            }

            // Chiếu 4 góc của B lên trục
            float minB = float.MaxValue, maxB = float.MinValue;
            for (int j = 0; j < 4; j++)
            {
                float proj = Vector2.Dot(b.corners[j], axis);
                if (proj < minB) minB = proj;
                if (proj > maxB) maxB = proj;
            }

            // Nếu tồn tại dù chỉ 1 trục phân tách -> 2 hộp hoàn toàn tách rời nhau (không giao)
            if (maxA < minB || maxB < minA)
            {
                separatingAxisIndex = i;
                return false;
            }
        }

        // Không có trục phân tách nào -> 2 hộp giao nhau (overlap)
        return true;
    }

    /// <summary>
    /// Kiểm tra xem vị trí đề xuất (candidatePosition) có bị chồng lấn lên linh kiện đã đặt nào không bằng OBB 2D / SAT.
    /// Cấm chồng lấn dù chỉ giao nhau một phần.
    /// </summary>
    public bool CheckOverlap(Vector3 candidatePosition, GameObject ignoreObject, out GameObject overlappingObject, float marginOffset = 0f)
    {
        overlappingObject = null;

        // 1. Xác định GameObject gốc đại diện cho linh kiện đang được kiểm tra (previewAnchor hoặc draggedObject)
        GameObject candidateObj = (previewAnchor != null)
            ? previewAnchor
            : ((ignoreObject != null) ? ignoreObject : (previewVisual != null ? previewVisual : currentPrefabToPlace));

        Quaternion candidateRot = (previewAnchor != null)
            ? previewAnchor.transform.rotation
            : ((ignoreObject != null) ? ignoreObject.transform.rotation : Quaternion.identity);

        if (candidateObj == null) return false;

        if (!GetFootprintOBB(candidateObj, candidatePosition, candidateRot, out FootprintOBB2D candidateOBB, marginOffset))
        {
            return false;
        }

        // 2. Thu thập danh sách các linh kiện đã đặt đang hoạt động trên bàn
        List<GameObject> activePlaced = new List<GameObject>();
        for (int i = 0; i < placedObjectsHistory.Count; i++)
        {
            GameObject p = placedObjectsHistory[i];
            if (p != null && p.activeInHierarchy && p != candidateObj && p != ignoreObject)
            {
                if (previewAnchor != null && p == previewAnchor) continue;
                if (!activePlaced.Contains(p)) activePlaced.Add(p);
            }
        }

        // Quét bổ sung các linh kiện Placed_ trong Hierarchy
        Transform[] allRoots = FindObjectsOfType<Transform>();
        for (int i = 0; i < allRoots.Length; i++)
        {
            Transform t = allRoots[i];
            if (t != null && t.name.StartsWith("Placed_") && t.gameObject.activeInHierarchy)
            {
                if (t.parent != null && t.parent.name.StartsWith("Placed_")) continue; // Bỏ qua object con
                if (t.gameObject == candidateObj || t.gameObject == ignoreObject) continue;
                if (previewAnchor != null && (t.gameObject == previewAnchor || t.IsChildOf(previewAnchor.transform))) continue;
                if (ignoreObject != null && t.IsChildOf(ignoreObject.transform)) continue;

                if (!activePlaced.Contains(t.gameObject))
                {
                    activePlaced.Add(t.gameObject);
                }
            }
        }

        // 3. Kiểm tra va chạm OBB 2D / SAT với từng linh kiện đã đặt
        for (int i = 0; i < activePlaced.Count; i++)
        {
            GameObject placed = activePlaced[i];
            if (placed == null) continue;

            // Bộ lọc sơ bộ khoảng cách 2D trên mặt bàn X-Z (> 45cm thì chắc chắn không va chạm)
            Vector2 diffXZ = new Vector2(candidatePosition.x - placed.transform.position.x, candidatePosition.z - placed.transform.position.z);
            if (diffXZ.sqrMagnitude > 0.2025f) // (0.45m)^2 = 0.2025
            {
                continue;
            }

            if (GetFootprintOBB(placed, placed.transform.position, placed.transform.rotation, out FootprintOBB2D placedOBB, 0f))
            {
                if (CheckOBBOverlapSAT(candidateOBB, placedOBB, out int separatingAxisIndex))
                {
                    // DEBUG TẠM THỜI theo yêu cầu người dùng:
                    Debug.LogWarning($"<color=red>[CheckOverlap OVERLAP]</color> " +
                        $"Candidate: [{candidateObj.name}] at ({candidatePosition.x:F3}, {candidatePosition.z:F3}), rotY={candidateRot.eulerAngles.y:F1}°, halfExtents=({candidateOBB.halfExtents.x * 100f:F1}cm, {candidateOBB.halfExtents.y * 100f:F1}cm) | " +
                        $"Existing: [{placed.name}] at ({placed.transform.position.x:F3}, {placed.transform.position.z:F3}), rotY={placed.transform.rotation.eulerAngles.y:F1}°, halfExtents=({placedOBB.halfExtents.x * 100f:F1}cm, {placedOBB.halfExtents.y * 100f:F1}cm) | " +
                        $"DistXZ={diffXZ.magnitude * 100f:F1}cm | All 4 SAT axes overlapped!");

                    overlappingObject = placed;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Bán kính footprint tương đương chân đế thực tế (dùng làm helper/fallback nếu cần)
    /// </summary>
    public float GetComponentRadius(GameObject obj)
    {
        if (obj == null) return 0.042f;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            float minY = float.MaxValue;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is LineRenderer) continue;
                if (renderers[i].bounds.min.y < minY) minY = renderers[i].bounds.min.y;
            }

            Bounds combined = default;
            bool hasValid = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is LineRenderer) continue;
                if (renderers[i].bounds.min.y > minY + 0.035f) continue;

                if (!hasValid)
                {
                    combined = renderers[i].bounds;
                    hasValid = true;
                }
                else
                {
                    combined.Encapsulate(renderers[i].bounds);
                }
            }

            if (hasValid)
            {
                float extX = combined.extents.x;
                float extZ = combined.extents.z;
                float footprintRadius = Mathf.Sqrt(extX * extZ);
                if (footprintRadius > 0.015f && footprintRadius < 0.20f) return footprintRadius;
                float avgExt = (extX + extZ) * 0.5f;
                if (avgExt > 0.015f && avgExt < 0.20f) return avgExt;
            }
        }

        Collider col = obj.GetComponentInChildren<Collider>();
        if (col != null && !col.CompareTag("Terminal"))
        {
            float extX = col.bounds.extents.x;
            float extZ = col.bounds.extents.z;
            float footprintRadius = Mathf.Sqrt(extX * extZ);
            if (footprintRadius > 0.015f && footprintRadius < 0.20f) return footprintRadius;
        }

        return 0.042f;
    }
}

// =========================================================================
// HỆ THỐNG COMMAND HISTORY CHO PHÉP UNDO / REDO ĐA THAO TÁC (Requirement 2 & 3)
// =========================================================================

/// <summary>
/// Giao diện đại diện cho một thao tác có thể Hoàn tác (Undo) và Làm lại (Redo).
/// </summary>
public interface IUndoableAction
{
    void Undo();
    void Redo();
    string Description { get; }
}

/// <summary>
/// Hành động đặt một linh kiện mới lên bảng mạch.
/// Quản lý trạng thái bằng SetActive để không hủy GameObject, bảo toàn nguyên vẹn tham chiếu và Terminal.
/// </summary>
public class PlaceComponentAction : IUndoableAction
{
    private readonly GameObject placedObject;
    private readonly TapToPlaceController tapController;

    public string Description => $"Đặt linh kiện [{placedObject?.name}]";

    public PlaceComponentAction(GameObject obj, TapToPlaceController controller)
    {
        placedObject = obj;
        tapController = controller;
    }

    public void Undo()
    {
        if (placedObject != null)
        {
            placedObject.SetActive(false);
            tapController?.NotifyComponentToggled(placedObject, false);
            Debug.Log($"<color=cyan>[History]</color> Undo: Đã ẩn linh kiện [{placedObject.name}]");
        }
    }

    public void Redo()
    {
        if (placedObject != null)
        {
            placedObject.SetActive(true);
            tapController?.NotifyComponentToggled(placedObject, true);
            Debug.Log($"<color=cyan>[History]</color> Redo: Đã khôi phục linh kiện [{placedObject.name}]");
        }
    }
}

/// <summary>
/// Hành động xóa một linh kiện và các dây nối kèm.
/// </summary>
public class DeleteComponentAction : IUndoableAction
{
    private readonly GameObject deletedObject;
    private readonly TapToPlaceController tapController;
    private readonly WireConnectionController wireController;
    private readonly List<(Transform termA, Transform termB)> attachedWires;

    public string Description => $"Xóa linh kiện [{deletedObject?.name}]";

    public DeleteComponentAction(GameObject obj, TapToPlaceController tController, WireConnectionController wController, List<(Transform, Transform)> wires)
    {
        deletedObject = obj;
        tapController = tController;
        wireController = wController;
        attachedWires = wires != null ? new List<(Transform, Transform)>(wires) : new List<(Transform, Transform)>();
    }

    public void Undo()
    {
        if (deletedObject != null)
        {
            deletedObject.SetActive(true);
            tapController?.NotifyComponentToggled(deletedObject, true);

            // Tái lập các dây kết nối trước đó
            if (wireController != null && attachedWires != null)
            {
                foreach (var (a, b) in attachedWires)
                {
                    if (a != null && b != null)
                    {
                        wireController.ConnectWireDirectly(a, b);
                    }
                }
            }
            Debug.Log($"<color=cyan>[History]</color> Undo: Khôi phục linh kiện [{deletedObject.name}] và {attachedWires?.Count ?? 0} dây nối.");
        }
    }

    public void Redo()
    {
        if (deletedObject != null)
        {
            if (wireController != null)
            {
                wireController.RemoveWiresConnectedTo(deletedObject.transform);
            }
            deletedObject.SetActive(false);
            tapController?.NotifyComponentToggled(deletedObject, false);
            Debug.Log($"<color=cyan>[History]</color> Redo: Xóa lại linh kiện [{deletedObject.name}].");
        }
    }
}

/// <summary>
/// Hành động di chuyển linh kiện từ vị trí cũ sang vị trí mới.
/// </summary>
public class MoveComponentAction : IUndoableAction
{
    private readonly GameObject targetObject;
    private readonly Vector3 oldPosition;
    private readonly Quaternion oldRotation;
    private readonly Vector3 newPosition;
    private readonly Quaternion newRotation;
    private readonly WireConnectionController wireController;

    public string Description => $"Di chuyển [{targetObject?.name}]";

    public MoveComponentAction(GameObject obj, Vector3 oldPos, Quaternion oldRot, Vector3 newPos, Quaternion newRot, WireConnectionController wController)
    {
        targetObject = obj;
        oldPosition = oldPos;
        oldRotation = oldRot;
        newPosition = newPos;
        newRotation = newRot;
        wireController = wController;
    }

    public void Undo()
    {
        if (targetObject != null)
        {
            targetObject.transform.position = oldPosition;
            targetObject.transform.rotation = oldRotation;
            wireController?.UpdateActiveWirePositionsExternal();
            Debug.Log($"<color=cyan>[History]</color> Undo: Đưa [{targetObject.name}] về vị trí trước khi di chuyển.");
        }
    }

    public void Redo()
    {
        if (targetObject != null)
        {
            targetObject.transform.position = newPosition;
            targetObject.transform.rotation = newRotation;
            wireController?.UpdateActiveWirePositionsExternal();
            Debug.Log($"<color=cyan>[History]</color> Redo: Đưa [{targetObject.name}] tới vị trí sau khi di chuyển.");
        }
    }
}

/// <summary>
/// Hành động nối dây giữa 2 điểm cực (Terminal).
/// Hỗ trợ hoàn hảo cấu trúc Y-branch nhiều nhánh.
/// </summary>
public class ConnectWireAction : IUndoableAction
{
    private readonly Transform terminalA;
    private readonly Transform terminalB;
    private readonly WireConnectionController wireController;

    public string Description => $"Nối dây [{terminalA?.name}] -> [{terminalB?.name}]";

    public ConnectWireAction(Transform a, Transform b, WireConnectionController controller)
    {
        terminalA = a;
        terminalB = b;
        wireController = controller;
    }

    public void Undo()
    {
        if (wireController != null && terminalA != null && terminalB != null)
        {
            wireController.RemoveWire(terminalA, terminalB);
            Debug.Log($"<color=cyan>[History]</color> Undo: Hủy kết nối dây [{terminalA.name}] <-> [{terminalB.name}].");
        }
    }

    public void Redo()
    {
        if (wireController != null && terminalA != null && terminalB != null)
        {
            wireController.ConnectWireDirectly(terminalA, terminalB);
            Debug.Log($"<color=cyan>[History]</color> Redo: Khôi phục kết nối dây [{terminalA.name}] <-> [{terminalB.name}].");
        }
    }
}

/// <summary>
/// Hành động ngắt một dây nối giữa 2 điểm cực.
/// </summary>
public class DisconnectWireAction : IUndoableAction
{
    private readonly Transform terminalA;
    private readonly Transform terminalB;
    private readonly WireConnectionController wireController;

    public string Description => $"Ngắt dây [{terminalA?.name}] -> [{terminalB?.name}]";

    public DisconnectWireAction(Transform a, Transform b, WireConnectionController controller)
    {
        terminalA = a;
        terminalB = b;
        wireController = controller;
    }

    public void Undo()
    {
        if (wireController != null && terminalA != null && terminalB != null)
        {
            wireController.ConnectWireDirectly(terminalA, terminalB);
            Debug.Log($"<color=cyan>[History]</color> Undo: Khôi phục dây bị ngắt [{terminalA.name}] <-> [{terminalB.name}].");
        }
    }

    public void Redo()
    {
        if (wireController != null && terminalA != null && terminalB != null)
        {
            wireController.RemoveWire(terminalA, terminalB);
            Debug.Log($"<color=cyan>[History]</color> Redo: Ngắt lại dây [{terminalA.name}] <-> [{terminalB.name}].");
        }
    }
}

/// <summary>
/// Quản lý ngăn xếp Lịch sử Thao tác (Undo / Redo Stack).
/// </summary>
public class CircuitHistoryManager
{
    private readonly Stack<IUndoableAction> undoStack = new Stack<IUndoableAction>();
    private readonly Stack<IUndoableAction> redoStack = new Stack<IUndoableAction>();

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;
    public int UndoCount => undoStack.Count;
    public int RedoCount => redoStack.Count;

    /// <summary>
    /// Ghi nhận một thao tác người dùng mới vào lịch sử. Xóa sạch Redo stack.
    /// </summary>
    public void RecordAction(IUndoableAction action)
    {
        if (action == null) return;
        undoStack.Push(action);
        redoStack.Clear(); // Thao tác mới -> xóa redo stack theo đúng quy tắc
        Debug.Log($"<color=green>[CircuitHistory]</color> Đã ghi nhận thao tác: {action.Description} (Undo stack: {undoStack.Count})");
    }

    /// <summary>
    /// Hoàn tác thao tác gần nhất
    /// </summary>
    public bool Undo()
    {
        if (undoStack.Count == 0)
        {
            Debug.LogWarning("<color=orange>[CircuitHistory]</color> Undo stack trống, không thể hoàn tác.");
            return false;
        }

        IUndoableAction action = undoStack.Pop();
        action.Undo();
        redoStack.Push(action);
        Debug.Log($"<color=yellow>[CircuitHistory]</color> Hoàn tác thành công [{action.Description}]. Còn lại: Undo={undoStack.Count}, Redo={redoStack.Count}");
        return true;
    }

    /// <summary>
    /// Làm lại thao tác vừa hoàn tác
    /// </summary>
    public bool Redo()
    {
        if (redoStack.Count == 0)
        {
            Debug.LogWarning("<color=orange>[CircuitHistory]</color> Redo stack trống, không thể làm lại.");
            return false;
        }

        IUndoableAction action = redoStack.Pop();
        action.Redo();
        undoStack.Push(action);
        Debug.Log($"<color=yellow>[CircuitHistory]</color> Làm lại thành công [{action.Description}]. Còn lại: Undo={undoStack.Count}, Redo={redoStack.Count}");
        return true;
    }

    /// <summary>
    /// Xóa toàn bộ lịch sử khi quét lại mặt phẳng hoặc reset toàn bộ mạch
    /// </summary>
    public void Clear()
    {
        undoStack.Clear();
        redoStack.Clear();
        Debug.Log("<color=cyan>[CircuitHistory]</color> Đã làm sạch toàn bộ lịch sử Undo/Redo.");
    }
}