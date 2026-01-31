/**
 * TOTP (Time-based One-Time Password) Generator
 * RFC 6238 implementation compatible with Google Authenticator
 */

/**
 * Base32 decode
 * @param {string} base32 - Base32 encoded string
 * @returns {Uint8Array}
 */
function base32Decode(base32) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const cleanBase32 = base32.toUpperCase().replace(/\s+/g, '').replace(/=+$/, '');
  
  let bits = 0;
  let value = 0;
  const output = [];
  
  for (let i = 0; i < cleanBase32.length; i++) {
    const idx = alphabet.indexOf(cleanBase32[i]);
    if (idx === -1) throw new Error('Invalid base32 character');
    
    value = (value << 5) | idx;
    bits += 5;
    
    if (bits >= 8) {
      output.push((value >>> (bits - 8)) & 255);
      bits -= 8;
    }
  }
  
  return new Uint8Array(output);
}

/**
 * Generate HMAC-SHA1
 * @param {Uint8Array} key - Secret key
 * @param {Uint8Array} data - Data to hash
 * @returns {Promise<ArrayBuffer>}
 */
async function hmacSHA1(key, data) {
  const cryptoKey = await crypto.subtle.importKey(
    'raw',
    key,
    { name: 'HMAC', hash: 'SHA-1' },
    false,
    ['sign']
  );
  
  return crypto.subtle.sign('HMAC', cryptoKey, data);
}

/**
 * Generate TOTP code
 * @param {string} secret - Base32 encoded secret key
 * @param {number} timeStep - Time step in seconds (default: 30)
 * @param {number} digits - Number of digits (default: 6)
 * @returns {Promise<string>} TOTP code
 */
export async function generateTOTP(secret, timeStep = 30, digits = 6) {
  try {
    // Decode base32 secret
    const key = base32Decode(secret);
    
    // Get current time counter
    const now = Math.floor(Date.now() / 1000);
    const counter = Math.floor(now / timeStep);
    
    // Convert counter to 8-byte array (big-endian)
    const counterBuffer = new ArrayBuffer(8);
    const counterView = new DataView(counterBuffer);
    counterView.setUint32(4, counter, false); // Big-endian
    
    // Generate HMAC
    const hmac = await hmacSHA1(key, new Uint8Array(counterBuffer));
    const hmacArray = new Uint8Array(hmac);
    
    // Dynamic truncation
    const offset = hmacArray[hmacArray.length - 1] & 0x0f;
    const binary = 
      ((hmacArray[offset] & 0x7f) << 24) |
      ((hmacArray[offset + 1] & 0xff) << 16) |
      ((hmacArray[offset + 2] & 0xff) << 8) |
      (hmacArray[offset + 3] & 0xff);
    
    // Generate code
    const code = binary % Math.pow(10, digits);
    return code.toString().padStart(digits, '0');
  } catch (error) {
    console.error('[TOTP] Generation failed:', error);
    throw error;
  }
}

/**
 * Get remaining seconds until next TOTP code
 * @param {number} timeStep - Time step in seconds (default: 30)
 * @returns {number} Remaining seconds
 */
export function getRemainingSeconds(timeStep = 30) {
  const now = Math.floor(Date.now() / 1000);
  return timeStep - (now % timeStep);
}

/**
 * Validate TOTP secret format
 * @param {string} secret - Base32 encoded secret
 * @returns {boolean}
 */
export function isValidTOTPSecret(secret) {
  const cleanSecret = secret.toUpperCase().replace(/\s+/g, '').replace(/=+$/, '');
  const base32Pattern = /^[A-Z2-7]+$/;
  return base32Pattern.test(cleanSecret) && cleanSecret.length >= 16;
}
