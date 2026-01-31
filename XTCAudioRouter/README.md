# XTCAudioRouter

macOS 音訊路由 CLI 工具，用於監控系統音訊裝置變更並同步更新 Wine 音訊設定。

## 功能

- 監控 macOS 預設音訊輸出裝置變更
- 自動更新 Wine Registry 中的音訊裝置設定
- 觸發 Wine 重新掃描音訊裝置
- 監控遊戲程序，遊戲結束時自動退出

## 建置

```bash
# Debug 建置
./build.sh

# Release 建置
./build.sh release
```

## 使用方式

```bash
./XTCAudioRouter --pid <遊戲PID> --wineprefix <Wine前綴路徑> --wine <Wine執行檔路徑>
```

### 參數

| 參數 | 說明 |
|------|------|
| `--pid` | 遊戲程序的 PID |
| `--wineprefix` | Wine prefix 目錄路徑 |
| `--wine` | Wine 執行檔路徑（wine64） |

### 範例

```bash
# Bundle 環境
./XTCAudioRouter \
  --pid 12345 \
  --wineprefix ~/Library/Application\ Support/XIVTheCalamity/wineprefix \
  --wine /Applications/XIVTheCalamity.app/Contents/Resources/wine/bin/wine64

# 開發環境
./XTCAudioRouter \
  --pid 12345 \
  --wineprefix ~/Library/Application\ Support/XIVTheCalamity/wineprefix \
  --wine /path/to/project/wine/bin/wine64
```

## 整合至 App Bundle

建置後的執行檔會被複製到 App Bundle 的 Resources 目錄：

```
XIVTheCalamity.app/
└── Contents/
    └── Resources/
        └── XTCAudioRouter
```

## 技術細節

### 音訊裝置監控

使用 CoreAudio API 監聽系統預設輸出裝置變更：
- `AudioObjectAddPropertyListener` 監聽裝置變更事件
- 變更時取得新裝置的 UID

### Wine Registry 操作

- **寫入**：使用 `wine reg add` 命令（可靠）
- **讀取**：直接解析 `user.reg` 檔案（快速）

### Registry 路徑

```
HKEY_CURRENT_USER\Software\Wine\Drivers\winecoreaudio.drv
  - DefaultOutput: 預設輸出裝置
  - RescanDevices: 觸發重新掃描

HKEY_CURRENT_USER\Software\Wine\Drivers\winecoreaudio.drv\devices\0,<CoreAudioUID>
  - guid: 裝置 GUID (REG_BINARY)
```

## 授權

與 XIV The Calamity 主專案相同授權。
