#!/bin/bash

# HSMbridge API Test Script
# Make sure HSMbridge is running before executing this script

BASE_URL="${1:-http://localhost:5000}"

echo "=========================================="
echo "HSMbridge API Test Script"
echo "Base URL: $BASE_URL"
echo "=========================================="
echo ""

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

success() {
    echo -e "${GREEN}✓ PASS${NC}: $1"
}

fail() {
    echo -e "${RED}✗ FAIL${NC}: $1"
}

info() {
    echo -e "${YELLOW}→${NC} $1"
}

# Test 1: Health Check
echo "--- Test 1: Health Check ---"
HEALTH=$(curl -s "$BASE_URL/health")
echo "$HEALTH" | python3 -m json.tool 2>/dev/null || echo "$HEALTH"
if echo "$HEALTH" | grep -q '"healthy":true'; then
    success "Health check passed"
else
    fail "Health check failed"
fi
echo ""

# Test 2: Get HSM Info
echo "--- Test 2: Get HSM Info ---"
INFO=$(curl -s "$BASE_URL/api/hsm/info")
echo "$INFO" | python3 -m json.tool 2>/dev/null || echo "$INFO"
if echo "$INFO" | grep -q '"active":true'; then
    success "HSM is active"
else
    fail "HSM is not active"
fi
echo ""

# Test 3: Get XPub (mainnet)
echo "--- Test 3: Get XPub (mainnet) ---"
XPUB=$(curl -s "$BASE_URL/api/hsm/xpub/mainnet")
echo "$XPUB" | python3 -m json.tool 2>/dev/null || echo "$XPUB"
if echo "$XPUB" | grep -q '"xpub"'; then
    success "Got mainnet xpub"
else
    fail "Failed to get xpub"
fi
echo ""

