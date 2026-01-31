# Auto-Update System Implementation Plan

## Overview

實現 XIVTheCalamity 的全方位自動更新功能，涵蓋：
- **應用程式更新** - 使用 electron-updater + GitHub Releases
- **遊戲更新** - 已完成 (Phase 7)
- **Dalamud 更新** - 已完成
- **Wine 更新** - 待評估

## Configuration

| 設定 | 值 |
|------|-----|
| GitHub Owner | PlusoneChiang |
| GitHub Repo | XIVTheCalamity |
| 更新行為 | 自動檢查 + 提示下載安裝 |
| 更新套件 | electron-updater |

## Platform Support

| 平台 | 打包格式 | 自動更新 | 簽名需求 |
|------|---------|---------|---------|
| macOS | DMG + ZIP | ✅ | Apple Developer (公證用) |
| Windows | NSIS (.exe) | ✅ | Code Signing (避免 SmartScreen) |
| Linux | AppImage | ✅ | 無需簽名 |

---

## Phase 10.1: 應用程式自動更新

### Step 1: 安裝依賴

```bash
cd frontend
npm install electron-updater
```

### Step 2: 修改 package.json

```json
{
  "build": {
    "appId": "com.xivthecalamity.launcher",
    "productName": "XIVTheCalamity",
    "artifactName": "${productName}-${version}-${os}-${arch}.${ext}",
    
    "publish": {
      "provider": "github",
      "owner": "PlusoneChiang",
      "repo": "XIVTheCalamity"
    },
    
    "mac": {
      "category": "public.app-category.games",
      "target": [
        {
          "target": "dmg",
          "arch": ["arm64", "x64"]
        },
        {
          "target": "zip",
          "arch": ["arm64", "x64"]
        }
      ],
      "icon": "build/XIVTC.icon",
      "hardenedRuntime": true,
      "gatekeeperAssess": false
    },
    
    "win": {
      "target": [
        {
          "target": "nsis",
          "arch": ["x64"]
        }
      ],
      "icon": "build/icon.ico"
    },
    
    "linux": {
      "target": [
        {
          "target": "AppImage",
          "arch": ["x64"]
        }
      ],
      "icon": "build/icons",
      "category": "Game"
    },
    
    "nsis": {
      "oneClick": false,
      "allowToChangeInstallationDirectory": true,
      "createDesktopShortcut": true,
      "createStartMenuShortcut": true
    }
  }
}
```

### Step 3: 建立 Updater 模組

建立 `frontend/src/main/updater.js`:

```javascript
const { autoUpdater } = require('electron-updater');
const { app, dialog, BrowserWindow } = require('electron');
const log = require('electron-log');

// 配置日誌
autoUpdater.logger = log;
autoUpdater.logger.transports.file.level = 'info';

// 禁用自動下載，讓用戶確認
autoUpdater.autoDownload = false;
autoUpdater.autoInstallOnAppQuit = true;

class AppUpdater {
  constructor() {
    this.mainWindow = null;
    this.updateAvailable = false;
    this.updateDownloaded = false;
    this.downloadProgress = 0;
  }

  setMainWindow(window) {
    this.mainWindow = window;
  }

  // 初始化更新檢查
  async checkForUpdates() {
    if (process.env.NODE_ENV === 'development') {
      log.info('Skipping update check in development mode');
      return;
    }

    try {
      await autoUpdater.checkForUpdates();
    } catch (error) {
      log.error('Update check failed:', error);
    }
  }

  // 開始下載更新
  async downloadUpdate() {
    try {
      await autoUpdater.downloadUpdate();
    } catch (error) {
      log.error('Update download failed:', error);
    }
  }

  // 安裝更新並重啟
  quitAndInstall() {
    autoUpdater.quitAndInstall(false, true);
  }

  // 設定事件監聽
  setupEventListeners() {
    // 檢查更新中
    autoUpdater.on('checking-for-update', () => {
      log.info('Checking for updates...');
      this.sendToRenderer('update-checking');
    });

    // 有可用更新
    autoUpdater.on('update-available', (info) => {
      log.info('Update available:', info.version);
      this.updateAvailable = true;
      this.sendToRenderer('update-available', {
        version: info.version,
        releaseNotes: info.releaseNotes,
        releaseDate: info.releaseDate
      });
    });

    // 無更新
    autoUpdater.on('update-not-available', (info) => {
      log.info('No updates available. Current version:', info.version);
      this.sendToRenderer('update-not-available', { version: info.version });
    });

    // 下載進度
    autoUpdater.on('download-progress', (progress) => {
      this.downloadProgress = progress.percent;
      this.sendToRenderer('update-download-progress', {
        percent: progress.percent,
        bytesPerSecond: progress.bytesPerSecond,
        transferred: progress.transferred,
        total: progress.total
      });
    });

    // 下載完成
    autoUpdater.on('update-downloaded', (info) => {
      log.info('Update downloaded:', info.version);
      this.updateDownloaded = true;
      this.sendToRenderer('update-downloaded', {
        version: info.version,
        releaseNotes: info.releaseNotes
      });
    });

    // 錯誤處理
    autoUpdater.on('error', (error) => {
      log.error('Update error:', error);
      this.sendToRenderer('update-error', { message: error.message });
    });
  }

  // 發送事件到渲染進程
  sendToRenderer(channel, data = {}) {
    if (this.mainWindow && !this.mainWindow.isDestroyed()) {
      this.mainWindow.webContents.send(channel, data);
    }
  }
}

const appUpdater = new AppUpdater();
appUpdater.setupEventListeners();

module.exports = appUpdater;
```

