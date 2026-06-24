import { Buffer } from 'buffer';
window.Buffer = Buffer;
console.log('[Polyfill] Global Buffer initialized');

if (!window.xivtc) {
  const backendUrl = 'http://localhost:5050'; // Absolute URL required when page is served via custom scheme (xivtc://)

  async function apiCall(path, method = 'POST', body = null) {
    const options = {
      method: method,
      headers: {
        'Content-Type': 'application/json'
      }
    };
    if (body) {
      options.body = JSON.stringify(body);
    }
    try {
      const res = await fetch(`${backendUrl}${path}`, options);
      if (!res.ok) {
        let errData;
        try { errData = await res.json(); } catch(e) {}
        return {
          success: false,
          ok: false,
          status: res.status,
          statusText: res.statusText,
          data: errData
        };
      }
      const data = await res.json();
      return {
        success: true,
        ok: true,
        status: res.status,
        statusText: res.statusText,
        data: data
      };
    } catch(e) {
      return {
        success: false,
        ok: false,
        status: 500,
        statusText: 'Network Error',
        error: e.message
      };
    }
  }

  window.xivtc = {
    backend: {
      call: async (endpoint, options = {}) => {
        const method = options.method || 'GET';
        let body = options.body;
        if (body && typeof body === 'string') {
          try { body = JSON.parse(body); } catch(e) {}
        }
        return await apiCall(endpoint, method, body);
      }
    },
    storage: {
      save: async (filename, data) => {
        const res = await apiCall('/api/storage/save', 'POST', { filename, data });
        return res.data || { success: false, error: res.error || 'Failed' };
      },
      load: async (filename) => {
        const res = await apiCall('/api/storage/load', 'POST', { filename });
        return res.data || { success: false, data: null, error: res.error || 'Failed' };
      },
      delete: async (filename) => {
        const res = await apiCall('/api/storage/delete', 'POST', { filename });
        return res.data || { success: false, error: res.error || 'Failed' };
      }
    },
    openSettings: async () => {
      const res = await apiCall('/api/window/open-settings', 'POST');
      if (res.success && res.data) return res.data;
      return { success: false, message: res.error || res.statusText || 'Failed to open settings' };
    },
    selectDirectory: async (options) => {
      const res = await apiCall('/api/app/select-directory', 'POST', options);
      if (res.success && res.data) {
        return res.data;
      }
      return { success: false, canceled: true };
    },
    openExternal: async (url) => {
      const res = await apiCall('/api/shell/open-external', 'POST', { url });
      if (res.success && res.data) return res.data;
      return { success: false, message: res.error || res.statusText || 'Failed to open external link' };
    },
    getVersion: async () => {
      const res = await apiCall('/api/app/get-version', 'GET');
      return res.data || { appName: 'XIVTheCalamity', version: '2.0.0' };
    },
    openLogFolder: async () => {
      const res = await apiCall('/api/app/open-log-folder', 'POST');
      if (res.success && res.data) return res.data;
      return { success: false, message: res.error || res.statusText || 'Failed to open log folder' };
    },
    getPlatform: () => {
      try {
        const params = new URLSearchParams(window.location.search);
        const p = params.get('platform');
        if (p === 'darwin' || p === 'win32' || p === 'linux') {
          return p;
        }
      } catch (e) {
        console.warn('[Polyfill] Failed to parse platform query parameter:', e);
      }
      const ua = window.navigator.userAgent.toLowerCase();
      if (ua.includes('macintosh') || ua.includes('mac os x')) return 'darwin';
      if (ua.includes('windows') || ua.includes('win32')) return 'win32';
      return 'linux';
    },
    closeWindow: async () => {
      const res = await apiCall('/api/window/close', 'POST');
      if (res.success && res.data) return res.data;
      return { success: false, message: res.error || res.statusText || 'Failed to close window' };
    },
    createDirectory: async (path) => {
      const res = await apiCall('/api/app/create-directory', 'POST', { url: path });
      if (res.success && res.data) return res.data;
      return { success: false, message: res.error || res.statusText || 'Failed to create directory' };
    },
    validateGameDirectory: async (path) => {
      const res = await apiCall('/api/app/validate-game-directory', 'POST', { url: path });
      if (res.success && res.data) return res.data;
      return { valid: false, reason: res.error || res.statusText || 'Failed to validate game directory' };
    },
    showMessageBox: async (options) => {
      const res = await apiCall('/api/dialog/show-message-box', 'POST', options);
      return res.data || { response: 0 };
    },
    events: {
      send: async (eventName, data) => {
        return await apiCall('/api/events/broadcast', 'POST', { eventName, data });
      },
      on: (eventName, callback) => {
        if (!window.__photinoEventSource) {
          window.__photinoEventSource = new EventSource(`http://localhost:5050/api/events/stream`);
          window.__photinoEventListeners = {};
          window.__photinoEventSource.onmessage = (e) => {
            try {
              const evt = JSON.parse(e.data);
              const listeners = window.__photinoEventListeners[evt.eventName];
              if (listeners) {
                listeners.forEach(cb => cb(evt.data));
              }
            } catch(err) {
              console.error('[PhotinoBridge] Error parsing event:', err);
            }
          };
        }
        if (!window.__photinoEventListeners[eventName]) {
          window.__photinoEventListeners[eventName] = [];
        }
        window.__photinoEventListeners[eventName].push(callback);
      },
      off: (eventName) => {
        if (window.__photinoEventListeners) {
          delete window.__photinoEventListeners[eventName];
        }
      }
    },
    updater: {
      check: async () => ({ success: true, skipped: true }),
      download: async () => {},
      install: async () => {},
      onChecking: (cb) => () => {},
      onAvailable: (cb) => () => {},
      onNotAvailable: (cb) => () => {},
      onProgress: (cb) => () => {},
      onDownloaded: (cb) => () => {},
      onError: (cb) => () => {}
    }
  };

  // Live DevMode state tracking
  let isDevMode = false;

  function updateLocalDevMode(config) {
    if (config && config.launcher) {
      isDevMode = !!config.launcher.developmentMode;
      console.log('[Polyfill] Local devMode updated to:', isDevMode);
    }
  }

  // Fetch initial configuration from backend
  fetch(`${backendUrl}/api/config`)
    .then(res => res.json())
    .then(json => {
      if (json && json.success && json.data) {
        updateLocalDevMode(json.data);
      }
    })
    .catch(err => console.error('[Polyfill] Failed to fetch initial config:', err));

  // Listen to live config updates from backend
  window.xivtc.events.on('config-updated', (config) => {
    updateLocalDevMode(config);
  });

  // Global event interceptor for context menu
  document.addEventListener('contextmenu', (e) => {
    if (!isDevMode) {
      e.preventDefault();
      e.stopPropagation();
    }
  }, true);

  // Global event interceptor for developer tools keyboard shortcuts
  document.addEventListener('keydown', (e) => {
    if (isDevMode) return;

    // Block F12, Ctrl+Shift+I, Cmd+Option+I
    const isF12 = e.key === 'F12' || e.keyCode === 123;
    const isCtrlShiftI = (e.ctrlKey || e.metaKey) && e.shiftKey && (e.key === 'i' || e.key === 'I' || e.keyCode === 73);
    const isCmdOptI = e.metaKey && e.altKey && (e.key === 'i' || e.key === 'I' || e.keyCode === 73);

    if (isF12 || isCtrlShiftI || isCmdOptI) {
      e.preventDefault();
      e.stopPropagation();
    }
  }, true);

  console.log('[Polyfill] window.xivtc polyfill successfully initialized');
}
