#ifndef AMALGAMATED_BUILD
#include "hsm.h"
#include "jade_assert.h"
#include "jade_log.h"
#include "random.h"
#include "sensitive.h"

#include <inttypes.h>
#include <string.h>
#include <time.h>

#include <mbedtls/gcm.h>
#include <sodium/utils.h>
#include <wally_bip32.h>
#include <wally_core.h>
#include <wally_crypto.h>

// Static HSM keychain
static hsm_keychain_t hsm_keychain;

// Path strings for display
static const char* HSM_PATH_MAINNET = "m/86'/0'/0'/6000'";
static const char* HSM_PATH_TESTNET = "m/86'/1'/0'/6000'";

void hsm_init(void)
{
    JADE_LOGI("Initializing HSM module");
    sodium_memzero(&hsm_keychain, sizeof(hsm_keychain));
    hsm_keychain.is_active = false;
}

bool hsm_is_active(void)
{
    return hsm_keychain.is_active;
}

const hsm_keychain_t* hsm_get_keychain(void)
{
    return hsm_keychain.is_active ? &hsm_keychain : NULL;
}

bool hsm_activate(const uint8_t* seed, size_t seed_len, uint8_t userdata)
{
    JADE_ASSERT(seed);
    JADE_ASSERT(seed_len == BIP32_ENTROPY_LEN_256 || seed_len == BIP32_ENTROPY_LEN_512);

    if (hsm_keychain.is_active) {
        JADE_LOGW("HSM already active");
        return false;
    }

    JADE_LOGI("Activating HSM mode");

    // Derive master key from seed
    struct ext_key master_key;
    SENSITIVE_PUSH(&master_key, sizeof(master_key));

    int ret = bip32_key_from_seed(seed, seed_len, BIP32_VER_MAIN_PRIVATE, 0, &master_key);
    if (ret != WALLY_OK) {
        JADE_LOGE("Failed to derive master key from seed: %d", ret);
        SENSITIVE_POP(&master_key);
        return false;
    }

    // Derive mainnet HSM key: m/86'/0'/0'/6000'
    struct ext_key mainnet_key;
    SENSITIVE_PUSH(&mainnet_key, sizeof(mainnet_key));

    uint32_t mainnet_path[] = { HSM_PATH_PURPOSE, HSM_PATH_COIN_MAIN, HSM_PATH_ACCOUNT, HSM_PATH_HSM_BRANCH };
    ret = bip32_key_from_parent_path(&master_key, mainnet_path, 4, BIP32_FLAG_KEY_PRIVATE, &mainnet_key);
    if (ret != WALLY_OK) {
        JADE_LOGE("Failed to derive mainnet HSM key: %d", ret);
        SENSITIVE_POP(&mainnet_key);
        SENSITIVE_POP(&master_key);
        return false;
    }

    // Derive testnet HSM key: m/86'/1'/0'/6000'
    struct ext_key testnet_key;
    SENSITIVE_PUSH(&testnet_key, sizeof(testnet_key));

    uint32_t testnet_path[] = { HSM_PATH_PURPOSE, HSM_PATH_COIN_TEST, HSM_PATH_ACCOUNT, HSM_PATH_HSM_BRANCH };
    ret = bip32_key_from_parent_path(&master_key, testnet_path, 4, BIP32_FLAG_KEY_PRIVATE, &testnet_key);
    if (ret != WALLY_OK) {
        JADE_LOGE("Failed to derive testnet HSM key: %d", ret);
        SENSITIVE_POP(&testnet_key);
        SENSITIVE_POP(&mainnet_key);
        SENSITIVE_POP(&master_key);
        return false;
    }

    // Store the derived keys
    memcpy(hsm_keychain.mainnet_private_key, mainnet_key.priv_key + 1, EC_PRIVATE_KEY_LEN);
    memcpy(hsm_keychain.mainnet_chain_code, mainnet_key.chain_code, 32);
    memcpy(hsm_keychain.mainnet_public_key, mainnet_key.pub_key, EC_PUBLIC_KEY_LEN);

    memcpy(hsm_keychain.testnet_private_key, testnet_key.priv_key + 1, EC_PRIVATE_KEY_LEN);
    memcpy(hsm_keychain.testnet_chain_code, testnet_key.chain_code, 32);
    memcpy(hsm_keychain.testnet_public_key, testnet_key.pub_key, EC_PUBLIC_KEY_LEN);

    // Clean up sensitive data
    SENSITIVE_POP(&testnet_key);
    SENSITIVE_POP(&mainnet_key);
    SENSITIVE_POP(&master_key);

    // Initialize state
    hsm_keychain.is_active = true;
    hsm_keychain.auto_lock_timeout = 0;  // Disabled by default
    hsm_keychain.last_activity_timestamp = (uint32_t)time(NULL);
    hsm_keychain.operations_count = 0;
    hsm_keychain.userdata = userdata;

    JADE_LOGI("HSM mode activated successfully");
    return true;
}

