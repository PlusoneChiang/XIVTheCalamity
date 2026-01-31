/**
 * Encoding utilities for secure data transmission
 */

/**
 * Convert string to hex encoding (UTF-8 -> lowercase hex)
 * Used to encode email and password before sending to backend
 * @param {string} str - Input string
 * @returns {string} Hex encoded string
 */
export function toHex(str) {
  const encoder = new TextEncoder();
  const bytes = encoder.encode(str);
  
  return Array.from(bytes)
    .map(byte => byte.toString(16).padStart(2, '0'))
    .join('');
}

/**
 * Decode hex string back to original string
 * @param {string} hexStr - Hex encoded string
 * @returns {string} Decoded string
 */
export function fromHex(hexStr) {
  if (!hexStr || hexStr.length % 2 !== 0) {
    throw new Error('Invalid hex string');
  }
  
  const bytes = new Uint8Array(
    hexStr.match(/.{1,2}/g).map(byte => parseInt(byte, 16))
  );
  const decoder = new TextDecoder();
  return decoder.decode(bytes);
}

/**
 * Test function to verify encoding/decoding works correctly
 */
export function testEncoding() {
  const testCases = [
    'test@email.com',
    'password123',
    '測試中文',
    'Special!@#$%^&*()',
  ];
  
  console.group('Encoding Test');
  testCases.forEach(test => {
    const encoded = toHex(test);
    const decoded = fromHex(encoded);
    const passed = test === decoded;
    console.log(`Input: ${test}`);
    console.log(`Encoded: ${encoded}`);
    console.log(`Decoded: ${decoded}`);
    console.log(`Passed: ${passed ? '✓' : '✗'}`);
    console.log('---');
  });
  console.groupEnd();
}
