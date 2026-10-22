using UnityEngine;
using UnityEngine.UI;

namespace PotatoOptimization.UI
{
    // Native UI geometry: no extra textures, materials or recurring animation.
    public sealed class LearningButtonGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            float radius = Mathf.Min(12, Mathf.Min(rect.width, rect.height) * 0.5f);
            const int segments = 8;
            const int perimeter = 4 * (segments + 1);
            mesh.AddVert(rect.center, color, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                float x = corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius;
                float y = corner < 2 ? rect.yMax - radius : rect.yMin + radius;
                for (int step = 0; step <= segments; step++)
                {
                    float angle = (corner * 90 + step * 90f / segments) * Mathf.Deg2Rad;
                    mesh.AddVert(new Vector2(x + Mathf.Cos(angle) * radius, y + Mathf.Sin(angle) * radius), color, Vector2.zero);
                }
            }
            for (int point = 0; point < perimeter; point++)
                mesh.AddTriangle(0, point + 1, (point + 1) % perimeter + 1);
        }
    }
}
