# Release Notes

## v2.1.3
#### 🇹🇼 zh-TW
- 修正 macOS 過場動畫播放問題。
- 修正 Windows 更新權限問題。
- 修正更新說明無法顯示。
- Wine Runtime 更新至 `v2026.08.05`。

#### 🇺🇸 English
- Fixed cutscene playback on macOS.
- Fixed Windows update permissions.
- Fixed missing update notes.
- Updated the Wine Runtime to `v2026.08.05`.

## v2.1.2
#### 🇹🇼 zh-TW
### 🐛 錯誤修正
- **macOS**：修復 `d3dcompiler_47.dll` Not Found問題。
- **Dalamud**：新增多重預設主庫功能。 

#### 🇺🇸 English
### 🐛 Bug Fixes & Features
- **macOS**: Fixed `d3dcompiler_47.dll` Not Found issue.
- **Dalamud**: Added support for multiple default plugin repositories.

## v2.1.0
#### 🇹🇼 zh-TW
### 🚀 核心架構
- **輕量化重構**：移除 Electron 依賴，全面遷移至 Photino.NET 架構，大幅降低記憶體佔用與啟動延遲。
- **資安防護**：前端靜態資源改為內建 Resource 檔案打包，防止檔案被意外修改或篡改。

### ✨ 新功能
- **多設定檔支援**：新增多設定檔（Profiles）管理功能。
- **帳號綁定**：支援遊戲帳號與特定設定檔綁定，方便多帳號快速切換。
- **Linux 升級**：Linux 環境升級支援 GE-Proton 11，並實現 AppImage 自動化打包發布。

### 🐛 錯誤修正
- **macOS**：修正 App Bundle 相對路徑讀取與執行錯誤。
- **Linux**：修正版本檢查、更新邏輯與執行相容性問題。
- **Windows**：修復部分環境下的遊戲路徑初始化及設定檔寫入問題。
- **介面修正**：修復前端彈出視窗與元件互動體驗問題。

#### 🇺🇸 English
### 🚀 Core Architecture
- **Lightweight Refactor**: Replaced Electron with Photino.NET, significantly reducing memory footprint and startup time.
- **Security & Integrity**: Embedded frontend assets directly as built-in resources to prevent tampering.

### ✨ New Features
- **Multi-Profile Support**: Added management for multiple user profiles.
- **Account Binding**: Added feature to bind game accounts to specific profiles for quick switching.
- **Linux Improvements**: Upgraded Linux runtime to GE-Proton 11 with automated AppImage build packaging.

### 🐛 Bug Fixes
- **macOS**: Fixed App Bundle relative path resolution issue.
- **Linux**: Fixed version check and update compatibility issues.
- **Windows**: Fixed game path initialization and configuration issues on certain environments.
- **UI**: Fixed modal dialog rendering and interactive frontend issues.

## v1.9.4
#### 🇹🇼 zh-TW
### 🐛 修復
- 設定頁面版面錯誤

#### 🇺🇸 English
### 🐛 Bug Fixes
- Setting page display error

## v1.9.3
#### 🇹🇼 zh-TW
### 🔧 調整與優化
- Mac版 - 升級 Winecx 到 Wine 11 版本
- Mac版 - 移除輸入法定位點設定功能 (Wine 11 已不支援)
- Mac版 - 移除 DXVK 並改為原生 DXMT，並升級 DXMT 到 v0.80
- Mac版 - 簡化音訊路由 (Audio Routing) 實作，提升相容性
- Linux版 - 調整沙盒環境檢測方式
- Linux版 - 修正環境變數注入、啟動參數注入方式
- 改善中國服 (陸服) Dalamud 套件相容性
- 修正前端，導入現代化架構加快載入速度
- 使用前端套件取代原先自行開發的模組，強化相容性與安全性
- 後端重構，強化可維護性

#### 🇺🇸 English
### 🔧 Changes & Optimizations
- Mac - Upgraded Winecx to Wine 11.
- Mac - Removed IME candidate window positioning settings (no longer supported in Wine 11).
- Mac - Removed DXVK and switched to native DXMT, and upgraded DXMT to v0.80.
- Mac - Simplified audio routing implementation for better compatibility.
- Linux - Adjusted sandbox environment detection method.
- Linux - Fixed environment variables and launch options injection methods.
- Improved compatibility for Chinese-server Dalamud plugins.
- Refactored frontend and introduced a modernized architecture to speed up load times.
- Replaced self-developed frontend features with external packages to enhance compatibility and security.
- Refactored the backend to improve maintainability.


