/**
 * Login page logic
 * Handles authentication flow with Taiwan region FFXIV servers
 */

// Import utilities
import { toHex } from '../../utils/encoding.js';
import { initAccountManagement, handleAutoFillOTPChange, getOTPSecretInput, cleanupAccountManagement, clearAutoFilledState, isAllAutoFilled, refreshAccountState } from './accountManagement.js';
import { savePassword, saveOTPSecret, saveAutoFillOTP, hasOTPSecret } from '../../utils/accountStorage.js';
import i18n from '../../i18n/index.js';
import { startBackgroundUpdate, setAppVersionText, isUpdating, handleConfigChanged } from './updateManager.js';
import { startDalamudUpdate, cancelDalamudUpdate, handleDalamudConfigChanged, isDalamudUpdating } from './dalamudManager.js';
import { initAppUpdater } from './appUpdater.js';
import { handleApiResponse, getErrorMessage } from '../../utils/apiError.js';
import { applyTheme } from '../../utils/theme.js';

// Constants
const SITE_KEY = "6Ld6VmorAAAAANQdQeqkaOeScR42qHC7Hyalq00r";

// State
let currentState = 'idle';
let appVersionText = 'XIV The Calamity';  // Will be updated by loadVersion()

/**
 * 更新按鈕的 i18n 文字，保持內部 <span data-i18n> 結構完整，
 * 使 i18n.updateElements() 在語系切換時能自動更新。
 */
function setButtonI18n(button, i18nKey) {
  if (!button) return;
  let span = button.querySelector('span[data-i18n]');
  if (!span) {
    button.textContent = '';
    span = document.createElement('span');
    button.appendChild(span);
  }
  span.setAttribute('data-i18n', i18nKey);
  span.textContent = i18n.t(i18nKey);
}

// Session & remain countdown timers
let sessionTimerId = null;
let remainTimerId = null;
let remainExpireTime = null;
let sessionObtainedAt = null;

/**
 * Initialize the login page
 */
async function init() {
  console.log('[Login] Initializing login page');
  
  if (!window.electronAPI) {
    console.error('[Login] ERROR: window.electronAPI is not available!');
    console.error('[Login] Preload script may not be loaded correctly.');
    alert(i18n.t('login.system_error'));
    return;
  }
  console.log('[Login] electronAPI is available');
  
  // Load config for language and dev mode
  try {
    const configResp = await window.electronAPI.backend.call('/api/config');
    const rawData = configResp?.data;
    const config = rawData?.success ? rawData.data : rawData;
    if (config?.launcher?.language) {
      i18n.setLocale(config.launcher.language);
    }
    applyTheme(config?.launcher?.theme || 'dark');
    if (config?.launcher?.developmentMode) {
      document.body.classList.add('dev-mode');
    }
  } catch (e) {
    console.error('[Login] Failed to load config:', e);
  }
  
  // Load version
  loadVersion();
  
  // Initialize account management
  initAccountManagement();
  
  // Clear any previous session on startup
  localStorage.removeItem('sessionId');
  localStorage.removeItem('subscriptionType');
  localStorage.removeItem('remain');
  localStorage.removeItem('sessionObtainedAt');
  clearCountdownTimers('all');
  console.log('[Login] Cleared previous session, ready for login');
  
  // Bind event listeners
  document.getElementById('loginButton').addEventListener('click', handleLogin);
  document.getElementById('reloginButton').addEventListener('click', handleRelogin);
  document.getElementById('launchButton').addEventListener('click', handleLaunchGame);
  
  // Bind keyboard navigation and auto-fill state tracking
  bindKeyboardNavigation();
  
  // Settings button (initially disabled until Wine initialization completes)
  const settingsBtn = document.getElementById('settingsBtn');
  settingsBtn.addEventListener('click', handleOpenSettings);
  
  // Disable settings button on macOS/Linux until Wine is ready
  const platform = window.electronAPI?.getPlatform ? window.electronAPI.getPlatform() : 'unknown';
  if (platform === 'darwin' || platform === 'linux') {
    settingsBtn.disabled = true;
    console.log('[Login] Settings button disabled until Wine initialization completes');
  }
  
  // Close button (Linux only)
  const closeBtn = document.getElementById('closeBtn');
  if (platform === 'linux') {
    closeBtn.style.display = 'flex';  // Show close button on Linux
    closeBtn.addEventListener('click', () => {
      console.log('[Login] Close button clicked');
      window.electronAPI.closeWindow();
    });
  }
  
  // 監聽設定變更事件
  if (window.electronAPI.events) {
    window.electronAPI.events.on('config-changed', async (data) => {
      console.log('[Login] Received config-changed event:', data);
      handleConfigChanged(data);
      // 處理 Dalamud 設定變更
      if (data.dalamudEnabledChanged !== undefined) {
        handleDalamudConfigChanged(data);
      }
      // 重新讀取設定檔以更新頁面
      try {
        const configResp = await window.electronAPI.backend.call('/api/config');
        const rawData = configResp?.data;
        const config = rawData?.success ? rawData.data : rawData;
        if (config?.launcher?.language) {
          i18n.setLocale(config.launcher.language);
        }
        applyTheme(config?.launcher?.theme || 'dark');
        if (config?.launcher?.developmentMode) {
          document.body.classList.add('dev-mode');
        } else {
          document.body.classList.remove('dev-mode');
        }
        updateTitleBarDevMode();
      } catch (e) {
        console.error('[Login] Failed to reload config after change:', e);
      }
      // 重新檢查帳號狀態（OTP 金鑰可能已被清除）
      await refreshAccountState();
    });
  }
  
  // 語系變更時，重新渲染 JS 動態生成的文字
  window.addEventListener('locale-changed', () => {
    // 重新渲染訂閱資訊面板
    if (window._lastSubscriptionType != null && window._lastRemainSeconds != null) {
      updateSubscriptionInfo(window._lastSubscriptionType, window._lastRemainSeconds);
    }
  });
  
  // === Sequential initialization pipeline ===
  // Each step must complete before the next starts
  
  // Step 1: Check for launcher updates (blocking)
  try {
    await initAppUpdater();
  } catch (err) {
    console.error('[Login] Failed to check app updates:', err);
  }
  
  // Step 2: Check game directory setup (blocking - waits for dialog if needed)
  await checkGameDirectorySetup();
  
  // Step 3: Environment initialization (Wine download + prefix)
  console.log('[Login] Starting environment initialization...');
  await startEnvironmentInitialization();
  
  // Step 4: Game version check
  console.log('[Login] Starting game update check...');
  try {
    await startBackgroundUpdate();
  } catch (err) {
    console.error('[Login] Game update check failed:', err);
  }
  
  // Step 5: Dalamud check
  console.log('[Login] Starting Dalamud update check...');
  try {
    await startDalamudUpdate();
  } catch (err) {
    console.error('[Login] Dalamud update check failed:', err);
  }
  
  console.log('[Login] All initialization checks complete');
  
  // Cleanup on page unload
  window.addEventListener('beforeunload', () => {
    cleanupAccountManagement();
    // 移除事件監聽
    if (window.electronAPI.events) {
      window.electronAPI.events.off('config-changed');
    }
  });
  
  console.log('[Login] Initialization complete');
}

