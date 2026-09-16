import { apiFetch } from '../../utils/polyfill.js';
/**
 * App Auto-Update UI Manager
 * Handles launcher self-update notifications as a floating dialog.
 * Update check is blocking — subsequent initialization steps
 * wait until the check completes and the user responds.
 */

import i18n from '../../i18n/index.js';
import { showTitleBarProgress, hideTitleBarProgress } from './login.js';
import { parseReleaseNotes } from './releaseNotes.js';

let updateState = 'idle'; // idle | checking | available | downloading | downloaded | error
let pendingVersion = null;
let resolveUpdatePromise = null;
let initialCheckDone = false;

/**
 * Check for launcher updates (blocking).
 * Returns a Promise that resolves when:
 *  - no update is available, or
 *  - the user dismisses the dialog, or
 *  - an error occurs (don't block on errors).
 * The Promise never resolves if the user chooses "Restart Now".
 */
export async function initAppUpdater() {
  if (!window.xivtc?.updater) {
    console.log('[APP-UPDATE] updater API not available');
    return;
  }

  console.log('[APP-UPDATE] Starting blocking update check');

  return new Promise((resolve) => {
    resolveUpdatePromise = resolve;

    window.xivtc.updater.onChecking(() => {
      updateState = 'checking';
      console.log('[APP-UPDATE] Checking for app updates...');
      showTitleBarProgress(0, i18n.t('app_update.checking'));
    });

    window.xivtc.updater.onAvailable((data) => {
      updateState = 'available';
      pendingVersion = data.version;
      pendingReleaseNotes = data.releaseNotes || null;
      console.log('[APP-UPDATE] Update available:', data.version);
      hideTitleBarProgress();
      if (initialCheckDone) {
        // Background periodic check: show non-intrusive reminder
        showReminder('available');
      } else {
        // Initial startup check: show full blocking dialog
        showUpdateDialog(data.version, data.releaseNotes);
      }
    });

    window.xivtc.updater.onNotAvailable(() => {
      updateState = 'idle';
      console.log('[APP-UPDATE] App is up to date');
      hideTitleBarProgress();
      if (!initialCheckDone) {
        initialCheckDone = true;
        resolve();
      }
    });

    window.xivtc.updater.onProgress((data) => {
      updateState = 'downloading';
      const percent = Math.round(data.percent);
      const speedMB = (data.bytesPerSecond / 1024 / 1024).toFixed(1);
      const msg = `${i18n.t('app_update.downloading')} ${percent}% (${speedMB} MB/s)`;
      showTitleBarProgress(percent, msg);
    });

    window.xivtc.updater.onDownloaded((data) => {
      updateState = 'downloaded';
      pendingVersion = data.version;
      console.log('[APP-UPDATE] Update downloaded:', data.version);
      hideTitleBarProgress();
      showRestartDialog(data.version);
    });

    window.xivtc.updater.onError((data) => {
      updateState = 'error';
      console.error('[APP-UPDATE] Update error:', data.message);
      hideTitleBarProgress();
      removeDialog();
      if (!initialCheckDone) {
        initialCheckDone = true;
        resolve();
      }
    });

    console.log('[APP-UPDATE] Listeners ready, triggering update check');
    window.xivtc.updater.check().then((result) => {
      console.log('[APP-UPDATE] Check result:', result);
      if (result?.skipped) {
        console.log('[APP-UPDATE] Update check skipped (dev mode)');
        initialCheckDone = true;
        resolve();
      }
    }).catch((err) => {
      console.error('[APP-UPDATE] Check failed:', err);
      initialCheckDone = true;
      resolve();
    });
  });
}

// ── Dialog helpers ──────────────────────────────────

let pendingReleaseNotes = null;

