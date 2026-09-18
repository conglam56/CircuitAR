using UnityEngine;
using System.Collections.Generic;

public class TapToPlaceController : MonoBehaviour
{
    [Header("Tham chiếu Hệ thống")]
    public ButtonInteractor buttonInteractor;
    public MenuHUDController menuHUD;
    public SinglePlaneLockController planeLock;

    [Header("Prefab linh kiện (Khớp tên với Menu HUD)")]
    public GameObject[] componentPrefabs;
    public string[] componentNames;

    [Header("Cấu hình Preview & Di chuyển")]
    [Range(0.1f, 1f)] public float previewAlpha = 0.6f;
    public float scaleMultiplier = 0.035f;

    private GameObject previewAnchor;
    private GameObject previewVisual;
    private GameObject currentPrefabToPlace;

    private bool canPlace = false;
    private float cooldownTimer = 0f;

    // --- DI CHUYỂN VẬT THỂ (DRAG & DROP) ---
    private GameObject draggedObject = null;
    private bool isDragging = false;
    private float unpinchGraceTimer = 0f;
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        if (planeLock == null) planeLock = FindObjectOfType<SinglePlaneLockController>();
        if (planeLock == null || !planeLock.HasLockedPlane)
        {
            if (previewAnchor != null) Destroy(previewAnchor);
            return;
        }

        if (buttonInteractor == null || menuHUD == null) return;

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        if (cooldownTimer > 0) cooldownTimer -= Time.deltaTime;

