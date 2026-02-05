/**
 * Account Management UI Module
 * Handles account dropdown, password toggle, and OTP management
 */

import { getSavedAccounts, getAccount, deleteAccount, getDecryptedPassword, hasOTPSecret, getDecryptedOTPSecret, saveOTPSecret, getAutoFillOTP, saveAutoFillOTP, getLastUsedAccount } from '../../utils/accountStorage.js';
import { generateTOTP, getRemainingSeconds, isValidTOTPSecret } from '../../utils/totp.js';

// State
let otpInterval = null;
let currentEmail = '';

// Track auto-filled fields for reCaptcha consideration
// When all fields are auto-filled, we require user interaction (click login button)
export const autoFilledFields = {
  email: false,
  password: false,
  otp: false
};

/**
 * Reset auto-filled state for a specific field
 */
export function clearAutoFilledState(field) {
  if (field in autoFilledFields) {
    autoFilledFields[field] = false;
  }
}

/**
 * Check if all fields are auto-filled
 */
export function isAllAutoFilled() {
  return autoFilledFields.email && autoFilledFields.password && autoFilledFields.otp;
}

/**
 * Initialize account management UI
 */
export async function initAccountManagement() {
  console.log('[AccountManagement] Initializing...');
  
  // Bind event listeners
  bindAccountDropdown();
  bindPasswordToggle();
  bindOTPAutoFill();
  bindEmailChange();
  bindOTPSecretConfirm();
  
  // Load saved accounts
  await loadAccountDropdown();
  
  // Auto-fill last used account
  await autoFillLastUsedAccount();
  
  console.log('[AccountManagement] Initialized');
}

/**
 * Bind account dropdown events
 */
function bindAccountDropdown() {
  const btn = document.getElementById('accountDropdownBtn');
  const dropdown = document.getElementById('accountDropdown');
  
  btn.addEventListener('click', (e) => {
    e.stopPropagation();
    const isVisible = dropdown.style.display === 'block';
    dropdown.style.display = isVisible ? 'none' : 'block';
    if (!isVisible) {
      loadAccountDropdown();
    }
  });
  
  // Close dropdown when clicking outside
  document.addEventListener('click', () => {
    dropdown.style.display = 'none';
  });
}

/**
 * Load and display saved accounts in dropdown
 */
async function loadAccountDropdown() {
  const dropdown = document.getElementById('accountDropdown');
  const accounts = await getSavedAccounts();
  
  if (accounts.length === 0) {
    dropdown.innerHTML = '<div class="dropdown-empty">尚無儲存的帳號</div>';
    return;
  }
  
  dropdown.innerHTML = accounts.map(account => `
    <div class="dropdown-item" data-email="${account.email}">
      <span>${account.email}</span>
      <button class="delete-btn" data-email="${account.email}">刪除</button>
    </div>
  `).join('');
  
  // Bind click events
  dropdown.querySelectorAll('.dropdown-item').forEach(item => {
    item.addEventListener('click', async (e) => {
      if (e.target.classList.contains('delete-btn')) {
        e.stopPropagation();
        const email = e.target.dataset.email;
        if (confirm(`確定要刪除帳號 ${email} 嗎？`)) {
          deleteAccount(email);
          loadAccountDropdown();
        }
      } else {
        const email = item.dataset.email;
        await selectAccount(email);
        dropdown.style.display = 'none';
      }
    });
  });
}

/**
 * Select and fill account
 * @param {string} email - The email to select
 * @param {boolean} isStartup - Whether this is called during startup (affects focus behavior)
 */
async function selectAccount(email, isStartup = false) {
  // Reset auto-filled states
  autoFilledFields.email = false;
  autoFilledFields.password = false;
  autoFilledFields.otp = false;
  
  // Fill email
  document.getElementById('email').value = email;
  currentEmail = email;
  autoFilledFields.email = true;
  
  // Fill password and set remember checkbox
  const password = await getDecryptedPassword(email);
  const rememberCheckbox = document.getElementById('rememberPassword');
  const passwordInput = document.getElementById('password');
  
  if (password) {
    passwordInput.value = password;
    rememberCheckbox.checked = true;
    autoFilledFields.password = true;
  } else {
    passwordInput.value = '';
    rememberCheckbox.checked = false;
  }
  
  // Check and set auto-fill OTP checkbox
  const autoFill = await getAutoFillOTP(email);
  const checkbox = document.getElementById('autoFillOTP');
  checkbox.checked = autoFill;
  
  // If auto-fill is enabled, start OTP
  if (autoFill) {
    await handleAutoFillOTPChange();
  }
  
  // Handle focus based on what was auto-filled (only on startup)
  if (isStartup) {
    setTimeout(() => {
      if (!autoFilledFields.password) {
        // Only email filled, focus password
        passwordInput.focus();
      } else if (!autoFilledFields.otp) {
        // Email and password filled, focus OTP
        document.getElementById('otp').focus();
      }
      // If all filled, don't focus anything (force user to click login for reCaptcha)
    }, 100);
  }
}