void hsm_deactivate(void)
{
    JADE_LOGI("Deactivating HSM mode");
    sodium_memzero(&hsm_keychain, sizeof(hsm_keychain));
    hsm_keychain.is_active = false;
}

bool hsm_is_unlocked_by_source(uint8_t source)
{
    return hsm_keychain.is_active && hsm_keychain.userdata == source;
}

void hsm_update_activity(void)
{
    if (hsm_keychain.is_active) {
        hsm_keychain.last_activity_timestamp = (uint32_t)time(NULL);
    }
}

bool hsm_check_timeout(void)
{
    if (!hsm_keychain.is_active || hsm_keychain.auto_lock_timeout == 0) {
        return false;
    }

    uint32_t now = (uint32_t)time(NULL);
    uint32_t elapsed = now - hsm_keychain.last_activity_timestamp;

    if (elapsed >= hsm_keychain.auto_lock_timeout) {
        JADE_LOGI("HSM auto-lock timeout expired");
        hsm_deactivate();
        return true;
    }

    return false;
}

void hsm_set_timeout(uint32_t timeout_seconds)
{
    if (hsm_keychain.is_active) {
        hsm_keychain.auto_lock_timeout = timeout_seconds;
        hsm_keychain.last_activity_timestamp = (uint32_t)time(NULL);
        JADE_LOGI("HSM auto-lock timeout set to %" PRIu32 " seconds", timeout_seconds);
    }
}

uint32_t hsm_get_timeout(void)
{
    return hsm_keychain.is_active ? hsm_keychain.auto_lock_timeout : 0;
}

uint32_t hsm_get_remaining_time(void)
{
    if (!hsm_keychain.is_active || hsm_keychain.auto_lock_timeout == 0) {
        return 0;
    }

    uint32_t now = (uint32_t)time(NULL);
    uint32_t elapsed = now - hsm_keychain.last_activity_timestamp;

    if (elapsed >= hsm_keychain.auto_lock_timeout) {
        return 0;
    }

    return hsm_keychain.auto_lock_timeout - elapsed;
}

void hsm_increment_ops(void)
{
    if (hsm_keychain.is_active) {
        hsm_keychain.operations_count++;
        hsm_update_activity();
    }
}

uint64_t hsm_get_ops_count(void)
{
    return hsm_keychain.is_active ? hsm_keychain.operations_count : 0;
}

// Internal helper to get keys for a network
static bool get_network_keys(hsm_network_t network, const uint8_t** privkey, const uint8_t** chaincode, const uint8_t** pubkey)
{
    if (!hsm_keychain.is_active) {
        return false;
    }

    if (network == HSM_NETWORK_MAINNET) {
        *privkey = hsm_keychain.mainnet_private_key;
        *chaincode = hsm_keychain.mainnet_chain_code;
        *pubkey = hsm_keychain.mainnet_public_key;
    } else {
        *privkey = hsm_keychain.testnet_private_key;
        *chaincode = hsm_keychain.testnet_chain_code;
        *pubkey = hsm_keychain.testnet_public_key;
    }

    return true;
}

