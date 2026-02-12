const { autoUpdater } = require('electron-updater');
const { ipcMain } = require('electron');
const log = require('electron-log');

// Configure auto-updater
autoUpdater.logger = log;
autoUpdater.autoDownload = false;
autoUpdater.autoInstallOnAppQuit = true;

let mainWindow = null;

function sendToRenderer(channel, data = {}) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send(channel, data);
  }
}

// Event listeners
autoUpdater.on('checking-for-update', () => {
  log.info('[Updater] Checking for updates...');
  sendToRenderer('app-update:checking');
});

autoUpdater.on('update-available', (info) => {
  log.info('[Updater] Update available:', info.version);
  sendToRenderer('app-update:available', {
    version: info.version,
    releaseNotes: info.releaseNotes,
    releaseDate: info.releaseDate
  });
});

autoUpdater.on('update-not-available', (info) => {
  log.info('[Updater] No updates available. Current:', info.version);
  sendToRenderer('app-update:not-available');
});

autoUpdater.on('download-progress', (progress) => {
  sendToRenderer('app-update:download-progress', {
    percent: progress.percent,
    bytesPerSecond: progress.bytesPerSecond,
    transferred: progress.transferred,
    total: progress.total
  });
});

autoUpdater.on('update-downloaded', (info) => {
  log.info('[Updater] Update downloaded:', info.version);
  sendToRenderer('app-update:downloaded', {
    version: info.version
  });
});

autoUpdater.on('error', (error) => {
  log.error('[Updater] Error:', error.message);
  sendToRenderer('app-update:error', {
    message: error.message
  });
});

// IPC handlers
ipcMain.handle('app:check-updates', async () => {
  if (process.env.NODE_ENV === 'development' || process.argv.includes('--dev')) {
    log.info('[Updater] Skipping update check in development mode');
    return { success: true, skipped: true };
  }
  try {
    const result = await autoUpdater.checkForUpdates();
    return { success: true, version: result?.updateInfo?.version };
  } catch (error) {
    log.error('[Updater] Check failed:', error.message);
    return { success: false, error: error.message };
  }
});

ipcMain.handle('app:download-update', async () => {
  try {
    await autoUpdater.downloadUpdate();
    return { success: true };
  } catch (error) {
    log.error('[Updater] Download failed:', error.message);
    return { success: false, error: error.message };
  }
});

ipcMain.handle('app:install-update', () => {
  autoUpdater.quitAndInstall(false, true);
});

/**
 * Initialize the updater with the main window reference
 * @param {BrowserWindow} window
 */
function initUpdater(window) {
  mainWindow = window;
}

module.exports = { initUpdater };
