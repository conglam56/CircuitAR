using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vẽ con trỏ tròn trực tiếp bằng UI mesh. Không dùng Image/sprite nên không thể
/// bị rơi về hình vuông trắng khi texture runtime chưa sẵn sàng trên Android.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HandCursorGraphic : MaskableGraphic
{
    [SerializeField, Range(16, 96)] private int segments = 48;
    [SerializeField] private bool ring;
    [SerializeField, Range(0.05f, 0.95f)] private float ringThickness = 0.16f;

    public bool IsRing => ring;

    public void Configure(bool drawRing, float thickness = 0.16f)
    {
        ring = drawRing;
        ringThickness = Mathf.Clamp(thickness, 0.05f, 0.95f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        int count = Mathf.Clamp(segments, 16, 96);

        if (!ring)
        {
            vh.AddVert(center, color, Vector2.one * 0.5f);
            for (int i = 0; i <= count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vh.AddVert(center + direction * radius, color, (direction + Vector2.one) * 0.5f);
            }
            for (int i = 0; i < count; i++) vh.AddTriangle(0, i + 1, i + 2);
            return;
        }

        float innerRadius = radius * (1f - ringThickness);
        for (int i = 0; i <= count; i++)
        {
            float angle = i * Mathf.PI * 2f / count;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            vh.AddVert(center + direction * radius, color, (direction + Vector2.one) * 0.5f);
            vh.AddVert(center + direction * innerRadius, color, (direction + Vector2.one) * 0.5f);
        }
        for (int i = 0; i < count; i++)
        {
            int outer = i * 2;
            int inner = outer + 1;
            vh.AddTriangle(outer, outer + 2, inner);
            vh.AddTriangle(outer + 2, inner + 2, inner);
        }
    }
}