bool hsm_derive_key(hsm_network_t network, uint32_t index,
                    uint8_t* privkey_out, size_t privkey_len,
                    uint8_t* pubkey_out, size_t pubkey_len)
{
    JADE_ASSERT(privkey_out);
    JADE_ASSERT(privkey_len >= EC_PRIVATE_KEY_LEN);
    JADE_ASSERT(pubkey_out);
    JADE_ASSERT(pubkey_len >= EC_PUBLIC_KEY_LEN);

    const uint8_t* root_privkey;
    const uint8_t* root_chaincode;
    const uint8_t* root_pubkey;

    if (!get_network_keys(network, &root_privkey, &root_chaincode, &root_pubkey)) {
        return false;
    }

    // Build ext_key from stored data
    struct ext_key root_key;
    SENSITIVE_PUSH(&root_key, sizeof(root_key));

    memset(&root_key, 0, sizeof(root_key));
    root_key.priv_key[0] = BIP32_FLAG_KEY_PRIVATE;
    memcpy(root_key.priv_key + 1, root_privkey, EC_PRIVATE_KEY_LEN);
    memcpy(root_key.chain_code, root_chaincode, 32);
    memcpy(root_key.pub_key, root_pubkey, EC_PUBLIC_KEY_LEN);
    root_key.depth = 4;  // Already at depth 4 (m/86'/coin'/0'/6000')
    root_key.version = (network == HSM_NETWORK_MAINNET) ? BIP32_VER_MAIN_PRIVATE : BIP32_VER_TEST_PRIVATE;

    // Derive child key (non-hardened)
    struct ext_key child_key;
    SENSITIVE_PUSH(&child_key, sizeof(child_key));

    int ret = bip32_key_from_parent(&root_key, index, BIP32_FLAG_KEY_PRIVATE, &child_key);
    if (ret != WALLY_OK) {
        JADE_LOGE("Failed to derive child key at index %" PRIu32 ": %d", index, ret);
        SENSITIVE_POP(&child_key);
        SENSITIVE_POP(&root_key);
        return false;
    }

    memcpy(privkey_out, child_key.priv_key + 1, EC_PRIVATE_KEY_LEN);
    memcpy(pubkey_out, child_key.pub_key, EC_PUBLIC_KEY_LEN);

    SENSITIVE_POP(&child_key);
    SENSITIVE_POP(&root_key);

    return true;
}

bool hsm_get_pubkey(hsm_network_t network, uint32_t index,
                    uint8_t* pubkey_out, size_t pubkey_len)
{
    uint8_t privkey[EC_PRIVATE_KEY_LEN];
    SENSITIVE_PUSH(privkey, sizeof(privkey));

    bool result = hsm_derive_key(network, index, privkey, sizeof(privkey), pubkey_out, pubkey_len);

    SENSITIVE_POP(privkey);
    return result;
}

bool hsm_get_root_pubkey(hsm_network_t network, uint8_t* pubkey_out, size_t pubkey_len)
{
    JADE_ASSERT(pubkey_out);
    JADE_ASSERT(pubkey_len >= EC_PUBLIC_KEY_LEN);

    const uint8_t* root_privkey;
    const uint8_t* root_chaincode;
    const uint8_t* root_pubkey;

    if (!get_network_keys(network, &root_privkey, &root_chaincode, &root_pubkey)) {
        return false;
    }

    memcpy(pubkey_out, root_pubkey, EC_PUBLIC_KEY_LEN);
    return true;
}

bool hsm_get_xpub(hsm_network_t network, char** xpub_out)
{
    JADE_ASSERT(xpub_out);

    const uint8_t* root_privkey;
    const uint8_t* root_chaincode;
    const uint8_t* root_pubkey;

    if (!get_network_keys(network, &root_privkey, &root_chaincode, &root_pubkey)) {
        return false;
    }

    // Compute hash160 of the public key (required for xpub serialization)
    uint8_t hash160[HASH160_LEN];
    int ret = wally_hash160(root_pubkey, EC_PUBLIC_KEY_LEN, hash160, sizeof(hash160));
    if (ret != WALLY_OK) {
        JADE_LOGE("Failed to compute hash160: %d", ret);
        return false;
    }

    // Use bip32_key_init to properly initialize the ext_key structure
    struct ext_key key;
    uint32_t version = (network == HSM_NETWORK_MAINNET) ? BIP32_VER_MAIN_PUBLIC : BIP32_VER_TEST_PUBLIC;

    ret = bip32_key_init(
        version,
        4,  // depth
        HSM_PATH_HSM_BRANCH,  // child_num
        root_chaincode, 32,
        root_pubkey, EC_PUBLIC_KEY_LEN,
        NULL, 0,  // no private key for public xpub
        hash160, sizeof(hash160),
        NULL, 0,  // parent160 not needed for serialization
        &key);

    if (ret != WALLY_OK) {
        JADE_LOGE("Failed to initialize ext_key: %d", ret);
        return false;
    }

    ret = bip32_key_to_base58(&key, BIP32_FLAG_KEY_PUBLIC, xpub_out);
    if (ret != WALLY_OK) {
        JADE_LOGE("Failed to serialize xpub: %d", ret);
        return false;
    }

    return true;
}

