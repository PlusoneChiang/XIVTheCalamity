const { contextBridge, ipcRenderer } = require('electron');

// Expose protected APIs to renderer process
contextBridge.exposeInMainWorld('electronAPI', {
  // System info
  platform: process.platform,
  
  // Backend API communication
  backend: {
    call: (endpoint, data) => ipcRenderer.invoke('backend:call', endpoint, data)
  },
  
  // File storage API
  storage: {
    save: (filename, data) => ipcRenderer.invoke('storage:save', filename, data),
    load: (filename) => ipcRenderer.invoke('storage:load', filename),
    delete: (filename) => ipcRenderer.invoke('storage:delete', filename)
  },
  
  // Event listeners
  on: (channel, callback) => {
    ipcRenderer.on(channel, (event, ...args) => callback(...args));
  },
  
  removeListener: (channel, callback) => {
    ipcRenderer.removeListener(channel, callback);
  }
});

console.log('Preload script loaded');
