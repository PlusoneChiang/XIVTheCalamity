const { app, BrowserWindow, ipcMain, Menu } = require('electron');
const path = require('path');
const fs = require('fs');
const https = require('https');
const http = require('http');
const log = require('electron-log');
const { spawn } = require('child_process');

// Set application name FIRST to ensure correct case in directory names
// Must be called before any app.getPath() calls
app.setName('XIVTheCalamity');

// Platform detection
const isMacOS = process.platform === 'darwin';
const isLinux = process.platform === 'linux';
const isWindows = process.platform === 'win32';

// Hide menu on Linux/Windows (macOS keeps native menu bar)
if (!isMacOS) {
  Menu.setApplicationMenu(null);
}

// Load version info
let versionInfo = { version: '0.1.0', appName: 'XIV The Calamity', description: 'Final Fantasy XIV Cross-Platform Launcher' };
try {
  const versionPath = path.join(__dirname, '../renderer/version.json');
  versionInfo = JSON.parse(fs.readFileSync(versionPath, 'utf8'));
} catch (error) {
  console.error('[Main] Failed to load version.json:', error);
}

// Configure electron-log
const logDir = path.join(app.getPath('appData'), 'XIVTheCalamity', 'logs');
if (!fs.existsSync(logDir)) {
  fs.mkdirSync(logDir, { recursive: true });
}

const logPath = path.join(logDir, `app-${new Date().toISOString().split('T')[0]}.log`);
log.transports.file.resolvePathFn = () => logPath;
log.transports.file.level = 'debug';
log.transports.console.level = 'debug';
log.transports.file.format = '[{y}-{m}-{d} {h}:{i}:{s}.{ms}] [{level}] {text}';

// Override console methods
const originalConsole = {
  log: console.log,
  info: console.info,
  warn: console.warn,
  error: console.error,
  debug: console.debug
};

console.log = (...args) => {
  log.log(...args);
  originalConsole.log(...args);
};
console.info = (...args) => {
  log.info(...args);
  originalConsole.info(...args);
};
console.warn = (...args) => {
  log.warn(...args);
  originalConsole.warn(...args);
};
console.error = (...args) => {
  log.error(...args);
  originalConsole.error(...args);
};
console.debug = (...args) => {
  log.debug(...args);
  originalConsole.debug(...args);
};

log.info('XIVTheCalamity starting...');
log.info('Log file:', logPath);

// Track settings window instance (only one allowed)
let settingsWindowInstance = null;

// Track if debug/development mode is enabled (read from config on startup)
let isDebugModeEnabled = false; // Will be loaded from config

/**
 * Safe logging functions that won't throw EPIPE
 */
function safeLog(...args) {
  try {
    log.info(...args);
  } catch (error) {
    // Ignore EPIPE errors
  }
}

function safeError(...args) {
  try {
    log.error(...args);
  } catch (error) {
    // Ignore EPIPE errors
  }
}

/**
 * Configure Electron paths before app is ready
 * Uses Electron's default appData paths:
 * - macOS: ~/Library/Application Support
 * - Linux: ~/.config
 * - Windows: %APPDATA%
 */
const os = require('os');

// Electron system files go to Caches
const electronCacheDir = path.join(app.getPath('cache'), 'XIVTheCalamity');
app.setPath('userData', electronCacheDir);

// User config files go to Application Support (or .config on Linux)
const userConfigDir = path.join(app.getPath('appData'), 'XIVTheCalamity');

// Ensure user config directory exists
if (!fs.existsSync(userConfigDir)) {
  fs.mkdirSync(userConfigDir, { recursive: true });
  safeLog('[Main] Created user config directory:', userConfigDir);
}

/**
 * Create main window with Virtual Host for reCAPTCHA
 * Uses loadURL + baseURLForDataURL for HTTPS origin
 */