bool hsm_sign(hsm_network_t network, uint32_t index, hsm_algo_t algo,
              const uint8_t* hash, size_t hash_len,
              uint8_t* signature_out, size_t sig_out_len, size_t* sig_written)
{
    JADE_ASSERT(hash);
    JADE_ASSERT(hash_len == SHA256_LEN);
    JADE_ASSERT(signature_out);
    JADE_ASSERT(sig_written);

    uint8_t privkey[EC_PRIVATE_KEY_LEN];
    uint8_t pubkey[EC_PUBLIC_KEY_LEN];
    SENSITIVE_PUSH(privkey, sizeof(privkey));

    if (!hsm_derive_key(network, index, privkey, sizeof(privkey), pubkey, sizeof(pubkey))) {
        SENSITIVE_POP(privkey);
        return false;
    }

    int ret;
    if (algo == HSM_ALGO_SCHNORR) {
        // BIP-340 Schnorr signature (64 bytes)
        if (sig_out_len < EC_SIGNATURE_LEN) {
            JADE_LOGE("Output buffer too small for Schnorr signature");
            SENSITIVE_POP(privkey);
            return false;
        }

        uint8_t aux_rand[32];
        get_random(aux_rand, sizeof(aux_rand));

        ret = wally_ec_sig_from_bytes(privkey, sizeof(privkey), hash, hash_len,
                                      EC_FLAG_SCHNORR, signature_out, EC_SIGNATURE_LEN);
        if (ret != WALLY_OK) {
            JADE_LOGE("Schnorr signing failed: %d", ret);
            SENSITIVE_POP(privkey);
            return false;
        }
        *sig_written = EC_SIGNATURE_LEN;
    } else {
        // ECDSA DER signature
        uint8_t sig_compact[EC_SIGNATURE_LEN];
        ret = wally_ec_sig_from_bytes(privkey, sizeof(privkey), hash, hash_len,
                                      EC_FLAG_ECDSA, sig_compact, sizeof(sig_compact));
        if (ret != WALLY_OK) {
            JADE_LOGE("ECDSA signing failed: %d", ret);
            SENSITIVE_POP(privkey);
            return false;
        }

        // Convert to DER
        ret = wally_ec_sig_to_der(sig_compact, sizeof(sig_compact), signature_out, sig_out_len, sig_written);
        if (ret != WALLY_OK) {
            JADE_LOGE("DER encoding failed: %d", ret);
            SENSITIVE_POP(privkey);
            return false;
        }
    }

    SENSITIVE_POP(privkey);
    hsm_increment_ops();
    return true;
}

bool hsm_ecdh(hsm_network_t network, uint32_t index,
              const uint8_t* their_pubkey, size_t their_pubkey_len,
              uint8_t* shared_secret_out, size_t secret_len)
{
    JADE_ASSERT(their_pubkey);
    JADE_ASSERT(their_pubkey_len == EC_PUBLIC_KEY_LEN || their_pubkey_len == EC_PUBLIC_KEY_UNCOMPRESSED_LEN);
    JADE_ASSERT(shared_secret_out);
    JADE_ASSERT(secret_len >= SHA256_LEN);

    uint8_t privkey[EC_PRIVATE_KEY_LEN];
    uint8_t pubkey[EC_PUBLIC_KEY_LEN];
    SENSITIVE_PUSH(privkey, sizeof(privkey));

    if (!hsm_derive_key(network, index, privkey, sizeof(privkey), pubkey, sizeof(pubkey))) {
        SENSITIVE_POP(privkey);
        return false;
    }

    int ret = wally_ecdh(their_pubkey, their_pubkey_len, privkey, sizeof(privkey),
                         shared_secret_out, secret_len);
    if (ret != WALLY_OK) {
        JADE_LOGE("ECDH failed: %d", ret);
        SENSITIVE_POP(privkey);
        return false;
    }

    SENSITIVE_POP(privkey);
    hsm_increment_ops();
    return true;
}

