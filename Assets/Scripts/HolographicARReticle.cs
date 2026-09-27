using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HolographicARReticle - Tâm ngắm Hologram AR công nghệ cao (Chuẩn Apple Measure / Sci-Fi AR):
/// 1. Nằm phẳng trong World-Space áp sát bề mặt bàn nhận diện (Vector3.up).
/// 2. Khung ngắm đa lớp: Vòng ngoài xoay mượt, vòng trong nhịp thở, tâm ngắm Laser, vòng sóng xung kích Sonar lan tỏa.
/// 3. Bộ lọc làm mượt Lerp vị trí để triệt tiêu hiện tượng giật cục từ camera ARCore SLAM.
/// 4. 100% Procedural Graphics: Tự động sinh texture sắc nét trong bộ nhớ, không phụ thuộc asset ngoài.
/// </summary>
public class HolographicARReticle : MonoBehaviour
{
    private static Sprite reticleRingSprite;
    private static Sprite reticleDotSprite;
    private static Sprite sonarRingSprite;

    private Canvas reticleCanvas;
    private CanvasGroup canvasGroup;
    private RectTransform outerRingRT;
    private RectTransform innerRingRT;
    private RectTransform sonarRingRT;
    private Image sonarImage;
    private Image outerRingImg;
    private Image innerRingImg;
    private Image centerDotImg;

    private Vector3 targetWorldPos;
    private Quaternion targetWorldRot = Quaternion.Euler(90f, 0f, 0f);
    private bool isVisible = false;
    private float currentAlpha = 0f;
    private float sonarTimer = 0f;

    public float positionSmoothing = 20f;
    public float rotationSpeed = 35f;

    public static HolographicARReticle Create(Transform parent = null)
    {
        EnsureSprites();

        GameObject root = new GameObject("HolographicAR_Reticle");
        if (parent != null) root.transform.SetParent(parent, false);

        HolographicARReticle instance = root.AddComponent<HolographicARReticle>();
        instance.BuildUI();
        return instance;
    }

    private static void EnsureSprites()
    {
        if (reticleRingSprite == null) reticleRingSprite = GenerateReticleRingSprite(256);
        if (reticleDotSprite == null) reticleDotSprite = GenerateDotSprite(64);
        if (sonarRingSprite == null) sonarRingSprite = GenerateSonarRingSprite(256);
    }

    private void BuildUI()
    {
        EnsureSprites();

        // 1. WorldSpace Canvas với kích thước chuẩn AR (0.001 scale)
        reticleCanvas = gameObject.AddComponent<Canvas>();
        reticleCanvas.renderMode = RenderMode.WorldSpace;
        reticleCanvas.sortingOrder = 50;

        RectTransform rootRT = GetComponent<RectTransform>();
        rootRT.sizeDelta = new Vector2(300, 300);
        rootRT.localScale = Vector3.one * 0.0015f; // ~45cm đường kính trên bàn thật

        canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        // 2. Sóng quét Sonar Ripple (Vòng dưới cùng lan tỏa)
        GameObject sonarObj = new GameObject("Sonar_Wave", typeof(RectTransform), typeof(Image));
        sonarObj.transform.SetParent(transform, false);
        sonarRingRT = sonarObj.GetComponent<RectTransform>();
        sonarRingRT.sizeDelta = new Vector2(280, 280);
        sonarImage = sonarObj.GetComponent<Image>();
        sonarImage.sprite = sonarRingSprite;
        sonarImage.color = new Color(0f, 0.94f, 1f, 0.4f);
        sonarImage.raycastTarget = false;

        // 3. Vòng ngoài có 4 ngoặc ngắm (Outer Rotating Brackets)
        GameObject outerObj = new GameObject("Outer_Ring", typeof(RectTransform), typeof(Image));
        outerObj.transform.SetParent(transform, false);
        outerRingRT = outerObj.GetComponent<RectTransform>();
        outerRingRT.sizeDelta = new Vector2(250, 250);
        outerRingImg = outerObj.GetComponent<Image>();
        outerRingImg.sprite = reticleRingSprite;
        outerRingImg.color = new Color(0f, 0.95f, 1f, 0.95f);
        outerRingImg.raycastTarget = false;

        // 4. Vòng trong thở nhẹ (Inner Breathing Ring)
        GameObject innerObj = new GameObject("Inner_Ring", typeof(RectTransform), typeof(Image));
        innerObj.transform.SetParent(transform, false);
        innerRingRT = innerObj.GetComponent<RectTransform>();
        innerRingRT.sizeDelta = new Vector2(150, 150);
        innerRingImg = innerObj.GetComponent<Image>();
        innerRingImg.sprite = reticleDotSprite;
        innerRingImg.color = new Color(0.2f, 0.8f, 1f, 0.5f);
        innerRingImg.raycastTarget = false;

        // 5. Chấm sáng trung tâm (Laser Center Dot)
        GameObject dotObj = new GameObject("Center_Dot", typeof(RectTransform), typeof(Image));
        dotObj.transform.SetParent(transform, false);
        RectTransform dotRT = dotObj.GetComponent<RectTransform>();
        dotRT.sizeDelta = new Vector2(22, 22);
        centerDotImg = dotObj.GetComponent<Image>();
        centerDotImg.sprite = reticleDotSprite;
        centerDotImg.color = Color.white;
        centerDotImg.raycastTarget = false;

        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        gameObject.SetActive(false);
    }

