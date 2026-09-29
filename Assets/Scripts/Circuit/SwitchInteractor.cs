using UnityEngine;
using UnityEngine.EventSystems;

public class SwitchInteractor : MonoBehaviour
{
    [Header("Tham chiếu Hệ thống")]
    public ButtonInteractor buttonInteractor;
    public Camera targetCamera;

    [Header("Cấu hình Raycast")]
    public LayerMask interactableLayer = ~0;
    public float maxDistance = 20f;

    void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (buttonInteractor == null)
        {
#if UNITY_2023_1_OR_NEWER
            buttonInteractor = FindFirstObjectByType<ButtonInteractor>();
#else
            buttonInteractor = FindObjectOfType<ButtonInteractor>();
#endif
        }
    }

    void Update()
    {
        // 1. Chỉ hoạt động trong Chế độ Mô phỏng
        if (CircuitSimulationManager.Instance == null || !CircuitSimulationManager.Instance.isSimulationMode)
        {
            return;
        }

        Vector2 interactionScreenPos = Vector2.zero;
        bool shouldInteract = false;

        // 2. ƯU TIÊN SỐ 1: Bắt cử chỉ Pinch từ MediaPipe (ButtonInteractor)
        if (buttonInteractor != null && buttonInteractor.JustPinched)
        {
            interactionScreenPos = buttonInteractor.CurrentScreenPos;
            shouldInteract = true;
        }
        // 3. FALLBACK: Chạm màn hình hoặc Click chuột trên Unity Editor
        else if (Input.GetMouseButtonDown(0))
        {
            // Bỏ qua nếu chạm vào phần tử UI Canvas
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            interactionScreenPos = Input.mousePosition;
            shouldInteract = true;
        }

        if (shouldInteract)
        {
            ExecuteSwitchRaycast(interactionScreenPos);
        }
    }

    private void ExecuteSwitchRaycast(Vector2 screenPos)
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, interactableLayer, QueryTriggerInteraction.Collide);

        if (hits == null || hits.Length == 0) return;

        // Sắp xếp các điểm chạm theo khoảng cách gần nhất
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            // Tìm SwitchComponent ở chính đối tượng trúng tia, ở Cha hoặc Con
            SwitchComponent sw = hit.collider.GetComponentInParent<SwitchComponent>();
            if (sw == null) sw = hit.collider.GetComponentInChildren<SwitchComponent>();

            if (sw != null)
            {
                Debug.Log($"<color=green>[SwitchInteractor]</color> Tương tác thành công với Khóa K: [{sw.gameObject.name}]");
                sw.ToggleSwitch();
                break; // Chỉ bật/tắt công tắc đầu tiên trúng tia
            }
        }
    }
}