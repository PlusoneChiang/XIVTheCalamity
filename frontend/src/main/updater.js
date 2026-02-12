const { ipcMain, app } = require('electron');
const log = require('electron-log');

let autoUpdater = null;
let mainWindow = null;

// electron-updater only works in packaged builds
const isPackaged = app.isPackaged;

function sendToRenderer(channel, data = {}) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send(channel, data);
  }
}

if (isPackaged) {
  try {
    autoUpdater = require('electron-updater').autoUpdater;
    
    // Configure auto-updater
    autoUpdater.logger = log;
    autoUpdater.autoDownload = false;
    autoUpdater.autoInstallOnAppQuit = true;

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
  } catch (err) {
    log.warn('[Updater] Failed to initialize electron-updater:', err.message);
    autoUpdater = null;
  }
} else {
  log.info('[Updater] Skipping electron-updater in unpackaged mode');
}

// IPC handlers
ipcMain.handle('app:check-updates', async () => {
  if (!autoUpdater) {
    log.info('[Updater] Skipping update check (updater not available)');
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
  if (!autoUpdater) {
    return { success: false, error: 'Updater not available' };
  }
  try {
    await autoUpdater.downloadUpdate();
    return { success: true };
  } catch (error) {
    log.error('[Updater] Download failed:', error.message);
    return { success: false, error: error.message };
  }
});

ipcMain.handle('app:install-update', () => {
  if (autoUpdater) {
    autoUpdater.quitAndInstall(false, true);
  }
});

/**
 * Initialize the updater with the main window reference
 * @param {BrowserWindow} window
 */
function initUpdater(window) {
  mainWindow = window;
}

module.exports = { initUpdater };