## v1.8.8
#### 🇹🇼 zh-TW
### ✨ 新功能 (Mac版本)
- 新增 Wine 設定選項：`UseHomeAlias`（UI：**啟用Home目錄相容模式**）。
- 啟用後會在 `/tmp` 自動建立 Home alias，並於啟動時套用到 Wine 執行環境路徑。

### 🔧 調整 (Mac版本)
- 強化 Home 路徑一致性處理，降低特殊使用者目錄名稱（例如尾端 `.`）造成的路徑映射問題。

#### 🇺🇸 English
### ✨ New Features (Mac Version)
- Added a new Wine setting: `UseHomeAlias` (UI: **Enable Home directory compatibility mode**).
- When enabled, the launcher automatically creates a Home alias under `/tmp` and applies it to Wine runtime paths.

### 🔧 Changes (Mac Version)
- Improved Home path consistency handling to reduce path mapping issues caused by special user directory names (e.g., trailing `.`).

## v1.8.7
#### 🇹🇼 zh-TW
### 🐛 修正 (Mac版本)
- 修正M5使用者啟動失敗問題。
- 修正錯誤訊息無法正確傳遞問題。
- 更新winecx版本為v2026.04.25，補上開發者簽章。

#### 🇺🇸 English
### 🐛 Bug Fixes (Mac Version)
- Fixed startup failure for M5 users.
- Fixed error messages not being passed correctly.
- Updated winecx to v2026.04.25 with Developer ID code signatures applied.

## v1.8.6
#### 🇹🇼 zh-TW
### 🔧 調整 (Mac版本)
- Discord Rich Presence功能，改為使用我自行開發的橋接器-xbridge
- 移除啟動器自帶的DC活動通知功能，改為遊戲內橋接器

#### 🇺🇸 English
### 🔧 Changes (Mac Version)
- Discord Rich Presence now uses a self-developed bridge — xbridge.
- Removed the launcher's built-in Discord activity notification; activity is now handled entirely by the in-game bridge.

## v1.8.5
#### 🇹🇼 zh-TW
### 🔧 調整 (Mac版本)
- 新增Discord Rich Presence IPC串接功能
- 啟動器DRP，無需任何套件與啟用遊戲內橋接設定
- 遊戲內如果要提供更豐富的DRP動態，需啟用遊戲內橋接，並啟用Dalamud，以及安裝Dalamud.RichPresence套件

#### 🇺🇸 English
### 🔧 Changes (Mac Version)
- Added Discord Rich Presence IPC integration.
- Launcher DRP now works without any extra package installation or enabling in-game bridge settings.
- For richer in-game DRP updates, enable the in-game bridge, enable Dalamud, and install the `Dalamud.RichPresence` plugin.

## v1.8.0
#### 🇹🇼 zh-TW
### ✨ 新功能
- 新增啟動器主題切換功能。
- 提供三種主題，深色(預設值)、淺色以及復刻戀人節主題配色。

#### 🇺🇸 English
### ✨ New Features
- Added launcher theme switching support.
- Three themes available: Dark (default), Light, and a recreated Valentine's Day theme.

## v1.7.5
#### 🇹🇼 zh-TW
### 🔧 調整 
- 免費試玩功能已啟用

### ✨ 重要調整
- 老公，我要去當兵了，再見！
- 愚人節快樂！

#### 🇺🇸 English
### 🔧 Changes
- Free trial feature enabled

### ✨ Important Changes
- Honey, I'm off to the military, goodbye!
- Happy April Fools' Day!

## v1.7.4
#### 🇹🇼 zh-TW
### 🐛 Mac修復
- WineCX版本更新至v2026.03.30
- 修正IME按鍵處理流程，避免IME功能被wine錯誤攔截而出現問題

#### 🇺🇸 English
### 🐛 Mac Bug Fixes
- Updated WineCX to v2026.03.30
- Fixed IME key handling to prevent Wine from incorrectly intercepting IME input


## v1.7.3
#### 🇹🇼 zh-TW
### 🐛 修復
- 修復Dalamud TC分支問題。
- 修復Dalamud在inject模式時，環境變數未正確設定問題。

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed Dalamud TC branch issues.
- Fixed environment variables not being set correctly when using Dalamud inject mode.

