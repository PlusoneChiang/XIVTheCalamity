/**
 * Encryption Key Manager
 * Manages the master encryption key for credentials
 * Stores key in a config file that users can backup/restore
 */

const CONFIG_FILE = 'config.json';

/**
 * Generate a random encryption key
 * @returns {string} - 32-character hexadecimal key
 */
function generateEncryptionKey() {
  const array = new Uint8Array(32);
  crypto.getRandomValues(array);
  return Array.from(array, byte => byte.toString(16).padStart(2, '0')).join('');
}

/**
 * Get or create encryption key
 * If key doesn't exist, generates a new one and saves it
 * @returns {Promise<string>} - Encryption key
 */
export async function getEncryptionKey() {
  try {
    // Try to load existing config
    const result = await window.electronAPI.storage.load(CONFIG_FILE);
    
    if (result.success && result.data && result.data.encryptionKey) {
      console.log('[KeyManager] Loaded existing encryption key');
      return result.data.encryptionKey;
    }
    
    // Generate new key
    console.log('[KeyManager] Generating new encryption key');
    const newKey = generateEncryptionKey();
    
    // Save config
    const config = {
      encryptionKey: newKey,
      createdAt: Date.now(),
      version: '1.0'
    };
    
    await window.electronAPI.storage.save(CONFIG_FILE, config);
    console.log('[KeyManager] Encryption key generated and saved');
    
    return newKey;
  } catch (error) {
    console.error('[KeyManager] Failed to get encryption key:', error);
    // Fallback to hardcoded key (should not happen in production)
    return 'xiv-calamity-fallback-key';
  }
}
