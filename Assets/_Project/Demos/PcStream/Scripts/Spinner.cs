using CardboardXR.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CardboardXR.Demos.PcStream
{
    // A progress ring that turns: a quarter of a circle sweeping round, for "working on it". Draws its
    // own ring picture so it needs no art, and sits in a UI canvas like any other element.
    public class Spinner : MonoBehaviour
    {
        const int k_Resolution = 128;
        // Degrees a second.
        const float k_Speed = 300f;

        Texture2D m_Texture;
        Sprite m_Sprite;

        public static Spinner Create(string name, RectTransform parent, Vector2 position, float size, Color color)
        {
            var image = M3.Surface(name, parent, position, new Vector2(size, size), color, 0f);
            var spinner = image.gameObject.AddComponent<Spinner>();
            spinner.m_Texture = MakeRing();
            spinner.m_Sprite = Sprite.Create(spinner.m_Texture, new Rect(0, 0, k_Resolution, k_Resolution), new Vector2(0.5f, 0.5f));
            image.sprite = spinner.m_Sprite;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillAmount = 0.3f;
            return spinner;
        }

        void Update() => transform.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * k_Speed % 360f);

        void OnDestroy()
        {
            if (m_Sprite != null)
                Destroy(m_Sprite);
            if (m_Texture != null)
                Destroy(m_Texture);
        }

        // A white ring, a little over a tenth as thick as it is wide, with soft edges.
        static Texture2D MakeRing()
        {
            var texture = new Texture2D(k_Resolution, k_Resolution, TextureFormat.RGBA32, false) { name = "Spinner Ring", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[k_Resolution * k_Resolution];
            const float outer = k_Resolution * 0.5f - 1f;
            const float inner = outer * 0.78f;
            var middle = (k_Resolution - 1) * 0.5f;
            for (var y = 0; y < k_Resolution; y++)
            {
                for (var x = 0; x < k_Resolution; x++)
                {
                    var distance = Mathf.Sqrt((x - middle) * (x - middle) + (y - middle) * (y - middle));
                    var coverage = Mathf.Clamp01(outer - distance + 0.5f) * Mathf.Clamp01(distance - inner + 0.5f);
                    pixels[y * k_Resolution + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