    void Update()
    {
        // 1. Fade Alpha mượt mà
        float targetAlpha = isVisible ? 1f : 0f;
        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.deltaTime * 6f);
        if (canvasGroup != null) canvasGroup.alpha = currentAlpha;

        if (currentAlpha <= 0.001f && !isVisible)
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }

        // 2. Làm mượt vị trí bám mặt bàn (chống rung lắc micro-jitter)
        transform.position = Vector3.Lerp(transform.position, targetWorldPos, 1f - Mathf.Exp(-positionSmoothing * Time.deltaTime));
        transform.rotation = targetWorldRot;

        // 3. Xoay vòng ngoài công nghệ
        if (outerRingRT != null)
        {
            outerRingRT.Rotate(0f, 0f, -rotationSpeed * Time.deltaTime);
        }

        // 4. Nhịp thở vòng trong
        if (innerRingRT != null)
        {
            float pulse = 1f + 0.06f * Mathf.Sin(Time.time * 4f);
            innerRingRT.localScale = new Vector3(pulse, pulse, 1f);
        }

        // 5. Hiệu ứng sóng Sonar mở rộng và tan biến
        sonarTimer += Time.deltaTime;
        float sonarPeriod = 1.4f;
        float sonarProgress = (sonarTimer % sonarPeriod) / sonarPeriod; // 0 -> 1

        if (sonarRingRT != null && sonarImage != null)
        {
            float scale = Mathf.Lerp(0.5f, 1.35f, sonarProgress);
            sonarRingRT.localScale = new Vector3(scale, scale, 1f);

            float alpha = Mathf.Sin(sonarProgress * Mathf.PI) * 0.45f;
            Color c = sonarImage.color;
            c.a = alpha * currentAlpha;
            sonarImage.color = c;
        }
    }

    public void SetTargetPose(Vector3 position, Quaternion rotation)
    {
        targetWorldPos = position;
        targetWorldRot = rotation;

        if (!isVisible)
        {
            isVisible = true;
            gameObject.SetActive(true);
            transform.position = position;
            transform.rotation = rotation;
        }
    }

    public void Show()
    {
        isVisible = true;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    public void Hide()
    {
        isVisible = false;
    }

    // =========================================================================
    // TẠO SPRITE PROCEDURAL SẮC NÉT CHO RETICLE
    // =========================================================================

    private static Sprite GenerateReticleRingSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.46f;
        float innerRadius = size * 0.41f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = y * size + x;
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);

                // Góc tính theo độ để cắt 4 khe ngoặc ngắm
                float angle = Mathf.Atan2(y + 0.5f - center.y, x + 0.5f - center.x) * Mathf.Rad2Deg;
                if (angle < 0) angle += 360f;

                // Cắt 4 khe hở tại 0°, 90°, 180°, 270° (độ mở 22 độ mỗi khe)
                float modAngle = angle % 90f;
                bool isGap = (modAngle > 39f && modAngle < 51f);

                if (d >= innerRadius && d <= radius && !isGap)
                {
                    // Làm mịn viền antialiasing
                    float edgeDist = Mathf.Min(d - innerRadius, radius - d);
                    float a = Mathf.Clamp01(edgeDist * 1.5f);
                    colors[idx] = new Color(1f, 1f, 1f, a);
                }
                else
                {
                    colors[idx] = Color.clear;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static Sprite GenerateDotSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.48f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = y * size + x;
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                if (d <= radius)
                {
                    float a = Mathf.Clamp01((radius - d) * 2f);
                    colors[idx] = new Color(1f, 1f, 1f, a);
                }
                else
                {
                    colors[idx] = Color.clear;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static Sprite GenerateSonarRingSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.48f;
        float thickness = size * 0.04f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = y * size + x;
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float distFromRing = Mathf.Abs(d - radius);
                if (distFromRing <= thickness)
                {
                    float a = Mathf.Clamp01(1f - (distFromRing / thickness));
                    colors[idx] = new Color(1f, 1f, 1f, a * a);
                }
                else
                {
                    colors[idx] = Color.clear;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
