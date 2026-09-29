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
        // Xác định xem điện áp rơi từ cực A sang cực B có bị âm không
        bool isReversePolarity = voltageDrop < -0.01f;
        float absVoltage = Mathf.Abs(voltageDrop);

        if (readingText != null)
        {
            if (absVoltage <= 0.01f)
            {
                readingText.text = "0.0 V";
                readingText.color = Color.black;
            }
            else if (isReversePolarity)
            {
                // Hiển thị dấu âm và đổi chữ màu đỏ để cảnh báo mắc ngược cực
                readingText.text = $"-{absVoltage:F1} V";
                readingText.color = Color.red;
            }
            else
            {
                // Mắc đúng cực hiển thị số dương bình thường
                readingText.text = $"{absVoltage:F1} V";
                readingText.color = Color.black;
            }
        }
    }
}