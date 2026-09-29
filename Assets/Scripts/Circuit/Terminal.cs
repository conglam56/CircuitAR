using System.Collections.Generic;
using UnityEngine;

public enum TerminalPolarity
{
    Neutral,    // Cực không phân biệt (Đèn, Công tắc)
    Positive,   // Cực dương (+) (Pin, Ampe kế, Vôn kế)
    Negative    // Cực âm (-) (Pin, Ampe kế, Vôn kế)
}

public class Terminal : MonoBehaviour
{
    [Header("--- THÔNG TIN ĐẦU CỰC ---")]
    [Tooltip("Linh kiện cha sở hữu đầu cực này")]
    public CircuitComponent parentComponent;

    [Tooltip("Phân cực của đầu cực (+ / - / Neutral)")]
    public TerminalPolarity polarity = TerminalPolarity.Neutral;

    [Tooltip("Tên định danh cực (vd: Pin_Anode, Light_TermA)")]
    public string terminalID;

    [Header("--- KẾT NỐI HIỆN TẠI ---")]
    // Danh sách các cực khác đang được nối tới cực này qua dây dẫn
    public List<Terminal> connectedTerminals = new List<Terminal>();

    // Điện thế tương đối tại nút này (đơn vị: Volt) - được cập nhật sau khi giải mạch
    [HideInInspector] public float electricPotential = 0f;

    void Awake()
    {
        if (parentComponent == null)
        {
            parentComponent = GetComponentInParent<CircuitComponent>();
        }
    }

    /// <summary>
    /// Gắn dây kết nối từ cực này tới cực đích
    /// </summary>
    public void ConnectTo(Terminal other)
    {
        if (other == null || other == this) return;

        if (!connectedTerminals.Contains(other))
        {
            connectedTerminals.Add(other);
        }
        if (!other.connectedTerminals.Contains(this))
        {
            other.connectedTerminals.Add(this);
        }

        // Báo cho linh kiện cha biết vừa có dây nối mới
        parentComponent?.NotifyConnectionChanged();
    }

    /// <summary>
    /// Ngắt kết nối với cực đích khi tháo hoặc xóa dây
    /// </summary>
    public void DisconnectFrom(Terminal other)
    {
        if (other == null) return;

        if (connectedTerminals.Contains(other))
        {
            connectedTerminals.Remove(other);
        }
        if (other.connectedTerminals.Contains(this))
        {
            other.connectedTerminals.Remove(this);
        }

        parentComponent?.NotifyConnectionChanged();
    }

    /// <summary>
    /// Kiểm tra cực này đã có dây nối vào hay chưa
    /// </summary>
    public bool IsConnected => connectedTerminals.Count > 0;
}