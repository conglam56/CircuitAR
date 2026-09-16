using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class SpatialMenuFollow : MonoBehaviour
{
    [Header("Khoảng cách phía trước Camera (mét)")]
    public float distance = 0.5f;

    [Header("Độ lệch chiều cao (âm để hạ thấp dưới tầm mắt)")]
    public float heightOffset = -0.15f;

    [Header("Tốc độ bám theo")]
    public float followSpeed = 3f;

    [Header("Góc nghiêng bổ sung trục X của Menu (độ)")]
    [Tooltip("Dương: ngửa mặt menu lên trời; Âm: chúc mặt menu xuống")]
    public float menuPitchX = 25f;

    [Header("Điều kiện kích hoạt theo góc cúi (độ)")]
    [Tooltip("Chỉ hiện khi camera cúi xuống quá góc này (mặc định 45 độ)")]
    public float pitchAngleThreshold = 45f;

    [Header("Tốc độ ẩn/hiện mượt")]
    public float fadeSpeed = 8f;

    private Transform camTransform;
    private CanvasGroup canvasGroup;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    void Start()
    {
        if (Camera.main != null)
        {
            camTransform = Camera.main.transform;
        }
    }

    void LateUpdate()
    {
        if (camTransform == null)
        {
            if (Camera.main != null) camTransform = Camera.main.transform;
            return;
        }

        // 1. Lấy góc cúi/ngửa (Pitch) thực tế của Camera
        // Trong Unity: Camera chúc xuống đất thì góc X dương (0 đến 90 độ)
        float currentCamPitch = camTransform.eulerAngles.x;
        if (currentCamPitch > 180f) currentCamPitch -= 360f; // Chuẩn hóa về khoảng [-180, 180]

        // 2. Kiểm tra điều kiện: Camera phải chúc xuống > 45 độ mới cho phép hiện
        bool shouldShow = currentCamPitch >= pitchAngleThreshold;

        // 3. Hiệu ứng làm mờ dần và bật/tắt nhận diện tương tác cử chỉ
        float targetAlpha = shouldShow ? 1f : 0f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, Time.deltaTime * fadeSpeed);
        canvasGroup.interactable = shouldShow;
        canvasGroup.blocksRaycasts = shouldShow;

        // Nếu menu đang ẩn hoàn toàn thì không tốn hiệu năng tính toán vị trí/xoay
        if (canvasGroup.alpha <= 0.001f) return;

        // 4. Cập nhật vị trí mục tiêu phía trước camera
        Vector3 targetPosition = camTransform.position
                               + (camTransform.forward * distance)
                               + (camTransform.up * heightOffset);

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * followSpeed);

        // 5. Xoay menu hướng về phía mắt người dùng và cộng thêm góc nghiêng X tùy chỉnh
        Vector3 lookDirection = transform.position - camTransform.position;
        if (lookDirection != Vector3.zero)
        {
            Quaternion baseRotation = Quaternion.LookRotation(lookDirection);
            // Xoay thêm trục X cục bộ theo thông số menuPitchX
            transform.rotation = baseRotation * Quaternion.Euler(menuPitchX, 0f, 0f);
        }
    }
}