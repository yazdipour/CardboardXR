// Included before every moonlight-ios source file the plugin compiles (-include), in place of that
// app's prefix header.
#import <Availability.h>
#include <TargetConditionals.h>
#ifdef __OBJC__
#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>
#import "Logger.h"
#import "Utils.h"
#endif
