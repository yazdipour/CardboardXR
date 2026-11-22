using UnityEngine;

namespace CardboardXR.Demos.PcStream
{
    // How big the PC's screen is in the room. It hangs at a fixed distance, so its size is its
    // width in meters, which is also how wide it looks: the resize buttons step that width, and the
    // panel shows it as an angle, since "62 degrees of your view" means more than "3.6 m".
    public static class ScreenLayout
    {
        // Meters from the head. Far enough to rest the eyes on, near enough that the screen is big.
        public const float Distance = 3f;
        public const float MinWidth = 1.2f;
        public const float MaxWidth = 8f;
        public const float DefaultWidth = 3.6f;
        // Each press of a resize button multiplies or divides the width by this, so the steps feel the
        // same at every size.
        public const float Step = 1.2f;
        // Until the stream says what shape it is.
        public const float DefaultAspect = 16f / 9f;

        // The width after a number of resize steps (negative to shrink), kept in range.
        public static float Resize(float width, int steps) =>
            Mathf.Clamp(width * Mathf.Pow(Step, steps), MinWidth, MaxWidth);

        public static float Height(float width, float aspect) => width / Mathf.Max(aspect, 0.1f);

        // How much of the view a screen of this width fills, across, in degrees.
        public static float AngularWidth(float width, float distance = Distance) =>
            2f * Mathf.Atan(width / 2f / distance) * Mathf.Rad2Deg;

        // Aspect of a frame, falling back to the default when there is none yet.
        public static float Aspect(Vector2Int frameSize) =>
            frameSize.x > 0 && frameSize.y > 0 ? (float)frameSize.x / frameSize.y : DefaultAspect;
    }
}
