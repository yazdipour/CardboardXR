// The plugin's front door (pcstream.h): pairing with Sunshine, launching the app, and running the
// stream. The networking and pairing are moonlight-ios's own classes (HttpManager, PairManager,
// CryptoManager, ...), copied in at build time; this file is the glue around them.
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

#import "CryptoManager.h"
#import "HttpManager.h"
#import "HttpRequest.h"
#import "PairManager.h"
#import "ServerInfoResponse.h"
#import "AppListResponse.h"
#import "StreamConfiguration.h"
#import "TemporaryApp.h"
#import "Utils.h"

#include <arpa/inet.h>
#include <string.h>

#include "pcstream.h"
#include "PcsInternal.h"

// --- State, readable from any thread ---

static NSObject* sLock;
static int sState = PCS_IDLE;
static NSString* sStatus = @"";
static NSString* sPin = @"";
static char sStatusBuffer[256];
static char sPinBuffer[16];
// Bumped by every pair/start/stop, so a background task that was superseded notices and stays quiet.
static int sGeneration;

static void SetState(int state, NSString* status)
{
    @synchronized (sLock) {
        sState = state;
        sStatus = status ?: @"";
    }
    NSLog(@"[PcStream] state %d: %@", state, status);
}

static BOOL IsCurrent(int generation)
{
    @synchronized (sLock) {
        return generation == sGeneration;
    }
}

static int NextGeneration(void)
{
    @synchronized (sLock) {
        return ++sGeneration;
    }
}

__attribute__((constructor)) static void InitLock(void)
{
    sLock = [[NSObject alloc] init];
}

int pcs_state(void)
{
    @synchronized (sLock) {
        return sState;
    }
}

const char* pcs_status(void)
{
    @synchronized (sLock) {
        strlcpy(sStatusBuffer, sStatus.UTF8String ?: "", sizeof(sStatusBuffer));
    }
    return sStatusBuffer;
}

const char* pcs_pin(void)
{
    @synchronized (sLock) {
        strlcpy(sPinBuffer, sPin.UTF8String ?: "", sizeof(sPinBuffer));
    }
    return sPinBuffer;
}

// --- Paired hosts: the host's certificate, kept so HTTPS to it can be checked ---

static NSString* CertKey(NSString* host)
{
    return [@"pcstream.servercert." stringByAppendingString:host];
}

static NSData* StoredCert(NSString* host)
{
    return [[NSUserDefaults standardUserDefaults] dataForKey:CertKey(host)];
}

static void StoreCert(NSString* host, NSData* cert)
{
    [[NSUserDefaults standardUserDefaults] setObject:cert forKey:CertKey(host)];
}

int pcs_is_paired(const char* host)
{
    return host != NULL && StoredCert([NSString stringWithUTF8String:host]) != nil;
}

static void EnsureClientCertificate(void)
{
    if ([CryptoManager readCertFromFile] == nil || [CryptoManager readKeyFromFile] == nil ||
        [CryptoManager readP12FromFile] == nil)
        [CryptoManager generateKeyPairUsingSSL];
}

// --- Pairing ---

@interface PcsPairCallback : NSObject <PairCallback>
@property (nonatomic) int generation;
@property (nonatomic, copy) NSString* host;
@property (nonatomic) BOOL needsRetry;
@end

@implementation PcsPairCallback
- (void)startPairing:(NSString*)PIN
{
    if (!IsCurrent(self.generation))
        return;
    @synchronized (sLock) {
        sPin = PIN;
    }
    SetState(PCS_PAIRING, [NSString stringWithFormat:@"Enter this PIN in Sunshine: %@\nOn the PC, open https://%@:47990/pin", PIN, self.host]);
}

- (void)pairSuccessful:(NSData*)serverCert
{
    StoreCert(self.host, serverCert);
    if (IsCurrent(self.generation))
        SetState(PCS_PAIRED, @"Paired");
}

- (void)pairFailed:(NSString*)message
{
    if (IsCurrent(self.generation))
        SetState(PCS_FAILED, message ?: @"Pairing failed");
}

