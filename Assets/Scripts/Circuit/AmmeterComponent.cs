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
        // Giữ nguyên dấu để biết dòng đi xuôi hay ngược
        bool isReversePolarity = currentPassing < -0.001f;
        float absVal = Mathf.Abs(currentPassing);

        if (readingText != null)
        {
            if (isReversePolarity)
            {
                // Hiển thị số âm cảnh báo mắc ngược cực
                readingText.text = $"-{absVal:F2} A";
                readingText.color = Color.red; // Đổi chữ sang màu đỏ cảnh báo
            }
            else
            {
                readingText.text = absVal > 0.001f ? $"{absVal:F2} A" : "0.00 A";
                readingText.color = Color.black;
            }
        }

        if (needleTransform != null)
        {
            if (isReversePolarity)
            {
                // Nếu mắc ngược cực, kim lệch ngược về bên trái vạch 0
                needleTransform.localRotation = Quaternion.Euler(0, 0, needleMinAngle + 15f);
            }
            else
            {
                float t = Mathf.Clamp01(absVal / maxRange);
                float angle = Mathf.Lerp(needleMinAngle, needleMaxAngle, t);
                needleTransform.localRotation = Quaternion.Euler(0, 0, angle);
            }
        }
    }
}