#ifndef HSM_H_
#define HSM_H_

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include <wally_bip32.h>
#include <wally_crypto.h>

// HSM derivation path constants
// Path: m/86'/coin'/0'/6000'
#define HSM_PATH_PURPOSE    0x80000056  // 86' (BIP-86 Taproot)
#define HSM_PATH_COIN_MAIN  0x80000000  // 0' (Bitcoin mainnet)
#define HSM_PATH_COIN_TEST  0x80000001  // 1' (Bitcoin testnet)
#define HSM_PATH_ACCOUNT    0x80000000  // 0'
#define HSM_PATH_HSM_BRANCH 0x80001770  // 6000' (HSM branch)

// Maximum plaintext size for encryption
#define HSM_MAX_PLAINTEXT_SIZE 1024

// AES-GCM constants
#define HSM_AES_KEY_SIZE    32
#define HSM_AES_NONCE_SIZE  12
#define HSM_AES_TAG_SIZE    16

// Signature algorithm types
typedef enum {
    HSM_ALGO_SCHNORR,
    HSM_ALGO_ECDSA
} hsm_algo_t;

// Network type for HSM operations
typedef enum {
    HSM_NETWORK_MAINNET,
    HSM_NETWORK_TESTNET
} hsm_network_t;

// HSM keychain structure - stores derived keys for both networks
typedef struct {
    // Mainnet keys: m/86'/0'/0'/6000'
    uint8_t mainnet_private_key[EC_PRIVATE_KEY_LEN];
    uint8_t mainnet_chain_code[32];
    uint8_t mainnet_public_key[EC_PUBLIC_KEY_LEN];

    // Testnet keys: m/86'/1'/0'/6000'
    uint8_t testnet_private_key[EC_PRIVATE_KEY_LEN];
    uint8_t testnet_chain_code[32];
    uint8_t testnet_public_key[EC_PUBLIC_KEY_LEN];

    // State
    bool is_active;
    uint32_t auto_lock_timeout;         // 0 = disabled, >0 = seconds
    uint32_t last_activity_timestamp;   // For auto-lock tracking
    uint64_t operations_count;          // Total operations performed

    // Message source that unlocked HSM mode
    uint8_t userdata;
} hsm_keychain_t;

// Initialize HSM module
void hsm_init(void);

// Check if HSM mode is active
bool hsm_is_active(void);

// Get HSM keychain (returns NULL if not active)
const hsm_keychain_t* hsm_get_keychain(void);

// Activate HSM mode from seed (wipes seed after derivation)
// Returns true on success
bool hsm_activate(const uint8_t* seed, size_t seed_len, uint8_t userdata);

// Deactivate HSM mode and clear all keys
void hsm_deactivate(void);

// Check if message source matches HSM unlock source
bool hsm_is_unlocked_by_source(uint8_t source);

// Update activity timestamp (for auto-lock)
void hsm_update_activity(void);

// Check and handle auto-lock timeout
// Returns true if HSM was locked due to timeout
bool hsm_check_timeout(void);

// Set auto-lock timeout (can only be set via device UI, not RPC)
void hsm_set_timeout(uint32_t timeout_seconds);

// Get auto-lock timeout
uint32_t hsm_get_timeout(void);

// Get remaining time before auto-lock (0 if disabled or not active)
uint32_t hsm_get_remaining_time(void);

// Increment operations counter
void hsm_increment_ops(void);

// Get operations count
uint64_t hsm_get_ops_count(void);

// Derive child key at index for specified network
// Returns true on success
bool hsm_derive_key(hsm_network_t network, uint32_t index,
                    uint8_t* privkey_out, size_t privkey_len,
                    uint8_t* pubkey_out, size_t pubkey_len);

// Get public key at index for specified network
bool hsm_get_pubkey(hsm_network_t network, uint32_t index,
                    uint8_t* pubkey_out, size_t pubkey_len);

// Get root public key for specified network
bool hsm_get_root_pubkey(hsm_network_t network,
                         uint8_t* pubkey_out, size_t pubkey_len);

// Get xpub for HSM root at specified network
bool hsm_get_xpub(hsm_network_t network, char** xpub_out);

// Sign a 32-byte hash with key at index
// For Schnorr: signature is 64 bytes
// For ECDSA: signature is DER encoded (up to 72 bytes)
bool hsm_sign(hsm_network_t network, uint32_t index, hsm_algo_t algo,
              const uint8_t* hash, size_t hash_len,
              uint8_t* signature_out, size_t sig_out_len, size_t* sig_written);

// Compute ECDH shared secret
bool hsm_ecdh(hsm_network_t network, uint32_t index,
              const uint8_t* their_pubkey, size_t their_pubkey_len,
              uint8_t* shared_secret_out, size_t secret_len);

// ECIES encryption
// Returns ciphertext, nonce, tag, and ephemeral pubkey
bool hsm_encrypt(hsm_network_t network, uint32_t index,
                 const uint8_t* plaintext, size_t plaintext_len,
                 const uint8_t* their_pubkey, size_t their_pubkey_len,  // optional, NULL for self-encryption
                 const uint8_t* aad, size_t aad_len,                    // optional additional authenticated data
                 uint8_t* ciphertext_out, size_t* ciphertext_len,
                 uint8_t* nonce_out, size_t nonce_len,
                 uint8_t* tag_out, size_t tag_len,
                 uint8_t* ephemeral_pubkey_out, size_t ephemeral_pubkey_len);

// ECIES decryption
bool hsm_decrypt(hsm_network_t network, uint32_t index,
                 const uint8_t* ciphertext, size_t ciphertext_len,
                 const uint8_t* nonce, size_t nonce_len,
                 const uint8_t* tag, size_t tag_len,
                 const uint8_t* ephemeral_pubkey, size_t ephemeral_pubkey_len,
                 const uint8_t* aad, size_t aad_len,
                 uint8_t* plaintext_out, size_t* plaintext_len);

// Helper to convert network string to enum
bool hsm_parse_network(const char* network_str, size_t str_len, hsm_network_t* network_out);

// Helper to convert algo string to enum
bool hsm_parse_algo(const char* algo_str, size_t str_len, hsm_algo_t* algo_out);

// Get derivation path string for network
const char* hsm_get_path_string(hsm_network_t network);

#endif /* HSM_H_ */
