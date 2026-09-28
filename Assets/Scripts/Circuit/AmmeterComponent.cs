using UnityEngine;
using TMPro;

public class AmmeterComponent : CircuitComponent
{
    [Header("--- THIẾT BỊ HIỂN THỊ ---")]
    public TextMeshPro readingText;       // Màn hình số LCD
    public Transform needleTransform;     // Kim đo cơ khí
    public float needleMinAngle = 45f;    // Góc lúc 0A
    public float needleMaxAngle = -45f;   // Góc lúc kịch kim (vd: 3A)
    public float maxRange = 3.0f;         // Thang đo cực đại 3A

    protected override void Awake()
    {
        componentType = ComponentType.Ammeter;
        resistance = 0.005f; // Điện trở Ampe kế rất nhỏ
        voltageSource = 0f;
        base.Awake();

        if (terminalA != null) terminalA.polarity = TerminalPolarity.Positive;
        if (terminalB != null) terminalB.polarity = TerminalPolarity.Negative;
    }

    public override void UpdateVisuals()
    {
        float currentVal = Mathf.Abs(currentPassing);

        if (readingText != null)
        {
            readingText.text = currentVal > 0.001f ? $"{currentVal:F2} A" : "0.00 A";
        }

        if (needleTransform != null)
        {
            float t = Mathf.Clamp01(currentVal / maxRange);
            float angle = Mathf.Lerp(needleMinAngle, needleMaxAngle, t);
            needleTransform.localRotation = Quaternion.Euler(0, 0, angle);
        }
    }
}