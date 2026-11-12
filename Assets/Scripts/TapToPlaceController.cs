using UnityEngine;
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;

public class TapToPlaceController : MonoBehaviour
{
    [Header("Tham chiếu Hệ thống")]
    public ButtonInteractor buttonInteractor;
    public MenuHUDController menuHUD;
    public SinglePlaneLockController planeLock;
    public WireConnectionController wireConnectionController;
    public ARRaycastManager arRaycastManager;
    public ARPlaneManager arPlaneManager;
    public ARAnchorManager arAnchorManager;

    [Header("Đặt nhanh")]
    [Tooltip("Cho phép đặt ngay ở khoảng cách ước lượng khi chưa có mặt phẳng. Neo AR được tạo trong nền.")]
    public bool enableInstantPlacement = true;
    [Tooltip("Chạm vào vùng camera để đặt hoặc chọn linh kiện.")]
    public bool enableTapToPlace = true;

    [Header("Prefab linh kiện (Khớp tên với Menu HUD)")]
    public GameObject[] componentPrefabs;
    public string[] componentNames;

    [Header("Cấu hình Preview & Di chuyển")]
    [Range(0.1f, 1f)] public float previewAlpha = 0.6f;
    public float scaleMultiplier = 0.035f;
    [Tooltip("Chuẩn hóa cạnh dài nhất của thân linh kiện theo mét, tránh khác đơn vị import.")]
    public float componentBodySize = 0.12f;
    [Tooltip("Độ lệch đáy hình học khỏi mặt thật (mét). Để 0 để đáy chạm đúng mặt đo được.")]
    [Range(-0.03f, 0.02f)] public float surfaceContactOffset = 0f;
    [Tooltip("Thời gian chờ trước khi báo vị trí kéo không hợp lệ khi AR raycast hụt tạm thời.")]
    [Range(0.05f, 0.75f)] public float surfaceLossGracePeriod = 0.3f;
    [Tooltip("Tốc độ làm mượt con trỏ tay trước khi raycast lên mặt thật.")]
    [Range(5f, 120f)] public float cursorSmoothingSpeed = 90f;
    [Tooltip("Tốc độ preview bám theo vị trí raycast; trục Y vẫn luôn áp sát mặt thật.")]
    [Range(5f, 120f)] public float previewFollowSpeed = 80f;
    [Tooltip("Khoảng cách dự phòng khi prefab không có bounds. Preview có bounds tự căn theo khung hình, không đổi tỉ lệ linh kiện.")]
    [Range(0.3f, 1.5f)] public float ungroundedPreviewDistance = 0.9f;
    [Tooltip("Tỉ lệ khung hình dành cho preview tạm trước khi tìm được sàn/bàn thật.")]
    [Range(0.15f, 0.75f)] public float ungroundedPreviewScreenFraction = 0.65f;
    [Tooltip("Thời gian preview chuyển sang điểm trên mặt thật; không đổi scale linh kiện.")]
    [Range(0.1f, 0.5f)] public float previewSurfaceTransitionDuration = 0.25f;
    [Tooltip("Chỉ hút vào mép polygon đã đo trong khoảng rất nhỏ, không kéo dài mặt bàn ra vô hạn (mét).")]
    [Range(0f, 0.04f)] public float surfaceEdgeSnapDistance = 0.025f;

    [Header("--- LỊCH SỬ LINH KIỆN (HỖ TRỢ UNDO / THU HỒI) ---")]
    public List<GameObject> placedObjectsHistory = new List<GameObject>();

    // Hệ thống quản lý lịch sử thao tác chuyên sâu (Command History)
    public CircuitHistoryManager History { get; } = new CircuitHistoryManager();

    private GameObject previewAnchor;
    private GameObject previewVisual;
    private GameObject currentPrefabToPlace;
    public GameObject CurrentPrefabToPlace => currentPrefabToPlace;

    private bool canPlace = false;
    private float cooldownTimer = 0f;

    // --- DI CHUYỂN VẬT THỂ (DRAG & DROP) ---
    private GameObject draggedObject = null;
    public GameObject DraggedObject => isDragging ? draggedObject : secondDrag.draggedObject;
    private bool isDragging = false;
    public bool IsDragging => isDragging || secondDrag.isDragging ||
        (processingSecondHand && primaryDrag.isDragging);
    private ARFloatingBubbleMenu floatingMenu = null;
    private float unpinchGraceTimer = 0f;
    private Vector3 lastValidDragPos = Vector3.zero;
    private Vector2 dragGrabScreenPosition;
    private int dragEye = -1;
    private bool dragPointerMoved;
    private Quaternion lastValidDragRot = Quaternion.identity;
    private Vector3 dragStartPos = Vector3.zero;
    private Vector2 dragPointerOffset;
    private float dragHandLostAt = float.NegativeInfinity;
    private bool dragNeedsOpenHandAfterPlacement;
    private Plane dragFreePlane;
    private ARSupportPolicy.LevelTransition dragLevelTransition = new ARSupportPolicy.LevelTransition();
    private Quaternion dragStartRot = Quaternion.identity;
    private Vector3 currentSmoothedDragPos = Vector3.zero;
    private bool isDragBlocked = false;
    private Camera mainCamera;
    private static readonly List<ARRaycastHit> surfaceHits = new List<ARRaycastHit>();
    private TrackableId lastPreviewTrackableId = TrackableId.invalidId;
    private Vector2 smoothedPlacementScreenPosition;
    private bool hasSmoothedPlacementScreenPosition = false;
    private bool hasPreviewSurfacePose = false;
    private float previewBoundsRadius;
    private Vector3 previewBoundsLocalCenter;
    private bool hasUngroundedPreviewFrame;
    private Matrix4x4 ungroundedPreviewProjection;
    private ARSessionState ungroundedPreviewSessionState;
    private Pose lastKnownPlacementPose = Pose.identity;
    private TrackableId lastKnownSurfaceTrackableId = TrackableId.invalidId;
    private bool hasLastKnownPlacementPose = false;
    private bool placementPending;
    private int placementGeneration;
    private bool worldPinchArmed;
    private float placementRequestExpiresAt = float.NegativeInfinity;
    private Vector2 placementRequestScreenPosition;
    private bool placementRequestAwaitingSurface;
    private bool placementRequestHasSurface;
    private bool placementRequestIsDepth;
    private bool placementRequestFromTap;
    private bool placementRequestHasEstimate;
    private Pose placementRequestEstimate;
    private bool placementTapThisFrame;
    private readonly ARPlacementTapInput placementTapInput = new ARPlacementTapInput();
    private readonly HashSet<GameObject> instantPlacedObjects = new HashSet<GameObject>();
    private readonly ARPlacementGeometryCache geometryCache = new ARPlacementGeometryCache();
    private readonly ARPlacementRenderPolicy renderPolicy = new ARPlacementRenderPolicy();
    private float nextReferenceLookupAt;
    private Pose placementRequestDepthPose;
    private TrackableId placementRequestPlaneId = TrackableId.invalidId;
    private Vector3 placementRequestLocalPoint;
    private bool placementRetryEligible;
    private float nextPlacementAttemptAt;
    private bool surfaceTransitionActive;
    private float surfaceTransitionStartedAt;
    private Pose surfaceTransitionStartPose;
    private TrackableId surfaceTransitionTrackableId = TrackableId.invalidId;
    private float nextPlacementDiagnosticAt;
    private ButtonInteractor.PlacementFeedback lastPlacementFeedback;
    private MeasuredDepthSurfaceProvider measuredDepth = new MeasuredDepthSurfaceProvider();
    private readonly MeasuredDepthSurfaceProvider contactDepth = new MeasuredDepthSurfaceProvider { RequireVisibleHand = false };
    private sealed class PlacedContact
    {
        public TrackableId planeId;
        public Transform parent;
        public Vector3 expectedLocal;
        public bool settling;
        public float targetHeight;
        public readonly ARSupportPolicy.ContactConfirmation confirmation = new ARSupportPolicy.ContactConfirmation();
    }
    private readonly Dictionary<GameObject, PlacedContact> placedContacts = new Dictionary<GameObject, PlacedContact>();
    private float nextContactProbeAt;
    private float nextContactDiagnosticAt;
    private int contactProbeIndex;
    private bool dragHasMeasuredSupport;
    private bool dragSurfaceIsEstimated;
    private bool previewSurfaceIsDepth;
    private bool hasDepthSupportEvidence;
    private Pose lastDepthSupportEvidence;
    private Vector2 lastDepthEvidencePointer;
    private bool loggedMeasuredPreviewGeometry;
    private GameObject placementStatusRoot;
    private TMPro.TextMeshProUGUI placementStatusText;
    private ARScanFloatingHUD surfaceGuidanceHUD;
    private bool surfaceGuidanceEnabled;
    private float nextSurfaceGuidanceAt;
    private float guidanceSurfaceReadySince = float.NegativeInfinity;
    private float placementConfirmedUntil;
    private float nextAnchorDiagnosticAt;
    private struct AnchorDiagnostic
    {
        public Vector3 world, local, anchor, camera;
    }
    private readonly Dictionary<GameObject, AnchorDiagnostic> anchorDiagnostics = new Dictionary<GameObject, AnchorDiagnostic>();
    private static readonly Vector2[] GuidanceProbeViewports =
    {
        new Vector2(0.5f, 0.45f), new Vector2(0.35f, 0.45f),
        new Vector2(0.65f, 0.45f), new Vector2(0.5f, 0.3f)
    };
    private string selectedDisplayName;
    private MaterialPropertyBlock previewPropertyBlock;
    private bool? previewIsInvalid;
    private TrackableId dragSurfaceTrackableId = TrackableId.invalidId;
    private readonly Dictionary<GameObject, ARAnchor> objectAnchors = new Dictionary<GameObject, ARAnchor>();
    private readonly Dictionary<GameObject, int> dragAnchorRequests = new Dictionary<GameObject, int>();
    private int nextDragAnchorRequest;

    [Header("--- QUẢN LÝ CHỌN LINH KIỆN ĐỂ XÓA & XOAY ---")]
    public GameObject SelectedComponent { get; private set; }
    public event System.Action<GameObject> OnSelectedComponentChanged;
    public event System.Action OnPlacementFinished;
    private Dictionary<Renderer, Material[]> originalSelectedMaterials = new Dictionary<Renderer, Material[]>();
    private Material selectionHighlightMat = null;
    private Material selectionInvalidMat = null;

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
            UpdateSelectedComponentVisual();
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

    /// <summary>
    /// Xoay linh kiện đang được chọn 90 độ, kiểm tra va chạm bằng SAT và cập nhật visual feedback
    /// </summary>
    public void RotateSelectedComponent()
    {
        if (SelectedComponent == null) return;
        SelectedComponent.GetComponent<ARInstantPlacementAnchor>()?.StopRefinement();

        Quaternion oldRot = SelectedComponent.transform.rotation;
        SelectedComponent.transform.Rotate(Vector3.up, 90f, Space.Self);
        Quaternion newRot = SelectedComponent.transform.rotation;

        // Cập nhật tọa độ dây nối theo thời gian thực
        if (wireConnectionController != null)
        {
            wireConnectionController.UpdateActiveWirePositionsExternal();
        }

        // Kiểm tra hợp lệ vị trí sau khi xoay (CheckOverlap) và đổi màu tương ứng
        UpdateSelectedComponentVisual();

        // Ghi nhận vào lịch sử Undo/Redo
        History.RecordAction(new RotateComponentAction(SelectedComponent, oldRot, newRot, wireConnectionController, this));
    }

    /// <summary>
    /// Đồng bộ visual cho linh kiện khi Undo/Redo thao tác xoay
    /// </summary>
    public void NotifyComponentRotated(GameObject obj)
    {
        if (obj == null) return;
        obj.GetComponent<ARInstantPlacementAnchor>()?.StopRefinement();
        if (SelectedComponent == obj)
        {
            UpdateSelectedComponentVisual();
        }
    }

