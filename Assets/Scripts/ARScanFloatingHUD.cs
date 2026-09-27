using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ARScanFloatingHUD - HUD chỉ dẫn quét mặt phẳng AR dạng Floating Pill (Chuẩn Dynamic Island / Cyber Glassmorphism):
/// 1. Nằm ở đỉnh màn hình tại cao độ an toàn (Y = -170px), hoàn toàn KHÔNG đè lên nút Quay Lại (Y = -65px).
/// 2. 100% Procedural Vector Icons: Dùng Image Texture2D thay vì ký tự emoji, triệt tiêu 100% lỗi ô vuông (tofu glyph).
/// 3. Icon trạng thái động: Biểu tượng điện thoại quét đung đưa tự nhiên khi tìm mặt bàn, checkmark phát sáng khi bắt được.
/// 4. TextMeshPro sắc nét: Tiêu đề rõ ràng kèm chú thích hướng dẫn người dùng tinh tế.
/// </summary>
public class ARScanFloatingHUD : MonoBehaviour
{
    public static ARScanFloatingHUD Instance { get; private set; }

    private Canvas hudCanvas;
    private CanvasGroup canvasGroup;
    private RectTransform cardRT;
    private Image cardBg;
    private Outline cardOutline;

    private RectTransform iconRT;
    private Image iconGraphicImg;
    private Image iconBadgeImg;

    private TextMeshProUGUI titleTMP;
    private TextMeshProUGUI subtitleTMP;

    private Image pulseDotImg;
    private CanvasGroup pulseDotGroup;

    private bool isVisible = false;
    private float currentAlpha = 0f;
    private ScanHUDState currentState = ScanHUDState.Hidden;

    private static Sprite roundedPillSprite;
    private static Sprite circleSprite;
    private static Sprite scanIconSprite;
    private static Sprite checkIconSprite;
    private static Sprite warningIconSprite;

    public enum ScanHUDState
    {
        Hidden,
        Scanning,
        Detected,
        Warning
    }

    public static ARScanFloatingHUD EnsureInstance()
    {
        if (Instance != null) return Instance;

        GameObject host = new GameObject("ARScanFloatingHUD_Root");
        Instance = host.AddComponent<ARScanFloatingHUD>();
        Instance.BuildUI();
        return Instance;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private static void EnsureSprites()
    {
        if (circleSprite == null) circleSprite = GenerateCircleSprite(64);
        if (roundedPillSprite == null) roundedPillSprite = GenerateRoundedRectSprite(128, 128, 64);
        if (scanIconSprite == null) scanIconSprite = GenerateScanIconSprite(64);
        if (checkIconSprite == null) checkIconSprite = GenerateCheckIconSprite(64);
        if (warningIconSprite == null) warningIconSprite = GenerateWarningIconSprite(64);
    }

    private void BuildUI()
    {
        EnsureSprites();

        // 1. Screen Space Overlay Canvas
        hudCanvas = gameObject.AddComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 2500; // Nằm trên camera AR nhưng dưới Welcome Screen 3000

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0f;

        gameObject.AddComponent<GraphicRaycaster>();

        canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        // 2. Thẻ Card Floating Pill ở đỉnh màn hình:
        // Đặt tại Y = -170px (dưới nút Quay Lại ở Y = -65px -> -145px) để KHÔNG BAO GIỜ bị đè!
        GameObject cardObj = new GameObject("Floating_Pill", typeof(RectTransform), typeof(Image));
        cardObj.transform.SetParent(transform, false);
        cardRT = cardObj.GetComponent<RectTransform>();
        cardRT.anchorMin = new Vector2(0.5f, 1f);
        cardRT.anchorMax = new Vector2(0.5f, 1f);
        cardRT.pivot = new Vector2(0.5f, 1f);
        cardRT.sizeDelta = new Vector2(560, 76);
        cardRT.anchoredPosition = new Vector2(0, -170f);

        cardBg = cardObj.GetComponent<Image>();
        cardBg.sprite = roundedPillSprite;
        cardBg.type = Image.Type.Sliced;
        cardBg.color = new Color(0.04f, 0.07f, 0.14f, 0.92f); // Dark Slate Glass
        cardBg.raycastTarget = false;

        cardOutline = cardObj.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0f, 0.9f, 1f, 0.65f); // Neon Cyan Outline
        cardOutline.effectDistance = new Vector2(1.5f, -1.5f);

        // 3. Icon Badge bên trái
        GameObject iconBadgeObj = new GameObject("Icon_Badge", typeof(RectTransform), typeof(Image));
        iconBadgeObj.transform.SetParent(cardObj.transform, false);
        RectTransform badgeRT = iconBadgeObj.GetComponent<RectTransform>();
        badgeRT.anchorMin = new Vector2(0f, 0.5f);
        badgeRT.anchorMax = new Vector2(0f, 0.5f);
        badgeRT.pivot = new Vector2(0f, 0.5f);
        badgeRT.sizeDelta = new Vector2(48, 48);
        badgeRT.anchoredPosition = new Vector2(14f, 0f);

        iconBadgeImg = iconBadgeObj.GetComponent<Image>();
        iconBadgeImg.sprite = circleSprite;
        iconBadgeImg.color = new Color(0.08f, 0.18f, 0.32f, 0.95f);
        iconBadgeImg.raycastTarget = false;

        // Vector Graphic Icon bên trong badge (Dùng Image sprite thay vì emoji text để triệt tiêu lỗi ô vuông)
        GameObject iconGraphicObj = new GameObject("Icon_Graphic", typeof(RectTransform), typeof(Image));
        iconGraphicObj.transform.SetParent(iconBadgeObj.transform, false);
        iconRT = iconGraphicObj.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 0.5f);
        iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.sizeDelta = new Vector2(28, 28);
        iconRT.anchoredPosition = Vector2.zero;