/**
 * Auto-fill last used account on startup
 */
async function autoFillLastUsedAccount() {
  try {
    const lastEmail = await getLastUsedAccount();
    
    if (lastEmail) {
      await selectAccount(lastEmail, true); // isStartup = true for focus handling
    }
  } catch (error) {
    console.error('[AccountManagement] Failed to auto-fill last account:', error);
  }
}

/**
 * Bind password toggle (show/hide)
 */
function bindPasswordToggle() {
  const btn = document.getElementById('passwordToggleBtn');
  const input = document.getElementById('password');
  const eyeIcon = btn.querySelector('.eye-icon');
  const eyeSlashIcon = btn.querySelector('.eye-slash-icon');
  
  // Mouse/touch events for "hold to show"
  const showPassword = () => {
    input.type = 'text';
    eyeIcon.style.display = 'block';         // Show open eye
    eyeSlashIcon.style.display = 'none';     // Hide slashed eye
  };
  
  const hidePassword = () => {
    input.type = 'password';
    eyeIcon.style.display = 'none';          // Hide open eye
    eyeSlashIcon.style.display = 'block';    // Show slashed eye
  };
  
  btn.addEventListener('mousedown', showPassword);
  btn.addEventListener('mouseup', hidePassword);
  btn.addEventListener('mouseleave', hidePassword);
  btn.addEventListener('touchstart', showPassword);
  btn.addEventListener('touchend', hidePassword);
  btn.addEventListener('touchcancel', hidePassword);  // Also handle touch cancel
}

/**
 * Bind auto-fill OTP checkbox
 */
function bindOTPAutoFill() {
  const checkbox = document.getElementById('autoFillOTP');
  checkbox.addEventListener('change', handleAutoFillOTPChange);
}

/**
 * Handle auto-fill OTP checkbox change
 */
export async function handleAutoFillOTPChange() {
  const checkbox = document.getElementById('autoFillOTP');
  const otpSecretGroup = document.getElementById('otpSecretGroup');
  const otpInput = document.getElementById('otp');
  const email = document.getElementById('email').value.trim();
  
  if (!checkbox.checked) {
    // Disable auto-fill
    stopOTPTimer();
    otpSecretGroup.style.display = 'none';
    otpSecretGroup.classList.remove('expanded');
    otpInput.value = '';
    otpInput.readOnly = false;
    return;
  }
  
  if (!email) {
    alert('請先輸入帳號');
    checkbox.checked = false;
    return;
  }
  
  // Check if account has OTP secret
  const hasSecret = await hasOTPSecret(email);
  
  if (hasSecret) {
    // Has OTP secret, start auto-fill
    otpInput.readOnly = true;
    await startOTPAutoFill(email);
  } else {
    // No OTP secret, show input field
    otpSecretGroup.style.display = 'block';
    setTimeout(() => otpSecretGroup.classList.add('expanded'), 10);
    otpInput.readOnly = false;
  }
}

/**
 * Start OTP auto-fill
 */
async function startOTPAutoFill(email) {
  const otpSecret = await getDecryptedOTPSecret(email);
  if (!otpSecret) {
    console.error('[AccountManagement] No OTP secret found');
    alert('無法讀取 OTP 金鑰');
    return;
  }
  
  const otpInput = document.getElementById('otp');
  const timer = document.getElementById('otpTimer');
  
  // Set OTP input to readonly
  otpInput.readOnly = true;
  
  // Mark OTP as auto-filled
  autoFilledFields.otp = true;
  
  // Update OTP function
  const updateOTP = async () => {
    try {
      const code = await generateTOTP(otpSecret);
      otpInput.value = code;
      
      const remaining = getRemainingSeconds();
      updateTimerDisplay(remaining);
    } catch (error) {
      console.error('[AccountManagement] Failed to generate OTP:', error);
      stopOTPTimer();
      alert('OTP 生成失敗，請檢查金鑰是否正確');
    }
  };
  
  // Clear existing interval (don't hide timer)
  if (otpInterval) {
    clearInterval(otpInterval);
    otpInterval = null;
  }
  
  // Show timer by removing hidden class
  timer.classList.remove('hidden');
  
  // Update immediately
  await updateOTP();
  
  // Update every second
  otpInterval = setInterval(updateOTP, 1000);
}

