/**
 * Settings Page Logic
 */

import i18n from '../../i18n/index.js';

let currentConfig = null;
let currentPlatform = 'win32';

/**
 * Initialize settings page
 */
async function init() {
  console.log('[Settings] Initializing settings page');
  
  // Detect platform and add body class (synchronous)
  detectPlatform();
  
  // Initialize tab navigation
  initTabNavigation();
  
  // Load configuration
  await loadConfig();
  
  // Initialize each tab
  initGeneralTab();
  initWineTab();
  initProtonTab();
  initDalamudTab();
  initAboutTab();
  
  // Setup event listeners
  setupEventListeners();
  
  // Apply i18n
  i18n.updateElements();
  
  console.log('[Settings] Initialization complete');
}

/**
 * Detect platform and set body class
 */
function detectPlatform() {
  try {
    currentPlatform = window.electronAPI.getPlatform();
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
async function loadConfig() {
  try {
    const response = await window.electronAPI.backend.call('/api/config', {
      method: 'GET'
    });
    if (response.ok && response.data) {
      // Handle new API response format: { success: true, data: {...} }
      const configData = response.data.success ? response.data.data : response.data;
      currentConfig = configData;
      populateForm(currentConfig);
      console.log('[Settings] Configuration loaded:', currentConfig);
    }
  } catch (error) {
    console.error('[Settings] Failed to load configuration:', error);
    showError(i18n.t('settings.load_failed'));
  }
}

/**
 * Populate form with current configuration
 */
function populateForm(config) {
  if (!config) return;
  
  // General settings
  if (config.launcher) {
    document.getElementById('language').value = i18n.locale || 'zh-TW';
    document.getElementById('debugLogging').checked = config.launcher.developmentMode || false;
  }
  
  if (config.game) {
    document.getElementById('gamePath').value = config.game.gamePath || '';
  }
  
  // Wine settings
  if (config.wine) {
    console.log('[Settings] Populating Wine settings:', JSON.stringify(config.wine));
    document.getElementById('dxmtEnabled').checked = config.wine.dxmtEnabled || false;
    document.getElementById('metalFxEnabled').checked = config.wine.metalFxSpatialEnabled || false;
    document.getElementById('metalFxFactor').value = config.wine.metalFxSpatialFactor || 2.0;
    updateMetalFxFactorValue(config.wine.metalFxSpatialFactor || 2.0);
    document.getElementById('hudEnabled').checked = config.wine.metal3PerformanceOverlay || false;
    document.getElementById('hudScale').value = config.wine.hudScale || 1.0;
    updateHudScaleValue(config.wine.hudScale || 1.0);
    document.getElementById('nativeResolution').checked = config.wine.nativeResolution || false;
    document.getElementById('maxFramerate').value = config.wine.maxFramerate || 60;
    document.getElementById('audioRouting').checked = config.wine.audioRouting || false;
    console.log('[Settings] audioRouting value from config:', config.wine.audioRouting, '-> checkbox set to:', document.getElementById('audioRouting').checked);
    document.getElementById('esyncEnabled').checked = config.wine.esyncEnabled !== undefined ? config.wine.esyncEnabled : true;
    document.getElementById('fsyncEnabled').checked = config.wine.fsyncEnabled || false;
    document.getElementById('msyncEnabled').checked = config.wine.msync !== undefined ? config.wine.msync : true;
    document.getElementById('wineDebug').value = config.wine.wineDebug || '';
    console.log('[Settings] wineDebug value from config:', config.wine.wineDebug, '-> input set to:', document.getElementById('wineDebug').value);
    
    // Keyboard mapping (macOS only)
    document.getElementById('leftOptionMapping').value = config.wine.leftOptionIsAlt !== undefined ? String(config.wine.leftOptionIsAlt) : 'true';
    document.getElementById('rightOptionMapping').value = config.wine.rightOptionIsAlt !== undefined ? String(config.wine.rightOptionIsAlt) : 'true';
    document.getElementById('leftCommandMapping').value = config.wine.leftCommandIsCtrl !== undefined ? String(config.wine.leftCommandIsCtrl) : 'true';
    document.getElementById('rightCommandMapping').value = config.wine.rightCommandIsCtrl !== undefined ? String(config.wine.rightCommandIsCtrl) : 'true';
  }
  
  // Dalamud settings
  if (config.dalamud) {
    document.getElementById('dalamudEnabled').checked = config.dalamud.enabled || false;
    document.getElementById('injectDelay').value = config.dalamud.injectDelay || 5000;
    document.getElementById('safeMode').checked = config.dalamud.safeMode || false;
    document.getElementById('pluginRepoUrl').value = config.dalamud.pluginRepoUrl || '';
  }
}

/**
 * Collect form data
 */
function collectFormData() {
  const formData = {
    launcher: {
      developmentMode: document.getElementById('debugLogging').checked
    },
    game: {
      gamePath: document.getElementById('gamePath').value
    },
    wine: {
      dxmtEnabled: document.getElementById('dxmtEnabled').checked,
      metalFxSpatialEnabled: document.getElementById('metalFxEnabled').checked,
      metalFxSpatialFactor: parseFloat(document.getElementById('metalFxFactor').value),
      metal3PerformanceOverlay: document.getElementById('hudEnabled').checked,
      hudScale: parseFloat(document.getElementById('hudScale').value),
      nativeResolution: document.getElementById('nativeResolution').checked,
      maxFramerate: parseInt(document.getElementById('maxFramerate').value),
      audioRouting: document.getElementById('audioRouting').checked,
      esyncEnabled: document.getElementById('esyncEnabled').checked,
      fsyncEnabled: document.getElementById('fsyncEnabled').checked,
      msync: document.getElementById('msyncEnabled').checked,
      wineDebug: document.getElementById('wineDebug').value,
      leftOptionIsAlt: document.getElementById('leftOptionMapping').value === 'true',
      rightOptionIsAlt: document.getElementById('rightOptionMapping').value === 'true',
      leftCommandIsCtrl: document.getElementById('leftCommandMapping').value === 'true',
      rightCommandIsCtrl: document.getElementById('rightCommandMapping').value === 'true'
    },
    proton: {
      dxvkHudEnabled: document.getElementById('protonDxvkHudEnabled')?.checked || false,
      maxFramerate: parseInt(document.getElementById('protonMaxFramerate')?.value || 60),
      gameModeEnabled: document.getElementById('protonGameModeEnabled')?.checked !== false,
      esyncEnabled: document.getElementById('protonEsyncEnabled')?.checked !== false,
      fsyncEnabled: document.getElementById('protonFsyncEnabled')?.checked !== false,
      wineDebug: document.getElementById('protonWineDebug')?.value || ''
    },
    dalamud: {
      enabled: document.getElementById('dalamudEnabled').checked,
      injectDelay: parseInt(document.getElementById('injectDelay').value),
      safeMode: document.getElementById('safeMode').checked,
      pluginRepoUrl: document.getElementById('pluginRepoUrl').value
    }
  };
  console.log('[Settings] collectFormData - audioRouting:', formData.wine.audioRouting, 'wineDebug:', formData.wine.wineDebug);
  return formData;
}

/**
 * Save configuration
 */
async function saveConfig() {
  try {
    // Show loading overlay
    showLoadingOverlay(i18n.t('settings.applying'));
    
    const formData = collectFormData();
    
    // 記錄變更前的設定（用於比較）
    const oldGamePath = currentConfig?.game?.gamePath || '';
    const newGamePath = formData.game?.gamePath || '';
    
    // Save language separately
    const newLocale = document.getElementById('language').value;
    if (newLocale !== i18n.locale) {
      i18n.setLocale(newLocale);
    }
    
    console.log('[Settings] Saving configuration:', formData);
    
    // Step 1: Save configuration
    const response = await window.electronAPI.backend.call('/api/config', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: formData
    });
    
    console.log('[Settings] Save response:', response);
    
    if (!response.ok) {
      throw new Error(response.data?.message || response.statusText || 'Save failed');
    }
    
    console.log('[Settings] Configuration saved successfully');
    
    // Step 2: Apply Wine settings to registry (macOS only)
    if (currentPlatform === 'darwin') {
      console.log('[Settings] Applying Wine settings to registry...');
      
      const applyResponse = await window.electronAPI.backend.call('/api/wine/apply-settings', {
        method: 'POST'
      });
      
      console.log('[Settings] Apply Wine settings response:', applyResponse);
      
      if (!applyResponse.ok) {
        console.error('[Settings] Failed to apply Wine settings:', applyResponse.data?.message);
        hideLoadingOverlay();
        showError(i18n.t('settings.apply_wine_failed'));
        return;
      }
      
      console.log('[Settings] Wine settings applied successfully');
    }
    
    // Step 3: 通知登入頁設定已變更
    const gamePathChanged = oldGamePath !== newGamePath;
    
    // 檢查 Dalamud 啟用狀態變更
    const oldDalamudEnabled = currentConfig?.dalamud?.enabled || false;
    const newDalamudEnabled = formData.dalamud?.enabled || false;
    const dalamudEnabledChanged = oldDalamudEnabled !== newDalamudEnabled;
    
    if (gamePathChanged || dalamudEnabledChanged) {
      console.log('[Settings] Notifying login page of config change');
      window.electronAPI.events.send('config-changed', {
        gamePathChanged,
        dalamudEnabledChanged: dalamudEnabledChanged ? newDalamudEnabled : undefined,
        newGamePath
      });
    }
    
    // Hide overlay and close window
    hideLoadingOverlay();
    window.close();
  } catch (error) {
    console.error('[Settings] Failed to save configuration:', error);
    hideLoadingOverlay();
    showError(i18n.t('settings.save_failed'));
  }
}

/**
 * Apply configuration without closing the window
 */
async function applyConfig() {
  try {
    // Show loading overlay
    showLoadingOverlay(i18n.t('settings.applying'));
    
    const formData = collectFormData();
    
    // 記錄變更前的設定（用於比較）
    const oldGamePath = currentConfig?.game?.gamePath || '';
    const newGamePath = formData.game?.gamePath || '';
    
    // Save language separately
    const newLocale = document.getElementById('language').value;
    if (newLocale !== i18n.locale) {
      i18n.setLocale(newLocale);
    }
    
    console.log('[Settings] Applying configuration:', formData);
    
    // Step 1: Save configuration
    const response = await window.electronAPI.backend.call('/api/config', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: formData
    });
    
    console.log('[Settings] Save response:', response);
    
    if (!response.ok) {
      throw new Error(response.data?.message || response.statusText || 'Save failed');
    }
    
    console.log('[Settings] Configuration saved successfully');
    
    // Step 2: Apply Wine settings to registry (macOS only)
    if (currentPlatform === 'darwin') {
      console.log('[Settings] Applying Wine settings to registry...');
      
      const applyResponse = await window.electronAPI.backend.call('/api/wine/apply-settings', {
        method: 'POST'
      });
      
      console.log('[Settings] Apply Wine settings response:', applyResponse);
      
      if (!applyResponse.ok) {
        console.error('[Settings] Failed to apply Wine settings:', applyResponse.data?.message);
        hideLoadingOverlay();
        showError(i18n.t('settings.apply_wine_failed'));
        return;
      }
      
      console.log('[Settings] Wine settings applied successfully');
    }
    
    // Step 3: 通知登入頁設定已變更
    const gamePathChanged = oldGamePath !== newGamePath;
    
    // 檢查 Dalamud 啟用狀態變更
    const oldDalamudEnabled = currentConfig?.dalamud?.enabled || false;
    const newDalamudEnabled = formData.dalamud?.enabled || false;
    const dalamudEnabledChanged = oldDalamudEnabled !== newDalamudEnabled;
    
    if (gamePathChanged || dalamudEnabledChanged) {
      console.log('[Settings] Notifying login page of config change');
      window.electronAPI.events.send('config-changed', {
        gamePathChanged,
        dalamudEnabledChanged: dalamudEnabledChanged ? newDalamudEnabled : undefined,
        newGamePath
      });
    }
    
    // Update currentConfig for subsequent applies
    currentConfig = formData;
    
    // Hide overlay and show success notification
    hideLoadingOverlay();
    showNotification(i18n.t('settings.applied'));
  } catch (error) {
    console.error('[Settings] Failed to apply configuration:', error);
    hideLoadingOverlay();
    showError(i18n.t('settings.save_failed'));
  }
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
  
  // Open LOG path
  document.getElementById('openLogPathButton').addEventListener('click', async () => {
    try {
      const result = await window.electronAPI.openLogFolder();
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
      const result = await window.electronAPI.selectDirectory();
      if (result && result.success && result.path) {
        // 驗證遊戲目錄是否有效（需包含 boot 和 game 子目錄）
        const validation = await window.electronAPI.validateGameDirectory(result.path);
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
  
  // DXMT toggle affects MetalFX
  document.getElementById('dxmtEnabled').addEventListener('change', (e) => {
    const isEnabled = e.target.checked;
    document.getElementById('metalFxEnabled').disabled = !isEnabled;
    document.getElementById('metalFxFactor').disabled = !isEnabled;
  });
  
  // MetalFX toggle affects factor slider
  document.getElementById('metalFxEnabled').addEventListener('change', (e) => {
    document.getElementById('metalFxFactor').disabled = !e.target.checked;
  });
  
  // Wine tools
  document.getElementById('openWineCfgButton').addEventListener('click', () => openWineTool('winecfg'));
  document.getElementById('openRegeditButton').addEventListener('click', () => openWineTool('regedit'));
  document.getElementById('openCmdButton').addEventListener('click', () => openWineTool('wineconsole'));
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

    await window.electronAPI.backend.call(`/api/wine/open-${tool}`, {
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

/**
 * Initialize Proton Tab (Linux)
 */
function initProtonTab() {
  if (!currentConfig?.proton) {
    console.warn('[Settings] Proton config not found');
    return;
  }
  
  const config = currentConfig.proton;
  console.log('[Settings] Loading Proton settings:', config);
  
  // Graphics
  document.getElementById('protonDxvkHudEnabled').checked = config.dxvkHudEnabled || false;
  document.getElementById('protonMaxFramerate').value = config.maxFramerate || 60;
  
  // Performance
  document.getElementById('protonGameModeEnabled').checked = config.gameModeEnabled !== false; // default true
  
  // Advanced
  document.getElementById('protonEsyncEnabled').checked = config.esyncEnabled !== false; // default true
  document.getElementById('protonFsyncEnabled').checked = config.fsyncEnabled !== false; // default true
  document.getElementById('protonWineDebug').value = config.wineDebug || '';
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
    button.textContent = i18n.t('settings.dalamud.test_launching') || '啟動中...';
    
    // Show game running overlay
    overlay.style.display = 'flex';
    
    const response = await window.electronAPI.backend.call('/api/game/fake-launch', {
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
        exitCodeMessage.textContent = i18n.t('settings.dalamud.abnormal_exit', { code: exitCode }) 
          || `遊戲異常結束，Exit Code: ${exitCode}`;
        exitCodeDialog.style.display = 'flex';
      } else {
        // Normal exit
        button.textContent = i18n.t('settings.dalamud.test_success') || '測試完成';
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
      alert(i18n.t('settings.dalamud.test_failed') + ': ' + errorMsg);
      button.disabled = false;
      button.textContent = originalText;
    }
  } catch (error) {
    console.error('[Settings] Test launch exception:', error);
    overlay.style.display = 'none';
    alert(i18n.t('settings.dalamud.test_failed') + ': ' + error.message);
    button.disabled = false;
    button.textContent = originalText;
  }
}

/**
 * Load Dalamud version
 */
async function loadDalamudVersion() {
  try {
    const response = await window.electronAPI.backend.call('/api/dalamud/version');
    if (response.ok && response.data) {
      // Handle new API response format
      const result = response.data.success ? response.data.data : response.data;
      document.getElementById('dalamudVersion').textContent = result.version || 'Unknown';
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
      await window.electronAPI.openExternal('https://github.com/PlusoneChiang/XIVTheCalamity');
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
    const versionData = await window.electronAPI.getVersion();
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
  document.getElementById('cancelButton').addEventListener('click', () => window.close());
  
  // Exit code dialog OK button
  document.getElementById('exitCodeOkButton').addEventListener('click', () => {
    document.getElementById('exitCodeDialog').style.display = 'none';
  });
}

/**
 * Show notification
 */
function showNotification(message) {
  console.log('[Settings] Notification:', message);
  // TODO: Implement notification UI
  alert(message);
}

/**
 * Show error
 */
function showError(message) {
  console.error('[Settings] Error:', message);
  // TODO: Implement error UI
  alert(message);
}

// Initialize on load
init();
