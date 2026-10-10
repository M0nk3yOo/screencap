#!/usr/bin/env bash
# Creates a self-signed code signing certificate "Screencap Dev" in the login keychain.
# Only for local development: it keeps macOS permissions stable across rebuilds.
set -euo pipefail
NAME="${1:-Screencap Dev}"
if security find-identity -p codesigning | grep -q "\"$NAME\""; then
  echo "Certificate '$NAME' already exists."
  exit 0
fi
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
PASS="$(openssl rand -hex 12)"
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=$NAME" \
  -keyout "$TMP/key.pem" -out "$TMP/cert.pem" \
  -addext "extendedKeyUsage=critical,codeSigning" \
  -addext "keyUsage=critical,digitalSignature" \
  -addext "basicConstraints=critical,CA:false"
openssl pkcs12 -export -inkey "$TMP/key.pem" -in "$TMP/cert.pem" -name "$NAME" \
  -out "$TMP/dev.p12" -passout "pass:$PASS"
security import "$TMP/dev.p12" -k "$HOME/Library/Keychains/login.keychain-db" -P "$PASS" -T /usr/bin/codesign
echo "Created '$NAME'."