function showUpdateDialog(version, releaseNotes) {
  removeDialog();
  removeReminder();
  pendingReleaseNotes = releaseNotes || null;

  const notesHtml = parseReleaseNotes(releaseNotes, i18n.locale);
  const notesSection = notesHtml
    ? `<div class="app-update-dialog-notes-label" data-i18n="app_update.whats_new">${i18n.t('app_update.whats_new')}</div>
       <div class="app-update-dialog-notes">${notesHtml}</div>`
    : '';

  const overlay = document.createElement('div');
  overlay.id = 'appUpdateOverlay';
  overlay.className = 'app-update-overlay';
  overlay.innerHTML = `
    <div class="app-update-dialog">
      <div class="app-update-dialog-icon">🎉</div>
      <p class="app-update-dialog-title" data-i18n="app_update.available_msg" data-i18n-options='${JSON.stringify({ version })}'>${i18n.t('app_update.available_msg', { version })}</p>
      ${notesSection}
      <div class="app-update-dialog-buttons">
        <button class="app-update-btn app-update-btn-primary" id="appUpdateDownloadBtn">
          <span data-i18n="app_update.download">${i18n.t('app_update.download')}</span>
        </button>
        <button class="app-update-btn app-update-btn-secondary" id="appUpdateLaterBtn">
          <span data-i18n="app_update.later">${i18n.t('app_update.later')}</span>
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

    await window.xivtc.updater.download();
  });

  document.getElementById('appUpdateLaterBtn').addEventListener('click', () => {
    removeDialog();
    showReminder('available');
    initialCheckDone = true;
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
      <p class="app-update-dialog-title" data-i18n="app_update.ready_msg" data-i18n-options='${JSON.stringify({ version })}'>${i18n.t('app_update.ready_msg', { version })}</p>
      <div class="app-update-dialog-buttons">
        <button class="app-update-btn app-update-btn-primary app-update-btn-green" id="appUpdateInstallBtn">
          <span data-i18n="app_update.restart">${i18n.t('app_update.restart')}</span>
        </button>
        <button class="app-update-btn app-update-btn-secondary" id="appUpdateLaterBtn">
          <span data-i18n="app_update.restart_later">${i18n.t('app_update.restart_later')}</span>
        </button>
      </div>
    </div>
  `;
  document.body.appendChild(overlay);

  document.getElementById('appUpdateInstallBtn').addEventListener('click', async () => {
    await window.xivtc.updater.install();
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
  const i18nKey = isReady ? 'app_update.reminder_ready' : 'app_update.reminder_available';

  const btn = document.createElement('button');
  btn.id = 'appUpdateReminder';
  btn.className = 'app-update-reminder' + (isReady ? ' app-update-reminder-ready' : '');
  
  const span = document.createElement('span');
  span.setAttribute('data-i18n', i18nKey);
  span.textContent = i18n.t(i18nKey);
  btn.appendChild(span);

  // Place inside login-container so it's positioned relative to the login card
  const loginContainer = document.querySelector('.login-container') || document.querySelector('.login-section') || document.body;
  loginContainer.appendChild(btn);

  btn.addEventListener('click', () => {
    removeReminder();
    if (updateState === 'downloaded') {
      showRestartDialog(pendingVersion);
    } else {
      showUpdateDialog(pendingVersion, pendingReleaseNotes);
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

// Rosetta 安裝提示，沿用更新視窗樣式。

async function postRosettaAction(path) {
  const response = await apiFetch(`http://localhost:5050${path}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: '{}'
  });
  let data = null;
  try {
    data = await response.json();
  } catch {
    // The HTTP status below is still enough to report the failed request.
  }

  if (!response.ok || !data?.success) {
    throw new Error(data?.error?.message || '');
  }
}

export function showRosettaDialog() {
  if (window.xivtc?.getPlatform?.() !== 'darwin' || document.getElementById('rosettaOverlay')) return;

  const previouslyFocused = document.activeElement;
  const overlay = document.createElement('div');
  overlay.id = 'rosettaOverlay';
  overlay.className = 'app-update-overlay rosetta-overlay';
  overlay.innerHTML = `
    <div class="app-update-dialog rosetta-dialog" role="dialog" aria-modal="true" aria-labelledby="rosettaDialogTitle" aria-describedby="rosettaDialogDescription">
      <div class="app-update-dialog-icon">🍎</div>
      <p class="app-update-dialog-title" id="rosettaDialogTitle">${i18n.t('rosetta.title')}</p>
      <div class="app-update-dialog-notes rosetta-dialog-content" id="rosettaDialogDescription">
        <p>${i18n.t('rosetta.description')}</p>
        <p>${i18n.t('rosetta.terms_prefix')} <a href="https://www.apple.com/legal/sla/" target="_blank" rel="noopener noreferrer">${i18n.t('rosetta.terms_link')}</a>${i18n.t('rosetta.terms_suffix')}</p>
        <p class="rosetta-dialog-error" role="alert" hidden></p>
      </div>
      <div class="app-update-dialog-buttons">
        <button class="app-update-btn app-update-btn-primary app-update-btn-green" type="button" id="rosettaInstallBtn">${i18n.t('rosetta.install_restart')}</button>
        <button class="app-update-btn app-update-btn-secondary" type="button" id="rosettaCancelBtn">${i18n.t('button.cancel')}</button>
      </div>
    </div>
  `;
  document.body.appendChild(overlay);

  const dialog = overlay.querySelector('.rosetta-dialog');
  const installButton = overlay.querySelector('#rosettaInstallBtn');
  const cancelButton = overlay.querySelector('#rosettaCancelBtn');
  const error = overlay.querySelector('.rosetta-dialog-error');
  let busy = false;
  let installed = false;

  const close = () => {
    if (busy) return;
    overlay.remove();
    previouslyFocused?.focus?.();
  };

  const setBusy = (message) => {
    busy = true;
    installButton.disabled = true;
    cancelButton.disabled = true;
    installButton.innerHTML = `<span class="app-update-spinner" aria-hidden="true"></span><span>${message}</span>`;
    error.hidden = true;
    overlay.querySelector('a').focus();
  };

  const setError = (message) => {
    busy = false;
    installButton.disabled = false;
    cancelButton.disabled = false;
    installButton.textContent = installed ? i18n.t('rosetta.restart') : i18n.t('rosetta.install_restart');
    error.textContent = message;
    error.hidden = false;
    installButton.focus();
  };

  installButton.addEventListener('click', async () => {
    try {
      if (!installed) {
        setBusy(i18n.t('rosetta.installing'));
        await postRosettaAction('/api/environment/install-rosetta');
        installed = true;
      }
      setBusy(i18n.t('rosetta.restarting'));
      await postRosettaAction('/api/environment/restart');
    } catch (exception) {
      console.error('[Rosetta] Installation or restart failed:', exception);
      const errorKey = exception.message?.startsWith('rosetta.')
        ? exception.message
        : (installed ? 'rosetta.restart_failed' : 'rosetta.install_failed');
      setError(i18n.t(errorKey));
    }
  });

  cancelButton.addEventListener('click', close);
  overlay.querySelector('a').addEventListener('click', (event) => {
    event.preventDefault();
    window.xivtc?.openExternal?.(event.currentTarget.href);
  });
  dialog.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && !busy) {
      event.preventDefault();
      close();
      return;
    }
    if (event.key !== 'Tab') return;

    const focusable = [...dialog.querySelectorAll('a[href], button:not([disabled])')];
    const first = focusable[0];
    const last = focusable.at(-1);
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });

  installButton.focus();
}
