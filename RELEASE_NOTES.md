# Release Notes

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
