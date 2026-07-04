/**
 * Settings Page Logic
 */

import '../../utils/polyfill.js';
import i18n from '../../i18n/index.js';
import { applyTheme } from '../../utils/theme.js';
import { getLastUsedAccount, deleteOTPSecret } from '../../utils/credentialsStore.js';

let currentConfig = null;
let currentPlatform = 'win32';
let localProfiles = ['default'];
let currentSelectedProfile = 'default';
let startupActiveProfile = 'default';
let profileDescriptions = {}; // Map of profileName -> description string

/**
 * Konami Code 偵測引擎 - 使用 KeyCode 避免大小寫問題
 */
const konamiCode = {
  // KeyCode 序列: ↑↑↓↓←→←→BA
  sequence: [38, 38, 40, 40, 37, 39, 37, 39, 66, 65],
  input: [],
  
  reset() {
    this.input = [];
  },
  
  isMatching(keyCode) {
    this.input.push(keyCode);
    
    // 檢查是否匹配
    const isMatch = this.input.length === this.sequence.length && 
                    this.input.every((code, index) => code === this.sequence[index]);
    
    // 只保留最後 10 個鍵
    if (this.input.length > this.sequence.length) {
      this.input.shift();
    }
    
    return isMatch;
  }
};


/**
 * Initialize settings page
 */
async function init() {
  console.log('[Settings] Initializing settings page');
  
  // Wrap window.close to log stack traces
  const originalClose = window.close;
  window.close = function() {
    console.log('[Settings] window.close() triggered. Call stack:', new Error().stack);
    if (originalClose) {
      originalClose.apply(this, arguments);
    }
  };
  
  // Detect platform and add body class (synchronous)
  detectPlatform();
  
  // Initialize tab navigation
  initTabNavigation();
  
  // Load configuration and apply theme/locale visuals on startup active profile load
  await loadConfig(null, true);
  
  // Update Dalamud tab visibility based on configuration
  updateDalamudTabVisibility();
  
  // Initialize each tab
  initGeneralTab();
  initWineTab();
  initProtonGeTab();
  initDalamudTab();
  initAboutTab();
  
  // Setup event listeners
  setupEventListeners();
  
  // Add Konami Code listener
  document.addEventListener('keydown', handleKonamiCodeInput);
  
  // Apply i18n
  i18n.updateElements();
  
  console.log('[Settings] Initialization complete');
}

/**
 * Detect platform and set body class
 */
function detectPlatform() {
  try {
    currentPlatform = window.xivtc.getPlatform();
    document.body.classList.add(`platform-${currentPlatform}`);
    console.log('[Settings] Platform detected:', currentPlatform);
  } catch (error) {
    console.error('[Settings] Failed to detect platform:', error);
    // Default to darwin (macOS) since that's our primary platform
    currentPlatform = 'darwin';
    document.body.classList.add('platform-darwin');
  }
}

/**
 * Initialize tab navigation
 */
function initTabNavigation() {
  const tabButtons = document.querySelectorAll('.tab-button');
  
  tabButtons.forEach(button => {
    button.addEventListener('click', () => {
      const tabId = button.dataset.tab;
      switchTab(tabId);
    });
  });
}

/**
 * Switch to a different tab
 */
function switchTab(tabId) {
  // Deactivate all tabs
  document.querySelectorAll('.tab-button').forEach(btn => {
    btn.classList.remove('active');
  });
  document.querySelectorAll('.tab-content').forEach(content => {
    content.classList.remove('active');
  });
  
  // Activate target tab
  const targetButton = document.querySelector(`[data-tab="${tabId}"]`);
  const targetContent = document.getElementById(`tab-${tabId}`);
  
  if (targetButton && targetContent) {
    targetButton.classList.add('active');
    targetContent.classList.add('active');
  }
}

/**
 * Load configuration from backend
 */
async function loadConfig(profile = null, applyVisuals = false) {
  try {
    const url = profile ? `/api/config?profile=${profile}` : '/api/config';
    const response = await window.xivtc.backend.call(url, {
      method: 'GET'
    });
    if (response.ok && response.data) {
      // Handle new API response format: { success: true, data: {...} }
      const configData = response.data.success ? response.data.data : response.data;
      currentConfig = configData;
      populateForm(currentConfig, applyVisuals);
      if (profile) {
        currentSelectedProfile = profile;
        profileDescriptions[profile] = configData.launcher?.description || '';
      }
      console.log('[Settings] Configuration loaded for profile:', profile || 'active', currentConfig);
      // Update game path editable state after profile switch
      updateGamePathReadonly();
    }
  } catch (error) {
    console.error('[Settings] Failed to load configuration:', error);
    showError(i18n.t('settings.load_failed'));
  }
}

/**
 * Show/hide elements marked with class "debug-only" based on Debug mode state
 */
function updateDebugOnlyVisibility() {
  const isDebug = document.getElementById('debugLogging')?.checked || false;
  document.querySelectorAll('.debug-only').forEach(el => {
    el.style.display = isDebug ? '' : 'none';
  });
}

/**
 * Update Dalamud tab visibility based on configuration
 */
function updateDalamudTabVisibility() {
  const dalamudTabButton = document.querySelector('[data-tab="dalamud"]');
  const dalamudTabContent = document.getElementById('tab-dalamud');
  
  if (!currentConfig?.launcher?.showDalamudTab) {
    // Hide Dalamud tab
    if (dalamudTabButton) {
      dalamudTabButton.classList.add('hidden');
      dalamudTabButton.classList.remove('active');
      console.log('[Settings] Dalamud tab button hidden');
    }
    if (dalamudTabContent) {
      dalamudTabContent.classList.add('hidden');
      dalamudTabContent.classList.remove('active');
      console.log('[Settings] Dalamud tab content hidden');
    }
  } else {
    // Show Dalamud tab
    if (dalamudTabButton) {
      dalamudTabButton.classList.remove('hidden');
      console.log('[Settings] Dalamud tab button shown');
    }
    if (dalamudTabContent) {
      dalamudTabContent.classList.remove('hidden');
      console.log('[Settings] Dalamud tab content shown');
    }
  }
}

/**
 * Populate form with current configuration
 */