/**
 * Bind keyboard navigation and auto-fill state tracking
 * - OTP Enter key triggers login (unless all fields are auto-filled)
 * - Manual input clears auto-filled state for reCaptcha consideration
 */
function bindKeyboardNavigation() {
  const emailInput = document.getElementById('email');
  const passwordInput = document.getElementById('password');
  const otpInput = document.getElementById('otp');
  
  // Clear auto-filled state when user manually types
  emailInput.addEventListener('input', () => {
    clearAutoFilledState('email');
  });
  
  passwordInput.addEventListener('input', () => {
    clearAutoFilledState('password');
  });
  
  otpInput.addEventListener('input', () => {
    // Only clear if OTP is not readonly (manual input mode)
    if (!otpInput.readOnly) {
      clearAutoFilledState('otp');
    }
  });
  
  // OTP Enter key triggers login
  otpInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      
      // If all fields are auto-filled, block Enter login for reCaptcha scoring
      // User must click the login button to collect enough interaction data
      if (isAllAutoFilled()) {
        console.log('[Login] All fields auto-filled, Enter login blocked. Please click the login button.');
        // Optionally show a subtle hint (not blocking alert)
        return;
      }
      
      // Trigger login
      handleLogin();
    }
  });
}

/**
 * Handle login button click
 */
async function handleLogin(event) {
  console.log('[Login] Login button clicked');
  
  // Note: 登入不再中斷更新，更新會在背景持續進行
  
  // Validate form
  if (!validateLoginForm()) {
    return;
  }
  
  // Get form values
  const email = document.getElementById('email').value.trim();
  const password = document.getElementById('password').value;
  const otp = document.getElementById('otp').value.trim();
  const autoFillOTP = document.getElementById('autoFillOTP').checked;
  const rememberPassword = document.getElementById('rememberPassword').checked;
  
  // Get OTP secret if provided
  const otpSecret = getOTPSecretInput();
  
  // Show loading state
  setLoginState('loading');
  
  try {
    // Get reCAPTCHA token (800ms delay for behavior collection)
    console.log('[Login] Getting reCAPTCHA token...');
    const recaptchaToken = await getRecaptchaToken();
    console.log('[Login] reCAPTCHA token obtained');
    
    // Encode credentials (HEX encoding)
    console.log('[Login] Encoding credentials...');
    const encodedEmail = toHex(email);
    const encodedPassword = toHex(password);
    
    // Call backend API via IPC (bypasses fetch issues with baseURLForDataURL)
    console.log('[Login] Calling backend API via IPC: /api/auth/login');
    console.log('[Login] Request body:', {
      email: encodedEmail.substring(0, 10) + '...',
      password: '***',
      otp: otp,
      recaptchaToken: recaptchaToken ? recaptchaToken.substring(0, 20) + '...' : 'none'
    });
    
    const response = await window.electronAPI.backend.call('/api/auth/login', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: {
        email: encodedEmail,
        password: encodedPassword,
        otp: otp,
        recaptchaToken: recaptchaToken
      }
    });
    
    let data;
    try {
      data = await handleApiResponse(response);
    } catch (error) {
      console.error('[Login] API error:', getErrorMessage(error, i18n));
      throw new Error(getErrorMessage(error, i18n));
    }
    
    console.log('[Login] Response received - success');
    
    // handleApiResponse already unwrapped the data, no need to check .success
    // If we got here without error, login was successful
    console.log('[Login] Login successful');
    console.log('[Login] Full response data:', JSON.stringify(data, null, 2));
    console.log('[Login] sessionId:', data.sessionId);
    console.log('[Login] subscriptionType:', data.subscriptionType);
    console.log('[Login] remain (seconds):', data.remain);
    
    // Save session ID and subscription info to localStorage
    localStorage.setItem('sessionId', data.sessionId);
    localStorage.setItem('subscriptionType', data.subscriptionType);
    localStorage.setItem('remain', data.remain);
    
    // Save or clear password based on rememberPassword checkbox
    if (rememberPassword) {
      console.log('[Login] Saving password...');
      await savePassword(email, password);
    } else {
      console.log('[Login] Not saving password (remember unchecked)');
      // Clear password field after successful login
      document.getElementById('password').value = '';
    }
    
    // Save OTP secret if provided
    if (otpSecret) {
      console.log('[Login] Saving OTP secret...');
      await saveOTPSecret(email, otpSecret);
    }
    
    // Save auto-fill OTP preference (always save when login)
    if (await hasOTPSecret(email)) {
      console.log('[Login] Saving autoFillOTP setting:', autoFillOTP);
      await saveAutoFillOTP(email, autoFillOTP);
    }
    
    console.log('[Login] ========== Login Successful ==========');
    console.log('[Login] Session ID:', data.sessionId);
    console.log('[Login] Subscription Type:', data.subscriptionType);
    console.log('[Login] Remain (seconds):', data.remain);
    
    // Show success state (will handle environment initialization internally)
    setLoginState('success', data.sessionId, data.subscriptionType, data.remain);
    
  } catch (error) {
    console.error('[Login] Login failed:', error);
    
    // Show error message
    showError(getErrorMessage(error));
    
    // Reset to idle state
    setLoginState('idle');
  }
}

