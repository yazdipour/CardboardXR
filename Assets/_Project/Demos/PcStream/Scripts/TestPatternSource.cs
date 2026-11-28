using UnityEngine;

namespace CardboardXR.Demos.PcStream
{
    // A still test picture standing in for the PC: color bars, a grid, and a circle that shows
    // whether the screen's shape is right. It's what the demo shows in the Editor and wherever the
    // native stream plugin isn't there, so the screen and its resize buttons can be tried without a PC.
    public sealed class TestPatternSource : IStreamSource
    {
        const int k_Width = 1280;
        const int k_Height = 720;

        Texture2D m_Texture;

        public StreamState State { get; private set; } = StreamState.Idle;
        public string Status { get; private set; } = "Test picture";
        public Texture Texture => m_Texture;
        public bool FlipVertical => false;
        public bool NeedsHost => false;
        public Vector2Int FrameSize => m_Texture != null ? new Vector2Int(k_Width, k_Height) : default;

        public void Connect(string host)
        {
            if (m_Texture == null)
                m_Texture = Build();
            State = StreamState.Streaming;
        }

        public void Disconnect() => State = StreamState.Idle;

        public void Tick() { }

        public void Dispose()
        {
            if (m_Texture != null)
                Object.Destroy(m_Texture);
            m_Texture = null;
        }

        static Texture2D Build()
        {
            var bars = new[]
            {
                new Color32(235, 235, 235, 255), new Color32(235, 235, 16, 255), new Color32(16, 235, 235, 255),
                new Color32(16, 235, 16, 255), new Color32(235, 16, 235, 255), new Color32(235, 16, 16, 255),
                new Color32(16, 16, 235, 255),
            };
            var pixels = new Color32[k_Width * k_Height];
            var circleCenter = new Vector2(k_Width / 2f, k_Height * 0.4f);
            var circleRadius = k_Height * 0.28f;
            for (var y = 0; y < k_Height; y++)
            {
                for (var x = 0; x < k_Width; x++)
                {
                    // The top fifth is color bars, the rest a dark grid with a circle.
                    Color32 color;
                    if (y >= k_Height * 4 / 5)
                    {
                        color = bars[x * bars.Length / k_Width];
                    }
                    else
                    {
                        var line = x % 80 == 0 || y % 80 == 0;
                        color = line ? new Color32(90, 90, 100, 255) : new Color32(24, 24, 30, 255);
                        var distance = Vector2.Distance(new Vector2(x, y), circleCenter);
                        if (Mathf.Abs(distance - circleRadius) < 3f)
                            color = new Color32(255, 160, 40, 255);
                    }
                    pixels[y * k_Width + x] = color;
                }
            }
            var texture = new Texture2D(k_Width, k_Height, TextureFormat.RGBA32, false) { name = "PC Test Picture" };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
