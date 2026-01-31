/**
 * Account Storage Manager
 * Manages saved accounts with encrypted passwords and OTP secrets
 * Passwords and OTP secrets are stored separately in files
 */

import { encryptText, decryptText } from './crypto.js';
import { getEncryptionKey } from './keyManager.js';

const PASSWORDS_FILE = 'passwords.json';
const OTP_SECRETS_FILE = 'otp_secrets.json';

/**
 * Password data structure
 * @typedef {Object} PasswordData
 * @property {string} email - Account email (plain text)
 * @property {string} password - Encrypted password
 * @property {number} savedAt - Timestamp when saved
 */

/**
 * OTP Secret data structure
 * @typedef {Object} OTPSecretData
 * @property {string} email - Account email (plain text)
 * @property {string} otpSecret - Encrypted OTP secret
 * @property {number} savedAt - Timestamp when saved
 */

/**
 * Load data from file
 */
async function loadFromFile(filename) {
  try {
    const result = await window.electronAPI.storage.load(filename);
    if (result.success && result.data) {
      return result.data;
    }
    return {};
  } catch (error) {
    console.error('[AccountStorage] Failed to load from file:', filename, error);
    return {};
  }
}

/**
 * Save data to file
 */
async function saveToFile(filename, data) {
  try {
    const result = await window.electronAPI.storage.save(filename, data);
    return result.success;
  } catch (error) {
    console.error('[AccountStorage] Failed to save to file:', filename, error);
    return false;
  }
}

/**
 * Get all saved account emails (from passwords)
 * @returns {Promise<string[]>}
 */
export async function getSavedAccounts() {
  try {
    const passwords = await loadFromFile(PASSWORDS_FILE);
    return Object.keys(passwords).map(email => ({
      email,
      savedAt: passwords[email].savedAt,
      lastUsedAt: passwords[email].lastUsedAt || passwords[email].savedAt
    }));
  } catch (error) {
    console.error('[AccountStorage] Failed to load accounts:', error);
    return [];
  }
}

/**
 * Get last used account based on lastUsedAt timestamp
 * @returns {Promise<string|null>}
 */
export async function getLastUsedAccount() {
  try {
    const passwords = await loadFromFile(PASSWORDS_FILE);
    const accounts = Object.values(passwords);
    
    if (accounts.length === 0) return null;
    
    // Sort by lastUsedAt (most recent first)
    accounts.sort((a, b) => {
      const aTime = a.lastUsedAt || a.savedAt || 0;
      const bTime = b.lastUsedAt || b.savedAt || 0;
      return bTime - aTime;
    });
    
    return accounts[0].email;
  } catch (error) {
    console.error('[AccountStorage] Failed to get last used account:', error);
    return null;
  }
}

/**
 * Get account password data by email
 * @param {string} email
 * @returns {Promise<PasswordData|null>}
 */
export async function getAccount(email) {
  try {
    const passwords = await loadFromFile(PASSWORDS_FILE);
    return passwords[email] || null;
  } catch (error) {
    console.error('[AccountStorage] Failed to get account:', error);
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
    // Load existing passwords
    const passwords = await loadFromFile(PASSWORDS_FILE);
    
    // Get encryption key
    const masterPassword = await getEncryptionKey();
    
    // Encrypt password
    const encryptedPassword = await encryptText(password, masterPassword);
    
    // Save password with lastUsedAt timestamp
    passwords[email] = {
      email,
      password: encryptedPassword,
      lastUsedAt: Date.now(),  // Update on every save
      savedAt: passwords[email]?.savedAt || Date.now()
    };
    
    const success = await saveToFile(PASSWORDS_FILE, passwords);
    if (success) {
      console.log('[AccountStorage] Password saved:', email);
    }
    return success;
  } catch (error) {
    console.error('[AccountStorage] Failed to save password:', error);
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
    // Load existing OTP secrets
    const otpSecrets = await loadFromFile(OTP_SECRETS_FILE);
    
    // Get encryption key
    const masterPassword = await getEncryptionKey();
    
    // Encrypt OTP secret
    const encryptedOTP = await encryptText(otpSecret, masterPassword);
    
    // Preserve existing autoFillOTP setting if it exists
    const existingAutoFill = otpSecrets[email]?.autoFillOTP ?? true;
    
    // Save OTP secret
    otpSecrets[email] = {
      email,
      otpSecret: encryptedOTP,
      autoFillOTP: existingAutoFill,
      savedAt: Date.now()
    };
    
    const success = await saveToFile(OTP_SECRETS_FILE, otpSecrets);
    if (success) {
      console.log('[AccountStorage] OTP secret saved:', email);
    }
    return success;
  } catch (error) {
    console.error('[AccountStorage] Failed to save OTP secret:', error);
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
    let deleted = false;
    
    // Delete password
    const passwords = await loadFromFile(PASSWORDS_FILE);
    if (passwords[email]) {
      delete passwords[email];
      await saveToFile(PASSWORDS_FILE, passwords);
      deleted = true;
    }
    
    // Delete OTP secret
    const otpSecrets = await loadFromFile(OTP_SECRETS_FILE);
    if (otpSecrets[email]) {
      delete otpSecrets[email];
      await saveToFile(OTP_SECRETS_FILE, otpSecrets);
      deleted = true;
    }
    
    if (deleted) {
      console.log('[AccountStorage] Account deleted:', email);
    }
    return deleted;
  } catch (error) {
    console.error('[AccountStorage] Failed to delete account:', error);
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
    const account = await getAccount(email);
    if (!account || !account.password) return null;
    
    const masterPassword = await getEncryptionKey();
    return await decryptText(account.password, masterPassword);
  } catch (error) {
    console.error('[AccountStorage] Failed to decrypt password:', error);
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
    const otpSecrets = await loadFromFile(OTP_SECRETS_FILE);
    const secretData = otpSecrets[email];
    
    if (!secretData || !secretData.otpSecret) return null;
    
    const masterPassword = await getEncryptionKey();
    return await decryptText(secretData.otpSecret, masterPassword);
  } catch (error) {
    console.error('[AccountStorage] Failed to decrypt OTP secret:', error);
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
    const otpSecrets = await loadFromFile(OTP_SECRETS_FILE);
    return !!(otpSecrets[email] && otpSecrets[email].otpSecret);
  } catch (error) {
    console.error('[AccountStorage] Failed to check OTP secret:', error);
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
    const otpSecrets = await loadFromFile(OTP_SECRETS_FILE);
    const secretData = otpSecrets[email];
    return secretData?.autoFillOTP ?? false;
  } catch (error) {
    console.error('[AccountStorage] Failed to get autoFillOTP:', error);
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
    const otpSecrets = await loadFromFile(OTP_SECRETS_FILE);
    
    if (otpSecrets[email]) {
      otpSecrets[email].autoFillOTP = autoFillOTP;
      const success = await saveToFile(OTP_SECRETS_FILE, otpSecrets);
      if (success) {
        console.log('[AccountStorage] AutoFillOTP saved:', email, autoFillOTP);
      }
      return success;
    }
    
    return false;
  } catch (error) {
    console.error('[AccountStorage] Failed to save autoFillOTP:', error);
    return false;
  }
}

/**
 * Legacy: Save account (for compatibility)
 * Now saves password and OTP secret separately
 */
export async function saveAccount(email, password, otpSecret = null, autoFillOTP = false) {
  let success = true;
  
  // Save password
  if (password) {
    success = await savePassword(email, password) && success;
  }
  
  // Save OTP secret if provided
  if (otpSecret) {
    success = await saveOTPSecret(email, otpSecret) && success;
  }
  
  return success;
}
