/**
 * App Auto-Update UI Manager
 * Handles launcher self-update notifications as a floating dialog.
 * Update check is blocking — subsequent initialization steps
 * wait until the check completes and the user responds.
 */

import i18n from '../../i18n/index.js';
import { showTitleBarProgress, hideTitleBarProgress } from './login.js';

let updateState = 'idle'; // idle | checking | available | downloading | downloaded | error
let pendingVersion = null;
let resolveUpdatePromise = null;

/**
 * Check for launcher updates (blocking).
 * Returns a Promise that resolves when:
 *  - no update is available, or
 *  - the user dismisses the dialog, or
 *  - an error occurs (don't block on errors).
 * The Promise never resolves if the user chooses "Restart Now".
 */
export async function initAppUpdater() {
  if (!window.electronAPI?.updater) {
    console.log('[APP-UPDATE] updater API not available');
    return;
  }

  console.log('[APP-UPDATE] Starting blocking update check');

  return new Promise((resolve) => {
    resolveUpdatePromise = resolve;

    window.electronAPI.updater.onChecking(() => {
      updateState = 'checking';
      console.log('[APP-UPDATE] Checking for app updates...');
      showTitleBarProgress(0, i18n.t('app_update.checking'));
    });

    window.electronAPI.updater.onAvailable((data) => {
      updateState = 'available';
      pendingVersion = data.version;
      console.log('[APP-UPDATE] Update available:', data.version);
      hideTitleBarProgress();
      showUpdateDialog(data.version);
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
      pendingVersion = data.version;
      console.log('[APP-UPDATE] Update downloaded:', data.version);
      hideTitleBarProgress();
      showRestartDialog(data.version);
    });

    window.electronAPI.updater.onError((data) => {
      updateState = 'error';
      console.error('[APP-UPDATE] Update error:', data.message);
      hideTitleBarProgress();
      removeDialog();
      resolve();
    });

    console.log('[APP-UPDATE] Listeners ready, triggering update check');
    window.electronAPI.updater.check().then((result) => {
      console.log('[APP-UPDATE] Check result:', result);
      if (result?.skipped) {
        console.log('[APP-UPDATE] Update check skipped (dev mode)');
        resolve();
      }
    }).catch((err) => {
      console.error('[APP-UPDATE] Check failed:', err);
      resolve();
    });
  });
}

// ── Dialog helpers ──────────────────────────────────

function showUpdateDialog(version) {
  removeDialog();
  removeReminder();

  const overlay = document.createElement('div');
  overlay.id = 'appUpdateOverlay';
  overlay.className = 'app-update-overlay';
  overlay.innerHTML = `
    <div class="app-update-dialog">
      <div class="app-update-dialog-icon">🎉</div>
      <p class="app-update-dialog-title">${i18n.t('app_update.available_msg', { version })}</p>
      <div class="app-update-dialog-buttons">
        <button class="app-update-btn app-update-btn-primary" id="appUpdateDownloadBtn">
          ${i18n.t('app_update.download')}
        </button>
        <button class="app-update-btn app-update-btn-secondary" id="appUpdateLaterBtn">
          ${i18n.t('app_update.later')}
        </button>
      </div>
    </div>
  `;
  document.body.appendChild(overlay);

  document.getElementById('appUpdateDownloadBtn').addEventListener('click', async () => {
    // Replace buttons with spinner
    const dialog = overlay.querySelector('.app-update-dialog');
    dialog.querySelector('.app-update-dialog-icon').textContent = '';
    dialog.querySelector('.app-update-dialog-icon').innerHTML = '<div class="app-update-spinner"></div>';
    dialog.querySelector('.app-update-dialog-title').textContent = i18n.t('app_update.starting');
    dialog.querySelector('.app-update-dialog-buttons').remove();

    await window.electronAPI.updater.download();
  });

  document.getElementById('appUpdateLaterBtn').addEventListener('click', () => {
    removeDialog();
    showReminder('available');
    if (resolveUpdatePromise) {
      resolveUpdatePromise();
      resolveUpdatePromise = null;
    }
  });
}

function showRestartDialog(version) {
  removeDialog();
  removeReminder();

  const overlay = document.createElement('div');
  overlay.id = 'appUpdateOverlay';
  overlay.className = 'app-update-overlay';
  overlay.innerHTML = `
    <div class="app-update-dialog">
      <div class="app-update-dialog-icon">✅</div>
      <p class="app-update-dialog-title">${i18n.t('app_update.ready_msg', { version })}</p>
      <div class="app-update-dialog-buttons">
        <button class="app-update-btn app-update-btn-primary app-update-btn-green" id="appUpdateInstallBtn">
          ${i18n.t('app_update.restart')}
        </button>
        <button class="app-update-btn app-update-btn-secondary" id="appUpdateLaterBtn">
          ${i18n.t('app_update.restart_later')}
        </button>
      </div>
    </div>
  `;
  document.body.appendChild(overlay);

  document.getElementById('appUpdateInstallBtn').addEventListener('click', async () => {
    await window.electronAPI.updater.install();
  });

  document.getElementById('appUpdateLaterBtn').addEventListener('click', () => {
    removeDialog();
    showReminder('downloaded');
    if (resolveUpdatePromise) {
      resolveUpdatePromise();
      resolveUpdatePromise = null;
    }
  });
}

// ── Minimized reminder button ───────────────────────

function showReminder(type) {
  removeReminder();

  const isReady = type === 'downloaded';
  const text = isReady
    ? i18n.t('app_update.reminder_ready')
    : i18n.t('app_update.reminder_available');

  const btn = document.createElement('button');
  btn.id = 'appUpdateReminder';
  btn.className = 'app-update-reminder' + (isReady ? ' app-update-reminder-ready' : '');
  btn.textContent = text;

  // Place inside login-container so it's positioned relative to the login card
  const loginContainer = document.querySelector('.login-container') || document.querySelector('.login-section') || document.body;
  loginContainer.appendChild(btn);

  btn.addEventListener('click', () => {
    removeReminder();
    if (updateState === 'downloaded') {
      showRestartDialog(pendingVersion);
    } else {
      showUpdateDialog(pendingVersion);
    }
  });
}

// ── Cleanup ─────────────────────────────────────────

function removeDialog() {
  const el = document.getElementById('appUpdateOverlay');
  if (el) el.remove();
}

function removeReminder() {
  const el = document.getElementById('appUpdateReminder');
  if (el) el.remove();
}

export function isAppUpdateDownloading() {
  return updateState === 'downloading';
}
