# PC Stream: native plugin (iOS)

The PC Stream demo (`Assets/_Project/Demos/PcStream`) shows a PC's screen on a big virtual display.
Without this plugin it shows a test picture. With it, it streams from a PC running
[Sunshine](https://github.com/LizardByte/Sunshine) (Steam Big Picture is one of Sunshine's default
apps) using the Moonlight protocol, and passes a Bluetooth controller through to the PC.

## License

The pieces this plugin uses are **GPLv3**: [moonlight-common-c](https://github.com/moonlight-stream/moonlight-common-c)
and code from [moonlight-ios](https://github.com/moonlight-stream/moonlight-ios). This repository is
Apache-2.0, so none of it is committed. `Tools/build_pcstream_native.sh` downloads it at pinned versions
into the git-ignored `Native/PcStream/.build/`, and writes the finished library to the git-ignored
`Assets/Plugins/iOS/PcStream/`. An app built with that library is a GPLv3 work as a whole; an app built
without it is not affected.

## Building it

```sh
zsh Tools/build_pcstream_native.sh          # first run downloads and builds OpenSSL, Opus, moonlight-common-c
zsh Tools/ios_build_and_run.sh              # or any other iOS build: it picks the library up
zsh Tools/build_pcstream_native.sh --clean  # back to the test picture
```

`PcStreamNative.cs` (an editor script) defines `PCSTREAM_NATIVE` for iOS while the library exists, which
compiles in the demo's `NativeStreamSource`, and as an iOS post-build step links the system frameworks and
adds the local network permission text. Unity recompiles when the define changes, so after building the
library open the Editor once (or run any batch build) before the build that should stream. The command line
tools aren't enough for the library: it needs Xcode.

## Using it

1. Run Sunshine on the PC (same Wi-Fi), and set it up at `https://<pc>:47990`.
2. In the demo, open the panel (the gear in the screen's corner), tap the Wi-Fi button and enter the
   PC's IP address on the number pad, then Connect.
3. First time only: the panel says "Enter PIN 1234 in Sunshine". Type that into Sunshine's PIN page
   (`https://<pc>:47990/pin`).
4. The demo launches "Steam Big Picture" (or the first app with "steam" in its name) and streams it.
   The address is remembered. A paired controller works as an Xbox controller; Start and Select
   together are the Guide button.

iOS asks to allow local network access the first time.

## How it works

| File | What it does |
|---|---|
| `pcstream.h` | The C API Unity calls (`DllImport("__Internal")`) |
| `PcsStream.m` | Pairing, launching the app and running the stream, around moonlight-ios's own `PairManager`, `HttpManager`, `CryptoManager`, `ServerInfoResponse` and `AppListResponse` |
| `PcsVideo.m` | H.264 from Moonlight into a `VTDecompressionSession` (BGRA out), each frame wrapped as a Metal texture with `CVMetalTextureCache` |
| `PcsAudio.m` | Opus into a RemoteIO audio unit, with a short ring buffer |
| `shim/` | Small stand-ins for moonlight-ios types the reused code expects (`TemporaryHost`, `TemporaryApp`, ...) |

In Unity, `NativeStreamSource` implements `IStreamSource`: it wraps the Metal texture with
`Texture2D.CreateExternalTexture` and updates it each frame, flipped because a decoded picture is top
row first, and sends the controller with `ControllerMap` (tested in `Tests/`).

## Status and limits

Everything compiles for the iPhone, but **it has not been run against a real Sunshine host yet**: the
first run is the test. Known gaps:

- H.264 only, stereo audio only, 1080p at 60 fps and 25 Mbps (constants in `NativeStreamSource.cs`).
  HEVC would be better for text at the same bitrate; it needs a decoder path for its parameter sets.
- One controller, no rumble, no motion, no mouse or keyboard.
- No automatic discovery of the PC (Sunshine advertises `_nvstream._tcp` over Bonjour); the address is typed.
- Android has no plugin yet.
