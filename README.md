# XIV The Calamity

<div align="center">

**Final Fantasy XIV 跨平台登入器**

![Version](https://img.shields.io/badge/version-2.1.0-blue)
![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Linux%20%7C%20Windows-lightgrey)
![Status](https://img.shields.io/badge/status-Release-brightgreen)
![License](https://img.shields.io/badge/license-GPL--3.0-green)

[功能特色](#功能特色) • [技術架構](#技術架構) • [安裝與執行](#安裝與執行) • [開發指南](#開發指南) • [贊助支持](#-贊助支持) • [授權條款](#授權條款)

</div>

---

## 📖 專案簡介

**XIV The Calamity** 是一個開源的《Final Fantasy XIV》跨平台遊戲登入器，靈感來自以下專案：

- **[XIV on Mac (XoM)](https://github.com/marzent/XIV-on-Mac)**
- **[XIVLauncher](https://github.com/goatcorp/FFXIVQuickLauncher)**
- **[XIVLauncher.Core](https://github.com/goatcorp/XIVLauncher.Core)**

本專案採用 **Photino.NET** 作為跨平台桌面 UI 框架，搭配 **.NET 9 (NativeAOT)** 後端，並將前端靜態資源直接內建打包為內嵌資源（Resource），實現了高安全性、極低資源佔用與高速啟動的跨平台架構設計。

### 🎯 設計目標

- ✅ **全平台支援**：macOS (Apple Silicon)、Linux (x86_64) 與 Windows (x64)
- ✅ **輕量高效**：擺脫傳統 Electron 的龐大記憶體開銷，開機即啟動
- ✅ **資安防禦**：前端資源封裝於二進位檔內，防止檔案被改寫或篡改
- ✅ **現代化介面**：使用 Web 技術打造流暢使用者體驗
- ✅ **開源透明**：所有程式碼公開，歡迎社群貢獻

**支援平台**：
- ✅ macOS (Apple Silicon) - 穩定運行 (Wine Crossover + DXMT)
- ✅ Linux (x86_64) - 穩定運行 (GE-Proton 11 + AppImage)
- ✅ Windows (x64) - 原生支援 (DirectX)

---

## ✨ 功能特色

### 已實作功能

- 🎮 **遊戲啟動與環境配置**
  - macOS: Wine Crossover + DXMT (DirectX → Metal)
  - Linux: GE-Proton 11 + DXVK (DirectX → Vulkan)
  - Windows: 原生 DirectX 執行
  - 自動化 Wine / Proton 環境配置與初始化
  - 支援 Dalamud 插件框架

- 👥 **多帳號與多設定檔管理**
  - 新增多設定檔（Profiles）獨立配置功能
  - 支援遊戲帳號與特定設定檔關聯綁定
  - 快速切換多個遊戲帳號與獨立設定
  - AES-256-GCM 高強度加密儲存密碼與 OTP 密鑰
  - 支援 OTP 自動填入

- 🔄 **遊戲與程式自動更新**
  - 自動檢查與更新遊戲版本
  - 支援啟動器應用程式版本自動更新
  - 多執行緒並行下載與斷點續傳
  - 即時進度顯示（速度、剩餘時間、百分比）

- 🔌 **Dalamud 整合支援**
  - 整合 Dalamud 插件框架（跨平台自建版本）
  - 自動下載、安裝與跨平台版本管理
  - 優化 Dalamud 目錄規劃結構

- 🎨 **使用者體驗與主題**
  - 繁體中文 / 英文雙語介面
  - 多款視覺主題切換 (深色 / 淺色 / 節慶主題)
  - 詳細的日誌記錄與錯誤診斷訊息

- 🐧 **Linux 特性與自動化**
  - GE-Proton 11 自動下載與配置
  - DXVK/VKD3D 整合、GameMode 支援
  - 提供 AppImage 自動化打包

---

## 🏗️ 技術架構

### 技術棧

```
┌─────────────────────────────────────┐
│          Photino.NET (UI)           │
│    ├─ UI: HTML / CSS / JavaScript   │
│    ├─ Assets: Embedded Resources    │
│    └─ i18n: zh-TW, en-US            │
└─────────────────┬───────────────────┘
                  │ In-Process / REST API
┌─────────────────┴───────────────────┐
│     ASP.NET Core 9 (NativeAOT)      │
│    ├─ Runtime: .NET 9 NativeAOT     │
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

| 平台 | Wine / 相容層版本 | 圖形層 | 音訊路由 |
|------|-------------------|--------|----------|
| **macOS** | Wine Crossover (Wine 11) | DXMT (DX→Metal) | XTCAudioRouter |
| **Linux** | GE-Proton 11 | DXVK (DX→Vulkan) | PulseAudio / PipeWire |
| **Windows** | 原生 (Native) | 原生 DirectX | 原生 |

### 主要組件

| 組件 | 技術 | 用途 |
|------|------|------|
| **UI 視窗** | Photino.NET + HTML/CSS/JS | 輕量化跨平台桌面 UI 視窗 |
| **後端核心** | ASP.NET Core 9 NativeAOT | 遊戲邏輯、下載更新、平台與帳號管理 |
| **Wine (macOS)** | Wine Crossover (Fork) | macOS Wine 運行環境 |
| **Wine (Linux)** | GE-Proton 11 | Linux Wine 運行環境（自動下載 AppImage 包裝）|
| **音訊工具 (macOS)**| Swift (XTCAudioRouter) | macOS 音訊路由控制 |

### 專案架構

```
XIVTheCalamity/
├── frontend/              # 前端 UI (Vite / HTML / CSS / JS)
│   ├── src/               # UI 頁面與組件
│   └── scripts/           # 前端資源編譯與嵌入腳本
│
├── backend/               # .NET 9 NativeAOT 後端
│   └── src/
│       ├── XIVTheCalamity.Api.NativeAOT/ # NativeAOT REST API 主機
│       ├── XIVTheCalamity.Core/           # 核心模型與配置邏輯
│       ├── XIVTheCalamity.Game/           # 遊戲下載與 Patch 邏輯
│       ├── XIVTheCalamity.Dalamud/        # Dalamud 整合與更新
│       └── XIVTheCalamity.Platform/       # 跨平台 (macOS/Linux/Win) 特性
│
├── shared/                # 共用資源 (字型、圖標、預建二進位檔)
├── XTCAudioRouter/        # macOS 音訊路由工具 (Swift)
├── wine-builder/          # Wine 編譯工具 (macOS)
└── scripts/               # 跨平台自動化打包與建置腳本
```

---

## 🚀 安裝與執行

### 系統需求

#### macOS 使用者
- **作業系統**：macOS 12.0+ (Monterey 或更新)
- **架構**：Apple Silicon (arm64)
- **儲存空間**：約 100 GB（遊戲 + Wine + 登入器）

#### Linux 使用者
- **發行版**：Ubuntu 22.04+ / Fedora 38+ 或 SteamOS (Steam Deck)
- **架構**：x86_64 (AMD64)
- **依賴項**：
  - `webkit2gtk` / `libwebkit2gtk-4.0` / `libwebkit2gtk-4.1` (Photino.NET WebView 視窗渲染核心需求)
  - `libfuse2` (AppImage 需求)
  - PulseAudio / PipeWire (音訊)
  - Vulkan 驅動程式 (DXVK 需求)

> 💡 **依賴套件參考安裝指令**：
> ```bash
> # Ubuntu / Debian
> sudo apt update && sudo apt install -y libwebkit2gtk-4.1-0 libfuse2
> 
> # Fedora
> sudo dnf install -y webkit2gtk3 fuse-libs
> 
> # Arch Linux / Manjaro
> sudo pacman -S --needed webkit2gtk fuse2
> ```

#### Windows 使用者
- **作業系統**：Windows 10 / 11 64-bit
- **架構**：x64

---

## 👨‍💻 開發與建置指南

### 開發需求
- Node.js 20+
- .NET 9 SDK

### 快速建置 (無簽署開發版)

#### macOS
```bash
./scripts/build-mac-dev.sh
```

#### Linux
```bash
./scripts/build-linux.sh
```

#### Windows
```powershell
powershell ./scripts/build-windows.ps1
```

---

## 💖 贊助支持

如果您覺得這個專案對您有幫助，歡迎請作者喝一杯珍奶，支持本專案的持續開發與維護！

👉 **[Give me a Boba! (請我喝杯珍奶)](https://plusone.bobaboba.me)**

---

## 📜 授權條款

本專案採用 **GNU General Public License v3.0** 授權。詳細條款請參閱 [LICENSE](LICENSE) 檔案。

---

## ⚠️ 免責聲明

**本專案為非官方遊戲登入器，使用風險自負。**

- 「FINAL FANTASY」與「FINAL FANTASY XIV」為 Square Enix Holdings Co., Ltd. 之註冊商標。
- 本專案與 Square Enix 無任何官方關聯或贊助關係。

---

## 📊 開發路線圖

#### 已完成 ✅
- [x] Photino.NET 輕量化架構遷移（完全移除 Electron 依賴）
- [x] 前端靜態資源內嵌與防篡改封裝
- [x] 多帳號與 OTP 自動填入
- [x] 多設定檔（Profiles）獨立管理與帳號綁定功能
- [x] 全平台支援 (macOS Apple Silicon / Linux x86_64 / Windows x64)
- [x] 應用程式自動更新功能
- [x] Linux GE-Proton 11 升級與 AppImage 自動化發布
- [x] Dalamud 框架跨平台整合與目錄規劃

---

<div align="center">

**Made with ❤️ for FFXIV Taiwan Community**

</div>
