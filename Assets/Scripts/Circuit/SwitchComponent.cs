using System.Collections;
using UnityEngine;

public class SwitchComponent : MonoBehaviour
{
    [Header("Cấu hình Cần gạt")]
    [Tooltip("Kéo Transform của thanh cần gạt vào đây")]
    public Transform leverTransform;
    [Tooltip("Góc xoay khi MỞ (nhấc lên)")]
    public Vector3 openRotationAngles = new Vector3(0f, 0f, 30f);
    [Tooltip("Góc xoay khi ĐÓNG (chạm chốt tiếp điểm)")]
    public Vector3 closedRotationAngles = new Vector3(0f, 0f, 0f);

    [Header("Thông số điện học mô phỏng")]
    public float resistanceOpen = 100000000f; // 10^8 Ohm (Hở mạch)
    public float resistanceClosed = 0.001f;   // 0.001 Ohm (Đóng mạch)
    public float currentResistance = 100000000f;

    [Header("Trạng thái")]
    public bool isClosed = false;
    public float rotateDuration = 0.18f;

    private bool isRotating = false;
    private Coroutine rotateCoroutine = null;

    void Start()
    {
        // Khởi tạo vị trí cần gạt và điện trở ban đầu
        if (leverTransform != null)
        {
            leverTransform.localRotation = Quaternion.Euler(isClosed ? closedRotationAngles : openRotationAngles);
        }
        currentResistance = isClosed ? resistanceClosed : resistanceOpen;
    }

    public void ToggleSwitch()
    {
        // Khóa debounce: Đang trong quá trình xoay thì không nhận lệnh đè
        if (isRotating) return;

        isClosed = !isClosed;
        currentResistance = isClosed ? resistanceClosed : resistanceOpen;

        if (rotateCoroutine != null) StopCoroutine(rotateCoroutine);

        Vector3 targetAngles = isClosed ? closedRotationAngles : openRotationAngles;
        rotateCoroutine = StartCoroutine(AnimateLever(Quaternion.Euler(targetAngles)));

        Debug.Log($"<color=cyan>[SwitchComponent]</color> Khóa K -> {(isClosed ? "ĐÓNG (Kín mạch)" : "MỞ (Hở mạch)")}, R = {currentResistance} Ohm");
    }

    private IEnumerator AnimateLever(Quaternion targetRot)
    {
        if (leverTransform == null) yield break;

        isRotating = true;
        Quaternion startRot = leverTransform.localRotation;
        float elapsed = 0f;

        while (elapsed < rotateDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / rotateDuration);
            // Làm mượt chuyển động xoay
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            leverTransform.localRotation = Quaternion.Slerp(startRot, targetRot, smoothT);
            yield return null;
        }

        leverTransform.localRotation = targetRot;
        isRotating = false;
    }

    // LƯU Ý QUAN TRỌNG: Tuyệt đối KHÔNG viết hàm OnMouseDown() ở đây
    // để tránh bị xung đột kích hoạt 2 lần với SwitchInteractor.
}