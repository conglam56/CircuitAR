using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

public class TapToPlaceController : MonoBehaviour
{
    [Header("Tham chiếu Hệ thống")]
    public ButtonInteractor buttonInteractor;
    public MenuHUDController menuHUD;
    public ARRaycastManager raycastManager;
    public SinglePlaneLockController planeLock;

    [Header("Prefab linh kiện (Khớp tên với Menu HUD)")]
    public GameObject[] componentPrefabs;
    public string[] componentNames;

    [Header("Cấu hình Preview & Di chuyển")]
    [Range(0.1f, 1f)] public float previewAlpha = 0.5f;
    public float scaleMultiplier = 0.035f;
    public LayerMask placedObjectLayer;

    private GameObject previewAnchor;
    private GameObject previewVisual;
    private GameObject currentPrefabToPlace;
    private static List<ARRaycastHit> hits = new List<ARRaycastHit>();

    private bool canPlace = false;
    private float cooldownTimer = 0f;

    // --- BIẾN QUẢN LÝ DI CHUYỂN VẬT THỂ (DRAG & DROP) ---
    private GameObject draggedObject = null;
    private bool isDragging = false;
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        if (buttonInteractor == null || menuHUD == null || raycastManager == null) return;

        if (cooldownTimer > 0) cooldownTimer -= Time.deltaTime;

        // 1. CHẾ ĐỘ ĐẶT VẬT MỚI (Khi Menu có lựa chọn)
        if (menuHUD.HasSelection)
        {
            if (isDragging) ReleaseDraggedObject();

            if (currentPrefabToPlace == null)
            {
                StartPreview(menuHUD.SelectedComponent);
                canPlace = false;
                cooldownTimer = 1.0f;
            }

            if (!buttonInteractor.isPinching && cooldownTimer <= 0)
            {
                canPlace = true;
            }

            UpdatePreviewAndPlacement();
        }
        // 2. CHẾ ĐỘ DI CHUYỂN VẬT CŨ (Khi Menu trống)
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

    // --- LOGIC ĐẶT VẬT MỚI ---
    private void UpdatePreviewAndPlacement()
    {
        if (currentPrefabToPlace == null || previewAnchor == null) return;

        Vector2 screenPos = GetCursorScreenPosition();

        if (raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinBounds))
        {
            Pose hitPose = default;
            bool foundValidHit = false;

            foreach (var hit in hits)
            {
                if (planeLock == null || !planeLock.HasLockedPlane || hit.trackableId == planeLock.LockedPlaneId)
                {
                    hitPose = hit.pose;
                    foundValidHit = true;
                    break;
                }
            }

            if (foundValidHit)
            {
                Vector3 normal = hitPose.up;
                Vector3 forward = Vector3.ProjectOnPlane(mainCamera.transform.forward, normal).normalized;
                Quaternion properRotation = Quaternion.LookRotation(forward, normal);

                previewAnchor.transform.position = hitPose.position;
                previewAnchor.transform.rotation = properRotation;

                if (!previewAnchor.activeSelf) previewAnchor.SetActive(true);

                if (canPlace && buttonInteractor.isPinching && cooldownTimer <= 0)
                {
                    PlaceObject();
                    canPlace = false;
                    cooldownTimer = 1.0f;
                }
            }
            else
            {
                if (previewAnchor.activeSelf) previewAnchor.SetActive(false);
            }
        }
        else
        {
            if (previewAnchor.activeSelf) previewAnchor.SetActive(false);
        }
    }

    // --- LOGIC MỚI: KÉO THẢ VẬT THỂ ---
    private void HandleDragAndDrop()
    {
        Vector2 screenPos = GetCursorScreenPosition();

        // 2.1 Bắt đầu nắm vật thể
        if (buttonInteractor.JustPinched && !isDragging)
        {
            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                // Truy ngược lên để tìm gốc object có chứa BoxCollider mà bạn đã đặt tay
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

        // 2.2 Đang giữ và kéo vật thể
        if (isDragging && draggedObject != null)
        {
            if (buttonInteractor.isPinching)
            {
                if (raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinBounds))
                {
                    foreach (var arHit in hits)
                    {
                        if (planeLock == null || !planeLock.HasLockedPlane || arHit.trackableId == planeLock.LockedPlaneId)
                        {
                            draggedObject.transform.position = arHit.pose.position;
                            break;
                        }
                    }
                }
            }
            // 2.3 Thả tay
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

    // --- CÁC HÀM TIỆN ÍCH KHÁC ---
    private Vector2 GetCursorScreenPosition()
    {
        if (buttonInteractor.debugPoint == null) return new Vector2(Screen.width / 2, Screen.height / 2);
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

    private void PlaceObject()
    {
        if (!previewAnchor.activeSelf) return;

        SetPreviewTransparent(previewVisual, 1f);
        previewAnchor.name = "Placed_" + currentPrefabToPlace.name;

        // ĐÃ XÓA KHỐI LỆNH TỰ ĐỘNG TẠO BOX COLLIDER ĐỂ KHÔNG BỊ TRÙNG LẶP/RÁC

        if (hits.Count > 0)
        {
            ARPlane hitPlane = raycastManager.GetComponent<ARPlaneManager>().GetPlane(hits[0].trackableId);
            if (hitPlane != null)
            {
                RectanglePlaneVisualizer visualizer = hitPlane.GetComponent<RectanglePlaneVisualizer>();
                if (visualizer != null)
                {
                    visualizer.ExpandToIncludePoint(previewAnchor.transform.position);
                }
            }
        }

        previewAnchor = null;
        previewVisual = null;
        currentPrefabToPlace = null;
        menuHUD.ClearSelection();
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
                if (mat.HasProperty("_Color")) { Color c = mat.color; c.a = alpha; mat.color = c; }
                else if (mat.HasProperty("_BaseColor")) { Color c = mat.GetColor("_BaseColor"); c.a = alpha; mat.SetColor("_BaseColor", c); }

                if (alpha < 1f)
                {
                    mat.SetFloat("_Surface", 1); mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000;
                }
                else
                {
                    mat.SetFloat("_Surface", 0); mat.SetOverrideTag("RenderType", "Opaque"); mat.renderQueue = 2000;
                }
            }
        }
    }
}