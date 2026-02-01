const { contextBridge, ipcRenderer } = require('electron');

/**
 * Preload script to expose safe APIs to renderer process
 * This bridges the gap between main process and renderer process
 */

// Expose protected methods that allow the renderer process to use
// ipcRenderer without exposing the entire object
contextBridge.exposeInMainWorld('electronAPI', {
  // Backend API calls through IPC
  backend: {
    /**
     * Call backend API through main process
     * @param {string} endpoint - API endpoint (e.g., '/api/auth/login')
     * @param {object} options - Fetch options (method, body, headers)
     * @returns {Promise<{ok: boolean, status: number, data: any}>}
     */
    call: async (endpoint, options = {}) => {
      return await ipcRenderer.invoke('backend:call', endpoint, options);
    }
  },
  
  // File storage API
  storage: {
    /**
     * Save data to file
     * @param {string} filename - Filename (e.g., 'passwords.json')
     * @param {object} data - Data to save
     * @returns {Promise<{success: boolean, error?: string}>}
     */
    save: async (filename, data) => {
      return await ipcRenderer.invoke('storage:save', filename, data);
    },
    
    /**
     * Load data from file
     * @param {string} filename - Filename (e.g., 'passwords.json')
     * @returns {Promise<{success: boolean, data?: object, error?: string}>}
     */
    load: async (filename) => {
      return await ipcRenderer.invoke('storage:load', filename);
    },
    
    /**
     * Delete file
     * @param {string} filename - Filename to delete
     * @returns {Promise<{success: boolean, error?: string}>}
     */
    delete: async (filename) => {
      return await ipcRenderer.invoke('storage:delete', filename);
    }
  },
  
  // Window operations
  openSettings: async () => {
    return await ipcRenderer.invoke('window:open-settings');
  },
  
  // Dialog operations
  selectDirectory: async () => {
    return await ipcRenderer.invoke('dialog:select-directory');
  },
  
  // Shell operations
  openExternal: async (url) => {
    return await ipcRenderer.invoke('shell:open-external', url);
  },
  
  // App operations
  getVersion: async () => {
    return await ipcRenderer.invoke('app:get-version');
  },
  
  openLogFolder: async () => {
    return await ipcRenderer.invoke('app:open-log-folder');
  },
  
  getPlatform: () => {
    return process.platform; // 'darwin', 'win32', 'linux'
  },
  
  closeWindow: async () => {
    return await ipcRenderer.invoke('window:close');
  },
  
  // Directory operations
  selectDirectory: async (options) => {
    return await ipcRenderer.invoke('app:select-directory', options);
  },
  
  createDirectory: async (path) => {
    return await ipcRenderer.invoke('app:create-directory', path);
  },
  
  validateGameDirectory: async (path) => {
    return await ipcRenderer.invoke('app:validate-game-directory', path);
  },
  
  // 跨視窗事件通訊
  events: {
    /**
     * 發送事件到其他視窗
     * @param {string} eventName - 事件名稱
     * @param {object} data - 事件資料
     */
    send: (eventName, data) => {
      ipcRenderer.send('app:broadcast-event', eventName, data);
    },
    
    /**
     * 監聽來自其他視窗的事件
     * @param {string} eventName - 事件名稱
     * @param {function} callback - 回調函數
     */
    on: (eventName, callback) => {
      ipcRenderer.on(`app:event:${eventName}`, (event, data) => callback(data));
    },
    
    /**
     * 移除事件監聽
     * @param {string} eventName - 事件名稱
     */
    off: (eventName) => {
      ipcRenderer.removeAllListeners(`app:event:${eventName}`);
    }
  },
  
  /**
   * 顯示訊息對話框
   * @param {object} options - 對話框選項 { type, title, message, buttons }
   * @returns {Promise<{response: number}>}
   */
  showMessageBox: async (options) => {
    return await ipcRenderer.invoke('dialog:show-message-box', options);
  }
});

console.log('[Preload] API exposed to renderer');