function populateForm(config, applyVisuals = false) {
  if (!config) return;
  
  // General settings
  if (config.launcher) {
    document.getElementById('language').value = config.launcher.language || 'zh-TW';
    if (applyVisuals) {
      i18n.setLocale(config.launcher.language || 'zh-TW');
    }
    document.getElementById('debugLogging').checked = config.launcher.developmentMode || false;
    document.getElementById('enablePreRelease').checked = config.launcher.enablePreRelease || false;
    updateDebugOnlyVisibility();
    
    const savedTheme = config.launcher.theme || 'dark';
    const themeRadio = document.querySelector(`input[name="theme"][value="${savedTheme}"]`);
    if (themeRadio) themeRadio.checked = true;
    if (applyVisuals) {
      applyTheme(savedTheme);
    }

  }
  
  if (config.game) {
    document.getElementById('gamePath').value = config.game.gamePath || '';
  }
  
  // Wine settings
  if (config.wine) {
    console.log('[Settings] Populating Wine settings:', JSON.stringify(config.wine));
    document.getElementById('metalFxEnabled').checked = config.wine.metalFxSpatialEnabled || false;
    document.getElementById('metalFxFactor').value = config.wine.metalFxSpatialFactor || 2.0;
    document.getElementById('metalFxFactor').disabled = !document.getElementById('metalFxEnabled').checked;
    updateMetalFxFactorValue(config.wine.metalFxSpatialFactor || 2.0);
    document.getElementById('hudEnabled').checked = config.wine.metal3PerformanceOverlay || false;
    document.getElementById('hudScale').value = config.wine.hudScale || 1.0;
    updateHudScaleValue(config.wine.hudScale || 1.0);
    document.getElementById('nativeResolution').checked = config.wine.nativeResolution || false;
    document.getElementById('maxFramerate').value = config.wine.maxFramerate || 60;
    document.getElementById('audioRouting').checked = config.wine.audioRouting || false;
    console.log('[Settings] audioRouting value from config:', config.wine.audioRouting, '-> checkbox set to:', document.getElementById('audioRouting').checked);
    document.getElementById('fsyncEnabled').checked = config.wine.fsyncEnabled || false;
    document.getElementById('msyncEnabled').checked = config.wine.msync !== undefined ? config.wine.msync : true;
    document.getElementById('useHomeAlias').checked = config.wine.useHomeAlias || false;
    document.getElementById('wineDebug').value = config.wine.wineDebug || '';
    console.log('[Settings] wineDebug value from config:', config.wine.wineDebug, '-> input set to:', document.getElementById('wineDebug').value);
    
    // Keyboard mapping (macOS only)
    document.getElementById('leftOptionMapping').value = config.wine.leftOptionIsAlt !== undefined ? String(config.wine.leftOptionIsAlt) : 'true';
    document.getElementById('rightOptionMapping').value = config.wine.rightOptionIsAlt !== undefined ? String(config.wine.rightOptionIsAlt) : 'true';
    document.getElementById('leftCommandMapping').value = config.wine.leftCommandIsCtrl !== undefined ? String(config.wine.leftCommandIsCtrl) : 'true';
    document.getElementById('rightCommandMapping').value = config.wine.rightCommandIsCtrl !== undefined ? String(config.wine.rightCommandIsCtrl) : 'true';
    

  }

  // Discord RPC bridge settings (macOS only) — no config fields to populate
  if (document.getElementById('installDiscordRpcButton')) {
    refreshDiscordRpcStatus();
  }
  
  // Dalamud settings
  if (config.dalamud) {
    document.getElementById('dalamudEnabled').checked = config.dalamud.enabled || false;
    document.getElementById('injectDelay').value = config.dalamud.injectDelay || 5000;
    document.getElementById('safeMode').checked = config.dalamud.safeMode || false;
    document.getElementById('pluginRepoUrl').value = config.dalamud.pluginRepoUrl || '';
    document.getElementById('entryPointMode').checked = config.dalamud.useEntryPoint ?? true;
    document.getElementById('useLatestPreRelease').checked = config.dalamud.useLatestPreRelease || false;
  }
}

/**
 * Collect form data
 */
function collectFormData() {
  const formData = {
    launcher: {
      developmentMode: document.getElementById('debugLogging').checked,
      enablePreRelease: document.getElementById('enablePreRelease').checked,
      language: document.getElementById('language').value,
      theme: document.querySelector('input[name="theme"]:checked')?.value || 'dark',
      // Preserve showDalamudTab from current config (set via Konami code)
      showDalamudTab: currentConfig?.launcher?.showDalamudTab || false,
      description: profileDescriptions[currentSelectedProfile] || ''
    },
    game: {
      gamePath: document.getElementById('gamePath').value
    },
    wine: {
      metalFxSpatialEnabled: document.getElementById('metalFxEnabled').checked,
      metalFxSpatialFactor: parseFloat(document.getElementById('metalFxFactor').value),
      metal3PerformanceOverlay: document.getElementById('hudEnabled').checked,
      hudScale: parseFloat(document.getElementById('hudScale').value),
      nativeResolution: document.getElementById('nativeResolution').checked,
      maxFramerate: parseInt(document.getElementById('maxFramerate').value),
      audioRouting: document.getElementById('audioRouting').checked,
      fsyncEnabled: document.getElementById('fsyncEnabled').checked,
      msync: document.getElementById('msyncEnabled').checked,
      useHomeAlias: document.getElementById('useHomeAlias').checked,
      wineDebug: document.getElementById('wineDebug').value,
      leftOptionIsAlt: document.getElementById('leftOptionMapping').value === 'true',
      rightOptionIsAlt: document.getElementById('rightOptionMapping').value === 'true',
      leftCommandIsCtrl: document.getElementById('leftCommandMapping').value === 'true',
      rightCommandIsCtrl: document.getElementById('rightCommandMapping').value === 'true'
    },
    protonGe: {
      dxvkHudEnabled: document.getElementById('protongeDxvkHudEnabled')?.checked || false,
      dxvkAsyncEnabled: document.getElementById('protongeDxvkAsyncEnabled')?.checked || false,
      maxFramerate: parseInt(document.getElementById('protongeMaxFramerate')?.value || 60),
      gameModeEnabled: false, // GameMode disabled by default due to compatibility issues
      esyncEnabled: document.getElementById('protongeEsyncEnabled')?.checked !== false,
      fsyncEnabled: document.getElementById('protongeFsyncEnabled')?.checked !== false,
      wineAlsaSpacialEnabled: document.getElementById('protongeWineAlsaSpacialEnabled')?.checked || false,
      wineAlsaChannels: document.getElementById('protongeWineAlsaChannels')?.value ? parseInt(document.getElementById('protongeWineAlsaChannels').value) : null,
      useProtonOptiscaler: document.getElementById('protongeUseProtonOptiscaler')?.checked || false,
      useProtonDiscordBridge: document.getElementById('protongeUseProtonDiscordBridge')?.checked || false,
      wineDebug: document.getElementById('protongeWineDebug')?.value || '',
      extraEnvironmentVariables: parseExtraEnvVars(document.getElementById('protongeExtraEnvVars')?.value || ''),
      launchOptions: document.getElementById('protongeLaunchOptions')?.value || '%command%'
    },
    discordRpc: {},
    dalamud: {
      enabled: document.getElementById('dalamudEnabled').checked,
      injectDelay: parseInt(document.getElementById('injectDelay').value),
      safeMode: document.getElementById('safeMode').checked,
      pluginRepoUrl: document.getElementById('pluginRepoUrl').value,
      useEntryPoint: document.getElementById('entryPointMode').checked,
      useLatestPreRelease: document.getElementById('useLatestPreRelease').checked
    }
  };
  console.log('[Settings] collectFormData - audioRouting:', formData.wine.audioRouting, 'wineDebug:', formData.wine.wineDebug);
  return formData;
}