bool hsm_encrypt(hsm_network_t network, uint32_t index,
                 const uint8_t* plaintext, size_t plaintext_len,
                 const uint8_t* their_pubkey, size_t their_pubkey_len,
                 const uint8_t* aad, size_t aad_len,
                 uint8_t* ciphertext_out, size_t* ciphertext_len,
                 uint8_t* nonce_out, size_t nonce_len,
                 uint8_t* tag_out, size_t tag_len,
                 uint8_t* ephemeral_pubkey_out, size_t ephemeral_pubkey_len)
{
    JADE_ASSERT(plaintext);
    JADE_ASSERT(plaintext_len <= HSM_MAX_PLAINTEXT_SIZE);
    JADE_ASSERT(ciphertext_out);
    JADE_ASSERT(ciphertext_len);
    JADE_ASSERT(nonce_out);
    JADE_ASSERT(nonce_len >= HSM_AES_NONCE_SIZE);
    JADE_ASSERT(tag_out);
    JADE_ASSERT(tag_len >= HSM_AES_TAG_SIZE);
    JADE_ASSERT(ephemeral_pubkey_out);
    JADE_ASSERT(ephemeral_pubkey_len >= EC_PUBLIC_KEY_LEN);

    // Generate ephemeral key pair
    uint8_t ephemeral_privkey[EC_PRIVATE_KEY_LEN];
    SENSITIVE_PUSH(ephemeral_privkey, sizeof(ephemeral_privkey));

    get_random(ephemeral_privkey, sizeof(ephemeral_privkey));

    int ret = wally_ec_public_key_from_private_key(ephemeral_privkey, sizeof(ephemeral_privkey),
                                                    ephemeral_pubkey_out, EC_PUBLIC_KEY_LEN);
    if (ret != WALLY_OK) {
        JADE_LOGE("Failed to derive ephemeral public key: %d", ret);
        SENSITIVE_POP(ephemeral_privkey);
        return false;
    }

    // Determine recipient pubkey
    uint8_t recipient_pubkey[EC_PUBLIC_KEY_LEN];
    if (their_pubkey && their_pubkey_len > 0) {
        // Encrypt to specified recipient
        if (their_pubkey_len == EC_PUBLIC_KEY_LEN) {
            memcpy(recipient_pubkey, their_pubkey, EC_PUBLIC_KEY_LEN);
        } else if (their_pubkey_len == EC_PUBLIC_KEY_UNCOMPRESSED_LEN) {
            // Compress uncompressed key
            ret = wally_ec_public_key_decompress(their_pubkey, their_pubkey_len,
                                                  recipient_pubkey, sizeof(recipient_pubkey));
            if (ret != WALLY_OK) {
                // Try direct copy for compressed
                memcpy(recipient_pubkey, their_pubkey, EC_PUBLIC_KEY_LEN);
            }
        } else {
            JADE_LOGE("Invalid recipient pubkey length");
            SENSITIVE_POP(ephemeral_privkey);
            return false;
        }
    } else {
        // Self-encryption: encrypt to our own pubkey at this index
        if (!hsm_get_pubkey(network, index, recipient_pubkey, sizeof(recipient_pubkey))) {
            SENSITIVE_POP(ephemeral_privkey);
            return false;
        }
    }

    // ECDH with ephemeral private key and recipient public key
    uint8_t shared_point[SHA256_LEN];
    SENSITIVE_PUSH(shared_point, sizeof(shared_point));

    ret = wally_ecdh(recipient_pubkey, sizeof(recipient_pubkey),
                     ephemeral_privkey, sizeof(ephemeral_privkey),
                     shared_point, sizeof(shared_point));
    if (ret != WALLY_OK) {
        JADE_LOGE("ECDH for encryption failed: %d", ret);
        SENSITIVE_POP(shared_point);
        SENSITIVE_POP(ephemeral_privkey);
        return false;
    }

    // Derive encryption key: SHA256(shared_point || ephemeral_pubkey || recipient_pubkey)
    uint8_t key_material[SHA256_LEN + EC_PUBLIC_KEY_LEN + EC_PUBLIC_KEY_LEN];
    memcpy(key_material, shared_point, SHA256_LEN);
    memcpy(key_material + SHA256_LEN, ephemeral_pubkey_out, EC_PUBLIC_KEY_LEN);
    memcpy(key_material + SHA256_LEN + EC_PUBLIC_KEY_LEN, recipient_pubkey, EC_PUBLIC_KEY_LEN);

    uint8_t encryption_key[SHA256_LEN];
    SENSITIVE_PUSH(encryption_key, sizeof(encryption_key));

    ret = wally_sha256(key_material, sizeof(key_material), encryption_key, sizeof(encryption_key));
    if (ret != WALLY_OK) {
        JADE_LOGE("Key derivation failed: %d", ret);
        SENSITIVE_POP(encryption_key);
        SENSITIVE_POP(shared_point);
        SENSITIVE_POP(ephemeral_privkey);
        return false;
    }

    // Generate random nonce
    get_random(nonce_out, HSM_AES_NONCE_SIZE);

    // AES-256-GCM encryption
    mbedtls_gcm_context gcm;
    mbedtls_gcm_init(&gcm);

    ret = mbedtls_gcm_setkey(&gcm, MBEDTLS_CIPHER_ID_AES, encryption_key, 256);
    if (ret != 0) {
        JADE_LOGE("AES key setup failed: %d", ret);
        mbedtls_gcm_free(&gcm);
        SENSITIVE_POP(encryption_key);
        SENSITIVE_POP(shared_point);
        SENSITIVE_POP(ephemeral_privkey);
        return false;
    }

    ret = mbedtls_gcm_crypt_and_tag(&gcm, MBEDTLS_GCM_ENCRYPT,
                                     plaintext_len, nonce_out, HSM_AES_NONCE_SIZE,
                                     aad, aad_len,
                                     plaintext, ciphertext_out,
                                     HSM_AES_TAG_SIZE, tag_out);

    mbedtls_gcm_free(&gcm);

    if (ret != 0) {
        JADE_LOGE("AES-GCM encryption failed: %d", ret);
        SENSITIVE_POP(encryption_key);
        SENSITIVE_POP(shared_point);
        SENSITIVE_POP(ephemeral_privkey);
        return false;
    }

    *ciphertext_len = plaintext_len;

    SENSITIVE_POP(encryption_key);
    SENSITIVE_POP(shared_point);
    SENSITIVE_POP(ephemeral_privkey);

    hsm_increment_ops();
    return true;
}

