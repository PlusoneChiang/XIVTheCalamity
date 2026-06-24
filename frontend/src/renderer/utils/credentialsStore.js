/**
 * Credentials Storage Manager
 * Manages saved accounts with encrypted passwords and OTP secrets in a unified storage
 */

import { encryptText, decryptText } from './crypto.js';
import { getEncryptionKey } from './keyManager.js';

const CREDENTIALS_FILE = 'credentials.json';

// Legacy Key Derivation (using the old, buggy salt mapping for backward compatibility during migration)
async function legacyDeriveKey(password, salt) {
  const encoder = new TextEncoder();
  const passwordBuffer = encoder.encode(password);
  const saltBuffer = encoder.encode(salt);
  
  const keyMaterial = await crypto.subtle.importKey(
    'raw',
    passwordBuffer,
    { name: 'PBKDF2' },
    false,
    ['deriveBits', 'deriveKey']
  );
  
  return crypto.subtle.deriveKey(
    {
      name: 'PBKDF2',
      salt: saltBuffer,
      iterations: 100000,
      hash: 'SHA-256'
    },
    keyMaterial,
    { name: 'AES-GCM', length: 256 },
    false,
    ['encrypt', 'decrypt']
  );
}

// Legacy Decrypt (using the old, buggy salt mapping for backward compatibility during migration)
async function legacyDecryptText(encryptedBase64, password) {
  const combined = Uint8Array.from(atob(encryptedBase64), c => c.charCodeAt(0));
  const salt = combined.slice(0, 16);
  const iv = combined.slice(16, 28);
  const encrypted = combined.slice(28);
  
  const saltStr = Array.from(salt).map(b => String.fromCharCode(b)).join('');
  const key = await legacyDeriveKey(password, saltStr);
  
  const decrypted = await crypto.subtle.decrypt(
    { name: 'AES-GCM', iv: iv },
    key,
    encrypted
  );
  
  const decoder = new TextDecoder();
  return decoder.decode(decrypted);
}

let storeCache = null;
let migrationPromise = null;

/**
 * Migration helper to convert legacy passwords.json and otp_secrets.json to credentials.json
 */