    private void EnsureOriginalMaterialsSaved(GameObject obj)
    {
        if (obj == null) return;

        Renderer[] rens = obj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rens)
        {
            if (r is LineRenderer || originalSelectedMaterials.ContainsKey(r)) continue;
            originalSelectedMaterials[r] = r.sharedMaterials;
        }
    }

    public void UpdateSelectedComponentVisual()
    {
        if (SelectedComponent == null) return;
        bool isOverlapping = CheckOverlap(SelectedComponent.transform.position, SelectedComponent, out _);
        if (SelectedComponent == draggedObject) isDragBlocked = isOverlapping;
        if (isOverlapping)
        {
            ApplySelectionInvalid(SelectedComponent);
        }
        else
        {
            ApplySelectionHighlight(SelectedComponent);
        }
    }

    private void ApplySelectionHighlight(GameObject obj)
    {
        if (obj == null) return;
        EnsureOriginalMaterialsSaved(obj);

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

        Renderer[] rens = obj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rens)
        {
            if (r is LineRenderer) continue;
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = selectionHighlightMat;
            r.materials = mats;
        }
    }

    private void ApplySelectionInvalid(GameObject obj)
    {
        if (obj == null) return;
        EnsureOriginalMaterialsSaved(obj);

        if (selectionInvalidMat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            selectionInvalidMat = new Material(shader);
            Color invalidColor = new Color(1f, 0.22f, 0.22f, 0.9f);
            if (selectionInvalidMat.HasProperty("_BaseColor")) selectionInvalidMat.SetColor("_BaseColor", invalidColor);
            else if (selectionInvalidMat.HasProperty("_Color")) selectionInvalidMat.color = invalidColor;
            if (selectionInvalidMat.HasProperty("_EmissionColor"))
            {
                selectionInvalidMat.EnableKeyword("_EMISSION");
                selectionInvalidMat.SetColor("_EmissionColor", new Color(0.9f, 0.1f, 0.1f) * 0.8f);
            }
        }

        Renderer[] rens = obj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rens)
        {
            if (r is LineRenderer) continue;
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = selectionInvalidMat;
            r.materials = mats;
        }
    }

    private void ClearSelectionHighlight()
    {
        var restored = new List<Renderer>();
        foreach (var pair in originalSelectedMaterials)
        {
            if (pair.Key != null && IsObjectBeingDragged(FindPlacedRoot(pair.Key.transform))) continue;
            if (pair.Key != null && pair.Value != null) pair.Key.sharedMaterials = pair.Value;
            restored.Add(pair.Key);
        }
        foreach (var renderer in restored) originalSelectedMaterials.Remove(renderer);
    }

    private void RestoreReleasedHighlight(GameObject obj)
    {
        if (obj == null || obj == SelectedComponent) return;
        foreach (var renderer in obj.GetComponentsInChildren<Renderer>(true))
            if (originalSelectedMaterials.TryGetValue(renderer, out var materials))
            {
                renderer.sharedMaterials = materials;
                originalSelectedMaterials.Remove(renderer);
            }
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

    // Called after Reset destroys all active and undone content/support roots.
    public void ClearPlacedObjectState()
    {
        ClearPreview();
        foreach (GameObject placed in placedObjectsHistory)
        {
            if (placed == null) continue;
            var pendingSupport = placed.GetComponent<ARInstantPlacementAnchor>();
            if (pendingSupport != null) pendingSupport.enabled = false;
        }
        secondDrag = new HandDragState();
        placedObjectsHistory.Clear();
        instantPlacedObjects.Clear();
        objectAnchors.Clear();
        placedContacts.Clear();
        dragAnchorRequests.Clear();
        anchorDiagnostics.Clear();
        geometryCache.Clear();
        isDragging = false;
        draggedObject = null;
    }

    // =========================================================================
    // ĐĂNG KÝ SỰ KIỆN CHUYỂN CHẾ ĐỘ MÔ PHỎNG (CIRCUIT SIMULATION MANAGER)
    // =========================================================================

    void OnEnable()
    {
        if (Application.platform == RuntimePlatform.Android && !Application.isEditor)
            renderPolicy.Apply(FindFirstObjectByType<ARSession>());
        if (CircuitSimulationManager.Instance != null)
        {
            CircuitSimulationManager.Instance.OnSimulationModeChanged += HandleSimulationModeChanged;
        }
    }

    void OnDisable()
    {
        if (isDragging) ReleaseDraggedObject();
        ProcessSecondHand(HandOperation.Release);
        renderPolicy.Restore();
        placementTapInput.Reset();
        CancelContactCorrections();
        EndSurfaceGuidance();
        placementGeneration++;
        placementPending = false;
        ClearPlacementRequest();
        placementRetryEligible = false;
        nextPlacementAttemptAt = 0f;
        surfaceTransitionActive = false;
        SetPlacementFeedback(ButtonInteractor.PlacementFeedback.None);
        worldPinchArmed = false;
        if (placementStatusRoot != null) placementStatusRoot.SetActive(false);
        if (CircuitSimulationManager.Instance != null)
        {
            CircuitSimulationManager.Instance.OnSimulationModeChanged -= HandleSimulationModeChanged;
        }
    }

    private void HandleSimulationModeChanged(bool isSimulating)
    {
        if (isSimulating)
        {
            // 1. Nếu đang có linh kiện được chọn -> Bỏ chọn ngay và tắt viền highlight
            ClearSelectedComponent();

            // 2. Nếu đang xem trước (Preview) chuẩn bị đặt vật mới -> Hủy luôn preview
            CancelPlacement();

            // 3. Nếu đang dở thao tác kéo (Drag) linh kiện -> Nhả tay lập tức
            if (isDragging)
            {
                ReleaseDraggedObject();
            }
        }
    }

    void Start()
    {
        mainCamera = Camera.main;
#if UNITY_2023_1_OR_NEWER
        if (buttonInteractor == null) buttonInteractor = FindFirstObjectByType<ButtonInteractor>();
        if (menuHUD == null) menuHUD = FindFirstObjectByType<MenuHUDController>();
        if (planeLock == null) planeLock = FindFirstObjectByType<SinglePlaneLockController>();
        if (wireConnectionController == null) wireConnectionController = FindFirstObjectByType<WireConnectionController>();
        if (arRaycastManager == null) arRaycastManager = FindFirstObjectByType<ARRaycastManager>();
        if (arPlaneManager == null) arPlaneManager = FindFirstObjectByType<ARPlaneManager>();
        if (arAnchorManager == null) arAnchorManager = FindFirstObjectByType<ARAnchorManager>();
#else
        if (buttonInteractor == null) buttonInteractor = FindObjectOfType<ButtonInteractor>();
        if (menuHUD == null) menuHUD = FindObjectOfType<MenuHUDController>();
        if (planeLock == null) planeLock = FindObjectOfType<SinglePlaneLockController>();
        if (wireConnectionController == null) wireConnectionController = FindObjectOfType<WireConnectionController>();
        if (arRaycastManager == null) arRaycastManager = FindObjectOfType<ARRaycastManager>();
        if (arPlaneManager == null) arPlaneManager = FindObjectOfType<ARPlaneManager>();
        if (arAnchorManager == null) arAnchorManager = FindObjectOfType<ARAnchorManager>();
#endif
        if (CircuitSimulationManager.Instance != null)
        {
            CircuitSimulationManager.Instance.OnSimulationModeChanged -= HandleSimulationModeChanged;
            CircuitSimulationManager.Instance.OnSimulationModeChanged += HandleSimulationModeChanged;
        }
    }

    // Reuse the AR placement and drag logic with independent preview, gesture and support state per hand.
    private sealed class HandDragState
    {
        public bool previewSurfaceIsDepth;
        public GameObject previewAnchor;
        public GameObject previewVisual;
        public GameObject currentPrefabToPlace;
        public bool canPlace = false;
        public float cooldownTimer = 0f;
        public TrackableId lastPreviewTrackableId = TrackableId.invalidId;
        public Vector2 smoothedPlacementScreenPosition;
        public bool hasSmoothedPlacementScreenPosition = false;
        public bool hasPreviewSurfacePose = false;
        public float previewBoundsRadius;
        public Vector3 previewBoundsLocalCenter;
        public bool hasUngroundedPreviewFrame;
        public Matrix4x4 ungroundedPreviewProjection;
        public ARSessionState ungroundedPreviewSessionState;
        public bool placementPending;
        public int placementGeneration;
        public float placementRequestExpiresAt = float.NegativeInfinity;
        public Vector2 placementRequestScreenPosition;
        public bool placementRequestAwaitingSurface;
        public bool placementRequestHasSurface;
        public bool placementRequestIsDepth;
        public bool placementRequestFromTap;
        public bool placementRequestHasEstimate;
        public Pose placementRequestEstimate;
        public bool placementTapThisFrame;
        public Pose placementRequestDepthPose;
        public TrackableId placementRequestPlaneId = TrackableId.invalidId;
        public Vector3 placementRequestLocalPoint;
        public bool placementRetryEligible;
        public float nextPlacementAttemptAt;
        public bool surfaceTransitionActive;
        public float surfaceTransitionStartedAt;
        public Pose surfaceTransitionStartPose;
        public TrackableId surfaceTransitionTrackableId = TrackableId.invalidId;
        public bool loggedMeasuredPreviewGeometry;
        public string selectedDisplayName;
        public bool? previewIsInvalid;
        public ButtonInteractor previewInput;
        public bool previewNeedsRelease;
        public bool requireMeasuredForPreview;
        public GameObject draggedObject = null;
        public bool isDragging = false;
        public float unpinchGraceTimer = 0f;
        public Vector3 lastValidDragPos = Vector3.zero;
        public Vector2 dragGrabScreenPosition = Vector2.zero;
        public int dragEye = -1;
        public bool dragPointerMoved = false;
        public Quaternion lastValidDragRot = Quaternion.identity;
        public Vector3 dragStartPos = Vector3.zero;
        public Vector2 dragPointerOffset = Vector2.zero;
        public float dragHandLostAt = float.NegativeInfinity;
        public bool dragNeedsOpenHandAfterPlacement = false;
        public Plane dragFreePlane;
        public ARSupportPolicy.LevelTransition dragLevelTransition = new ARSupportPolicy.LevelTransition();
        public Quaternion dragStartRot = Quaternion.identity;
        public Vector3 currentSmoothedDragPos = Vector3.zero;
        public bool isDragBlocked = false;
        public bool dragHasMeasuredSupport = false;
        public bool dragSurfaceIsEstimated = false;
        public TrackableId dragSurfaceTrackableId = TrackableId.invalidId;
        public MeasuredDepthSurfaceProvider measuredDepth = new MeasuredDepthSurfaceProvider();
        public bool hasDepthSupportEvidence = false;
        public Pose lastDepthSupportEvidence = default;
        public Vector2 lastDepthEvidencePointer = Vector2.zero;
        public void Capture(TapToPlaceController owner)
        {
            previewAnchor = owner.previewAnchor;
            previewVisual = owner.previewVisual;
            currentPrefabToPlace = owner.currentPrefabToPlace;
            canPlace = owner.canPlace;
            cooldownTimer = owner.cooldownTimer;
            lastPreviewTrackableId = owner.lastPreviewTrackableId;
            smoothedPlacementScreenPosition = owner.smoothedPlacementScreenPosition;
            hasSmoothedPlacementScreenPosition = owner.hasSmoothedPlacementScreenPosition;
            hasPreviewSurfacePose = owner.hasPreviewSurfacePose;
            previewSurfaceIsDepth = owner.previewSurfaceIsDepth;
            previewBoundsRadius = owner.previewBoundsRadius;
            previewBoundsLocalCenter = owner.previewBoundsLocalCenter;
            hasUngroundedPreviewFrame = owner.hasUngroundedPreviewFrame;
            ungroundedPreviewProjection = owner.ungroundedPreviewProjection;
            ungroundedPreviewSessionState = owner.ungroundedPreviewSessionState;
            placementPending = owner.placementPending;
            placementGeneration = owner.placementGeneration;
            placementRequestExpiresAt = owner.placementRequestExpiresAt;
            placementRequestScreenPosition = owner.placementRequestScreenPosition;
            placementRequestAwaitingSurface = owner.placementRequestAwaitingSurface;
            placementRequestHasSurface = owner.placementRequestHasSurface;
            placementRequestIsDepth = owner.placementRequestIsDepth;
            placementRequestFromTap = owner.placementRequestFromTap;
            placementRequestHasEstimate = owner.placementRequestHasEstimate;
            placementRequestEstimate = owner.placementRequestEstimate;
            placementTapThisFrame = owner.placementTapThisFrame;
            placementRequestDepthPose = owner.placementRequestDepthPose;
            placementRequestPlaneId = owner.placementRequestPlaneId;
            placementRequestLocalPoint = owner.placementRequestLocalPoint;
            placementRetryEligible = owner.placementRetryEligible;
            nextPlacementAttemptAt = owner.nextPlacementAttemptAt;
            surfaceTransitionActive = owner.surfaceTransitionActive;
            surfaceTransitionStartedAt = owner.surfaceTransitionStartedAt;
            surfaceTransitionStartPose = owner.surfaceTransitionStartPose;
            surfaceTransitionTrackableId = owner.surfaceTransitionTrackableId;
            loggedMeasuredPreviewGeometry = owner.loggedMeasuredPreviewGeometry;
            selectedDisplayName = owner.selectedDisplayName;
            previewIsInvalid = owner.previewIsInvalid;
            previewInput = owner.previewInput;
            previewNeedsRelease = owner.previewNeedsRelease;
            requireMeasuredForPreview = owner.requireMeasuredForPreview;
            draggedObject = owner.draggedObject;
            isDragging = owner.isDragging;
            unpinchGraceTimer = owner.unpinchGraceTimer;
            lastValidDragPos = owner.lastValidDragPos;
            dragGrabScreenPosition = owner.dragGrabScreenPosition;
            dragEye = owner.dragEye;
            dragPointerMoved = owner.dragPointerMoved;
            lastValidDragRot = owner.lastValidDragRot;
            dragStartPos = owner.dragStartPos;
            dragPointerOffset = owner.dragPointerOffset;
            dragHandLostAt = owner.dragHandLostAt;
            dragNeedsOpenHandAfterPlacement = owner.dragNeedsOpenHandAfterPlacement;
            dragFreePlane = owner.dragFreePlane;
            dragLevelTransition = owner.dragLevelTransition;
            dragStartRot = owner.dragStartRot;
            currentSmoothedDragPos = owner.currentSmoothedDragPos;
            isDragBlocked = owner.isDragBlocked;
            dragHasMeasuredSupport = owner.dragHasMeasuredSupport;
            dragSurfaceIsEstimated = owner.dragSurfaceIsEstimated;
            dragSurfaceTrackableId = owner.dragSurfaceTrackableId;
            measuredDepth = owner.measuredDepth;
            hasDepthSupportEvidence = owner.hasDepthSupportEvidence;
            lastDepthSupportEvidence = owner.lastDepthSupportEvidence;
            lastDepthEvidencePointer = owner.lastDepthEvidencePointer;
        }
        public void Restore(TapToPlaceController owner)
        {
            owner.previewAnchor = previewAnchor;
            owner.previewVisual = previewVisual;
            owner.currentPrefabToPlace = currentPrefabToPlace;
            owner.canPlace = canPlace;
            owner.cooldownTimer = cooldownTimer;
            owner.lastPreviewTrackableId = lastPreviewTrackableId;
            owner.smoothedPlacementScreenPosition = smoothedPlacementScreenPosition;
            owner.hasSmoothedPlacementScreenPosition = hasSmoothedPlacementScreenPosition;
            owner.hasPreviewSurfacePose = hasPreviewSurfacePose;
            owner.previewSurfaceIsDepth = previewSurfaceIsDepth;
            owner.previewBoundsRadius = previewBoundsRadius;
            owner.previewBoundsLocalCenter = previewBoundsLocalCenter;
            owner.hasUngroundedPreviewFrame = hasUngroundedPreviewFrame;
            owner.ungroundedPreviewProjection = ungroundedPreviewProjection;
            owner.ungroundedPreviewSessionState = ungroundedPreviewSessionState;
            owner.placementPending = placementPending;
            owner.placementGeneration = placementGeneration;
            owner.placementRequestExpiresAt = placementRequestExpiresAt;
            owner.placementRequestScreenPosition = placementRequestScreenPosition;
            owner.placementRequestAwaitingSurface = placementRequestAwaitingSurface;
            owner.placementRequestHasSurface = placementRequestHasSurface;
            owner.placementRequestIsDepth = placementRequestIsDepth;
            owner.placementRequestFromTap = placementRequestFromTap;
            owner.placementRequestHasEstimate = placementRequestHasEstimate;
            owner.placementRequestEstimate = placementRequestEstimate;
            owner.placementTapThisFrame = placementTapThisFrame;
            owner.placementRequestDepthPose = placementRequestDepthPose;
            owner.placementRequestPlaneId = placementRequestPlaneId;
            owner.placementRequestLocalPoint = placementRequestLocalPoint;
            owner.placementRetryEligible = placementRetryEligible;
            owner.nextPlacementAttemptAt = nextPlacementAttemptAt;
            owner.surfaceTransitionActive = surfaceTransitionActive;
            owner.surfaceTransitionStartedAt = surfaceTransitionStartedAt;
            owner.surfaceTransitionStartPose = surfaceTransitionStartPose;
            owner.surfaceTransitionTrackableId = surfaceTransitionTrackableId;
            owner.loggedMeasuredPreviewGeometry = loggedMeasuredPreviewGeometry;
            owner.selectedDisplayName = selectedDisplayName;
            owner.previewIsInvalid = previewIsInvalid;
            owner.previewInput = previewInput;
            owner.previewNeedsRelease = previewNeedsRelease;
            owner.requireMeasuredForPreview = requireMeasuredForPreview;
            owner.draggedObject = draggedObject;
            owner.isDragging = isDragging;
            owner.unpinchGraceTimer = unpinchGraceTimer;
            owner.lastValidDragPos = lastValidDragPos;
            owner.dragGrabScreenPosition = dragGrabScreenPosition;
            owner.dragEye = dragEye;
            owner.dragPointerMoved = dragPointerMoved;
            owner.lastValidDragRot = lastValidDragRot;
            owner.dragStartPos = dragStartPos;
            owner.dragPointerOffset = dragPointerOffset;
            owner.dragHandLostAt = dragHandLostAt;
            owner.dragNeedsOpenHandAfterPlacement = dragNeedsOpenHandAfterPlacement;
            owner.dragFreePlane = dragFreePlane;
            owner.dragLevelTransition = dragLevelTransition;
            owner.dragStartRot = dragStartRot;
            owner.currentSmoothedDragPos = currentSmoothedDragPos;
            owner.isDragBlocked = isDragBlocked;
            owner.dragHasMeasuredSupport = dragHasMeasuredSupport;
            owner.dragSurfaceIsEstimated = dragSurfaceIsEstimated;
            owner.dragSurfaceTrackableId = dragSurfaceTrackableId;
            owner.measuredDepth = measuredDepth;
            owner.hasDepthSupportEvidence = hasDepthSupportEvidence;
            owner.lastDepthSupportEvidence = lastDepthSupportEvidence;
            owner.lastDepthEvidencePointer = lastDepthEvidencePointer;
        }
    }
    private enum HandOperation { Drag, ExistingDrag, Preview, Release, Frame }
    private HandDragState secondDrag = new HandDragState();
    private readonly HandDragState primaryDrag = new HandDragState();
    private ButtonInteractor secondInput;
    private ButtonInteractor previewInput;
    private bool previewNeedsRelease;
    private bool requireMeasuredForPreview;
    private bool processingSecondHand;

    public bool IsObjectBeingDragged(GameObject obj) =>
        obj != null && ((isDragging && draggedObject == obj) ||
        (secondDrag.isDragging && secondDrag.draggedObject == obj) ||
        (processingSecondHand && primaryDrag.isDragging && primaryDrag.draggedObject == obj));

    public void SetSecondHand(ButtonInteractor input)
    {
        if (secondInput == input) return;
        if (secondInput != null && secondDrag.previewAnchor != null) RunWithSecondHand(ClearActivePreview);
        if (secondInput != null) ProcessSecondHand(HandOperation.Release);
        secondInput = input;
        secondDrag = new HandDragState();
    }

    private void ProcessSecondHand(HandOperation operation)
    {
        if (secondInput == null || (operation != HandOperation.Release && !secondInput.isActiveAndEnabled) ||
            (operation == HandOperation.ExistingDrag && !secondDrag.isDragging)) return;
        RunWithSecondHand(() =>
        {
            measuredDepth.Configure(mainCamera, arRaycastManager, buttonInteractor);
            if (operation == HandOperation.Frame)
            {
                cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);
                UpdateActiveHand();
            }
            else if (operation == HandOperation.Preview)
            {
                if (isDragging) ReleaseDraggedObject();
                UpdatePreviewAndPlacement();
            }
            else if (operation == HandOperation.Release) ReleaseDraggedObject();
            else HandleDragAndDrop();
        });
    }

    private void RunWithSecondHand(System.Action action)
    {
        ButtonInteractor primaryInput = buttonInteractor;
        primaryDrag.Capture(this);
        secondDrag.Restore(this);
        buttonInteractor = secondInput;
        processingSecondHand = true;
        try { action(); }
        finally
        {
            secondDrag.Capture(this);
            primaryDrag.Restore(this);
            buttonInteractor = primaryInput;
            processingSecondHand = false;
        }
    }

    void Update()
    {
        RefreshSurfaceGuidance();
        // Nếu đang bật Chế độ Mô phỏng, khóa hoàn toàn các thao tác chọn, nhấc và di chuyển linh kiện
        if (CircuitSimulationManager.Instance != null && CircuitSimulationManager.Instance.isSimulationMode)
        {
            return; // Dừng toàn bộ xử lý cử chỉ chọn/kéo phía dưới
        }

        // Missing optional managers must not trigger a whole-scene search every frame.
        if (Time.unscaledTime >= nextReferenceLookupAt)
        {
            nextReferenceLookupAt = Time.unscaledTime + 1f;
#if UNITY_2023_1_OR_NEWER
            if (buttonInteractor == null) buttonInteractor = FindFirstObjectByType<ButtonInteractor>();
            if (arRaycastManager == null) arRaycastManager = FindFirstObjectByType<ARRaycastManager>();
            if (wireConnectionController == null) wireConnectionController = FindFirstObjectByType<WireConnectionController>();
#else
            if (buttonInteractor == null) buttonInteractor = FindObjectOfType<ButtonInteractor>();
            if (arRaycastManager == null) arRaycastManager = FindObjectOfType<ARRaycastManager>();
            if (wireConnectionController == null) wireConnectionController = FindObjectOfType<WireConnectionController>();
#endif
        }
        if (buttonInteractor == null) return;

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        // Request measured geometry separately from visual occlusion. The latter
        // must stay disabled: enabling it previously cut the components in half.
        measuredDepth.Configure(mainCamera, arRaycastManager, buttonInteractor);

        if (cooldownTimer > 0) cooldownTimer -= Time.deltaTime;
        if (buttonInteractor.IsHandVisible && !buttonInteractor.isPinching && !buttonInteractor.IsPointerOverUI)
            worldPinchArmed = true;
        if (!buttonInteractor.IsHandVisible || buttonInteractor.PinchConsumedByUI)
            worldPinchArmed = false;

        if (menuHUD != null && menuHUD.HasSelection && currentPrefabToPlace == null)
            StartPreview(menuHUD.SelectedComponent);
        UpdateActiveHand();
        ProcessSecondHand(HandOperation.Frame);
        UpdatePlacedContacts();
    }

    private void UpdateActiveHand()
    {
        if (currentPrefabToPlace != null && previewAnchor != null)
        {
            if (isDragging) ReleaseDraggedObject();
            UpdatePreviewAndPlacement();
        }
        else HandleDragAndDrop();
    }

    public bool HasAnyPreview => currentPrefabToPlace != null || secondDrag.currentPrefabToPlace != null ||
        (processingSecondHand && primaryDrag.currentPrefabToPlace != null);

    private void UpdatePreviewAndPlacement()
    {
        if (currentPrefabToPlace == null || previewAnchor == null) return;
        if (placementPending)
        {
            if (ShouldCancelPendingPlacement())
            {
                CancelPendingPlacementAttempt();
                return;
            }
            ObservePendingPlacementGesture();
            SetPlacementFeedback(ButtonInteractor.PlacementFeedback.Anchoring);
            if (buttonInteractor.JustPinched) SetPlacementStatus("Pinch: đang chờ neo AR của lần đặt trước.");
            return;
        }
        placementTapThisFrame = false;
        bool hasTapPointer = enableTapToPlace && placementTapInput.Read(out _, out _);
        Vector2 tapPosition = placementTapInput.Position;
        placementTapThisFrame = hasTapPointer && placementTapInput.Confirmed;
        bool hasWorldPointer = HasWorldPlacementPointer() || hasTapPointer;
        Vector2 placementScreenPosition = hasTapPointer ? tapPosition : hasWorldPointer
            ? GetStablePlacementScreenPosition()
            : placementRequestHasEstimate || placementRequestFromTap ? placementRequestScreenPosition
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        UpdatePlacementRequest(hasWorldPointer, placementScreenPosition);
        if (!hasWorldPointer && !placementRequestHasSurface && !placementRequestHasEstimate && !placementRequestFromTap &&
            buttonInteractor.IsGestureTemporarilyOccluded)
        {
            // Preserve intent through a short missing-hand sample, but never
            // capture a real surface using the stale pointer during that gap.
            canPlace = false;
            SetPlacementFeedback(ButtonInteractor.PlacementFeedback.SearchingSurface);
            return;
        }
        if (ARSession.state != ARSessionState.SessionTracking)
        {
            hasDepthSupportEvidence = false;
            canPlace = false;
            if (enableInstantPlacement || !placementRequestAwaitingSurface) ClearPlacementRequest();
            surfaceTransitionActive = false;
            SetPlacementFeedback(ButtonInteractor.PlacementFeedback.SearchingSurface);
            if (buttonInteractor.JustPinched)
                SetPlacementStatus("Pinch: AR chưa tracking (" + ARSession.state + ").");
            if (!hasPreviewSurfacePose) ShowUngroundedPreview();
            return;
        }

        // Chụp điểm thật ngay khi tia pinch có hit, không đợi anchor subsystem.
        // Từ đây tay/camera dịch chuyển không được đổi điểm đang xác nhận.
        if (!placementRequestHasSurface && !placementRequestHasEstimate && Time.unscaledTime <= placementRequestExpiresAt)
        {
            if (TryGetMeasuredPlacementSurface(placementRequestScreenPosition, lastPreviewTrackableId,
                out Pose requestPose, out TrackableId requestPlaneId, out bool requestIsDepth))
                TryCaptureMeasuredPlacementRequest(requestPose, requestPlaneId, requestIsDepth);
            else if (IsWorldPinchHeld() && !enableInstantPlacement) placementRequestAwaitingSurface = true;
            if (enableInstantPlacement && !requireMeasuredForPreview && !placementRequestHasSurface && !placementRequestHasEstimate &&
                TryGetInstantPlacementPose(placementRequestScreenPosition, out placementRequestEstimate))
                placementRequestHasEstimate = true;
        }

        Pose surfacePose = default;
        TrackableId hitTrackableId = TrackableId.invalidId;
        bool surfaceIsDepth = placementRequestHasSurface && placementRequestIsDepth;
        bool hasSurface = placementRequestHasSurface &&
            TryResolvePlacementRequestSurface(out surfacePose, out hitTrackableId);
        if (placementRequestHasSurface && !hasSurface)
        {
            ClearPlacementRequest();
            surfaceTransitionActive = false;
        }
        if (!hasSurface && hasWorldPointer && !placementRequestHasEstimate)
            hasSurface = TryGetMeasuredPlacementSurface(placementScreenPosition, lastPreviewTrackableId,
                out surfacePose, out hitTrackableId, out surfaceIsDepth);

        if (hasSurface)
        {
            Vector3 contactPosition = GetContactPosition(surfacePose);
            Quaternion rotation = previewAnchor.activeSelf ? previewAnchor.transform.rotation : GetPlacementRotation();
            UpdatePreviewSurfacePose(contactPosition, rotation, hitTrackableId);
            previewSurfaceIsDepth = surfaceIsDepth;
            if (!loggedMeasuredPreviewGeometry)
            {
                LogPreviewGeometry("measured-" + (surfaceIsDepth ? "depth" : "plane"));
                loggedMeasuredPreviewGeometry = true;
            }
            lastKnownPlacementPose = new Pose(contactPosition, rotation);
            lastKnownSurfaceTrackableId = hitTrackableId;
            hasLastKnownPlacementPose = true;

            // Đỏ chỉ có nghĩa va chạm linh kiện, không phải raycast hụt mép bàn.
            bool overlapping = CheckOverlap(contactPosition, null, out _);
            UpdatePreviewVisual(overlapping);
            canPlace = !overlapping && (enableInstantPlacement || !surfaceTransitionActive) && cooldownTimer <= 0f &&
                (enableInstantPlacement || CanCreateMeasuredSurfaceAnchor(hitTrackableId, surfaceIsDepth));
            SetPlacementFeedback(overlapping ? ButtonInteractor.PlacementFeedback.Blocked :
                (canPlace ? ButtonInteractor.PlacementFeedback.Ready : ButtonInteractor.PlacementFeedback.Anchoring));
            if (!overlapping) TryCommitPlacementRequest();
            return;
        }

        Pose estimatedPose = placementRequestEstimate;
        bool hasEstimatedPose = placementRequestHasEstimate ||
            (enableInstantPlacement && TryGetInstantPlacementPose(placementScreenPosition, out estimatedPose));
        if (enableInstantPlacement && (hasWorldPointer || placementRequestHasEstimate || placementRequestFromTap) && hasEstimatedPose)
        {
            surfaceTransitionActive = false;
            hasPreviewSurfacePose = false;
            previewSurfaceIsDepth = false;
            lastPreviewTrackableId = TrackableId.invalidId;
            previewAnchor.transform.SetPositionAndRotation(estimatedPose.position, estimatedPose.rotation);
            previewAnchor.SetActive(true);
            bool overlapping = CheckOverlap(estimatedPose.position, null, out _);
            UpdatePreviewVisual(overlapping);
            canPlace = !requireMeasuredForPreview && !overlapping && cooldownTimer <= 0f;
            SetPlacementFeedback(overlapping ? ButtonInteractor.PlacementFeedback.Blocked :
                (requireMeasuredForPreview ? ButtonInteractor.PlacementFeedback.SearchingSurface : ButtonInteractor.PlacementFeedback.Ready));
            if (canPlace && Time.unscaledTime <= placementRequestExpiresAt && placementRequestHasEstimate)
                PlaceInstantObject(placementRequestEstimate, placementRequestScreenPosition, TrackableId.invalidId, false);
            return;
        }

        canPlace = false;
        SetPlacementFeedback(ButtonInteractor.PlacementFeedback.SearchingSurface);
        if (!hasPreviewSurfacePose) ShowUngroundedPreview();
        else UpdatePreviewVisual(false);
    }

    private ButtonInteractor PlacementInput => previewInput != null ? previewInput : buttonInteractor;

    private bool HasWorldPlacementPointer()
    {
        var input = PlacementInput;
        return input != null && input.IsHandVisible && !input.PinchConsumedByUI &&
            (!input.IsPointerOverUI || input.isPinching);
    }

    private void ClearPlacementRequest()
    {
        placementRequestExpiresAt = float.NegativeInfinity;
        placementRequestAwaitingSurface = false;
        placementRequestHasSurface = false;
        placementRequestIsDepth = false;
        placementRequestFromTap = false;
        placementRequestHasEstimate = false;
        placementRequestDepthPose = Pose.identity;
        placementRequestPlaneId = TrackableId.invalidId;
        placementRequestLocalPoint = Vector3.zero;
    }

    private void UpdatePlacementRequest(bool hasWorldPointer, Vector2 screenPosition)
    {
        if (previewNeedsRelease)
        {
            ClearPlacementRequest();
            if (buttonInteractor.IsHandVisible && !buttonInteractor.isPinching && !buttonInteractor.IsGestureTemporarilyOccluded)
                previewNeedsRelease = false;
            return;
        }
        if (placementTapThisFrame)
        {
            ClearPlacementRequest();
            placementRequestFromTap = true;
            placementRequestScreenPosition = screenPosition;
            placementRequestExpiresAt = Time.unscaledTime + Mathf.Max(0f, cooldownTimer) + 0.35f;
            return;
        }
        if (placementRequestFromTap)
        {
            if (Time.unscaledTime > placementRequestExpiresAt) ClearPlacementRequest();
            return;
        }
        if (placementRequestHasSurface || (enableInstantPlacement && placementRequestHasEstimate))
        {
            // Pinch đã trúng điểm thật là một lệnh đã nhận: nhả/mất tay ngắn
            // không được nuốt lệnh trong chuyển tiếp. Một cử chỉ UI mới vẫn hủy.
            if (buttonInteractor.JustPinched && buttonInteractor.PinchConsumedByUI)
                ClearPlacementRequest();
            else if (Time.unscaledTime > placementRequestExpiresAt) ClearPlacementRequest();
            else if (hasWorldPointer && buttonInteractor.isPinching)
                placementRequestExpiresAt = Time.unscaledTime + previewSurfaceTransitionDuration + 0.7f;
            return;
        }
        if (!hasWorldPointer && buttonInteractor.IsGestureTemporarilyOccluded &&
            !buttonInteractor.PinchConsumedByUI && Time.unscaledTime <= placementRequestExpiresAt)
            return;
        if (!hasWorldPointer)
        {
            ClearPlacementRequest();
            if (buttonInteractor.JustPinched)
                SetPlacementStatus("Pinch: cử chỉ đang thuộc UI hoặc không còn dữ liệu tay hợp lệ.");
            return;
        }

        if (buttonInteractor.JustPinched && HasWorldPlacementPointer())
        {
            // JustPinched đã là cạnh một lần; không bắt thêm khóa worldPinchArmed.
            ClearPlacementRequest();
            placementRequestScreenPosition = screenPosition;
            placementRequestExpiresAt = Time.unscaledTime + Mathf.Max(0f, cooldownTimer) + 0.35f;
            placementRequestAwaitingSurface = !hasPreviewSurfacePose ||
                ARSession.state != ARSessionState.SessionTracking;
            worldPinchArmed = false;
        }

        if (!placementRequestAwaitingSurface) return;
        if (!buttonInteractor.isPinching)
        {
            // Không tự đặt muộn khi người dùng đã nhả pinch từ lúc chưa có mặt thật.
            ClearPlacementRequest();
            return;
        }

        // Khi chưa có mặt thật, giữ ý định trong chính cử chỉ đang giữ và dữ liệu
        // tay còn mới. Chỉ chụp cố định tia sau khi đã có một điểm đặt thật.
        placementRequestScreenPosition = screenPosition;
        placementRequestExpiresAt = Time.unscaledTime + Mathf.Max(0f, cooldownTimer) + 0.35f;
    }

    private bool TryCapturePlacementRequestSurface(Pose surfacePose, TrackableId planeId)
    {
        if (placementRequestHasSurface || Time.unscaledTime > placementRequestExpiresAt ||
            ARSession.state != ARSessionState.SessionTracking) return false;
        ARPlane plane = arPlaneManager != null ? arPlaneManager.GetPlane(planeId) : null;
        if (!IsUsableSurfacePlane(plane)) return false;
        Vector3 local = plane.transform.InverseTransformPoint(surfacePose.position);
        if (Mathf.Abs(local.y) > 0.03f || !IsPointInSurfaceBoundary(new Vector2(local.x, local.z), plane.boundary)) return false;
        placementRequestLocalPoint = new Vector3(local.x, 0f, local.z);
        placementRequestPlaneId = planeId;
        placementRequestHasSurface = true;
        placementRequestIsDepth = false;
        placementRequestAwaitingSurface = false;
        placementRequestExpiresAt = Time.unscaledTime + Mathf.Max(0f, cooldownTimer) + previewSurfaceTransitionDuration + 0.7f;
        return true;
    }

    private bool TryCaptureMeasuredPlacementRequest(Pose surfacePose, TrackableId planeId, bool isDepth)
    {
        if (!isDepth) return TryCapturePlacementRequestSurface(surfacePose, planeId);
        if (placementRequestHasSurface || Time.unscaledTime > placementRequestExpiresAt ||
            ARSession.state != ARSessionState.SessionTracking || !measuredDepth.IsReady ||
            planeId != TrackableId.invalidId) return false;

        // This pose has passed the finite measured-depth patch and temporal tests.
        // Freeze it in world space; never re-project the moving finger/camera while
        // the native anchor request is in flight.
        placementRequestDepthPose = surfacePose;
        placementRequestPlaneId = TrackableId.invalidId;
        placementRequestHasSurface = true;
        placementRequestIsDepth = true;
        placementRequestAwaitingSurface = false;
        placementRequestExpiresAt = Time.unscaledTime + Mathf.Max(0f, cooldownTimer) +
            previewSurfaceTransitionDuration + 0.7f;
        return true;
    }

    private bool TryResolvePlacementRequestSurface(out Pose pose, out TrackableId planeId)
    {
        pose = default;
        planeId = TrackableId.invalidId;
        if (!placementRequestHasSurface || ARSession.state != ARSessionState.SessionTracking) return false;
        if (placementRequestIsDepth)
        {
            if (!measuredDepth.IsReady) return false;
            pose = placementRequestDepthPose;
            return true;
        }
        ARPlane plane = arPlaneManager != null ? arPlaneManager.GetPlane(placementRequestPlaneId) : null;
        if (!IsUsableSurfacePlane(plane) || !IsPointInSurfaceBoundary(
            new Vector2(placementRequestLocalPoint.x, placementRequestLocalPoint.z), plane.boundary)) return false;
        pose = new Pose(plane.transform.TransformPoint(placementRequestLocalPoint), plane.transform.rotation);
        planeId = plane.trackableId;
        return true;
    }

    private bool TryCommitPlacementRequest()
    {
        if (previewNeedsRelease || Time.unscaledTime > placementRequestExpiresAt || cooldownTimer > 0f ||
            (!enableInstantPlacement && surfaceTransitionActive) ||
            Time.unscaledTime < nextPlacementAttemptAt) return false;
        if (!TryResolvePlacementRequestSurface(out Pose surfacePose, out TrackableId planeId)) return false;

        // SessionTracking/plane có thể sẵn sàng trước anchor subsystem trong vài
        // frame đầu. Đừng nuốt lệnh pinch trong khoảng khởi động ngắn đó.
        if (!enableInstantPlacement && !CanCreateMeasuredSurfaceAnchor(planeId, placementRequestIsDepth))
            return false;

        Vector3 contactPosition = GetContactPosition(surfacePose);
        Quaternion rotation = hasPreviewSurfacePose ? previewAnchor.transform.rotation : GetPlacementRotation();
        placementRequestExpiresAt = float.NegativeInfinity;
        // Kiểm tra đúng điểm sẽ xác nhận, không kiểm tra điểm preview còn đang lerp.
        if (CheckOverlap(contactPosition, null, out _))
        {
            UpdatePreviewVisual(true);
            canPlace = false;
            ClearPlacementRequest();
            SetPlacementStatus("Pinch: vị trí đặt chồng lên linh kiện khác.");
            return false;
        }

        previewAnchor.transform.SetPositionAndRotation(contactPosition, rotation);
        previewAnchor.SetActive(true);
        hasPreviewSurfacePose = true;
        lastPreviewTrackableId = planeId;
        UpdatePreviewVisual(false);
        if (enableInstantPlacement)
            PlaceInstantObject(new Pose(contactPosition, rotation), placementRequestScreenPosition, planeId, true);
        else
            TryPlaceObject(new Pose(contactPosition, rotation), planeId);
        canPlace = false;
        return placementPending || currentPrefabToPlace == null;
    }

    private void UpdatePreviewSurfacePose(Vector3 contactPosition, Quaternion rotation, TrackableId planeId)
    {
        bool firstSurface = !hasPreviewSurfacePose;
        bool changedLevel = (planeId != lastPreviewTrackableId ||
            (planeId == TrackableId.invalidId && previewSurfaceIsDepth)) &&
            Mathf.Abs(contactPosition.y - previewAnchor.transform.position.y) > 0.08f;
        if (previewAnchor.activeSelf && (firstSurface || changedLevel) &&
            (!surfaceTransitionActive || surfaceTransitionTrackableId != planeId))
        {
            surfaceTransitionActive = true;
            surfaceTransitionStartedAt = Time.unscaledTime;
            surfaceTransitionStartPose = new Pose(previewAnchor.transform.position, previewAnchor.transform.rotation);
            surfaceTransitionTrackableId = planeId;
            canPlace = false;
        }

        if (surfaceTransitionActive)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - surfaceTransitionStartedAt) /
                Mathf.Max(0.01f, previewSurfaceTransitionDuration));
            float smooth = Mathf.SmoothStep(0f, 1f, t);
            previewAnchor.transform.SetPositionAndRotation(
                Vector3.Lerp(surfaceTransitionStartPose.position, contactPosition, smooth),
                Quaternion.Slerp(surfaceTransitionStartPose.rotation, rotation, smooth));
            if (t >= 1f) surfaceTransitionActive = false;
        }
        else if (firstSurface || !previewAnchor.activeSelf)
            previewAnchor.transform.SetPositionAndRotation(contactPosition, rotation);
        else
        {
            Vector3 position = Vector3.Lerp(previewAnchor.transform.position, contactPosition,
                1f - Mathf.Exp(-previewFollowSpeed * Time.unscaledDeltaTime));
            position.y = contactPosition.y;
            previewAnchor.transform.SetPositionAndRotation(position, rotation);
        }
        previewAnchor.SetActive(true);
        hasPreviewSurfacePose = true;
        lastPreviewTrackableId = planeId;
    }

    private void SetPlacementFeedback(ButtonInteractor.PlacementFeedback feedback)
    {
        if (PlacementInput != null) PlacementInput.SetPlacementFeedback(feedback);
        if (feedback != ButtonInteractor.PlacementFeedback.None &&
            Time.unscaledTime >= nextPlacementDiagnosticAt)
        {
            int trackedPlanes = 0;
            if (arPlaneManager != null)
                foreach (ARPlane plane in arPlaneManager.trackables)
                    if (IsUsableSurfacePlane(plane)) trackedPlanes++;
            Debug.Log($"[ARPlacement] {feedback}; session={ARSession.state}; trackingReason={ARSession.notTrackingReason}; horizontalPlanes={trackedPlanes}; " +
                $"captured={placementRequestHasSurface}; transition={surfaceTransitionActive}; " +
                $"source={(hasPreviewSurfacePose ? (previewSurfaceIsDepth ? "depth" : "plane") : "unmeasured")}; " +
                $"depthReady={measuredDepth.IsReady}; depthSamples={measuredDepth.LastSupportSampleCount}; " +
                $"handMasked={measuredDepth.HandMaskedProbeCount}; probeRadius={measuredDepth.LastProbeRadiusPixels:F0}; " +
                $"depthHits={measuredDepth.LastNativeHitCount}/{measuredDepth.LastNativeQueryCount}; " +
                $"noisyNormal={measuredDepth.LastUnreliableNormalCount}; rejectRange={measuredDepth.LastRejectedRangeCount}; " +
                $"rejectHeight={measuredDepth.LastRejectedHeightCount}");
            nextPlacementDiagnosticAt = Time.unscaledTime + 2f;
        }
        lastPlacementFeedback = feedback;
    }

    private void PlaceObject(Vector3 position, Quaternion rotation, ARAnchor nativeAnchor)
    {
        if (nativeAnchor == null)
            throw new System.InvalidOperationException("Placed content requires a native anchor outside the camera hierarchy.");
        CompletePlacement(position, rotation, nativeAnchor.transform, nativeAnchor);
    }

    private void CompletePlacement(Vector3 position, Quaternion rotation, Transform support, ARAnchor nativeAnchor)
    {
        if (previewAnchor == null || !previewAnchor.activeSelf) return;
        if (support == null || (mainCamera != null && support.IsChildOf(mainCamera.transform)))
            throw new System.InvalidOperationException("Placed content requires a world binding outside the camera hierarchy.");

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

        // Chỉ chuyển sang vật đã đặt sau khi native anchor được tạo thành công.
        // Subsequent contact verification may correct Y, never camera/finger XZ.
        previewAnchor.transform.SetParent(support, true);
        previewAnchor.transform.SetPositionAndRotation(position, rotation);
        previewAnchor.transform.localScale = Vector3.one;
        if (previewVisual != null)
        {
            // Tính lại sau khi gắn native anchor để sai số transform/pivot không
            // làm đáy vật nhấc khỏi mặt thật ở frame xác nhận.
            AlignVisualBaseToAnchor(previewAnchor, previewVisual);
        }

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
        GameObject placedObject = previewAnchor;
        if (nativeAnchor != null)
        {
            if (!nativeAnchor.gameObject.name.StartsWith("SurfaceAnchor_"))
                nativeAnchor.gameObject.name = "SurfaceAnchor_" + placedObject.name;
            objectAnchors[placedObject] = nativeAnchor;
            var stabilizer = placedObject.AddComponent<ARPlacedObjectStabilizer>();
            stabilizer.Bind(this, nativeAnchor);
        }
        geometryCache.Invalidate(placedObject);
        placedObjectsHistory.Add(placedObject);
        RememberPlacedContact(placedObject, placementRequestPlaneId);
        Debug.Log($"[ARBinding] {placedObject.name}#{placedObject.GetInstanceID()}; nativeAnchor={nativeAnchor != null}; " +
            $"world={placedObject.transform.position:F3}; local={placedObject.transform.localPosition:F3}");
        placementConfirmedUntil = Time.unscaledTime + 2.5f;
        // A closed placement gesture, including a hand re-detected still
        // closed, must never silently become a new grab of the placed object.
        if (previewInput == secondInput && secondInput != null && !processingSecondHand)
            secondDrag.dragNeedsOpenHandAfterPlacement = true;
        else if (processingSecondHand && previewInput != secondInput)
            primaryDrag.dragNeedsOpenHandAfterPlacement = true;
        else dragNeedsOpenHandAfterPlacement = true;
        History.RecordAction(new PlaceComponentAction(placedObject, this));
        Debug.Log($"<color=green>[TapToPlace]</color> Đã đặt thành công: [{placedObject.name}]. Tổng số linh kiện: {placedObjectsHistory.Count}");

        previewAnchor = null;
        previewVisual = null;
        currentPrefabToPlace = null;
        if (menuHUD != null) menuHUD.ClearSelection();
        cooldownTimer = 0.5f;
        placementRetryEligible = false;
        ClearPlacementRequest();
        surfaceTransitionActive = false;
        SetPlacementFeedback(ButtonInteractor.PlacementFeedback.None);
        worldPinchArmed = false;
        if (placementStatusRoot != null) placementStatusRoot.SetActive(false);
        OnPlacementFinished?.Invoke();
    }

    private void BeginHandDrag(GameObject target, Vector2 screenPos)
    {
        draggedObject = target;
        draggedObject.GetComponent<ARInstantPlacementAnchor>()?.StopRefinement();
        isDragging = true;
        dragStartPos = draggedObject.transform.position;
        dragStartRot = draggedObject.transform.rotation;
        dragLevelTransition.Begin(dragStartPos.y);
        Vector2 baseScreen = mainCamera.WorldToScreenPoint(dragStartPos);
        if (dragEye >= 0)
        {
            if (PhoneHeadsetStereo.TryProjectIntoEye(mainCamera, dragStartPos, dragEye == 1,
                out Vector2 eyeScreen, clipToLens: false))
                baseScreen = eyeScreen;
            else
                dragEye = -1;
        }
        dragPointerOffset = baseScreen - screenPos;
        dragGrabScreenPosition = screenPos;
        dragPointerMoved = false;
        lastValidDragPos = dragStartPos;
        lastValidDragRot = dragStartRot;
        currentSmoothedDragPos = dragStartPos;
        isDragBlocked = false;
        unpinchGraceTimer = 0f;
        dragSurfaceTrackableId = TrackableId.invalidId;
        dragHasMeasuredSupport = false;
        if (placedContacts.TryGetValue(draggedObject, out PlacedContact contact))
        {
            dragSurfaceTrackableId = contact.planeId;
            contact.settling = false;
            contact.confirmation.Reset();
        }

        if (SelectedComponent == draggedObject) UpdateSelectedComponentVisual();
        else SelectComponent(draggedObject);
        Debug.Log("[ARDrag] Grabbed " + draggedObject.name);
        dragFreePlane = new Plane(mainCamera.transform.forward, dragStartPos);
    }

    private void HandleDragAndDrop()
    {
        // Tracking/UI ownership ends a drag immediately. A brief hand occlusion
        // below freezes the last measured world pose instead of dropping it.
        if ((!isDragging && buttonInteractor.IsPointerOverUI) ||
            buttonInteractor.PinchConsumedByUI || ARSession.state != ARSessionState.SessionTracking ||
            (!isDragging && wireConnectionController != null && wireConnectionController.IsWireMode))
        {
            placementTapInput.Reset();
            if (isDragging) ReleaseDraggedObject();
            return;
        }
        if (!isDragging && !buttonInteractor.isPinching && !buttonInteractor.JustPinched &&
            enableTapToPlace && !buttonInteractor.IsHeadsetMode &&
            placementTapInput.Read(out Vector2 tap, out bool confirmed) && confirmed)
        {
            if (TryFindTapTarget(tap, out GameObject tappedObject)) SelectComponent(tappedObject);
            else ClearSelectedComponent();
            return;
        }
        if (dragNeedsOpenHandAfterPlacement)
        {
            if (buttonInteractor.IsHandVisible && !buttonInteractor.isPinching &&
                !buttonInteractor.IsGestureTemporarilyOccluded)
                dragNeedsOpenHandAfterPlacement = false;
            return;
        }
        if (!buttonInteractor.IsHandVisible)
        {
            if (isDragging)
            {
                if (float.IsNegativeInfinity(dragHandLostAt)) dragHandLostAt = Time.unscaledTime;
                // No stale ray movement: freeze, but survive a brief occluded hand.
                if (draggedObject != null)
                    RestoreLastDragPose();
                if (Time.unscaledTime - dragHandLostAt >= 0.35f) ReleaseDraggedObject();
            }
            return;
        }
        dragHandLostAt = float.NegativeInfinity;
        Vector2 screenPos = GetCursorScreenPosition();
        // 1. BẮT ĐẦU KÉO / CHỌN LINH KIỆN
        if (buttonInteractor.JustPinched && cooldownTimer <= 0 && !isDragging)
        {
            placementTapInput.Reset();
            worldPinchArmed = false;
            if (TryFindPinchTarget(screenPos, out GameObject target, out dragEye))
            {
                if (IsObjectBeingDragged(target)) return;
                BeginHandDrag(target, screenPos);
            }
            else if (SelectedComponent != null && !IsObjectBeingDragged(SelectedComponent))
            {
#if UNITY_2023_1_OR_NEWER
                if (floatingMenu == null) floatingMenu = FindFirstObjectByType<ARFloatingBubbleMenu>();
#else
                if (floatingMenu == null) floatingMenu = FindObjectOfType<ARFloatingBubbleMenu>();
#endif
                if (floatingMenu != null && (floatingMenu.IsPointerOverDeleteButton(screenPos) || floatingMenu.IsPointerOverRotateButton(screenPos)))
                {
                    return;
                }

                ClearSelectedComponent();
            }
        }

        // 2. ĐANG KÉO
        if (isDragging && draggedObject != null)
        {
            if (buttonInteractor.isPinching)
            {
                unpinchGraceTimer = 0f;

                float grabSlop = Mathf.Max(8f, mainCamera.pixelHeight * 0.008f);
                if (!dragPointerMoved && (screenPos - dragGrabScreenPosition).sqrMagnitude < grabSlop * grabSlop)
                    return;
                dragPointerMoved = true;

                if (TryGetDragSurface(screenPos, out Pose surfacePose, out TrackableId currentPlaneId))
                {
                    Vector3 targetPoint = dragSurfaceIsEstimated ? surfacePose.position : GetContactPosition(surfacePose);
                    // A cross-level interpolation can display the new height above the old surface.
                    bool sameSupport = currentPlaneId == dragSurfaceTrackableId &&
                        Mathf.Abs(targetPoint.y - lastValidDragPos.y) <= ARSupportPolicy.LevelTransition.SameLevelTolerance;
                    currentSmoothedDragPos = targetPoint;

                    bool isOverlapping = CheckOverlap(currentSmoothedDragPos, draggedObject, out _, 0f);
                    Quaternion dragTargetRot = dragStartRot;

                    draggedObject.transform.position = currentSmoothedDragPos;
                    draggedObject.transform.rotation = dragTargetRot;

                    // Reassigning the same materials each frame allocates arrays
                    // and invalidates render state on both headset cameras.
                    if (isDragBlocked != isOverlapping)
                    {
                        if (isOverlapping) ApplySelectionInvalid(draggedObject);
                        else ApplySelectionHighlight(draggedObject);
                    }
                    isDragBlocked = isOverlapping;
                    if (!isOverlapping)
                    {
                        lastValidDragPos = currentSmoothedDragPos;
                        lastValidDragRot = dragTargetRot;
                        dragSurfaceTrackableId = currentPlaneId;
                        dragHasMeasuredSupport = !dragSurfaceIsEstimated;
                    }
                    else if (!sameSupport) dragLevelTransition.Begin(lastValidDragPos.y);

                    // Active wires already rebuild after this move in LateUpdate.
                    if (wireConnectionController != null && !wireConnectionController.isActiveAndEnabled)
                        wireConnectionController.UpdateActiveWirePositionsExternal();
                }
                else
                {
                    // Giữ vị trí hợp lệ gần nhất nếu hoàn toàn không có thông tin
                    RestoreLastDragPose();
                }
            }
            else
            {
                unpinchGraceTimer += Time.deltaTime;
                if (unpinchGraceTimer > 0.22f)
                {
                    ReleaseDraggedObject();
                }
            }
        }
    }

    private void RestoreLastDragPose()
    {
        bool restoreHighlight = isDragBlocked && draggedObject.transform.position != lastValidDragPos;
        draggedObject.transform.SetPositionAndRotation(lastValidDragPos, lastValidDragRot);
        currentSmoothedDragPos = lastValidDragPos;
        if (restoreHighlight)
        {
            if (CheckOverlap(draggedObject.transform.position, draggedObject, out _)) ApplySelectionInvalid(draggedObject);
            else ApplySelectionHighlight(draggedObject);
        }
    }

    private void ReleaseDraggedObject()
    {
        GameObject releasedObject = draggedObject;
        if (draggedObject != null)
        {
            // Thả tay bình thường trên bàn -> Đảm bảo không bị chồng lấn
            bool isOverlapping = CheckOverlap(draggedObject.transform.position, draggedObject, out _);
            if (isOverlapping)
            {
                draggedObject.transform.position = lastValidDragPos;
                draggedObject.transform.rotation = lastValidDragRot;
            }

            // Keep a nearby genuine anchor. Distant moves need an async native
            // replacement; a pending request is not a reason to bounce the
            // component back up onto its original tabletop.
            if (dragHasMeasuredSupport)
            {
                instantPlacedObjects.Remove(draggedObject);
                RememberPlacedContact(draggedObject, dragSurfaceTrackableId);
            }
            if (Vector3.Distance(dragStartPos, draggedObject.transform.position) > 0.006f &&
                (!HasReusableDragAnchor(draggedObject) ||
                 !IsSameDragSupportLevel(dragStartPos.y, draggedObject.transform.position.y)))
                ReanchorDraggedObject(draggedObject);

            if (SelectedComponent == draggedObject)
            {
                UpdateSelectedComponentVisual();
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

        isDragging = false;
        draggedObject = null;
        RestoreReleasedHighlight(releasedObject);
        dragEye = -1;
        unpinchGraceTimer = 0f;
        isDragBlocked = false;
        dragSurfaceTrackableId = TrackableId.invalidId;
        dragHasMeasuredSupport = false;
        dragPointerOffset = Vector2.zero;
        dragHandLostAt = float.NegativeInfinity;
        dragLevelTransition.CancelPending();
        worldPinchArmed = false;
    }

    private async void ReanchorDraggedObject(GameObject obj)
    {
        if (obj == null || arAnchorManager == null || !arAnchorManager.isActiveAndEnabled ||
            arAnchorManager.subsystem == null || !arAnchorManager.subsystem.running ||
            ARSession.state != ARSessionState.SessionTracking) return;
        ARAnchor previous = obj.GetComponentInParent<ARAnchor>();
        // Never replace a real binding with a free-floating Transform fallback.
        if (previous == null) return;
        Transform oldParent = obj.transform.parent;
        Vector3 localPosition = obj.transform.localPosition;
        Quaternion localRotation = obj.transform.localRotation;
        Pose requested = new Pose(obj.transform.position, obj.transform.rotation);
        if (TryGetNearbySupportAnchor(requested.position, obj, out ARAnchor shared) && shared != previous)
        {
            AdoptSupportAnchor(obj, shared, requested, previous);
            return;
        }
        int request = ++nextDragAnchorRequest;
        dragAnchorRequests[obj] = request;
        var stabilizer = obj.GetComponent<ARPlacedObjectStabilizer>();
        if (stabilizer != null) stabilizer.SuspendFiltering = true;
        ARAnchor replacement = null;
        bool StillCurrent() => this != null && obj != null && obj.activeInHierarchy && isActiveAndEnabled &&
            dragAnchorRequests.TryGetValue(obj, out int current) && current == request &&
            !IsObjectBeingDragged(obj) && obj.transform.parent == oldParent &&
            Vector3.Distance(obj.transform.localPosition, localPosition) < 0.0001f &&
            Quaternion.Angle(obj.transform.localRotation, localRotation) < 0.1f;
        try
        {
            // ReleaseDraggedObject clears its interaction state in this frame.
            // Even a synchronously completed native API must not mistake that
            // original release for a new grab and discard its replacement.
            await Awaitable.NextFrameAsync();
            if (!StillCurrent() || ARSession.state != ARSessionState.SessionTracking) return;
            var result = await arAnchorManager.TryAddAnchorAsync(requested);
            if (result.status.IsSuccess()) replacement = result.value;
            float deadline = Time.realtimeSinceStartup + 2f;
            while (replacement != null && StillCurrent() && ARSession.state == ARSessionState.SessionTracking &&
                (replacement.pending || replacement.trackingState != TrackingState.Tracking) &&
                Time.realtimeSinceStartup < deadline)
                await Awaitable.NextFrameAsync();
            if (replacement == null || !StillCurrent() || replacement.pending ||
                replacement.trackingState != TrackingState.Tracking || ARSession.state != ARSessionState.SessionTracking) return;

            // Keep the currently presented contact through the async handoff.
            // The old native frame may have refined while the new one was pending.
            AdoptSupportAnchor(obj, replacement, new Pose(obj.transform.position, obj.transform.rotation), previous);
            replacement = null; // Ownership transferred to the component.
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[TapToPlace] New support anchor not ready; retaining the genuine previous anchor: " + ex.Message);
        }
        finally
        {
            if (replacement != null) Destroy(replacement.gameObject);
            if (this != null && dragAnchorRequests.TryGetValue(obj, out int current) && current == request)
            {
                dragAnchorRequests.Remove(obj);
                if (stabilizer != null) stabilizer.SuspendFiltering = false;
            }
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) CancelContactCorrections();
    }

    private void CancelContactCorrections()
    {
        contactDepth.Reset();
        foreach (PlacedContact contact in placedContacts.Values)
        {
            contact.settling = false;
            contact.confirmation.Reset();
        }
    }

    private void RememberPlacedContact(GameObject obj, TrackableId planeId)
    {
        placedContacts[obj] = new PlacedContact { planeId = planeId,
            parent = obj.transform.parent, expectedLocal = GetAuthoredContactLocalPosition(obj) };
    }

    private void UpdatePlacedContacts()
    {
        bool idle = ARSession.state == ARSessionState.SessionTracking && !IsDragging && !placementPending &&
            !HasAnyPreview && !(menuHUD != null && menuHUD.HasSelection) &&
            !(buttonInteractor != null && buttonInteractor.isPinching) &&
            !(secondInput != null && secondInput.isPinching) &&
            !(wireConnectionController != null && wireConnectionController.IsWireMode);
        bool wiresChanged = false;
        foreach (var entry in placedContacts)
        {
            GameObject obj = entry.Key;
            PlacedContact contact = entry.Value;
            if (obj == null) continue;
            if (!idle || !obj.activeInHierarchy || !HasStableContactBinding(obj) ||
                dragAnchorRequests.ContainsKey(obj))
            {
                contact.settling = false;
                contact.confirmation.Reset();
                continue;
            }
            if (obj.transform.parent != contact.parent)
            {
                contact.parent = obj.transform.parent;
                contact.expectedLocal = GetAuthoredContactLocalPosition(obj);
                contact.settling = false;
                contact.confirmation.Reset();
            }
            else if (Vector3.Distance(GetAuthoredContactLocalPosition(obj), contact.expectedLocal) > 0.006f)
            {
                // Undo/redo or another authored move owns the new contact. Do not
                // settle it back onto the surface associated with the old pose.
                contact.planeId = TrackableId.invalidId;
                contact.settling = false;
                contact.confirmation.Reset();
            }
            if (contact.settling)
            {
                StepContactCorrection(obj.transform, contact.targetHeight, Time.unscaledDeltaTime);
                wiresChanged = true;
                if (obj.transform.position.y <= contact.targetHeight + 0.0001f)
                {
                    contact.settling = false;
                    ReanchorDraggedObject(obj);
                }
            }
            contact.expectedLocal = GetAuthoredContactLocalPosition(obj);
        }
        if (wiresChanged) wireConnectionController?.UpdateActiveWirePositionsExternal();
        if (!idle || mainCamera == null || arPlaneManager == null ||
            placedObjectsHistory.Count == 0 || Time.unscaledTime < nextContactProbeAt) return;

        // At most one eight-sample environment query per 250 ms, only while no
        // placement/drag is querying depth. Each object has its own confirmation.
        nextContactProbeAt = Time.unscaledTime + 0.25f;
        contactProbeIndex = (contactProbeIndex + 1) % placedObjectsHistory.Count;
        GameObject candidate = placedObjectsHistory[contactProbeIndex];
        if (candidate == null || !candidate.activeInHierarchy ||
            !placedContacts.TryGetValue(candidate, out PlacedContact state) || state.settling ||
            dragAnchorRequests.ContainsKey(candidate) ||
            !HasStableContactBinding(candidate)) return;
        if (!TryGetVerticalContactSupport(candidate.transform.position, state.planeId,
            out Vector3 support, out TrackableId supportId))
        {
            state.confirmation.Reset();
            LogContactProbe(candidate, "no bounded vertical correction; intendedPlane=" + state.planeId);
            return;
        }
        Vector3 screen = mainCamera.WorldToScreenPoint(support);
        if (screen.z <= mainCamera.nearClipPlane || screen.x < 0f || screen.y < 0f ||
            screen.x >= Screen.width || screen.y >= Screen.height)
        {
            state.confirmation.Reset();
            return;
        }
        contactDepth.Configure(mainCamera, arRaycastManager, buttonInteractor);
        // Never reuse another component's finite patch or temporal confirmation.
        contactDepth.Reset();
        bool measured = contactDepth.TryGetSurface(new Vector2(screen.x, screen.y), out Pose depth);
        if (!measured && contactDepth.IsConfirmingSurface)
        {
            depth = contactDepth.ConfirmingSurfacePose;
            measured = true; // The per-object confirmation below supplies the dwell.
        }
        // Confirm a bounded tracked plane even on devices without running depth.
        // A running depth provider still has to agree before changing contact.
        if (!measured && !contactDepth.IsReady)
        {
            depth = new Pose(support, Quaternion.identity);
            measured = true;
        }
        if (!measured || Mathf.Abs(depth.position.y - support.y) > 0.025f ||
            Vector2.Distance(new Vector2(depth.position.x, depth.position.z), new Vector2(support.x, support.z)) > 0.08f)
        {
            state.confirmation.Reset();
            LogContactProbe(candidate, $"depthAgreement=false; planeY={support.y:F3}; " +
                $"depthY={(measured ? depth.position.y.ToString("F3") : "missing")}; samples={contactDepth.LastSupportSampleCount}");
            return;
        }
        if (state.planeId != supportId) state.confirmation.Reset();
        state.planeId = supportId;
        // The depth fit, rather than the broad plane estimate, sets final height.
        float height = depth.position.y + surfaceContactOffset;
        LogContactProbe(candidate, $"depthAgreement=true; currentY={candidate.transform.position.y:F3}; measuredY={height:F3}");
        if (state.confirmation.Observe(candidate.transform.position.y, height, Time.unscaledTime))
        {
            state.targetHeight = height;
            state.settling = true;
            state.confirmation.Reset();
            Debug.Log($"[ARGroundContact] {candidate.name}#{candidate.GetInstanceID()}; " +
                $"fromY={candidate.transform.position.y:F3}; measuredY={height:F3}; plane={supportId}; XZ=preserved");
        }
    }

    private static Vector3 GetAuthoredContactLocalPosition(GameObject obj)
    {
        var stabilizer = obj.GetComponent<ARPlacedObjectStabilizer>();
        return stabilizer != null ? stabilizer.AuthoredContactLocalPosition : obj.transform.localPosition;
    }

    private bool HasStableContactBinding(GameObject obj)
    {
        if (objectAnchors.TryGetValue(obj, out ARAnchor anchor) && anchor != null)
            return !anchor.pending && anchor.trackingState == TrackingState.Tracking;
        var instant = obj.GetComponent<ARInstantPlacementAnchor>();
        return instant != null && instant.SupportRoot != null;
    }

    private static void StepContactCorrection(Transform body, float height, float deltaTime)
    {
        Vector3 position = body.position;
        position.y = Mathf.MoveTowards(position.y, Mathf.Min(height, position.y),
            1.2f * Mathf.Clamp(deltaTime, 0f, 0.05f));
        body.position = position;
    }

    private void LogContactProbe(GameObject obj, string detail)
    {
        if (!Debug.isDebugBuild || Time.unscaledTime < nextContactDiagnosticAt) return;
        nextContactDiagnosticAt = Time.unscaledTime + 5f;
        Debug.Log($"[ARGroundContact] Probe {obj.name}#{obj.GetInstanceID()}; {detail}");
    }

    private bool TryGetVerticalContactSupport(Vector3 contact, TrackableId preferred,
        out Vector3 support, out TrackableId id)
    {
        support = default;
        id = TrackableId.invalidId;
        ARPlane preferredPlane = preferred != TrackableId.invalidId ? arPlaneManager.GetPlane(preferred) : null;
        for (int i = 0; i < 16 && preferredPlane != null && preferredPlane.subsumedBy != null; i++)
            preferredPlane = preferredPlane.subsumedBy;
        if (preferredPlane != null)
        {
            if (!TryGetVerticalContactOnPlane(contact, preferredPlane, out support)) return false;
            id = preferredPlane.trackableId;
            return true;
        }
        float bestGap = float.PositiveInfinity;
        foreach (ARPlane plane in arPlaneManager.trackables)
        {
            if (!TryGetVerticalContactOnPlane(contact, plane, out Vector3 point)) continue;
            float gap = Mathf.Abs(contact.y - point.y);
            if (gap >= bestGap) continue;
            bestGap = gap;
            support = point;
            id = plane.trackableId;
        }
        return id != TrackableId.invalidId;
    }

    private bool TryGetVerticalContactOnPlane(Vector3 contact, ARPlane plane, out Vector3 point,
        bool requireHeightCorrection = true)
    {
        point = default;
        if (!IsUsableSurfacePlane(plane)) return false;
        Vector2 center = plane.center;
        Vector3 planePoint = plane.transform.TransformPoint(new Vector3(center.x, 0f, center.y));
        if (!ARSupportPolicy.TryProjectContactVertically(contact, planePoint, plane.transform.up, out point) ||
            (requireHeightCorrection && !ARSupportPolicy.CanCorrectContactHeight(contact.y, point.y + surfaceContactOffset)) ||
            mainCamera.transform.position.y <= point.y + 0.02f) return false;
        Vector3 local = plane.transform.InverseTransformPoint(point);
        return IsPointInSurfaceBoundary(new Vector2(local.x, local.z), plane.boundary);
    }

    private bool HasReusableDragAnchor(GameObject obj)
    {
        if (!objectAnchors.TryGetValue(obj, out ARAnchor anchor) || anchor == null)
            anchor = obj.GetComponentInParent<ARAnchor>();
        return anchor != null && CanReuseDragAnchor(anchor.trackingState, anchor.pending,
            anchor.transform.position, obj.transform.position);
    }

    private bool TryGetNearbySupportAnchor(Vector3 measuredContact, GameObject ignore, out ARAnchor result)
    {
        result = null;
        float nearest = float.PositiveInfinity;
        // Content on one nearby support must move in one native coordinate frame.
        // Independent anchors can refine differently, even with the same plane ID.
        foreach (GameObject placed in placedObjectsHistory)
        {
            if (placed == null || placed == ignore || !placed.activeInHierarchy ||
                IsObjectBeingDragged(placed) || dragAnchorRequests.ContainsKey(placed) ||
                !objectAnchors.TryGetValue(placed, out ARAnchor candidate) || candidate == null ||
                !candidate.isActiveAndEnabled || placed.transform.parent != candidate.transform ||
                (mainCamera != null && candidate.transform.IsChildOf(mainCamera.transform)) ||
                !ARSupportPolicy.CanShareAnchor(candidate.trackingState, candidate.pending, candidate.transform.position,
                    placed.transform.position, measuredContact)) continue;
            float distance = (placed.transform.position - measuredContact).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            result = candidate;
        }
        return result != null;
    }

    private void AdoptSupportAnchor(GameObject obj, ARAnchor target, Pose contact, ARAnchor previous)
    {
        if (!target.gameObject.name.StartsWith("SurfaceAnchor_"))
            target.gameObject.name = "SurfaceAnchor_" + obj.name;
        obj.transform.SetParent(target.transform, true);
        // A shared anchor's origin is another component's contact, not this one.
        obj.transform.SetPositionAndRotation(contact.position, contact.rotation);
        objectAnchors[obj] = target;
        obj.GetComponent<ARPlacedObjectStabilizer>()?.Bind(this, target);
        bool stillUsed = false;
        foreach (var entry in objectAnchors)
            if (entry.Key != null && entry.Key != obj && entry.Value == previous) { stillUsed = true; break; }
        if (!stillUsed && previous != null && previous != target) Destroy(previous.gameObject);
        Debug.Log($"[ARBinding] Rebound {obj.name}#{obj.GetInstanceID()}; anchor={target.trackableId}; " +
            $"world={obj.transform.position:F3}; local={obj.transform.localPosition:F3}");
        wireConnectionController?.UpdateActiveWirePositionsExternal();
    }

    private static bool CanReuseDragAnchor(TrackingState state, bool pending, Vector3 anchorPosition, Vector3 objectPosition)
    {
        // Small same-surface moves can stay children of their genuine native
        // anchor. Recreating it on every release caused pending-anchor rollbacks.
        return state == TrackingState.Tracking && !pending &&
            (objectPosition - anchorPosition).sqrMagnitude <= 1f;
    }

    private static GameObject FindPlacedRoot(Transform hit)
    {
        while (hit != null)
        {
            if (hit.name.StartsWith("Placed_")) return hit.gameObject;
            hit = hit.parent;
        }
        return null;
    }

    private bool TryFindPinchTarget(Vector2 screenPoint, out GameObject target, out int eyeIndex)
    {
        target = null;
        eyeIndex = -1;
        if (mainCamera.TryGetComponent(out PhoneHeadsetAR headset) && headset.Stereo?.IsReady == true)
        {
            if (!PhoneHeadsetStereo.TryGetEyeRay(mainCamera, screenPoint, false, out Ray leftRay) ||
                !PhoneHeadsetStereo.TryGetEyeRay(mainCamera, screenPoint, true, out Ray rightRay)) return false;

            // The hand cursor has the same source position in both eyes. Their
            // virtual silhouettes shift, so require agreement on the nearest component.
            int mask = headset.Stereo.SceneCullingMask;
            GameObject leftTarget = FindClosestPinchHit(leftRay, mask, out float leftDistance);
            if (leftTarget == null) return false;
            GameObject rightTarget = FindClosestPinchHit(rightRay, mask, out float rightDistance);
            if (leftTarget != rightTarget) return false;

            target = leftTarget;
            eyeIndex = rightDistance < leftDistance ? 1 : 0;
        }
        else
        {
            target = FindClosestPinchHit(mainCamera.ScreenPointToRay(screenPoint), mainCamera.cullingMask,
                out _);
        }
        return target != null;
    }

    private GameObject FindClosestPinchHit(Ray ray, int cullingMask, out float closestDistance)
    {
        GameObject target = null;
        closestDistance = 20f;
        foreach (GameObject obj in placedObjectsHistory)
        {
            if (obj != null && obj.activeInHierarchy &&
                geometryCache.TryHitVisibleMesh(obj, ray, cullingMask, ref closestDistance))
                target = obj;
        }
        return target;
    }

    private bool TryFindTapTarget(Vector2 screenPoint, out GameObject target)
    {
        target = null;
        Physics.SyncTransforms();
        RaycastHit[] hits = Physics.RaycastAll(mainCamera.ScreenPointToRay(screenPoint), 20f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            // Wire Mode is rejected before this method. Terminals belong to
            // their component in Move Mode, not to an invisible dead zone.
            target = FindPlacedRoot(hit.collider.transform);
            if (target != null) return true;
        }
        return TryFindNearbyTapTarget(screenPoint, out target);
    }

    private bool TryFindNearbyTapTarget(Vector2 screenPoint, out GameObject target)
    {
        target = null;
        int cullingMask = mainCamera.cullingMask;
        float margin = Mathf.Max(22f, mainCamera.pixelHeight * 0.022f);
        float bestDistance = float.PositiveInfinity;
        float bestDepth = float.PositiveInfinity;
        foreach (GameObject obj in placedObjectsHistory)
        {
            if (obj == null || !obj.activeInHierarchy) continue;
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is LineRenderer ||
                    (cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!hasBounds) continue;
            float distance = ProjectedTapDistance(bounds, screenPoint);
            float depth = mainCamera.WorldToScreenPoint(bounds.center).z;
            if (distance > margin * margin || distance > bestDistance ||
                (Mathf.Approximately(distance, bestDistance) && depth >= bestDepth)) continue;
            bestDistance = distance;
            bestDepth = depth;
            target = obj;
        }
        return target != null;
    }

    private float ProjectedTapDistance(Bounds bounds, Vector2 pointer)
    {
        Vector2 min = Vector2.one * float.PositiveInfinity, max = Vector2.one * float.NegativeInfinity;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            Vector3 screen = mainCamera.WorldToScreenPoint(corner);
            if (screen.z <= mainCamera.nearClipPlane) return float.PositiveInfinity;
            min = Vector2.Min(min, screen);
            max = Vector2.Max(max, screen);
        }
        var nearest = new Vector2(Mathf.Clamp(pointer.x, min.x, max.x), Mathf.Clamp(pointer.y, min.y, max.y));
        return (pointer - nearest).sqrMagnitude;
    }

    private bool TryGetDragSurface(Vector2 screenPoint, out Pose pose, out TrackableId planeId)
    {
        dragSurfaceIsEstimated = false;
        pose = default;
        planeId = TrackableId.invalidId;
        if (mainCamera == null ||
            ARSession.state != ARSessionState.SessionTracking) return false;
        Vector2 offsetPoint = screenPoint + dragPointerOffset;
        bool canUseEstimatedLevel = enableInstantPlacement && !dragHasMeasuredSupport &&
            draggedObject != null && instantPlacedObjects.Contains(draggedObject);
        if (!canUseEstimatedLevel && TryProjectDragOnBoundPlane(offsetPoint, out pose))
        {
            planeId = dragSurfaceTrackableId;
            dragLevelTransition.Begin(GetContactPosition(pose).y);
            return true;
        }
        // A measured surface takes priority over an estimated object's old air height.
        if (canUseEstimatedLevel && arPlaneManager != null &&
            TryGetVerticalContactSupport(lastValidDragPos, TrackableId.invalidId, out Vector3 below, out TrackableId belowId))
        {
            pose = new Pose(below, Quaternion.identity);
            dragLevelTransition.Begin(GetContactPosition(pose).y);
            planeId = belowId;
            return true;
        }
        if (TryGetRealHorizontalSurface(screenPoint, dragSurfaceTrackableId, out Pose rawSupport, out TrackableId rawId))
        {
            if (canUseEstimatedLevel || dragLevelTransition.Accept(GetContactPosition(rawSupport), screenPoint, Time.unscaledTime))
            {
                pose = OffsetMeasuredDragSupport(screenPoint, rawSupport, rawId);
                if (canUseEstimatedLevel) dragLevelTransition.Begin(GetContactPosition(pose).y);
                planeId = rawId;
                return true;
            }
            return false;
        }
        if (dragEye >= 0)
        {
            // Follow the eye that actually grabbed the mesh. Project at the last
            // accepted height, then translate to the physical camera for any ARCore queries.
            if (!PhoneHeadsetStereo.TryGetEyeRay(mainCamera, offsetPoint, dragEye == 1, out Ray eyeRay, clipToLens: false) ||
                !new Plane(Vector3.up, lastValidDragPos).Raycast(eyeRay, out float distance) ||
                distance <= 0f || distance > 20f)
            {
                dragLevelTransition.CancelPending();
                if (canUseEstimatedLevel)
                {
                    dragSurfaceIsEstimated = true;
                    return TryProjectFreeHandDrag(offsetPoint, out pose);
                }
                return false;
            }
            Vector3 contact = eyeRay.GetPoint(distance);
            if (TryGetHeadsetSameLevelSupport(contact, out pose))
            {
                planeId = dragSurfaceTrackableId;
                dragLevelTransition.CancelPending();
                return true;
            }
            offsetPoint = mainCamera.WorldToScreenPoint(contact);
        }
        if (!TryGetMeasuredDragSupport(offsetPoint, out pose, out planeId) &&
            (dragEye >= 0 || measuredDepth.IsConfirmingSurface || dragPointerOffset.sqrMagnitude < 1f ||
             !TryGetMeasuredDragSupport(screenPoint, out pose, out planeId)))
        {
            dragLevelTransition.CancelPending();
            // An estimated placement remains movable even before surface detection.
            // The frozen grab-depth plane permits lowering the hand; measured objects retain bounded surface rules.
            if (canUseEstimatedLevel)
            {
                dragSurfaceIsEstimated = true;
                return TryProjectFreeHandDrag(offsetPoint, out pose);
            }
            return false;
        }
        // Crossing a measured table/floor height is intentional only after a
        // short stable hold. A single ray beyond an edge must not teleport.
        return dragLevelTransition.Accept(GetContactPosition(pose), screenPoint, Time.unscaledTime);
    }

    private bool TryProjectDragOnBoundPlane(Vector2 pointer, out Pose pose)
    {
        pose = default;
        if (arPlaneManager == null || dragSurfaceTrackableId == TrackableId.invalidId) return false;
        ARPlane plane = arPlaneManager.GetPlane(dragSurfaceTrackableId);
        for (int i = 0; i < 16 && plane != null && plane.subsumedBy != null; i++) plane = plane.subsumedBy;
        if (!IsUsableSurfacePlane(plane)) return false;
        Ray ray = mainCamera.ScreenPointToRay(pointer);
        if (dragEye >= 0 && !PhoneHeadsetStereo.TryGetEyeRay(mainCamera, pointer, dragEye == 1, out ray, clipToLens: false)) return false;
        Vector2 center = plane.center;
        Vector3 origin = plane.transform.TransformPoint(new Vector3(center.x, 0f, center.y));
        if (!new Plane(plane.transform.up, origin).Raycast(ray, out float distance) || distance <= 0f || distance > 20f) return false;
        Vector3 contact = ray.GetPoint(distance);
        Vector3 local = plane.transform.InverseTransformPoint(contact);
        if (!IsPointInSurfaceBoundary(new Vector2(local.x, local.z), plane.boundary)) return false;
        pose = new Pose(contact, plane.transform.rotation);
        dragSurfaceTrackableId = plane.trackableId;
        return true;
    }

    private Pose OffsetMeasuredDragSupport(Vector2 pointer, Pose measured, TrackableId id)
    {
        Ray ray = mainCamera.ScreenPointToRay(pointer + dragPointerOffset);
        if (dragEye >= 0 && !PhoneHeadsetStereo.TryGetEyeRay(mainCamera, pointer + dragPointerOffset,
            dragEye == 1, out ray, clipToLens: false)) return measured;
        if (!new Plane(Vector3.up, GetContactPosition(measured)).Raycast(ray, out float distance) || distance <= 0f || distance > 20f)
            return measured;
        Vector3 point = ray.GetPoint(distance);
        ARPlane plane = arPlaneManager != null ? arPlaneManager.GetPlane(id) : null;
        return TryGetVerticalContactOnPlane(point, plane, out Vector3 support, requireHeightCorrection: false)
            ? new Pose(support, measured.rotation) : measured;
    }

    private bool TryProjectFreeHandDrag(Vector2 pointer, out Pose pose)
    {
        pose = default;
        Ray ray = mainCamera.ScreenPointToRay(pointer);
        if (dragEye >= 0 && !PhoneHeadsetStereo.TryGetEyeRay(mainCamera, pointer, dragEye == 1, out ray, clipToLens: false)) return false;
        if (!dragFreePlane.Raycast(ray, out float distance) || distance <= 0f || distance > 5f) return false;
        Vector3 point = ray.GetPoint(distance);
        if ((point - dragStartPos).sqrMagnitude > 1f) return false;
        pose = new Pose(point, dragStartRot);
        return true;
    }

    private bool TryGetHeadsetSameLevelSupport(Vector3 contact, out Pose pose)
    {
        pose = default;
        if (arPlaneManager == null || dragSurfaceTrackableId == TrackableId.invalidId) return false;
        ARPlane plane = arPlaneManager.GetPlane(dragSurfaceTrackableId);
        if (!TryGetVerticalContactOnPlane(contact, plane, out Vector3 measured, requireHeightCorrection: false) ||
            !IsSameDragSupportLevel(contact.y, measured.y + surfaceContactOffset)) return false;

        // Inside the same tracked polygon, keep the accepted height and avoid
        // native depth probes competing with hand tracking on every short move.
        pose = new Pose(measured, Quaternion.identity);
        return true;
    }

    private bool TryGetMeasuredDragSupport(Vector2 screenPoint, out Pose pose, out TrackableId planeId)
    {
        bool planeHit = TryGetRealHorizontalSurface(screenPoint, dragSurfaceTrackableId, out Pose planePose, out TrackableId id);
        return TryChooseMeasuredSupport(screenPoint, planeHit, planePose, id, out pose, out planeId, out _);
    }

    private static bool IsSameDragSupportLevel(float startHeight, float contactHeight)
    {
        return !float.IsNaN(contactHeight) && !float.IsInfinity(contactHeight) &&
            Mathf.Abs(startHeight - contactHeight) <= ARSupportPolicy.LevelTransition.SameLevelTolerance;
    }

    private Vector2 GetCursorScreenPosition()
    {
        if (buttonInteractor == null || !buttonInteractor.IsHandVisible)
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        // One cursor filter is enough: use the same visible point for UI and held objects.
        return buttonInteractor.CurrentScreenPos;
    }

    private Vector2 GetStablePlacementScreenPosition()
    {
        Vector2 rawPosition = GetCursorScreenPosition();
        if (enableInstantPlacement)
        {
            // ButtonInteractor already filters the cursor; use the displayed point.
            smoothedPlacementScreenPosition = rawPosition;
            hasSmoothedPlacementScreenPosition = true;
            return rawPosition;
        }
        if (!hasSmoothedPlacementScreenPosition)
        {
            smoothedPlacementScreenPosition = rawPosition;
            hasSmoothedPlacementScreenPosition = true;
            return rawPosition;
        }

        // Khi người dùng đổi hẳn vùng trỏ thì snap ngay; dao động nhỏ của hand tracking
        // mới được lọc để tránh raycast liên tục chạy ra/vào mép polygon.
        float snapDistance = Mathf.Max(80f, Screen.height * 0.12f);
        if (Vector2.Distance(rawPosition, smoothedPlacementScreenPosition) > snapDistance)
        {
            smoothedPlacementScreenPosition = rawPosition;
        }
        else
        {
            float follow = 1f - Mathf.Exp(-cursorSmoothingSpeed * Time.deltaTime);
            smoothedPlacementScreenPosition = Vector2.Lerp(smoothedPlacementScreenPosition, rawPosition, follow);
        }

        return smoothedPlacementScreenPosition;
    }

    /// <summary>
    /// Raycast trực tiếp lên polygon của mặt phẳng ngang do AR Foundation nhận diện.
    /// Không dùng estimated plane và không dùng một collider vô hạn, vì cả hai trường
    /// hợp đều có thể cho phép thả vật ra ngoài mép bàn rồi treo trong không khí.
    /// </summary>
    private bool TryGetRealHorizontalSurface(
        Vector2 screenPosition,
        TrackableId preferredTrackableId,
        out Pose pose,
        out TrackableId hitTrackableId)
    {
        if (TryGetHorizontalSurface(screenPosition, preferredTrackableId,
            TrackableType.PlaneWithinPolygon, out pose, out hitTrackableId)) return true;

        // Use the same tracked polygon if the provider raycast temporarily misses.
        // Unlike bounds/extended-plane previews, this result is valid for placement.
        return TryProjectPointerToTrackedPlane(screenPosition, preferredTrackableId,
            out pose, out hitTrackableId);
    }

    private bool TryGetPlacementSurface(Vector2 screenPosition, TrackableId preferredPlaneId,
        out Pose pose, out TrackableId planeId)
    {
        if (TryGetRealHorizontalSurface(screenPosition, preferredPlaneId, out pose, out planeId)) return true;
        return TryFindNearbyTrackedSurface(screenPosition, out pose, out planeId);
    }

    private bool TryGetMeasuredPlacementSurface(Vector2 screenPosition, TrackableId preferredPlaneId,
        out Pose pose, out TrackableId planeId, out bool isDepth)
    {
        bool planeHit = TryGetRealHorizontalSurface(screenPosition, preferredPlaneId, out Pose planePose, out TrackableId id);
        if (TryChooseMeasuredSupport(screenPosition, planeHit, planePose, id, out pose, out planeId, out isDepth)) return true;
        // Do not snap an edge upward over a real lower depth patch, including
        // its first still-confirming observation.
        if (measuredDepth.IsConfirmingSurface) return false;
        return !planeHit && TryFindNearbyTrackedSurface(screenPosition, out pose, out planeId);
    }

    private bool TryChooseMeasuredSupport(Vector2 screenPoint, bool planeHit, Pose planePose, TrackableId id,
        out Pose pose, out TrackableId planeId, out bool isDepth)
    {
        pose = planePose;
        planeId = id;
        isDepth = false;
        bool depthHit = measuredDepth.TryGetSurface(screenPoint, out Pose depthPose);
        if (depthHit)
        {
            hasDepthSupportEvidence = true;
            lastDepthSupportEvidence = depthPose;
            lastDepthEvidencePointer = screenPoint;
        }
        if (depthHit && (!planeHit || ARSupportPolicy.PreferMeasuredDepth(planePose, depthPose)))
        {
            pose = depthPose;
            planeId = TrackableId.invalidId;
            isDepth = true;
            return true;
        }
        if (planeHit && measuredDepth.IsConfirmingSurface &&
            ARSupportPolicy.PreferMeasuredDepth(planePose, measuredDepth.ConfirmingSurfacePose)) return false;
        if (planeHit && !depthHit && hasDepthSupportEvidence && mainCamera != null &&
            ARSupportPolicy.PreferMeasuredDepth(planePose, lastDepthSupportEvidence) &&
            Vector2.Distance(screenPoint, lastDepthEvidencePointer) <= 40f)
        {
            // Missing depth is not evidence that the previously disproved table
            // height became correct again. This only REJECTS a conflicting plane
            // near the same measured spot; it never authorizes stale-depth placement.
            Ray ray = mainCamera.ScreenPointToRay(screenPoint);
            var previousLevel = new Plane(Vector3.up, lastDepthSupportEvidence.position);
            if (previousLevel.Raycast(ray, out float distance) && distance > 0f &&
                Vector3.Distance(ray.GetPoint(distance), lastDepthSupportEvidence.position) <= 0.08f)
                return false;
        }
        return planeHit;
    }

    private static bool IsUsableSurfacePlane(ARPlane plane)
    {
        return plane != null && plane.alignment == PlaneAlignment.HorizontalUp &&
            plane.trackingState == TrackingState.Tracking && plane.subsumedBy == null;
    }

    private bool TryFindNearbyTrackedSurface(Vector2 screenPosition, out Pose pose, out TrackableId planeId)
    {
        pose = default;
        planeId = TrackableId.invalidId;
        if (mainCamera == null || arPlaneManager == null || surfaceEdgeSnapDistance <= 0f ||
            ARSession.state != ARSessionState.SessionTracking) return false;
        Ray ray = mainCamera.ScreenPointToRay(screenPosition);
        float nearestDistance = float.PositiveInfinity;
        float screenTolerance = Mathf.Max(12f, Screen.height * 0.015f);
        foreach (ARPlane plane in arPlaneManager.trackables)
        {
            if (!IsUsableSurfacePlane(plane) ||
                Vector3.Dot(mainCamera.transform.position - plane.transform.position, plane.transform.up) <= 0f) continue;
            Plane measuredPlane = new Plane(plane.transform.up, plane.transform.position);
            if (!measuredPlane.Raycast(ray, out float distance) || distance <= mainCamera.nearClipPlane || distance > 20f) continue;
            Vector3 intersection = ray.GetPoint(distance);
            Vector3 local = plane.transform.InverseTransformPoint(intersection);
            if (!TryGetClosestSurfaceBoundaryPoint(new Vector2(local.x, local.z), plane.boundary, out Vector2 closest)) continue;
            Vector3 measuredPoint = plane.transform.TransformPoint(new Vector3(closest.x, 0f, closest.y));
            if (Vector3.Distance(measuredPoint, intersection) > surfaceEdgeSnapDistance) continue;
            Vector3 projected = mainCamera.WorldToScreenPoint(measuredPoint);
            if (projected.z <= mainCamera.nearClipPlane ||
                Vector2.Distance(new Vector2(projected.x, projected.y), screenPosition) > screenTolerance) continue;
            float candidateDistance = Vector3.Distance(mainCamera.transform.position, measuredPoint);
            if (candidateDistance >= nearestDistance) continue;
            nearestDistance = candidateDistance;
            pose = new Pose(measuredPoint, plane.transform.rotation);
            planeId = plane.trackableId;
        }
        return planeId != TrackableId.invalidId;
    }

    private static bool TryGetClosestSurfaceBoundaryPoint(Vector2 point, NativeArray<Vector2> boundary, out Vector2 closest)
    {
        closest = default;
        if (!boundary.IsCreated || boundary.Length < 3) return false;
        if (IsPointInSurfaceBoundary(point, boundary)) { closest = point; return true; }
        float nearest = float.PositiveInfinity;
        for (int i = 0, j = boundary.Length - 1; i < boundary.Length; j = i++)
        {
            Vector2 edge = boundary[i] - boundary[j];
            float t = edge.sqrMagnitude > 0f
                ? Mathf.Clamp01(Vector2.Dot(point - boundary[j], edge) / edge.sqrMagnitude) : 0f;
            Vector2 candidate = boundary[j] + edge * t;
            float sqrDistance = (candidate - point).sqrMagnitude;
            if (sqrDistance >= nearest) continue;
            nearest = sqrDistance;
            closest = candidate;
        }
        return !float.IsPositiveInfinity(nearest);
    }

    private bool TryProjectPointerToTrackedPlane(
        Vector2 screenPosition,
        TrackableId preferredTrackableId,
        out Pose pose,
        out TrackableId hitTrackableId)
    {
        pose = default;
        hitTrackableId = TrackableId.invalidId;
        if (mainCamera == null || arPlaneManager == null ||
            ARSession.state != ARSessionState.SessionTracking) return false;

        Ray ray = mainCamera.ScreenPointToRay(screenPosition);
        bool found = false;
        float nearestDistance = float.PositiveInfinity;
        Pose nearestPose = default;
        TrackableId nearestTrackableId = TrackableId.invalidId;
        Pose preferredPose = default;
        bool foundPreferred = false;

        foreach (ARPlane plane in arPlaneManager.trackables)
        {
            if (!IsUsableSurfacePlane(plane)) continue;

            Vector2 center = plane.center;
            Vector3 worldCenter = plane.transform.TransformPoint(new Vector3(center.x, 0f, center.y));
            Vector3 planeUp = plane.transform.up;
            if (Vector3.Dot(mainCamera.transform.position - worldCenter, planeUp) <= 0f) continue;

            Plane infinitePlane = new Plane(planeUp, worldCenter);
            if (!infinitePlane.Raycast(ray, out float distance) ||
                distance <= mainCamera.nearClipPlane || distance > 20f) continue;

            Vector3 worldPoint = ray.GetPoint(distance);
            Vector3 localPoint = plane.transform.InverseTransformPoint(worldPoint);
            if (!IsPointInSurfaceBoundary(
                new Vector2(localPoint.x, localPoint.z), plane.boundary)) continue;

            Pose candidate = new Pose(worldPoint, plane.transform.rotation);
            if (plane.trackableId == preferredTrackableId && preferredTrackableId != TrackableId.invalidId)
            {
                preferredPose = candidate;
                foundPreferred = true;
            }

            if (distance < nearestDistance)
            {
                found = true;
                nearestDistance = distance;
                nearestPose = candidate;
                nearestTrackableId = plane.trackableId;
            }
        }

        if (!found) return false;
        bool usePreferred = foundPreferred && Mathf.Abs(preferredPose.position.y - nearestPose.position.y) <= 0.03f;
        pose = usePreferred ? preferredPose : nearestPose;
        hitTrackableId = usePreferred ? preferredTrackableId : nearestTrackableId;
        return true;
    }

    private static bool IsPointInSurfaceBoundary(Vector2 point, NativeArray<Vector2> boundary)
    {
        if (!boundary.IsCreated || boundary.Length < 3) return false;
        bool inside = false;
        const float edgeTolerance = 0.001f;
        for (int i = 0, j = boundary.Length - 1; i < boundary.Length; j = i++)
        {
            Vector2 a = boundary[j];
            Vector2 b = boundary[i];
            Vector2 edge = b - a;
            float t = edge.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0f;
            if ((point - (a + edge * t)).sqrMagnitude <= edgeTolerance * edgeTolerance) return true;
            if ((a.y > point.y) != (b.y > point.y) &&
                point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    private bool TryGetHorizontalSurface(
        Vector2 screenPosition,
        TrackableId preferredTrackableId,
        TrackableType trackableType,
        out Pose pose,
        out TrackableId hitTrackableId)
    {
        pose = default;
        hitTrackableId = TrackableId.invalidId;
        if (arRaycastManager == null || arPlaneManager == null ||
            ARSession.state != ARSessionState.SessionTracking) return false;

        surfaceHits.Clear();
        if (!arRaycastManager.Raycast(screenPosition, surfaceHits, trackableType))
        {
            return false;
        }

        bool hasFallback = false;
        Pose fallbackPose = default;
        TrackableId fallbackTrackableId = TrackableId.invalidId;

        for (int i = 0; i < surfaceHits.Count; i++)
        {
            ARRaycastHit hit = surfaceHits[i];
            ARPlane plane = arPlaneManager != null ? arPlaneManager.GetPlane(hit.trackableId) : null;

            if (!IsUsableSurfacePlane(plane)) continue;
            if (Vector3.Dot(mainCamera.transform.position - hit.pose.position, plane.transform.up) <= 0f) continue;

            // Chỉ ưu tiên plane cũ nếu nó gần như đồng phẳng với hit gần nhất.
            // Nhờ vậy các mảnh plane cùng mặt không làm preview rung, nhưng vẫn cho
            // phép chuyển đúng từ sàn lên mặt bàn (hoặc ngược lại).
            if (hit.trackableId == preferredTrackableId &&
                preferredTrackableId != TrackableId.invalidId &&
                (!hasFallback || Mathf.Abs(hit.pose.position.y - fallbackPose.position.y) <= 0.03f))
            {
                pose = hit.pose;
                hitTrackableId = hit.trackableId;
                return true;
            }

            if (!hasFallback)
            {
                hasFallback = true;
                fallbackPose = hit.pose;
                fallbackTrackableId = hit.trackableId;
            }
        }

        if (!hasFallback) return false;

        pose = fallbackPose;
        hitTrackableId = fallbackTrackableId;
        return true;
    }

    private Vector3 GetContactPosition(Pose surfacePose)
    {
        Vector3 surfaceUp = surfacePose.rotation * Vector3.up;
        if (Vector3.Dot(surfaceUp, Vector3.up) < 0f) surfaceUp = -surfaceUp;
        return surfacePose.position + surfaceUp.normalized * surfaceContactOffset;
    }

    private void ShowUngroundedPreview()
    {
        if (previewAnchor == null) return;
        canPlace = false;
        if (mainCamera == null) mainCamera = Camera.main;

        bool hasPointer = HasWorldPlacementPointer();
        bool initialize = !previewAnchor.activeSelf;

        if (mainCamera == null)
        {
            previewAnchor.transform.SetPositionAndRotation(Vector3.forward * ungroundedPreviewDistance, Quaternion.identity);
            previewAnchor.SetActive(true);
            return;
        }

        Matrix4x4 projection = mainCamera.projectionMatrix;
        bool startupFrameChanged = !hasUngroundedPreviewFrame ||
            ARSession.state != ARSessionState.SessionTracking ||
            ungroundedPreviewSessionState != ARSession.state || ungroundedPreviewProjection != projection;
        // Không giữ một world pose tạo trước camera AR: origin và projection có
        // thể chỉ có dữ liệu thật ở frame sau. Sau khởi động, mất tay vẫn giữ pose.
        if (!initialize && !hasPointer && !startupFrameChanged) return;

        Vector2 viewportPoint = hasPointer
            ? new Vector2(buttonInteractor.CurrentScreenPos.x / Mathf.Max(1f, Screen.width),
                buttonInteractor.CurrentScreenPos.y / Mathf.Max(1f, Screen.height))
            : new Vector2(0.5f, 0.55f);
        // Only frame the initial no-hand ghost. Clamping an active pointer
        // here made the model stop while the white cursor kept moving.
        if (!hasPointer)
        {
            float framingMargin = Mathf.Max(0.24f, ungroundedPreviewScreenFraction * 0.5f + 0.04f);
            viewportPoint.x = Mathf.Clamp(viewportPoint.x, framingMargin, 1f - framingMargin);
            viewportPoint.y = Mathf.Clamp(viewportPoint.y, framingMargin, 1f - framingMargin);
        }

        // Chỉ căn khoảng cách tạm theo bounds, tuyệt đối không phóng/thu scale
        // prefab. Dùng projection thật thay vì fieldOfView có thể còn giá trị cũ.
        float distance = GetUngroundedPreviewDistance();
        Quaternion rotation = initialize || startupFrameChanged
            ? GetPlacementRotation() : previewAnchor.transform.rotation;
        Vector3 center = mainCamera.ViewportToWorldPoint(new Vector3(viewportPoint.x, viewportPoint.y, distance));
        Vector3 position = center - rotation * previewBoundsLocalCenter;
        // The cursor is already filtered. A second world-space lerp delays a
        // camera-relative ghost during both finger and camera movement.
        if (hasPointer || initialize || startupFrameChanged)
            previewAnchor.transform.SetPositionAndRotation(position, rotation);
        else
            previewAnchor.transform.position = Vector3.Lerp(previewAnchor.transform.position, position,
                1f - Mathf.Exp(-previewFollowSpeed * Time.unscaledDeltaTime));

        previewAnchor.SetActive(true);
        UpdatePreviewVisual(false);
        hasUngroundedPreviewFrame = true;
        ungroundedPreviewProjection = projection;
        ungroundedPreviewSessionState = ARSession.state;
    }

    private float GetUngroundedPreviewDistance()
    {
        if (mainCamera == null || previewBoundsRadius <= 0.0001f) return ungroundedPreviewDistance;
        Matrix4x4 projection = mainCamera.projectionMatrix;
        float tanHalfFov = 1f / Mathf.Max(Mathf.Max(Mathf.Abs(projection.m00), Mathf.Abs(projection.m11)), 0.001f);
        float targetTangent = Mathf.Max(0.001f, tanHalfFov * ungroundedPreviewScreenFraction);
        float fitDistance = previewBoundsRadius * Mathf.Sqrt(1f + targetTangent * targetTangent) / targetTangent;
        return Mathf.Max(mainCamera.nearClipPlane + previewBoundsRadius + 0.01f, fitDistance);
    }

    private void LogPreviewGeometry(string stage)
    {
        if (mainCamera == null || previewAnchor == null || previewVisual == null) return;
        Vector3 viewportCenter = mainCamera.WorldToViewportPoint(
            previewAnchor.transform.TransformPoint(previewBoundsLocalCenter));
        Debug.Log($"[ARPreview] {stage}; prefab={currentPrefabToPlace?.name}; " +
            $"scale={previewVisual.transform.localScale}; radius={previewBoundsRadius:F4}; " +
            $"fitDistance={GetUngroundedPreviewDistance():F3}; viewportCenter={viewportCenter}; " +
            $"projection=({mainCamera.projectionMatrix.m00:F3},{mainCamera.projectionMatrix.m11:F3}); " +
            $"screen={Screen.width}x{Screen.height}");
    }

    private Quaternion GetPlacementRotation()
    {
        Vector3 forward = mainCamera != null
            ? Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up)
            : Vector3.forward;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private bool TryGetInstantPlacementPose(Vector2 screenPoint, out Pose pose)
    {
        pose = default;
        if (mainCamera == null || ARSession.state != ARSessionState.SessionTracking) return false;

        // Keep neighbouring estimated components on the same authored level.
        // This is an interaction aid, never evidence of a measured table/floor.
        for (int i = placedObjectsHistory.Count - 1; i >= 0; i--)
        {
            GameObject placed = placedObjectsHistory[i];
            if (placed != null && placed.activeInHierarchy && instantPlacedObjects.Contains(placed) &&
                TryProjectEstimatedLevel(screenPoint, placed.transform.position, out pose))
                return true;
        }
        float distance = GetUngroundedPreviewDistance();
        Vector3 point = mainCamera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, distance));
        pose = new Pose(point, previewAnchor != null && previewAnchor.activeSelf
            ? previewAnchor.transform.rotation : GetPlacementRotation());
        return true;
    }

    private bool TryProjectEstimatedLevel(Vector2 screenPoint, Vector3 origin, out Pose pose)
    {
        pose = default;
        if (mainCamera == null) return false;
        Ray ray = mainCamera.ScreenPointToRay(screenPoint);
        if (!new Plane(Vector3.up, origin).Raycast(ray, out float distance) || distance <= 0f || distance > 5f)
            return false;
        Vector3 point = ray.GetPoint(distance);
        // Avoid a near-parallel pointer sending an estimated component far away.
        if ((point - origin).sqrMagnitude > 1f) return false;
        pose = new Pose(point, GetPlacementRotation());
        return true;
    }

    private bool PlaceInstantObject(Pose pose, Vector2 screenPoint, TrackableId planeId, bool measuredSurface)
    {
        if (!enableInstantPlacement || placementPending || previewAnchor == null || mainCamera == null ||
            ARSession.state != ARSessionState.SessionTracking || cooldownTimer > 0f) return false;
        if (CheckOverlap(pose.position, null, out _))
        {
            UpdatePreviewVisual(true);
            canPlace = false;
            ClearPlacementRequest();
            SetPlacementFeedback(ButtonInteractor.PlacementFeedback.Blocked);
            return false;
        }
        // Nearby measured components should share one tracked coordinate frame,
        // including the immediate-placement path used by AR and phone VR.
        if (measuredSurface && TryGetNearbySupportAnchor(pose.position, null, out ARAnchor sharedAnchor))
        {
            placementRequestPlaneId = planeId;
            previewAnchor.SetActive(true);
            CompletePlacement(pose.position, pose.rotation, sharedAnchor.transform, sharedAnchor);
            return true;
        }
        GameObject placed = previewAnchor;
        Transform support = new GameObject("InstantSupport_" + currentPrefabToPlace.name).transform;
        if (arRaycastManager != null && !arRaycastManager.transform.IsChildOf(mainCamera.transform))
            support.SetParent(arRaycastManager.transform, true);
        support.SetPositionAndRotation(pose.position, pose.rotation);
        previewAnchor.transform.SetPositionAndRotation(pose.position, pose.rotation);
        previewAnchor.SetActive(true);
        placementRequestPlaneId = planeId;
        if (!measuredSurface) instantPlacedObjects.Add(placed);
        float estimatedDistance = Vector3.Distance(mainCamera.transform.position, pose.position);
        CompletePlacement(pose.position, pose.rotation, support, null);
        if (placed != null)
            placed.AddComponent<ARInstantPlacementAnchor>().Initialize(this, arRaycastManager,
                arAnchorManager, mainCamera, screenPoint, estimatedDistance, support, measuredSurface);
        return true;
    }

    internal void AdoptInstantPlacementAnchor(GameObject obj, ARAnchor anchor, bool measuredSurface)
    {
        if (obj == null || anchor == null) return;
        objectAnchors[obj] = anchor;
        anchor.gameObject.name = "SurfaceAnchor_" + obj.name;
        var stabilizer = obj.GetComponent<ARPlacedObjectStabilizer>();
        if (stabilizer == null) stabilizer = obj.AddComponent<ARPlacedObjectStabilizer>();
        stabilizer.Bind(this, anchor);
        if (measuredSurface) instantPlacedObjects.Remove(obj);
        TrackableId supportId = placedContacts.TryGetValue(obj, out PlacedContact contact)
            ? contact.planeId : TrackableId.invalidId;
        RememberPlacedContact(obj, supportId);
        wireConnectionController?.UpdateActiveWirePositionsExternal();
    }

    internal bool TryRefineInstantPlacement(GameObject obj, Transform support, ARPlane measuredPlane)
    {
        if (obj == null || support == null || !obj.activeInHierarchy ||
            IsObjectBeingDragged(obj) ||
            (CircuitSimulationManager.Instance != null && CircuitSimulationManager.Instance.isSimulationMode)) return false;
        Vector3 position = obj.transform.position;
        if (mainCamera == null ||
            !TryGetVerticalContactOnPlane(position, measuredPlane, out Vector3 point, requireHeightCorrection: false))
            return false;
        float height = point.y + surfaceContactOffset;
        float change = height - position.y;
        if (change >= 0f) return change <= 0.012f;
        if (change < -0.002f && !ARSupportPolicy.CanCorrectContactHeight(position.y, height)) return false;
        Vector3 candidate = new Vector3(position.x, height, position.z);
        if (CheckOverlapForObject(obj, candidate, obj.transform.rotation, obj, out _, 0f)) return false;
        support.position += candidate - position;
        wireConnectionController?.UpdateActiveWirePositionsExternal();
        return true;
    }

    private async void TryPlaceObject(Pose pose, TrackableId planeId)
    {
        if (placementPending || previewAnchor == null || !previewAnchor.activeSelf) return;
        bool isDepth = placementRequestHasSurface && placementRequestIsDepth;
        if (!CanCreateMeasuredSurfaceAnchor(planeId, isDepth))
        {
            SetPlacementStatus("Chưa bám chắc mặt thật — hãy thử lại.");
            return;
        }

        if (TryGetNearbySupportAnchor(pose.position, null, out ARAnchor sharedAnchor))
        {
            // The measured contact remains authoritative. Do not snap a new
            // component to the shared anchor origin or to a neighbouring object.
            PlaceObject(pose.position, pose.rotation, sharedAnchor);
            return;
        }

        placementPending = true;
        placementRetryEligible = IsWorldPinchHeld();
        int generation = placementGeneration;
        GameObject requestedPreview = previewAnchor;
        ARAnchor nativeAnchor = null;
        SetPlacementStatus("Đang cố định linh kiện lên mặt thật…");
        try
        {
            // Use the plane/depth only to measure the contact. A plane-attached
            // anchor inherits later plane refits/merges, which lifted the whole
            // placed group on device. A session anchor tracks this measured world
            // point without remaining welded to that changing plane estimate.
            var result = await arAnchorManager.TryAddAnchorAsync(pose);
            if (result.status.IsSuccess()) nativeAnchor = result.value;

            if (this == null || generation != placementGeneration || requestedPreview != previewAnchor)
            {
                if (nativeAnchor != null) Destroy(nativeAnchor.gameObject);
                return;
            }
            if (ShouldCancelPendingPlacement())
            {
                CancelPendingPlacementAttempt();
                if (nativeAnchor != null) Destroy(nativeAnchor.gameObject);
                return;
            }

            // A successful request can still be pending/Limited until a later
            // subsystem update. Keep the requested world pose while it settles.
            float anchorDeadline = Time.realtimeSinceStartup + 2f;
            while (nativeAnchor != null &&
                   (nativeAnchor.pending || nativeAnchor.trackingState != TrackingState.Tracking) &&
                   Time.realtimeSinceStartup < anchorDeadline)
            {
                await Awaitable.NextFrameAsync();
                if (this == null || !isActiveAndEnabled || generation != placementGeneration ||
                    requestedPreview != previewAnchor)
                {
                    if (nativeAnchor != null) Destroy(nativeAnchor.gameObject);
                    return;
                }
                if (ShouldCancelPendingPlacement())
                {
                    CancelPendingPlacementAttempt();
                    if (nativeAnchor != null) Destroy(nativeAnchor.gameObject);
                    return;
                }
            }

            if (ShouldCancelPendingPlacement())
            {
                CancelPendingPlacementAttempt();
                if (nativeAnchor != null) Destroy(nativeAnchor.gameObject);
                return;
            }
            if (nativeAnchor == null || nativeAnchor.trackingState != TrackingState.Tracking ||
                ARSession.state != ARSessionState.SessionTracking)
            {
                if (nativeAnchor != null) Destroy(nativeAnchor.gameObject);
                SetPlacementStatus("Chưa cố định được vị trí — giữ máy ổn định rồi thử lại.");
                return;
            }

            PlaceObject(pose.position, pose.rotation, nativeAnchor);
        }
        catch (System.Exception ex)
        {
            if (nativeAnchor != null) Destroy(nativeAnchor.gameObject);
            Debug.LogWarning("[TapToPlace] Anchor creation failed: " + ex.Message);
            if (this != null && generation == placementGeneration)
                SetPlacementStatus("Chưa cố định được vị trí — hãy thử lại.");
        }
        finally
        {
            if (this != null && generation == placementGeneration)
            {
                placementPending = false;
                RetryPlacementIfStillHeld(generation, requestedPreview);
            }
        }
    }

    private bool IsWorldPinchHeld()
    {
        return HasWorldPlacementPointer() && PlacementInput.isPinching;
    }

    private bool ShouldCancelPendingPlacement()
    {
        return ARSession.state != ARSessionState.SessionTracking ||
            (PlacementInput != null && PlacementInput.JustPinched && PlacementInput.PinchConsumedByUI) ||
            (placementRequestHasSurface && !TryResolvePlacementRequestSurface(out _, out _));
    }

    private void CancelPendingPlacementAttempt()
    {
        // Callback native đến muộn sẽ tự hủy anchor bằng generation guard; không
        // xóa preview hoặc làm sống lại lệnh đã hủy sau khi session hồi phục.
        placementGeneration++;
        placementPending = false;
        placementRetryEligible = false;
        ClearPlacementRequest();
        surfaceTransitionActive = false;
        canPlace = false;
        SetPlacementFeedback(ButtonInteractor.PlacementFeedback.SearchingSurface);
    }

    private void ObservePendingPlacementGesture()
    {
        // Một lần nhả/mất tay/UI trong lúc chờ native anchor kết thúc quyền retry
        // của cử chỉ cũ, kể cả người dùng đã pinch lại trước callback thất bại.
        if (!IsWorldPinchHeld() || PlacementInput.JustPinched) placementRetryEligible = false;
    }

    private void RetryPlacementIfStillHeld(int generation, GameObject requestedPreview)
    {
        if (!placementRetryEligible || !isActiveAndEnabled || generation != placementGeneration ||
            previewAnchor == null || requestedPreview != previewAnchor || currentPrefabToPlace == null ||
            !IsWorldPinchHeld()) return;

        // Native anchor cũng có thể chưa ổn định dù subsystem đã running. Chỉ
        // thử lại cho chính pinch còn đang giữ, tối đa 4 lần/giây, không đặt giả.
        if (!TryResolvePlacementRequestSurface(out _, out _))
        {
            ClearPlacementRequest();
            placementRequestAwaitingSurface = true;
            placementRequestScreenPosition = PlacementInput.CurrentScreenPos;
        }
        nextPlacementAttemptAt = Time.unscaledTime + 0.25f;
        placementRequestExpiresAt = nextPlacementAttemptAt + previewSurfaceTransitionDuration + 0.7f;
        placementRetryEligible = false;
    }

    private bool CanCreateSurfaceAnchor(TrackableId planeId)
    {
        ARPlane plane = arPlaneManager != null ? arPlaneManager.GetPlane(planeId) : null;
        return IsUsableSurfacePlane(plane) &&
            ARSession.state == ARSessionState.SessionTracking && arAnchorManager != null &&
            arAnchorManager.isActiveAndEnabled && arAnchorManager.subsystem != null && arAnchorManager.subsystem.running;
    }

    private bool CanCreateMeasuredSurfaceAnchor(TrackableId planeId, bool isDepth)
    {
        if (!isDepth) return CanCreateSurfaceAnchor(planeId);
        return planeId == TrackableId.invalidId && measuredDepth.IsReady &&
            ARSession.state == ARSessionState.SessionTracking && arAnchorManager != null &&
            arAnchorManager.isActiveAndEnabled && arAnchorManager.subsystem != null && arAnchorManager.subsystem.running;
    }

    private bool TryFindVisibleSurface(out Pose pose, out TrackableId planeId)
    {
        pose = default;
        planeId = TrackableId.invalidId;
        if (ARSession.state != ARSessionState.SessionTracking) return false;
        // Vùng trung tâm: ưu tiên điểm ngay trước người dùng, không phải phần UI đáy màn hình.
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                float u = x == 0 ? 0.5f : (x == 1 ? 0.3f : 0.7f);
                float v = y == 0 ? 0.5f : (y == 1 ? 0.35f : 0.65f);
                if (TryGetRealHorizontalSurface(new Vector2(Screen.width * u, Screen.height * v),
                    TrackableId.invalidId, out pose, out planeId)) return true;
            }
        }
        if (arPlaneManager == null) return false;
        foreach (var plane in arPlaneManager.trackables)
        {
            if (plane.trackingState != TrackingState.Tracking || plane.alignment != PlaneAlignment.HorizontalUp) continue;
            Vector2 localCenter = plane.center;
            Vector3 worldCenter = plane.transform.TransformPoint(new Vector3(localCenter.x, 0f, localCenter.y));
            if (!IsWorldPointVisible(worldCenter)) continue;
            Vector3 screen = mainCamera.WorldToScreenPoint(worldCenter);
            if (TryGetRealHorizontalSurface(new Vector2(screen.x, screen.y), plane.trackableId, out pose, out planeId))
                return true;
        }

        // Intersect the tracked polygon directly when the native raycast misses.
        return TryProjectPointerToTrackedPlane(
            new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
            TrackableId.invalidId, out pose, out planeId);
    }

    private void SetPlacementStatus(string message)
    {
        // Technical diagnostics stay in the log. The reused scan pill below
        // presents concise readiness/gesture states, not a second banner.
        if (placementStatusRoot != null) placementStatusRoot.SetActive(false);
        Debug.Log("[TapToPlace] " + message);
    }

    public void BeginSurfaceGuidance()
    {
        surfaceGuidanceEnabled = true;
        guidanceSurfaceReadySince = float.NegativeInfinity;
        nextSurfaceGuidanceAt = 0f;
        surfaceGuidanceHUD = ARScanFloatingHUD.EnsureInstance();
        RefreshSurfaceGuidance();
    }

    public void EndSurfaceGuidance()
    {
        surfaceGuidanceEnabled = false;
        guidanceSurfaceReadySince = float.NegativeInfinity;
        if (surfaceGuidanceHUD != null) surfaceGuidanceHUD.HideImmediate();
    }

    private void RefreshSurfaceGuidance()
    {
        if (!surfaceGuidanceEnabled) return;
        // The wire mode badge owns the top of the screen until the user exits.
        if (wireConnectionController != null && wireConnectionController.IsWireMode)
        {
            if (surfaceGuidanceHUD != null && surfaceGuidanceHUD.gameObject.activeSelf)
                surfaceGuidanceHUD.HideImmediate();
            nextSurfaceGuidanceAt = 0f;
            return;
        }
        if (Time.unscaledTime < nextSurfaceGuidanceAt) return;
        nextSurfaceGuidanceAt = Time.unscaledTime + 0.2f;
        if (surfaceGuidanceHUD == null) surfaceGuidanceHUD = ARScanFloatingHUD.EnsureInstance();
        LogAnchorStateIfDue();
        if (CircuitSimulationManager.Instance != null && CircuitSimulationManager.Instance.isSimulationMode)
        {
            surfaceGuidanceHUD.HideImmediate();
            return;
        }
        if (ARSession.state != ARSessionState.SessionTracking)
        {
            guidanceSurfaceReadySince = float.NegativeInfinity;
            if (placedObjectsHistory.Count > 0)
                surfaceGuidanceHUD.ShowWarning("Đang tìm lại vị trí AR", "Hướng camera về vùng đã đặt, lia chậm và giữ máy ổn định");
            else if (ARSession.notTrackingReason == NotTrackingReason.InsufficientLight)
                surfaceGuidanceHUD.ShowWarning("Chưa đủ ánh sáng", "Hướng camera vào mặt bàn / sàn sáng và có chi tiết");
            else if (enableInstantPlacement)
                surfaceGuidanceHUD.ShowScanning("Camera AR đang khởi động", "Giữ máy ổn định; có thể chọn linh kiện ngay");
            else
                surfaceGuidanceHUD.ShowScanning("Đang nhận diện không gian", "Lia chậm trái–phải quanh bàn / sàn, chờ thanh chuyển xanh");
            return;
        }
        if (placementPending)
        {
            surfaceGuidanceHUD.ShowScanning("Đang neo linh kiện", "Giữ máy ổn định; vật chỉ cố định khi neo AR hoàn tất");
            return;
        }
        if (isDragging)
        {
            if (dragLevelTransition.IsPending)
                surfaceGuidanceHUD.ShowScanning("Đang chuyển sang mặt đỡ mới", "Giữ con trỏ trên sàn / bàn mới một chút rồi mở tay để thả");
            else
                surfaceGuidanceHUD.ShowDetected("Đang kéo linh kiện", "Mở tay để thả; chỉ di chuyển trên mặt đỡ đã nhận diện");
            return;
        }
        if (dragAnchorRequests.Count > 0)
        {
            surfaceGuidanceHUD.ShowScanning("Đang neo lên mặt đỡ mới", "Giữ máy ổn định; vật giữ nguyên điểm sàn / bàn vừa đo được");
            return;
        }
        if (currentPrefabToPlace != null)
        {
            if (lastPlacementFeedback == ButtonInteractor.PlacementFeedback.Blocked)
                surfaceGuidanceHUD.ShowWarning("Vị trí đang chồng linh kiện", "Di chuyển con trỏ sang chỗ trống trên mặt bàn / sàn");
            else if (canPlace && enableInstantPlacement)
                surfaceGuidanceHUD.ShowDetected("Chạm hoặc pinch để đặt", hasPreviewSurfacePose
                    ? "Vật sẽ đặt tại mặt bàn / sàn dưới con trỏ"
                    : "Vị trí ước lượng; có thể kéo để chỉnh sau khi đặt");
            else if (canPlace)
                surfaceGuidanceHUD.ShowDetected("Có thể đặt tại đây", "Pinch để đặt; chờ báo đã neo rồi mở tay");
            else if (enableInstantPlacement)
                surfaceGuidanceHUD.ShowDetected("Chạm vào vị trí muốn đặt", "Hoặc đưa tay vào khung hình và pinch để đặt");
            else if (buttonInteractor == null || !buttonInteractor.IsHandVisible)
                surfaceGuidanceHUD.ShowScanning("Linh kiện chưa được đặt", "Đưa tay vào khung hình, trỏ vào mặt đỡ rồi pinch");
            else
                surfaceGuidanceHUD.ShowScanning("Đang tìm điểm đặt dưới con trỏ", "Trỏ vào vùng bàn / sàn đã quét; vật hiện tại là xem trước");
            return;
        }
        if (Time.unscaledTime < placementConfirmedUntil)
        {
            surfaceGuidanceHUD.ShowDetected(enableInstantPlacement ? "Đã đặt linh kiện" : "Đã neo linh kiện",
                "Pinch giữ vào vật để kéo; mở tay để thả");
            return;
        }

        if (enableInstantPlacement)
        {
            surfaceGuidanceHUD.ShowDetected("Sẵn sàng đặt linh kiện", "Mở kho dụng cụ, chọn vật rồi chạm hoặc pinch để đặt");
            return;
        }

        bool hasVisibleSupport = HasGuidanceSupport();
        if (!hasVisibleSupport) guidanceSurfaceReadySince = float.NegativeInfinity;
        else if (float.IsNegativeInfinity(guidanceSurfaceReadySince)) guidanceSurfaceReadySince = Time.unscaledTime;
        if (hasVisibleSupport && Time.unscaledTime - guidanceSurfaceReadySince >= 0.5f)
            surfaceGuidanceHUD.ShowDetected("Đã sẵn sàng đặt linh kiện", "Mở kho dụng cụ để lấy vật, trỏ vào mặt đỡ rồi pinch");
        else
            surfaceGuidanceHUD.ShowScanning("Đang tìm mặt bàn / sàn", "Lia chậm quanh bề mặt, chờ thanh xanh rồi lấy linh kiện");
    }

    private bool HasGuidanceSupport()
    {
        if (mainCamera == null || arPlaneManager == null || arAnchorManager == null ||
            !arAnchorManager.isActiveAndEnabled || arAnchorManager.subsystem == null ||
            !arAnchorManager.subsystem.running) return false;
        // Managed polygon intersections only: no extra native depth/camera work
        // just for displaying the status pill before a component is selected.
        foreach (Vector2 viewport in GuidanceProbeViewports)
            if (TryProjectPointerToTrackedPlane(new Vector2(viewport.x * Screen.width, viewport.y * Screen.height),
                TrackableId.invalidId, out _, out _)) return true;
        return false;
    }

    private void LogAnchorStateIfDue()
    {
        if (!Application.isPlaying || !Debug.isDebugBuild || mainCamera == null ||
            Time.unscaledTime < nextAnchorDiagnosticAt) return;
        nextAnchorDiagnosticAt = Time.unscaledTime + 2f;
        for (int i = placedObjectsHistory.Count - 1; i >= 0; i--)
        {
            GameObject obj = placedObjectsHistory[i];
            if (obj == null || !obj.activeInHierarchy || !objectAnchors.TryGetValue(obj, out ARAnchor anchor) || anchor == null) continue;
            bool same = anchorDiagnostics.TryGetValue(obj, out AnchorDiagnostic old);
            Vector3 contactScreen = mainCamera.WorldToScreenPoint(obj.transform.position);
            Pose diagnosticSupport = default;
            bool hasMeasuredSupport = contactScreen.z > mainCamera.nearClipPlane &&
                TryProjectPointerToTrackedPlane(new Vector2(contactScreen.x, contactScreen.y),
                    TrackableId.invalidId, out diagnosticSupport, out _);
            // Diagnostics only: never reproject/move placed objects from this ray.
            string supportGap = hasMeasuredSupport
                ? (obj.transform.position.y - diagnosticSupport.position.y).ToString("F3") : "unseen";
            Debug.Log($"[ARObjectState] {obj.name}#{obj.GetInstanceID()}; anchor={anchor.trackableId}; " +
                $"tracking={anchor.trackingState}; pending={anchor.pending}; world={obj.transform.position:F3}; " +
                $"supportGapM={supportGap}; cameraLocal={mainCamera.transform.localPosition:F3}; " +
                $"dragging={isDragging && draggedObject == obj}; pinch={buttonInteractor != null && buttonInteractor.isPinching}; " +
                $"cameraParent={anchor.transform.IsChildOf(mainCamera.transform)}; " +
                $"worldDeltaMm={(same ? Vector3.Distance(obj.transform.position, old.world) * 1000f : 0f):F1}; " +
                $"localDeltaMm={(same ? Vector3.Distance(obj.transform.localPosition, old.local) * 1000f : 0f):F1}; " +
                $"anchorDeltaMm={(same ? Vector3.Distance(anchor.transform.position, old.anchor) * 1000f : 0f):F1}; " +
                $"cameraDeltaMm={(same ? Vector3.Distance(mainCamera.transform.position, old.camera) * 1000f : 0f):F1}");
            anchorDiagnostics[obj] = new AnchorDiagnostic { world = obj.transform.position, local = obj.transform.localPosition,
                anchor = anchor.transform.position, camera = mainCamera.transform.position };
        }
    }

    private void OnDestroy()
    {
        renderPolicy.Restore();
        geometryCache.Clear();
        instantPlacedObjects.Clear();
        placementGeneration++;
        if (placementStatusRoot != null)
        {
            if (Application.isPlaying) Destroy(placementStatusRoot);
            else DestroyImmediate(placementStatusRoot);
        }
    }

    private bool IsWorldPointVisible(Vector3 worldPosition)
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return false;

        Vector3 viewport = mainCamera.WorldToViewportPoint(worldPosition);
        return viewport.z > mainCamera.nearClipPlane &&
               viewport.x >= 0.04f && viewport.x <= 0.96f &&
               viewport.y >= 0.04f && viewport.y <= 0.96f;
    }

    public bool StartHandPreview(string componentName, ButtonInteractor input)
    {
        if (input == null || !input.IsHandVisible || CircuitSimulationManager.Instance?.isSimulationMode == true) return false;
        bool started = false;
        void StartForOwner()
        {
            if (isDragging) ReleaseDraggedObject();
            StartPreview(componentName, buttonInteractor);
            previewNeedsRelease = true;
            requireMeasuredForPreview = false;
            started = previewAnchor != null;
        }
        if (input == secondInput) RunWithSecondHand(StartForOwner);
        else if (input == buttonInteractor) StartForOwner();
        return started;
    }

    public void StartPreview(string componentName)
    {
        StartPreview(componentName, buttonInteractor);
    }

    public void StartPreview(string componentName, ButtonInteractor input)
    {
        ClearActivePreview();
        previewInput = input;

        GameObject prefab = GetPrefabByName(componentName);
        if (prefab == null)
        {
            Debug.LogWarning("<color=orange>[TapToPlace]</color> Không tìm thấy prefab phù hợp cho: " + componentName);
            return;
        }

        currentPrefabToPlace = prefab;
        loggedMeasuredPreviewGeometry = false;
        previewIsInvalid = null;
        selectedDisplayName = componentName;
        previewAnchor = new GameObject("PreviewAnchor_" + prefab.name);
        previewVisual = Instantiate(prefab);
        previewVisual.transform.SetParent(previewAnchor.transform, false);
        // A prefab's authored scene position is not a placement offset.
        previewVisual.transform.localPosition = Vector3.zero;
        previewVisual.transform.localScale = Vector3.one * scaleMultiplier;
        NormalizeComponentBody(previewVisual);

        // VÔ HIỆU HOÁ TOÀN BỘ COLLIDER KHI PREVIEW ĐỂ KHÔNG TỰ CHẶN RAYCAST (KHẮC PHỤC TRIỆT ĐỂ LỖI NHÁP NHÁY)
        Collider[] colliders = previewVisual.GetComponentsInChildren<Collider>();
        foreach (var c in colliders)
        {
            c.enabled = false;
        }

        AlignVisualBaseToAnchor(previewAnchor, previewVisual);
        Bounds previewBounds = default;
        bool hasPreviewBounds = false;
        foreach (Renderer renderer in previewVisual.GetComponentsInChildren<Renderer>())
        {
            if (renderer is LineRenderer) continue;
            if (!hasPreviewBounds) { previewBounds = renderer.bounds; hasPreviewBounds = true; }
            else previewBounds.Encapsulate(renderer.bounds);
        }
        previewBoundsRadius = hasPreviewBounds ? previewBounds.extents.magnitude : 0f;
        previewBoundsLocalCenter = hasPreviewBounds
            ? previewAnchor.transform.InverseTransformPoint(previewBounds.center) : Vector3.zero;
        SetPreviewTransparent(previewVisual, previewAlpha);

        lastPreviewTrackableId = TrackableId.invalidId;
        hasSmoothedPlacementScreenPosition = false;
        hasPreviewSurfacePose = false;

        // Nếu trong phiên AR đã có một mặt thật hợp lệ và điểm đó vẫn nằm trong
        // khung hình, hiển thị linh kiện mới ngay tại đó. Điều này loại bỏ trường hợp
        // bấm lấy vật nhưng preview không xuất hiện vì tay đang che nút UI.
        ARPlane knownPlane = arPlaneManager != null ? arPlaneManager.GetPlane(lastKnownSurfaceTrackableId) : null;
        if (hasLastKnownPlacementPose && knownPlane != null && knownPlane.trackingState == TrackingState.Tracking &&
            ARSession.state == ARSessionState.SessionTracking && IsWorldPointVisible(lastKnownPlacementPose.position))
        {
            previewAnchor.transform.SetPositionAndRotation(
                lastKnownPlacementPose.position,
                lastKnownPlacementPose.rotation);
            previewAnchor.SetActive(true);
            hasPreviewSurfacePose = true;
            lastPreviewTrackableId = lastKnownSurfaceTrackableId;
        }
        else if (ARSession.state == ARSessionState.SessionTracking &&
                 TryFindVisibleSurface(out Pose initialSurfacePose, out TrackableId initialTrackableId))
        {
            previewAnchor.transform.SetPositionAndRotation(
                GetContactPosition(initialSurfacePose), GetPlacementRotation());
            previewAnchor.SetActive(true);
            hasPreviewSurfacePose = true;
            lastPreviewTrackableId = initialTrackableId;
        }
        else
        {
            previewAnchor.SetActive(false);
            ShowUngroundedPreview();
        }

        canPlace = false;
        LogPreviewGeometry(hasPreviewSurfacePose ? "selection-plane" : "selection-unmeasured");
        SetPlacementFeedback(ButtonInteractor.PlacementFeedback.SearchingSurface);
        worldPinchArmed = false;
        cooldownTimer = 0.4f;
    }

    public void ClearPreview()
    {
        ClearActivePreview();
        if (!processingSecondHand && secondInput != null && secondDrag.previewAnchor != null)
            RunWithSecondHand(ClearActivePreview);
    }

    private void ClearActivePreview()
    {
        previewNeedsRelease = requireMeasuredForPreview = false;
        if (previewInput != null) previewInput.SetPlacementFeedback(ButtonInteractor.PlacementFeedback.None);
        previewInput = null;
        placementGeneration++;
        placementPending = false;
        ClearPlacementRequest();
        placementTapInput.Reset();
        placementRetryEligible = false;
        nextPlacementAttemptAt = 0f;
        surfaceTransitionActive = false;
        surfaceTransitionTrackableId = TrackableId.invalidId;
        SetPlacementFeedback(ButtonInteractor.PlacementFeedback.None);
        if (placementStatusRoot != null) placementStatusRoot.SetActive(false);
        if (previewAnchor != null)
        {
            geometryCache.Remove(previewAnchor);
            if (Application.isPlaying) Destroy(previewAnchor);
            else DestroyImmediate(previewAnchor);
            previewAnchor = null;
            previewVisual = null;
            currentPrefabToPlace = null;
        }
        lastPreviewTrackableId = TrackableId.invalidId;
        hasSmoothedPlacementScreenPosition = false;
        hasPreviewSurfacePose = false;
        previewSurfaceIsDepth = false;
        if (!isDragging) measuredDepth.Reset();
        hasDepthSupportEvidence = false;
        hasUngroundedPreviewFrame = false;
        canPlace = false;
        if (menuHUD != null) menuHUD.ClearSelection();
    }

    public void CancelPlacement()
    {
        if (isDragging) ReleaseDraggedObject();
        ProcessSecondHand(HandOperation.Release);
        bool hadPreview = HasAnyPreview;
        ClearPreview();
        canPlace = false;
        isDragging = false;
        draggedObject = null;
        Debug.Log("<color=yellow>[TapToPlace]</color> Đã hủy chế độ đặt linh kiện (Placement Cancelled).");
        if (hadPreview) OnPlacementFinished?.Invoke();
    }

    private void NormalizeComponentBody(GameObject visual)
    {
        Bounds bounds = default;
        bool found = false;
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || renderer is LineRenderer || IsTerminalVisual(renderer.transform, visual.transform)) continue;
            if (found) bounds.Encapsulate(renderer.bounds);
            else { bounds = renderer.bounds; found = true; }
        }
        float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (found && longest > 0.00001f && componentBodySize > 0f)
            visual.transform.localScale *= componentBodySize / longest;
    }

    private void AlignVisualBaseToAnchor(GameObject anchor, GameObject visual)
    {
        if (anchor == null || visual == null) return;
        // Interaction colliders are intentionally generous, and are not the
        // rendered base. Using their lower face can visibly suspend the model.
        // This runs only when selecting/committing a model, never per frame.
        float bottomY = float.PositiveInfinity;
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || renderer is LineRenderer ||
                IsTerminalVisual(renderer.transform, visual.transform)) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null)
            {
                if (mesh.isReadable)
                {
                    foreach (Vector3 vertex in mesh.vertices)
                        bottomY = Mathf.Min(bottomY, renderer.transform.TransformPoint(vertex).y);
                }
                else
                {
                    // Use local bounds for meshes imported without CPU access.
                    // For upright body meshes and yaw rotation this is the lower face.
                    bottomY = Mathf.Min(bottomY, GetLowestTransformedMeshBound(mesh.bounds, renderer.transform));
                }
            }
            else
            {
                // Skinned/custom renderers without MeshFilter retain a safe
                // geometry-based fallback; never fall back to a grab collider.
                bottomY = Mathf.Min(bottomY, renderer.bounds.min.y);
            }
        }
        if (float.IsInfinity(bottomY) || float.IsNaN(bottomY)) return;
        visual.transform.position += Vector3.up * (anchor.transform.position.y - bottomY);
        geometryCache.Invalidate(anchor);
    }

    private static bool IsTerminalVisual(Transform transform, Transform visualRoot)
    {
        while (transform != null)
        {
            if (transform.CompareTag("Terminal")) return true;
            if (transform == visualRoot) break;
            transform = transform.parent;
        }
        return false;
    }

    private static float GetLowestTransformedMeshBound(Bounds bounds, Transform transform)
    {
        float bottomY = float.PositiveInfinity;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            bottomY = Mathf.Min(bottomY, transform.TransformPoint(corner).y);
        }
        return bottomY;
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
        if (previewPropertyBlock == null) previewPropertyBlock = new MaterialPropertyBlock();
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (r is LineRenderer) continue;
            Material[] materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null) continue;
                previewPropertyBlock.Clear();
                r.GetPropertyBlock(previewPropertyBlock, i);
                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = alpha;
                    previewPropertyBlock.SetColor("_BaseColor", c);
                }
                else if (mat.HasProperty("_Color"))
                {
                    Color c = mat.color;
                    c.a = alpha;
                    previewPropertyBlock.SetColor("_Color", c);
                }
                r.SetPropertyBlock(previewPropertyBlock, i);
            }
        }
    }

    private void SetPreviewColor(GameObject obj, Color tintColor)
    {
        if (obj == null) return;
        if (previewPropertyBlock == null) previewPropertyBlock = new MaterialPropertyBlock();
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (r is LineRenderer) continue;
            Material[] materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null) continue;
                previewPropertyBlock.Clear();
                r.GetPropertyBlock(previewPropertyBlock, i);
                if (mat.HasProperty("_BaseColor"))
                {
                    previewPropertyBlock.SetColor("_BaseColor", tintColor);
                }
                else if (mat.HasProperty("_Color"))
                {
                    previewPropertyBlock.SetColor("_Color", tintColor);
                }
                r.SetPropertyBlock(previewPropertyBlock, i);
            }
        }
    }

    private void UpdatePreviewVisual(bool invalid)
    {
        if (previewIsInvalid == invalid) return;
        previewIsInvalid = invalid;
        if (invalid) SetPreviewColor(previewVisual, new Color(1f, 0.22f, 0.22f, 0.75f));
        else
        {
            RestoreOriginalMaterials(previewVisual, currentPrefabToPlace);
            SetPreviewTransparent(previewVisual, previewAlpha);
        }
    }

    public bool UndoLastPlacedComponent()
    {
        return History.Undo();
    }

    public bool RedoLastAction()
    {
        return History.Redo();
    }

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
                instanceRenderers[i].SetPropertyBlock(null);
                for (int j = 0; j < prefabRenderers[i].sharedMaterials.Length; j++)
                    instanceRenderers[i].SetPropertyBlock(null, j);
            }
        }
    }

    public struct FootprintOBB2D
    {
        public Vector2 center;
        public Vector2 halfExtents;
        public Vector2[] corners;
        public Vector2 axis1;
        public Vector2 axis2;
    }

    public bool GetFootprintOBB(GameObject obj, Vector3 worldPos, Quaternion worldRot, out FootprintOBB2D obb, float margin = 0f)
    {
        return geometryCache.TryGetFootprint(obj, worldPos, worldRot, out obb, margin);
    }

    public static bool CheckOBBOverlapSAT(FootprintOBB2D a, FootprintOBB2D b, out int separatingAxisIndex)
    {
        return ARPlacementGeometryCache.Overlaps(a, b, out separatingAxisIndex);
    }

    public bool CheckOverlap(Vector3 candidatePosition, GameObject ignoreObject, out GameObject overlappingObject, float marginOffset = 0f)
    {
        overlappingObject = null;

        const float collisionSkin = 0.003f;
        float effectiveMargin = Mathf.Max(marginOffset, collisionSkin);

        // 1. Xác định GameObject gốc đại diện cho linh kiện đang được kiểm tra (previewAnchor hoặc draggedObject)
        GameObject candidateObj = (previewAnchor != null)
            ? previewAnchor
            : ((ignoreObject != null) ? ignoreObject : (previewVisual != null ? previewVisual : currentPrefabToPlace));

        Quaternion candidateRot = (previewAnchor != null)
            ? previewAnchor.transform.rotation
            : ((ignoreObject != null) ? ignoreObject.transform.rotation : Quaternion.identity);

        if (candidateObj == null) return false;

        return CheckOverlapForObject(candidateObj, candidatePosition, candidateRot, ignoreObject,
            out overlappingObject, effectiveMargin);
    }

    private bool CheckOverlapForObject(GameObject candidateObj, Vector3 candidatePosition, Quaternion candidateRot,
        GameObject ignoreObject, out GameObject overlappingObject, float marginOffset)
    {
        overlappingObject = null;
        const float collisionSkin = 0.003f;
        float effectiveMargin = Mathf.Max(marginOffset, collisionSkin);

        if (!GetFootprintOBB(candidateObj, candidatePosition, candidateRot, out FootprintOBB2D candidateOBB, effectiveMargin))
        {
            return false;
        }

        // History is the authoritative registry (including undo/redo). Never scan
        // the scene or allocate a temporary list in this per-frame path.
        for (int i = 0; i < placedObjectsHistory.Count; i++)
        {
            GameObject placed = placedObjectsHistory[i];
            if (placed == null || !placed.activeInHierarchy || placed == candidateObj || placed == ignoreObject)
                continue;

            // Sàn và mặt bàn có thể chồng nhau theo XZ nhưng ở cao độ khác nhau.
            // Chỉ chống đè lên nhau giữa các vật trên cùng một bề mặt thực.
            if (Mathf.Abs(candidatePosition.y - placed.transform.position.y) > 0.08f)
            {
                continue;
            }

            if (GetFootprintOBB(placed, placed.transform.position, placed.transform.rotation, out FootprintOBB2D placedOBB, collisionSkin))
            {
                if (CheckOBBOverlapSAT(candidateOBB, placedOBB, out int separatingAxisIndex))
                {
                    overlappingObject = placed;
                    return true;
                }
            }
        }

        return false;
    }

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
// HỆ THỐNG COMMAND HISTORY CHO PHÉP UNDO / REDO ĐA THAO TÁC
// =========================================================================