        iconGraphicImg = iconGraphicObj.GetComponent<Image>();
        iconGraphicImg.sprite = scanIconSprite;
        iconGraphicImg.color = new Color(0f, 0.95f, 1f, 1f);
        iconGraphicImg.raycastTarget = false;

        // 4. Cụm chữ Tiêu đề & Chú thích (ở giữa)
        GameObject textGroup = new GameObject("Text_Group", typeof(RectTransform));
        textGroup.transform.SetParent(cardObj.transform, false);
        RectTransform tgRT = textGroup.GetComponent<RectTransform>();
        tgRT.anchorMin = new Vector2(0f, 0f);
        tgRT.anchorMax = new Vector2(1f, 1f);
        tgRT.offsetMin = new Vector2(74f, 4f);
        tgRT.offsetMax = new Vector2(-42f, -4f);

        // Tiêu đề
        GameObject titleObj = new GameObject("Title", typeof(RectTransform));
        titleObj.transform.SetParent(textGroup.transform, false);
        RectTransform titleRT = titleObj.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 0.5f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.offsetMin = Vector2.zero;
        titleRT.offsetMax = Vector2.zero;

        titleTMP = titleObj.AddComponent<TextMeshProUGUI>();
        titleTMP.font = WelcomeScreenController.GetSafeFont();
        titleTMP.text = "Đang quét tìm mặt bàn...";
        titleTMP.fontSize = 18;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Left;
        titleTMP.color = Color.white;
        titleTMP.raycastTarget = false;

        // Phụ đề
        GameObject subObj = new GameObject("Subtitle", typeof(RectTransform));
        subObj.transform.SetParent(textGroup.transform, false);
        RectTransform subRT = subObj.GetComponent<RectTransform>();
        subRT.anchorMin = new Vector2(0f, 0f);
        subRT.anchorMax = new Vector2(1f, 0.5f);
        subRT.offsetMin = Vector2.zero;
        subRT.offsetMax = Vector2.zero;

        subtitleTMP = subObj.AddComponent<TextMeshProUGUI>();
        subtitleTMP.font = WelcomeScreenController.GetSafeFont();
        subtitleTMP.text = "Lia nhẹ camera quanh bề mặt phẳng";
        subtitleTMP.fontSize = 13;
        subtitleTMP.fontStyle = FontStyles.Normal;
        subtitleTMP.alignment = TextAlignmentOptions.Left;
        subtitleTMP.color = new Color(0.58f, 0.64f, 0.72f, 1f); // Slate-400
        subtitleTMP.raycastTarget = false;