bool hsm_decrypt(hsm_network_t network, uint32_t index,
                 const uint8_t* ciphertext, size_t ciphertext_len,
                 const uint8_t* nonce, size_t nonce_len,
                 const uint8_t* tag, size_t tag_len,
                 const uint8_t* ephemeral_pubkey, size_t ephemeral_pubkey_len,
                 const uint8_t* aad, size_t aad_len,
                 uint8_t* plaintext_out, size_t* plaintext_len)
{
    JADE_ASSERT(ciphertext);
    JADE_ASSERT(nonce);
    JADE_ASSERT(nonce_len >= HSM_AES_NONCE_SIZE);
    JADE_ASSERT(tag);
    JADE_ASSERT(tag_len >= HSM_AES_TAG_SIZE);
    JADE_ASSERT(ephemeral_pubkey);
    JADE_ASSERT(ephemeral_pubkey_len == EC_PUBLIC_KEY_LEN);
    JADE_ASSERT(plaintext_out);
    JADE_ASSERT(plaintext_len);

    // Get our private key at this index
    uint8_t privkey[EC_PRIVATE_KEY_LEN];
    uint8_t our_pubkey[EC_PUBLIC_KEY_LEN];
    SENSITIVE_PUSH(privkey, sizeof(privkey));

    if (!hsm_derive_key(network, index, privkey, sizeof(privkey), our_pubkey, sizeof(our_pubkey))) {
        SENSITIVE_POP(privkey);
        return false;
    }

    // ECDH with our private key and sender's ephemeral public key
    uint8_t shared_point[SHA256_LEN];
    SENSITIVE_PUSH(shared_point, sizeof(shared_point));

    int ret = wally_ecdh(ephemeral_pubkey, ephemeral_pubkey_len,
                         privkey, sizeof(privkey),
                         shared_point, sizeof(shared_point));
    if (ret != WALLY_OK) {
        JADE_LOGE("ECDH for decryption failed: %d", ret);
        SENSITIVE_POP(shared_point);
        SENSITIVE_POP(privkey);
        return false;
    }

    // Derive decryption key: SHA256(shared_point || ephemeral_pubkey || our_pubkey)
    uint8_t key_material[SHA256_LEN + EC_PUBLIC_KEY_LEN + EC_PUBLIC_KEY_LEN];
    memcpy(key_material, shared_point, SHA256_LEN);
    memcpy(key_material + SHA256_LEN, ephemeral_pubkey, EC_PUBLIC_KEY_LEN);
    memcpy(key_material + SHA256_LEN + EC_PUBLIC_KEY_LEN, our_pubkey, EC_PUBLIC_KEY_LEN);

    uint8_t decryption_key[SHA256_LEN];
    SENSITIVE_PUSH(decryption_key, sizeof(decryption_key));

    ret = wally_sha256(key_material, sizeof(key_material), decryption_key, sizeof(decryption_key));
    if (ret != WALLY_OK) {
        JADE_LOGE("Key derivation failed: %d", ret);
        SENSITIVE_POP(decryption_key);
        SENSITIVE_POP(shared_point);
        SENSITIVE_POP(privkey);
        return false;
    }

    // AES-256-GCM decryption
    mbedtls_gcm_context gcm;
    mbedtls_gcm_init(&gcm);

    ret = mbedtls_gcm_setkey(&gcm, MBEDTLS_CIPHER_ID_AES, decryption_key, 256);
    if (ret != 0) {
        JADE_LOGE("AES key setup failed: %d", ret);
        mbedtls_gcm_free(&gcm);
        SENSITIVE_POP(decryption_key);
        SENSITIVE_POP(shared_point);
        SENSITIVE_POP(privkey);
        return false;
    }

    ret = mbedtls_gcm_auth_decrypt(&gcm, ciphertext_len,
                                    nonce, HSM_AES_NONCE_SIZE,
                                    aad, aad_len,
                                    tag, HSM_AES_TAG_SIZE,
                                    ciphertext, plaintext_out);

    mbedtls_gcm_free(&gcm);

    if (ret != 0) {
        JADE_LOGE("AES-GCM decryption failed (authentication error): %d", ret);
        SENSITIVE_POP(decryption_key);
        SENSITIVE_POP(shared_point);
        SENSITIVE_POP(privkey);
        return false;
    }

    *plaintext_len = ciphertext_len;

    SENSITIVE_POP(decryption_key);
    SENSITIVE_POP(shared_point);
    SENSITIVE_POP(privkey);

    hsm_increment_ops();
    return true;
}