function createWindow() {
  const mainWindow = new BrowserWindow({
    width: 910,
    height: 682,
    resizable: false,
    transparent: true,
    titleBarStyle: 'hiddenInset',
    backgroundColor: '#00000000',
    vibrancy: 'dark',
    title: 'XIV The Calamity',
    autoHideMenuBar: !isMacOS, // Hide menu bar on Linux/Windows
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      nodeIntegration: false,
      contextIsolation: true,
      cache: true  // Always enable cache
    }
  });

  // DISABLED: clearCache was deleting Application Support data
  // If you need to clear cache during development, do it manually:
  // rm -rf ~/Library/Caches/XIVTheCalamity
  
  // Load directly without clearing cache
  loadLoginPageWithVirtualHost(mainWindow);
  
  // Suppress Autofill errors (known Electron issue)
  mainWindow.webContents.on('console-message', (event, level, message) => {
    if (message.includes('Autofill')) {
      event.preventDefault();
    }
  });
  
  // DevTools control - Open automatically in development mode
  if (isDebugModeEnabled) {
    mainWindow.webContents.openDevTools();
    safeLog('[Main] DevTools opened (development mode)');
  }
  
  mainWindow.webContents.on('devtools-opened', () => {
    // Allow DevTools to be manually opened if needed
  });

  // Close settings window when main window closes
  mainWindow.on('close', () => {
    if (settingsWindowInstance && !settingsWindowInstance.isDestroyed()) {
      settingsWindowInstance.close();
    }
  });
}

/**
 * Create settings window (singleton - only one instance allowed)
 */
function createSettingsWindow() {
  // If settings window already exists, focus it instead of creating a new one
  if (settingsWindowInstance) {
    if (!settingsWindowInstance.isDestroyed()) {
      safeLog('[Settings] Window already exists, bringing to front');
      // Ensure window is visible and on top
      if (settingsWindowInstance.isMinimized()) {
        settingsWindowInstance.restore();
      }
      settingsWindowInstance.show();
      settingsWindowInstance.moveTop();
      settingsWindowInstance.focus();
      return settingsWindowInstance;
    } else {
      safeLog('[Settings] Window was destroyed, creating new one');
      settingsWindowInstance = null;
    }
  }
  
  safeLog('[Settings] Creating new settings window');
  settingsWindowInstance = new BrowserWindow({
    width: 800,
    height: 600,
    resizable: false,
    transparent: true,
    titleBarStyle: 'hiddenInset',
    backgroundColor: '#00000000',
    vibrancy: 'dark',
    title: 'Settings - XIV The Calamity',
    autoHideMenuBar: !isMacOS, // Hide menu bar on Linux/Windows
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      nodeIntegration: false,
      contextIsolation: true
    }
  });
  
  // Load settings page
  const settingsPath = path.join(__dirname, '../renderer/pages/settings/settings.html');
  settingsWindowInstance.loadFile(settingsPath);

  // DevTools control - Allow manual opening if needed
  settingsWindowInstance.webContents.on('devtools-opened', () => {
    // Allow DevTools to be manually opened if needed
  });

  // Clean up reference when window is closed
  settingsWindowInstance.on('closed', () => {
    settingsWindowInstance = null;
  });

  return settingsWindowInstance;
}

/**
 * Remove ES module syntax from JavaScript code
 */
function stripModuleSyntax(code) {
  return code
    .replace(/import.*from.*;/g, '')
    .replace(/export default /g, '')
    .replace(/export async function/g, 'async function')
    .replace(/export function/g, 'function')
    .replace(/export const /g, 'const ')
    .replace(/export \{[^}]+\}/g, '');
}

/**
 * Load and read all required JavaScript files
 */
function loadJavaScriptFiles() {
  const basePath = path.join(__dirname, '../renderer');
  const files = {
    i18n: path.join(basePath, 'i18n/index.js'),
    encoding: path.join(basePath, 'utils/encoding.js'),
    crypto: path.join(basePath, 'utils/crypto.js'),
    totp: path.join(basePath, 'utils/totp.js'),
    keyManager: path.join(basePath, 'utils/keyManager.js'),
    accountStorage: path.join(basePath, 'utils/accountStorage.js'),
    apiError: path.join(basePath, 'utils/apiError.js'),
    accountManagement: path.join(basePath, 'pages/login/accountManagement.js'),
    updateManager: path.join(basePath, 'pages/login/updateManager.js'),
    dalamudManager: path.join(basePath, 'pages/login/dalamudManager.js'),
    login: path.join(basePath, 'pages/login/login.js')
  };

  return Object.entries(files).reduce((acc, [key, filePath]) => {
    acc[key] = fs.readFileSync(filePath, 'utf8');
    return acc;
  }, {});
}

/**
 * Combine JavaScript modules in dependency order
 */
