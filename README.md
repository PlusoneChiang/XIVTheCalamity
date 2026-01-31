# XIV The Calamity

<div align="center">

**Final Fantasy XIV 跨平台登入器**

![Version](https://img.shields.io/badge/version-0.1.0-blue)
![Platform](https://img.shields.io/badge/platform-macOS-lightgrey)
![Status](https://img.shields.io/badge/status-Alpha-orange)
![License](https://img.shields.io/badge/license-GPL--3.0-green)

[功能特色](#功能特色) • [技術架構](#技術架構) • [安裝與執行](#安裝與執行) • [專案結構](#專案結構) • [授權條款](#授權條款)

</div>

---

## 📖 專案簡介

**XIV The Calamity** 是一個開源的《Final Fantasy XIV》跨平台遊戲登入器，靈感來自以下專案：

- **[XIV on Mac (XoM)](https://github.com/marzent/XIV-on-Mac)**
- **[XIVLauncher](https://github.com/goatcorp/FFXIVQuickLauncher)**
- **[XIVTCLauncher](https://github.com/cycleapple/XIVTCLauncher)**

本專案採用 **Electron** 作為前端框架，搭配 **.NET 9** 後端，實現了跨平台架構設計。**目前專注於 macOS (Apple Silicon) 平台開發**，未來將逐步擴展到 Windows 與 Linux。

### 🎯 設計目標

- ✅ **跨平台架構**：前後端分離設計，為多平台支援做準備
- ✅ **現代化介面**：使用 Web 技術打造流暢的使用者體驗
- ✅ **易於擴展**：模組化架構，易於維護與功能擴充
- ✅ **開源透明**：所有程式碼公開，歡迎社群貢獻

**當前主要平台**：macOS (Apple Silicon)

---

## ✨ 功能特色

### 已實作功能

- 🎮 **遊戲啟動**
  - 支援 macOS (Apple Silicon)
  - Wine 環境自動配置與初始化
  - DirectX → Metal 轉換 (DXMT)
  - 整合 XTCAudioRouter 音訊路由

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
  - 版本管理

- 🎨 **使用者體驗**
  - 繁體中文 / 英文介面
  - 即時系統狀態監控
  - 詳細的錯誤訊息與記錄

### 🚧 規劃中功能

- 🌍 **更多平台** - Windows、Linux 支援

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

### 主要組件

| 組件 | 技術 | 用途 |
|------|------|------|
| **前端** | Electron 40 + JavaScript | 使用者介面與互動 |
| **後端** | ASP.NET Core 9 | 遊戲邏輯、更新管理、Wine 控制 |
| **通訊** | HTTP REST API | 前後端資料交換 |
| **Wine** | Wine Crossover 24.x (Fork) | Windows 遊戲相容層 |
| **音訊** | XTCAudioRouter | macOS 音訊路由 |
| **圖形** | DXMT | DirectX → Metal 轉換 |

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
├── wine/                 # Wine 執行環境 (Fork)
├── wine-builder/         # Wine 編譯工具 (Fork from winecx)
├── XTCAudioRouter/       # 音訊路由工具
└── scripts/              # 建置與打包腳本
```

---

## 🚀 安裝與執行

### 系統需求

#### 使用者

- **作業系統**：macOS 12.0+ (Monterey 或更新)
- **架構**：Apple Silicon (arm64)
- **儲存空間**：約 100 GB（遊戲 + Wine + 登入器）

#### 開發者

- **作業系統**：macOS 12.0+ (Monterey 或更新)
- **架構**：Apple Silicon (arm64)
- **開發工具**：
  - Node.js 18+
  - .NET 9 SDK
  - Xcode Command Line Tools

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

```bash
# 從專案根目錄執行
./scripts/quick-pack.sh

# 產出位置：Release/mac-arm64/XIVTheCalamity.app
```

### 安裝說明

**⚠️ 首次開啟注意事項**

由於本程式未使用付費的 Apple Developer ID 簽名，macOS 會阻止直接打開。

**安裝步驟**：
1. 下載並解壓縮 `XIVTheCalamity.app`
2. **右鍵**點擊 app → 選擇「打開」
3. 在隱私與安全性中，授予權限。
4. 在警告視窗中點擊「打開」按鈕
5. 之後可以正常雙擊開啟

---

## 📂 專案結構

### 核心目錄說明

| 目錄 | 說明 |
|------|------|
| `frontend/` | Electron 前端應用程式 |
| `backend/` | .NET 後端服務 |
| `shared/` | 前後端共用的資源檔案 |
| `wine/` | Wine 執行環境（Fork from Wine Crossover） |
| `wine-builder/` | Wine 編譯工具（Fork from winecx） |
| `XTCAudioRouter/` | macOS 音訊路由工具 |
| `scripts/` | 建置、測試、打包腳本 |

### 詳細架構文檔

相關技術文檔請參考專案內的文檔說明。

---

## 📜 授權條款

本專案採用 **GNU General Public License v3.0** 授權。

詳細條款請參閱 [LICENSE](LICENSE) 檔案。

### 第三方組件

本專案使用或修改了以下開源專案的程式碼：

- **[XIV on Mac](https://github.com/marzent/XIV-on-Mac)** - Wine 配置、字型設定
- **[Wine Crossover (winecx)](https://github.com/marzent/winecx)** - Windows 相容層（已 Fork 並修改）
- **GStreamer** - 多媒體框架
- **Electron** - 跨平台桌面框架
- **.NET** - 後端執行環境

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

**當前版本**：v1.0.1  
**主要平台**：macOS (Apple Silicon)  
**目標區域**：台、港、澳、星、馬  

### 開發路線圖

- [x] 基礎登入功能
- [x] Wine 環境自動配置
- [x] 遊戲版本檢查與更新
- [x] 多執行緒並行下載
- [x] Dalamud 框架整合
- [ ] Windows 平台支援
- [ ] Linux 平台支援
- [ ] 自更新

---

## 🙏 致謝

感謝以下專案與社群的啟發與支援：

- **[XIV on Mac (XoM)](https://github.com/marzent/XIV-on-Mac)**
- **[XIVLauncher](https://github.com/goatcorp/FFXIVQuickLauncher)**
- **[XIVTCLauncher](https://github.com/cycleapple/XIVTCLauncher)**
- **[Wine Crossover](https://github.com/marzent/winecx)**
- **Wine 社群** - 持續改善 Windows 相容性
- **FFXIV TC服社群** - 測試與回饋

---

<div align="center">

**Made with ❤️ for FFXIV Taiwan Community**

</div>
