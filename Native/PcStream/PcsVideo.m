// Video: moonlight-common-c hands over H.264 pictures as Annex B; VideoToolbox decodes them in hardware
// to BGRA pixel buffers, and each one is wrapped as a Metal texture (no copy) for Unity to draw.
#import <Foundation/Foundation.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>
#import <Metal/Metal.h>
#import <VideoToolbox/VideoToolbox.h>

#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

#include "pcstream.h"
#include "PcsInternal.h"

#define NAL_LENGTH_PREFIX_SIZE 4
#define RING_SIZE 4

static VTDecompressionSessionRef sSession;
static CMVideoFormatDescriptionRef sFormat;
static NSMutableArray<NSData*>* sParameterSets;
static int sWidth, sHeight;

static CVMetalTextureCacheRef sCache;
static CVMetalTextureRef sRing[RING_SIZE];
static int sRingWidth[RING_SIZE], sRingHeight[RING_SIZE];
static uint32_t sFrameCount;
static int sNewest = -1;
// Textures given to Unity stay alive for a few more hand-outs: Unity draws a frame or two behind
// the call that gets the pointer, and the ring may have recycled the slot by then.
#define HELD_SIZE 4
static CVMetalTextureRef sHeld[HELD_SIZE];
static int sHeldNext;
static uint32_t sLastHandedOut;
static NSLock* sLock;

static void Publish(CVImageBufferRef image)
{
    if (sCache == NULL)
        return;
    int width = (int)CVPixelBufferGetWidth(image);
    int height = (int)CVPixelBufferGetHeight(image);
    CVMetalTextureRef texture = NULL;
    // sRGB, because the Unity project works in linear color and converts when it samples.
    CVReturn status = CVMetalTextureCacheCreateTextureFromImage(kCFAllocatorDefault, sCache, image, NULL,
                                                                MTLPixelFormatBGRA8Unorm_sRGB, width, height, 0, &texture);
    if (status != kCVReturnSuccess || texture == NULL)
        return;
    [sLock lock];
    int slot = (sNewest + 1) % RING_SIZE;
    if (sRing[slot] != NULL)
        CFRelease(sRing[slot]);
    sRing[slot] = texture;
    sRingWidth[slot] = width;
    sRingHeight[slot] = height;
    sNewest = slot;
    sFrameCount++;
    if (sFrameCount % 240 == 0)
        CVMetalTextureCacheFlush(sCache, 0);
    [sLock unlock];
}

void* pcs_frame(int* width, int* height, uint32_t* frameNumber)
{
    void* result = NULL;
    [sLock lock];
    if (sNewest >= 0 && sRing[sNewest] != NULL) {
        id<MTLTexture> texture = CVMetalTextureGetTexture(sRing[sNewest]);
        result = (__bridge void*)texture;
        if (sFrameCount != sLastHandedOut) {
            sLastHandedOut = sFrameCount;
            if (sHeld[sHeldNext] != NULL)
                CFRelease(sHeld[sHeldNext]);
            sHeld[sHeldNext] = (CVMetalTextureRef)CFRetain(sRing[sNewest]);
            sHeldNext = (sHeldNext + 1) % HELD_SIZE;
        }
        if (width) *width = sRingWidth[sNewest];
        if (height) *height = sRingHeight[sNewest];
        if (frameNumber) *frameNumber = sFrameCount;
    }
    [sLock unlock];
    return result;
}

void PcsVideoReset(void)
{
    [sLock lock];
    for (int i = 0; i < RING_SIZE; i++) {
        if (sRing[i] != NULL) {
            CFRelease(sRing[i]);
            sRing[i] = NULL;
        }
    }
    sNewest = -1;
    // The count keeps going across streams, so a new stream's first frame is never mistaken for one Unity has.
    // Textures already handed out stay alive in sHeld until newer ones replace them.
    [sLock unlock];
}

static void DecodeOutput(void* refCon, void* frameRefCon, OSStatus status, VTDecodeInfoFlags flags,
                         CVImageBufferRef image, CMTime presentationTime, CMTime duration)
{
    if (status == noErr && image != NULL)
        Publish(image);
}

static void DestroySession(void)
{
    if (sSession != NULL) {
        VTDecompressionSessionInvalidate(sSession);
        CFRelease(sSession);
        sSession = NULL;
    }
}