function combineJavaScriptModules(jsFiles) {
  return [
    '// i18n module',
    stripModuleSyntax(jsFiles.i18n),
    '// Encoding utilities',
    stripModuleSyntax(jsFiles.encoding),
    '// Crypto utilities',
    stripModuleSyntax(jsFiles.crypto),
    '// TOTP utilities',
    stripModuleSyntax(jsFiles.totp),
    '// Key manager',
    stripModuleSyntax(jsFiles.keyManager),
    '// Account storage',
    stripModuleSyntax(jsFiles.accountStorage),
    '// API error handling',
    stripModuleSyntax(jsFiles.apiError),
    '// Account management UI',
    stripModuleSyntax(jsFiles.accountManagement),
    '// Update manager',
    stripModuleSyntax(jsFiles.updateManager),
    '// Dalamud manager',
    stripModuleSyntax(jsFiles.dalamudManager),
    '// Login logic',
    stripModuleSyntax(jsFiles.login)
  ].join('\n\n');
}

/**
 * Embed mask image as base64 data URL in CSS
 */
function embedImageInCSS(css) {
  const maskPath = path.join(__dirname, '../../resources/mask.png');
  const maskBase64 = fs.readFileSync(maskPath).toString('base64');
  const dataUrl = `data:image/png;base64,${maskBase64}`;
  return css.replace(/url\(['"]?.*?resources\/mask\.png['"]?\)/g, `url('${dataUrl}')`);
}

/**
 * Inline CSS and JavaScript into HTML
 */
function inlineResources(html, css, js) {
  return html
    .replace(
      '<style>\n        /* CSS will be inlined here by main process */\n    </style>',
      `<style>\n${css}\n    </style>`
    )
    .replace(
      '<script>\n        /* JavaScript will be inlined here by main process */\n    </script>',
      `<script>\n${js}\n    </script>`
    );
}

/**
 * Load login page with Virtual Host implementation
 * Inlines all resources and loads with HTTPS baseURL for reCAPTCHA compatibility
 */
function loadLoginPageWithVirtualHost(window) {
  try {
    safeLog('[Main] Loading login page with Virtual Host');
    
    const basePath = path.join(__dirname, '../renderer/pages/login');
    const html = fs.readFileSync(path.join(basePath, 'index.html'), 'utf8');
    const css = fs.readFileSync(path.join(basePath, 'style.css'), 'utf8');
    
    const jsFiles = loadJavaScriptFiles();
    const combinedJs = combineJavaScriptModules(jsFiles);
    const processedCSS = embedImageInCSS(css);
    const finalHTML = inlineResources(html, processedCSS, combinedJs);
    
    const baseURL = 'https://user.ffxiv.com.tw/';
    const dataURL = `data:text/html;charset=utf-8,${encodeURIComponent(finalHTML)}`;
    
    window.loadURL(dataURL, { baseURLForDataURL: baseURL });
    safeLog('[Main] Login page loaded, origin:', baseURL);
  } catch (error) {
    safeError('[Main] Failed to load login page:', error);
    window.loadFile(path.join(__dirname, '../renderer/index.html'));
  }
}

/**
 * Backend server management
 */
let backendProcess = null;
const BACKEND_PORT = 5050;
const BACKEND_URL = `http://localhost:${BACKEND_PORT}`;

/**
 * Find the backend executable
 */
function getBackendExecutable() {
  // Development: backend/src/XIVTheCalamity.Api/bin/Debug/net9.0/XIVTheCalamity.Api
  const devPath = path.join(__dirname, '..', '..', '..', 'backend', 'src', 'XIVTheCalamity.Api', 'bin', 'Debug', 'net9.0', 'XIVTheCalamity.Api');
  if (fs.existsSync(devPath) || fs.existsSync(devPath + '.exe')) {
    return devPath;
  }
  
  // Bundle: XIVTheCalamity.app/Contents/MacOS/XIVTheCalamity.Api
  const bundlePath = path.join(process.resourcesPath, '..', 'MacOS', 'XIVTheCalamity.Api');
  if (fs.existsSync(bundlePath)) {
    return bundlePath;
  }
  
  // Alternative bundle: XIVTheCalamity.app/Contents/Resources/backend/XIVTheCalamity.Api
  const altBundlePath = path.join(process.resourcesPath, 'backend', 'XIVTheCalamity.Api');
  if (fs.existsSync(altBundlePath)) {
    return altBundlePath;
  }
  
  log.warn('[Backend] Backend executable not found');
  return null;
}

/**
 * Check if backend is already running
 */
async function checkBackendHealth() {
  return new Promise((resolve) => {
    const req = http.get(`${BACKEND_URL}/api/config`, (res) => {
      resolve(res.statusCode === 200);
    });
    req.on('error', () => resolve(false));
    req.setTimeout(2000, () => {
      req.destroy();
      resolve(false);
    });
  });
}

/**
 * Read app config to check development mode
 */
function isDevelopmentMode() {
  try {
    const configPath = path.join(app.getPath('appData'), 'XIVTheCalamity', 'config.json');
    if (fs.existsSync(configPath)) {
      const configData = fs.readFileSync(configPath, 'utf8');
      const config = JSON.parse(configData);
      return config.launcher?.developmentMode === true;
    }
  } catch (error) {
    log.warn('[Backend] Failed to read config for development mode:', error.message);
  }
  return false;
}

/**
 * Start the backend server
 */
async function startBackend() {
  // Check if already running
  const isRunning = await checkBackendHealth();
  if (isRunning) {
    log.info('[Backend] Backend is already running');
    return true;
  }
  
  const backendExe = getBackendExecutable();
  if (!backendExe) {
    log.error('[Backend] Cannot find backend executable');
    return false;
  }
  
  log.info('[Backend] Starting backend from:', backendExe);
  
  // Get the directory of the backend executable
  const backendDir = path.dirname(backendExe);
  
  // Determine environment based on config file
  const devMode = isDevelopmentMode();
  const aspnetEnv = devMode ? 'Development' : 'Production';
  log.info(`[Backend] Development mode: ${devMode ? 'ON' : 'OFF'}`);
  log.info(`[Backend] Using ASPNETCORE_ENVIRONMENT: ${aspnetEnv}`);
  
  return new Promise((resolve) => {
    backendProcess = spawn(backendExe, [], {
      cwd: backendDir, // Run in backend directory, not frontend
      stdio: ['ignore', 'pipe', 'pipe'],
      detached: false,
      env: { 
        ...process.env, 
        ASPNETCORE_ENVIRONMENT: aspnetEnv,
        ASPNETCORE_LOGGING__CONSOLE__LOGLEVEL__DEFAULT: devMode ? 'Debug' : 'Information'
      }
    });
    
    backendProcess.stdout.on('data', (data) => {
      log.info('[Backend]', data.toString().trim());
    });
    
    backendProcess.stderr.on('data', (data) => {
      log.error('[Backend Error]', data.toString().trim());
    });
    
    backendProcess.on('error', (error) => {
      log.error('[Backend] Failed to start:', error);
      resolve(false);
    });
    
    backendProcess.on('exit', (code) => {
      log.info('[Backend] Process exited with code:', code);
      backendProcess = null;
    });
    
    // Wait for backend to be ready
    let attempts = 0;
    const maxAttempts = 60; // 30 seconds max
    const checkInterval = setInterval(async () => {
      attempts++;
      const isHealthy = await checkBackendHealth();
      
      if (isHealthy) {
        clearInterval(checkInterval);
        log.info('[Backend] Backend is ready');
        resolve(true);
      } else if (attempts >= maxAttempts) {
        clearInterval(checkInterval);
        log.error('[Backend] Backend failed to start within timeout');
        if (backendProcess) {
          backendProcess.kill();
        }
        resolve(false);
      }
    }, 500);
  });
}

/**
 * Stop the backend server
 */
function stopBackend() {
  if (backendProcess) {
    log.info('[Backend] Stopping backend server');
    backendProcess.kill();
    backendProcess = null;
  }
}

app.whenReady().then(async () => {
  // Initialize debug mode from config
  isDebugModeEnabled = isDevelopmentMode();
  log.info('[Main] Debug mode:', isDebugModeEnabled ? 'enabled' : 'disabled');
  
  // Start backend first
  const backendStarted = await startBackend();
  if (!backendStarted) {
    log.error('[Main] Failed to start backend, continuing anyway...');
  }
  
  createWindow();

  app.on('activate', function () {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', function () {
  stopBackend();
  app.quit();
});

app.on('before-quit', () => {
  stopBackend();
});

/**
 * IPC Handler: Backend API calls
 * This allows renderer to call backend API without being affected by baseURLForDataURL
 */
ipcMain.handle('backend:call', async (event, endpoint, options = {}) => {
  const backendUrl = 'http://localhost:5050';
  const url = `${backendUrl}${endpoint}`;
  
  safeLog('[IPC] Backend call:', options.method || 'GET', url);
  
  return new Promise((resolve, reject) => {
    const urlObj = new URL(url);
    const isHttps = urlObj.protocol === 'https:';
    const httpModule = isHttps ? https : http;
    
    const requestOptions = {
      hostname: urlObj.hostname,
      port: urlObj.port || (isHttps ? 443 : 80),
      path: urlObj.pathname + urlObj.search,
      method: options.method || 'GET',
      headers: options.headers || {}
    };
    
    // Add Content-Type for POST/PUT requests
    if (options.body && !requestOptions.headers['Content-Type']) {
      requestOptions.headers['Content-Type'] = 'application/json';
    }
    
    const req = httpModule.request(requestOptions, (res) => {
      let data = '';
      
      res.on('data', (chunk) => {
        data += chunk;
      });
      
      res.on('end', () => {
        safeLog('[IPC] Response:', res.statusCode);
        
        let parsedData;
        try {
          parsedData = JSON.parse(data);
        } catch (e) {
          parsedData = data;
        }
        
        resolve({
          ok: res.statusCode >= 200 && res.statusCode < 300,
          status: res.statusCode,
          statusText: res.statusMessage,
          data: parsedData
        });
      });
    });
    
    req.on('error', (error) => {
      safeError('[IPC] Request failed:', error);
      reject(error);
    });
    
    // Send body if present
    if (options.body) {
      const bodyStr = typeof options.body === 'string' 
        ? options.body 
        : JSON.stringify(options.body);
      req.write(bodyStr);
    }
    
    req.end();
  });
});

/**
 * IPC Handler: File Storage
 * Store data in ~/Library/Application Support/XIVTheCalamity/credentials
 * This directory is separate from Electron's userData and won't be affected by clearCache
 */

// Use Application Support for user config (NOT userData which points to Cache)
const credentialsDir = path.join(userConfigDir, 'credentials');

// Ensure credentials directory exists
if (!fs.existsSync(credentialsDir)) {
  fs.mkdirSync(credentialsDir, { recursive: true });
  safeLog('[Storage] Created credentials directory:', credentialsDir);
} else {
  // Directory exists, check what's in it
  try {
    const files = fs.readdirSync(credentialsDir);
    safeLog('[Storage] Credentials directory exists with files:', files);
  } catch (e) {
    safeLog('[Storage] Credentials directory exists but cannot read:', e.message);
  }
}

// Watch the credentials directory for changes (debugging)
if (process.env.NODE_ENV !== 'production') {
  try {
    fs.watch(credentialsDir, (eventType, filename) => {
      safeLog('[Storage] FILE CHANGE:', eventType, filename, 'at', new Date().toISOString());
    });
    safeLog('[Storage] Watching credentials directory for changes');
  } catch (e) {
    safeLog('[Storage] Cannot watch directory:', e.message);
  }
}

/**
 * Save data to file
 */
ipcMain.handle('storage:save', async (event, filename, data) => {
  try {
    const filePath = path.join(credentialsDir, filename);
    const jsonData = JSON.stringify(data, null, 2);
    fs.writeFileSync(filePath, jsonData, 'utf8');
    safeLog('[Storage] Saved to:', filePath);
    return { success: true };
  } catch (error) {
    safeError('[Storage] Save failed:', error);
    return { success: false, error: error.message };
  }
});

/**
 * Load data from file
 */
ipcMain.handle('storage:load', async (event, filename) => {
  try {
    const filePath = path.join(credentialsDir, filename);
    if (!fs.existsSync(filePath)) {
      safeLog('[Storage] File not found:', filePath);
      return { success: true, data: null };
    }
    const jsonData = fs.readFileSync(filePath, 'utf8');
    const data = JSON.parse(jsonData);
    safeLog('[Storage] Loaded from:', filePath);
    return { success: true, data };
  } catch (error) {
    safeError('[Storage] Load failed:', error);
    return { success: false, error: error.message, data: null };
  }
});

/**
 * Delete file
 */
ipcMain.handle('storage:delete', async (event, filename) => {
  try {
    const filePath = path.join(credentialsDir, filename);
    if (fs.existsSync(filePath)) {
      fs.unlinkSync(filePath);
      safeLog('[Storage] Deleted:', filePath);
    }
    return { success: true };
  } catch (error) {
    safeError('[Storage] Delete failed:', error);
    return { success: false, error: error.message };
  }
});

/**
 * Open settings window
 */
ipcMain.handle('window:open-settings', async () => {
  try {
    safeLog('[IPC] Opening settings window');
    createSettingsWindow();
    return { success: true };
  } catch (error) {
    safeError('[IPC] Failed to open settings:', error);
    return { success: false, error: error.message };
  }
});

/**
 * Select directory
 */
ipcMain.handle('dialog:select-directory', async () => {
  const { dialog } = require('electron');
  try {
    return await dialog.showOpenDialog({
      properties: ['openDirectory']
    });
  } catch (error) {
    safeError('[IPC] Dialog failed:', error);
    return { canceled: true };
  }
});

/**
 * Open external URL
 */
ipcMain.handle('shell:open-external', async (event, url) => {
  const { shell } = require('electron');
  try {
    await shell.openExternal(url);
    return { success: true };
  } catch (error) {
    safeError('[IPC] Failed to open URL:', error);
    return { success: false, error: error.message };
  }
});

/**
 * Get version info
 */
ipcMain.handle('app:get-version', async () => {
  return versionInfo;
});

/**
 * Open log folder
 */
ipcMain.handle('app:open-log-folder', async () => {
  const { shell } = require('electron');
  try {
    await shell.openPath(logDir);
    return { success: true };
  } catch (error) {
    safeError('[IPC] Failed to open log folder:', error);
    return { success: false, error: error.message };
  }
});

/**
 * Select directory dialog
 */
ipcMain.handle('app:select-directory', async (event, options = {}) => {
  const { dialog } = require('electron');
  try {
    const result = await dialog.showOpenDialog({
      properties: ['openDirectory', 'createDirectory'],
      title: options.title || 'Select Directory',
      buttonLabel: options.buttonLabel || 'Select'
    });
    
    if (result.canceled) {
      return { success: false, canceled: true };
    }
    
    return { success: true, path: result.filePaths[0] };
  } catch (error) {
    safeError('[IPC] Failed to show directory dialog:', error);
    return { success: false, error: error.message };
  }
});

/**
 * Create directory with game subdirectories
 */
ipcMain.handle('app:create-directory', async (event, dirPath) => {
  try {
    // Create main directory
    if (!fs.existsSync(dirPath)) {
      fs.mkdirSync(dirPath, { recursive: true });
    }
    
    // Create game and boot subdirectories
    const gameDir = path.join(dirPath, 'game');
    const bootDir = path.join(dirPath, 'boot');
    
    if (!fs.existsSync(gameDir)) {
      fs.mkdirSync(gameDir, { recursive: true });
    }
    
    if (!fs.existsSync(bootDir)) {
      fs.mkdirSync(bootDir, { recursive: true });
    }
    
    log.info(`[IPC] Created game directory structure at: ${dirPath}`);
    return { success: true, path: dirPath };
  } catch (error) {
    safeError('[IPC] Failed to create directory:', error);
    return { success: false, error: error.message };
  }
});

/**
 * Validate game directory structure
 */
ipcMain.handle('app:validate-game-directory', async (event, dirPath) => {
  try {
    if (!dirPath || !fs.existsSync(dirPath)) {
      return { valid: false, reason: 'Directory does not exist' };
    }
    
    const gameDir = path.join(dirPath, 'game');
    const bootDir = path.join(dirPath, 'boot');
    
    const hasGame = fs.existsSync(gameDir) && fs.statSync(gameDir).isDirectory();
    const hasBoot = fs.existsSync(bootDir) && fs.statSync(bootDir).isDirectory();
    
    if (!hasGame || !hasBoot) {
      return { 
        valid: false, 
        reason: 'Missing required subdirectories (game, boot)',
        hasGame,
        hasBoot
      };
    }
    
    return { valid: true, path: dirPath };
  } catch (error) {
    safeError('[IPC] Failed to validate directory:', error);
    return { valid: false, error: error.message };
  }
});

/**
 * 顯示訊息對話框
 */
ipcMain.handle('dialog:show-message-box', async (event, options) => {
  const { BrowserWindow } = require('electron');
  const focusedWindow = BrowserWindow.getFocusedWindow() || mainWindow;
  
  return await dialog.showMessageBox(focusedWindow, {
    type: options.type || 'info',
    title: options.title || 'XIV The Calamity',
    message: options.message || '',
    buttons: options.buttons || ['OK']
  });
});

/**
 * 廣播事件到所有視窗
 */
ipcMain.on('app:broadcast-event', (event, eventName, data) => {
  safeLog(`[IPC] Broadcasting event: ${eventName}`);
  
  // 發送到所有視窗
  const { BrowserWindow } = require('electron');
  const allWindows = BrowserWindow.getAllWindows();
  
  for (const win of allWindows) {
    // 不發送給自己
    if (win.webContents.id !== event.sender.id) {
      win.webContents.send(`app:event:${eventName}`, data);
    }
  }
});


