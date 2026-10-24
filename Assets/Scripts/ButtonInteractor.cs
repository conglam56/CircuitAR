using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using Mediapipe.Tasks.Vision.HandLandmarker;

[DefaultExecutionOrder(-100)]
public class ButtonInteractor : MonoBehaviour
{
    public enum PlacementFeedback
    {
        None,
        SearchingSurface,
        Ready,
        Anchoring,
        Blocked
    }

    [Header("KÉO OBJECT 'Screen' VÀO ĐÂY")]
    public RectTransform videoPanel;

    [Header("Kéo Nút Vàng (Debug Point - Ngón Trỏ) vào đây")]
    public RectTransform debugPoint;

    [Header("Kéo Nút Ngón Cái (Thumb Point) vào đây (Tự động tạo nếu để trống)")]
    public RectTransform thumbPoint;

    [Header("Tham chiếu Hệ thống")]
    public HandLandmarkerRunner handRunner;

    [Header("Kết nối Logic Game")]
    public MenuHUDController menuHUD;

    [Header("Danh sách Nút tương tác")]
    public List<RectTransform> interactiveButtons;

    [Header("Cấu hình màu sắc con trỏ")]
    public Color hoverColor = new Color(0.7f, 0.9f, 1f);
    public Color pressColor = Color.green;
    public Color indexNormalColor = new Color(1f, 0.88f, 0.1f, 0.95f); // Vàng tươi ngón trỏ
    public Color thumbNormalColor = new Color(0.15f, 0.85f, 1f, 0.95f); // Cyan ngón cái

    [Header("Con trỏ tay kiểu XR")]
    [Min(8f)] public float cursorDiameter = 34f;
    [Range(5f, 120f)] public float cursorFollowSpeed = 60f;
    [Range(0.6f, 1f)] public float pinchPressedScale = 0.8f;
    [Min(0.01f)] public float pinchCompressDuration = 0.08f;
    [Min(0.01f)] public float pinchReleaseDuration = 0.10f;
    [Min(0.01f)] public float pinchHaloDuration = 0.18f;
    [Range(1.2f, 3f)] public float pinchHaloEndScale = 2.35f;
    [Range(0f, 1f)] public float pinchHaloOpacity = 0.62f;
    public Color cursorColor = Color.white;
    public Color cursorOutlineColor = new Color(0.08f, 0.09f, 0.11f, 0.92f);

    [Header("Cấu hình Pinch Hysteresis, Ổn định & Chống Spam")]
    [Tooltip("Ngưỡng chụm ngón tay sau khi chuẩn hóa theo kích thước bàn tay (Pinch ON)")]
    [Range(0.02f, 0.08f)] public float pinchThreshold = 0.040f; // pinchEnterThreshold
    [Tooltip("Ngưỡng nhả ngón tay sau khi chuẩn hóa theo kích thước bàn tay (Pinch OFF)")]
    [Range(0.03f, 0.10f)] public float pinchExitThreshold = 0.052f;
    [Tooltip("Số frame liên tục phải thỏa điều kiện để xác nhận trạng thái (chống nhiễu 1 frame)")]
    public int minStablePinchFrames = 2;
    [Tooltip("Thời gian giãn cách tối thiểu giữa 2 lần kích hoạt Pinch (giây)")]
    public float pinchDebounceTime = 0.25f;
    public float clickCooldown = 0.45f;

    [Header("Bảng Điều Khiển Trục Tọa Độ")]
    public bool hoanDoiTrucXY = true; // Bật sẵn để sửa lỗi lên thành trái
    public bool latNguocTrucX = false;
    public bool latNguocTrucY = false;

    // Tọa độ màn hình ngón trỏ và ngón cái cho các hệ thống khác sử dụng
    public Vector2 CurrentScreenPos { get; private set; }
    public Vector2 CurrentThumbScreenPos { get; private set; }
    public bool JustPinched { get; private set; }
    public bool IsHandVisible { get; private set; }
    public bool IsGestureTemporarilyOccluded { get; private set; }
    public bool IsPointerOverUI { get; private set; }
    public bool PinchConsumedByUI { get; private set; }
    public PlacementFeedback CurrentPlacementFeedback { get; private set; }

    public void SetPlacementFeedback(PlacementFeedback state)
    {
        CurrentPlacementFeedback = state;
        ApplyPlacementFeedback();
    }

