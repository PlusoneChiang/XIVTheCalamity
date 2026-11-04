/**
 * TOTP (Time-based One-Time Password) Generator
 * RFC 6238 implementation using otplib
 */

import { createGuardrails, generate } from 'otplib';

// 相容既有 16 字元 Base32 金鑰（10 bytes），不改變外部服務核發的金鑰。
const guardrails = createGuardrails({ MIN_SECRET_BYTES: 10 });

/**
 * Generate TOTP code
 * @param {string} secret - Base32 encoded secret key
 * @param {number} timeStep - Time step in seconds (default: 30)
 * @param {number} digits - Number of digits (default: 6)
 * @returns {Promise<string>} TOTP code
 */
export async function generateTOTP(secret, timeStep = 30, digits = 6) {
  try {
    return await generate({
      secret: secret.toUpperCase().replace(/\s+/g, '').replace(/=+$/, ''),
      period: timeStep,
      digits,
      guardrails
    });
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