static BOOL CreateSession(void)
{
    DestroySession();
    NSDictionary* attributes = @{
        (id)kCVPixelBufferPixelFormatTypeKey: @(kCVPixelFormatType_32BGRA),
        (id)kCVPixelBufferMetalCompatibilityKey: @YES,
        (id)kCVPixelBufferIOSurfacePropertiesKey: @{},
    };
    VTDecompressionOutputCallbackRecord callback = { DecodeOutput, NULL };
    OSStatus status = VTDecompressionSessionCreate(kCFAllocatorDefault, sFormat, NULL, (__bridge CFDictionaryRef)attributes,
                                                   &callback, &sSession);
    if (status != noErr) {
        NSLog(@"[PcStream] VTDecompressionSessionCreate failed: %d", (int)status);
        sSession = NULL;
        return NO;
    }
    VTSessionSetProperty(sSession, kVTDecompressionPropertyKey_RealTime, kCFBooleanTrue);
    return YES;
}

// Annex B (00 00 01 / 00 00 00 01 start codes) to length-prefixed NAL units, which is what
// CoreMedia takes. Returns a malloc'ed buffer the caller owns.
static uint8_t* ToLengthPrefixed(const uint8_t* in, int length, int* outLength)
{
    uint8_t* out = malloc((size_t)length + (size_t)length / 3 + 16);
    int written = 0;
    int nalStart = -1;
    int i = 0;
    while (i + 3 <= length) {
        int prefix = 0;
        if (in[i] == 0 && in[i + 1] == 0) {
            if (in[i + 2] == 1)
                prefix = 3;
            else if (i + 4 <= length && in[i + 2] == 0 && in[i + 3] == 1)
                prefix = 4;
        }
        if (prefix == 0) {
            i++;
            continue;
        }
        if (nalStart >= 0) {
            int size = i - nalStart;
            out[written++] = (uint8_t)(size >> 24);
            out[written++] = (uint8_t)(size >> 16);
            out[written++] = (uint8_t)(size >> 8);
            out[written++] = (uint8_t)size;
            memcpy(out + written, in + nalStart, (size_t)size);
            written += size;
        }
        nalStart = i + prefix;
        i += prefix;
    }
    if (nalStart >= 0 && nalStart < length) {
        int size = length - nalStart;
        out[written++] = (uint8_t)(size >> 24);
        out[written++] = (uint8_t)(size >> 16);
        out[written++] = (uint8_t)(size >> 8);
        out[written++] = (uint8_t)size;
        memcpy(out + written, in + nalStart, (size_t)size);
        written += size;
    }
    *outLength = written;
    return out;
}

static int Setup(int videoFormat, int width, int height, int redrawRate, void* context, int drFlags)
{
    if (!(videoFormat & VIDEO_FORMAT_H264)) {
        NSLog(@"[PcStream] Unsupported video format %#x", videoFormat);
        return -1;
    }
    if (sLock == nil)
        sLock = [[NSLock alloc] init];
    sWidth = width;
    sHeight = height;
    sParameterSets = [NSMutableArray array];
    if (sCache == NULL) {
        id<MTLDevice> device = MTLCreateSystemDefaultDevice();
        if (CVMetalTextureCacheCreate(kCFAllocatorDefault, NULL, device, NULL, &sCache) != kCVReturnSuccess) {
            NSLog(@"[PcStream] Couldn't make the Metal texture cache");
            return -1;
        }
    }
    PcsVideoReset();
    return 0;
}

static void Cleanup(void)
{
    DestroySession();
    if (sFormat != NULL) {
        CFRelease(sFormat);
        sFormat = NULL;
    }
    sParameterSets = nil;
}

// Makes a new format description from the parameter sets collected since the last picture, and a new
// decoder session if the picture's shape changed.
static BOOL UpdateFormat(void)
{
    if (sParameterSets.count == 0)
        return sFormat != NULL;
    size_t count = sParameterSets.count;
    const uint8_t* pointers[count];
    size_t sizes[count];
    for (size_t i = 0; i < count; i++) {
        pointers[i] = sParameterSets[i].bytes;
        sizes[i] = sParameterSets[i].length;
    }
    CMVideoFormatDescriptionRef format = NULL;
    OSStatus status = CMVideoFormatDescriptionCreateFromH264ParameterSets(kCFAllocatorDefault, count, pointers, sizes,
                                                                          NAL_LENGTH_PREFIX_SIZE, &format);
    [sParameterSets removeAllObjects];
    if (status != noErr) {
        NSLog(@"[PcStream] H.264 format description failed: %d", (int)status);
        return sFormat != NULL;
    }
    if (sFormat != NULL && CMFormatDescriptionEqual(sFormat, format)) {
        CFRelease(format);
        return YES;
    }
    if (sFormat != NULL)
        CFRelease(sFormat);
    sFormat = format;
    if (sSession != NULL && VTDecompressionSessionCanAcceptFormatDescription(sSession, sFormat))
        return YES;
    return CreateSession();
}

