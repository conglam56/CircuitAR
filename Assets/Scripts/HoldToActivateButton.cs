using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// HoldToActivateButton - Cơ chế GIỮ ĐỂ KÍCH HOẠT (Hold-To-Confirm / Pinch & Hold).
/// Dành riêng cho các nút có tính rủi ro cao như 'RESET MẠCH' và 'QUÉT MẶT PHẲNG'.
/// - Chụm ngón tay (Pinch MediaPipe) hoặc chạm cảm ứng giữ liên tục trong 1.25 giây mới kích hoạt.
/// - Hiển thị vòng tròn tiến trình (Radial 360 Progress Ring) quét dần từ 0% -> 100%.
/// - Nhãn chữ hiển thị trực tiếp tiến trình: 'GIỮ ĐỂ RESET (50%)'.
/// - Nếu thả tay trước thời hạn (nhấp nhầm / accidental pinch): ngay lập tức hủy bỏ và reset về 0.
/// </summary>
public class HoldToActivateButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("--- CẤU HÌNH THỜI GIAN GIỮ ---")]
    [Tooltip("Thời gian cần giữ liên tục để kích hoạt (giây). Mặc định 1.25s chuẩn công thái học XR.")]
    public float holdDuration = 1.25f;

    [Tooltip("Hành động khi giữ đủ thời gian")]
    public Action onHoldComplete;

    [Tooltip("Nhãn chữ mặc định")]
    public string defaultLabel = "";

    [Tooltip("Tiền tố nhãn chữ khi đang giữ (ví dụ: 'GIỮ ĐỂ RESET')")]
    public string holdingLabel = "GIỮ ĐỂ KÍCH HOẠT";

    [Header("--- THAM CHIẾU UI ---")]
    public Image progressRingImage;
    public TextMeshProUGUI labelTMP;
    public RectTransform buttonRect;

    private bool isPinchHovering = false;
    private bool isPinching = false;
    private bool isPointerDown = false;
    private float currentHoldTimer = 0f;
    private float lastPinchTime = 0f;
    private bool isTriggered = false;
    private Vector3 originalScale = Vector3.one;

    void Awake()
    {
        if (buttonRect == null) buttonRect = GetComponent<RectTransform>();
        if (buttonRect != null) originalScale = buttonRect.localScale;
    }

    /// <summary>
    /// Nhận trạng thái Hover và Pinch từ hệ thống nhận diện cử chỉ MediaPipe (ButtonInteractor)
    /// </summary>
    public void SetPinchHoverState(bool hovering, bool pinching)
    {
        isPinchHovering = hovering;
        isPinching = pinching;

        if (hovering && pinching)
        {
            lastPinchTime = Time.time;
        }
    }

    void Update()
    {
        // Grace period 0.12s cho cử chỉ tay MediaPipe chống rung lắc khung hình (jitter)
        bool pinchActive = isPinchHovering && (isPinching || (Time.time - lastPinchTime < 0.12f && currentHoldTimer > 0.1f));
        bool isHolding = pinchActive || isPointerDown;

        if (isHolding && !isTriggered)
        {
            currentHoldTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(currentHoldTimer / holdDuration);

            // 1. Cập nhật vòng tròn tiến trình
            if (progressRingImage != null)
            {
                if (!progressRingImage.gameObject.activeSelf) progressRingImage.gameObject.SetActive(true);
                progressRingImage.fillAmount = progress;
            }

            // 2. Hiệu ứng sạc năng lượng: Nút phồng nhẹ 1.0 -> 1.08x
            if (buttonRect != null)
            {
                float scalePulse = 1f + 0.08f * progress;
                buttonRect.localScale = originalScale * scalePulse;
            }

            // 3. Cập nhật chữ hiển thị phần trăm
            if (labelTMP != null && !string.IsNullOrEmpty(holdingLabel))
            {
                int percent = Mathf.RoundToInt(progress * 100f);
                labelTMP.text = $"{holdingLabel} ({percent}%)";
            }

            // 4. Kích hoạt khi đạt đủ 100% thời gian giữ
            if (currentHoldTimer >= holdDuration)
            {
                isTriggered = true;
                TriggerAction();
            }
        }
        else if (!isHolding && currentHoldTimer > 0f)
        {
            // Thả tay trước thời hạn -> Hủy bỏ ngay lập tức, an toàn tuyệt đối
            ResetHoldState();
        }
    }

    private void TriggerAction()
    {
        Debug.Log("<color=cyan>[HoldToActivateButton]</color> Đã giữ đủ 1.25s -> KÍCH HOẠT HÀNH ĐỘNG: " + defaultLabel);

        if (progressRingImage != null) progressRingImage.fillAmount = 1f;

        if (labelTMP != null) labelTMP.text = "XONG!";

        StartCoroutine(CompletionBounce());
    }

    private IEnumerator CompletionBounce()
    {
        // Nảy nhẹ phản hồi thị giác
        if (buttonRect != null)
        {
            buttonRect.localScale = originalScale * 1.15f;
            yield return new WaitForSeconds(0.12f);
            buttonRect.localScale = originalScale;
        }

        // Thực hiện hành động chính
        onHoldComplete?.Invoke();

        yield return new WaitForSeconds(0.35f);

        ResetHoldState();
    }

    public void ResetHoldState()
    {
        currentHoldTimer = 0f;
        isTriggered = false;

        if (progressRingImage != null)
        {
            progressRingImage.fillAmount = 0f;
            progressRingImage.gameObject.SetActive(false);
        }

        if (buttonRect != null)
        {
            buttonRect.localScale = originalScale;
        }

        if (labelTMP != null && !string.IsNullOrEmpty(defaultLabel))
        {
            labelTMP.text = defaultLabel;
        }
    }

    // Touchscreen / Mouse Handlers
    public void OnPointerDown(PointerEventData eventData) => isPointerDown = true;
    public void OnPointerUp(PointerEventData eventData) => isPointerDown = false;
    public void OnPointerExit(PointerEventData eventData) => isPointerDown = false;

    void OnDisable()
    {
        ResetHoldState();
    }
}
