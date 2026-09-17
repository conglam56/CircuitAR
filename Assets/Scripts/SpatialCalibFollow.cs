using UnityEngine;

public class SpatialCalibFollow : MonoBehaviour
{
    [Header("Khoảng cách trước mắt")]
    public float distance = 0.5f;       // Cách camera 50cm
    public float heightOffset = -0.1f;   // Hạ thấp 10cm dưới tầm mắt để không che tâm ngắm
    public float followSpeed = 5f;

    private Transform camTransform;

    void Start()
    {
        if (Camera.main != null) camTransform = Camera.main.transform;
    }

    void LateUpdate()
    {
        if (camTransform == null)
        {
            if (Camera.main != null) camTransform = Camera.main.transform;
            return;
        }

        // Luôn định vị nút lơ lửng ngay trước camera, không phụ thuộc điều kiện mặt phẳng
        Vector3 targetPos = camTransform.position
                          + (camTransform.forward * distance)
                          + (camTransform.up * heightOffset);

        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * followSpeed);

        // Luôn xoay mặt nút hướng thẳng về mắt người dùng
        Vector3 lookDir = transform.position - camTransform.position;
        if (lookDir != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(lookDir);
        }
    }
}