    private void ApplyPlacementFeedback()
    {
        if (cursorVisualGraphic == null) return;
        bool drawRing = CurrentPlacementFeedback == PlacementFeedback.SearchingSurface ||
                        CurrentPlacementFeedback == PlacementFeedback.Anchoring;
        // Giữ đúng một con trỏ trắng; chỉ đổi mesh khi trạng thái vòng/chấm đổi.
        // Nhịp co chấm và halo của pinch vẫn do UpdateCursorVisual điều khiển.
        if (cursorVisualGraphic.IsRing != drawRing) cursorVisualGraphic.Configure(drawRing);
    }

    private Dictionary<RectTransform, Image> buttonImages = new Dictionary<RectTransform, Image>();
    private Dictionary<RectTransform, Color> originalColors = new Dictionary<RectTransform, Color>();

    private float targetX = 0f;
    private float targetY = 0f;
    private float targetThumbX = 0f;
    private float targetThumbY = 0f;
    private float rawDistance = 1f;
    private bool isHandVisible = false;
    public bool isPinching = false;
    private bool wasPinching = false;       // trạng thái frame trước để xác định chủ sở hữu cử chỉ
    private bool wasPinchingGlobal = false; // dùng riêng cho JustPinched
    private int consecutivePinchFrames = 0;
    private int consecutiveReleaseFrames = 0;
    private float lastGlobalPinchTime = float.NegativeInfinity;
    private float lastClickTime = float.NegativeInfinity;
    private object _lock = new object();
    private long lastHandResultTicks;
    private float lastVisibleHandAt = float.NegativeInfinity;
    private const float GestureOcclusionGrace = 0.18f;
    private const int HandMaskLandmarkCount = 21;
    private const float HandMaskMarginPixels = 8f;
    private readonly Vector2[] rawHandMaskLandmarks = new Vector2[HandMaskLandmarkCount];
    private readonly Vector2[] handMaskImagePoints = new Vector2[HandMaskLandmarkCount];
    private readonly Vector2[] handMaskScreenPoints = new Vector2[HandMaskLandmarkCount];
    private readonly Vector2[] handMaskHull = new Vector2[HandMaskLandmarkCount * 2];
    private bool hasRawHandMaskLandmarks;
    private int handMaskHullCount;

    private Image debugPointImage;
    private Image thumbPointImage;
    private GameObject cursorOverlayRoot;
    private RectTransform cursorOverlayRect;
    private RectTransform cursorVisual;
    private HandCursorGraphic cursorVisualGraphic;
    private RectTransform pinchHalo;
    private HandCursorGraphic pinchHaloGraphic;
    private Vector2 displayedCursorAnchor;
    private bool cursorPositionInitialized;
    private bool pinchVisualPlaying;
    private float pinchVisualElapsed;

    void Start()
    {
        for (int i = interactiveButtons.Count - 1; i >= 0; i--)
        {
            var btn = interactiveButtons[i];
            if (btn != null)
            {
                RegisterButton(btn);
            }
        }

        SetupFingerPointVisuals();
    }

    /// <summary>
    /// Thiết lập kích thước và tự động tạo chấm ngón cái nếu chưa có
    /// </summary>
    private void SetupFingerPointVisuals()
    {
        if (debugPoint != null)
        {
            debugPoint.sizeDelta = Vector2.one * cursorDiameter;
            debugPointImage = debugPoint.GetComponent<Image>();
            if (debugPointImage == null) debugPointImage = debugPoint.gameObject.AddComponent<Image>();
            debugPointImage.raycastTarget = false;
            // RectTransform này chỉ giữ tọa độ logic cho raycast/UI. Hình con trỏ
            // được vẽ trên một overlay riêng để không bị RawImage/Canvas MediaPipe che.
            debugPointImage.enabled = false;
            EnsureCursorOverlay();
        }

        if (thumbPoint == null && videoPanel != null)
        {
            GameObject thumbObj = new GameObject("ThumbPoint_Auto", typeof(RectTransform), typeof(Image));
            thumbObj.transform.SetParent(videoPanel, false);
            thumbPoint = thumbObj.GetComponent<RectTransform>();
            thumbPoint.sizeDelta = new Vector2(24f, 24f);

            thumbPointImage = thumbObj.GetComponent<Image>();
            thumbPointImage.raycastTarget = false;
            thumbPointImage.enabled = false;
        }
        else if (thumbPoint != null)
        {
            thumbPoint.sizeDelta = new Vector2(24f, 24f);
            thumbPointImage = thumbPoint.GetComponent<Image>();
            if (thumbPointImage != null)
            {
                thumbPointImage.raycastTarget = false;
                thumbPointImage.enabled = false;
            }
        }
    }

