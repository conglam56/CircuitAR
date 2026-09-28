using System;
using UnityEngine;

public enum ComponentType
{
    Battery,    // Nguồn điện (Pin)
    Bulb,       // Bóng đèn
    Switch,     // Công tắc (Khóa K)
    Ammeter,    // Ampe kế
    Voltmeter   // Vôn kế
}

public abstract class CircuitComponent : MonoBehaviour
{
    [Header("--- THÔNG TIN LINH KIỆN CƠ BẢN ---")]
    public ComponentType componentType;
    public string componentName = "LinhKien";

    [Header("--- ĐẦU CỰC LIÊN KẾT (2 CỰC) ---")]
    [Tooltip("Cực 1 (hoặc Cực Dương nếu có phân cực)")]
    public Terminal terminalA;

    [Tooltip("Cực 2 (hoặc Cực Âm nếu có phân cực)")]
    public Terminal terminalB;

    [Header("--- THÔNG SỐ ĐIỆN VẬT LÝ ---")]
    [Tooltip("Điện trở nội tại của linh kiện (Ohm)")]
    public float resistance = 1.0f;

    [Tooltip("Suất điện động E nếu là nguồn (Volt)")]
    public float voltageSource = 0f;

    [Header("--- THÔNG SỐ ĐO ĐẠC THỰC THỜI (OUTPUT) ---")]
    public float currentPassing = 0f;      // Dòng điện I qua linh kiện (A)
    public float voltageDrop = 0f;         // Hiệu điện thế rơi U = Va - Vb (V)

    // Event thông báo khi cấu trúc dây nối hoặc trạng thái linh kiện thay đổi
    public static event Action OnCircuitGraphDirty;

    protected virtual void Awake()
    {
        // Tự động tìm terminal nếu chưa gán trong Inspector
        if (terminalA == null || terminalB == null)
        {
            Terminal[] terms = GetComponentsInChildren<Terminal>();
            if (terms.Length >= 2)
            {
                if (terminalA == null) terminalA = terms[0];
                if (terminalB == null) terminalB = terms[1];
            }
        }

        if (terminalA != null) terminalA.parentComponent = this;
        if (terminalB != null) terminalB.parentComponent = this;
    }

    /// <summary>
    /// Kiểm tra cả 2 cực của linh kiện đã được nối dây đầy đủ chưa
    /// </summary>
    public virtual bool IsFullyConnected()
    {
        return terminalA != null && terminalB != null && terminalA.IsConnected && terminalB.IsConnected;
    }

    /// <summary>
    /// Lấy cực đối diện trong cùng một linh kiện
    /// </summary>
    public Terminal GetOppositeTerminal(Terminal current)
    {
        if (current == terminalA) return terminalB;
        if (current == terminalB) return terminalA;
        return null;
    }

    /// <summary>
    /// Nhận thông số tính toán từ Bộ giải mạch điện (Solver)
    /// </summary>
    public virtual void ApplyElectricalState(float current, float uDrop)
    {
        currentPassing = current;
        voltageDrop = uDrop;
        UpdateVisuals();
    }

    /// <summary>
    /// Đặt lại trạng thái khi mạch hở hoặc reset
    /// </summary>
    public virtual void ResetElectricalState()
    {
        currentPassing = 0f;
        voltageDrop = 0f;
        UpdateVisuals();
    }

    /// <summary>
    /// Cập nhật hiển thị trực quan riêng của từng loại linh kiện (đèn sáng, kim quay, v.v.)
    /// </summary>
    public abstract void UpdateVisuals();

    public void NotifyConnectionChanged()
    {
        OnCircuitGraphDirty?.Invoke();
    }
}