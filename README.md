# XIV The Calamity

<div align="center">

**Final Fantasy XIV 跨平台登入器**

![Version](https://img.shields.io/badge/version-1.1.0-blue)
![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Linux-lightgrey)
![Status](https://img.shields.io/badge/status-Beta-orange)
![License](https://img.shields.io/badge/license-GPL--3.0-green)

[功能特色](#功能特色) • [技術架構](#技術架構) • [安裝與執行](#安裝與執行) • [開發指南](#開發指南) • [授權條款](#授權條款)

</div>

---

## 📖 專案簡介

**XIV The Calamity** 是一個開源的《Final Fantasy XIV》跨平台遊戲登入器，靈感來自以下專案：

- **[XIV on Mac (XoM)](https://github.com/marzent/XIV-on-Mac)**
- **[XIVLauncher](https://github.com/goatcorp/FFXIVQuickLauncher)**
- **[XIVLauncher.Core](https://github.com/goatcorp/XIVLauncher.Core)**

本專案採用 **Electron** 作為前端框架，搭配 **.NET 9** 後端，實現了跨平台架構設計。

### 🎯 設計目標

- ✅ **跨平台支援**：macOS (Apple Silicon) 與 Linux 平台
- ✅ **現代化介面**：使用 Web 技術打造流暢的使用者體驗
- ✅ **易於擴展**：模組化架構，易於維護與功能擴充
- ✅ **開源透明**：所有程式碼公開，歡迎社群貢獻

**支援平台**：
- ✅ macOS (Apple Silicon) - 穩定運行
- ✅ Linux (x86_64) - 測試中
- 🚧 Windows - 規劃中

---

## ✨ 功能特色

### 已實作功能

- 🎮 **遊戲啟動**
  - macOS: Wine Crossover + DXMT (DirectX → Metal)
  - Linux: GE-Proton + DXVK (DirectX → Vulkan)
  - 自動環境配置與初始化
  - 支援 Dalamud 插件框架

- 👥 **多帳號管理**
  - 快速切換多個遊戲帳號
  - AES-256-GCM 加密密碼儲存
  - 記住帳號設定與 OTP

- 🔄 **遊戲更新**
  - 自動檢查遊戲版本
  - 多執行緒並行下載
  - 即時進度顯示（速度、剩餘時間、百分比）
  - 斷點續傳支援

- 🔌 **Dalamud 支援**
  - 整合 Dalamud 插件框架
  - 自動下載與安裝
  - 跨平台版本管理

- 🎨 **使用者體驗**
  - 繁體中文 / 英文介面
  - OTP 自動填入
  - 多帳號快速切換
  - 詳細的錯誤訊息與記錄

- 🐧 **Linux 特性**
  - GE-Proton 自動下載與配置
  - DXVK/VKD3D 支援
  - GameMode 整合
  - Esync/Fsync 支援

### 🚧 規劃中功能

- 🪟 **Windows 平台** - 原生 Windows 支援
- 🔄 **自動更新** - 啟動器自我更新功能

---

## 🏗️ 技術架構

### 技術棧

```
┌─────────────────────────────────────┐
│         Electron (Frontend)         │
│    ├─ UI: HTML/CSS/JavaScript       │
│    ├─ Framework: Electron 40        │
│    └─ i18n: zh-TW, en-US            │
└─────────────────┬───────────────────┘
                  │ HTTP REST API
┌─────────────────┴───────────────────┐
│       ASP.NET Core (Backend)        │
│    ├─ Runtime: .NET 9               │
│    ├─ API: RESTful                  │
│    └─ Logging: Serilog              │
└─────────────────┬───────────────────┘
                  │
         ┌────────┴────────┐
         │                 │
    ┌────▼────┐      ┌────▼────┐
    │  Wine   │      │ Dalamud │
    │ Runtime │      │ Plugins │
    └─────────┘      └─────────┘
```

### 平台特定組件

| 平台 | Wine 版本 | 圖形層 | 音訊路由 |
|------|-----------|--------|----------|
| **macOS** | Wine Crossover 24.x | DXMT (DX→Metal) | XTCAudioRouter |
| **Linux** | GE-Proton (runtime) | DXVK (DX→Vulkan) | PulseAudio/PipeWire |
| **Windows** | Native | Native DirectX | Native |

### 主要組件

| 組件 | 技術 | 用途 |
|------|------|------|
| **前端** | Electron 40 + JavaScript | 使用者介面與互動 |
| **後端** | ASP.NET Core 9 | 遊戲邏輯、更新管理、平台控制 |
| **通訊** | HTTP REST API | 前後端資料交換 |
| **Wine (macOS)** | Wine Crossover (Fork) | macOS Wine 環境 |
| **Wine (Linux)** | GE-Proton | Linux Wine 環境（GE-Proton，運行時下載）|

### 專案架構

```
XIVTheCalamity/
├── frontend/              # Electron 前端
│   ├── src/
│   │   ├── main/         # 主程序（Electron）
│   │   └── renderer/     # 渲染程序（UI）
│   └── package.json
│
├── backend/              # .NET 後端
│   └── src/
│       ├── XIVTheCalamity.Api/          # Web API
│       ├── XIVTheCalamity.Core/         # 核心功能
│       ├── XIVTheCalamity.Game/         # 遊戲邏輯
│       ├── XIVTheCalamity.Dalamud/      # Dalamud 整合
│       └── XIVTheCalamity.Platform/     # 平台特定功能
│
├── shared/               # 共用資源
│   └── resources/        # 字型、圖標、DLL
│
├── wine-builder/         # Wine 編譯工具 (Fork from winecx)
├── XTCAudioRouter/       # 音訊路由工具
└── scripts/              # 建置與打包腳本
```

---

## 🚀 安裝與執行

### 系統需求

#### macOS 使用者

- **作業系統**：macOS 12.0+ (Monterey 或更新)
- **架構**：Apple Silicon (arm64)
- **儲存空間**：約 100 GB（遊戲 + Wine + 登入器）

#### Linux 使用者

- **發行版**：Ubuntu 22.04+ / Fedora 38+ 或其他主流發行版
- **架構**：x86_64 (AMD64)
- **儲存空間**：約 100 GB（遊戲 + 登入器，GE-Proton 自動下載）
- **依賴項**：
  - libfuse2 (AppImage 需要)
  - PulseAudio 或 PipeWire (音訊)
  - Vulkan 驅動程式 (DXVK 需要)

#### 開發者

**macOS**:
- macOS 12.0+ (Apple Silicon)
- Node.js 20+
- .NET 9 SDK
- Xcode Command Line Tools

**Linux**:
- Ubuntu 22.04+ 或 Fedora 38+
- Node.js 20+
- .NET 9 SDK
- 標準開發工具 (build-essential 或 gcc/g++)

### 開發環境設定

```bash
# 1. Clone 專案
git clone https://github.com/plusone-dev/XIVTheCalamity.git
cd XIVTheCalamity

# 2. 安裝前端依賴
cd frontend
npm install

# 3. 還原後端依賴
cd ../backend
dotnet restore

# 4. 建置後端
dotnet build
```

### 執行開發版本

**方式 A：手動啟動（開發除錯）**

```bash
# 終端機 1：啟動後端
cd backend
dotnet run --project src/XIVTheCalamity.Api

# 終端機 2：啟動前端
cd frontend
npm start
```

**方式 B：快速建置測試**

```bash
# 從專案根目錄執行
./scripts/build-and-test.sh
```

### 打包發布版本

#### macOS 版本
```bash
# 從專案根目錄執行
./scripts/mac-pack.sh

# 產出位置：Release/mac-arm64/XIVTheCalamity.app
```

#### Linux 版本

**在 Linux 上編譯（原生）**：
```bash
# 從專案根目錄執行
./scripts/build-linux.sh

# 產出位置：Release/XIVTheCalamity-*.AppImage
```

### 安裝說明

#### macOS 安裝

**⚠️ 首次開啟注意事項**

由於本程式未使用付費的 Apple Developer ID 簽名，macOS 會阻止直接打開。

**安裝步驟**：
1. 下載並解壓縮 `XIVTheCalamity.app`
2. **右鍵**點擊 app → 選擇「打開」
3. 在隱私與安全性中，授予權限。
4. 在警告視窗中點擊「打開」按鈕
5. 之後可以正常雙擊開啟

#### Linux 安裝

```bash
# 設置可執行權限
chmod +x XIVTheCalamity-1.1.0-linux-x86_64.AppImage

# 執行
./XIVTheCalamity-1.1.0-linux-x86_64.AppImage

# GE-Proton 會在首次運行時自動下載
```

**Steam Deck / 遊戲模式啟動**：

您可以直接將本啟動器的 AppImage 作為「非 Steam 遊戲」加入到 Steam 中。在 Steam OS 遊戲模式下即可直接啟動，不需要任何額外的啟動參數。

**首次啟動**：
- GE-Proton 自動下載和配置（需要網路連接）
- 預計需要 3-5 分鐘
- 下載進度會顯示在標題欄

---

## 👨‍💻 開發指南

### 版本管理

專案使用統一的版本號管理系統：

**版本來源**：`frontend/src/renderer/version.json`
```json
{
  "version": "1.1.0",
  "appName": "XIV The Calamity",
  "description": "Final Fantasy XIV Cross-Platform Launcher"
}
```

**修改版本號**：
```bash
# 1. 編輯 version.json
vim frontend/src/renderer/version.json

# 2. 建置時自動同步到 package.json
./scripts/build-linux.sh      # Linux
./scripts/build-mac-dev.sh    # macOS
```

**版本同步機制**：
- `scripts/sync-version.js` - 版本同步腳本
- `package.json` 的 `prebuild` hook 自動執行
- 確保 version.json 是唯一真實來源（Single Source of Truth）

### 建置腳本說明

| 腳本 | 平台 | 用途 |
|------|------|------|
| `build-mac-dev.sh` | macOS | macOS 開發版本建置 |
| `build-linux.sh` | Linux | Linux 原生建置 |
| `sync-version.js` | 通用 | 版本號同步工具 |


---

## 📂 專案結構

### 核心目錄說明

| 目錄 | 說明 |
|------|------|
| `frontend/` | Electron 前端應用程式 |
| `backend/` | .NET 後端服務 |
| `shared/` | 前後端共用的資源檔案 |
| `wine-builder/` | Wine 編譯工具（macOS，Fork from winecx）|
| `XTCAudioRouter/` | macOS 音訊路由工具 |
| `scripts/` | 建置、測試、打包腳本 |

### 平台差異

| 組件 | macOS | Linux |
|------|-------|-------|
| Wine | 本地編譯（wine/） | 運行時下載（GE-Proton）|
| 音訊路由 | XTCAudioRouter | PulseAudio/PipeWire |
| 圖形 API | DXMT (DX→Metal) | DXVK (DX→Vulkan) |

---

## 📜 授權條款

本專案採用 **GNU General Public License v3.0** 授權。

詳細條款請參閱 [LICENSE](LICENSE) 檔案。

### 第三方組件

本專案使用或修改了以下開源專案的程式碼：

- **[XIV on Mac](https://github.com/marzent/XIV-on-Mac)** - Wine 配置、字型設定
- **[XIVLauncher.Core](https://github.com/goatcorp/XIVLauncher.Core)** - Linux 平台參考實作
- **[Wine Crossover (winecx)](https://github.com/marzent/winecx)** - macOS Wine 相容層（已 Fork 並修改）

- **GStreamer** - 多媒體框架
- **Electron** - 跨平台桌面框架
- **.NET** - 後端執行環境
- **DXVK** - DirectX to Vulkan 轉換層（Linux）
- **DXMT** - DirectX to Metal 轉換層（macOS）

完整的第三方授權聲明請參閱 [NOTICE](NOTICE) 檔案。

---

## ⚠️ 免責聲明

**本專案為非官方的遊戲登入器，使用風險自負。**

- **商標聲明**：「FINAL FANTASY」與「FINAL FANTASY XIV」為 Square Enix Holdings Co., Ltd. 的註冊商標。
- **無關聯性**：本專案與 Square Enix Holdings Co., Ltd. 無任何關聯、背書或贊助關係。
- **使用風險**：使用第三方登入器可能違反遊戲服務條款，請自行評估風險。

---

## 🤝 貢獻

歡迎任何形式的貢獻！

- 🐛 回報問題：[GitHub Issues](https://github.com/plusone-dev/XIVTheCalamity/issues)
- 💡 功能建議：[GitHub Discussions](https://github.com/plusone-dev/XIVTheCalamity/discussions)

---

## 📊 開發狀態

**當前版本**：v1.1.0  
**支援平台**：macOS (Apple Silicon) / Linux (x86_64)  
**開發狀態**：Beta  

### 開發路線圖

#### 已完成 ✅
- [x] 基礎登入功能（多帳號、OTP 支援）
- [x] macOS Wine 環境自動配置
- [x] Linux GE-Proton 運行時下載
- [x] 遊戲版本檢查與更新
- [x] 多執行緒並行下載
- [x] Dalamud 框架整合（跨平台）
- [x] 跨平台建置系統
- [x] 版本管理自動化

#### 進行中 🚧
- [ ] Linux 平台穩定性測試
- [ ] 效能優化與記憶體管理
- [ ] 詳細的錯誤處理與恢復

#### 未來規劃 📋
- [ ] Windows 平台原生支援
- [ ] 啟動器自動更新功能
- [ ] 插件管理介面
- [ ] 更多語言支援

---

## 🙏 致謝

感謝以下專案與社群的啟發與支援：

- **[XIV on Mac (XoM)](https://github.com/marzent/XIV-on-Mac)** - macOS Wine 配置參考
- **[XIVLauncher](https://github.com/goatcorp/FFXIVQuickLauncher)** - Windows 登入器先驅
- **[XIVLauncher.Core](https://github.com/goatcorp/XIVLauncher.Core)** - Linux 跨平台實作
- **[Wine Crossover](https://github.com/marzent/winecx)** - macOS Wine 基礎

- **Wine 社群** - 持續改善 Windows 相容性
- **FFXIV 台服社群** - 測試與回饋

---

<div align="center">

**Made with ❤️ for FFXIV Taiwan Community**

</div>
