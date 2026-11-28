#if PCSTREAM_NATIVE && UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CardboardXR.Demos.PcStream
{
    // The PC's real screen: streamed from Sunshine by the native plugin (Native/PcStream), decoded in
    // hardware to a Metal texture the screen draws, with the first controller passed through to the PC.
    // Only compiled when the plugin is built into the app (Tools/build_pcstream_native.sh), which is
    // also what defines PCSTREAM_NATIVE.
    public sealed class NativeStreamSource : IStreamSource
    {
        const int k_Width = 1920;
        const int k_Height = 1080;
        const int k_Fps = 60;
        const int k_BitrateKbps = 25000;
        const string k_App = "Steam Big Picture";

        // The plugin's states (pcstream.h).
        const int Idle = 0, Pairing = 1, Paired = 2, Connecting = 3, Streaming = 4, Failed = 5;

        [DllImport("__Internal")] static extern int pcs_state();
        [DllImport("__Internal")] static extern IntPtr pcs_status();
        [DllImport("__Internal")] static extern int pcs_is_paired(string host);
        [DllImport("__Internal")] static extern void pcs_pair(string host);
        [DllImport("__Internal")] static extern void pcs_start(string host, string app, int width, int height, int fps, int bitrateKbps);
        [DllImport("__Internal")] static extern void pcs_stop();
        [DllImport("__Internal")] static extern IntPtr pcs_frame(out int width, out int height, out uint frameNumber);
        [DllImport("__Internal")] static extern void pcs_gamepad(uint buttons, byte leftTrigger, byte rightTrigger,
            short leftX, short leftY, short rightX, short rightY);

        string m_Host;
        bool m_StartWhenPaired;
        Texture2D m_Texture;
        uint m_Frame;
        ControllerPacket m_LastPacket;
        bool m_SentPacket;

        public StreamState State => pcs_state() switch
        {
            Streaming => StreamState.Streaming,
            Failed => StreamState.Failed,
            Idle => StreamState.Idle,
            _ => StreamState.Connecting,
        };

        public string Status => Marshal.PtrToStringAnsi(pcs_status());
        public Texture Texture => m_Texture;
        public Vector2Int FrameSize => m_Texture != null ? new Vector2Int(m_Texture.width, m_Texture.height) : default;
        public bool FlipVertical => true;
        public bool NeedsHost => true;

        // Pairs first if this phone hasn't paired with the PC, then streams: the plugin shows the PIN in Status.
        public void Connect(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;
            m_Host = host.Trim();
            if (pcs_is_paired(m_Host) != 0)
            {
                StartStream();
            }
            else
            {
                m_StartWhenPaired = true;
                pcs_pair(m_Host);
            }
        }

        void StartStream()
        {
            m_StartWhenPaired = false;
            pcs_start(m_Host, k_App, k_Width, k_Height, k_Fps, k_BitrateKbps);
        }

        public void Disconnect()
        {
            m_StartWhenPaired = false;
            pcs_stop();
        }

        public void Tick()
        {
            if (m_StartWhenPaired && pcs_state() == Paired)
                StartStream();
            UpdateTexture();
            SendController();
        }

        void UpdateTexture()
        {
            var pointer = pcs_frame(out var width, out var height, out var frame);
            if (pointer == IntPtr.Zero || frame == m_Frame)
                return;
            m_Frame = frame;
            if (m_Texture == null || m_Texture.width != width || m_Texture.height != height)
            {
                if (m_Texture != null)
                    UnityEngine.Object.Destroy(m_Texture);
                // BGRA, sRGB (the plugin makes the Metal texture that way), so not linear.
                m_Texture = Texture2D.CreateExternalTexture(width, height, TextureFormat.BGRA32, false, false, pointer);
                m_Texture.name = "PC Stream";
            }
            else
            {
                m_Texture.UpdateExternalTexture(pointer);
            }
        }

        // Sends the first controller's state whenever it changes.
        void SendController()
        {
            var pad = Gamepad.current;
            if (pad == null || pcs_state() != Streaming)
                return;
            var input = new ControllerInput
            {
                South = pad.buttonSouth.isPressed, East = pad.buttonEast.isPressed,
                West = pad.buttonWest.isPressed, North = pad.buttonNorth.isPressed,
                DpadUp = pad.dpad.up.isPressed, DpadDown = pad.dpad.down.isPressed,
                DpadLeft = pad.dpad.left.isPressed, DpadRight = pad.dpad.right.isPressed,
                LeftShoulder = pad.leftShoulder.isPressed, RightShoulder = pad.rightShoulder.isPressed,
                LeftStick = pad.leftStickButton.isPressed, RightStick = pad.rightStickButton.isPressed,
                Start = pad.startButton.isPressed, Select = pad.selectButton.isPressed,
                LeftTrigger = pad.leftTrigger.ReadValue(), RightTrigger = pad.rightTrigger.ReadValue(),
                LeftStickAxis = pad.leftStick.ReadValue(), RightStickAxis = pad.rightStick.ReadValue(),
            };
            var packet = ControllerMap.Map(input);
            if (m_SentPacket && packet.Equals(m_LastPacket))
                return;
            m_LastPacket = packet;
            m_SentPacket = true;
            pcs_gamepad(packet.Buttons, packet.LeftTrigger, packet.RightTrigger, packet.LeftX, packet.LeftY, packet.RightX, packet.RightY);
        }

        public void Dispose()
        {
            pcs_stop();
            if (m_Texture != null)
                UnityEngine.Object.Destroy(m_Texture);
            m_Texture = null;
        }
    }
}
#endif
