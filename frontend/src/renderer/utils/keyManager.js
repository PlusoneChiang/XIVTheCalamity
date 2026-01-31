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

/**
 * Get current encryption key (for display)
 * @returns {Promise<string|null>}
 */
export async function getCurrentKey() {
  try {
    const result = await window.electronAPI.storage.load(CONFIG_FILE);
    if (result.success && result.data && result.data.encryptionKey) {
      return result.data.encryptionKey;
    }
    return null;
  } catch (error) {
    console.error('[KeyManager] Failed to get current key:', error);
    return null;
  }
}

/**
 * Set encryption key (for import/restore)
 * WARNING: This will invalidate all existing encrypted data
 * @param {string} key - New encryption key
 * @returns {Promise<boolean>}
 */
export async function setEncryptionKey(key) {
  try {
    if (!key || key.length < 16) {
      console.error('[KeyManager] Invalid key length');
      return false;
    }
    
    const config = {
      encryptionKey: key,
      createdAt: Date.now(),
      version: '1.0',
      imported: true
    };
    
    await window.electronAPI.storage.save(CONFIG_FILE, config);
    console.log('[KeyManager] Encryption key updated');
    return true;
  } catch (error) {
    console.error('[KeyManager] Failed to set encryption key:', error);
    return false;
  }
}

/**
 * Reset encryption key and clear all encrypted data
 * @returns {Promise<boolean>}
 */
export async function resetEncryptionKey() {
  try {
    // Generate new key
    const newKey = generateEncryptionKey();
    
    const config = {
      encryptionKey: newKey,
      createdAt: Date.now(),
      version: '1.0'
    };
    
    // Save new config
    await window.electronAPI.storage.save(CONFIG_FILE, config);
    
    // Delete all encrypted data
    await window.electronAPI.storage.delete('passwords.json');
    await window.electronAPI.storage.delete('otp_secrets.json');
    
    console.log('[KeyManager] Encryption key reset, all data cleared');
    return true;
  } catch (error) {
    console.error('[KeyManager] Failed to reset encryption key:', error);
    return false;
  }
}

/**
 * Export config (for backup)
 * @returns {Promise<object|null>}
 */
export async function exportConfig() {
  try {
    const result = await window.electronAPI.storage.load(CONFIG_FILE);
    if (result.success && result.data) {
      return result.data;
    }
    return null;
  } catch (error) {
    console.error('[KeyManager] Failed to export config:', error);
    return null;
  }
}

/**
 * Save last used account
 * @param {string} email
 * @returns {Promise<boolean>}
 */
export async function saveLastUsedAccount(email) {
  try {
    const result = await window.electronAPI.storage.load(CONFIG_FILE);
    const config = (result.success && result.data) ? result.data : {};
    
    config.lastUsedAccount = email;
    config.lastUsedAt = Date.now();
    
    await window.electronAPI.storage.save(CONFIG_FILE, config);
    console.log('[KeyManager] Last used account saved:', email);
    return true;
  } catch (error) {
    console.error('[KeyManager] Failed to save last used account:', error);
    return false;
  }
}

/**
 * Get last used account
 * @returns {Promise<string|null>}
 */
export async function getLastUsedAccount() {
  try {
    const result = await window.electronAPI.storage.load(CONFIG_FILE);
    if (result.success && result.data && result.data.lastUsedAccount) {
      return result.data.lastUsedAccount;
    }
    return null;
  } catch (error) {
    console.error('[KeyManager] Failed to get last used account:', error);
    return null;
  }
}
