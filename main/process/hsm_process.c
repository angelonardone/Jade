#ifndef AMALGAMATED_BUILD
#include "../hsm.h"
#include "../jade_assert.h"
#include "../jade_log.h"
#include "../process.h"
#include "../ui.h"
#include "../utils/cbor_rpc.h"
#include "../utils/malloc_ext.h"

#include "process_utils.h"

#include <inttypes.h>
#include <string.h>
#include <wally_core.h>

// HSM RPC error codes
#define HSM_ERROR_NOT_ACTIVE -32001
#define HSM_ERROR_INVALID_INDEX -32002
#define HSM_ERROR_DECRYPT_FAILED -32003
#define HSM_ERROR_PAYLOAD_TOO_LARGE -32004
#define HSM_ERROR_ALREADY_ACTIVE -32006
#define HSM_ERROR_INVALID_NETWORK -32008
#define HSM_ERROR_INVALID_ALGO -32009

// Check if HSM is active and unlocked by message source
#define HSM_UNLOCKED_BY_MESSAGE_SOURCE(process) \
    (hsm_is_active() && hsm_is_unlocked_by_source((uint8_t)process->ctx.source))

#define ASSERT_HSM_UNLOCKED_BY_MESSAGE_SOURCE(process) JADE_ASSERT(HSM_UNLOCKED_BY_MESSAGE_SOURCE(process))

// Helper to extract network parameter
static bool get_network_param(CborValue* params, hsm_network_t* network, const char** errmsg)
{
    const char* network_str = NULL;
    size_t network_len = 0;
    rpc_get_string_ptr("network", params, &network_str, &network_len);
    if (!network_str || network_len == 0) {
        *errmsg = "Missing or invalid 'network' parameter";
        return false;
    }

    if (!hsm_parse_network(network_str, network_len, network)) {
        *errmsg = "Invalid network - must be 'mainnet' or 'testnet'";
        return false;
    }

    return true;
}

// Helper to extract index parameter
static bool get_index_param(CborValue* params, uint32_t* index, const char** errmsg)
{
    size_t idx = 0;
    if (!rpc_get_sizet("index", params, &idx)) {
        *errmsg = "Missing or invalid 'index' parameter";
        return false;
    }

    // Index must be non-hardened (< 2^31)
    if (idx >= 0x80000000) {
        *errmsg = "Index must be less than 2^31 (non-hardened)";
        return false;
    }

    *index = (uint32_t)idx;
    return true;
}

// Callback to build hsm_get_info result
static void hsm_get_info_result_cb(const void* ctx, CborEncoder* container)
{
    const bool is_active = *(const bool*)ctx;

    // Create result map
    CborEncoder result_map;
    CborError cberr = cbor_encoder_create_map(container, &result_map, CborIndefiniteLength);
    JADE_ASSERT(cberr == CborNoError);

    add_boolean_to_map(&result_map, "active", is_active);

    if (is_active) {
        // Add networks array
        CborEncoder networks_array;
        cberr = cbor_encode_text_stringz(&result_map, "networks");
        JADE_ASSERT(cberr == CborNoError);
        cberr = cbor_encoder_create_array(&result_map, &networks_array, 2);
        JADE_ASSERT(cberr == CborNoError);
        cberr = cbor_encode_text_stringz(&networks_array, "mainnet");
        JADE_ASSERT(cberr == CborNoError);
        cberr = cbor_encode_text_stringz(&networks_array, "testnet");
        JADE_ASSERT(cberr == CborNoError);
        cberr = cbor_encoder_close_container(&result_map, &networks_array);
        JADE_ASSERT(cberr == CborNoError);

        // Mainnet info
        add_string_to_map(&result_map, "mainnet_root_path", hsm_get_path_string(HSM_NETWORK_MAINNET));

        uint8_t mainnet_pubkey[EC_PUBLIC_KEY_LEN];
        if (hsm_get_root_pubkey(HSM_NETWORK_MAINNET, mainnet_pubkey, sizeof(mainnet_pubkey))) {
            add_bytes_to_map(&result_map, "mainnet_root_pubkey", mainnet_pubkey, sizeof(mainnet_pubkey));
        }

        // Testnet info
        add_string_to_map(&result_map, "testnet_root_path", hsm_get_path_string(HSM_NETWORK_TESTNET));

        uint8_t testnet_pubkey[EC_PUBLIC_KEY_LEN];
        if (hsm_get_root_pubkey(HSM_NETWORK_TESTNET, testnet_pubkey, sizeof(testnet_pubkey))) {
            add_bytes_to_map(&result_map, "testnet_root_pubkey", testnet_pubkey, sizeof(testnet_pubkey));
        }

        // Operations count
        add_uint_to_map(&result_map, "operations_count", hsm_get_ops_count());

        // Auto-lock info
        add_uint_to_map(&result_map, "auto_lock_timeout", hsm_get_timeout());
        uint32_t remaining = hsm_get_remaining_time();
        if (remaining > 0) {
            add_uint_to_map(&result_map, "auto_lock_remaining", remaining);
        }
    }

    cberr = cbor_encoder_close_container(container, &result_map);
    JADE_ASSERT(cberr == CborNoError);
}