static int SubmitDecodeUnit(PDECODE_UNIT unit)
{
    // Join the picture's pieces, and keep the parameter sets of an IDR frame for the format.
    int total = 0;
    for (PLENTRY entry = unit->bufferList; entry != NULL; entry = entry->next) {
        if (entry->bufferType == BUFFER_TYPE_PICDATA)
            total += entry->length;
    }
    uint8_t* picture = malloc((size_t)total + 1);
    if (picture == NULL)
        return DR_NEED_IDR;
    int offset = 0;
    for (PLENTRY entry = unit->bufferList; entry != NULL; entry = entry->next) {
        if (entry->bufferType == BUFFER_TYPE_PICDATA) {
            memcpy(picture + offset, entry->data, (size_t)entry->length);
            offset += entry->length;
        } else if (unit->frameType == FRAME_TYPE_IDR &&
                   (entry->bufferType == BUFFER_TYPE_SPS || entry->bufferType == BUFFER_TYPE_PPS)) {
            const uint8_t* data = (const uint8_t*)entry->data;
            int start = data[2] == 0x01 ? 3 : 4;
            [sParameterSets addObject:[NSData dataWithBytes:data + start length:(NSUInteger)(entry->length - start)]];
        }
    }

    if (unit->frameType == FRAME_TYPE_IDR && !UpdateFormat()) {
        free(picture);
        return DR_NEED_IDR;
    }
    if (sFormat == NULL || sSession == NULL) {
        free(picture);
        return DR_NEED_IDR;
    }

    int sampleLength = 0;
    uint8_t* sample = ToLengthPrefixed(picture, total, &sampleLength);
    free(picture);
    if (sampleLength == 0) {
        free(sample);
        return DR_NEED_IDR;
    }

    CMBlockBufferRef block = NULL;
    // The block buffer owns the memory from here on, and frees it with the default allocator (free).
    OSStatus status = CMBlockBufferCreateWithMemoryBlock(NULL, sample, (size_t)sampleLength, kCFAllocatorDefault, NULL, 0,
                                                         (size_t)sampleLength, 0, &block);
    if (status != noErr) {
        free(sample);
        return DR_NEED_IDR;
    }
    CMSampleBufferRef buffer = NULL;
    CMSampleTimingInfo timing = {
        .duration = kCMTimeInvalid,
        .presentationTimeStamp = CMTimeMake((int64_t)unit->presentationTimeUs, 1000000),
        .decodeTimeStamp = kCMTimeInvalid,
    };
    size_t sampleSize = (size_t)sampleLength;
    status = CMSampleBufferCreate(kCFAllocatorDefault, block, true, NULL, NULL, sFormat, 1, 1, &timing, 1, &sampleSize, &buffer);
    CFRelease(block);
    if (status != noErr)
        return DR_NEED_IDR;

    status = VTDecompressionSessionDecodeFrame(sSession, buffer,
                                               kVTDecodeFrame_EnableAsynchronousDecompression | kVTDecodeFrame_1xRealTimePlayback,
                                               NULL, NULL);
    CFRelease(buffer);
    if (status == kVTInvalidSessionErr || status == kVTVideoDecoderMalfunctionErr) {
        // The system took the decoder away (the app went to the background, say): make another from the next IDR.
        DestroySession();
        return DR_NEED_IDR;
    }
    return status == noErr ? DR_OK : DR_NEED_IDR;
}

void PcsVideoFillCallbacks(PDECODER_RENDERER_CALLBACKS callbacks)
{
    LiInitializeVideoCallbacks(callbacks);
    callbacks->setup = Setup;
    callbacks->cleanup = Cleanup;
    callbacks->submitDecodeUnit = SubmitDecodeUnit;
    callbacks->capabilities = CAPABILITY_REFERENCE_FRAME_INVALIDATION_AVC;
}