/**
 * Persist configuration. If closeWindow is true, closes the window on success;
 * otherwise shows a success notification.
 */
async function persistConfig(closeWindow) {
  try {
    showLoadingOverlay(i18n.t('settings.applying'));
    
    const selectedProfile = currentSelectedProfile;
    const formData = collectFormData();
    
    const oldGamePath = currentConfig?.game?.gamePath || '';
    const newGamePath = formData.game?.gamePath || '';
    const oldLocale = currentConfig?.launcher?.language || 'zh-TW';
    const newLocale = formData.launcher.language;
    if (oldLocale !== newLocale) {
      i18n.setLocale(newLocale);
    }
    
    applyTheme(formData.launcher.theme || 'dark');
    
    // Calculate differences before mutating currentConfig
    // Only treat gamePathChanged as true when both paths are non-empty AND actually differ
    // (avoids false positives from form initialization on first load)
    const gamePathChanged = !!(oldGamePath && newGamePath && oldGamePath !== newGamePath);
    const oldDalamudEnabled = currentConfig?.dalamud?.enabled || false;
    const newDalamudEnabled = formData.dalamud?.enabled || false;
    const dalamudEnabledChanged = oldDalamudEnabled !== newDalamudEnabled;
    
    console.log('[Settings] Saving configuration for profile:', selectedProfile);
    
    // Save current profile configuration directly (no profile switch needed)
    const response = await window.xivtc.backend.call(`/api/config?profile=${selectedProfile}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: formData
    });
    
    if (!response.ok) {
      throw new Error(response.data?.message || response.statusText || 'Save failed');
    }
    
    console.log('[Settings] Config saved successfully for profile:', selectedProfile);
    currentConfig = formData;
    
    // Apply Wine settings to registry (macOS only)
    if (currentPlatform === 'darwin') {
      console.log('[Settings] Applying Wine settings to registry...');
      
      const applyResponse = await window.xivtc.backend.call('/api/wine/apply-settings', {
        method: 'POST'
      });
      
      if (!applyResponse.ok) {
        console.error('[Settings] Failed to apply Wine settings:', applyResponse.data?.message);
        hideLoadingOverlay();
        showError(i18n.t('settings.apply_wine_failed'));
        return;
      }
      
      console.log('[Settings] Wine settings applied successfully');
      await refreshDiscordRpcStatus();
    }
    
    // Notify login page of config changes (language/theme/dalamud/gamepath)
    await window.xivtc.events.send('config-changed', {
      gamePathChanged,
      dalamudEnabledChanged: dalamudEnabledChanged ? newDalamudEnabled : undefined,
      newGamePath
    });
    
    hideLoadingOverlay();
    if (closeWindow) {
      window.close();
    } else {
      showNotification(i18n.t('settings.applied'));
    }
  } catch (error) {
    console.error('[Settings] Failed to save configuration:', error);
    hideLoadingOverlay();
    showError(i18n.t('settings.save_failed'));
  }
}

/**
 * Save configuration
 */
async function saveConfig() {
  console.log('[Settings] saveConfig() called');
  return persistConfig(true);
}

/**
 * Apply configuration without closing the window
 */
async function applyConfig() {
  console.log('[Settings] applyConfig() called');
  return persistConfig(false);
}

/**
 * Initialize General Tab
 */
function initGeneralTab() {
  // Language change
  document.getElementById('language').addEventListener('change', (e) => {
    i18n.setLocale(e.target.value);
    i18n.updateElements();
  });

  document.getElementById('debugLogging').addEventListener('change', updateDebugOnlyVisibility);
  
  // Open LOG path
  document.getElementById('openLogPathButton').addEventListener('click', async () => {
    try {
      const result = await window.xivtc.openLogFolder();
      if (!result.success) {
        console.error('[Settings] Failed to open log folder:', result.error);
      }
    } catch (error) {
      console.error('[Settings] Failed to open log folder:', error);
    }
  });
  
  // Browse game path
  document.getElementById('browseGamePathButton').addEventListener('click', async () => {
    try {
      const result = await window.xivtc.selectDirectory();
      if (result && result.success && result.path) {
        // 驗證遊戲目錄是否有效（需包含 boot 和 game 子目錄）
        const validation = await window.xivtc.validateGameDirectory(result.path);
        console.log('[Settings] Game directory validation:', validation);
        
        if (!validation.valid) {
          // 翻譯驗證原因
          let translatedReason = validation.reason;
          if (validation.reason === 'Directory does not exist') {
            translatedReason = i18n.t('login.game_setup.validation.not_exist');
          } else if (validation.reason === 'Missing required subdirectories (game, boot)') {
            translatedReason = i18n.t('login.game_setup.validation.missing_subdirs');
          }
          
          alert(i18n.t('login.game_setup.error_invalid', { reason: translatedReason }));
          return;
        }
        
        document.getElementById('gamePath').value = result.path;
      }
    } catch (error) {
      console.error('[Settings] Failed to browse game path:', error);
    }
  });
  
  // Clear OTP key
  document.getElementById('clearOtpButton').addEventListener('click', async () => {
    try {
      
      // Get current/last used account
      const email = await getLastUsedAccount();
      
      if (!email) {
        alert(i18n.t('settings.general.clear_otp_no_account'));
        return;
      }
      
      // Confirm deletion
      const confirmMessage = i18n.t('settings.general.clear_otp_confirm', { email });
      if (!confirm(confirmMessage)) {
        return;
      }
      
      // Delete OTP secret
      const success = await deleteOTPSecret(email);
      
      if (success) {
        alert(i18n.t('settings.general.clear_otp_success', { email }));
        // 通知登入頁刷新帳號狀態
        window.xivtc.events.send('config-changed', {});
      } else {
        alert(i18n.t('settings.general.clear_otp_not_found', { email }));
      }
    } catch (error) {
      console.error('[Settings] Failed to clear OTP secret:', error);
      alert(i18n.t('settings.general.clear_otp_error'));
    }
  });

  // Initialize profile section
  initProfileSection();

  // Apply game path readonly state based on current profile
  updateGamePathReadonly();
}

/**
 * Lock/unlock game path input based on whether the active profile is 'default'.
 * Only the default profile may change the game installation path.
 */
function updateGamePathReadonly() {
  const isDefault = currentSelectedProfile === 'default';
  const input = document.getElementById('gamePath');
  const browseBtn = document.getElementById('browseGamePathButton');
  if (!input || !browseBtn) return;

  if (isDefault) {
    input.removeAttribute('readonly');
    input.removeAttribute('disabled');
    input.style.opacity = '';
    browseBtn.disabled = false;
    browseBtn.style.opacity = '';
    browseBtn.title = '';
  } else {
    input.setAttribute('readonly', 'true');
    input.style.opacity = '0.5';
    browseBtn.disabled = true;
    browseBtn.style.opacity = '0.4';
    const hint = i18n.getLocale() === 'en-US'
      ? 'Game path can only be changed in the default profile'
      : '遊戲路徑只能在預設設定檔中修改';
    browseBtn.title = hint;
    input.title = hint;
  }
}

/**
 * Initialize Wine Tab
 */
function initWineTab() {
  // MetalFX Factor slider
  const metalFxFactorSlider = document.getElementById('metalFxFactor');
  metalFxFactorSlider.addEventListener('input', (e) => {
    updateMetalFxFactorValue(parseFloat(e.target.value));
  });
  
  // HUD scale slider
  const hudScaleSlider = document.getElementById('hudScale');
  hudScaleSlider.addEventListener('input', (e) => {
    updateHudScaleValue(parseFloat(e.target.value));
  });
  
  // MetalFX toggle affects factor slider
  document.getElementById('metalFxEnabled').addEventListener('change', (e) => {
    document.getElementById('metalFxFactor').disabled = !e.target.checked;
  });
  
  // Wine tools
  document.getElementById('openWineCfgButton').addEventListener('click', () => openWineTool('winecfg'));
  document.getElementById('openRegeditButton').addEventListener('click', () => openWineTool('regedit'));
  document.getElementById('openCmdButton').addEventListener('click', () => openWineTool('wineconsole'));

  const installDiscordRpcButton = document.getElementById('installDiscordRpcButton');
  const removeDiscordRpcButton = document.getElementById('removeDiscordRpcButton');
  if (installDiscordRpcButton) {
    installDiscordRpcButton.addEventListener('click', installDiscordRpcBridge);
    removeDiscordRpcButton?.addEventListener('click', removeDiscordRpcBridge);
    refreshDiscordRpcStatus();
  }
}

/**
 * Update MetalFX Factor value display
 */
function updateMetalFxFactorValue(value) {
  document.getElementById('metalFxFactorValue').textContent = `${value.toFixed(1)}x`;
}

/**
 * Update HUD scale value display
 */
function updateHudScaleValue(value) {
  document.getElementById('hudScaleValue').textContent = `${value.toFixed(1)}x`;
}

/**
 * Show loading overlay with custom text
 */
function showLoadingOverlay(text) {
  const overlay = document.getElementById('loadingOverlay');
  const loadingText = overlay.querySelector('.loading-text');
  if (loadingText && text) {
    loadingText.textContent = text;
  }
  overlay.style.display = 'flex';
  console.log('[Settings] Loading overlay shown:', text);
}

/**
 * Hide loading overlay
 */
function hideLoadingOverlay() {
  const overlay = document.getElementById('loadingOverlay');
  overlay.style.display = 'none';
  console.log('[Settings] Loading overlay hidden');
}

/**
 * Open Wine tool
 */
async function openWineTool(tool) {
  try {
    showLoadingOverlay(i18n.t('settings.wine.launching_tool'));
    console.log(`[Settings] Launching Wine tool: ${tool}`);

    await window.xivtc.backend.call(`/api/wine/launch/${tool}`, {
      method: 'POST'
    });

    // 等待5秒让Wine工具窗口显示
    setTimeout(() => {
      hideLoadingOverlay();
    }, 5000);

  } catch (error) {
    console.error(`[Settings] Failed to open ${tool}:`, error);
    hideLoadingOverlay();
    showError(i18n.t('settings.wine.tool_failed', { tool }));
  }
}

function renderDiscordRpcStatus(message, hasError = false) {
  const el = document.getElementById('discordRpcStatusText');
  if (!el) return;

  el.textContent = message;
  el.style.color = hasError ? '#ef4444' : '';
}

function updateDiscordRpcButtonStates(installed) {
  const installButton = document.getElementById('installDiscordRpcButton');
  const removeButton = document.getElementById('removeDiscordRpcButton');
  if (installButton) installButton.disabled = installed;
  if (removeButton) removeButton.disabled = !installed;
}

async function refreshDiscordRpcStatus() {
  if (currentPlatform !== 'darwin') return;

  try {
    const response = await window.xivtc.backend.call('/api/discord-rpc/status', { method: 'GET' });
    if (!response.ok) {
      updateDiscordRpcButtonStates(false);
      renderDiscordRpcStatus(i18n.t('settings.discord_rpc.status_error'), true);
      return;
    }

    const payload = response.data?.success ? response.data.data : response.data;
    const status = payload?.status;
    if (!status) {
      updateDiscordRpcButtonStates(false);
      renderDiscordRpcStatus(i18n.t('settings.discord_rpc.status_error'), true);
      return;
    }

    updateDiscordRpcButtonStates(status.prefixBridgeInstalled);

    if (status.prefixBridgeInstalled) {
      renderDiscordRpcStatus(i18n.t('settings.discord_rpc.status_installed'));
    } else {
      renderDiscordRpcStatus(i18n.t('settings.discord_rpc.status_needs_install'), true);
    }
  } catch (error) {
    console.error('[Settings] Failed to refresh Discord RPC status:', error);
    updateDiscordRpcButtonStates(false);
    renderDiscordRpcStatus(i18n.t('settings.discord_rpc.status_error'), true);
  }
}

async function installDiscordRpcBridge() {
  try {
    showLoadingOverlay(i18n.t('settings.discord_rpc.installing'));
    const response = await window.xivtc.backend.call('/api/discord-rpc/install', { method: 'POST' });
    if (!response.ok) {
      throw new Error(response.data?.message || response.statusText || 'Install failed');
    }

    await refreshDiscordRpcStatus();
    hideLoadingOverlay();
    showSuccess(i18n.t('settings.discord_rpc.install_success'));
  } catch (error) {
    console.error('[Settings] Failed to install Discord RPC bridge:', error);
    hideLoadingOverlay();
    showError(i18n.t('settings.discord_rpc.install_failed'));
  }
}

async function removeDiscordRpcBridge() {
  try {
    showLoadingOverlay(i18n.t('settings.discord_rpc.removing'));
    const response = await window.xivtc.backend.call('/api/discord-rpc/remove', { method: 'POST' });
    if (!response.ok) {
      throw new Error(response.data?.message || response.statusText || 'Remove failed');
    }

    await refreshDiscordRpcStatus();
    hideLoadingOverlay();
    showSuccess(i18n.t('settings.discord_rpc.remove_success'));
  } catch (error) {
    console.error('[Settings] Failed to remove Discord RPC bridge:', error);
    hideLoadingOverlay();
    showError(i18n.t('settings.discord_rpc.remove_failed'));
  }
}

/**
 * Parse "KEY=VALUE" multiline text into { key: value } dict.
 * Lines without '=' are ignored.
 */
function parseExtraEnvVars(text) {
  const result = {};
  for (const line of text.split('\n')) {
    const idx = line.indexOf('=');
    if (idx <= 0) continue;
    const key = line.slice(0, idx).trim();
    const value = line.slice(idx + 1).trim();
    if (key) result[key] = value;
  }
  return result;
}

/**
 * Format { key: value } dict into "KEY=VALUE" multiline text.
 */
function formatExtraEnvVars(dict) {
  return Object.entries(dict).map(([k, v]) => `${k}=${v}`).join('\n');
}

/**
 * Initialize Wine-XIV Tab (Linux)
 */
function initProtonGeTab() {
  if (!currentConfig?.protonGe) {
    console.warn('[Settings] ProtonGe config not found');
    return;
  }
  
  const config = currentConfig.protonGe;
  console.log('[Settings] Loading ProtonGe settings:', config);
  
  // Graphics
  document.getElementById('protongeDxvkHudEnabled').checked = config.dxvkHudEnabled || false;
  document.getElementById('protongeDxvkAsyncEnabled').checked = config.dxvkAsyncEnabled || false;
  document.getElementById('protongeMaxFramerate').value = config.maxFramerate || 60;
  
  // Performance - GameMode is now disabled by default and hidden from UI
  // document.getElementById('protongeGameModeEnabled').checked = config.gameModeEnabled !== false;
  
  // Advanced
  document.getElementById('protongeEsyncEnabled').checked = config.esyncEnabled !== false; // default true
  document.getElementById('protongeFsyncEnabled').checked = config.fsyncEnabled !== false; // default true
  document.getElementById('protongeWineAlsaSpacialEnabled').checked = config.wineAlsaSpacialEnabled || false;
  document.getElementById('protongeWineAlsaChannels').value = config.wineAlsaChannels !== undefined && config.wineAlsaChannels !== null ? config.wineAlsaChannels : '';
  document.getElementById('protongeUseProtonOptiscaler').checked = config.useProtonOptiscaler || false;
  document.getElementById('protongeUseProtonDiscordBridge').checked = config.useProtonDiscordBridge || false;
  document.getElementById('protongeWineDebug').value = config.wineDebug || '';
  document.getElementById('protongeExtraEnvVars').value = formatExtraEnvVars(config.extraEnvironmentVariables || {});
  document.getElementById('protongeLaunchOptions').value = config.launchOptions || '%command%';
}


/**
 * Initialize Dalamud Tab
 */
function initDalamudTab() {
  // Load Dalamud version
  loadDalamudVersion();
  
  // Setup test launch button
  const testLaunchButton = document.getElementById('testLaunchButton');
  if (testLaunchButton) {
    testLaunchButton.addEventListener('click', handleTestLaunch);
  }
}

/**
 * Handle test launch button click
 */
async function handleTestLaunch() {
  const button = document.getElementById('testLaunchButton');
  const originalText = button.textContent;
  const overlay = document.getElementById('gameRunningOverlay');
  const exitCodeDialog = document.getElementById('exitCodeDialog');
  const exitCodeMessage = document.getElementById('exitCodeMessage');
  
  try {
    console.log('[Settings] Test launch requested');
    button.disabled = true;
    button.textContent = i18n.t('settings.general.test_launching') || '啟動中...';
    
    // Show game running overlay
    overlay.style.display = 'flex';
    
    const response = await window.xivtc.backend.call('/api/game/fake-launch', {
      method: 'POST'
    });
    
    // Hide overlay
    overlay.style.display = 'none';
    
    if (response.ok && response.data) {
      // Handle new API response format
      const result = response.data.success ? response.data.data : response.data;
      const exitCode = result.exitCode;
      console.log('[Settings] Game exited with code:', exitCode);
      
      // Check if exit code is abnormal (not 0 or 1)
      if (exitCode !== 0 && exitCode !== 1) {
        // Show exit code dialog
        exitCodeMessage.textContent = i18n.t('settings.general.abnormal_exit', { code: exitCode }) 
          || `遊戲異常結束，Exit Code: ${exitCode}`;
        exitCodeDialog.style.display = 'flex';
      } else {
        // Normal exit
        button.textContent = i18n.t('settings.general.test_success') || '測試完成';
        setTimeout(() => {
          button.disabled = false;
          button.textContent = originalText;
        }, 2000);
      }
      
      button.disabled = false;
      button.textContent = originalText;
    } else {
      const errorMsg = response.data?.error?.message || response.data?.error || 'Unknown error';
      console.error('[Settings] Test launch failed:', errorMsg);
      alert(i18n.t('settings.general.test_failed') + ': ' + errorMsg);
      button.disabled = false;
      button.textContent = originalText;
    }
  } catch (error) {
    console.error('[Settings] Test launch exception:', error);
    overlay.style.display = 'none';
    alert(i18n.t('settings.general.test_failed') + ': ' + error.message);
    button.disabled = false;
    button.textContent = originalText;
  }
}

/**
 * Load Dalamud version
 */
async function loadDalamudVersion() {
  try {
    const response = await window.xivtc.backend.call('/api/dalamud/status');
    if (response.ok && response.data) {
      const result = response.data.success ? response.data.data : response.data;
      document.getElementById('dalamudVersion').textContent = result.localVersion || 'Unknown';
    }
  } catch (error) {
    console.error('[Settings] Failed to load Dalamud version:', error);
    document.getElementById('dalamudVersion').textContent = 'Failed to load';
  }
}

/**
 * Initialize About Tab
 */
/**
 * Initialize About Tab
 */
function initAboutTab() {
  // Load version
  loadVersion();
  
  // GitHub link
  document.getElementById('githubLink').addEventListener('click', async () => {
    try {
      await window.xivtc.openExternal('https://github.com/PlusoneChiang/XIVTheCalamity');
    } catch (error) {
      console.error('[Settings] Failed to open GitHub:', error);
    }
  });
  
  document.getElementById('showLicenseButton').addEventListener('click', showLicense);
}

/**
 * Load version from config
 */
async function loadVersion() {
  try {
    const versionData = await window.xivtc.getVersion();
    document.getElementById('appVersion').textContent = versionData.version;
  } catch (error) {
    console.error('[Settings] Failed to load version:', error);
    document.getElementById('appVersion').textContent = '0.1.0';
  }
}

/**
 * Show license dialog
 */
function showLicense() {
  alert('GPL v3.0 License\n\nSee LICENSE file for details.');
}

/**
 * Setup event listeners
 */
function setupEventListeners() {
  document.getElementById('saveButton').addEventListener('click', saveConfig);
  document.getElementById('applyButton').addEventListener('click', applyConfig);
  document.getElementById('cancelButton').addEventListener('click', async () => {
    await cleanupUnsavedProfiles();
    window.close();
  });
  
  // Exit code dialog OK button
  document.getElementById('exitCodeOkButton').addEventListener('click', () => {
    document.getElementById('exitCodeDialog').style.display = 'none';
  });
  
  // Konami Code Dialog buttons
  document.getElementById('konamiYesBtn').addEventListener('click', () => setDalamudTabEnabled(true));
  document.getElementById('konamiNoBtn').addEventListener('click', () => setDalamudTabEnabled(false));
  
  // Close Konami Code dialog when clicking overlay background
  document.getElementById('konamiCodeOverlay').addEventListener('click', async (e) => {
    if (e.target.id === 'konamiCodeOverlay') {
      hideKonamiCodeDialog();
    }
  });
}

let toastTimeout = null;

/**
 * Show a beautiful, lightweight in-page toast notification with a small icon next to the text,
 * avoiding obtrusive native dialogs with titlebars/program names.
 */
function showToast(message, type = 'info') {
  // Find or create toast container
  let toast = document.getElementById('customToast');
  if (!toast) {
    toast = document.createElement('div');
    toast.id = 'customToast';
    toast.className = 'toast-container';
    
    const icon = document.createElement('div');
    icon.id = 'customToastIcon';
    icon.className = 'toast-icon';
    
    const msg = document.createElement('span');
    msg.id = 'customToastMsg';
    msg.className = 'toast-message';
    
    toast.appendChild(icon);
    toast.appendChild(msg);
    document.body.appendChild(toast);
  }
  
  const iconEl = document.getElementById('customToastIcon');
  const msgEl = document.getElementById('customToastMsg');
  
  // Set message
  msgEl.textContent = message;
  
  // Set icon and style based on type
  if (type === 'error') {
    iconEl.className = 'toast-icon toast-icon-error';
    iconEl.textContent = '✕'; // Small cross
  } else {
    iconEl.className = 'toast-icon toast-icon-info';
    iconEl.textContent = '✓'; // Small checkmark
  }
  
  // Show toast with smooth CSS transition
  toast.classList.add('show');
  
  // Clear any existing active timeout
  if (toastTimeout) {
    clearTimeout(toastTimeout);
  }
  
  // Auto hide after 2.5 seconds
  toastTimeout = setTimeout(() => {
    toast.classList.remove('show');
  }, 2500);
}

/**
 * Show notification
 */
function showNotification(message) {
  console.log('[Settings] Notification:', message);
  showToast(message, 'info');
}

/**
 * Show error
 */
function showError(message) {
  console.error('[Settings] Error:', message);
  showToast(message, 'error');
}

/**
 * Show success
 */
function showSuccess(message) {
  console.log('[Settings] Success:', message);
  showToast(message, 'info');
}

/**
 * Handle Konami Code input
 */
function handleKonamiCodeInput(event) {
  if (konamiCode.isMatching(event.keyCode)) {
    showKonamiCodeDialog();
    konamiCode.reset();
  }
}

/**
 * Show Konami Code dialog
 */
function showKonamiCodeDialog() {
  const overlay = document.getElementById('konamiCodeOverlay');
  if (overlay) {
    overlay.style.display = 'flex';
    overlay.classList.remove('hidden');
    console.log('[Settings] Konami Code dialog shown');
  }
}

/**
 * Hide Konami Code dialog
 */
function hideKonamiCodeDialog() {
  const overlay = document.getElementById('konamiCodeOverlay');
  if (overlay) {
    overlay.style.display = 'none';
    overlay.classList.add('hidden');
    console.log('[Settings] Konami Code dialog hidden');
  }
}

/**
 * Set Dalamud tab enabled state via Konami code and save immediately
 */
async function setDalamudTabEnabled(enabled) {
  try {
    const action = enabled ? 'enable' : 'disable';
    console.log(`[Settings] Setting Dalamud tab ${action} via Konami code`);
    
    // 1. Call API to update configuration - immediately save the setting
    const response = await window.xivtc.backend.call('/api/config', {
      method: 'PATCH',
      body: JSON.stringify({
        launcher: {
          showDalamudTab: enabled
        }
      })
    });
    
    if (response.ok && response.data) {
      // 2. Hide dialog
      hideKonamiCodeDialog();
      
      // 3. Reload configuration to ensure UI consistency
      await loadConfig();
      updateDalamudTabVisibility();
      
      // 4. Show feedback message
      const message = enabled 
        ? (i18n.t('settings.konami.success') || '✓ Dalamud 功能已啟用')
        : '✓ Dalamud 功能已關閉';
      showSuccess(message);
      console.log(`[Settings] Dalamud tab ${action} successfully`);
    } else {
      throw new Error(`Failed to ${action} Dalamud tab`);
    }
  } catch (error) {
    console.error(`[Settings] Failed to set Dalamud tab:`, error);
    hideKonamiCodeDialog();
    showError(i18n.t('settings.konami.error') || '✗ 操作失敗，請重試');
  }
}

/**
 * Show a custom confirmation modal. Returns a Promise that resolves true (confirm) or false (cancel).
 */
function showConfirmModal(title, message) {
  return new Promise((resolve) => {
    const modal = document.getElementById('confirmProfileApplyModal');
    const titleEl = document.getElementById('confirmProfileApplyTitle');
    const msgEl = document.getElementById('confirmProfileApplyMsg');
    const confirmBtn = document.getElementById('confirmProfileApplyConfirmBtn');
    const cancelBtn = document.getElementById('confirmProfileApplyCancelBtn');
    if (!modal || !titleEl || !msgEl || !confirmBtn || !cancelBtn) {
      // Fallback to native confirm if elements not found
      resolve(window.confirm(message));
      return;
    }
    titleEl.textContent = title;
    msgEl.textContent = message;
    modal.style.display = 'flex';

    function onConfirm() {
      modal.style.display = 'none';
      cleanup();
      resolve(true);
    }
    function onCancel() {
      modal.style.display = 'none';
      cleanup();
      resolve(false);
    }
    function cleanup() {
      confirmBtn.removeEventListener('click', onConfirm);
      cancelBtn.removeEventListener('click', onCancel);
    }
    confirmBtn.addEventListener('click', onConfirm);
    cancelBtn.addEventListener('click', onCancel);
  });
}

/**
 * Initialize Profile Section
 */
async function initProfileSection() {
  const container = document.getElementById('profilesListContainer');
  const addButton = document.getElementById('addProfileButton');
  
  if (!container || !addButton) return;

  // 1. Fetch current profiles or fallback to default
  let active = 'default';
  try {
    const response = await window.xivtc.backend.call('/api/config/profiles', { method: 'GET' });
    if (response.ok && response.data) {
      const result = response.data.success ? response.data.data : response.data;
      active = result.active || 'default';
      const profilesList = result.profiles || [];
      localProfiles = profilesList.map(p => p.name);
      currentSelectedProfile = active;
      startupActiveProfile = active;
      
      // Update game path editable state after active profile is retrieved
      updateGamePathReadonly();
      
      // Prepopulate profile descriptions map
      profileDescriptions = {};
      profilesList.forEach(p => {
        profileDescriptions[p.name] = p.description || '';
      });
    }
  } catch (err) {
    console.warn('[Settings] Failed to fetch profiles, using default fallback:', err);
  }

  // Setup Edit Description Modal elements
  const editDescModal = document.getElementById('editProfileDescModal');
  const editDescInput = document.getElementById('editProfileDescInput');
  const editDescCancelBtn = document.getElementById('editProfileDescCancelBtn');
  const editDescConfirmBtn = document.getElementById('editProfileDescConfirmBtn');
  let editingProfileDescName = '';

  if (editDescCancelBtn && editDescModal) {
    editDescCancelBtn.addEventListener('click', () => {
      editDescModal.style.display = 'none';
    });
  }

  if (editDescConfirmBtn && editDescModal && editDescInput) {
    editDescConfirmBtn.addEventListener('click', async () => {
      const val = editDescInput.value.trim();
      if (editingProfileDescName) {
        // Read, modify, and write description back to backend immediately
        try {
          const getResponse = await window.xivtc.backend.call(`/api/config?profile=${editingProfileDescName}`, { method: 'GET' });
          if (getResponse.ok && getResponse.data) {
            const configData = getResponse.data.success ? getResponse.data.data : getResponse.data;
            if (!configData.launcher) configData.launcher = {};
            configData.launcher.description = val;
            
            const putResponse = await window.xivtc.backend.call(`/api/config?profile=${editingProfileDescName}`, {
              method: 'PUT',
              headers: { 'Content-Type': 'application/json' },
              body: configData
            });
            
            if (putResponse.ok && editingProfileDescName === currentSelectedProfile) {
              if (!currentConfig.launcher) currentConfig.launcher = {};
              currentConfig.launcher.description = val;
            }
          }
        } catch (err) {
          console.error('[Settings] Failed to update description:', err);
        }
      }
      editDescModal.style.display = 'none';
      await initProfileSection();
    });
  }

  function renderProfilesList() {
    container.innerHTML = '';
    
    localProfiles.forEach(p => {
      const card = document.createElement('div');
      card.className = `profile-card${p === currentSelectedProfile ? ' active' : ''}`;
      
      const info = document.createElement('div');
      info.className = 'profile-info';
      
      const textContainer = document.createElement('div');
      textContainer.style.display = 'flex';
      textContainer.style.flexDirection = 'column';
      textContainer.style.gap = '2px';
      
      const nameWrapper = document.createElement('div');
      nameWrapper.style.display = 'flex';
      nameWrapper.style.alignItems = 'center';
      nameWrapper.style.gap = '8px';
      
      const name = document.createElement('span');
      name.className = 'profile-name';
      name.textContent = p === 'default' ? '預設 (default)' : p;
      nameWrapper.appendChild(name);
      
      if (p === startupActiveProfile) {
        const badge = document.createElement('span');
        badge.className = 'profile-badge badge-active';
        badge.setAttribute('data-i18n', 'settings.profile.active');
        badge.textContent = i18n.t('settings.profile.active') || '使用中';
        nameWrapper.appendChild(badge);
      }
      
      textContainer.appendChild(nameWrapper);
      
      // Description field under the title
      const desc = document.createElement('span');
      desc.className = 'profile-desc';
      desc.textContent = profileDescriptions[p] || '';
      textContainer.appendChild(desc);
      
      info.appendChild(textContainer);
      card.appendChild(info);
      
      const actions = document.createElement('div');
      actions.className = 'profile-actions';
      
      // Edit Description Button
      const editBtn = document.createElement('button');
      editBtn.type = 'button';
      editBtn.className = 'profile-btn btn-edit';
      editBtn.setAttribute('data-i18n', 'settings.profile.edit_btn');
      editBtn.textContent = i18n.t('settings.profile.edit_btn') || '編輯說明';
      editBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        editingProfileDescName = p;
        if (editDescModal && editDescInput) {
          editDescInput.value = profileDescriptions[p] || '';
          editDescModal.style.display = 'flex';
          editDescInput.focus();
        }
      });
      actions.appendChild(editBtn);

      // Show "Apply" button (disabled if it is the current active profile)
      const useBtn = document.createElement('button');
      useBtn.type = 'button';
      useBtn.className = 'profile-btn btn-use';
      useBtn.setAttribute('data-i18n', 'settings.profile.use');
      useBtn.textContent = i18n.t('settings.profile.use') || '應用';
      
      if (p === currentSelectedProfile) {
        useBtn.disabled = true;
        useBtn.style.opacity = '0.4';
        useBtn.style.cursor = 'not-allowed';
      } else {
        useBtn.addEventListener('click', async (e) => {
          e.stopPropagation();
          e.preventDefault();
          const targetProfile = p;
          
          const confirmTitle = i18n.getLocale() === 'en-US'
            ? `Apply Profile`
            : `應用設定檔`;
          const confirmMsg = i18n.getLocale() === 'en-US'
            ? `Are you sure you want to apply profile "${targetProfile}"?\nUnsaved changes will be discarded.`
            : `確定要應用設定檔「${targetProfile}」嗎？\n未儲存的變更將會捨棄。`;
          
          const confirmed = await showConfirmModal(confirmTitle, confirmMsg);
          if (!confirmed) {
            return;
          }
          
          console.log('[Settings] Applying profile (discard current unsaved changes):', targetProfile);
          
          try {
            showLoadingOverlay(i18n.t('settings.applying'));
            
            // Directly switch to target profile (discard unsaved changes)
            // 直接切換設定檔，放棄當前未儲存的變更
            const switchResponse = await window.xivtc.backend.call('/api/config/profiles/switch', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ name: targetProfile })
            });
            
            if (switchResponse.ok) {
              const oldGamePath = currentConfig?.game?.gamePath || '';
              const oldDalamudEnabled = currentConfig?.dalamud?.enabled || false;

              // 3. Load target profile settings into UI
              await loadConfig(targetProfile, true); // true to apply theme immediately
              
              const newGamePath = currentConfig?.game?.gamePath || '';
              const newDalamudEnabled = currentConfig?.dalamud?.enabled || false;
              const gamePathChanged = oldGamePath !== newGamePath;
              const dalamudEnabledChanged = oldDalamudEnabled !== newDalamudEnabled;

              console.log('[Settings] Notifying login page of profile switch. gamePathChanged:', gamePathChanged, 'dalamudEnabledChanged:', dalamudEnabledChanged);
              await window.xivtc.events.send('config-changed', {
                gamePathChanged,
                dalamudEnabledChanged: dalamudEnabledChanged ? newDalamudEnabled : undefined,
                newGamePath
              });

              // Refresh list to update active badges/actions (do NOT close settings window)
              await initProfileSection();
            }
          } catch (err) {
            console.error('[Settings] Failed to switch profile:', err);
            showError(i18n.t('settings.save_failed'));
          } finally {
            hideLoadingOverlay();
          }
        });
      }
      actions.appendChild(useBtn);
      
      // If not default profile, show "Delete" button
      if (p !== 'default') {
        const deleteBtn = document.createElement('button');
        deleteBtn.type = 'button';
        deleteBtn.className = 'profile-btn btn-delete';
        deleteBtn.setAttribute('data-i18n', 'settings.general.delete_profile');
        deleteBtn.textContent = i18n.t('settings.general.delete_profile') || '刪除';
        deleteBtn.addEventListener('click', async (e) => {
          e.stopPropagation();
          const delTitle = i18n.getLocale() === 'en-US'
            ? `Delete Profile`
            : `刪除設定檔`;
          const confirmMsg = i18n.getLocale() === 'en-US'
            ? `Are you sure you want to delete profile "${p}"?\nThis will erase its settings and plugins.`
            : `確定要刪除設定檔「${p}」嗎？\n這將會清除其專屬設定與插件。`;
          const confirmed = await showConfirmModal(delTitle, confirmMsg);
          if (confirmed) {
            try {
              showLoadingOverlay(i18n.t('settings.applying'));
              
              const response = await window.xivtc.backend.call(`/api/config/profiles/${p}`, { method: 'DELETE' });
              if (response.ok) {
                if (currentSelectedProfile === p) {
                  // Deleted the active profile → switch to default, notify, refresh list (do NOT close)
                  await window.xivtc.backend.call('/api/config/profiles/switch', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ name: 'default' })
                  });
                  await loadConfig('default', true);
                  await window.xivtc.events.send('config-changed', {
                    gamePathChanged: true,
                    newGamePath: currentConfig?.game?.gamePath || ''
                  });
                  await initProfileSection();
                } else {
                  // Deleted a non-active profile → just reload list
                  await initProfileSection();
                }
              }
            } catch (err) {
              console.error('[Settings] Failed to delete profile:', err);
            } finally {
              hideLoadingOverlay();
            }
          }
        });
        actions.appendChild(deleteBtn);
      }
      
      card.appendChild(actions);
      container.appendChild(card);
    });
  }

  // 2. Populate list initially
  renderProfilesList();

  // 4. Add profile listener -> show custom modal
  const modal = document.getElementById('addProfileModal');
  const cancelBtn = document.getElementById('addProfileCancelBtn');
  const confirmBtn = document.getElementById('addProfileConfirmBtn');
  const nameInput = document.getElementById('newProfileNameInput');
  const descInput = document.getElementById('newProfileDescInput');
  const copyCheckbox = document.getElementById('copyDefaultSettingsCheckbox');

  addButton.addEventListener('click', () => {
    if (modal && nameInput && descInput && copyCheckbox) {
      nameInput.value = '';
      descInput.value = '';
      copyCheckbox.checked = true;
      modal.style.display = 'flex';
      nameInput.focus();
    }
  });

  if (cancelBtn && modal) {
    cancelBtn.addEventListener('click', () => {
      modal.style.display = 'none';
    });
  }

  if (confirmBtn && modal && nameInput && descInput && copyCheckbox) {
    confirmBtn.addEventListener('click', async () => {
      const name = nameInput.value.trim();
      const description = descInput.value.trim();
      if (!name) {
        alert(i18n.getLocale() === 'en-US' ? "Please enter a profile name" : "請輸入設定檔名稱");
        return;
      }
      const sanitized = name.replace(/[^a-zA-Z0-9_-]/g, '');
      if (!sanitized || sanitized !== name) {
        alert(i18n.getLocale() === 'en-US' ? "Profile name must only contain alphanumeric characters, hyphens, or underscores" : "設定檔名稱僅限使用英數字、底線或減號");
        return;
      }
      modal.style.display = 'none';
      
      try {
        showLoadingOverlay(i18n.t('settings.applying'));
        
        // 1. Save current profile changes
        const currentForm = collectFormData();
        await window.xivtc.backend.call(`/api/config?profile=${currentSelectedProfile}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: currentForm
        });
        
        // 2. Create new profile immediately
        const addResponse = await window.xivtc.backend.call('/api/config/profiles', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ name: sanitized, copyDefault: copyCheckbox.checked })
        });
        
        if (addResponse.ok) {
          // 3. Write description if provided
          if (description) {
            const getResponse = await window.xivtc.backend.call(`/api/config?profile=${sanitized}`, { method: 'GET' });
            if (getResponse.ok && getResponse.data) {
              const configData = getResponse.data.success ? getResponse.data.data : getResponse.data;
              if (!configData.launcher) configData.launcher = {};
              configData.launcher.description = description;
              
              await window.xivtc.backend.call(`/api/config?profile=${sanitized}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: configData
              });
            }
          }
          
          // 4. Switch persistently to the new profile
          await window.xivtc.backend.call('/api/config/profiles/switch', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: sanitized })
          });
          
          const oldGamePath = currentConfig?.game?.gamePath || '';
          const oldDalamudEnabled = currentConfig?.dalamud?.enabled || false;

          // 5. Load the new configuration and update UI
          await loadConfig(sanitized, true);
          
          const newGamePath = currentConfig?.game?.gamePath || '';
          const newDalamudEnabled = currentConfig?.dalamud?.enabled || false;
          const gamePathChanged = oldGamePath !== newGamePath;
          const dalamudEnabledChanged = oldDalamudEnabled !== newDalamudEnabled;

          console.log('[Settings] Notifying login page of profile creation switch. gamePathChanged:', gamePathChanged, 'dalamudEnabledChanged:', dalamudEnabledChanged);
          await window.xivtc.events.send('config-changed', {
            gamePathChanged,
            dalamudEnabledChanged: dalamudEnabledChanged ? newDalamudEnabled : undefined,
            newGamePath
          });
          
          await initProfileSection();
        }
      } catch (err) {
        console.error('[Settings] Failed to add profile:', err);
      } finally {
        hideLoadingOverlay();
      }
    });
  }
}

/**
 * Cleanup unsaved newly-added profile directories on Cancel
 */
async function cleanupUnsavedProfiles() {
}

// Initialize on load
init();