async function migrateCredentials() {
  try {
    const passwordsFile = 'passwords.json';
    const otpSecretsFile = 'otp_secrets.json';
    
    // Check if legacy passwords.json exists
    const passwordsResult = await window.xivtc.storage.load(passwordsFile);
    if (!passwordsResult.success || !passwordsResult.data) {
      // No legacy passwords.json found, check if legacy otp_secrets.json exists alone
      const otpCheck = await window.xivtc.storage.load(otpSecretsFile);
      if (!otpCheck.success || !otpCheck.data) {
        return; // No legacy files exist, skip migration
      }
    }
    
    console.log('[CredentialsStore] Legacy credentials files found. Starting migration...');
    
    const legacyPasswords = passwordsResult.data || {};
    
    let legacyOtpSecrets = {};
    const otpResult = await window.xivtc.storage.load(otpSecretsFile);
    if (otpResult.success && otpResult.data) {
      legacyOtpSecrets = otpResult.data;
    }
    
    // Create backup copies of the legacy files first
    await window.xivtc.storage.save('passwords.json.bak', legacyPasswords);
    if (Object.keys(legacyOtpSecrets).length > 0) {
      await window.xivtc.storage.save('otp_secrets.json.bak', legacyOtpSecrets);
    }
    
    const masterPassword = await getEncryptionKey();
    const migratedStore = {};
    
    // Decrypt and migrate accounts from legacy passwords
    for (const email of Object.keys(legacyPasswords)) {
      const pRecord = legacyPasswords[email];
      const oRecord = legacyOtpSecrets[email];
      
      let decryptedPassword = null;
      let decryptedOtpSecret = null;
      
      if (pRecord.password) {
        try {
          decryptedPassword = await legacyDecryptText(pRecord.password, masterPassword);
        } catch (err) {
          console.error(`[CredentialsStore] Failed to decrypt legacy password for ${email}:`, err);
        }
      }
      
      if (oRecord && oRecord.otpSecret) {
        try {
          decryptedOtpSecret = await legacyDecryptText(oRecord.otpSecret, masterPassword);
        } catch (err) {
          console.error(`[CredentialsStore] Failed to decrypt legacy OTP secret for ${email}:`, err);
        }
      }
      
      let newEncryptedPassword = null;
      let newEncryptedOtpSecret = null;
      
      if (decryptedPassword) {
        newEncryptedPassword = await encryptText(decryptedPassword, masterPassword);
      }
      if (decryptedOtpSecret) {
        newEncryptedOtpSecret = await encryptText(decryptedOtpSecret, masterPassword);
      }
      
      migratedStore[email] = {
        email,
        password: newEncryptedPassword,
        otpSecret: newEncryptedOtpSecret,
        autoFillOTP: oRecord ? (oRecord.autoFillOTP ?? true) : false,
        savedAt: pRecord.savedAt || Date.now(),
        lastUsedAt: pRecord.lastUsedAt || pRecord.savedAt || Date.now()
      };
    }
    
    // Decrypt and migrate orphaned legacy OTP secrets
    for (const email of Object.keys(legacyOtpSecrets)) {
      if (!migratedStore[email]) {
        const oRecord = legacyOtpSecrets[email];
        let decryptedOtpSecret = null;
        if (oRecord.otpSecret) {
          try {
            decryptedOtpSecret = await legacyDecryptText(oRecord.otpSecret, masterPassword);
          } catch (err) {
            console.error(`[CredentialsStore] Failed to decrypt legacy orphaned OTP secret for ${email}:`, err);
          }
        }
        
        let newEncryptedOtpSecret = null;
        if (decryptedOtpSecret) {
          newEncryptedOtpSecret = await encryptText(decryptedOtpSecret, masterPassword);
        }
        
        migratedStore[email] = {
          email,
          password: null,
          otpSecret: newEncryptedOtpSecret,
          autoFillOTP: oRecord.autoFillOTP ?? true,
          savedAt: oRecord.savedAt || Date.now(),
          lastUsedAt: oRecord.savedAt || Date.now()
        };
      }
    }
    
    // Load and merge with any existing credentials.json
    const credentialsFile = CREDENTIALS_FILE;
    const existingResult = await window.xivtc.storage.load(credentialsFile);
    let finalStore = {};
    if (existingResult.success && existingResult.data) {
      finalStore = existingResult.data;
    }
    
    finalStore = { ...migratedStore, ...finalStore };
    
    const saveResult = await window.xivtc.storage.save(credentialsFile, finalStore);
    if (saveResult.success) {
      console.log('[CredentialsStore] Migration successful. Cleaning up legacy files...');
      await window.xivtc.storage.delete(passwordsFile);
      await window.xivtc.storage.delete(otpSecretsFile);
      console.log('[CredentialsStore] Cleanup complete.');
    } else {
      console.error('[CredentialsStore] Migration failed to save new store.');
    }
  } catch (error) {
    console.error('[CredentialsStore] Migration error:', error);
  }
}

// Auto-trigger migration on import
migrationPromise = (async () => {
  try {
    await migrateCredentials();
  } catch (error) {
    console.error('[CredentialsStore] Auto-migration failed:', error);
  } finally {
    migrationPromise = null;
  }
})();

/**
 * Load store data, waiting for migration if in progress
 */
async function loadStore() {
  if (migrationPromise) {
    await migrationPromise;
  }
  
  if (storeCache) {
    return storeCache;
  }
  
  try {
    const result = await window.xivtc.storage.load(CREDENTIALS_FILE);
    if (result.success && result.data) {
      storeCache = result.data;
    } else {
      storeCache = {};
    }
  } catch (error) {
    console.error('[CredentialsStore] Load store failed:', error);
    storeCache = {};
  }
  return storeCache;
}