// hsm_get_info - Get HSM status and configuration
void hsm_get_info_process(void* process_ptr)
{
    JADE_LOGI("Starting: hsm_get_info");
    jade_process_t* process = process_ptr;

    ASSERT_CURRENT_MESSAGE(process, "hsm_get_info");

    const bool is_active = hsm_is_active();

    uint8_t buf[512];
    jade_process_reply_to_message_result(process->ctx, buf, sizeof(buf), &is_active, hsm_get_info_result_cb);

    JADE_LOGI("Success");
}

// hsm_get_pubkey - Get public key for network + index
void hsm_get_pubkey_process(void* process_ptr)
{
    JADE_LOGI("Starting: hsm_get_pubkey");
    jade_process_t* process = process_ptr;

    ASSERT_CURRENT_MESSAGE(process, "hsm_get_pubkey");

    if (!HSM_UNLOCKED_BY_MESSAGE_SOURCE(process)) {
        jade_process_reject_message(process, HSM_ERROR_NOT_ACTIVE, "HSM mode not active");
        goto cleanup;
    }

    GET_MSG_PARAMS(process);
    const char* errmsg = NULL;

    hsm_network_t network;
    if (!get_network_param(&params, &network, &errmsg)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_NETWORK, errmsg);
        goto cleanup;
    }

    uint32_t index;
    if (!get_index_param(&params, &index, &errmsg)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_INDEX, errmsg);
        goto cleanup;
    }

    uint8_t pubkey[EC_PUBLIC_KEY_LEN];
    if (!hsm_get_pubkey(network, index, pubkey, sizeof(pubkey))) {
        jade_process_reject_message(process, CBOR_RPC_INTERNAL_ERROR, "Failed to derive public key");
        goto cleanup;
    }

    // Build response with pubkey and path
    uint8_t buf[256];
    CborEncoder root_encoder;
    cbor_encoder_init(&root_encoder, buf, sizeof(buf), 0);

    CborEncoder root_map;
    CborError cberr = cbor_encoder_create_map(&root_encoder, &root_map, 2);  // id + result
    JADE_ASSERT(cberr == CborNoError);

    const char* id = NULL;
    size_t id_len = 0;
    rpc_get_id_ptr(&process->ctx.value, &id, &id_len);
    rpc_init_cbor(&root_map, id, id_len);  // Adds "id" and "result" key

    CborEncoder result_map;
    cberr = cbor_encoder_create_map(&root_map, &result_map, 2);  // pubkey + path
    JADE_ASSERT(cberr == CborNoError);

    add_bytes_to_map(&result_map, "pubkey", pubkey, sizeof(pubkey));

    // Build path string
    char path_str[64];
    snprintf(path_str, sizeof(path_str), "%s/%" PRIu32, hsm_get_path_string(network), index);
    add_string_to_map(&result_map, "path", path_str);

    cberr = cbor_encoder_close_container(&root_map, &result_map);
    JADE_ASSERT(cberr == CborNoError);
    cberr = cbor_encoder_close_container(&root_encoder, &root_map);
    JADE_ASSERT(cberr == CborNoError);

    const size_t cbor_len = cbor_encoder_get_buffer_size(&root_encoder, buf);
    jade_process_reply_to_message_ex(process->ctx.source, buf, cbor_len);

    JADE_LOGI("Success");