- (void)alreadyPaired
{
    // The PC says it knows us, but we don't have its certificate (an earlier install, or the app's data was
    // cleared). Pairing again is the only way to get it, which means unpairing first.
    if (StoredCert(self.host) != nil) {
        if (IsCurrent(self.generation))
            SetState(PCS_PAIRED, @"Paired");
    } else {
        self.needsRetry = YES;
    }
}
@end

static void PairOnce(NSString* host, int generation, BOOL allowRetry)
{
    HttpManager* http = [[HttpManager alloc] initWithAddress:host httpsPort:0 serverCert:nil];
    PcsPairCallback* callback = [[PcsPairCallback alloc] init];
    callback.generation = generation;
    callback.host = host;
    PairManager* pair = [[PairManager alloc] initWithManager:http clientCert:[CryptoManager readCertFromFile] callback:callback];
    [pair main];
    if (callback.needsRetry && allowRetry && IsCurrent(generation)) {
        [http executeRequestSynchronously:[HttpRequest requestWithUrlRequest:[http newUnpairRequest]]];
        PairOnce(host, generation, NO);
    } else if (callback.needsRetry && IsCurrent(generation)) {
        SetState(PCS_FAILED, @"The PC already has this phone paired. Unpair it in Sunshine and try again.");
    }
}

void pcs_pair(const char* host)
{
    if (host == NULL)
        return;
    NSString* address = [NSString stringWithUTF8String:host];
    int generation = NextGeneration();
    SetState(PCS_PAIRING, @"Contacting the PC");
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        EnsureClientCertificate();
        PairOnce(address, generation, YES);
    });
}

// --- Streaming ---

static SERVER_INFORMATION sServerInfo;
static STREAM_CONFIGURATION sStreamConfig;
static CONNECTION_LISTENER_CALLBACKS sListener;
static DECODER_RENDERER_CALLBACKS sVideo;
static AUDIO_RENDERER_CALLBACKS sAudio;
static char sHostString[256];
static char sAppVersion[32];
static char sGfeVersion[32];
static char sRtspUrl[256];
static int sStreamGeneration;
static volatile BOOL sConnected;

static void ListenerStageStarting(int stage)
{
    if (IsCurrent(sStreamGeneration))
        SetState(PCS_CONNECTING, [NSString stringWithFormat:@"Starting %s", LiGetStageName(stage)]);
}

static void ListenerStageFailed(int stage, int error)
{
    if (IsCurrent(sStreamGeneration))
        SetState(PCS_FAILED, [NSString stringWithFormat:@"%s failed (error %d)", LiGetStageName(stage), error]);
}

static void ListenerConnectionStarted(void)
{
    sConnected = YES;
    if (IsCurrent(sStreamGeneration))
        SetState(PCS_STREAMING, @"Streaming");
}

static void ListenerConnectionTerminated(int error)
{
    sConnected = NO;
    if (IsCurrent(sStreamGeneration))
        SetState(error == 0 ? PCS_IDLE : PCS_FAILED, error == 0 ? @"Stream ended" : [NSString stringWithFormat:@"Connection lost (error %d)", error]);
}

static void ListenerLog(const char* format, ...)
{
    va_list args;
    va_start(args, format);
    vfprintf(stderr, format, args);
    va_end(args);
}

static TemporaryApp* FindApp(NSSet* apps, NSString* wanted)
{
    NSString* lower = wanted.lowercaseString;
    for (TemporaryApp* app in apps) {
        if ([app.name.lowercaseString isEqualToString:lower])
            return app;
    }
    for (TemporaryApp* app in apps) {
        if ([app.name.lowercaseString containsString:lower])
            return app;
    }
    // "Steam Big Picture" on Sunshine; "Steam" on some hosts.
    for (TemporaryApp* app in apps) {
        if ([app.name.lowercaseString containsString:@"steam"])
            return app;
    }
    return nil;
}

