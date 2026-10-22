namespace PotatoOptimization.Utilities
{
    public enum LearningView { Static, Animated }

    public readonly struct LearningProfile
    {
        public LearningProfile(int logicFps, int renderInterval, float renderScale, bool freezeScene)
        {
            LogicFps = logicFps;
            RenderInterval = renderInterval;
            RenderScale = renderScale;
            FreezeScene = freezeScene;
        }

        public int LogicFps { get; }
        public int RenderInterval { get; }
        public float RenderScale { get; }
        public bool FreezeScene { get; }
    }

    public static class LearningModePolicy
    {
        public static LearningProfile Select(LearningView view, bool visible, bool interacting)
        {
            if (!visible) return new LearningProfile(10, 10, 0.4f, true);
            if (interacting) return new LearningProfile(15, 1, 0.4f, view == LearningView.Static);
            return view == LearningView.Static
                ? new LearningProfile(10, 10, 0.4f, true)
                : new LearningProfile(15, 3, 0.4f, false);
        }
    }
}
