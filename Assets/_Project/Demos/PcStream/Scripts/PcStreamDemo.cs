using System.Text;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.PcStream
{
    // Play a PC's games in the headset: the PC's screen (Steam Big Picture, say) hangs ahead in your
    // room, played with a Bluetooth controller. A gear in the screen's top right corner opens a small
    // panel that makes the screen smaller or larger (it steps through ScreenLayout), puts it back in
    // front of you, and shows how the stream is doing; the gear or the panel's close button puts it
    // away. The panel's PC button opens a number pad for the PC's address, which is kept for next time.
    // Connecting shows a dialog with a spinning ring and what's happening (the PIN to type into Sunshine
    // while pairing); if it fails, the dialog says why and what to try, and when the picture arrives it
    // closes itself.
    // Nothing else on the screen is clickable, so a stray tap never gets in the game's way.
    // Passthrough is on by default (the Core settings panel can switch it off), which runs 6DoF so
    // the screen stays where it was put as you move. Hand tracking is off: it's most of the phone's
    // heat, and the controller does the playing. Logs [PcStream].
    public class PcStreamDemo : Demo
    {
        const float k_PanelDistance = 1.5f;
        // The panel appears this far below where the head looks, in degrees.
        const float k_PanelBelowGaze = 10f;
        const float k_LowestPitch = -55f;
        static readonly Vector2 k_ControlsSize = new(400f, 264f);
        static readonly Vector2 k_ConnectSize = new(400f, 372f);
        static readonly Vector2 k_StatusSize = new(400f, 300f);
        // The gear: a round button this big (dp at the screen's distance), inside the screen's corner
        // by this many meters, and this far in front of it.
        const float k_GearSize = 64f;
        const float k_GearInset = 0.3f;
        const float k_GearForward = 0.03f;
        const string k_HostKey = "PcStream.Host";
        const int k_MaxHostLength = 21;

        [SerializeField] StreamScreen m_Screen;

        CoreServices m_Core;
        IStreamSource m_Source;
        Texture m_ShownTexture;
        Vector2Int m_ShownSize;
        readonly StringBuilder m_Host = new();

        RectTransform m_Panel;
        RectTransform m_ControlsPage;
        RectTransform m_ConnectPage;
        RectTransform m_StatusPage;
        RectTransform m_Gear;
        TMP_Text m_Status;
        TMP_Text m_SizeLabel;
        TMP_Text m_HostLabel;
        TMP_Text m_StatusTitle;
        TMP_Text m_StatusMessage;
        TMP_Text m_StatusError;
        Spinner m_Spinner;
        GameObject m_CancelButton;
        GameObject m_RetryButton;
        GameObject m_ChangeButton;
        bool m_PanelShown;
        // True from pressing Connect until the picture arrives or the dialog is dismissed.
        bool m_Attempting;
        StreamState m_LastState;
        string m_LastLoggedStatus;

        enum Page { Controls, Connect, Status }

        public override void OnDemoEnter(CoreServices core)
        {
            m_Core = core;
            m_Screen.Place(core.Head);
            m_Host.Append(SavedHost());
            BuildPanel();
            BuildGear();

            m_Source = CreateSource();
            Debug.Log($"[PcStream] Source: {m_Source.GetType().Name}");
            m_Screen.Show(null, default);
            var connecting = m_Source.NeedsHost && m_Host.Length > 0;
            if (connecting)
                m_Source.Connect(m_Host.ToString());
            else if (!m_Source.NeedsHost)
                m_Source.Connect(null);
            m_LastState = m_Source.State;
            Refresh();
            // Shown at the start, so a first look finds out what the gear does: the progress of a
            // connection that's starting, the number pad when there's no PC address yet, else the controls.
            if (connecting)
                ShowStatus();
            else
                ShowPanel(m_Source.NeedsHost ? Page.Connect : Page.Controls);
        }

        public override void OnDemoExit(CoreServices core)
        {
            m_Screen.Show(null, default);
            m_Source?.Dispose();
            m_Source = null;
        }

        // The stream source for this build: the real stream where the native plugin is built in, a test
        // picture everywhere else.
        static IStreamSource CreateSource()
        {
#if PCSTREAM_NATIVE && UNITY_IOS && !UNITY_EDITOR
            return new NativeStreamSource();
#else
            return new TestPatternSource();
#endif
        }

        // The address typed last time, else the one in Resources/pcstream-default-host.txt, a git-ignored file
        // holding the PC this build is for, so a build for your own PC needs no typing on the phone.
        static string SavedHost()
        {
            var saved = PlayerPrefs.GetString(k_HostKey, "");
            if (!string.IsNullOrWhiteSpace(saved))
                return saved;
            var file = Resources.Load<TextAsset>("pcstream-default-host");
            return file != null ? file.text.Trim() : "";
        }

        void Update()
        {
            if (m_Source == null)
                return;
            m_Source.Tick();
            Refresh();
            WatchConnection();
        }

        // Moves the dialog along as the connection does: closed once streaming, and brought back up when a
        // connection fails, even one that was going well.
        void WatchConnection()
        {
            var state = m_Source.State;
            if (state == StreamState.Streaming && m_Attempting)
            {
                m_Attempting = false;
                HidePanel();
            }
            else if (state == StreamState.Failed && m_LastState != StreamState.Failed)
            {
                ShowStatus();
            }
            m_LastState = state;
            if (m_Attempting)
                UpdateStatusPage();
        }

        // Keeps the screen on the source's latest frame, the gear on the screen's corner, and the
        // panel's labels up to date.
        void Refresh()
        {
            if (m_Source.Texture != m_ShownTexture || m_Source.FrameSize != m_ShownSize)
            {
                m_ShownTexture = m_Source.Texture;
                m_ShownSize = m_Source.FrameSize;
                m_Screen.Show(m_ShownTexture, m_ShownSize, m_Source.FlipVertical);
                Debug.Log($"[PcStream] Frame {m_ShownSize.x}x{m_ShownSize.y}, aspect {ScreenLayout.Aspect(m_ShownSize):F2}");
            }
            var status = m_Source.Status ?? "";
            if (m_LastLoggedStatus != status)
            {
                m_LastLoggedStatus = status;
                Debug.Log($"[PcStream] {status}");
            }
            m_SizeLabel.text = $"{ScreenLayout.AngularWidth(m_Screen.Width):F0}° wide";
            m_HostLabel.text = m_Host.Length > 0 ? m_Host.ToString() : "PC address";
            m_Status.text = status;
            m_Gear.SetPositionAndRotation(m_Screen.TopRightCorner(k_GearInset, k_GearForward), m_Screen.transform.rotation);
        }

        void Resize(int steps)
        {
            m_Screen.SetWidth(ScreenLayout.Resize(m_Screen.Width, steps));
            Debug.Log($"[PcStream] Screen {m_Screen.Width:F2} m wide, {ScreenLayout.AngularWidth(m_Screen.Width):F0}° of view");
            Refresh();
        }

        void ResetSize()
        {
            m_Screen.SetWidth(ScreenLayout.DefaultWidth);
            Refresh();
        }

        void Recenter()
        {
            m_Screen.Place(m_Core.Head);
            Debug.Log("[PcStream] Screen put back in front");
            Refresh();
        }

        void TogglePanel()
        {
            if (m_PanelShown)
                HidePanel();
            else
                ShowPanel(m_Attempting ? Page.Status : Page.Controls);
        }

        float HeadPitch => Mathf.Asin(Mathf.Clamp(m_Core.Head.forward.y, -1f, 1f)) * Mathf.Rad2Deg;

        // Where the head is looking, a little below, and it stays there until put away and shown again.
        void ShowPanel(Page page)
        {
            m_PanelShown = true;
            ShowPage(page);
            WorldUi.PlaceInFront(m_Panel, m_Core.Head, k_PanelDistance, Mathf.Max(HeadPitch - k_PanelBelowGaze, k_LowestPitch));
            M3Fade.Show(m_Panel.gameObject);
        }

        void ShowPage(Page page)
        {
            m_ControlsPage.gameObject.SetActive(page == Page.Controls);
            m_ConnectPage.gameObject.SetActive(page == Page.Connect);
            m_StatusPage.gameObject.SetActive(page == Page.Status);
            m_Panel.sizeDelta = page switch { Page.Connect => k_ConnectSize, Page.Status => k_StatusSize, _ => k_ControlsSize };
        }

        // The connection dialog, put in front of the viewer wherever the panel was.
        void ShowStatus()
        {
            m_Attempting = true;
            UpdateStatusPage();
            if (m_PanelShown)
                ShowPage(Page.Status);
            else
                ShowPanel(Page.Status);
        }

        // A spinner and the plugin's own words while it works; on failure, why and what to try.
        void UpdateStatusPage()
        {
            var failed = m_Source.State == StreamState.Failed;
            m_Spinner.gameObject.SetActive(!failed);
            m_StatusError.gameObject.SetActive(failed);
            m_CancelButton.SetActive(!failed);
            m_RetryButton.SetActive(failed);
            m_ChangeButton.SetActive(failed);
            if (failed)
            {
                var problem = ConnectionProblem.Explain(m_Source.Status, m_Host.ToString());
                m_StatusTitle.text = problem.Title;
                m_StatusMessage.text = problem.Message;
            }
            else
            {
                m_StatusTitle.text = $"Connecting to {m_Host}";
                m_StatusMessage.text = m_Source.Status;
            }
        }

        void HidePanel()
        {
            m_PanelShown = false;
            M3Fade.Hide(m_Panel.gameObject);
        }

        // --- The PC's address ---

        void AddToHost(string character)
        {
            if (m_Host.Length < k_MaxHostLength)
                m_Host.Append(character);
            Refresh();
        }

        void BackspaceHost()
        {
            if (m_Host.Length > 0)
                m_Host.Length--;
            Refresh();
        }

        void ConnectToHost()
        {
            if (m_Host.Length == 0 || !m_Source.NeedsHost)
            {
                ShowPage(Page.Controls);
                return;
            }
            PlayerPrefs.SetString(k_HostKey, m_Host.ToString());
            PlayerPrefs.Save();
            StartConnecting();
        }

        void StartConnecting()
        {
            Debug.Log($"[PcStream] Connecting to {m_Host}");
            m_Source.Connect(m_Host.ToString());
            m_LastState = m_Source.State;
            ShowStatus();
            Refresh();
        }

        // Stops trying and goes back to the address.
        void CancelConnecting()
        {
            m_Source.Disconnect();
            m_Attempting = false;
            ShowPage(Page.Connect);
        }

        void ChangeAddress()
        {
            m_Source.Disconnect();
            m_Attempting = false;
            ShowPage(Page.Connect);
        }

        // --- Building the UI ---

        // The gear, sized in dp for the screen's distance so it looks the same size as the panel's buttons
        // would from there.
        void BuildGear()
        {
            m_Gear = WorldUi.Panel("PC Stream Gear", transform, new Vector2(k_GearSize, k_GearSize), ScreenLayout.Distance,
                radius: M3Theme.k_ShapeFull);
            M3.IconButton("Settings", m_Gear, "settings", M3Button.Variant.Text, Vector2.zero, TogglePanel, k_GearSize - 8f);
        }

        void BuildPanel()
        {
            m_Panel = WorldUi.Panel("PC Stream Panel", transform, k_ControlsSize, k_PanelDistance);
            BuildControlsPage();
            BuildConnectPage();
            BuildStatusPage();
        }

        void BuildControlsPage()
        {
            // Laid out from the page's middle, which is the panel's: the panel is 264 dp tall here.
            m_ControlsPage = WorldUi.CreateGroup("Controls", m_Panel);
            M3.Text("Title", m_ControlsPage, "PC Stream", M3Theme.Type.TitleMedium, new Vector2(0f, 104f), new Vector2(280f, 28f));
            M3.IconButton("Close", m_ControlsPage, "close", M3Button.Variant.Text, new Vector2(166f, 104f), HidePanel, 44f);
            // Two lines, for the pairing instructions.
            m_Status = M3.Text("Status", m_ControlsPage, "", M3Theme.Type.BodyMedium, new Vector2(0f, 62f), new Vector2(360f, 44f),
                M3Theme.OnSurfaceVariant);

            M3.IconButton("Smaller", m_ControlsPage, "zoom_out", M3Button.Variant.Tonal, new Vector2(-110f, -4f), () => Resize(-1), 64f);
            m_SizeLabel = M3.Text("Size", m_ControlsPage, "", M3Theme.Type.TitleMedium, new Vector2(0f, -4f), new Vector2(140f, 28f));
            M3.IconButton("Larger", m_ControlsPage, "zoom_in", M3Button.Variant.Tonal, new Vector2(110f, -4f), () => Resize(1), 64f);

            M3.IconButton("PC", m_ControlsPage, "wifi", M3Button.Variant.Tonal, new Vector2(-150f, -92f), () => ShowPage(Page.Connect), 56f);
            M3.Button("Recenter", m_ControlsPage, "Recenter", M3Button.Variant.Tonal, new Vector2(-30f, -92f), Recenter, null, 124f);
            M3.Button("Reset size", m_ControlsPage, "Reset size", M3Button.Variant.Tonal, new Vector2(112f, -92f), ResetSize, null, 136f);
        }

        // A spinner, or on failure an error mark, over the plugin's words; Cancel while it works, Try again and
        // Change address when it hasn't.
        void BuildStatusPage()
        {
            m_StatusPage = WorldUi.CreateGroup("Status", m_Panel);
            m_StatusTitle = M3.Text("Title", m_StatusPage, "", M3Theme.Type.TitleMedium, new Vector2(0f, 118f), new Vector2(340f, 28f));
            M3.IconButton("Close", m_StatusPage, "close", M3Button.Variant.Text, new Vector2(166f, 118f), HidePanel, 44f);
            m_Spinner = Spinner.Create("Spinner", m_StatusPage, new Vector2(0f, 62f), 48f, M3Theme.Primary);
            m_StatusError = M3.Icon("Error", m_StatusPage, "error", 48f, new Vector2(0f, 62f), M3Theme.Error);
            m_StatusMessage = M3.Text("Message", m_StatusPage, "", M3Theme.Type.BodyMedium, new Vector2(0f, -22f), new Vector2(360f, 112f),
                M3Theme.OnSurfaceVariant);
            m_CancelButton = M3.Button("Cancel", m_StatusPage, "Cancel", M3Button.Variant.Tonal, new Vector2(0f, -118f), CancelConnecting, null, 140f).gameObject;
            m_RetryButton = M3.Button("Try again", m_StatusPage, "Try again", M3Button.Variant.Filled, new Vector2(-91f, -118f), StartConnecting, null, 140f).gameObject;
            m_ChangeButton = M3.Button("Change address", m_StatusPage, "Change address", M3Button.Variant.Tonal, new Vector2(76f, -118f), ChangeAddress, null, 170f).gameObject;
        }

        // A number pad, since there's no keyboard in a headset: 1 to 9, a dot, 0 and backspace.
        void BuildConnectPage()
        {
            m_ConnectPage = WorldUi.CreateGroup("Connect", m_Panel);
            M3.Text("Title", m_ConnectPage, "PC address", M3Theme.Type.TitleMedium, new Vector2(0f, 156f), new Vector2(280f, 28f));
            M3.IconButton("Back", m_ConnectPage, "close", M3Button.Variant.Text, new Vector2(166f, 156f), () => ShowPage(Page.Controls), 44f);
            m_HostLabel = M3.Text("Host", m_ConnectPage, "", M3Theme.Type.TitleLarge, new Vector2(0f, 112f), new Vector2(360f, 36f));

            const float step = 56f;
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", null };
            for (var i = 0; i < keys.Length; i++)
            {
                var position = new Vector2((i % 3 - 1) * step * 1.5f, 60f - i / 3 * step);
                var key = keys[i];
                if (key == null)
                    M3.IconButton("Backspace", m_ConnectPage, "backspace", M3Button.Variant.Tonal, position, BackspaceHost, 56f);
                else
                    M3.Button($"Key {key}", m_ConnectPage, key, M3Button.Variant.Tonal, position, () => AddToHost(key), null, 76f);
            }
            M3.Button("Connect", m_ConnectPage, "Connect", M3Button.Variant.Filled, new Vector2(0f, -160f), ConnectToHost, null, 200f);
        }
    }
}