/**
 * Get reCAPTCHA token
 */
async function getRecaptchaToken() {
  // Wait 800ms to let reCAPTCHA collect behavior data
  await new Promise(resolve => setTimeout(resolve, 800));
  
  return new Promise((resolve, reject) => {
    if (typeof grecaptcha === 'undefined' || !grecaptcha.enterprise) {
      reject(new Error('reCAPTCHA not loaded'));
      return;
    }
    
    grecaptcha.enterprise.ready(() => {
      grecaptcha.enterprise.execute(SITE_KEY, { action: 'LOGIN' })
        .then(token => {
          console.log('[Login] reCAPTCHA token length:', token.length);
          resolve(token);
        })
        .catch(error => {
          console.error('[Login] reCAPTCHA error:', error);
          reject(error);
        });
    });
  });
}

/**
 * Validate login form
 */
function validateLoginForm() {
  const email = document.getElementById('email').value.trim();
  const password = document.getElementById('password').value;
  const otp = document.getElementById('otp').value.trim();
  
  // Email format validation
  const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
  if (!emailRegex.test(email)) {
    showError(i18n.t('login.invalid_email'));
    return false;
  }
  
  // Password length check
  if (password.length < 6) {
    showError(i18n.t('login.password_too_short'));
    return false;
  }
  
  // OTP format check (6 digits)
  if (!/^\d{6}$/.test(otp)) {
    showError(i18n.t('login.invalid_otp'));
    return false;
  }
  
  return true;
}

/**
 * Handle re-login button click
 */
function handleRelogin() {
  console.log('[Login] Re-login requested');
  
  // Clear countdown timers
  clearCountdownTimers('all');
  
  // Clear session ID
  localStorage.removeItem('sessionId');
  localStorage.removeItem('sessionObtainedAt');
  
  // Reset to idle state
  setLoginState('idle');
  
  // Clear OTP field
  document.getElementById('otp').value = '';
}

let isOpeningSettings = false;

/**
 * Handle open settings button click
 */
async function handleOpenSettings() {
  // Prevent multiple rapid clicks
  if (isOpeningSettings) {
    console.log('[Login] Settings window is already opening, ignoring click');
    return;
  }
  
  isOpeningSettings = true;
  console.log('[Login] Opening settings window');
  try {
    await window.electronAPI.openSettings();
  } catch (error) {
    console.error('[Login] Failed to open settings:', error);
  } finally {
    // Reset after a short delay
    setTimeout(() => {
      isOpeningSettings = false;
    }, 500);
  }
}

/**
 * Handle launch game button click
 */
async function handleLaunchGame() {
  const sessionId = localStorage.getItem('sessionId');
  
  if (!sessionId) {
    showError(i18n.t('login.no_session'));
    return;
  }
  
  if (!isEnvironmentInitialized) {
    showError(i18n.t('login.env_not_ready'));
    return;
  }
  
  console.log('[Login] Launching game with session:', sessionId);
  
  const launchButton = document.getElementById('launchButton');
  launchButton.disabled = true;
  setButtonI18n(launchButton, 'login.launching');
  
  try {
    // Launch game via IPC
    const response = await window.electronAPI.backend.call('/api/game/launch', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ sessionId })
    });
    
    let data;
    try {
      data = await handleApiResponse(response);
    } catch (error) {
      console.error('[Login] Failed to launch game:', getErrorMessage(error, i18n));
      showError(i18n.t('login.launch_failed', { message: getErrorMessage(error, i18n) }));
      launchButton.disabled = false;
      setButtonI18n(launchButton, 'button.launch');
      return;
    }
    
    // handleApiResponse unwrapped the data, if we got here it's successful
    console.log('[Login] Game launched successfully, PID:', data.processId);
    setButtonI18n(launchButton, 'login.game_running');
    showTitleMessage(i18n.t('login.game_launched'));
    
    // Wait for game exit
    await waitForGameExit();
  } catch (error) {
    console.error('[Login] Launch error:', error);
    showError(i18n.t('login.launch_error'));
    launchButton.disabled = false;
    setButtonI18n(launchButton, 'button.launch');
  }
}

/**
 * Wait for game to exit and handle exit code
 */
