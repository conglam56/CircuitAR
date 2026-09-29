using UnityEngine;

public class BatteryComponent : CircuitComponent
{
    [Header("--- CẤU HÌNH PIN ---")]
    [Tooltip("Hiệu điện thế danh định của pin (V)")]
    public float batteryVoltage = 9.0f; // hoặc 1.5V, 4.5V tùy bài tập
    public float internalResistance = 0.5f;

    protected override void Awake()
    {
        componentType = ComponentType.Battery;
        voltageSource = batteryVoltage;
        resistance = internalResistance;
        base.Awake();

        if (terminalA != null) terminalA.polarity = TerminalPolarity.Positive;
        if (terminalB != null) terminalB.polarity = TerminalPolarity.Negative;
    }

    public override void UpdateVisuals()
    {
        // Pin không cần hiệu ứng chuyển động phức tạp
    }
}