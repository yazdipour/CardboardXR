using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardboardXR.Demos.PcStream
{
    // The PC's screen: a flat quad ScreenLayout.Distance ahead of where the head faced when it was
    // placed, as wide as the demo says and as tall as the picture's shape needs. It stays put in the
    // room (the demo runs in 3DoF, so it doesn't drift as the phone moves), and a tap or pinch with
    // the gaze on it (Clicked) brings up the panel.
    public class StreamScreen : MonoBehaviour
    {
        [Tooltip("An unlit material whose main texture the PC's picture is drawn into. Copied.")]
        [SerializeField] Material m_Material;

        Material m_Instance;
        MeshRenderer m_Renderer;
        BoxCollider m_Collider;
        float m_Width = ScreenLayout.DefaultWidth;
        float m_Aspect = ScreenLayout.DefaultAspect;

        public event Action Clicked;

        public float Width => m_Width;

        void Awake()
        {
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildQuad();
            m_Renderer = gameObject.AddComponent<MeshRenderer>();
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Instance = new Material(m_Material) { name = "PC Screen" };
            m_Renderer.sharedMaterial = m_Instance;
            m_Collider = gameObject.AddComponent<BoxCollider>();
            gameObject.AddComponent<ClickRelay>().Clicked += () => Clicked?.Invoke();
            // Nothing to show until a picture comes in; a screen with no picture would be a white rectangle.
            m_Renderer.enabled = false;
            m_Collider.enabled = false;
            Apply();
        }

        void OnDestroy()
        {
            if (m_Instance != null)
                Destroy(m_Instance);
            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                Destroy(filter.sharedMesh);
        }

        // Hangs the screen ahead of where the head faces, level with the eyes, facing the head.
        public void Place(Transform head)
        {
            var flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            var forward = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            transform.SetPositionAndRotation(head.position + forward * ScreenLayout.Distance, Quaternion.LookRotation(forward, Vector3.up));
        }

        // The screen's top right corner as the viewer sees it, moved in by inset meters and out toward
        // the viewer by a little, so something placed there is hit by the gaze before the screen is.
        public Vector3 TopRightCorner(float inset, float towardViewer)
        {
            var height = ScreenLayout.Height(m_Width, m_Aspect);
            var local = new Vector3(0.5f - inset / m_Width, 0.5f - inset / height, 0f);
            return transform.TransformPoint(local) - transform.forward * towardViewer;
        }

        public void SetWidth(float width)
        {
            m_Width = Mathf.Clamp(width, ScreenLayout.MinWidth, ScreenLayout.MaxWidth);
            Apply();
        }

        // Shows a texture, or hides the screen for null. frameSize gives the picture's shape, and
        // flipVertical turns a picture stored top row first the right way up.
        public void Show(Texture texture, Vector2Int frameSize, bool flipVertical = false)
        {
            m_Instance.mainTexture = texture;
            m_Instance.mainTextureScale = new Vector2(1f, flipVertical ? -1f : 1f);
            m_Instance.mainTextureOffset = new Vector2(0f, flipVertical ? 1f : 0f);
            m_Aspect = ScreenLayout.Aspect(frameSize);
            m_Renderer.enabled = texture != null;
            m_Collider.enabled = texture != null;
            Apply();
        }

        void Apply()
        {
            transform.localScale = new Vector3(m_Width, ScreenLayout.Height(m_Width, m_Aspect), 1f);
            m_Collider.size = new Vector3(1f, 1f, 0.02f);
        }

        // A unit quad whose front is the -Z side: Place turns the screen to look away from the head
        // (+Z points away from the viewer), so the viewer stands on its front. Clockwise as seen from there.
        static Mesh BuildQuad()
        {
            var mesh = new Mesh
            {
                name = "PC Screen Quad",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
                },
                uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
            };
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }
    }

    // Passes a gaze click on this collider to whoever listens.
    public class ClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public event Action Clicked;

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    }
}