        // 1. CHẾ ĐỘ ĐẶT VẬT MỚI
        if (menuHUD.HasSelection)
        {
            if (isDragging) ReleaseDraggedObject();

            if (currentPrefabToPlace == null)
            {
                StartPreview(menuHUD.SelectedComponent);
                canPlace = false;
                cooldownTimer = 0.4f;
            }

            if (!buttonInteractor.isPinching && cooldownTimer <= 0)
            {
                canPlace = true;
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
    /// Cập nhật vị trí bóng xem trước (Preview) và đặt vật thể
    /// </summary>
    private void UpdatePreviewAndPlacement()
    {
        if (currentPrefabToPlace == null || previewAnchor == null) return;

        Vector2 screenPos = GetCursorScreenPosition();
        Ray ray = mainCamera.ScreenPointToRay(screenPos);

        // Dùng RaycastAll để tia nhìn xuyên qua preview và không bao giờ tự chặn chính nó (khắc phục lỗi nhấp nháy liên tục)
        RaycastHit[] hits = Physics.RaycastAll(ray, 20f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        bool foundBoard = false;
        RaycastHit boardHit = default;

        foreach (var h in hits)
        {
            // Bỏ qua tất cả collider thuộc previewAnchor
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
            // Cố định vị trí và xoay áp sát mặt bảng mạch
            previewAnchor.transform.position = boardHit.point;
            previewAnchor.transform.rotation = boardHit.collider.transform.rotation;

            if (!previewAnchor.activeSelf) previewAnchor.SetActive(true);

            if (canPlace && buttonInteractor.isPinching && cooldownTimer <= 0)
            {
                PlaceObject(boardHit.point, boardHit.collider.transform.rotation, boardHit.collider.transform);
                canPlace = false;
                cooldownTimer = 0.8f;
            }
            return;
        }

        if (previewAnchor.activeSelf) previewAnchor.SetActive(false);
    }

    /// <summary>
    /// Đặt linh kiện thật xuống mặt bảng mạch
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

        // 1. Khôi phục độ đục
        SetPreviewTransparent(previewVisual, 1f);
        previewAnchor.name = "Placed_" + currentPrefabToPlace.name;

        // 2. KHẮC PHỤC TRIỆT ĐỂ LỖI HÌNH BỊ MÉO:
        Transform rootAnchor = boardParent.parent != null ? boardParent.parent : boardParent;
        previewAnchor.transform.SetParent(rootAnchor, true);

        // Ép Scale chuẩn xác tuyệt đối, loại bỏ việc bị bóp dẹt theo mặt phẳng
        previewAnchor.transform.localScale = Vector3.one;
        if (previewVisual != null)
        {
            previewVisual.transform.localScale = Vector3.one * scaleMultiplier;
        }

        // Tạo Collider bao quanh nếu chưa có
        Collider col = previewAnchor.GetComponentInChildren<Collider>();
        if (col == null)
        {
            BoxCollider box = previewAnchor.AddComponent<BoxCollider>();
            Renderer ren = previewAnchor.GetComponentInChildren<Renderer>();
            if (ren != null)
            {
                box.center = previewAnchor.transform.InverseTransformPoint(ren.bounds.center);
                box.size = ren.bounds.size / scaleMultiplier;
            }
        }

        previewAnchor = null;
        previewVisual = null;
        currentPrefabToPlace = null;
        menuHUD.ClearSelection();
    }

    /// <summary>
    /// Xử lý kéo thả linh kiện (Drag & Drop) mượt mà theo thời gian thực
    /// </summary>
    private void HandleDragAndDrop()
    {
        Vector2 screenPos = GetCursorScreenPosition();
        Ray ray = mainCamera.ScreenPointToRay(screenPos);

        // 1. BẮT ĐẦU KÉO: Khi người dùng bấm chụm ngón tay vào vật thể
        if (buttonInteractor.JustPinched && !isDragging)
        {
            RaycastHit[] allHits = Physics.RaycastAll(ray, 20f);
            System.Array.Sort(allHits, (a, b) => a.distance.CompareTo(b.distance));

            // Bỏ qua nếu bấm trúng cực (Terminal) để nhường cho WireConnectionController xử lý nối dây
            for (int i = 0; i < allHits.Length; i++)
            {
                if (allHits[i].collider.CompareTag("Terminal")) return;
            }

            for (int i = 0; i < allHits.Length; i++)
            {
                Transform rootObj = allHits[i].collider.transform;
                while (rootObj != null)
                {
                    if (rootObj.name.StartsWith("Placed_"))
                    {
                        draggedObject = rootObj.gameObject;
                        isDragging = true;
                        unpinchGraceTimer = 0f;
                        break;
                    }
                    rootObj = rootObj.parent;
                }
                if (isDragging) break;
            }
        }

        // 2. ĐANG KÉO: Cập nhật vị trí vật thể trượt mượt mà liên tục theo ngón tay (không bị đơ, không bị giật nhảy đến điểm cuối)
        if (isDragging && draggedObject != null)
        {
            if (buttonInteractor.isPinching)
            {
                unpinchGraceTimer = 0f;

                // Dùng RaycastAll để tia raycast xuyên qua chính vật thể đang cầm và chạm vào mặt bàn bên dưới
                RaycastHit[] hits = Physics.RaycastAll(ray, 20f);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                bool foundBoard = false;
                RaycastHit boardHit = default;

                foreach (var h in hits)
                {
                    // Bỏ qua chính vật thể đang kéo và tất cả đối tượng con của nó
                    if (h.transform.IsChildOf(draggedObject.transform)) continue;

                    // Bỏ qua các cọc nối dây
                    if (h.collider.CompareTag("Terminal")) continue;

                    if (h.collider.CompareTag("CircuitBoard") || h.collider.name.Contains("Board"))
                    {
                        boardHit = h;
                        foundBoard = true;
                        break;
                    }
                }

                if (foundBoard)
                {
                    // Di chuyển mượt mà bám sát điểm chạm trên mặt bàn
                    draggedObject.transform.position = boardHit.point;
                    draggedObject.transform.rotation = boardHit.collider.transform.rotation;
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
        isDragging = false;
        draggedObject = null;
        unpinchGraceTimer = 0f;
    }

    private Vector2 GetCursorScreenPosition()
    {
        if (buttonInteractor.debugPoint == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Canvas canvas = buttonInteractor.debugPoint.GetComponentInParent<Canvas>();
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
        return RectTransformUtility.WorldToScreenPoint(uiCam, buttonInteractor.debugPoint.position);
    }

    private void StartPreview(string componentName)
    {
        GameObject prefab = GetPrefabByName(componentName);
        if (prefab == null) return;

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
        previewAnchor.SetActive(false);
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
        for (int i = 0; i < componentNames.Length; i++)
        {
            if (componentNames[i] == name) return componentPrefabs[i];
        }
        return null;
    }

    private void SetPreviewTransparent(GameObject obj, float alpha)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
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
}