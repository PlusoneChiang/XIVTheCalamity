const { ipcMain, app } = require('electron');
const { spawn } = require('child_process');
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
      isDownloading = true;
      sendToRenderer('app-update:download-progress', {
        percent: progress.percent,
        bytesPerSecond: progress.bytesPerSecond,
        transferred: progress.transferred,
        total: progress.total
      });
    });

    autoUpdater.on('update-downloaded', (info) => {
      isDownloading = false;
      log.info('[Updater] Update downloaded:', info.version);
      sendToRenderer('app-update:downloaded', {
        version: info.version
      });
    });

    autoUpdater.on('error', (error) => {
      isDownloading = false;
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

// Periodic update check (every hour)
const UPDATE_CHECK_INTERVAL = 60 * 60 * 1000;
let periodicCheckTimer = null;
let isDownloading = false;

function startPeriodicUpdateCheck() {
  if (!autoUpdater || periodicCheckTimer) return;
  periodicCheckTimer = setInterval(async () => {
    if (isDownloading) return;
    try {
      log.info('[Updater] Periodic update check...');
      await autoUpdater.checkForUpdates();
    } catch (error) {
      log.error('[Updater] Periodic check failed:', error.message);
    }
  }, UPDATE_CHECK_INTERVAL);
  log.info('[Updater] Periodic update check scheduled (every 1 hour)');
}

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
    // On Linux (AppImage), quitAndInstall restarts without preserving command-line
    // args like --no-sandbox (required for SteamOS Gaming Mode).
    // Register a will-quit handler to spawn the new process with original args.
    if (process.platform === 'linux' && process.env.APPIMAGE) {
      const appPath = process.env.APPIMAGE;
      const args = process.argv.slice(1);
      log.info('[Updater] Linux AppImage: scheduling restart with args:', [appPath, ...args]);

      app.once('will-quit', () => {
        spawn(appPath, args, {
          detached: true,
          stdio: 'ignore',
          env: { ...process.env }
        }).unref();
      });
      // Install silently, do not auto-relaunch (we handle it above)
      autoUpdater.quitAndInstall(true, false);
    } else {
      autoUpdater.quitAndInstall(false, true);
    }
  }
});

/**
 * Initialize the updater with the main window reference
 * @param {BrowserWindow} window
 */
function initUpdater(window) {
  mainWindow = window;
  startPeriodicUpdateCheck();
}

module.exports = { initUpdater };
