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
    // Khôi phục lại biến planeLock nếu bạn có dùng nó
    public SinglePlaneLockController planeLock;

    [Header("Prefab linh kiện (Khớp tên với Menu HUD)")]
    public GameObject[] componentPrefabs;
    public string[] componentNames;

    [Header("Cấu hình Preview")]
    [Range(0.1f, 1f)] public float previewAlpha = 0.5f;
    public float scaleMultiplier = 0.035f;

    private GameObject previewAnchor;
    private GameObject previewVisual;
    private GameObject currentPrefabToPlace;
    private static List<ARRaycastHit> hits = new List<ARRaycastHit>();

    private bool canPlace = false;
    private float cooldownTimer = 0f;

    void Update()
    {
        if (buttonInteractor == null || menuHUD == null || raycastManager == null) return;

        if (cooldownTimer > 0) cooldownTimer -= Time.deltaTime;

        if (menuHUD.HasSelection && currentPrefabToPlace == null)
        {
            StartPreview(menuHUD.SelectedComponent);
            canPlace = false;
            cooldownTimer = 1.0f;
        }

        if (currentPrefabToPlace == null || previewAnchor == null) return;

        if (!buttonInteractor.isPinching && cooldownTimer <= 0)
        {
            canPlace = true;
        }

        Vector2 screenPos = GetCursorScreenPosition();

        // CHÌA KHÓA: Trở lại dùng PlaneWithinPolygon để an toàn, không dùng Infinity nữa.
        
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
                Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, normal).normalized;
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

        // KHÔI PHỤC: Hàm đo đáy tự động của bạn
        AlignVisualBaseToAnchor(previewAnchor, previewVisual);

        SetPreviewTransparent(previewVisual, previewAlpha);
        previewAnchor.SetActive(false);
    }

    // KHÔI PHỤC: Logic tính hộp bao quanh (Bounding Box) để nâng vật lên
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
        if (hits.Count > 0)
        {
            // Tìm đối tượng Trackable (chính là ARPlane) mà tia raycast vừa chạm vào
            var hitTrackable = raycastManager.raycastPrefab; // (Chỉ mang tính chất dò, dùng cách dưới)

            // Lấy ARPlane từ hit đầu tiên
            ARPlane hitPlane = raycastManager.GetComponent<ARPlaneManager>().GetPlane(hits[0].trackableId);

            if (hitPlane != null)
            {
                // Tìm script Visualizer nằm trên mặt phẳng đó và ra lệnh mở rộng
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