    private void EnsureCursorOverlay()
    {
        if (cursorOverlayRoot != null) return;

        cursorOverlayRoot = new GameObject("HandCursorOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        cursorOverlayRect = cursorOverlayRoot.GetComponent<RectTransform>();
        Canvas overlayCanvas = cursorOverlayRoot.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 10000;

        CanvasScaler scaler = cursorOverlayRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject cursorObject = new GameObject("Cursor", typeof(RectTransform), typeof(HandCursorGraphic));
        cursorObject.transform.SetParent(cursorOverlayRoot.transform, false);
        cursorVisual = cursorObject.GetComponent<RectTransform>();
        cursorVisual.anchorMin = cursorVisual.anchorMax = new Vector2(0.5f, 0.5f);
        cursorVisual.pivot = new Vector2(0.5f, 0.5f);
        cursorVisual.sizeDelta = Vector2.one * cursorDiameter;

        cursorVisualGraphic = cursorObject.GetComponent<HandCursorGraphic>();
        ApplyPlacementFeedback();
        cursorVisualGraphic.color = cursorColor;
        cursorVisualGraphic.raycastTarget = false;

        Shadow cursorShadow = cursorObject.AddComponent<Shadow>();
        cursorShadow.effectColor = new Color(0f, 0f, 0f, 0.48f);
        cursorShadow.effectDistance = new Vector2(0f, -2f);
        cursorShadow.useGraphicAlpha = true;

        Outline cursorOutline = cursorObject.AddComponent<Outline>();
        cursorOutline.effectColor = cursorOutlineColor;
        cursorOutline.effectDistance = new Vector2(2f, 2f);
        cursorOutline.useGraphicAlpha = true;

        GameObject haloObject = new GameObject("PinchHalo", typeof(RectTransform), typeof(HandCursorGraphic));
        haloObject.transform.SetParent(cursorVisual, false);
        pinchHalo = haloObject.GetComponent<RectTransform>();
        pinchHalo.anchorMin = pinchHalo.anchorMax = new Vector2(0.5f, 0.5f);
        pinchHalo.pivot = new Vector2(0.5f, 0.5f);
        pinchHalo.anchoredPosition = Vector2.zero;
        pinchHalo.sizeDelta = Vector2.one * cursorDiameter;

        pinchHaloGraphic = haloObject.GetComponent<HandCursorGraphic>();
        pinchHaloGraphic.Configure(true, 0.14f);
        pinchHaloGraphic.raycastTarget = false;
        SetHaloAlpha(0f);
        cursorOverlayRoot.SetActive(false);
    }

    /// <summary>
    /// Đăng ký nút UI động vào hệ thống nhận diện cử chỉ MediaPipe
    /// </summary>
    public void RegisterButton(RectTransform btn)
    {
        if (btn == null) return;
        if (!interactiveButtons.Contains(btn))
        {
            interactiveButtons.Add(btn);
        }
        Image img = btn.GetComponent<Image>();
        if (img != null && !buttonImages.ContainsKey(btn))
        {
            buttonImages[btn] = img;
            originalColors[btn] = img.color;
        }
    }

    /// <summary>
    /// Hủy đăng ký nút UI khỏi hệ thống cử chỉ
    /// </summary>
    public void UnregisterButton(RectTransform btn)
    {
        if (btn == null) return;
        if (buttonImages.ContainsKey(btn))
        {
            buttonImages[btn].color = originalColors[btn];
            buttonImages.Remove(btn);
            originalColors.Remove(btn);
        }
        interactiveButtons.Remove(btn);
    }

    void OnEnable() { if (handRunner != null) handRunner.OnHandResult += ProcessHandResult; }
    void OnDisable()
    {
        if (handRunner != null) handRunner.OnHandResult -= ProcessHandResult;
        IsHandVisible = false;
        IsGestureTemporarilyOccluded = false;
        lastVisibleHandAt = float.NegativeInfinity;
        isPinching = false;
        JustPinched = false;
        PinchConsumedByUI = false;
        wasPinching = wasPinchingGlobal = false;
        lock (_lock) { hasRawHandMaskLandmarks = false; }
        handMaskHullCount = 0;
        SetPlacementFeedback(PlacementFeedback.None);
        if (cursorOverlayRoot != null) cursorOverlayRoot.SetActive(false);
        ResetCursorVisual();
    }

    void OnDestroy()
    {
        if (cursorOverlayRoot != null)
        {
            if (Application.isPlaying) Destroy(cursorOverlayRoot);
            else DestroyImmediate(cursorOverlayRoot);
        }
    }

    void Update()
    {
        float x, y, tx, ty, dist;
        bool visible;
        bool hasHandMask;
        bool freshMissingHand;

        lock (_lock)
        {
            x = targetX;
            y = targetY;
            tx = targetThumbX;
            ty = targetThumbY;
            dist = rawDistance;
            double resultAge = (System.Diagnostics.Stopwatch.GetTimestamp() - lastHandResultTicks) /
                               (double)System.Diagnostics.Stopwatch.Frequency;
            visible = isHandVisible && resultAge <= 0.3;
            freshMissingHand = !isHandVisible && resultAge <= 0.3;
            hasHandMask = hasRawHandMaskLandmarks;
            if (hasHandMask)
                System.Array.Copy(rawHandMaskLandmarks, handMaskImagePoints, HandMaskLandmarkCount);
        }
        // A missing display matrix has a finite portrait fallback; a corrupt
        // landmark does not. Never keep a previous pinch alive at fallback center.
        visible = visible && IsFiniteHandPoint(new Vector2(x, y)) &&
            IsFiniteHandPoint(new Vector2(tx, ty)) && !float.IsNaN(dist) && !float.IsInfinity(dist);

        // Resolve the active source on Unity's main thread, not in MediaPipe's
        // native callback. AR uses the same crop/orientation as its background;
        // webcam/video sources retain the existing Inspector axis calibration.
        Vector2 indexViewportPoint;
        Vector2 thumbViewportPoint;
        var imageSource = Mediapipe.Unity.Sample.ImageSourceProvider.ImageSource;
        var arSource = imageSource as CustomARFrameBridge;
        if (arSource != null)
        {
            arSource.TryMapImagePointsToViewport(new Vector2(x, y), new Vector2(tx, ty),
                out indexViewportPoint, out thumbViewportPoint);
            // The CPU image can include a hand outside the visible AR crop. Do
            // not clamp that finger onto an unrelated floor point at the edge.
            visible = visible && indexViewportPoint.x >= 0f && indexViewportPoint.x <= 1f &&
                indexViewportPoint.y >= 0f && indexViewportPoint.y <= 1f;
        }
        else
        {
            indexViewportPoint = MapManualImagePointToViewport(new Vector2(x, y));
            thumbViewportPoint = MapManualImagePointToViewport(new Vector2(tx, ty));
        }

        IsHandVisible = visible && videoPanel != null && debugPoint != null;
        bool finiteInput = IsFiniteHandPoint(new Vector2(x, y)) &&
            IsFiniteHandPoint(new Vector2(tx, ty)) && !float.IsNaN(dist) && !float.IsInfinity(dist);
        IsGestureTemporarilyOccluded = !IsHandVisible && freshMissingHand && finiteInput &&
            videoPanel != null && debugPoint != null && isPinching &&
            Time.unscaledTime - lastVisibleHandAt <= GestureOcclusionGrace &&
            (imageSource == null || imageSource.isPlaying) &&
            (handRunner == null || (!handRunner.IsPaused && handRunner.isActiveAndEnabled));
        if (IsHandVisible) lastVisibleHandAt = Time.unscaledTime;
        handMaskHullCount = 0;
        if (IsHandVisible && hasHandMask &&
            (handRunner == null || (!handRunner.IsPaused && handRunner.isActiveAndEnabled)) &&
            (imageSource == null || imageSource.isPlaying))
            UpdateHandDepthProbeMask(arSource);

        if (!visible || videoPanel == null)
        {
            foreach (var btn in interactiveButtons)
            {
                if (btn != null)
                {
                    if (buttonImages.ContainsKey(btn))
                        buttonImages[btn].color = originalColors[btn];

                    HoldToActivateButton holdBtn = btn.GetComponent<HoldToActivateButton>();
                    if (holdBtn != null) holdBtn.ResetHoldState();
                }
            }
            if (debugPoint != null) debugPoint.gameObject.SetActive(false);
            if (thumbPoint != null) thumbPoint.gameObject.SetActive(false);
            if (cursorOverlayRoot != null) cursorOverlayRoot.SetActive(false);

            JustPinched = false;
            IsPointerOverUI = false;
            // No pointer or depth mask is valid during this gap. Retain only
            // the active gesture's identity/owner so one missing inference
            // cannot cancel a world pinch or turn a UI pinch into a world one.
            if (IsGestureTemporarilyOccluded) return;
            PinchConsumedByUI = false;
            wasPinchingGlobal = false;
            wasPinching = false;
            consecutivePinchFrames = 0;
            consecutiveReleaseFrames = 0;
            isPinching = false;
            cursorPositionInitialized = false;
            ResetCursorVisual();
            return;
        }

        // --- 1. HYSTERESIS VÀ TEMPORAL STABILITY CHO PINCH DETECTION ---
        if (dist < pinchThreshold)
        {
            consecutivePinchFrames++;
            consecutiveReleaseFrames = 0;
        }
        else if (dist > pinchExitThreshold)
        {
            consecutiveReleaseFrames++;
            consecutivePinchFrames = 0;
        }

        if (!isPinching && consecutivePinchFrames >= minStablePinchFrames)
        {
            isPinching = true;
        }
        else if (isPinching && consecutiveReleaseFrames >= minStablePinchFrames)
        {
            isPinching = false;
        }

        // Phát sự kiện đúng một lần ở cạnh bắt đầu pinch. Giữ hai ngón khép
        // không thể kích hoạt lặp cho tới khi tay đã nhả qua ngưỡng hysteresis.
        if (isPinching && !wasPinchingGlobal)
        {
            if (Time.time - lastGlobalPinchTime >= pinchDebounceTime)
            {
                JustPinched = true;
                wasPinchingGlobal = true;
                lastGlobalPinchTime = Time.time;
                BeginPinchVisual();
            }
            else
            {
                JustPinched = false;
            }
        }
        else
        {
            JustPinched = false;
            if (!isPinching) wasPinchingGlobal = false;
        }

        // --- 2. CẬP NHẬT TỌA ĐỘ VÀ VISUAL CHO 2 ĐẦU NGÓN TAY (INDEX + THUMB) ---
        Vector2 screenPos = Vector2.zero;
        Vector2 thumbScreenPos = Vector2.zero;

        // Cập nhật chấm ngón trỏ
        if (debugPoint != null)
        {
            debugPoint.gameObject.SetActive(true);
            if (debugPoint.parent != videoPanel) debugPoint.SetParent(videoPanel, false);

            Vector2 desiredCursorAnchor = new Vector2(Mathf.Clamp01(indexViewportPoint.x), Mathf.Clamp01(indexViewportPoint.y));
            if (!cursorPositionInitialized)
            {
                displayedCursorAnchor = desiredCursorAnchor;
                cursorPositionInitialized = true;
            }
            else
            {
                float follow = 1f - Mathf.Exp(-cursorFollowSpeed * Time.unscaledDeltaTime);
                displayedCursorAnchor = Vector2.Lerp(displayedCursorAnchor, desiredCursorAnchor, follow);
            }

            debugPoint.anchorMin = displayedCursorAnchor;
            debugPoint.anchorMax = displayedCursorAnchor;
            debugPoint.anchoredPosition = Vector2.zero;

            // Dùng trực tiếp tọa độ màn hình đã hiệu chỉnh. Đi vòng qua Canvas
            // MediaPipe làm sai vị trí trên Android dọc và có thể đẩy overlay ra ngoài.
            screenPos = new Vector2(displayedCursorAnchor.x * Screen.width,
                displayedCursorAnchor.y * Screen.height);
            UpdateCursorOverlayPosition(screenPos);
            UpdateCursorVisual(Time.unscaledDeltaTime);
        }

        // Cập nhật chấm ngón cái
        if (thumbPoint != null)
        {
            thumbPoint.gameObject.SetActive(true);
            if (thumbPoint.parent != videoPanel) thumbPoint.SetParent(videoPanel, false);
            thumbPoint.anchorMin = thumbViewportPoint;
            thumbPoint.anchorMax = thumbViewportPoint;
            thumbPoint.anchoredPosition = Vector2.zero;

            thumbScreenPos = new Vector2(thumbViewportPoint.x * Screen.width,
                thumbViewportPoint.y * Screen.height);
        }

        CurrentScreenPos = screenPos;
        CurrentThumbScreenPos = thumbScreenPos;
        bool anyButtonHovered = false;
        bool pinchStarted = isPinching && !wasPinching;
        if (!isPinching) PinchConsumedByUI = false;

        // --- 3. TƯƠNG TÁC GIAO DIỆN UI (HOVER / CLICK / HOLD) ---
        foreach (var btn in interactiveButtons)
        {
            if (btn == null || !btn.gameObject.activeInHierarchy || !buttonImages.ContainsKey(btn)) continue;

            Image img = buttonImages[btn];
            if (!IsButtonAvailable(btn, img))
            {
                if (img != null) img.color = originalColors[btn];
                var hiddenHold = btn.GetComponent<HoldToActivateButton>();
                if (hiddenHold != null) hiddenHold.ResetHoldState();
                continue;
            }

            Canvas btnCanvas = btn.GetComponentInParent<Canvas>();
            Camera btnCam = (btnCanvas != null && btnCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? btnCanvas.worldCamera : null;

            if (btnCam == null && btnCanvas != null && btnCanvas.renderMode == RenderMode.WorldSpace)
            {
                btnCam = Camera.main;
            }

            bool isHovering = RectTransformUtility.RectangleContainsScreenPoint(btn, screenPos, btnCam);
            // Chỉ UI ở điểm bắt đầu pinch mới được nhận cử chỉ này. Đi ngang qua
            // nút khi đang giữ pinch đặt vật không được đánh cắp lệnh đặt.
            if (isHovering && pinchStarted) PinchConsumedByUI = true;
            Color hoverTint = hoverColor;
            Color pressTint = pressColor;
            hoverTint.a = originalColors[btn].a;
            pressTint.a = originalColors[btn].a;

            HoldToActivateButton holdComp = btn.GetComponent<HoldToActivateButton>();
            if (holdComp != null)
            {
                holdComp.SetPinchHoverState(isHovering, isPinching && PinchConsumedByUI);
                if (isHovering)
                {
                    anyButtonHovered = true;
                    img.color = isPinching ? pressTint : hoverTint;
                }
                else
                {
                    img.color = originalColors[btn];
                }
                continue;
            }

            if (isHovering)
            {
                anyButtonHovered = true;
                img.color = isPinching ? pressTint : hoverTint;

                if (pinchStarted && Time.time - lastClickTime > clickCooldown)
                {
                    Button uiBtn = btn.GetComponent<Button>();
                    if (uiBtn != null && uiBtn.interactable)
                    {
                        uiBtn.onClick.Invoke();
                    }
                    if (menuHUD != null) menuHUD.ReceiveClick(btn);
                    lastClickTime = Time.time;
                }
            }
            else
            {
                img.color = originalColors[btn];
            }
        }

        wasPinching = isPinching;
        IsPointerOverUI = anyButtonHovered;

    }

    private static bool IsButtonAvailable(RectTransform button, Image image)
    {
        if (image == null || !image.isActiveAndEnabled || !image.raycastTarget || image.color.a <= 0.001f)
            return false;
        Canvas canvas = button.GetComponentInParent<Canvas>();
        if (canvas == null || !canvas.isActiveAndEnabled) return false;
        Button uiButton = button.GetComponent<Button>();
        if (uiButton != null && (!uiButton.IsActive() || !uiButton.IsInteractable())) return false;

        float alpha = 1f;
        for (Transform parent = button; parent != null; parent = parent.parent)
        {
            bool ignoreParents = false;
            foreach (CanvasGroup group in parent.GetComponents<CanvasGroup>())
            {
                if (!group.isActiveAndEnabled) continue;
                alpha *= group.alpha;
                if (alpha <= 0.001f || !group.blocksRaycasts || !group.interactable) return false;
                ignoreParents |= group.ignoreParentGroups;
            }
            if (ignoreParents) break;
        }
        return true;
    }

    private void BeginPinchVisual()
    {
        pinchVisualElapsed = 0f;
        pinchVisualPlaying = true;
        if (pinchHalo != null) pinchHalo.localScale = Vector3.one;
        SetHaloAlpha(0f);
    }

    private void UpdateCursorVisual(float deltaTime)
    {
        if (cursorVisual == null) return;
        if (cursorVisualGraphic != null) cursorVisualGraphic.color = cursorColor;

        if (!pinchVisualPlaying)
        {
            cursorVisual.localScale = Vector3.one;
            SetHaloAlpha(0f);
            return;
        }

        pinchVisualElapsed += Mathf.Max(0f, deltaTime);
        float compressDuration = Mathf.Max(0.01f, pinchCompressDuration);
        float releaseDuration = Mathf.Max(0.01f, pinchReleaseDuration);
        float haloDuration = Mathf.Max(0.01f, pinchHaloDuration);

        float cursorScale;
        if (pinchVisualElapsed < compressDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, pinchVisualElapsed / compressDuration);
            cursorScale = Mathf.Lerp(1f, pinchPressedScale, t);
        }
        else if (pinchVisualElapsed < compressDuration + releaseDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, (pinchVisualElapsed - compressDuration) / releaseDuration);
            cursorScale = Mathf.Lerp(pinchPressedScale, 1f, t);
        }
        else
        {
            cursorScale = 1f;
        }
        cursorVisual.localScale = Vector3.one * cursorScale;

        float haloElapsed = pinchVisualElapsed - compressDuration;
        if (haloElapsed >= 0f && haloElapsed < haloDuration)
        {
            float t = Mathf.Clamp01(haloElapsed / haloDuration);
            float easedExpansion = 1f - Mathf.Pow(1f - t, 3f);
            if (pinchHalo != null)
            {
                pinchHalo.localScale = Vector3.one * Mathf.Lerp(1f, pinchHaloEndScale, easedExpansion);
            }
            SetHaloAlpha(pinchHaloOpacity * Mathf.Pow(1f - t, 2f));
        }
        else
        {
            SetHaloAlpha(0f);
        }

        float totalDuration = Mathf.Max(compressDuration + releaseDuration, compressDuration + haloDuration);
        if (pinchVisualElapsed >= totalDuration)
        {
            pinchVisualPlaying = false;
            cursorVisual.localScale = Vector3.one;
            SetHaloAlpha(0f);
        }
    }