## v1.7.2
### 🐛 修復
- 修復Dalamud國際服分支相容性問題，目前最新版本為12.0.1.5 TC build 13(12.0.1.5-tc.13)
- 修復本地化相容性問題

### ✨ 新功能
- Linux Proton-GE 新增啟動選項實驗性功能。

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed Dalamud global server branch compatibility; latest version is now 12.0.1.5 TC build 13 (12.0.1.5-tc.13)
- Fixed localization compatibility issues

### ✨ New Features
- Added experimental Launch Options support for Linux Proton-GE

## v1.7.1
#### 🇹🇼 zh-TW
### 🐛 修復
- 改回Dalamud CN版API 12分支，暫緩國際服版本相容性問題。

#### 🇺🇸 English
### 🐛 Bug Fixes
- Reverted to Dalamud CN API branch 12 to temporarily defer global server compatibility issues.

## v1.7.0

#### 🇹🇼 zh-TW
### 🐛 修復
- Linux桌面環境輸入法無法使用問題
- Linux啟用輸入法時，頻繁出現吃鍵問題
- Steam虛擬鍵盤按鍵操作穿透問題

### 🔧 調整
- Dalamud改為自編譯版本
- Dalamud注入模式改為預設EntryPoint
- Linux執行環境換為Proton-GE

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed IME input not working in Linux desktop environments
- Fixed frequent key input being swallowed when IME is enabled on Linux
- Fixed Steam virtual keyboard key presses passing through to the game

### 🔧 Changes
- Dalamud switched to a custom-built version
- Dalamud injection mode changed to EntryPoint by default
- Linux runtime switched to Proton-GE

## v1.6.14

#### 🇹🇼 zh-TW
### 🐛 修復
- 修正 macOS Wine 環境下輸入法確認文字後，方向鍵失效的問題
- 修正候選字視窗在未設定自訂座標時，定位異常的問題

### 🔧 調整
- Wine 更新至 v2026.03.14

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed arrow keys not working after committing IME text in macOS Wine
- Fixed candidate window positioning when custom coordinates are not set

### 🔧 Changes
- Updated Wine to v2026.03.14

## v1.6.13

#### 🇹🇼 zh-TW
### 🐛 修復
- 修正 macOS Wine 環境下無法使用 Ctrl+V 貼上的問題
- 修正 macOS Wine 環境下輸入法按鍵卡住的問題
- 修正 Wine 版本升級時未自動重建 wineprefix 的問題

### ✨ 新功能
- 新增 Wine 輸入法候選字視窗自訂座標設定
- Wine 更新至 v2026.03.13，改善 CJK 輸入法相容性

### 🎨 介面調整
- 春季櫻花粉主題 🌸

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed Ctrl+V paste not working in macOS Wine environment
- Fixed IME key input getting stuck in macOS Wine environment
- Fixed wineprefix not being rebuilt on Wine version upgrade

### ✨ New Features
- Added custom position settings for Wine IME candidate window
- Updated Wine to v2026.03.13 with improved CJK IME compatibility

### 🎨 UI Changes
- Spring sakura pink theme 🌸

## v1.6.12

#### 🇹🇼 zh-TW
### 🐛 修復
- 修正 Patch 安裝順序錯誤可能導致遊戲損壞的問題
- 新增 Patch 下載後 SHA1 雜湊驗證，失敗自動重試
- 新增安裝完成後版本檔案備份 (.ver → .bck)

### 🔧 調整
- Mac的wine字體改為更紗黑體，確保CJK字元相容性。

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed patch install order causing potential game corruption
- Added SHA1 hash verification after patch download with auto-retry
- Added version file backup (.ver → .bck) after patching

### 🔧 Changes
- Switched Mac Wine font to Sarasa Gothic for better CJK character compatibility.

## v1.6.11

#### 🇹🇼 zh-TW
### 🐛 修復
- 修正 macOS/Linux Wine 下載進度條不正確的問題
- 修正初始化檢查同時執行導致進度混亂的問題
- 環境未就緒時禁止啟動遊戲

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed incorrect Wine download progress on macOS/Linux
- Fixed initialization checks running concurrently causing progress issues
- Blocked game launch until environment setup is complete

## v1.6.10

