# Release Notes


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
