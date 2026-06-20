#!/usr/bin/env bash
# Build the Sideport sample apps as UNSIGNED IPAs. Sideport re-signs each one with
# YOUR own Apple ID when it installs them, so you need no paid developer account
# and no signing identity here.
#
# Requirements: Xcode + XcodeGen (`brew install xcodegen`).
# Usage:
#   ./build.sh                 # build every app in this folder -> ./dist/*.ipa
#   ./build.sh DiceRoll        # build just one
set -euo pipefail
cd "$(dirname "$0")"

OUT="$(pwd)/dist"
mkdir -p "$OUT"

APPS=("$@")
if [ ${#APPS[@]} -eq 0 ]; then
  APPS=(CertCountdown DiceRoll)
fi

for app in "${APPS[@]}"; do
  echo "==> $app"
  ( cd "$app" && xcodegen generate >/dev/null )

  dd="$(mktemp -d)"
  xcodebuild -project "$app/$app.xcodeproj" -scheme "$app" -configuration Release \
    -sdk iphoneos -destination 'generic/platform=iOS' -derivedDataPath "$dd" \
    CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY="" build >/dev/null

  stage="$(mktemp -d)"
  mkdir -p "$stage/Payload"
  cp -R "$dd/Build/Products/Release-iphoneos/$app.app" "$stage/Payload/"
  ( cd "$stage" && zip -qry "$OUT/$app.ipa" Payload )
  echo "    -> dist/$app.ipa"
done

echo "Done. Point Sideport at the IPAs in ./dist/."