/**
 * Stop OTP timer
 */
function stopOTPTimer() {
  if (otpInterval) {
    clearInterval(otpInterval);
    otpInterval = null;
  }
  
  const timer = document.getElementById('otpTimer');
  timer.classList.add('hidden');
}

/**
 * Update timer display
 */
function updateTimerDisplay(seconds) {
  const timerText = document.querySelector('.timer-text');
  const timerProgress = document.querySelector('.timer-progress');
  
  if (timerText) {
    timerText.textContent = seconds;
  }
  
  if (timerProgress) {
    const circumference = 2 * Math.PI * 16; // radius = 16
    const progress = (seconds / 30) * circumference;
    timerProgress.style.strokeDasharray = circumference;
    timerProgress.style.strokeDashoffset = circumference - progress;
  }
}

/**
 * Bind email input change
 */
function bindEmailChange() {
  const emailInput = document.getElementById('email');
  emailInput.addEventListener('change', async () => {
    const email = emailInput.value.trim();
    const previousEmail = currentEmail;
    
    // Only reset if email actually changed
    if (email !== previousEmail) {
      currentEmail = email;
      
      // Reset password and OTP
      document.getElementById('password').value = '';
      document.getElementById('otp').value = '';
      
      // Uncheck autoFillOTP - this will trigger handleAutoFillOTPChange and stop timer
      const checkbox = document.getElementById('autoFillOTP');
      checkbox.checked = false;
      await handleAutoFillOTPChange();
      
      // Uncheck rememberPassword initially
      const rememberCheckbox = document.getElementById('rememberPassword');
      rememberCheckbox.checked = false;
      
      // Try to load account data
      const account = await getAccount(email);
      if (account) {
        const password = await getDecryptedPassword(email);
        if (password) {
          document.getElementById('password').value = password;
          rememberCheckbox.checked = true;
        }
      }
      
      // Check if has OTP secret
      const hasSecret = await hasOTPSecret(email);
      if (hasSecret) {
        checkbox.checked = true;
        await handleAutoFillOTPChange();
      }
    }
  });
}

/**
 * Bind OTP secret confirm button
 */
function bindOTPSecretConfirm() {
  const btn = document.getElementById('confirmOTPSecretBtn');
  btn.addEventListener('click', handleOTPSecretConfirm);
}

/**
 * Handle OTP secret confirmation
 */
async function handleOTPSecretConfirm() {
  const email = document.getElementById('email').value.trim();
  const otpSecretInput = document.getElementById('otpSecret');
  const otpSecret = otpSecretInput.value.trim();
  
  if (!email) {
    alert('請先輸入帳號');
    return;
  }
  
  if (!otpSecret) {
    alert('請輸入 OTP 金鑰');
    return;
  }
  
  if (!isValidTOTPSecret(otpSecret)) {
    alert('OTP 金鑰格式錯誤，請輸入正確的 Base32 格式金鑰');
    return;
  }
  
  // Save OTP secret
  const success = await saveOTPSecret(email, otpSecret);
  
  if (!success) {
    alert('保存 OTP 金鑰失敗');
    return;
  }
  
  // Collapse OTP secret input area
  const otpSecretGroup = document.getElementById('otpSecretGroup');
  otpSecretGroup.classList.remove('expanded');
  setTimeout(() => {
    otpSecretGroup.style.display = 'none';
    otpSecretInput.value = '';
  }, 300);
  
  // Start auto-fill OTP
  await startOTPAutoFill(email);
}

/**
 * Get OTP secret input value
 * @returns {string|null}
 */
export function getOTPSecretInput() {
  const otpSecretGroup = document.getElementById('otpSecretGroup');
  if (otpSecretGroup.classList.contains('expanded')) {
    const input = document.getElementById('otpSecret');
    const secret = input.value.trim();
    
    if (!secret) return null;
    
    if (!isValidTOTPSecret(secret)) {
      alert('OTP 金鑰格式錯誤，請輸入正確的 Base32 格式金鑰');
      return null;
    }
    
    return secret;
  }
  
  return null;
}

/**
 * Cleanup on page unload
 */
export function cleanupAccountManagement() {
  stopOTPTimer();
}