cleanup:
    return;
}

// hsm_get_xpub - Get extended public key for HSM root
void hsm_get_xpub_process(void* process_ptr)
{
    JADE_LOGI("Starting: hsm_get_xpub");
    jade_process_t* process = process_ptr;

    ASSERT_CURRENT_MESSAGE(process, "hsm_get_xpub");

    if (!HSM_UNLOCKED_BY_MESSAGE_SOURCE(process)) {
        jade_process_reject_message(process, HSM_ERROR_NOT_ACTIVE, "HSM mode not active");
        goto cleanup;
    }

    GET_MSG_PARAMS(process);
    const char* errmsg = NULL;

    hsm_network_t network;
    if (!get_network_param(&params, &network, &errmsg)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_NETWORK, errmsg);
        goto cleanup;
    }

    char* xpub = NULL;
    if (!hsm_get_xpub(network, &xpub)) {
        jade_process_reject_message(process, CBOR_RPC_INTERNAL_ERROR, "Failed to generate xpub");
        goto cleanup;
    }

    // Build response
    uint8_t buf[256];
    CborEncoder root_encoder;
    cbor_encoder_init(&root_encoder, buf, sizeof(buf), 0);

    CborEncoder root_map;
    CborError cberr = cbor_encoder_create_map(&root_encoder, &root_map, 2);  // Fixed size: id + result
    JADE_ASSERT(cberr == CborNoError);

    const char* id = NULL;
    size_t id_len = 0;
    rpc_get_id_ptr(&process->ctx.value, &id, &id_len);
    rpc_init_cbor(&root_map, id, id_len);  // Adds "id" and "result" key

    CborEncoder result_map;
    cberr = cbor_encoder_create_map(&root_map, &result_map, 2);  // Fixed size: xpub + path
    JADE_ASSERT(cberr == CborNoError);

    add_string_to_map(&result_map, "xpub", xpub);
    add_string_to_map(&result_map, "path", hsm_get_path_string(network));

    cberr = cbor_encoder_close_container(&root_map, &result_map);
    JADE_ASSERT(cberr == CborNoError);
    cberr = cbor_encoder_close_container(&root_encoder, &root_map);
    JADE_ASSERT(cberr == CborNoError);

    const size_t cbor_len = cbor_encoder_get_buffer_size(&root_encoder, buf);
    jade_process_reply_to_message_ex(process->ctx.source, buf, cbor_len);

    wally_free_string(xpub);
    JADE_LOGI("Success");

cleanup:
    return;
}

