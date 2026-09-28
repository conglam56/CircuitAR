using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class InteractionDiagnosticHUD : MonoBehaviour
{
    [Header("Cấu hình kiểm tra")]
    public Camera arCamera;
    public LayerMask layerMask = ~0;

    // Các biến lưu vết chẩn đoán để hiển thị lên màn hình
    private string lastInputInfo = "Chưa có thao tác chạm";
    private string lastUIStatus = "Chưa kiểm tra";
    private string lastRaycastHits = "0 đối tượng";
    private string lastDetectedComponent = "Chưa tìm thấy";
    private string lastExecutionStatus = "Chưa kích hoạt";
    private Color statusColor = Color.white;

    void Awake()
    {
        if (arCamera == null) arCamera = Camera.main;
    }

    void Update()
    {
        Vector2 inputPos = Vector2.zero;
        bool hasTouch = false;
        int fingerId = -1;

        // --- TRẠM 1: KIỂM TRA INPUT ---
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                inputPos = t.position;
                fingerId = t.fingerId;
                hasTouch = true;
                lastInputInfo = $"Touch tại: ({inputPos.x:F0}, {inputPos.y:F0})";
            }
        }
        else if (Input.GetMouseButtonDown(0))
        {
            inputPos = Input.mousePosition;
            hasTouch = true;
            lastInputInfo = $"Click chuột tại: ({inputPos.x:F0}, {inputPos.y:F0})";
        }

        if (!hasTouch) return;

        // Reset trạng thái kiểm tra cho lượt chạm mới
        lastUIStatus = "An toàn (Không bị UI chặn)";
        lastRaycastHits = "0";
        lastDetectedComponent = "Không";
        lastExecutionStatus = "Dừng";
        statusColor = Color.yellow;

        // --- TRẠM 2: KIỂM TRA XUNG ĐỘT UI (EventSystem / Raycast Target) ---
        if (EventSystem.current != null)
        {
            PointerEventData eventData = new PointerEventData(EventSystem.current) { position = inputPos };
            List<RaycastResult> uiHits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, uiHits);

            if (uiHits.Count > 0)
            {
                // Liệt kê tên UI GameObject chặn tia
                string uiBlockerName = uiHits[0].gameObject.name;
                lastUIStatus = $"BỊ CHẶN BỞI UI: [{uiBlockerName}]";
                statusColor = Color.red;
                Debug.LogWarning($"[Diagnostic] Chạm bị chặn bởi phần tử UI: {uiBlockerName}");
                return; // Nếu bị UI chặn, chuỗi dừng tại đây!
            }
        }

        // --- TRẠM 3: KIỂM TRA BẮN TIA RAYCAST VẬT LÝ 3D ---
        if (arCamera == null)
        {
            lastRaycastHits = "LỖI: Camera bị NULL!";
            statusColor = Color.red;
            return;
        }

        Ray ray = arCamera.ScreenPointToRay(inputPos);
        Debug.DrawRay(ray.origin, ray.direction * 5f, Color.green, 2f);

        RaycastHit[] hits = Physics.RaycastAll(ray, 50f, layerMask, QueryTriggerInteraction.Collide);
        lastRaycastHits = $"{hits.Length} đối tượng trúng tia";

        if (hits.Length == 0)
        {
            statusColor = Color.red;
            lastExecutionStatus = "Trượt Collider (Chưa chạm trúng linh kiện)";
            return;
        }

        // --- TRẠM 4: KIỂM TRA CẤU TRÚC COMPONENT ---
        SwitchComponent foundSwitch = null;
        string hitDetails = "";

        foreach (var hit in hits)
        {
            hitDetails += hit.collider.gameObject.name + " | ";
            foundSwitch = hit.collider.GetComponentInParent<SwitchComponent>();
            if (foundSwitch == null)
                foundSwitch = hit.collider.GetComponentInChildren<SwitchComponent>();

            if (foundSwitch != null) break;
        }

        lastRaycastHits = $"Trúng {hits.Length} mục: [{hitDetails}]";

        if (foundSwitch != null)
        {
            lastDetectedComponent = $"Tìm thấy SwitchComponent trên: {foundSwitch.gameObject.name}";
            
            // --- TRẠM 5: KIỂM TRA THỰC THI HÀM VÀ CHẾ ĐỘ ---
            if (CircuitSimulationManager.Instance != null && !CircuitSimulationManager.Instance.isSimulationMode)
            {
                lastExecutionStatus = "Từ chối: Chưa bật chế độ Mô phỏng (isSimulationMode = False)";
                statusColor = Color.orange;
            }
            else
            {
                lastExecutionStatus = "THÀNH CÔNG: Đã gọi ToggleSwitch()";
                statusColor = Color.green;
                foundSwitch.ToggleSwitch();
            }
        }
        else
        {
            lastDetectedComponent = "Trúng Collider nhưng KHÔNG tìm thấy script SwitchComponent!";
            statusColor = Color.red;
        }
    }

    // Hiển thị trực tiếp bảng chẩn đoán lên màn hình điện thoại
    void OnGUI()
    {
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.fontSize = 24;
        boxStyle.alignment = TextAnchor.UpperLeft;
        boxStyle.normal.textColor = statusColor;

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 22;
        labelStyle.normal.textColor = Color.white;

        GUILayout.BeginArea(new Rect(20, 40, Screen.width - 40, 260), boxStyle);
        GUILayout.Label("=== BẢNG CHẨN ĐOÁN TƯƠNG TÁC AR ===", labelStyle);
        GUILayout.Label($"1. Thao tác Input: {lastInputInfo}", labelStyle);
        GUILayout.Label($"2. Kiểm tra UI chặn: {lastUIStatus}", labelStyle);
        GUILayout.Label($"3. Physics Raycast: {lastRaycastHits}", labelStyle);
        GUILayout.Label($"4. Tìm Component: {lastDetectedComponent}", labelStyle);
        GUILayout.Label($"5. Trạng thái chạy: {lastExecutionStatus}", labelStyle);
        GUILayout.EndArea();
    }
}