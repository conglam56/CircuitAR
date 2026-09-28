using System;
using UnityEngine;

public class CircuitSimulationManager : MonoBehaviour
{
    public static CircuitSimulationManager Instance { get; private set; }

    [Header("--- TRẠNG THÁI MÔ PHỎNG ---")]
    [Tooltip("True: Đang ở chế độ Mô phỏng | False: Chế độ Lắp ráp/Chỉnh sửa")]
    public bool isSimulationMode = false;

    // Sự kiện thông báo khi trạng thái mô phỏng thay đổi
    public event Action<bool> OnSimulationModeChanged;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Đảo trạng thái Chế độ Mô phỏng
    /// </summary>
    public void ToggleSimulationMode()
    {
        SetSimulationMode(!isSimulationMode);
    }

    public void SetSimulationMode(bool enable)
    {
        if (isSimulationMode == enable) return;

        isSimulationMode = enable;
        Debug.Log($"<color=yellow>[SimulationManager]</color> Chế độ Mô phỏng: {(isSimulationMode ? "BẬT" : "TẮT")}");

        // Bắn sự kiện ra toàn bộ hệ thống
        OnSimulationModeChanged?.Invoke(isSimulationMode);
    }
}