/**
 * Save store data
 */
async function saveStore(store) {
  storeCache = store;
  try {
    const result = await window.xivtc.storage.save(CREDENTIALS_FILE, store);
    return result.success;
  } catch (error) {
    console.error('[CredentialsStore] Save store failed:', error);
    return false;
  }
}

/**
 * Get all saved account emails (that have passwords)
 * @returns {Promise<string[]>}
 */
export async function getSavedAccounts() {
  try {
    const store = await loadStore();
    return Object.keys(store)
      .filter(email => !!store[email].password)
      .map(email => ({
        email,
        savedAt: store[email].savedAt,
        lastUsedAt: store[email].lastUsedAt || store[email].savedAt
      }));
  } catch (error) {
    console.error('[CredentialsStore] Failed to get saved accounts:', error);
    return [];
  }
}

/**
 * Get last used account based on lastUsedAt timestamp
 * @returns {Promise<string|null>}
 */
export async function getLastUsedAccount() {
  try {
    const store = await loadStore();
    const accounts = Object.values(store).filter(acc => !!acc.password);
    
    if (accounts.length === 0) return null;
    
    // Sort by lastUsedAt (most recent first)
    accounts.sort((a, b) => {
      const aTime = a.lastUsedAt || a.savedAt || 0;
      const bTime = b.lastUsedAt || b.savedAt || 0;
      return bTime - aTime;
    });
    
    return accounts[0].email;
  } catch (error) {
    console.error('[CredentialsStore] Failed to get last used account:', error);
    return null;
  }
}

/**
 * Get account password data by email
 * @param {string} email
 * @returns {Promise<object|null>}
 */
export async function getAccount(email) {
  try {
    const store = await loadStore();
    const acc = store[email];
    if (!acc || !acc.password) return null;
    
    return {
      email: acc.email,
      password: acc.password,
      lastUsedAt: acc.lastUsedAt,
      savedAt: acc.savedAt
    };
  } catch (error) {
    console.error('[CredentialsStore] Failed to get account:', error);
    return null;
  }
}

/**
 * Save or update account password
 * @param {string} email
 * @param {string} password - Plain text password (will be encrypted)
 * @returns {Promise<boolean>}
 */
export async function savePassword(email, password) {
  try {
    const store = await loadStore();
    const masterPassword = await getEncryptionKey();
    const encryptedPassword = await encryptText(password, masterPassword);
    
    if (!store[email]) {
      store[email] = {
        email,
        password: encryptedPassword,
        otpSecret: null,
        autoFillOTP: false,
        savedAt: Date.now(),
        lastUsedAt: Date.now()
      };
    } else {
      store[email].password = encryptedPassword;
      store[email].lastUsedAt = Date.now();
    }
    
    const success = await saveStore(store);
    if (success) {
      console.log('[CredentialsStore] Password saved:', email);
    }
    return success;
  } catch (error) {
    console.error('[CredentialsStore] Failed to save password:', error);
    return false;
  }
}

/**
 * Save or update OTP secret for account
 * @param {string} email
 * @param {string} otpSecret - Plain text OTP secret (will be encrypted)
 * @returns {Promise<boolean>}
 */
export async function saveOTPSecret(email, otpSecret) {
  try {
    const store = await loadStore();
    const masterPassword = await getEncryptionKey();
    const encryptedOTP = await encryptText(otpSecret, masterPassword);
    
    if (!store[email]) {
      store[email] = {
        email,
        password: null,
        otpSecret: encryptedOTP,
        autoFillOTP: true,
        savedAt: Date.now(),
        lastUsedAt: Date.now()
      };
    } else {
      store[email].otpSecret = encryptedOTP;
      if (store[email].autoFillOTP === undefined || store[email].autoFillOTP === null) {
        store[email].autoFillOTP = true;
      }
    }
    
    const success = await saveStore(store);
    if (success) {
      console.log('[CredentialsStore] OTP secret saved:', email);
    }
    return success;
  } catch (error) {
    console.error('[CredentialsStore] Failed to save OTP secret:', error);
    return false;
  }
}

