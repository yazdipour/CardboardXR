// Stand-in for moonlight-ios's TemporaryHost (a Core Data-backed model there): just the fields the
// reused networking code reads and writes. See Tools/build_pcstream_native.sh.
#import <Foundation/Foundation.h>

@interface TemporaryHost : NSObject
@property (nonatomic, copy) NSString* activeAddress;
@property (nonatomic, copy) NSString* localAddress;
@property (nonatomic, copy) NSString* externalAddress;
@property (nonatomic, copy) NSString* mac;
@property (nonatomic, copy) NSString* name;
@property (nonatomic, copy) NSString* uuid;
@property (nonatomic, copy) NSString* currentGame;
@property (nonatomic, copy) NSData* serverCert;
@property (nonatomic) unsigned short httpsPort;
@property (nonatomic) BOOL isNvidiaServerSoftware;
@property (nonatomic) int pairState;
@property (nonatomic) int serverCodecModeSupport;
@property (nonatomic) NSMutableSet* appList;
@end
