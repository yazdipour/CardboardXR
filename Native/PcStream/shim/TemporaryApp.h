// Stand-in for moonlight-ios's TemporaryApp: the fields the app list parser fills in.
#import <Foundation/Foundation.h>
#import "TemporaryHost.h"

@interface TemporaryApp : NSObject
@property (nonatomic, copy) NSString* name;
@property (nonatomic, copy) NSString* id;
@property (nonatomic, copy) NSString* installPath;
@property (nonatomic) BOOL hdrSupported;
@property (nonatomic) BOOL hidden;
@end