    private void ResetCursorVisual()
    {
        pinchVisualPlaying = false;
        pinchVisualElapsed = 0f;
        if (cursorVisual != null) cursorVisual.localScale = Vector3.one;
        if (pinchHalo != null) pinchHalo.localScale = Vector3.one;
        SetHaloAlpha(0f);
    }

    private void SetHaloAlpha(float alpha)
    {
        if (pinchHaloGraphic == null) return;
        Color haloColor = Color.white;
        haloColor.a = Mathf.Clamp01(alpha);
        pinchHaloGraphic.color = haloColor;
        pinchHaloGraphic.enabled = haloColor.a > 0.001f;
    }

    private void UpdateCursorOverlayPosition(Vector2 screenPosition)
    {
        EnsureCursorOverlay();
        if (cursorOverlayRoot == null || cursorOverlayRect == null || cursorVisual == null) return;

        if (!cursorOverlayRoot.activeSelf) cursorOverlayRoot.SetActive(true);
        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);
        Vector2 normalized = new Vector2(
            Mathf.Clamp01(screenPosition.x / width),
            Mathf.Clamp01(screenPosition.y / height));
        cursorVisual.anchorMin = normalized;
        cursorVisual.anchorMax = normalized;
        cursorVisual.anchoredPosition = Vector2.zero;
    }

    private void ProcessHandResult(HandLandmarkerResult result)
    {
        lock (_lock) { lastHandResultTicks = System.Diagnostics.Stopwatch.GetTimestamp(); }
        if (result.handLandmarks == null || result.handLandmarks.Count == 0)
        {
            lock (_lock) { isHandVisible = false; hasRawHandMaskLandmarks = false; }
            return;
        }

        var hand = result.handLandmarks[0];
        var indexTip = hand.landmarks[8];
        var thumbTip = hand.landmarks[4];

        float rawX = indexTip.x;
        float rawY = indexTip.y;
        float rawThumbX = thumbTip.x;
        float rawThumbY = thumbTip.y;

        // Absolute image distance makes a distant, open hand stay "pinched".
        // Normalize by the palm so the same physical opening releases the gesture
        // at both near and far distances; retain the configured reference thresholds.
        var wrist = hand.landmarks[0];
        var middleMcp = hand.landmarks[9];
        var indexMcp = hand.landmarks[5];
        var pinkyMcp = hand.landmarks[17];
        float palmSize = Mathf.Max(
            Vector2.Distance(new Vector2(wrist.x, wrist.y), new Vector2(middleMcp.x, middleMcp.y)),
            Vector2.Distance(new Vector2(indexMcp.x, indexMcp.y), new Vector2(pinkyMcp.x, pinkyMcp.y)));
        float distance = GetNormalizedPinchDistance(
            new Vector2(indexTip.x, indexTip.y), new Vector2(thumbTip.x, thumbTip.y), palmSize);

        lock (_lock)
        {
            targetX = rawX;
            targetY = rawY;
            targetThumbX = rawThumbX;
            targetThumbY = rawThumbY;
            rawDistance = distance;
            isHandVisible = true;
            hasRawHandMaskLandmarks = hand.landmarks.Count >= HandMaskLandmarkCount;
            if (hasRawHandMaskLandmarks)
                for (int i = 0; i < HandMaskLandmarkCount; i++)
                {
                    var landmark = hand.landmarks[i];
                    rawHandMaskLandmarks[i] = new Vector2(landmark.x, landmark.y);
                }
        }
    }

    /// <summary>
    /// Conservative hand silhouette for rejecting environment-depth probes only.
    /// It must not filter plane hits, UI hover or the normal world pointer: those
    /// intentionally originate at the user's fingertip.
    /// </summary>
    public bool IsPointOverTrackedHand(Vector2 screenPoint)
    {
        if (!IsHandVisible || handMaskHullCount < 3 ||
            (handRunner != null && (handRunner.IsPaused || !handRunner.isActiveAndEnabled))) return false;
        var source = Mediapipe.Unity.Sample.ImageSourceProvider.ImageSource;
        if (source != null && !source.isPlaying) return false;
        lock (_lock)
        {
            double age = (System.Diagnostics.Stopwatch.GetTimestamp() - lastHandResultTicks) /
                (double)System.Diagnostics.Stopwatch.Frequency;
            if (!isHandVisible || !hasRawHandMaskLandmarks || age > 0.3) return false;
        }
        return IsPointInHandMask(screenPoint, handMaskHull, handMaskHullCount, HandMaskMarginPixels);
    }

    private void UpdateHandDepthProbeMask(CustomARFrameBridge arSource)
    {
        for (int i = 0; i < HandMaskLandmarkCount; i += 2)
        {
            int second = Mathf.Min(i + 1, HandMaskLandmarkCount - 1);
            Vector2 firstViewport, secondViewport;
            if (arSource != null)
                arSource.TryMapImagePointsToViewport(handMaskImagePoints[i], handMaskImagePoints[second],
                    out firstViewport, out secondViewport);
            else
            {
                firstViewport = MapManualImagePointToViewport(handMaskImagePoints[i]);
                secondViewport = MapManualImagePointToViewport(handMaskImagePoints[second]);
            }
            if (!IsFiniteHandPoint(handMaskImagePoints[i]) || !IsFiniteHandPoint(handMaskImagePoints[second]) ||
                !IsFiniteHandPoint(firstViewport) || !IsFiniteHandPoint(secondViewport)) return;
            handMaskScreenPoints[i] = new Vector2(firstViewport.x * Screen.width, firstViewport.y * Screen.height);
            handMaskScreenPoints[second] = new Vector2(secondViewport.x * Screen.width, secondViewport.y * Screen.height);
        }
        handMaskHullCount = BuildHandMaskHull(handMaskScreenPoints, HandMaskLandmarkCount, handMaskHull);
    }

    private static bool IsFiniteHandPoint(Vector2 point) =>
        !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsInfinity(point.x) && !float.IsInfinity(point.y);

    // Insertion sort and monotone-chain hull reuse the fixed 21-point buffers;
    // no LINQ, temporary arrays or per-frame delegates are needed.
    private static int BuildHandMaskHull(Vector2[] points, int count, Vector2[] hull)
    {
        if (points == null || hull == null || count < 3 || count > points.Length || hull.Length < count * 2) return 0;
        for (int i = 0; i < count; i++)
        {
            if (!IsFiniteHandPoint(points[i])) return 0;
            Vector2 point = points[i];
            int j = i - 1;
            while (j >= 0 && (points[j].x > point.x ||
                (points[j].x == point.x && points[j].y > point.y)))
            {
                points[j + 1] = points[j];
                j--;
            }
            points[j + 1] = point;
        }

        int length = 0;
        for (int i = 0; i < count; i++)
        {
            while (length >= 2 && HandMaskCross(hull[length - 2], hull[length - 1], points[i]) <= 0f) length--;
            hull[length++] = points[i];
        }
        int lowerLength = length + 1;
        for (int i = count - 2; i >= 0; i--)
        {
            while (length >= lowerLength && HandMaskCross(hull[length - 2], hull[length - 1], points[i]) <= 0f) length--;
            hull[length++] = points[i];
        }
        length--;
        return length >= 3 ? length : 0;
    }

    private static float HandMaskCross(Vector2 a, Vector2 b, Vector2 c) =>
        (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    private static bool IsPointInHandMask(Vector2 point, Vector2[] hull, int count, float margin)
    {
        if (!IsFiniteHandPoint(point) || hull == null || count < 3 || count > hull.Length) return false;
        bool inside = true;
        float marginSquared = Mathf.Max(0f, margin) * Mathf.Max(0f, margin);
        for (int i = 0, previous = count - 1; i < count; previous = i++)
        {
            Vector2 a = hull[previous];
            Vector2 b = hull[i];
            if (HandMaskCross(a, b, point) < 0f) inside = false;
            Vector2 edge = b - a;
            float t = edge.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0f;
            if ((point - (a + edge * t)).sqrMagnitude <= marginSquared) return true;
        }
        return inside;
    }

    private Vector2 MapManualImagePointToViewport(Vector2 imagePoint)
    {
        if (hoanDoiTrucXY) imagePoint = new Vector2(imagePoint.y, imagePoint.x);
        if (latNguocTrucX) imagePoint.x = 1f - imagePoint.x;
        if (latNguocTrucY) imagePoint.y = 1f - imagePoint.y;
        return new Vector2(imagePoint.x, 1f - imagePoint.y);
    }

    private static float GetNormalizedPinchDistance(Vector2 indexTip, Vector2 thumbTip, float palmSize)
    {
        if (palmSize < 0.001f) return 1f;
        return Vector2.Distance(indexTip, thumbTip) * 0.18f / palmSize;
    }
}