static void RunStream(NSString* host, NSString* appName, int width, int height, int fps, int bitrateKbps, int generation)
{
    NSData* cert = StoredCert(host);
    if (cert == nil) {
        SetState(PCS_FAILED, @"Not paired with this PC yet");
        return;
    }
    HttpManager* http = [[HttpManager alloc] initWithAddress:host httpsPort:0 serverCert:cert];

    SetState(PCS_CONNECTING, @"Contacting the PC");
    ServerInfoResponse* info = [[ServerInfoResponse alloc] init];
    [http executeRequestSynchronously:[HttpRequest requestForResponse:info withUrlRequest:[http newServerInfoRequest:false]
                                                        fallbackError:401 fallbackRequest:[http newHttpServerInfoRequest]]];
    if (![info isStatusOk]) {
        SetState(PCS_FAILED, info.statusMessage ?: @"Couldn't reach the PC");
        return;
    }
    NSString* pairStatus = [info getStringTag:@"PairStatus"];
    NSString* appVersion = [info getStringTag:@"appversion"];
    NSString* gfeVersion = [info getStringTag:@"GfeVersion"];
    NSString* serverState = [info getStringTag:@"state"];
    if (pairStatus == nil || appVersion == nil || serverState == nil) {
        SetState(PCS_FAILED, @"Unexpected reply from the PC (is Sunshine running?)");
        return;
    }
    if (![pairStatus isEqualToString:@"1"]) {
        [[NSUserDefaults standardUserDefaults] removeObjectForKey:CertKey(host)];
        SetState(PCS_FAILED, @"The PC no longer knows this phone. Pair again.");
        return;
    }
    if (!IsCurrent(generation))
        return;

    SetState(PCS_CONNECTING, @"Finding the app");
    AppListResponse* list = [[AppListResponse alloc] init];
    [http executeRequestSynchronously:[HttpRequest requestForResponse:list withUrlRequest:[http newAppListRequest]]];
    if (![list isStatusOk]) {
        SetState(PCS_FAILED, list.statusMessage ?: @"Couldn't read the PC's app list");
        return;
    }
    TemporaryApp* app = FindApp([list getAppList], appName);
    if (app == nil) {
        SetState(PCS_FAILED, [NSString stringWithFormat:@"No \"%@\" app in Sunshine", appName]);
        return;
    }

    StreamConfiguration* config = [[StreamConfiguration alloc] init];
    config.host = host;
    config.appVersion = appVersion;
    config.gfeVersion = gfeVersion;
    config.appID = app.id;
    config.appName = app.name;
    config.width = width;
    config.height = height;
    config.frameRate = fps;
    config.bitRate = bitrateKbps;
    config.supportedVideoFormats = VIDEO_FORMAT_H264;
    config.audioConfiguration = AUDIO_CONFIGURATION_STEREO;
    config.gamepadMask = 1;
    config.multiController = NO;
    config.optimizeGameSettings = NO;
    config.playAudioOnPC = NO;
    config.riKey = [Utils randomBytes:16];
    config.riKeyId = (int)arc4random();
    config.serverCert = cert;
    NSString* codecSupport = [info getStringTag:@"ServerCodecModeSupport"];
    config.serverCodecModeSupport = codecSupport ? codecSupport.intValue : 0;

    SetState(PCS_CONNECTING, [NSString stringWithFormat:@"Starting %@", app.name]);
    BOOL resume = [serverState hasSuffix:@"_SERVER_BUSY"];
    HttpResponse* launch = [[HttpResponse alloc] init];
    [http executeRequestSynchronously:[HttpRequest requestForResponse:launch
                                                       withUrlRequest:[http newLaunchOrResumeRequest:resume ? @"resume" : @"launch" config:config]]];
    NSString* sessionTag = resume ? @"resume" : @"gamesession";
    NSString* session = [launch getStringTag:sessionTag];
    if (![launch isStatusOk]) {
        SetState(PCS_FAILED, launch.statusMessage ?: @"The PC refused to start the app");
        return;
    }
    if (session == nil || [session isEqualToString:@"0"]) {
        SetState(PCS_FAILED, @"The PC couldn't start the app");
        return;
    }
    NSString* rtsp = [launch getStringTag:@"sessionUrl0"];
    if (!IsCurrent(generation))
        return;

    strlcpy(sHostString, [Utils addressPortStringToAddress:host].UTF8String, sizeof(sHostString));
    strlcpy(sAppVersion, appVersion.UTF8String, sizeof(sAppVersion));
    strlcpy(sGfeVersion, gfeVersion ? gfeVersion.UTF8String : "", sizeof(sGfeVersion));
    strlcpy(sRtspUrl, rtsp ? rtsp.UTF8String : "", sizeof(sRtspUrl));

    LiInitializeServerInformation(&sServerInfo);
    sServerInfo.address = sHostString;
    sServerInfo.serverInfoAppVersion = sAppVersion;
    if (gfeVersion != nil)
        sServerInfo.serverInfoGfeVersion = sGfeVersion;
    if (rtsp != nil)
        sServerInfo.rtspSessionUrl = sRtspUrl;
    sServerInfo.serverCodecModeSupport = config.serverCodecModeSupport;

    LiInitializeStreamConfiguration(&sStreamConfig);
    sStreamConfig.width = width;
    sStreamConfig.height = height;
    sStreamConfig.fps = fps;
    sStreamConfig.bitrate = bitrateKbps;
    sStreamConfig.supportedVideoFormats = VIDEO_FORMAT_H264;
    sStreamConfig.audioConfiguration = AUDIO_CONFIGURATION_STEREO;
    sStreamConfig.encryptionFlags = ENCFLG_ALL;
    sStreamConfig.streamingRemotely = STREAM_CFG_AUTO;
    sStreamConfig.packetSize = 1392;
    memcpy(sStreamConfig.remoteInputAesKey, config.riKey.bytes, config.riKey.length);
    memset(sStreamConfig.remoteInputAesIv, 0, 16);
    int keyId = (int)htonl((uint32_t)config.riKeyId);
    memcpy(sStreamConfig.remoteInputAesIv, &keyId, sizeof(keyId));

    PcsVideoFillCallbacks(&sVideo);
    PcsAudioFillCallbacks(&sAudio);
    LiInitializeConnectionCallbacks(&sListener);
    sListener.stageStarting = ListenerStageStarting;
    sListener.stageFailed = ListenerStageFailed;
    sListener.connectionStarted = ListenerConnectionStarted;
    sListener.connectionTerminated = ListenerConnectionTerminated;
    sListener.logMessage = ListenerLog;

    sStreamGeneration = generation;
    PcsVideoReset();
    int result = LiStartConnection(&sServerInfo, &sStreamConfig, &sListener, &sVideo, &sAudio, NULL, 0, NULL, 0);
    if (result != 0 && IsCurrent(generation) && pcs_state() != PCS_FAILED)
        SetState(PCS_FAILED, [NSString stringWithFormat:@"Couldn't start the stream (error %d)", result]);
}