### Step 4: 整合到 Main Process

修改 `frontend/src/main/index.js`:

```javascript
const appUpdater = require('./updater');
const { ipcMain } = require('electron');

// 設定主視窗
function createMainWindow() {
  const mainWindow = new BrowserWindow({...});
  appUpdater.setMainWindow(mainWindow);
  
  // 啟動時檢查更新 (延遲 3 秒)
  setTimeout(() => {
    appUpdater.checkForUpdates();
  }, 3000);
}

// IPC 處理
ipcMain.handle('updater:check', async () => {
  await appUpdater.checkForUpdates();
});

ipcMain.handle('updater:download', async () => {
  await appUpdater.downloadUpdate();
});

ipcMain.handle('updater:install', () => {
  appUpdater.quitAndInstall();
});
```

### Step 5: 前端 UI

建立更新通知組件 `frontend/src/renderer/components/update-modal.js`:

```javascript
class UpdateModal {
  constructor() {
    this.modal = null;
    this.setupListeners();
  }

  setupListeners() {
    window.electronAPI.on('update-available', (event, data) => {
      this.showUpdateAvailable(data);
    });

    window.electronAPI.on('update-download-progress', (event, data) => {
      this.updateProgress(data);
    });

    window.electronAPI.on('update-downloaded', (event, data) => {
      this.showUpdateReady(data);
    });

    window.electronAPI.on('update-error', (event, data) => {
      this.showError(data);
    });
  }

  showUpdateAvailable(data) {
    const html = `
      <div class="update-modal">
        <h3>🎉 新版本可用</h3>
        <p>版本 ${data.version} 已發布</p>
        <div class="release-notes">${data.releaseNotes || ''}</div>
        <div class="buttons">
          <button class="btn-primary" onclick="updateModal.download()">下載更新</button>
          <button class="btn-secondary" onclick="updateModal.close()">稍後提醒</button>
        </div>
      </div>
    `;
    this.show(html);
  }

  showUpdateReady(data) {
    const html = `
      <div class="update-modal">
        <h3>✅ 更新已就緒</h3>
        <p>版本 ${data.version} 已下載完成</p>
        <div class="buttons">
          <button class="btn-primary" onclick="updateModal.install()">立即重啟</button>
          <button class="btn-secondary" onclick="updateModal.close()">下次啟動時安裝</button>
        </div>
      </div>
    `;
    this.show(html);
  }

  updateProgress(data) {
    const progressBar = document.querySelector('.update-progress-bar');
    if (progressBar) {
      progressBar.style.width = `${data.percent}%`;
      progressBar.textContent = `${data.percent.toFixed(1)}%`;
    }
  }

  async download() {
    const html = `
      <div class="update-modal">
        <h3>⬇️ 下載中...</h3>
        <div class="progress-container">
          <div class="update-progress-bar" style="width: 0%">0%</div>
        </div>
      </div>
    `;
    this.show(html);
    await window.electronAPI.invoke('updater:download');
  }

  install() {
    window.electronAPI.invoke('updater:install');
  }

  show(html) {
    this.close();
    this.modal = document.createElement('div');
    this.modal.className = 'update-modal-overlay';
    this.modal.innerHTML = html;
    document.body.appendChild(this.modal);
  }

  close() {
    if (this.modal) {
      this.modal.remove();
      this.modal = null;
    }
  }

  showError(data) {
    console.error('Update error:', data.message);
  }
}

const updateModal = new UpdateModal();
```

### Step 6: Preload 橋接

修改 `frontend/src/preload/index.js`:

```javascript
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electronAPI', {
  // ... 現有 API ...
  
  // 更新相關
  invoke: (channel, ...args) => ipcRenderer.invoke(channel, ...args),
  on: (channel, callback) => {
    ipcRenderer.on(channel, callback);
    return () => ipcRenderer.removeListener(channel, callback);
  }
});
```

---

## Phase 10.2: CI/CD 整合 (GitHub Actions)

建立 `.github/workflows/release.yml`:

```yaml
name: Build and Release

on:
  push:
    tags:
      - 'v*'

jobs:
  build-mac:
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: '20'
          
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
          
      - name: Install dependencies
        run: |
          cd frontend
          npm ci
          
      - name: Build and Publish
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          # CSC_LINK: ${{ secrets.MAC_CERTIFICATE }}
          # CSC_KEY_PASSWORD: ${{ secrets.MAC_CERTIFICATE_PASSWORD }}
        run: |
          cd frontend
          npm run build
          npx electron-builder --mac --publish always

  build-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: '20'
          
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
          
      - name: Install dependencies
        run: |
          cd frontend
          npm ci
          
      - name: Build and Publish
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          # CSC_LINK: ${{ secrets.WIN_CERTIFICATE }}
          # CSC_KEY_PASSWORD: ${{ secrets.WIN_CERTIFICATE_PASSWORD }}
        run: |
          cd frontend
          npm run build
          npx electron-builder --win --publish always

  build-linux:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: '20'
          
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
          
      - name: Install dependencies
        run: |
          cd frontend
          npm ci
          
      - name: Build and Publish
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          cd frontend
          npm run build
          npx electron-builder --linux --publish always
```

---

## Phase 10.3: 版本發布流程

### 發布新版本步驟

1. **更新版本號**
   ```bash
   cd frontend
   # 修改 src/renderer/version.json
   npm run prebuild  # 同步版本到 package.json
   ```

2. **提交變更**
   ```bash
   git add .
   git commit -m "chore: bump version to x.x.x"
   ```

3. **建立 Tag**
   ```bash
   git tag v1.0.0
   git push origin main --tags
   ```

4. **GitHub Actions 自動執行**
   - 建置所有平台版本
   - 產生 `latest.yml`, `latest-mac.yml`, `latest-linux.yml`
   - 上傳到 GitHub Releases

5. **編輯 Release Notes**
   - 在 GitHub 上編輯自動建立的 Release
   - 添加變更說明

---

## Phase 10.4: Wine 更新 (待評估)

### 考量因素
- Wine 是大型 binary (~500MB+)
- 更新頻率較低
- 可能需要重新初始化 prefix

### 可能方案
1. **隨應用程式一起更新** - 簡單但增加下載大小
2. **獨立 Wine 更新** - 複雜但節省頻寬
3. **首次啟動時下載** - 減少初始安裝大小

### 建議
暫時採用方案 1，將 Wine 打包在應用程式中。未來可評估方案 2。

---

## Checklist

### Phase 10.1: 應用程式更新
- [ ] 安裝 electron-updater
- [ ] 修改 package.json (publish + 多平台 target)
- [ ] 建立 updater.js 模組
- [ ] 整合到 main process
- [ ] 建立前端 UI 組件
- [ ] 修改 preload 橋接
- [ ] 測試開發環境
- [ ] 測試生產環境

### Phase 10.2: CI/CD
- [ ] 建立 GitHub Actions workflow
- [ ] 測試 macOS 建置
- [ ] 測試 Windows 建置
- [ ] 測試 Linux 建置
- [ ] 設定 Code Signing (可選)

### Phase 10.3: 測試
- [ ] 測試版本檢查
- [ ] 測試下載進度
- [ ] 測試安裝重啟
- [ ] 測試跨版本升級

---

## Notes

### 開發測試
```javascript
// 強制在開發環境測試更新
autoUpdater.forceDevUpdateConfig = true;

// 或設定自定義 feed URL
autoUpdater.setFeedURL({
  provider: 'github',
  owner: 'PlusoneChiang',
  repo: 'XIVTheCalamity'
});
```

### 除錯
```bash
# 啟用詳細日誌
DEBUG=electron-updater npm start
```

### 私有 Repo
如果之後改為私有 repo，需設定 `GH_TOKEN` 環境變數：
```javascript
autoUpdater.setFeedURL({
  provider: 'github',
  owner: 'PlusoneChiang',
  repo: 'XIVTheCalamity',
  token: process.env.GH_TOKEN
});
```

---

## References

- [electron-updater 官方文檔](https://www.electron.build/auto-update.html)
- [Electron 更新指南](https://www.electronjs.org/docs/latest/tutorial/updates)
- [GitHub Actions Electron Builder](https://www.electron.build/multi-platform-build)
