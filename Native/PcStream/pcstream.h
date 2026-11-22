// The PC Stream plugin's C API, called from Unity (Assets/_Project/Demos/PcStream/Scripts/NativeStreamSource.cs).
// Everything here is safe to call from any thread, and none of it blocks: pairing and starting a stream
// run in the background and report through pcs_state() and pcs_status().
#ifndef PCSTREAM_H
#define PCSTREAM_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

enum {
    PCS_IDLE = 0,       // nothing going on
    PCS_PAIRING = 1,    // waiting for the PIN to be typed into Sunshine (pcs_pin())
    PCS_PAIRED = 2,     // paired with the host; ready to start
    PCS_CONNECTING = 3, // launching the app and opening the stream
    PCS_STREAMING = 4,  // frames are arriving
    PCS_FAILED = 5,     // pcs_status() says why
};

int pcs_state(void);
// One line for the panel. The pointer is valid until the next call.
const char* pcs_status(void);
// The PIN to type into Sunshine's web page (https://<pc>:47990/pin) while pairing.
const char* pcs_pin(void);
// 1 if this phone has paired with this host before.
int pcs_is_paired(const char* host);

void pcs_pair(const char* host);
// Starts appName (for example "Steam Big Picture") at this size and frame rate, then streams it.
void pcs_start(const char* host, const char* appName, int width, int height, int fps, int bitrateKbps);
void pcs_stop(void);

// The newest decoded frame as an id<MTLTexture> (BGRA, sRGB), or NULL before there is one. The frame
// number goes up with every new frame, so the caller can tell whether the texture changed. The texture
// stays valid for the next few frames.
void* pcs_frame(int* width, int* height, uint32_t* frameNumber);

// One controller's whole state, sent every time it changes. Buttons are Moonlight's flags (Limelight.h:
// A_FLAG, B_FLAG, ...); sticks are -32768..32767, up positive; triggers 0..255.
void pcs_gamepad(uint32_t buttons, uint8_t leftTrigger, uint8_t rightTrigger,
                 int16_t leftX, int16_t leftY, int16_t rightX, int16_t rightY);

#ifdef __cplusplus
}
#endif

#endif
