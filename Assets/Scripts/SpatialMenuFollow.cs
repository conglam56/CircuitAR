using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class SpatialMenuFollow : MonoBehaviour
{
    [Header("Cự ly")]
    public float distance = 0.5f;
    public float heightOffset = -0.15f;
    public float followSpeed = 4f;

    [Header("Góc nghiêng bổ sung trục X của Menu (độ)")]
    public float menuPitchX = 25f;

    [Header("Điều kiện kích hoạt theo góc cúi (độ)")]
    public float pitchAngleThreshold = 45f;
    public float fadeSpeed = 8f;

    private Transform camTransform;
    private CanvasGroup canvasGroup;
    private SinglePlaneLockController lockController;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    void Start()
    {
        if (Camera.main != null) camTransform = Camera.main.transform;
        lockController = FindObjectOfType<SinglePlaneLockController>();
    }

    void LateUpdate()
    {
        if (camTransform == null)
        {
            if (Camera.main != null) camTransform = Camera.main.transform;
            return;
        }

        if (lockController == null)
        {
            lockController = FindObjectOfType<SinglePlaneLockController>();
        }

        // ĐIỀU KIỆN 1: Bắt buộc đã khóa cố định mặt bàn thành công
        bool hasPlane = (lockController != null && lockController.HasLockedPlane);

        // ĐIỀU KIỆN 2: Camera phải chúc xuống đất vượt ngưỡng (ví dụ > 45 độ)
        float currentCamPitch = camTransform.eulerAngles.x;
        if (currentCamPitch > 180f) currentCamPitch -= 360f;
        bool isLookingDown = currentCamPitch >= pitchAngleThreshold;

        // Chỉ hiện khi thỏa mãn CẢ HAI điều kiện
        bool shouldShow = hasPlane && isLookingDown;

        float targetAlpha = shouldShow ? 1f : 0f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, Time.deltaTime * fadeSpeed);
        canvasGroup.interactable = shouldShow;
        canvasGroup.blocksRaycasts = shouldShow;

        if (canvasGroup.alpha <= 0.001f) return;

        // Bám theo tầm nhìn khi cúi xuống
        Vector3 targetPosition = camTransform.position
                               + (camTransform.forward * distance)
                               + (camTransform.up * heightOffset);

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * followSpeed);

        Vector3 lookDirection = transform.position - camTransform.position;
        if (lookDirection != Vector3.zero)
        {
            Quaternion baseRotation = Quaternion.LookRotation(lookDirection);
            transform.rotation = baseRotation * Quaternion.Euler(menuPitchX, 0f, 0f);
        }
    }
}