# Test 4: Get Public Keys (indices 0, 1, 2)
echo "--- Test 4: Get Public Keys ---"
for i in 0 1 2; do
    info "Getting pubkey at index $i..."
    PUBKEY=$(curl -s "$BASE_URL/api/hsm/pubkey/mainnet/$i")
    PUBKEY_HEX=$(echo "$PUBKEY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('pubkey',''))" 2>/dev/null)
    if [ -n "$PUBKEY_HEX" ] && [ ${#PUBKEY_HEX} -eq 66 ]; then
        success "Index $i: $PUBKEY_HEX"
    else
        fail "Failed to get pubkey at index $i"
        echo "$PUBKEY"
    fi
done
echo ""

# Store pubkey at index 0 for later tests
PUBKEY_0=$(curl -s "$BASE_URL/api/hsm/pubkey/mainnet/0" | python3 -c "import sys,json; print(json.load(sys.stdin).get('pubkey',''))" 2>/dev/null)
PUBKEY_1=$(curl -s "$BASE_URL/api/hsm/pubkey/mainnet/1" | python3 -c "import sys,json; print(json.load(sys.stdin).get('pubkey',''))" 2>/dev/null)

# Test 5: Sign with Schnorr
echo "--- Test 5: Sign with Schnorr ---"
TEST_HASH="0000000000000000000000000000000000000000000000000000000000000001"
info "Test hash: $TEST_HASH"
SIGN_RESULT=$(curl -s -X POST "$BASE_URL/api/hsm/sign" \
    -H "Content-Type: application/json" \
    -d "{\"network\":\"mainnet\",\"index\":0,\"hash\":\"$TEST_HASH\",\"algorithm\":\"schnorr\"}")
echo "$SIGN_RESULT" | python3 -m json.tool 2>/dev/null || echo "$SIGN_RESULT"
SIG_LEN=$(echo "$SIGN_RESULT" | python3 -c "import sys,json; print(len(json.load(sys.stdin).get('signature','')))" 2>/dev/null)
if [ "$SIG_LEN" = "128" ]; then
    success "Schnorr signature (64 bytes)"
else
    fail "Unexpected signature length: $SIG_LEN"
fi
echo ""

# Test 6: Sign with ECDSA
echo "--- Test 6: Sign with ECDSA ---"
ECDSA_RESULT=$(curl -s -X POST "$BASE_URL/api/hsm/sign" \
    -H "Content-Type: application/json" \
    -d "{\"network\":\"mainnet\",\"index\":0,\"hash\":\"$TEST_HASH\",\"algorithm\":\"ecdsa\"}")
echo "$ECDSA_RESULT" | python3 -m json.tool 2>/dev/null || echo "$ECDSA_RESULT"
if echo "$ECDSA_RESULT" | grep -q '"algorithm":"ecdsa"'; then
    success "ECDSA signature"
else
    fail "ECDSA signing failed"
fi
echo ""

# Test 7: ECDH
echo "--- Test 7: ECDH Shared Secret ---"
info "Computing ECDH between index 0 and index 1..."
ECDH_RESULT=$(curl -s -X POST "$BASE_URL/api/hsm/ecdh" \
    -H "Content-Type: application/json" \
    -d "{\"network\":\"mainnet\",\"index\":0,\"theirPubkey\":\"$PUBKEY_1\"}")
echo "$ECDH_RESULT" | python3 -m json.tool 2>/dev/null || echo "$ECDH_RESULT"
SECRET_LEN=$(echo "$ECDH_RESULT" | python3 -c "import sys,json; print(len(json.load(sys.stdin).get('sharedSecret','')))" 2>/dev/null)
if [ "$SECRET_LEN" = "64" ]; then
    success "ECDH shared secret (32 bytes)"
else
    fail "Unexpected shared secret length: $SECRET_LEN"
fi
echo ""

# Test 8: ECDH Symmetry
echo "--- Test 8: ECDH Symmetry Test ---"
info "Verifying ECDH is symmetric..."
ECDH_FORWARD=$(curl -s -X POST "$BASE_URL/api/hsm/ecdh" \
    -H "Content-Type: application/json" \
    -d "{\"network\":\"mainnet\",\"index\":0,\"theirPubkey\":\"$PUBKEY_1\"}" | python3 -c "import sys,json; print(json.load(sys.stdin).get('sharedSecret',''))" 2>/dev/null)
ECDH_REVERSE=$(curl -s -X POST "$BASE_URL/api/hsm/ecdh" \
    -H "Content-Type: application/json" \
    -d "{\"network\":\"mainnet\",\"index\":1,\"theirPubkey\":\"$PUBKEY_0\"}" | python3 -c "import sys,json; print(json.load(sys.stdin).get('sharedSecret',''))" 2>/dev/null)
if [ "$ECDH_FORWARD" = "$ECDH_REVERSE" ]; then
    success "ECDH is symmetric"
else
    fail "ECDH asymmetry detected!"
    echo "  Forward: $ECDH_FORWARD"
    echo "  Reverse: $ECDH_REVERSE"
fi
echo ""

# Test 9: Encryption/Decryption
echo "--- Test 9: ECIES Encryption/Decryption ---"
# "Hello, HSM!" in hex
PLAINTEXT_HEX="48656c6c6f2c2048534d21"
info "Plaintext (hex): $PLAINTEXT_HEX"
info "Plaintext (text): Hello, HSM!"

ENCRYPT_RESULT=$(curl -s -X POST "$BASE_URL/api/hsm/encrypt" \
    -H "Content-Type: application/json" \
    -d "{\"network\":\"mainnet\",\"index\":0,\"plaintext\":\"$PLAINTEXT_HEX\"}")
echo "Encryption result:"
echo "$ENCRYPT_RESULT" | python3 -m json.tool 2>/dev/null || echo "$ENCRYPT_RESULT"

# Extract encryption components
CIPHERTEXT=$(echo "$ENCRYPT_RESULT" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ciphertext',''))" 2>/dev/null)
NONCE=$(echo "$ENCRYPT_RESULT" | python3 -c "import sys,json; print(json.load(sys.stdin).get('nonce',''))" 2>/dev/null)
TAG=$(echo "$ENCRYPT_RESULT" | python3 -c "import sys,json; print(json.load(sys.stdin).get('tag',''))" 2>/dev/null)
EPHEMERAL=$(echo "$ENCRYPT_RESULT" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ephemeralPubkey',''))" 2>/dev/null)

if [ -n "$CIPHERTEXT" ] && [ -n "$NONCE" ] && [ -n "$TAG" ] && [ -n "$EPHEMERAL" ]; then
    success "Encryption successful"

    # Now decrypt
    info "Decrypting..."
    DECRYPT_RESULT=$(curl -s -X POST "$BASE_URL/api/hsm/decrypt" \
        -H "Content-Type: application/json" \
        -d "{\"network\":\"mainnet\",\"index\":0,\"ciphertext\":\"$CIPHERTEXT\",\"nonce\":\"$NONCE\",\"tag\":\"$TAG\",\"ephemeralPubkey\":\"$EPHEMERAL\"}")
    echo "Decryption result:"
    echo "$DECRYPT_RESULT" | python3 -m json.tool 2>/dev/null || echo "$DECRYPT_RESULT"

    DECRYPTED=$(echo "$DECRYPT_RESULT" | python3 -c "import sys,json; print(json.load(sys.stdin).get('plaintext',''))" 2>/dev/null)
    if [ "$DECRYPTED" = "$PLAINTEXT_HEX" ]; then
        success "Decryption matches original plaintext"
    else
        fail "Decryption mismatch!"
        echo "  Expected: $PLAINTEXT_HEX"
        echo "  Got: $DECRYPTED"
    fi
else
    fail "Encryption failed"
fi
echo ""

# Test 10: Testnet Keys
echo "--- Test 10: Testnet Keys ---"
TESTNET_XPUB=$(curl -s "$BASE_URL/api/hsm/xpub/testnet")
echo "$TESTNET_XPUB" | python3 -m json.tool 2>/dev/null || echo "$TESTNET_XPUB"
if echo "$TESTNET_XPUB" | grep -q '"xpub"'; then
    success "Got testnet xpub"
else
    fail "Failed to get testnet xpub"
fi
echo ""

# Test 11: Final HSM Info (check operations count increased)
echo "--- Test 11: Final HSM Info ---"
FINAL_INFO=$(curl -s "$BASE_URL/api/hsm/info")
echo "$FINAL_INFO" | python3 -m json.tool 2>/dev/null || echo "$FINAL_INFO"
OPS_COUNT=$(echo "$FINAL_INFO" | python3 -c "import sys,json; print(json.load(sys.stdin).get('operationsCount',0))" 2>/dev/null)
info "Total operations: $OPS_COUNT"
echo ""

echo "=========================================="
echo "Test Complete!"
echo "=========================================="
echo ""
echo "To lock HSM mode, run:"
echo "  curl -X POST $BASE_URL/api/hsm/lock"
