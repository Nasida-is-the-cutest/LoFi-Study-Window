using System;

namespace PotatoOptimization.Utilities
{
    public readonly struct WindowPlacement
    {
        public WindowPlacement(int x, int y, int width, int height)
        { X = x; Y = y; Width = width; Height = height; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
    }

    public static class WindowGeometry
    {
        public static WindowPlacement Clamp(int x, int y, int width, int height,
            int left, int top, int right, int bottom, double dpiScale, bool firstUse)
        {
            int areaWidth = Math.Max(1, right - left);
            int areaHeight = Math.Max(1, bottom - top);
            width = Math.Min(areaWidth, Math.Max((int)(320 * dpiScale), width));
            height = Math.Min(areaHeight, Math.Max((int)(180 * dpiScale), height));
            int margin = (int)(24 * dpiScale);
            if (firstUse) { x = right - width - margin; y = bottom - height - margin; }
            x = Math.Max(left, Math.Min(x, right - width));
            y = Math.Max(top, Math.Min(y, bottom - height));
            return new WindowPlacement(x, y, width, height);
        }
    }
}