async function waitForGameExit() {
  const launchButton = document.getElementById('launchButton');
  
  try {
    console.log('[Login] Waiting for game exit...');
    
    const response = await window.electronAPI.backend.call('/api/game/wait-exit', {
      method: 'GET'
    });
    
    let data;
    try {
      data = await handleApiResponse(response);
    } catch (error) {
      console.error('[Login] Wait for exit error:', getErrorMessage(error, i18n));
      return;
    }
    
    const exitCode = data.exitCode;
    console.log('[Login] Game exited with code:', exitCode);
    
    // Exit code 0 or 1 is normal, otherwise show warning
    if (exitCode !== 0 && exitCode !== 1 && exitCode !== null) {
      showGameExitWarning(exitCode);
    }
  } catch (error) {
    console.error('[Login] Wait for exit error:', error);
  } finally {
    // Re-enable launch button
    launchButton.disabled = false;
    setButtonI18n(launchButton, 'button.launch');
  }
}

/**
 * Show warning dialog for abnormal game exit
 */
function showGameExitWarning(exitCode) {
  const message = i18n.t('login.game_exit_abnormal', { exitCode });
  
  // Use native dialog if available, otherwise alert
  if (window.electronAPI && window.electronAPI.showMessageBox) {
    window.electronAPI.showMessageBox({
      type: 'warning',
      title: i18n.t('login.game_exit_warning_title'),
      message: message
    });
  } else {
    alert(message);
  }
}

/**
 * Set login UI state
 */
function setLoginState(state, sessionId = null, subscriptionType = null, remain = null) {
  console.log('[Login] State change:', currentState, '->', state);
  currentState = state;
  
  const loginForm = document.getElementById('loginForm');
  const loadingView = document.getElementById('loadingView');
  const successView = document.getElementById('successView');
  const loginButton = document.getElementById('loginButton');
  
  switch (state) {
    case 'idle':
      // Show login form
      loginForm.style.display = 'block';
      loadingView.style.display = 'none';
      successView.style.display = 'none';
      loginButton.disabled = false;
      break;
      
    case 'loading':
      // Show loading
      loginForm.style.display = 'none';
      loadingView.style.display = 'block';
      successView.style.display = 'none';
      loginButton.disabled = true;
      break;
      
    case 'success':
      // Show success view
      loginForm.style.display = 'none';
      loadingView.style.display = 'none';
      successView.style.display = 'block';
      
      // Handle launch button state
      const launchButton = document.getElementById('launchButton');
      if (launchButton) {
        // 初始禁用，等待方案B更新檢查完成
        launchButton.disabled = true;
        setButtonI18n(launchButton, 'login.checking_updates');
      }
      
      // Update subscription info
      if (subscriptionType !== null && remain !== null) {
        updateSubscriptionInfo(subscriptionType, remain);
      }
      
      // 登入成功後，檢查更新是否仍在進行
      // 更新會在背景持續進行，不需要重新啟動
      const gameUpdateInProgress = isUpdating();
      const dalamudUpdateInProgress = isDalamudUpdating();
      const envReady = isEnvironmentInitialized;
      
      if (!envReady || gameUpdateInProgress || dalamudUpdateInProgress) {
        console.log('[Login] Not ready (env:', envReady, ', gameUpdate:', gameUpdateInProgress, ', dalamudUpdate:', dalamudUpdateInProgress, ')');
        // 設置安全計時器：定期檢查所有狀態，避免 callback 遺漏導致按鈕永遠卡住
        const safetyCheckInterval = setInterval(() => {
          if (isEnvironmentInitialized && !isUpdating() && !isDalamudUpdating()) {
            clearInterval(safetyCheckInterval);
            const btn = document.getElementById('launchButton');
            if (btn && btn.disabled) {
              console.log('[Login] Safety check: all ready, enabling launch button');
              btn.disabled = false;
              setButtonI18n(btn, 'button.launch');
            }
          }
        }, 1000);
      } else {
        console.log('[Login] All ready, enabling launch button');
        if (launchButton) {
          launchButton.disabled = false;
          setButtonI18n(launchButton, 'button.launch');
        }
      }
      break;
  }
}

/**
 * Update subscription information display with live countdowns
 */
function updateSubscriptionInfo(subscriptionType, remainSeconds) {
  // 保存狀態供語言切換時重新渲染
  window._lastSubscriptionType = subscriptionType;
  window._lastRemainSeconds = remainSeconds;
  
  const subTypeKey = subscriptionType === 0 ? 'login.sub_free' :
                     subscriptionType === 1 ? 'login.sub_crystal' : 
                     subscriptionType === 2 ? 'login.sub_credit' : 
                     'login.sub_unknown';
  
  const sessionInfo = document.getElementById('sessionInfo');
  sessionInfo.innerHTML = `
    <div style="text-align: left; line-height: 1.8;">
      <div><span data-i18n="login.sub_type">${i18n.t('login.sub_type')}</span><br><strong style="padding-left: 1em;" data-i18n="${subTypeKey}">${i18n.t(subTypeKey)}</strong></div>
      <div><span data-i18n="login.remain_time">${i18n.t('login.remain_time')}</span><br><strong id="remainCountdown" style="padding-left: 1em;"></strong></div>
      <div><span data-i18n="login.session_time">${i18n.t('login.session_time')}</span><br><strong id="sessionCountdown" style="padding-left: 1em;"></strong></div>
    </div>
    <div id="sessionWarning" style="display: none;"></div>
  `;
  
  // Start remain countdown (subscription time)
  // Free trial (type 0) with remain=0 means unlimited
  if (subscriptionType === 0 && remainSeconds === 0) {
    const el = document.getElementById('remainCountdown');
    if (el) el.textContent = i18n.t('login.remain_unlimited');
  } else {
    startRemainCountdown(remainSeconds);
  }
  
  // Start session countdown (3-hour session TTL)
  startSessionCountdown();
}

