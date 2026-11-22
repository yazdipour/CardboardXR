#!/bin/zsh
# Builds the native half of the PC Stream demo for iOS: Moonlight's protocol library
# (moonlight-common-c), OpenSSL and Opus, as static libraries for the iPhone (arm64), then the
# plugin in Native/PcStream that joins them, into Assets/Plugins/iOS/PcStream/libpcstream.a.
#
# LICENSE: moonlight-common-c is GPLv3, and the plugin adapts code from moonlight-ios (also GPLv3).
# This repository is Apache-2.0, so none of that is committed: this script downloads it and the
# result goes in a git-ignored folder. An app built with the library is a GPLv3 work as a whole.
# Without the library the app builds as before, and the PC Stream demo shows its test picture.
#
#   zsh Tools/build_pcstream_native.sh [--clean]
#
# Needs Xcode (command line tools are not enough), cmake, git, curl and make.
set -euo pipefail

ROOT="${0:A:h:h}"
NATIVE="$ROOT/Native/PcStream"
WORK="$NATIVE/.build"
OUT="$ROOT/Assets/Plugins/iOS/PcStream"
MIN_IOS=15.0

# Pinned, so a build today is the build tomorrow.
MOONLIGHT_COMMON_C_COMMIT=94c65fd
MOONLIGHT_IOS_COMMIT=02dc9780496eeeac6d01c8bbdccb8b6fe71ef28a
OPENSSL_VERSION=3.4.1
OPUS_VERSION=1.5.2

[[ "${1:-}" == "--clean" ]] && { command rm -rf "$WORK" "$OUT" "$ROOT/Assets/Plugins/iOS/PcStream.meta"; echo "cleaned"; exit 0; }

SDK="$(xcrun --sdk iphoneos --show-sdk-path)"
CC="$(xcrun --sdk iphoneos -f clang)"
CFLAGS_IOS="-arch arm64 -isysroot $SDK -miphoneos-version-min=$MIN_IOS -O2"
JOBS="$(sysctl -n hw.ncpu)"
mkdir -p "$WORK" "$OUT"
PREFIX="$WORK/prefix"
mkdir -p "$PREFIX"

# --- OpenSSL (libcrypto is all Moonlight and the pairing code use) ---
if [[ ! -f "$PREFIX/lib/libcrypto.a" ]]; then
  echo "== OpenSSL $OPENSSL_VERSION"
  [[ -d "$WORK/openssl-$OPENSSL_VERSION" ]] || {
    curl -fsSL "https://github.com/openssl/openssl/releases/download/openssl-$OPENSSL_VERSION/openssl-$OPENSSL_VERSION.tar.gz" \
      | tar -xz -C "$WORK"
  }
  (
    cd "$WORK/openssl-$OPENSSL_VERSION"
    export CROSS_TOP="$(xcrun --sdk iphoneos --show-sdk-platform-path)/Developer"
    export CROSS_SDK="$(basename "$SDK")"
    ./Configure ios64-xcrun no-shared no-tests no-apps no-docs no-dso no-ui-console \
      -miphoneos-version-min=$MIN_IOS --prefix="$PREFIX" --openssldir="$PREFIX/ssl" >/dev/null
    make -j"$JOBS" build_libs >/dev/null
    make install_dev >/dev/null
  )
fi

# --- Opus ---
if [[ ! -f "$PREFIX/lib/libopus.a" ]]; then
  echo "== Opus $OPUS_VERSION"
  [[ -d "$WORK/opus-$OPUS_VERSION" ]] || {
    curl -fsSL "https://downloads.xiph.org/releases/opus/opus-$OPUS_VERSION.tar.gz" | tar -xz -C "$WORK"
  }
  cmake -S "$WORK/opus-$OPUS_VERSION" -B "$WORK/opus-build" -G "Unix Makefiles" \
    -DCMAKE_SYSTEM_NAME=iOS -DCMAKE_OSX_SYSROOT=iphoneos -DCMAKE_OSX_ARCHITECTURES=arm64 \
    -DCMAKE_OSX_DEPLOYMENT_TARGET=$MIN_IOS -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF \
    -DOPUS_BUILD_PROGRAMS=OFF -DOPUS_BUILD_TESTING=OFF -DOPUS_INSTALL_PKG_CONFIG_MODULE=OFF \
    -DOPUS_INSTALL_CMAKE_CONFIG_MODULE=OFF -DCMAKE_INSTALL_PREFIX="$PREFIX" >/dev/null
  cmake --build "$WORK/opus-build" -j"$JOBS" >/dev/null
  cmake --install "$WORK/opus-build" >/dev/null
fi