// hsm_sign - Sign hash with Schnorr or ECDSA
void hsm_sign_process(void* process_ptr)
{
    JADE_LOGI("Starting: hsm_sign");
    jade_process_t* process = process_ptr;

    ASSERT_CURRENT_MESSAGE(process, "hsm_sign");

    if (!HSM_UNLOCKED_BY_MESSAGE_SOURCE(process)) {
        jade_process_reject_message(process, HSM_ERROR_NOT_ACTIVE, "HSM mode not active");
        goto cleanup;
    }

    GET_MSG_PARAMS(process);
    const char* errmsg = NULL;

    hsm_network_t network;
    if (!get_network_param(&params, &network, &errmsg)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_NETWORK, errmsg);
        goto cleanup;
    }

    uint32_t index;
    if (!get_index_param(&params, &index, &errmsg)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_INDEX, errmsg);
        goto cleanup;
    }

    // Get hash
    const uint8_t* hash = NULL;
    size_t hash_len = 0;
    rpc_get_bytes_ptr("hash", &params, &hash, &hash_len);
    if (!hash || hash_len != SHA256_LEN) {
        jade_process_reject_message(process, CBOR_RPC_BAD_PARAMETERS, "Missing or invalid 'hash' parameter (must be 32 bytes)");
        goto cleanup;
    }

    // Get algorithm (optional, defaults to schnorr)
    const char* algo_str = NULL;
    size_t algo_len = 0;
    rpc_get_string_ptr("algo", &params, &algo_str, &algo_len);

    hsm_algo_t algo;
    if (!hsm_parse_algo(algo_str, algo_len, &algo)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_ALGO, "Invalid algorithm - must be 'schnorr' or 'ecdsa'");
        goto cleanup;
    }

    // Sign
    uint8_t signature[EC_SIGNATURE_DER_MAX_LEN];
    size_t sig_len = 0;
    if (!hsm_sign(network, index, algo, hash, hash_len, signature, sizeof(signature), &sig_len)) {
        jade_process_reject_message(process, CBOR_RPC_INTERNAL_ERROR, "Signing failed");
        goto cleanup;
    }

    // Get pubkey for response
    uint8_t pubkey[EC_PUBLIC_KEY_LEN];
    if (!hsm_get_pubkey(network, index, pubkey, sizeof(pubkey))) {
        jade_process_reject_message(process, CBOR_RPC_INTERNAL_ERROR, "Failed to get public key");
        goto cleanup;
    }

    // Build response
    uint8_t buf[256];
    CborEncoder root_encoder;
    cbor_encoder_init(&root_encoder, buf, sizeof(buf), 0);

    CborEncoder root_map;
    CborError cberr = cbor_encoder_create_map(&root_encoder, &root_map, 2);  // id + result
    JADE_ASSERT(cberr == CborNoError);

    const char* id = NULL;
    size_t id_len = 0;
    rpc_get_id_ptr(&process->ctx.value, &id, &id_len);
    rpc_init_cbor(&root_map, id, id_len);  // Adds "id" and "result" key

    CborEncoder result_map;
    cberr = cbor_encoder_create_map(&root_map, &result_map, 3);  // signature + pubkey + algo
    JADE_ASSERT(cberr == CborNoError);

    add_bytes_to_map(&result_map, "signature", signature, sig_len);
    add_bytes_to_map(&result_map, "pubkey", pubkey, sizeof(pubkey));
    add_string_to_map(&result_map, "algo", (algo == HSM_ALGO_SCHNORR) ? "schnorr" : "ecdsa");

    cberr = cbor_encoder_close_container(&root_map, &result_map);
    JADE_ASSERT(cberr == CborNoError);
    cberr = cbor_encoder_close_container(&root_encoder, &root_map);
    JADE_ASSERT(cberr == CborNoError);

    const size_t cbor_len = cbor_encoder_get_buffer_size(&root_encoder, buf);
    jade_process_reply_to_message_ex(process->ctx.source, buf, cbor_len);

    JADE_LOGI("Success");

cleanup:
    return;
}