/**
 * Format seconds into a human-readable time string (days, hours, minutes, seconds)
 */
function formatCountdown(totalSeconds) {
  if (totalSeconds <= 0) return i18n.t('login.time_less_1h');
  
  const days = Math.floor(totalSeconds / 86400);
  const hours = Math.floor((totalSeconds % 86400) / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  
  let parts = [];
  if (days > 0) parts.push(`${days} ${i18n.t('login.days')}`);
  if (hours > 0) parts.push(`${hours} ${i18n.t('login.hours')}`);
  if (minutes > 0) parts.push(`${minutes} ${i18n.t('login.minutes')}`);
  if (seconds > 0 || parts.length === 0) parts.push(`${seconds} ${i18n.t('login.seconds')}`);
  
  return parts.join(' ');
}

/**
 * Start remain countdown timer (subscription remaining time, precise to seconds)
 */
function startRemainCountdown(remainSeconds) {
  clearCountdownTimers('remain');
  
  // 只在首次設定 expireTime，語言切換重新渲染時沿用已有的
  if (!remainExpireTime) {
    remainExpireTime = Date.now() + remainSeconds * 1000;
  }
  
  function updateRemain() {
    const el = document.getElementById('remainCountdown');
    if (!el) { clearCountdownTimers('remain'); return; }
    
    const remaining = Math.max(0, Math.floor((remainExpireTime - Date.now()) / 1000));
    el.textContent = formatCountdown(remaining);
  }
  
  updateRemain();
  remainTimerId = setInterval(updateRemain, 1000);
}

/**
 * Start session elapsed timer (shows how long since sessionId was obtained)
 * Warns user to re-login after 3 hours
 */
const SESSION_TTL_SECONDS = 3 * 60 * 60; // 3 hours

function startSessionCountdown() {
  clearCountdownTimers('session');
  
  // 只在首次設定 sessionObtainedAt，語言切換重新渲染時沿用已有的
  if (!sessionObtainedAt) {
    sessionObtainedAt = Date.now();
    localStorage.setItem('sessionObtainedAt', sessionObtainedAt.toString());
  }
  let warned = false;
  
  function updateSession() {
    const el = document.getElementById('sessionCountdown');
    const warningEl = document.getElementById('sessionWarning');
    if (!el) { clearCountdownTimers('session'); return; }
    
    const elapsed = Math.floor((Date.now() - sessionObtainedAt) / 1000);
    el.textContent = formatCountdown(elapsed);
    
    // Warning when session exceeds 3 hours
    if (elapsed >= SESSION_TTL_SECONDS && !warned) {
      warned = true;
      el.style.color = '#ef4444';
      if (warningEl) {
        warningEl.style.display = 'block';
        warningEl.textContent = i18n.t('login.session_expired_warning');
      }
    }
  }
  
  updateSession();
  sessionTimerId = setInterval(updateSession, 1000);
}

/**
 * Clear countdown timers
 */
function clearCountdownTimers(type) {
  if (type === 'remain' || type === 'all') {
    if (remainTimerId) { clearInterval(remainTimerId); remainTimerId = null; }
    if (type === 'all') remainExpireTime = null;
  }
  if (type === 'session' || type === 'all') {
    if (sessionTimerId) { clearInterval(sessionTimerId); sessionTimerId = null; }
    if (type === 'all') sessionObtainedAt = null;
  }
}

/**
 * Show error message
 */
function showError(message) {
  const errorElement = document.getElementById('errorMessage');
  errorElement.textContent = message;
  errorElement.classList.add('show');
  
  // Auto hide after 5 seconds
  setTimeout(() => {
    errorElement.classList.remove('show');
  }, 5000);
}

/**
 * Get user-friendly error message
 */
function getErrorMessage(error) {
  const message = error.message || error.toString();
  
  // Map error codes to i18n keys
  const errorMap = {
    'AUTH_FAILED': 'error.auth_failed',
    'INVALID_OTP': 'error.invalid_otp',
    'CAPTCHA_FAILED': 'error.captcha_failed',
    'NETWORK_ERROR': 'error.network_error',
    'INVALID_REQUEST': 'error.invalid_request'
  };
  
  for (const [code, key] of Object.entries(errorMap)) {
    if (message.includes(code)) {
      return i18n.t(key);
    }
  }
  
  // Default error message
  if (message.includes('Failed to fetch') || message.includes('NetworkError')) {
    return i18n.t('error.server_unavailable');
  }
  
  return message || i18n.t('error.unknown');
}

/**
 * Start environment initialization
 */
let isEnvironmentInitialized = false;
let isInitializing = false;

function startEnvironmentInitialization() {
  return new Promise((resolve) => {
    console.log('[ENV-INIT] ========== startEnvironmentInitialization() called ==========');
    console.log('[ENV-INIT] isInitializing:', isInitializing);
    console.log('[ENV-INIT] isEnvironmentInitialized:', isEnvironmentInitialized);
    
    // Check platform first
    const platform = window.electronAPI?.getPlatform ? window.electronAPI.getPlatform() : 'unknown';
    console.log('[ENV-INIT] Platform:', platform);
    
    // Windows doesn't need Wine initialization
    if (platform === 'win32') {
      console.log('[ENV-INIT] Windows platform detected, skipping Wine initialization');
      isEnvironmentInitialized = true;
      const launchButton = document.getElementById('launchButton');
      const settingsBtn = document.getElementById('settingsBtn');
      if (settingsBtn) {
        settingsBtn.disabled = false;
      }
      console.log('[ENV-INIT] ========== Initialization skipped (Windows) ==========');
      resolve();
      return;
    }
    
    if (isInitializing || isEnvironmentInitialized) {
      console.log('[ENV-INIT] Skipping: Environment already initialized or initializing');
      resolve();
      return;
    }
  
  isInitializing = true;
  console.log('[ENV-INIT] Starting environment initialization...');
  
  // Get title bar elements
  const titleBarCard = document.querySelector('.title-bar-card');
  const titleBarText = document.getElementById('titleBarText');
  const progressBarBg = document.getElementById('progressBarBg');
  const progressFill = document.getElementById('progressFill');
  const launchButton = document.getElementById('launchButton');
  const settingsBtn = document.getElementById('settingsBtn');
  
  console.log('[ENV-INIT] UI elements:', {
    titleBarCard: !!titleBarCard,
    titleBarText: !!titleBarText,
    progressBarBg: !!progressBarBg,
    progressFill: !!progressFill,
    launchButton: !!launchButton,
    settingsBtn: !!settingsBtn
  });
  
  // Keep launch button and settings button disabled
  launchButton.disabled = true;
  setButtonI18n(launchButton, 'login.preparing');
  settingsBtn.disabled = true;
  
  // Switch to progress mode
  titleBarCard.classList.add('progress-mode');
  progressFill.style.width = '100%';
  titleBarText.textContent = i18n.t('login.init_env');
  
  console.log('[ENV-INIT] UI updated, connecting to SSE...');
  
  // Connect to SSE endpoint
  const baseUrl = 'http://localhost:5050';
  const sseUrl = `${baseUrl}/api/environment/initialize`;
  console.log('[ENV-INIT] SSE URL:', sseUrl);
  
  try {
    const eventSource = new EventSource(sseUrl);
    console.log('[ENV-INIT] EventSource created, readyState:', eventSource.readyState);
    
    eventSource.onopen = () => {
      console.log('[ENV-INIT] ✓ SSE connection OPENED');
    };
  
    eventSource.addEventListener('progress', (event) => {
      console.log('[ENV-INIT] << Received "progress" event');
      try {
        const data = JSON.parse(event.data);
        console.log('[ENV-INIT] Progress data:', data);
        
        // Get i18n message
        const message = i18n.t(data.messageKey);
        const percentage = data.percentage || 0;
        const fileName = data.currentFile || '';
        
        // Display format based on stage
        if (data.stage === 'downloading' && data.downloadedMB && data.totalMB) {
          // Detailed download format:
          // 下載WINE中 - {檔案名稱} - {已下載容量} MB/ {總容量} MB | {下載速度} MB/s - {已下載/總下載} %
          const downloadedMB = data.downloadedMB.toFixed(2);
          const totalMB = data.totalMB.toFixed(2);
          const speedMBps = (data.downloadSpeedMBps || 0).toFixed(2);
          titleBarText.textContent = `${message} - ${fileName} - ${downloadedMB} MB / ${totalMB} MB | ${speedMBps} MB/s - ${percentage.toFixed(1)}%`;
        } else if (fileName) {
          // Fallback: Show filename with percentage
          titleBarText.textContent = `${message} - ${fileName} - ${percentage.toFixed(1)}%`;
        } else {
          // Basic: Just message with percentage
          titleBarText.textContent = `${message} - ${percentage.toFixed(1)}%`;
        }
        
        // Update progress bar
        progressFill.style.width = `${percentage}%`;
        console.log('[ENV-INIT] Progress bar updated:', percentage.toFixed(1) + '%');
      } catch (err) {
        console.error('[ENV-INIT] Failed to parse progress event:', err, 'Raw data:', event.data);
      }
    });
  
    eventSource.addEventListener('complete', (event) => {
      console.log('[ENV-INIT] << Received "complete" event');
      try {
        const data = JSON.parse(event.data);
        console.log('[ENV-INIT] Complete data:', data);
        
        const message = data.messageKey
          ? (data.params ? i18n.t(data.messageKey, data.params) : i18n.t(data.messageKey))
          : i18n.t('progress.complete');
        titleBarText.textContent = message;
        progressFill.style.width = '100%';
        
        // Return to normal mode after 2 seconds
        setTimeout(() => {
          titleBarCard.classList.remove('progress-mode');
          setTimeout(() => {
            progressFill.style.width = '0%';
            titleBarText.textContent = appVersionText;
          }, 2000);
        }, 2000);
        
        // Environment initialization complete
        isEnvironmentInitialized = true;
        isInitializing = false;
        settingsBtn.disabled = false;
        
        console.log('[ENV-INIT] ========== Initialization COMPLETE ==========');
        
        // Close SSE connection after a short delay to ensure all events are processed
        setTimeout(() => {
          eventSource.close();
          console.log('[ENV-INIT] EventSource closed after completion');
        }, 500);
        
        // Resolve Promise — pipeline continues to next step
        resolve();
      } catch (err) {
        console.error('[ENV-INIT] Failed to parse complete event:', err, 'Raw data:', event.data);
        resolve();
      }
    });
  
    eventSource.addEventListener('error', (event) => {
      console.error('[ENV-INIT] << Received "error" event');
      
      // Ignore error events after successful completion
      // (Browser EventSource may trigger 'error' type event when server closes connection)
      if (isEnvironmentInitialized) {
        console.log('[ENV-INIT] Ignoring error event after successful completion');
        return;
      }
      
      if (event.data) {
        try {
          const data = JSON.parse(event.data);
          console.error('[ENV-INIT] Error data:', data);
          
          const errorMsg = data.errorMessageKey
            ? ((() => {
                const errorParams = data.params ?? data.extraData ?? data.errorParams;
                return errorParams ? i18n.t(data.errorMessageKey, errorParams) : i18n.t(data.errorMessageKey);
              })())
            : i18n.t('error.unknown');
          
          showError(errorMsg);
          alert(errorMsg);
          
          titleBarCard.classList.remove('progress-mode');
          setTimeout(() => {
            progressFill.style.width = '0%';
            titleBarText.textContent = appVersionText;
          }, 2000);
          
          isInitializing = false;
          launchButton.disabled = true;
          setButtonI18n(launchButton, 'login.env_init_failed');
          settingsBtn.disabled = false;  // Allow user to check settings even on error
          
          console.log('[ENV-INIT] ========== Initialization FAILED ==========');
          eventSource.close();
          resolve();
        } catch (err) {
          console.error('[ENV-INIT] Failed to parse error event:', err, 'Raw data:', event.data);
          resolve();
        }
      } else {
        console.error('[ENV-INIT] Error event without data');
      }
    });
  
    eventSource.onerror = (err) => {
      console.error('[ENV-INIT] !! EventSource onerror triggered');
      console.error('[ENV-INIT] Error object:', err);
      console.error('[ENV-INIT] EventSource readyState:', eventSource.readyState);
      
      // Wait a short moment to see if complete event arrives (race condition)
      // The backend may send complete and close connection quickly
      setTimeout(() => {
        // Only handle if not already completed or if backend truly disconnected
        if (!isEnvironmentInitialized && isInitializing) {
          console.error('[ENV-INIT] Backend connection failed during initialization');
          const errorMsg = i18n.t('login.backend_disconnected');
          showError(errorMsg);
          
          // Return to normal mode
          titleBarCard.classList.remove('progress-mode');
          setTimeout(() => {
            progressFill.style.width = '0%';
            titleBarText.textContent = appVersionText;
          }, 2000);
          
          isInitializing = false;
          // Keep button disabled on error
          launchButton.disabled = true;
          setButtonI18n(launchButton, 'login.backend_failed');
          settingsBtn.disabled = false;  // Allow user to check settings even on connection error
          
          console.log('[ENV-INIT] ========== Connection FAILED ==========');
          eventSource.close();
          resolve();
        } else {
          // Connection closed normally after completion
          console.log('[ENV-INIT] EventSource closed (normal after completion or already initialized)');
        }
      }, 300); // Wait 300ms for complete event
    };
  } catch (err) {
    console.error('[ENV-INIT] !! EXCEPTION creating EventSource:', err);
    showError(i18n.t('login.connection_failed'));
    isInitializing = false;
    resolve();
  }
  });
}

/**
 * Show message in title bar
 */
function showTitleMessage(message) {
  const titleBarText = document.getElementById('titleBarText');
  const originalText = titleBarText.textContent;
  
  titleBarText.textContent = message;
  
  setTimeout(() => {
    titleBarText.textContent = originalText;
  }, 3000);
}

/**
 * Load and display version
 */
async function loadVersion() {
  try {
    const versionData = await window.electronAPI.getVersion();
    appVersionText = `${versionData.appName} v${versionData.version}`;
    updateTitleBarDevMode();
    console.log('[Login] Version loaded:', appVersionText);
    
    // Share version text with updateManager
    setAppVersionText(appVersionText);
  } catch (error) {
    console.error('[Login] Failed to load version:', error);
    appVersionText = 'XIV The Calamity';  // Fallback
    setAppVersionText(appVersionText);
  }
}

/**
 * Update title bar text with dev mode indicator
 */
function updateTitleBarDevMode() {
  const titleBarText = document.getElementById('titleBarText');
  if (!titleBarText) return;
  // Strip existing dev mode suffix
  const baseText = appVersionText.replace(/ \(dev mode\)$/, '');
  appVersionText = baseText;
  if (document.body.classList.contains('dev-mode')) {
    appVersionText += ' (dev mode)';
  }
  titleBarText.textContent = appVersionText;
  setAppVersionText(appVersionText);
}

/**
 * Check game directory setup on startup
 */
async function checkGameDirectorySetup() {
  try {
    console.log('[GameSetup] Checking game directory configuration...');
    
    // Get current config
    const response = await window.electronAPI.backend.call('/api/config');
    console.log('[GameSetup] API response:', JSON.stringify(response, null, 2));
    
    let config;
    try {
      config = await handleApiResponse(response);
    } catch (error) {
      console.error('[GameSetup] Failed to load config:', getErrorMessage(error, i18n));
      await showGameSetupDialog();
      return;
    }
    
    console.log('[GameSetup] Config data:', JSON.stringify(config, null, 2));
    
    if (!config || !config.game || !config.game.gamePath) {
      console.log('[GameSetup] Game path is empty, showing setup dialog');
      await showGameSetupDialog();
      return;
    }
    
    console.log('[GameSetup] Game path found:', config.game.gamePath);
    
    // Validate game directory
    const validation = await window.electronAPI.validateGameDirectory(config.game.gamePath);
    console.log('[GameSetup] Validation result:', JSON.stringify(validation, null, 2));
    
    if (!validation.valid) {
      console.warn('[GameSetup] Game directory is invalid:', validation.reason);
      await showGameSetupDialog();
      return;
    }
    
    console.log('[GameSetup] Game directory is valid');
  } catch (error) {
    console.error('[GameSetup] Failed to check game directory:', error);
    await showGameSetupDialog();
  }
}

/**
 * Show game setup dialog
 * Returns a Promise that resolves when the user completes setup
 */
function showGameSetupDialog() {
  return new Promise((resolve) => {
    const dialog = document.getElementById('gameSetupDialog');
    const existingBtn = document.getElementById('existingGameButton');
    const installBtn = document.getElementById('installGameButton');
    
    dialog.style.display = 'flex';
    
    // Existing game - select directory
    existingBtn.onclick = async () => {
      try {
        const result = await window.electronAPI.selectDirectory({
          title: i18n.t('login.game_setup.select_existing'),
          buttonLabel: i18n.t('button.select')
        });
        
        if (result.canceled) {
          return;
        }
        
        if (!result.success) {
          alert(i18n.t('login.game_setup.error_select'));
          return;
        }
        
        // Validate selected directory
        const validation = await window.electronAPI.validateGameDirectory(result.path);
        console.log('[GameSetup] Validation result:', validation);
        
        if (!validation.valid) {
          // Translate the validation reason
          let translatedReason = validation.reason;
          if (validation.reason === 'Directory does not exist') {
            translatedReason = i18n.t('login.game_setup.validation.not_exist');
          } else if (validation.reason === 'Missing required subdirectories (game, boot)') {
            translatedReason = i18n.t('login.game_setup.validation.missing_subdirs');
          }
          
          alert(i18n.t('login.game_setup.error_invalid', { reason: translatedReason }));
          return;
        }
        
        // Save to config
        await saveGamePath(result.path);
        dialog.style.display = 'none';
        console.log('[GameSetup] Path saved, continuing pipeline');
        resolve();
        
      } catch (error) {
        console.error('[GameSetup] Failed to select existing game:', error);
        alert(i18n.t('login.game_setup.error_general'));
      }
    };
    
    // Install new game - create directory
    installBtn.onclick = async () => {
      try {
        const result = await window.electronAPI.selectDirectory({
          title: i18n.t('login.game_setup.select_install'),
          buttonLabel: i18n.t('button.create')
        });
        
        if (result.canceled) {
          return;
        }
        
        if (!result.success) {
          alert(i18n.t('login.game_setup.error_select'));
          return;
        }
        
        // Create game directory structure
        const createResult = await window.electronAPI.createDirectory(result.path);
        
        if (!createResult.success) {
          alert(i18n.t('login.game_setup.error_create'));
          return;
        }
        
        // Save to config
        await saveGamePath(createResult.path);
        dialog.style.display = 'none';
        console.log('[GameSetup] Path saved, continuing pipeline');
        resolve();
      
    } catch (error) {
      console.error('[GameSetup] Failed to create game directory:', error);
      alert(i18n.t('login.game_setup.error_general'));
    }
  };
  });
}

/**
 * Save game path to config
 */
async function saveGamePath(gamePath) {
  try {
    console.log('[GameSetup] Saving game path:', gamePath);
    
    // Get current config
    const response = await window.electronAPI.backend.call('/api/config');
    
    let config;
    try {
      config = await handleApiResponse(response);
    } catch (error) {
      console.error('[GameSetup] Failed to load config:', getErrorMessage(error, i18n));
      throw new Error(`Failed to load config: ${getErrorMessage(error, i18n)}`);
    }
    
    // Ensure game object exists
    if (!config.game) {
      config.game = {};
    }
    
    // Update game path
    config.game.gamePath = gamePath;
    
    // Save config
    const saveResponse = await window.electronAPI.backend.call('/api/config', {
      method: 'PUT',
      body: JSON.stringify(config)
    });
    
    try {
      await handleApiResponse(saveResponse);
    } catch (error) {
      console.error('[GameSetup] Failed to save config:', getErrorMessage(error, i18n));
      throw new Error(`Failed to save config: ${getErrorMessage(error, i18n)}`);
    }
    
    console.log('[GameSetup] Game path saved successfully');
  } catch (error) {
    console.error('[GameSetup] Failed to save game path:', error);
    throw error;
  }
}

// Initialize when DOM is ready
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init);
} else {
  init();
}

