// Audio: Opus packets from moonlight-common-c are decoded to 16-bit stereo and played through a
// RemoteIO audio unit, pulling from a ring buffer. The queue is kept short: late audio is dropped,
// not played late.
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <AudioToolbox/AudioToolbox.h>

#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

#include "opus_multistream.h"
#include "PcsInternal.h"

// Power of two, in frames (one frame is one sample per channel): about 340 ms at 48 kHz.
#define RING_FRAMES 16384
#define MAX_QUEUED_MS 80

static OpusMSDecoder* sDecoder;
static OPUS_MULTISTREAM_CONFIGURATION sConfig;
static AudioUnit sUnit;
static int16_t* sRing;
static atomic_uint sWrite;
static atomic_uint sRead;
static int16_t* sDecoded;

static OSStatus Render(void* refCon, AudioUnitRenderActionFlags* flags, const AudioTimeStamp* time, UInt32 bus,
                       UInt32 frames, AudioBufferList* io)
{
    int16_t* out = (int16_t*)io->mBuffers[0].mData;
    int channels = sConfig.channelCount;
    unsigned write = atomic_load_explicit(&sWrite, memory_order_acquire);
    unsigned read = atomic_load_explicit(&sRead, memory_order_relaxed);
    unsigned available = write - read;
    unsigned take = available < frames ? available : frames;
    for (unsigned i = 0; i < take; i++) {
        unsigned index = ((read + i) & (RING_FRAMES - 1)) * (unsigned)channels;
        for (int c = 0; c < channels; c++)
            out[i * (unsigned)channels + (unsigned)c] = sRing[index + (unsigned)c];
    }
    // Silence for whatever's missing.
    if (take < frames)
        memset(out + take * (unsigned)channels, 0, (frames - take) * (unsigned)channels * sizeof(int16_t));
    atomic_store_explicit(&sRead, read + take, memory_order_release);
    return noErr;
}

static int Init(int audioConfiguration, POPUS_MULTISTREAM_CONFIGURATION opusConfig, void* context, int flags)
{
    sConfig = *opusConfig;
    int error = 0;
    sDecoder = opus_multistream_decoder_create(opusConfig->sampleRate, opusConfig->channelCount, opusConfig->streams,
                                               opusConfig->coupledStreams, opusConfig->mapping, &error);
    if (sDecoder == NULL) {
        NSLog(@"[PcStream] Opus decoder failed: %d", error);
        return -1;
    }
    sRing = calloc((size_t)RING_FRAMES * (size_t)opusConfig->channelCount, sizeof(int16_t));
    sDecoded = calloc((size_t)opusConfig->samplesPerFrame * (size_t)opusConfig->channelCount, sizeof(int16_t));
    atomic_store(&sWrite, 0);
    atomic_store(&sRead, 0);

    // Mix with whatever else plays (Unity's own audio), and don't turn it down.
    [[AVAudioSession sharedInstance] setCategory:AVAudioSessionCategoryPlayback
                                     withOptions:AVAudioSessionCategoryOptionMixWithOthers
                                           error:nil];
    [[AVAudioSession sharedInstance] setActive:YES error:nil];

    AudioComponentDescription description = {
        .componentType = kAudioUnitType_Output,
        .componentSubType = kAudioUnitSubType_RemoteIO,
        .componentManufacturer = kAudioUnitManufacturer_Apple,
    };
    AudioComponent component = AudioComponentFindNext(NULL, &description);
    if (component == NULL || AudioComponentInstanceNew(component, &sUnit) != noErr) {
        NSLog(@"[PcStream] No audio output");
        return -1;
    }
    AudioStreamBasicDescription format = {
        .mSampleRate = opusConfig->sampleRate,
        .mFormatID = kAudioFormatLinearPCM,
        .mFormatFlags = kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked,
        .mFramesPerPacket = 1,
        .mChannelsPerFrame = (UInt32)opusConfig->channelCount,
        .mBitsPerChannel = 16,
        .mBytesPerFrame = 2 * (UInt32)opusConfig->channelCount,
        .mBytesPerPacket = 2 * (UInt32)opusConfig->channelCount,
    };
    AURenderCallbackStruct callback = { Render, NULL };
    OSStatus status = AudioUnitSetProperty(sUnit, kAudioUnitProperty_StreamFormat, kAudioUnitScope_Input, 0, &format, sizeof(format));
    status |= AudioUnitSetProperty(sUnit, kAudioUnitProperty_SetRenderCallback, kAudioUnitScope_Input, 0, &callback, sizeof(callback));
    status |= AudioUnitInitialize(sUnit);
    status |= AudioOutputUnitStart(sUnit);
    if (status != noErr) {
        NSLog(@"[PcStream] Audio unit failed: %d", (int)status);
        return -1;
    }
    return 0;
}

static void Cleanup(void)
{
    if (sUnit != NULL) {
        AudioOutputUnitStop(sUnit);
        AudioUnitUninitialize(sUnit);
        AudioComponentInstanceDispose(sUnit);
        sUnit = NULL;
    }
    if (sDecoder != NULL) {
        opus_multistream_decoder_destroy(sDecoder);
        sDecoder = NULL;
    }
    free(sRing);
    sRing = NULL;
    free(sDecoded);
    sDecoded = NULL;
}

static void DecodeAndPlay(char* data, int length)
{
    if (sDecoder == NULL || sRing == NULL)
        return;
    // Don't add to a queue that's already long: late audio is worse than a gap.
    if (LiGetPendingAudioDuration() > 30)
        return;
    int frames = opus_multistream_decode(sDecoder, (unsigned char*)data, length, sDecoded, sConfig.samplesPerFrame, 0);
    if (frames <= 0)
        return;
    int channels = sConfig.channelCount;
    unsigned write = atomic_load_explicit(&sWrite, memory_order_relaxed);
    unsigned read = atomic_load_explicit(&sRead, memory_order_acquire);
    unsigned queued = write - read;
    unsigned maxQueued = (unsigned)sConfig.sampleRate * MAX_QUEUED_MS / 1000;
    if (queued + (unsigned)frames > maxQueued)
        return;
    for (int i = 0; i < frames; i++) {
        unsigned index = ((write + (unsigned)i) & (RING_FRAMES - 1)) * (unsigned)channels;
        for (int c = 0; c < channels; c++)
            sRing[index + (unsigned)c] = sDecoded[i * channels + c];
    }
    atomic_store_explicit(&sWrite, write + (unsigned)frames, memory_order_release);
}

void PcsAudioFillCallbacks(PAUDIO_RENDERER_CALLBACKS callbacks)
{
    LiInitializeAudioCallbacks(callbacks);
    callbacks->init = Init;
    callbacks->cleanup = Cleanup;
    callbacks->decodeAndPlaySample = DecodeAndPlay;
    callbacks->capabilities = CAPABILITY_SUPPORTS_ARBITRARY_AUDIO_DURATION;
}