// hsm_ecdh - Compute ECDH shared secret
void hsm_ecdh_process(void* process_ptr)
{
    JADE_LOGI("Starting: hsm_ecdh");
    jade_process_t* process = process_ptr;

    ASSERT_CURRENT_MESSAGE(process, "hsm_ecdh");

    if (!HSM_UNLOCKED_BY_MESSAGE_SOURCE(process)) {
        jade_process_reject_message(process, HSM_ERROR_NOT_ACTIVE, "HSM mode not active");
        goto cleanup;
    }

    GET_MSG_PARAMS(process);
    const char* errmsg = NULL;

    hsm_network_t network;
    if (!get_network_param(&params, &network, &errmsg)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_NETWORK, errmsg);
        goto cleanup;
    }

    uint32_t index;
    if (!get_index_param(&params, &index, &errmsg)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_INDEX, errmsg);
        goto cleanup;
    }

    // Get their pubkey
    const uint8_t* their_pubkey = NULL;
    size_t their_pubkey_len = 0;
    rpc_get_bytes_ptr("their_pubkey", &params, &their_pubkey, &their_pubkey_len);
    if (!their_pubkey) {
        jade_process_reject_message(process, CBOR_RPC_BAD_PARAMETERS, "Missing 'their_pubkey' parameter");
        goto cleanup;
    }

    if (their_pubkey_len != EC_PUBLIC_KEY_LEN && their_pubkey_len != EC_PUBLIC_KEY_UNCOMPRESSED_LEN) {
        jade_process_reject_message(process, CBOR_RPC_BAD_PARAMETERS, "Invalid pubkey length (must be 33 or 65 bytes)");
        goto cleanup;
    }

    uint8_t shared_secret[SHA256_LEN];
    if (!hsm_ecdh(network, index, their_pubkey, their_pubkey_len, shared_secret, sizeof(shared_secret))) {
        jade_process_reject_message(process, CBOR_RPC_INTERNAL_ERROR, "ECDH failed");
        goto cleanup;
    }

    // Build response
    uint8_t buf[128];
    CborEncoder root_encoder;
    cbor_encoder_init(&root_encoder, buf, sizeof(buf), 0);

    CborEncoder root_map;
    CborError cberr = cbor_encoder_create_map(&root_encoder, &root_map, 2);  // id + result
    JADE_ASSERT(cberr == CborNoError);

    const char* id = NULL;
    size_t id_len = 0;
    rpc_get_id_ptr(&process->ctx.value, &id, &id_len);
    rpc_init_cbor(&root_map, id, id_len);  // Adds "id" and "result" key

    CborEncoder result_map;
    cberr = cbor_encoder_create_map(&root_map, &result_map, 1);  // shared_secret only
    JADE_ASSERT(cberr == CborNoError);

    add_bytes_to_map(&result_map, "shared_secret", shared_secret, sizeof(shared_secret));

    cberr = cbor_encoder_close_container(&root_map, &result_map);
    JADE_ASSERT(cberr == CborNoError);
    cberr = cbor_encoder_close_container(&root_encoder, &root_map);
    JADE_ASSERT(cberr == CborNoError);

    const size_t cbor_len = cbor_encoder_get_buffer_size(&root_encoder, buf);
    jade_process_reply_to_message_ex(process->ctx.source, buf, cbor_len);

    JADE_LOGI("Success");

cleanup:
    return;
}

// hsm_encrypt - ECIES encryption
void hsm_encrypt_process(void* process_ptr)
{
    JADE_LOGI("Starting: hsm_encrypt");
    jade_process_t* process = process_ptr;

    ASSERT_CURRENT_MESSAGE(process, "hsm_encrypt");

    if (!HSM_UNLOCKED_BY_MESSAGE_SOURCE(process)) {
        jade_process_reject_message(process, HSM_ERROR_NOT_ACTIVE, "HSM mode not active");
        goto cleanup;
    }

    GET_MSG_PARAMS(process);
    const char* errmsg = NULL;

    hsm_network_t network;
    if (!get_network_param(&params, &network, &errmsg)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_NETWORK, errmsg);
        goto cleanup;
    }

    uint32_t index;
    if (!get_index_param(&params, &index, &errmsg)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_INDEX, errmsg);
        goto cleanup;
    }

    // Get plaintext
    const uint8_t* plaintext = NULL;
    size_t plaintext_len = 0;
    rpc_get_bytes_ptr("plaintext", &params, &plaintext, &plaintext_len);
    if (!plaintext || plaintext_len == 0) {
        jade_process_reject_message(process, CBOR_RPC_BAD_PARAMETERS, "Missing or empty 'plaintext' parameter");
        goto cleanup;
    }

    if (plaintext_len > HSM_MAX_PLAINTEXT_SIZE) {
        jade_process_reject_message(process, HSM_ERROR_PAYLOAD_TOO_LARGE, "Plaintext exceeds maximum size (1024 bytes)");
        goto cleanup;
    }

    // Get optional their_pubkey (for encrypting to another party)
    const uint8_t* their_pubkey = NULL;
    size_t their_pubkey_len = 0;
    rpc_get_bytes_ptr("their_pubkey", &params, &their_pubkey, &their_pubkey_len);

    // Get optional AAD
    const uint8_t* aad = NULL;
    size_t aad_len = 0;
    rpc_get_bytes_ptr("aad", &params, &aad, &aad_len);

    // Allocate output buffers
    uint8_t* ciphertext = JADE_MALLOC(plaintext_len);
    size_t ciphertext_len = 0;
    uint8_t nonce[HSM_AES_NONCE_SIZE];
    uint8_t tag[HSM_AES_TAG_SIZE];
    uint8_t ephemeral_pubkey[EC_PUBLIC_KEY_LEN];

    bool success = hsm_encrypt(network, index,
                               plaintext, plaintext_len,
                               their_pubkey, their_pubkey_len,
                               aad, aad_len,
                               ciphertext, &ciphertext_len,
                               nonce, sizeof(nonce),
                               tag, sizeof(tag),
                               ephemeral_pubkey, sizeof(ephemeral_pubkey));

    if (!success) {
        free(ciphertext);
        jade_process_reject_message(process, CBOR_RPC_INTERNAL_ERROR, "Encryption failed");
        goto cleanup;
    }

    // Build response
    uint8_t buf[HSM_MAX_PLAINTEXT_SIZE + 256];
    CborEncoder root_encoder;
    cbor_encoder_init(&root_encoder, buf, sizeof(buf), 0);

    CborEncoder root_map;
    CborError cberr = cbor_encoder_create_map(&root_encoder, &root_map, 2);  // id + result
    JADE_ASSERT(cberr == CborNoError);

    const char* id = NULL;
    size_t id_len = 0;
    rpc_get_id_ptr(&process->ctx.value, &id, &id_len);
    rpc_init_cbor(&root_map, id, id_len);  // Adds "id" and "result" key

    CborEncoder result_map;
    cberr = cbor_encoder_create_map(&root_map, &result_map, 4);  // ciphertext + nonce + tag + ephemeral_pubkey
    JADE_ASSERT(cberr == CborNoError);

    add_bytes_to_map(&result_map, "ciphertext", ciphertext, ciphertext_len);
    add_bytes_to_map(&result_map, "nonce", nonce, sizeof(nonce));
    add_bytes_to_map(&result_map, "tag", tag, sizeof(tag));
    add_bytes_to_map(&result_map, "ephemeral_pubkey", ephemeral_pubkey, sizeof(ephemeral_pubkey));

    cberr = cbor_encoder_close_container(&root_map, &result_map);
    JADE_ASSERT(cberr == CborNoError);
    cberr = cbor_encoder_close_container(&root_encoder, &root_map);
    JADE_ASSERT(cberr == CborNoError);

    const size_t cbor_len = cbor_encoder_get_buffer_size(&root_encoder, buf);
    jade_process_reply_to_message_ex(process->ctx.source, buf, cbor_len);

    free(ciphertext);
    JADE_LOGI("Success");

