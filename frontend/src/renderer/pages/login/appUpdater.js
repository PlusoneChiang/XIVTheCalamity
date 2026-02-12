/**
 * App Auto-Update UI Manager
 * Handles launcher self-update notifications in the login page.
 * Update check is blocking — subsequent initialization steps
 * wait until the check completes and the user responds.
 */

import i18n from '../../i18n/index.js';
import { showTitleBarProgress, hideTitleBarProgress } from './login.js';

let updateState = 'idle'; // idle | checking | available | downloading | downloaded | error

/**
 * Check for launcher updates (blocking).
 * Returns a Promise that resolves when:
 *  - no update is available, or
 *  - the user dismisses the update/restart banner, or
 *  - an error occurs (don't block on errors).
 * The Promise never resolves if the user chooses "Restart Now"
 * because the app will quit and install.
 */
export async function initAppUpdater() {
  if (!window.electronAPI?.updater) {
    console.log('[APP-UPDATE] updater API not available');
    return;
  }

  console.log('[APP-UPDATE] Starting blocking update check');

  return new Promise((resolve) => {
    // ── Event listeners ──────────────────────────────────

    window.electronAPI.updater.onChecking(() => {
      updateState = 'checking';
      console.log('[APP-UPDATE] Checking for app updates...');
      showTitleBarProgress(0, i18n.t('app_update.checking'));
    });

    window.electronAPI.updater.onAvailable((data) => {
      updateState = 'available';
      console.log('[APP-UPDATE] Update available:', data.version);
      hideTitleBarProgress();
      showUpdateBanner(data.version, resolve);
    });

    window.electronAPI.updater.onNotAvailable(() => {
      updateState = 'idle';
      console.log('[APP-UPDATE] App is up to date');
      hideTitleBarProgress();
      resolve();
    });

    window.electronAPI.updater.onProgress((data) => {
      updateState = 'downloading';
      const percent = Math.round(data.percent);
      const speedMB = (data.bytesPerSecond / 1024 / 1024).toFixed(1);
      const msg = `${i18n.t('app_update.downloading')} ${percent}% (${speedMB} MB/s)`;
      showTitleBarProgress(percent, msg);
    });

    window.electronAPI.updater.onDownloaded((data) => {
      updateState = 'downloaded';
      console.log('[APP-UPDATE] Update downloaded:', data.version);
      hideTitleBarProgress();
      showRestartBanner(data.version, resolve);
    });

    window.electronAPI.updater.onError((data) => {
      updateState = 'error';
      console.error('[APP-UPDATE] Update error:', data.message);
      hideTitleBarProgress();
      resolve(); // Don't block on errors
    });

    // ── Trigger the check ────────────────────────────────
    console.log('[APP-UPDATE] Listeners ready, triggering update check');
    window.electronAPI.updater.check().then((result) => {
      console.log('[APP-UPDATE] Check result:', result);
      if (result?.skipped) {
        console.log('[APP-UPDATE] Update check skipped (dev mode)');
        resolve();
      }
    }).catch((err) => {
      console.error('[APP-UPDATE] Check failed:', err);
      resolve(); // Don't block on errors
    });
  });
}

/**
 * Show update available banner.
 * "Download" starts the download (banner stays, waiting for onDownloaded).
 * "Later" dismisses and resolves the promise so init continues.
 */
function showUpdateBanner(version, resolve) {
  removeExistingBanner();

  const banner = document.createElement('div');
  banner.id = 'appUpdateBanner';
  banner.className = 'app-update-banner';
  banner.innerHTML = `
    <span class="app-update-text">🎉 ${i18n.t('app_update.available_msg', { version })}</span>
    <button class="app-update-btn app-update-download" id="appUpdateDownloadBtn">
      ${i18n.t('app_update.download')}
    </button>
    <button class="app-update-btn app-update-dismiss" id="appUpdateDismissBtn">
      ${i18n.t('app_update.later')}
    </button>
  `;

  const container = document.querySelector('.login-container') || document.body;
  container.prepend(banner);

  document.getElementById('appUpdateDownloadBtn').addEventListener('click', async () => {
    banner.querySelector('.app-update-text').textContent = i18n.t('app_update.starting');
    banner.querySelectorAll('.app-update-btn').forEach(b => b.style.display = 'none');
    await window.electronAPI.updater.download();
  });

  document.getElementById('appUpdateDismissBtn').addEventListener('click', () => {
    removeExistingBanner();
    resolve();
  });
}

/**
 * Show restart-to-install banner.
 * "Restart Now" triggers quitAndInstall (never resolves).
 * "Install on next launch" dismisses and resolves so init continues.
 */
function showRestartBanner(version, resolve) {
  removeExistingBanner();

  const banner = document.createElement('div');
  banner.id = 'appUpdateBanner';
  banner.className = 'app-update-banner app-update-ready';
  banner.innerHTML = `
    <span class="app-update-text">✅ ${i18n.t('app_update.ready_msg', { version })}</span>
    <button class="app-update-btn app-update-install" id="appUpdateInstallBtn">
      ${i18n.t('app_update.restart')}
    </button>
    <button class="app-update-btn app-update-dismiss" id="appUpdateLaterBtn">
      ${i18n.t('app_update.restart_later')}
    </button>
  `;

  const container = document.querySelector('.login-container') || document.body;
  container.prepend(banner);

  document.getElementById('appUpdateInstallBtn').addEventListener('click', async () => {
    await window.electronAPI.updater.install();
  });

  document.getElementById('appUpdateLaterBtn').addEventListener('click', () => {
    removeExistingBanner();
    resolve();
  });
}

function removeExistingBanner() {
  const existing = document.getElementById('appUpdateBanner');
  if (existing) existing.remove();
}

export function isAppUpdateDownloading() {
  return updateState === 'downloading';
}