// Export for testing
export { 
  init, 
  handleLogin, 
  setLoginState, 
  validateLoginForm, 
  updateSubscriptionInfo, 
  startEnvironmentInitialization
};

/**
 * 顯示標題欄進度條
 */
export function showTitleBarProgress(percentage, message) {
  const titleBarCard = document.querySelector('.title-bar-card');
  const titleBarText = document.getElementById('titleBarText');
  const progressFill = document.getElementById('progressFill');
  
  if (!titleBarCard || !titleBarText || !progressFill) {
    return;
  }
  
  // Switch to progress mode
  titleBarCard.classList.add('progress-mode');
  
  // Update progress
  progressFill.style.width = `${percentage}%`;
  
  // Update text (support both i18n key and direct text)
  if (typeof message === 'string' && message.startsWith('login.')) {
    titleBarText.textContent = i18n.t(message);
  } else {
    titleBarText.textContent = message;
  }
}

/**
 * 隱藏標題欄進度條
 */
export function hideTitleBarProgress() {
  const titleBarCard = document.querySelector('.title-bar-card');
  const titleBarText = document.getElementById('titleBarText');
  const progressFill = document.getElementById('progressFill');
  
  if (!titleBarCard || !titleBarText || !progressFill) {
    return;
  }
  
  // Return to normal mode
  titleBarCard.classList.remove('progress-mode');
  
  // Reset progress bar
  setTimeout(() => {
    progressFill.style.width = '0%';
  }, 300);
  
  // Restore version text
  if (typeof window._appVersionText !== 'undefined') {
    titleBarText.textContent = window._appVersionText;
  } else {
    titleBarText.textContent = appVersionText;
  }
}