cleanup:
    return;
}

// hsm_decrypt - ECIES decryption
void hsm_decrypt_process(void* process_ptr)
{
    JADE_LOGI("Starting: hsm_decrypt");
    jade_process_t* process = process_ptr;

    ASSERT_CURRENT_MESSAGE(process, "hsm_decrypt");

    if (!HSM_UNLOCKED_BY_MESSAGE_SOURCE(process)) {
        jade_process_reject_message(process, HSM_ERROR_NOT_ACTIVE, "HSM mode not active");
        goto cleanup;
    }

    GET_MSG_PARAMS(process);
    const char* errmsg = NULL;

    hsm_network_t network;
    if (!get_network_param(&params, &network, &errmsg)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_NETWORK, errmsg);
        goto cleanup;
    }

    uint32_t index;
    if (!get_index_param(&params, &index, &errmsg)) {
        jade_process_reject_message(process, HSM_ERROR_INVALID_INDEX, errmsg);
        goto cleanup;
    }

    // Get ciphertext
    const uint8_t* ciphertext = NULL;
    size_t ciphertext_len = 0;
    rpc_get_bytes_ptr("ciphertext", &params, &ciphertext, &ciphertext_len);
    if (!ciphertext || ciphertext_len == 0) {
        jade_process_reject_message(process, CBOR_RPC_BAD_PARAMETERS, "Missing or empty 'ciphertext' parameter");
        goto cleanup;
    }

    // Get nonce
    const uint8_t* nonce = NULL;
    size_t nonce_len = 0;
    rpc_get_bytes_ptr("nonce", &params, &nonce, &nonce_len);
    if (!nonce || nonce_len != HSM_AES_NONCE_SIZE) {
        jade_process_reject_message(process, CBOR_RPC_BAD_PARAMETERS, "Missing or invalid 'nonce' parameter (must be 12 bytes)");
        goto cleanup;
    }

    // Get tag
    const uint8_t* tag = NULL;
    size_t tag_len = 0;
    rpc_get_bytes_ptr("tag", &params, &tag, &tag_len);
    if (!tag || tag_len != HSM_AES_TAG_SIZE) {
        jade_process_reject_message(process, CBOR_RPC_BAD_PARAMETERS, "Missing or invalid 'tag' parameter (must be 16 bytes)");
        goto cleanup;
    }

    // Get ephemeral_pubkey
    const uint8_t* ephemeral_pubkey = NULL;
    size_t ephemeral_pubkey_len = 0;
    rpc_get_bytes_ptr("ephemeral_pubkey", &params, &ephemeral_pubkey, &ephemeral_pubkey_len);
    if (!ephemeral_pubkey || ephemeral_pubkey_len != EC_PUBLIC_KEY_LEN) {
        jade_process_reject_message(process, CBOR_RPC_BAD_PARAMETERS,
                                    "Missing or invalid 'ephemeral_pubkey' parameter (must be 33 bytes)");
        goto cleanup;
    }

    // Get optional AAD
    const uint8_t* aad = NULL;
    size_t aad_len = 0;
    rpc_get_bytes_ptr("aad", &params, &aad, &aad_len);

    // Allocate output buffer
    uint8_t* plaintext = JADE_MALLOC(ciphertext_len);
    size_t plaintext_len = 0;

    bool success = hsm_decrypt(network, index,
                               ciphertext, ciphertext_len,
                               nonce, nonce_len,
                               tag, tag_len,
                               ephemeral_pubkey, ephemeral_pubkey_len,
                               aad, aad_len,
                               plaintext, &plaintext_len);

    if (!success) {
        free(plaintext);
        jade_process_reject_message(process, HSM_ERROR_DECRYPT_FAILED, "Decryption failed (authentication error)");
        goto cleanup;
    }

    // Build response
    uint8_t buf[HSM_MAX_PLAINTEXT_SIZE + 128];
    CborEncoder root_encoder;
    cbor_encoder_init(&root_encoder, buf, sizeof(buf), 0);

    CborEncoder root_map;
    CborError cberr = cbor_encoder_create_map(&root_encoder, &root_map, 2);  // id + result
    JADE_ASSERT(cberr == CborNoError);

    const char* id = NULL;
    size_t id_len = 0;
    rpc_get_id_ptr(&process->ctx.value, &id, &id_len);
    rpc_init_cbor(&root_map, id, id_len);  // Adds "id" and "result" key

    CborEncoder result_map;
    cberr = cbor_encoder_create_map(&root_map, &result_map, 1);  // plaintext only
    JADE_ASSERT(cberr == CborNoError);

    add_bytes_to_map(&result_map, "plaintext", plaintext, plaintext_len);

    cberr = cbor_encoder_close_container(&root_map, &result_map);
    JADE_ASSERT(cberr == CborNoError);
    cberr = cbor_encoder_close_container(&root_encoder, &root_map);
    JADE_ASSERT(cberr == CborNoError);

    const size_t cbor_len = cbor_encoder_get_buffer_size(&root_encoder, buf);
    jade_process_reply_to_message_ex(process->ctx.source, buf, cbor_len);

    free(plaintext);
    JADE_LOGI("Success");

cleanup:
    return;
}

// hsm_lock - Exit HSM mode
void hsm_lock_process(void* process_ptr)
{
    JADE_LOGI("Starting: hsm_lock");
    jade_process_t* process = process_ptr;

    ASSERT_CURRENT_MESSAGE(process, "hsm_lock");

    if (!HSM_UNLOCKED_BY_MESSAGE_SOURCE(process)) {
        jade_process_reject_message(process, HSM_ERROR_NOT_ACTIVE, "HSM mode not active");
        goto cleanup;
    }

    hsm_deactivate();

    // Reply with success
    uint8_t buf[64];
    jade_process_reply_to_message_result(process->ctx, buf, sizeof(buf), &(bool){ true }, cbor_result_boolean_cb);

    JADE_LOGI("HSM mode deactivated");

cleanup:
    return;
}

#endif // AMALGAMATED_BUILD
