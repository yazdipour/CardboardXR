using System;
using UnityEngine;

namespace CardboardXR.Demos.PcStream
{
    public enum StreamState
    {
        Idle,
        Connecting,
        Streaming,
        Failed,
    }

    // Where the PC screen's pictures come from. The demo shows whatever Texture says and doesn't care
    // how it got there: a test picture in the Editor, and the Sunshine stream on a phone.
    public interface IStreamSource : IDisposable
    {
        StreamState State { get; }

        // One line for the panel: what's happening, or why it failed.
        string Status { get; }

        // The latest frame, or null before there is one. The texture can change between frames
        // (a new size, a new native texture), so read it each Update rather than keeping it.
        Texture Texture { get; }

        // The frame's size in pixels, (0, 0) before there is one.
        Vector2Int FrameSize { get; }

        // True when the picture is stored top row first (a phone's video decoder does), which Unity
        // draws upside down unless the screen flips it.
        bool FlipVertical { get; }

        // True for a source that streams from a PC and so needs its address before Connect.
        bool NeedsHost { get; }

        // Starts streaming from host, a name or address. Sources that need none ignore it.
        void Connect(string host);

        void Disconnect();

        // Called every frame by the demo.
        void Tick();
    }
}