// Starting and stopping run one after the other, so a stop never lands in the middle of a start.
static dispatch_queue_t StreamQueue(void)
{
    static dispatch_queue_t queue;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        queue = dispatch_queue_create("pcstream.stream", DISPATCH_QUEUE_SERIAL);
    });
    return queue;
}

void pcs_start(const char* host, const char* appName, int width, int height, int fps, int bitrateKbps)
{
    if (host == NULL)
        return;
    NSString* address = [NSString stringWithUTF8String:host];
    NSString* app = appName ? [NSString stringWithUTF8String:appName] : @"Steam Big Picture";
    int generation = NextGeneration();
    // Said now, not when the work gets going, so a screen showing an earlier failure moves on at once.
    SetState(PCS_CONNECTING, @"Contacting the PC");
    // A stream already running is stopped first.
    dispatch_async(StreamQueue(), ^{
        if (sConnected) {
            LiStopConnection();
            sConnected = NO;
        }
        if (IsCurrent(generation))
            RunStream(address, app, width, height, fps, bitrateKbps, generation);
    });
}

void pcs_stop(void)
{
    int generation = NextGeneration();
    // Interrupt a start that's still negotiating, then stop on another thread: this can be called from
    // inside the library's own callbacks' thread of control and must not wait for it.
    LiInterruptConnection();
    SetState(PCS_IDLE, @"");
    dispatch_async(StreamQueue(), ^{
        LiStopConnection();
        sConnected = NO;
        PcsVideoReset();
        // Quiet if something has started since.
        if (IsCurrent(generation))
            SetState(PCS_IDLE, @"");
    });
}

void pcs_gamepad(uint32_t buttons, uint8_t leftTrigger, uint8_t rightTrigger,
                 int16_t leftX, int16_t leftY, int16_t rightX, int16_t rightY)
{
    if (!sConnected)
        return;
    LiSendMultiControllerEvent(0, 1, (int)buttons, leftTrigger, rightTrigger, leftX, leftY, rightX, rightY);
}
