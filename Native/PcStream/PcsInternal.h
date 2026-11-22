// Shared between the plugin's own source files.
#ifndef PCS_INTERNAL_H
#define PCS_INTERNAL_H

#include "Limelight.h"

// Fills the video decoder callbacks: hardware H.264 decoding to Metal textures (PcsVideo.m).
void PcsVideoFillCallbacks(PDECODER_RENDERER_CALLBACKS callbacks);
// Fills the audio callbacks: Opus decoding to the speaker (PcsAudio.m).
void PcsAudioFillCallbacks(PAUDIO_RENDERER_CALLBACKS callbacks);
// Drops the frames of an earlier stream.
void PcsVideoReset(void);

#endif
