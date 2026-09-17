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
        // 2. CHẾ ĐỘ DI CHUYỂN VẬT CŨ
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

    private void UpdatePreviewAndPlacement()
    {
        if (currentPrefabToPlace == null || previewAnchor == null) return;

        Vector2 screenPos = GetCursorScreenPosition();
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 15f))
        {
            if (hit.collider.CompareTag("CircuitBoard") || hit.collider.name.Contains("Board"))
            {
                // Cố định vị trí và xoay áp sát mặt bảng mạch
                previewAnchor.transform.position = hit.point;
                previewAnchor.transform.rotation = hit.collider.transform.rotation;

                if (!previewAnchor.activeSelf) previewAnchor.SetActive(true);

                if (canPlace && buttonInteractor.isPinching && cooldownTimer <= 0)
                {
                    PlaceObject(hit.point, hit.collider.transform.rotation, hit.collider.transform);
                    canPlace = false;
                    cooldownTimer = 0.8f;
                }
                return;
            }
        }

        if (previewAnchor.activeSelf) previewAnchor.SetActive(false);
    }

    private void PlaceObject(Vector3 position, Quaternion rotation, Transform boardParent)
    {
        if (!previewAnchor.activeSelf) return;

        // 1. Khôi phục độ đục
        SetPreviewTransparent(previewVisual, 1f);
        previewAnchor.name = "Placed_" + currentPrefabToPlace.name;

        // 2. KHẮC PHỤC TRIỆT ĐỂ LỖI HÌNH BỊ MÉO:
        // Không gắn trực tiếp vào boardParent vì sẽ bị dính Non-uniform scale
        // Gắn vào đối tượng cha ngoài cùng (World Anchor) của bàn
        Transform rootAnchor = boardParent.parent != null ? boardParent.parent : boardParent;
        previewAnchor.transform.SetParent(rootAnchor, true);

        // Ép Scale chuẩn xác tuyệt đối, loại bỏ việc bị bóp dẹt theo mặt phẳng
        previewAnchor.transform.localScale = Vector3.one;
        if (previewVisual != null)
        {
            previewVisual.transform.localScale = Vector3.one * scaleMultiplier;
        }

        // Tạo Collider cho vật thể mới
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

    private void HandleDragAndDrop()
    {
        Vector2 screenPos = GetCursorScreenPosition();

        if (buttonInteractor.JustPinched && !isDragging)
        {
            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 15f))
            {
                Transform rootObj = hit.collider.transform;
                while (rootObj != null)
                {
                    if (rootObj.name.StartsWith("Placed_"))
                    {
                        draggedObject = rootObj.gameObject;
                        isDragging = true;
                        break;
                    }
                    rootObj = rootObj.parent;
                }
            }
        }

        if (isDragging && draggedObject != null)
        {
            if (buttonInteractor.isPinching)
            {
                Ray ray = mainCamera.ScreenPointToRay(screenPos);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit, 15f))
                {
                    if (hit.collider.CompareTag("CircuitBoard") || hit.collider.name.Contains("Board"))
                    {
                        draggedObject.transform.position = hit.point;
                    }
                }
            }
            else
            {
                ReleaseDraggedObject();
            }
        }
    }

    private void ReleaseDraggedObject()
    {
        isDragging = false;
        draggedObject = null;
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