public interface IUndoableAction
{
    void Undo();
    void Redo();
    string Description { get; }
}

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
            targetObject.GetComponent<ARInstantPlacementAnchor>()?.StopRefinement();
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
            targetObject.GetComponent<ARInstantPlacementAnchor>()?.StopRefinement();
            targetObject.transform.position = newPosition;
            targetObject.transform.rotation = newRotation;
            wireController?.UpdateActiveWirePositionsExternal();
            Debug.Log($"<color=cyan>[History]</color> Redo: Đưa [{targetObject.name}] tới vị trí sau khi di chuyển.");
        }
    }
}

public class RotateComponentAction : IUndoableAction
{
    private readonly GameObject targetObject;
    private readonly Quaternion oldRotation;
    private readonly Quaternion newRotation;
    private readonly WireConnectionController wireController;
    private readonly TapToPlaceController placeController;

    public string Description => $"Xoay [{targetObject?.name}] 90°";

    public RotateComponentAction(GameObject obj, Quaternion oldRot, Quaternion newRot, WireConnectionController wController, TapToPlaceController pController)
    {
        targetObject = obj;
        oldRotation = oldRot;
        newRotation = newRot;
        wireController = wController;
        placeController = pController;
    }

    public void Undo()
    {
        if (targetObject != null)
        {
            targetObject.transform.rotation = oldRotation;
            wireController?.UpdateActiveWirePositionsExternal();
            placeController?.NotifyComponentRotated(targetObject);
            Debug.Log($"<color=cyan>[History]</color> Undo: Đưa [{targetObject.name}] về góc xoay trước.");
        }
    }

    public void Redo()
    {
        if (targetObject != null)
        {
            targetObject.transform.rotation = newRotation;
            wireController?.UpdateActiveWirePositionsExternal();
            placeController?.NotifyComponentRotated(targetObject);
            Debug.Log($"<color=cyan>[History]</color> Redo: Xoay lại [{targetObject.name}].");
        }
    }
}

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

public class CircuitHistoryManager
{
    private readonly Stack<IUndoableAction> undoStack = new Stack<IUndoableAction>();
    private readonly Stack<IUndoableAction> redoStack = new Stack<IUndoableAction>();

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;
    public int UndoCount => undoStack.Count;
    public int RedoCount => redoStack.Count;

    public void RecordAction(IUndoableAction action)
    {
        if (action == null) return;
        undoStack.Push(action);
        redoStack.Clear();
        Debug.Log($"<color=green>[CircuitHistory]</color> Đã ghi nhận thao tác: {action.Description} (Undo stack: {undoStack.Count})");
    }

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

    public void Clear()
    {
        undoStack.Clear();
        redoStack.Clear();
        Debug.Log("<color=cyan>[CircuitHistory]</color> Đã làm sạch toàn bộ lịch sử Undo/Redo.");
    }
}