/**
 * Delete OTP secret for a specific account
 * @param {string} email
 * @returns {Promise<boolean>}
 */
export async function deleteOTPSecret(email) {
  try {
    const store = await loadStore();
    if (store[email]) {
      store[email].otpSecret = null;
      
      // If record is empty of both credentials, delete it entirely
      if (!store[email].password && !store[email].otpSecret) {
        delete store[email];
      }
      
      const success = await saveStore(store);
      if (success) {
        console.log('[CredentialsStore] OTP secret deleted for:', email);
      }
      return success;
    }
    return false;
  } catch (error) {
    console.error('[CredentialsStore] Failed to delete OTP secret:', error);
    return false;
  }
}

/**
 * Delete account (both password and OTP secret)
 * @param {string} email
 * @returns {Promise<boolean>}
 */
export async function deleteAccount(email) {
  try {
    const store = await loadStore();
    if (store[email]) {
      delete store[email];
      const success = await saveStore(store);
      if (success) {
        console.log('[CredentialsStore] Account deleted:', email);
      }
      return success;
    }
    return false;
  } catch (error) {
    console.error('[CredentialsStore] Failed to delete account:', error);
    return false;
  }
}

/**
 * Get decrypted password
 * @param {string} email
 * @returns {Promise<string|null>}
 */
export async function getDecryptedPassword(email) {
  try {
    const store = await loadStore();
    const acc = store[email];
    if (!acc || !acc.password) return null;
    
    const masterPassword = await getEncryptionKey();
    return await decryptText(acc.password, masterPassword);
  } catch (error) {
    console.error('[CredentialsStore] Failed to decrypt password:', error);
    return null;
  }
}

/**
 * Get decrypted OTP secret
 * @param {string} email
 * @returns {Promise<string|null>}
 */
export async function getDecryptedOTPSecret(email) {
  try {
    const store = await loadStore();
    const acc = store[email];
    if (!acc || !acc.otpSecret) return null;
    
    const masterPassword = await getEncryptionKey();
    return await decryptText(acc.otpSecret, masterPassword);
  } catch (error) {
    console.error('[CredentialsStore] Failed to decrypt OTP secret:', error);
    return null;
  }
}

/**
 * Check if account has OTP secret
 * @param {string} email
 * @returns {Promise<boolean>}
 */
export async function hasOTPSecret(email) {
  try {
    const store = await loadStore();
    return !!(store[email] && store[email].otpSecret);
  } catch (error) {
    console.error('[CredentialsStore] Failed to check OTP secret:', error);
    return false;
  }
}

/**
 * Get auto-fill OTP setting for account
 * @param {string} email
 * @returns {Promise<boolean>}
 */
export async function getAutoFillOTP(email) {
  try {
    const store = await loadStore();
    return store[email]?.autoFillOTP ?? false;
  } catch (error) {
    console.error('[CredentialsStore] Failed to get autoFillOTP:', error);
    return false;
  }
}

/**
 * Save auto-fill OTP setting for account
 * @param {string} email
 * @param {boolean} autoFillOTP
 * @returns {Promise<boolean>}
 */
export async function saveAutoFillOTP(email, autoFillOTP) {
  try {
    const store = await loadStore();
    if (store[email]) {
      store[email].autoFillOTP = autoFillOTP;
      const success = await saveStore(store);
      if (success) {
        console.log('[CredentialsStore] AutoFillOTP saved:', email, autoFillOTP);
      }
      return success;
    }
    return false;
  } catch (error) {
    console.error('[CredentialsStore] Failed to save autoFillOTP:', error);
    return false;
  }
}