# --- moonlight-common-c ---
if [[ ! -f "$PREFIX/lib/libmoonlight-common-c.a" ]]; then
  echo "== moonlight-common-c $MOONLIGHT_COMMON_C_COMMIT"
  if [[ ! -d "$WORK/moonlight-common-c" ]]; then
    git clone --recurse-submodules --quiet https://github.com/moonlight-stream/moonlight-common-c.git "$WORK/moonlight-common-c"
    git -C "$WORK/moonlight-common-c" checkout --quiet "$MOONLIGHT_COMMON_C_COMMIT"
    git -C "$WORK/moonlight-common-c" submodule update --init --recursive --quiet
    # Its -Werror turns every new compiler warning into a failed build; warnings aren't ours to fix.
    sed -i '' 's/ -Werror//' "$WORK/moonlight-common-c/CMakeLists.txt"
  fi
  cmake -S "$WORK/moonlight-common-c" -B "$WORK/mcc-build" -G "Unix Makefiles" \
    -DCMAKE_SYSTEM_NAME=iOS -DCMAKE_OSX_SYSROOT=iphoneos -DCMAKE_OSX_ARCHITECTURES=arm64 \
    -DCMAKE_OSX_DEPLOYMENT_TARGET=$MIN_IOS -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF \
    -DOPENSSL_ROOT_DIR="$PREFIX" -DOPENSSL_CRYPTO_LIBRARY="$PREFIX/lib/libcrypto.a" \
    -DOPENSSL_INCLUDE_DIR="$PREFIX/include" -DOPENSSL_USE_STATIC_LIBS=ON >/dev/null
  cmake --build "$WORK/mcc-build" -j"$JOBS" >/dev/null
  # enet is built as its own static library; the plugin needs both.
  cp "$WORK/mcc-build/libmoonlight-common-c.a" "$PREFIX/lib/"
  find "$WORK/mcc-build" -name 'libenet.a' -exec cp {} "$PREFIX/lib/" \;
  mkdir -p "$PREFIX/include/moonlight"
  cp "$WORK/moonlight-common-c/src/Limelight.h" "$PREFIX/include/moonlight/"
fi

# --- moonlight-ios: its pairing, HTTP and certificate classes, reused as they are ---
IOS_SRC="$WORK/moonlight-ios/Limelight"
if [[ ! -d "$WORK/moonlight-ios" ]]; then
  echo "== moonlight-ios $MOONLIGHT_IOS_COMMIT"
  git clone --quiet https://github.com/moonlight-stream/moonlight-ios.git "$WORK/moonlight-ios"
  git -C "$WORK/moonlight-ios" checkout --quiet "$MOONLIGHT_IOS_COMMIT"
fi
# Its mkcert.c includes OpenSSL's headers with a capital O, which only works on a case-insensitive disk.
sed -i '' 's#<OpenSSL/#<openssl/#' "$IOS_SRC/Crypto/mkcert.c"
# It keeps the client's private key in the Documents folder, which this app shares with Finder and
# the Files app (so videos can be copied in). The Library folder is private to the app.
sed -i '' 's#NSDocumentDirectory#NSLibraryDirectory#' "$IOS_SRC/Crypto/CryptoManager.m"

# --- The plugin: moonlight-ios's classes plus Native/PcStream/*.m, one object file each, archived with
# the libraries they need ---
echo "== libpcstream.a"
OBJ="$WORK/obj"
command rm -rf "$OBJ"
mkdir -p "$OBJ"
REUSED=(
  "$IOS_SRC/Crypto/CryptoManager.m" "$IOS_SRC/Crypto/mkcert.c"
  "$IOS_SRC/Network/HttpManager.m" "$IOS_SRC/Network/HttpRequest.m" "$IOS_SRC/Network/HttpResponse.m"
  "$IOS_SRC/Network/ServerInfoResponse.m" "$IOS_SRC/Network/AppListResponse.m" "$IOS_SRC/Network/PairManager.m"
  "$IOS_SRC/Utility/Utils.m" "$IOS_SRC/Utility/Logger.m" "$IOS_SRC/Stream/StreamConfiguration.m"
)
INCLUDES=(-I"$PREFIX/include" -I"$PREFIX/include/moonlight" -I"$PREFIX/include/opus" -I"$NATIVE" -I"$NATIVE/shim"
  -I"$IOS_SRC/Crypto" -I"$IOS_SRC/Network" -I"$IOS_SRC/Utility" -I"$IOS_SRC/Stream")
for source in $REUSED "$NATIVE"/*.m(N) "$NATIVE"/*.mm(N) "$NATIVE"/*.c(N) "$NATIVE"/shim/*.m(N); do
  name="${source:t:r}"
  case "$source" in
    *.c)  lang=(-std=gnu11) ;;
    *.m)  lang=(-fobjc-arc -x objective-c -include "$NATIVE/shim/pcs-prefix.h") ;;
    *.mm) lang=(-fobjc-arc -x objective-c++ -std=c++17 -include "$NATIVE/shim/pcs-prefix.h") ;;
  esac
  # Warnings in code that isn't ours aren't ours to fix, but errors still stop the build.
  "$CC" ${=CFLAGS_IOS} "${lang[@]}" "${INCLUDES[@]}" -w -c "$source" -o "$OBJ/$name.o"
done
# One archive holding the plugin and every library it needs, so Unity links a single file.
command rm -f "$OUT/libpcstream.a"
libtool -static -o "$OUT/libpcstream.a" "$OBJ"/*.o \
  "$PREFIX/lib/libmoonlight-common-c.a" "$PREFIX/lib/libenet.a" "$PREFIX/lib/libopus.a" "$PREFIX/lib/libcrypto.a" 2>/dev/null
echo "built $OUT/libpcstream.a ($(du -h "$OUT/libpcstream.a" | cut -f1))\nNext: open the Unity Editor once (it defines PCSTREAM_NATIVE for iOS), then build."