bool hsm_parse_network(const char* network_str, size_t str_len, hsm_network_t* network_out)
{
    JADE_ASSERT(network_str);
    JADE_ASSERT(network_out);

    if (str_len == 7 && memcmp(network_str, "mainnet", 7) == 0) {
        *network_out = HSM_NETWORK_MAINNET;
        return true;
    } else if (str_len == 7 && memcmp(network_str, "testnet", 7) == 0) {
        *network_out = HSM_NETWORK_TESTNET;
        return true;
    }

    return false;
}

bool hsm_parse_algo(const char* algo_str, size_t str_len, hsm_algo_t* algo_out)
{
    JADE_ASSERT(algo_out);

    if (!algo_str || str_len == 0) {
        // Default to Schnorr
        *algo_out = HSM_ALGO_SCHNORR;
        return true;
    }

    if (str_len == 7 && memcmp(algo_str, "schnorr", 7) == 0) {
        *algo_out = HSM_ALGO_SCHNORR;
        return true;
    } else if (str_len == 5 && memcmp(algo_str, "ecdsa", 5) == 0) {
        *algo_out = HSM_ALGO_ECDSA;
        return true;
    }

    return false;
}

const char* hsm_get_path_string(hsm_network_t network)
{
    return (network == HSM_NETWORK_MAINNET) ? HSM_PATH_MAINNET : HSM_PATH_TESTNET;
}

#endif // AMALGAMATED_BUILD
