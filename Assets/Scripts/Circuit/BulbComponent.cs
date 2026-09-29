using UnityEngine;

public class BulbComponent : CircuitComponent
{
    [Header("--- THÔNG SỐ ĐÈN ---")]
    public float bulbResistance = 10.0f; // R đèn
    public float minLightingCurrent = 0.05f; // Ngưỡng dòng điện tối thiểu để sáng (A)

    [Header("--- THÀNH PHẦN HIỂN THỊ (VISUALS) ---")]
    public MeshRenderer bulbGlassRenderer;
    public Light pointLightSource;
    [ColorUsage(true, true)] public Color emissionColor = new Color(1f, 0.85f, 0.3f, 1f);

    private Material bulbMaterialInstance;

    protected override void Awake()
    {
        componentType = ComponentType.Bulb;
        resistance = bulbResistance;
        voltageSource = 0f;
        base.Awake();

        if (bulbGlassRenderer != null)
        {
            bulbMaterialInstance = bulbGlassRenderer.material;
        }

        if (pointLightSource != null)
        {
            pointLightSource.enabled = false;
        }
    }

    public override void UpdateVisuals()
    {
        bool isLit = Mathf.Abs(currentPassing) >= minLightingCurrent;

        // Bật/tắt nguồn sáng PointLight
        if (pointLightSource != null)
        {
            pointLightSource.enabled = isLit;
            if (isLit)
            {
                // Cường độ sáng tỷ lệ theo dòng điện
                pointLightSource.intensity = Mathf.Clamp(Mathf.Abs(currentPassing) * 2.0f, 0.5f, 3.5f);
            }
        }

        // Cập nhật hiệu ứng phát sáng Emission trên bóng đèn
        if (bulbMaterialInstance != null)
        {
            if (isLit)
            {
                bulbMaterialInstance.EnableKeyword("_EMISSION");
                float intensity = Mathf.Clamp(Mathf.Abs(currentPassing) * 1.5f, 0.2f, 2.5f);
                bulbMaterialInstance.SetColor("_EmissionColor", emissionColor * intensity);
            }
            else
            {
                bulbMaterialInstance.DisableKeyword("_EMISSION");
                bulbMaterialInstance.SetColor("_EmissionColor", Color.black);
            }
        }
    }
}