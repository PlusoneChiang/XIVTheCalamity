/**
 * App Auto-Update UI Manager
 * Handles launcher self-update notifications in the login page
 */

import i18n from '../../i18n/index.js';
import { showTitleBarProgress, hideTitleBarProgress } from './login.js';

let updateState = 'idle'; // idle | checking | available | downloading | downloaded | error

/**
 * Initialize app update listeners
 * Called once on page load, before environment initialization
 */
export function initAppUpdater() {
  if (!window.electronAPI?.updater) {
    console.log('[APP-UPDATE] updater API not available');
    return;
  }

  console.log('[APP-UPDATE] Initializing app update listeners');

  window.electronAPI.updater.onChecking(() => {
    updateState = 'checking';
    console.log('[APP-UPDATE] Checking for app updates...');
    showTitleBarProgress(0, i18n.t('app_update.checking'));
  });

  window.electronAPI.updater.onAvailable((data) => {
    updateState = 'available';
    console.log('[APP-UPDATE] Update available:', data.version);
    showUpdateBanner(data.version);
  });

  window.electronAPI.updater.onNotAvailable(() => {
    updateState = 'idle';
    console.log('[APP-UPDATE] App is up to date');
    hideTitleBarProgress();
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
    showRestartBanner(data.version);
  });

  window.electronAPI.updater.onError((data) => {
    updateState = 'error';
    console.error('[APP-UPDATE] Update error:', data.message);
    hideTitleBarProgress();
  });
}

/**
 * Show update available banner
 */
function showUpdateBanner(version) {
  hideTitleBarProgress();
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
  });
}

/**
 * Show restart to install banner
 */
function showRestartBanner(version) {
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
  });
}

function removeExistingBanner() {
  const existing = document.getElementById('appUpdateBanner');
  if (existing) existing.remove();
}

export function isAppUpdateDownloading() {
  return updateState === 'downloading';
}
