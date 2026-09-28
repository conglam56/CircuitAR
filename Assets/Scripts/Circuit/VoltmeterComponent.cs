using UnityEngine;
using TMPro;

public class VoltmeterComponent : CircuitComponent
{
    [Header("--- THIẾT BỊ HIỂN THỊ ---")]
    public TextMeshPro readingText;       // Màn hình số LCD
    public Transform needleTransform;     // Kim đo cơ khí
    public float needleMinAngle = 45f;    // Góc lúc 0V
    public float needleMaxAngle = -45f;   // Góc lúc kịch kim (vd: 12V)
    public float maxRange = 12.0f;        // Thang đo cực đại 12V

    protected override void Awake()
    {
        componentType = ComponentType.Voltmeter;
        resistance = 1e6f; // Điện trở Vôn kế cực lớn (1 MegaOhm)
        voltageSource = 0f;
        base.Awake();

        if (terminalA != null) terminalA.polarity = TerminalPolarity.Positive;
        if (terminalB != null) terminalB.polarity = TerminalPolarity.Negative;
    }

    public override void UpdateVisuals()
    {
        float voltageVal = Mathf.Abs(voltageDrop);

        if (readingText != null)
        {
            readingText.text = voltageVal > 0.01f ? $"{voltageVal:F1} V" : "0.0 V";
        }

        if (needleTransform != null)
        {
            float t = Mathf.Clamp01(voltageVal / maxRange);
            float angle = Mathf.Lerp(needleMinAngle, needleMaxAngle, t);
            needleTransform.localRotation = Quaternion.Euler(0, 0, angle);
        }
    }
}