        // 5. Đèn báo tín hiệu Pulse Dot bên phải
        GameObject dotObj = new GameObject("Pulse_Dot", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        dotObj.transform.SetParent(cardObj.transform, false);
        RectTransform dotRT = dotObj.GetComponent<RectTransform>();
        dotRT.anchorMin = new Vector2(1f, 0.5f);
        dotRT.anchorMax = new Vector2(1f, 0.5f);
        dotRT.pivot = new Vector2(1f, 0.5f);
        dotRT.sizeDelta = new Vector2(10, 10);
        dotRT.anchoredPosition = new Vector2(-18f, 0f);

        pulseDotImg = dotObj.GetComponent<Image>();
        pulseDotImg.sprite = circleSprite;
        pulseDotImg.color = new Color(0f, 0.95f, 1f, 1f);
        pulseDotImg.raycastTarget = false;

        pulseDotGroup = dotObj.GetComponent<CanvasGroup>();
        pulseDotGroup.alpha = 1f;

        gameObject.SetActive(false);
    }

    void Update()
    {
        // 1. Fade alpha mượt mà
        float target = isVisible ? 1f : 0f;
        currentAlpha = Mathf.MoveTowards(currentAlpha, target, Time.deltaTime * 6f);
        if (canvasGroup != null) canvasGroup.alpha = currentAlpha;

        if (currentAlpha <= 0.001f && !isVisible)
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }

        // 2. Animation icon khi đang quét (đung đưa góc nhẹ nhàng 12 độ)
        if (currentState == ScanHUDState.Scanning && iconRT != null)
        {
            float tiltAngle = Mathf.Sin(Time.time * 3.5f) * 12f;
            iconRT.localRotation = Quaternion.Euler(0f, 0f, tiltAngle);

            if (pulseDotGroup != null)
            {
                pulseDotGroup.alpha = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.time * 3.5f));
            }
        }
        else
        {
            if (iconRT != null) iconRT.localRotation = Quaternion.identity;
            if (pulseDotGroup != null) pulseDotGroup.alpha = 1f;
        }
    }

    public void ShowScanning(string title = "Đang quét tìm mặt bàn...", string subtitle = "Lia nhẹ camera quanh bề mặt phẳng")
    {
        EnsureReady();
        currentState = ScanHUDState.Scanning;
        isVisible = true;

        if (titleTMP != null)
        {
            titleTMP.text = title;
            titleTMP.color = Color.white;
        }

        if (subtitleTMP != null)
        {
            subtitleTMP.text = subtitle;
            subtitleTMP.color = new Color(0.6f, 0.68f, 0.78f, 1f);
        }

        if (iconGraphicImg != null)
        {
            iconGraphicImg.sprite = scanIconSprite;
            iconGraphicImg.color = new Color(0f, 0.95f, 1f, 1f);
        }

        if (cardOutline != null)
        {
            cardOutline.effectColor = new Color(0f, 0.9f, 1f, 0.7f); // Cyan
        }

        if (pulseDotImg != null)
        {
            pulseDotImg.color = new Color(0f, 0.95f, 1f, 1f);
        }

        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    public void ShowDetected(string title = "Đã tìm thấy mặt bàn!", string subtitle = "Chạm vào màn hình để đặt mạch")
    {
        EnsureReady();
        currentState = ScanHUDState.Detected;
        isVisible = true;

        if (titleTMP != null)
        {
            titleTMP.text = title;
            titleTMP.color = new Color(0.9f, 1f, 0.95f);
        }

        if (subtitleTMP != null)
        {
            subtitleTMP.text = subtitle;
            subtitleTMP.color = new Color(0.2f, 0.92f, 0.75f, 1f); // Mint green
        }

        if (iconGraphicImg != null)
        {
            iconGraphicImg.sprite = checkIconSprite;
            iconGraphicImg.color = new Color(0.15f, 0.95f, 0.6f, 1f);
        }

        if (cardOutline != null)
        {
            cardOutline.effectColor = new Color(0.0f, 0.95f, 0.6f, 0.85f); // Emerald outline
        }

        if (pulseDotImg != null)
        {
            pulseDotImg.color = new Color(0.1f, 0.95f, 0.6f, 1f);
        }

        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    public void ShowWarning(string title = "Chưa bắt được mặt bàn", string subtitle = "Hãy lia camera vào vùng có ánh sáng")
    {
        EnsureReady();
        currentState = ScanHUDState.Warning;
        isVisible = true;

        if (titleTMP != null)
        {
            titleTMP.text = title;
            titleTMP.color = new Color(1f, 0.9f, 0.6f);
        }

        if (subtitleTMP != null)
        {
            subtitleTMP.text = subtitle;
            subtitleTMP.color = new Color(0.98f, 0.75f, 0.2f, 1f); // Amber
        }

        if (iconGraphicImg != null)
        {
            iconGraphicImg.sprite = warningIconSprite;
            iconGraphicImg.color = new Color(0.98f, 0.75f, 0.2f, 1f);
        }

        if (cardOutline != null)
        {
            cardOutline.effectColor = new Color(0.98f, 0.7f, 0.1f, 0.85f); // Amber outline
        }

        if (pulseDotImg != null)
        {
            pulseDotImg.color = new Color(0.98f, 0.75f, 0.2f, 1f);
        }

        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    public void Hide()
    {
        isVisible = false;
        currentState = ScanHUDState.Hidden;
    }

    private void EnsureReady()
    {
        if (hudCanvas == null) BuildUI();
    }

    // =========================================================================
    // HELPER GENERATE SPRITE NỘI BỘ (100% VECTOR PROCEDURAL, KHÔNG CẦN EMOJI FONT)
    // =========================================================================

    private static Sprite GenerateCircleSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.48f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                if (d <= radius)
                {
                    float a = Mathf.Clamp01((radius - d) * 1.5f);
                    colors[y * size + x] = new Color(1f, 1f, 1f, a);
                }
                else
                {
                    colors[y * size + x] = Color.clear;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static Sprite GenerateRoundedRectSprite(int width, int height, int radius)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] colors = new Color[width * height];
        float r = radius;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x + 0.5f - width * 0.5f) - (width * 0.5f - r));
                float dy = Mathf.Max(0, Mathf.Abs(y + 0.5f - height * 0.5f) - (height * 0.5f - r));
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > r)
                {
                    colors[y * width + x] = Color.clear;
                }
                else if (dist > r - 1.5f)
                {
                    colors[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - dist));
                }
                else
                {
                    colors[y * width + x] = Color.white;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        Vector4 border = new Vector4(radius, radius, radius, radius);
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }

    private static Sprite GenerateScanIconSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.clear;

        // Biểu tượng điện thoại quét AR viền bo tròn sắc nét
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool insidePhone = (x >= 20 && x <= 44 && y >= 10 && y <= 54);
                bool phoneBorder = insidePhone && (x <= 23 || x >= 41 || y <= 13 || y >= 51);
                bool phoneScreen = (x >= 24 && x <= 40 && y >= 17 && y <= 47);
                bool topNotch = (x >= 29 && x <= 35 && y >= 49 && y <= 50);

                float dLeft = Vector2.Distance(new Vector2(x, y), new Vector2(32, 32));
                bool leftWave = (dLeft >= 22f && dLeft <= 25.5f && (x <= 16) && y >= 20 && y <= 44);
                bool rightWave = (dLeft >= 22f && dLeft <= 25.5f && (x >= 48) && y >= 20 && y <= 44);

                if (phoneBorder || topNotch || leftWave || rightWave)
                {
                    colors[y * size + x] = Color.white;
                }
                else if (phoneScreen)
                {
                    colors[y * size + x] = new Color(1f, 1f, 1f, 0.22f);
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static Sprite GenerateCheckIconSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.clear;

        Vector2 p1 = new Vector2(16, 32);
        Vector2 p2 = new Vector2(27, 18);
        Vector2 p3 = new Vector2(48, 44);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 pt = new Vector2(x, y);
                float dist1 = DistanceToLineSegment(pt, p1, p2);
                float dist2 = DistanceToLineSegment(pt, p2, p3);
                float d = Mathf.Min(dist1, dist2);

                if (d <= 3.8f)
                {
                    float a = Mathf.Clamp01((3.8f - d) * 1.5f);
                    colors[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static Sprite GenerateWarningIconSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.clear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool bar = (x >= 29 && x <= 35 && y >= 24 && y <= 48);
                float distDot = Vector2.Distance(new Vector2(x, y), new Vector2(32, 15));
                bool dot = distDot <= 3.5f;

                if (bar || dot)
                {
                    colors[y * size + x] = Color.white;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static float DistanceToLineSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 pa = p - a;
        Vector2 ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude;
    }
}