#### 🇹🇼 zh-TW
### 🐛 修復
- 登入後啟動按鈕可能卡住的問題
- 無更新內容時顯示資訊不正確問題

### ✨ 新功能
- 每小時背景檢查程式更新

### 🎨 介面調整
- 關閉節慶遮罩，調整主體配色

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed launch button getting stuck after login
- Fixed incorrect info displayed when no update notes available

### ✨ New Features
- Background update check every hour

### 🎨 UI Changes
- Disabled holiday mask overlay, adjusted main color scheme

## v1.6.9

#### 🇹🇼 zh-TW
### 🐛 修復
- SteamOS的DXVK錯誤

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed DXVK error on SteamOS

## v1.6.8

#### 🇹🇼 zh-TW
### 🎨 改善
- 主題變更，新年快樂！
- i18n顯示同步問題。
- 隱藏reCAPTCHA浮動元件。

### 🐛 修復
- 修正開發模式行為錯誤問題。
- 修正Steam Gaming Mode自動更新卡死問題。
- 修正Release Note顯示錯誤問題。

#### 🇺🇸 English
### 🎨 Improvements
- Theme update, Happy New Year!
- Fixed i18n display sync issue.
- Hidden reCAPTCHA floating widget.

### 🐛 Bug Fixes
- Fixed development mode behavior error.
- Fixed auto-update freeze in Steam Gaming Mode.
- Fixed release notes display error.

## v1.6.2

#### 🇹🇼 zh-TW
### 🎨 改善
- 修正Release Note顯示方式
- 修正i18n顯示問題

#### 🇺🇸 English
### 🎨 Improvements
- Fixed release notes display
- Fixed i18n display issues

## v1.6.1

#### 🇹🇼 zh-TW
### 🎨 改善
- Linux執行檔圖示修正。

#### 🇺🇸 English
### 🎨 Improvements
- Fixed Linux executable icon.

## v1.6.0

#### 🇹🇼 zh-TW
### 🐛 修復
- 修正語系切換問題
- 修正i18n顯示問題
- 修正設定檔同步問題

### 🎨 改善
- 春節主題
- 新年快樂！

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed language switching issue
- Fixed i18n display issue
- Fixed config sync issue

### 🎨 Improvements
- Spring Festival theme
- Happy New Year!

## v1.5.9

#### 🇹🇼 zh-TW
### 🐛 修復
- 修正 Windows 平台 Dalamud 載入錯誤

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed Windows Dalamud loading error

## v1.5.8

#### 🇹🇼 zh-TW
### 🎨 改善
- 版本更新調整

#### 🇺🇸 English
### 🎨 Improvements
- Version update adjustments

## v1.5.7

#### 🇹🇼 zh-TW
### 🎨 改善
- 版本更新調整

#### 🇺🇸 English
### 🎨 Improvements
- Version update adjustments

## v1.5.6

#### 🇹🇼 zh-TW
### 🎨 改善
- 自動更新測試版本

#### 🇺🇸 English
### 🎨 Improvements
- Auto-update test release

## v1.5.5

#### 🇹🇼 zh-TW
### 🆕 新功能
- 新增應用程式自動更新功能
- 新增 Dalamud 插件框架支援

#### 🇺🇸 English
### 🆕 New Features
- Added application auto-update
- Added Dalamud plugin framework support

## v1.5.2

#### 🇹🇼 zh-TW
### 🐛 修復
- 修復更新問題
- 修復 Windows 平台沒正常檢查更新問題

### 🆕 新功能
- 新增登入後 Session 連接時間統計

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed update issues
- Fixed Windows update check not working

### 🆕 New Features
- Added session connection time tracking after login

## v1.5.1

#### 🇹🇼 zh-TW
### 🆕 新功能
- 新增 Windows 環境支援

### 🐛 修復
- 修正顯示與部份已知問題

#### 🇺🇸 English
### 🆕 New Features
- Added Windows platform support

### 🐛 Bug Fixes
- Fixed display and known issues

## v1.2.1

#### 🇹🇼 zh-TW
### 🆕 新功能
- 完成 Linux 平台支援 (x86_64)

### 🐛 修復
- 修正部分問題，部份程式重構

#### 🇺🇸 English
### 🆕 New Features
- Added Linux platform support (x86_64)

### 🐛 Bug Fixes
- Fixed issues, partial codebase refactoring
