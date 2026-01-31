/**
 * Internationalization (i18n) Module
 * Supports: Traditional Chinese (zh-TW), English (en-US)
 */

const translations = {
  'zh-TW': {
    // Application
    'app.title': 'XIV The Calamity',
    'app.subtitle': 'Final Fantasy XIV 跨平台啟動器',
    
    // Buttons
    'button.launch': '啟動遊戲',
    'button.settings': '設定',
    'button.cancel': '取消',
    'button.confirm': '確認',
    'button.save': '儲存',
    'button.apply': '套用',
    'button.ok': '確定',
    'button.close': '關閉',
    'button.select': '選擇',
    'button.create': '建立',
    
    // Account
    'account.login': '登入',
    'account.logout': '登出',
    'account.username': '使用者名稱',
    'account.password': '密碼',
    'account.otp': '一次性密碼',
    'account.remember': '記住帳號',
    'account.management': '帳號管理',
    
    // Game
    'game.launching': '正在啟動遊戲...',
    'game.running': '遊戲執行中',
    'game.stopped': '遊戲已停止',
    
    // Update
    'update.checking': '檢查更新中...',
    'update.available': '有可用的更新',
    'update.downloading': '下載中...',
    'update.installing': '安裝中...',
    'update.completed': '更新完成',
    
    // Settings
    'settings.title': '設定',
    'settings.save_success': '設定已儲存',
    'settings.save_failed': '儲存失敗',
    'settings.load_failed': '載入設定失敗',
    'settings.applying': '正在套用設定...',
    'settings.apply_wine_failed': 'Wine 設定套用失敗',
    
    // Settings tabs
    'settings.tab.general': '一般',
    'settings.tab.wine': 'Wine',
    'settings.tab.dalamud': 'Dalamud',
    'settings.tab.about': '關於',
    
    // General settings
    'settings.general.basic': '基本設定',
    'settings.general.language': '語言',
    'settings.general.language_help': '選擇介面語言',
    'settings.general.debug': 'Debug 記錄',
    'settings.general.debug_help': '啟用詳細記錄以便除錯',
    'settings.general.open_log': '開啟記錄資料夾',
    'settings.general.game': '遊戲設定',
    'settings.general.game_path': '遊戲路徑',
    'settings.general.game_path_help': '指定遊戲安裝目錄',
    'settings.general.browse': '瀏覽',
    'settings.general.account': '帳號設定',
    'settings.general.remember_password': '記住密碼',
    'settings.general.remember_password_help': '儲存加密後的密碼',
    
    // Wine settings
    'settings.wine.graphics': '圖形設定',
    'settings.wine.dxmt': '啟用 DXMT',
    'settings.wine.dxmt_help': 'macOS Metal 轉譯層，停用時自動切換 DXVK',
    'settings.wine.metalfx': '啟用 MetalFX',
    'settings.wine.metalfx_help': 'Apple 升頻技術（需要 DXMT）',
    'settings.wine.metalfx_factor': 'MetalFX 倍率',
    'settings.wine.metalfx_factor_help': '超解析倍率 (1x-4x，整數倍)',
    'settings.wine.hud': 'HUD 顯示',
    'settings.wine.hud_help': '顯示效能監控資訊',
    'settings.wine.hud_scale': 'HUD 大小',
    'settings.wine.native_resolution': '原生解析度',
    'settings.wine.native_resolution_help': '使用 Retina 原生解析度（畫質較高但效能需求較高）',
    'settings.wine.max_framerate': '最高幀率',
    'settings.wine.max_framerate_help': '限制最高 FPS (30-240)',
    'settings.wine.audio': '音訊設定',
    'settings.wine.audio_routing': '音訊路由',
    'settings.wine.audio_routing_help': '改善音訊相容性',
    'settings.wine.advanced': '進階設定',
    'settings.wine.esync': 'Esync',
    'settings.wine.esync_help': '事件同步機制',
    'settings.wine.fsync': 'Fsync',
    'settings.wine.fsync_help': '快速同步機制 (Linux)',
    'settings.wine.msync': 'Msync',
    'settings.wine.msync_help': 'macOS 同步機制',
    'settings.wine.wine_debug': 'Wine Debug 參數',
    'settings.wine.wine_debug_help': 'Wine 除錯參數 (例如: -all,+module，留空停用)',
    'settings.wine.keyboard_mapping': '按鍵映射',
    'settings.wine.left_option': '左側 Option 鍵',
    'settings.wine.right_option': '右側 Option 鍵',
    'settings.wine.left_command': '左側 Command 鍵',
    'settings.wine.right_command': '右側 Command 鍵',
    'settings.wine.key_as_alt': '映射為 Alt',
    'settings.wine.key_as_option': '保持 Option',
    'settings.wine.key_as_ctrl': '映射為 Ctrl',
    'settings.wine.key_as_alt_key': '映射為 Alt',
    'settings.wine.tools': 'Wine 工具',
    'settings.wine.open_winecfg': '開啟 Winecfg',
    'settings.wine.open_regedit': '開啟登錄編輯器',
    'settings.wine.open_cmd': '開啟命令列',
    'settings.wine.tool_failed': '無法開啟 {tool}',
    'settings.wine.launching_tool': '正在啟動 Wine 工具...',
    
    // Dalamud settings
    'settings.dalamud.basic': '基本設定',
    'settings.dalamud.enable': '啟用 Dalamud',
    'settings.dalamud.enable_help': '載入遊戲插件框架',
    'settings.dalamud.version': 'Dalamud 版本',
    'settings.dalamud.path': 'Dalamud 路徑',
    'settings.dalamud.path_help': '固定於 Application Support 目錄',
    'settings.dalamud.injection': '注入設定',
    'settings.dalamud.inject_delay': '注入延遲',
    'settings.dalamud.inject_delay_help': '遊戲啟動後等待時間（毫秒）',
    'settings.dalamud.advanced_settings': '進階設定',
    'settings.dalamud.safe_mode': '安全模式',
    'settings.dalamud.safe_mode_help': '停用所有第三方插件',
    'settings.dalamud.plugin_repo': '插件倉庫 URL',
    'settings.dalamud.plugin_repo_help': '自訂插件來源',
    'settings.dalamud.test_section': '測試功能',
    'settings.dalamud.test_launch': '測試啟動遊戲',
    'settings.dalamud.test_launch_help': '不需登入即可測試遊戲啟動，無法連線至伺服器',
    'settings.dalamud.test_launching': '啟動中...',
    'settings.dalamud.test_success': '測試完成',
    'settings.dalamud.test_failed': '啟動失敗',
    'settings.dalamud.game_running': '遊戲執行中...',
    'settings.dalamud.game_exited': '遊戲已結束',
    'settings.dalamud.abnormal_exit': '遊戲異常結束，Exit Code: {{code}}',
    
    // Settings general
    'settings.applied': '設定已套用',
    
    // About page
    'settings.about.app_info': '應用程式資訊',
    'settings.about.version': '版本',
    'settings.about.tech_info': '技術資訊',
    'settings.about.electron': 'Electron',
    'settings.about.dotnet': '.NET',
    'settings.about.wine': 'Wine',
    'settings.about.platform': '平台',
    'settings.about.links': '連結',
    'settings.about.github': 'GitHub 儲存庫',
    'settings.about.license': '授權條款',
    'settings.about.third_party': '第三方授權',
    'settings.about.show_license': '顯示授權條款',
    'settings.about.show_third_party': '顯示第三方授權',
    
    // Messages
    'message.welcome': '歡迎來到艾歐澤亞',
    'message.error': '發生錯誤',
    'message.success': '操作成功',
    
    // Login page
    'login.email': '帳號 (Email)',
    'login.password': '密碼',
    'login.otp': 'OTP 驗證碼',
    'login.otp.placeholder': '000000',
    'login.button': '登入遊戲',
    'login.loading': '登入中...',
    'login.success': '登入成功！',
    'login.autoFillOTP': '自動填入 OTP',
    'login.rememberPassword': '記住帳號密碼',
    'login.system_error': '系統錯誤：無法連接到後端。請重新啟動應用程式。',
    'login.invalid_email': '電子郵件格式錯誤',
    'login.password_too_short': '密碼長度不足',
    'login.invalid_otp': 'OTP 必須為 6 位數字',
    'login.no_session': '沒有有效的登入 Session',
    'login.launching': '啟動中...',
    'login.wine_launched': 'Wine 配置已啟動',
    'login.launch_failed': '啟動失敗: {message}',
    'login.launch_error': '啟動遊戲時發生錯誤',
    'login.wine_config_failed': '無法啟動 Wine 配置',
    'login.game_launched': '遊戲已啟動',
    'login.game_running': '遊戲執行中',
    'login.game_exit_abnormal': '遊戲異常結束 (Exit Code: {exitCode})',
    'login.game_exit_warning_title': '遊戲異常結束',
    'login.preparing': '準備中...',
    'login.checking_updates': '檢查遊戲更新...',
    'login.downloading_patches': '更新檔下載中',
    'login.patch_progress': '更新檔 {current}/{total}',
    'login.download_progress': '{downloaded}/{total} MB',
    'login.download_speed': '{speed} MB/s',
    'login.time_remaining': '剩餘時間: {time}',
    'login.update_complete': '更新完成',
    'login.updating': '更新',
    'login.game_updating': '遊戲更新中',
    'login.patch_progress': '更新檔',
    'login.threads': '線程',
    'login.remaining': '剩餘',
    'login.downloading_short': '下載',
    'login.installing_short': '安裝',
    'login.remaining_short': '剩',
    
    // Dalamud update
    'login.dalamud_checking': '檢查 Dalamud 更新...',
    'login.dalamud_downloading': '下載 Dalamud...',
    'login.dalamud_extracting': '解壓縮 Dalamud...',
    'login.dalamud_runtime': '下載 .NET Runtime...',
    'login.dalamud_assets': '下載 Dalamud 資源...',
    'login.dalamud_verifying': '驗證資源檔案...',
    'login.dalamud_complete': 'Dalamud 更新完成',
    'login.dalamud_failed': 'Dalamud 更新失敗',
    
    'login.sub_crystal': '水晶訂閱',
    'login.sub_credit': '信用卡訂閱',
    'login.sub_unknown': '未知',
    'login.game_setup.title': '遊戲目錄設定',
    'login.game_setup.description': '首次使用需要設定遊戲目錄，請選擇您的情況',
    'login.game_setup.existing': '我已有遊戲',
    'login.game_setup.install': '安裝新遊戲',
    'login.game_setup.select_existing': '選擇現有遊戲目錄',
    'login.game_setup.select_install': '選擇遊戲安裝位置',
    'login.game_setup.error_select': '無法選擇目錄',
    'login.game_setup.error_invalid': '無效的遊戲目錄：{reason}',
    'login.game_setup.error_create': '無法創建遊戲目錄',
    'login.game_setup.error_general': '設定遊戲目錄時發生錯誤',
    'login.game_setup.validation.not_exist': '目錄不存在',
    'login.game_setup.validation.missing_subdirs': '缺少必要的子目錄 (game, boot)',
    'login.time_less_1h': '不足 1 小時',
    'login.days': '天',
    'login.hours': '小時',
    'login.sub_type': '訂閱方式：',
    'login.remain_time': '剩餘時間：',
    'login.init_env': '正在初始化環境...',
    'login.init_complete': '✓ 環境初始化完成',
    'login.init_failed': '初始化失敗: {message}',
    'login.env_init_failed': '環境初始化失敗',
    'login.backend_disconnected': '後端連線中斷，請檢查後端服務狀態',
    'login.backend_failed': '後端連線失敗',
    'login.connection_failed': '無法建立連線',
    
    // Progress messages
    'progress.checking': '正在檢查 Wine Prefix...',
    'progress.creating_prefix': '正在建立 Wine Prefix...',
    'progress.installing_fonts': '正在安裝字型...',
    'progress.setting_locale': '正在設定語系...',
    'progress.configuring_media': '正在配置 MediaFoundation...',
    'progress.complete': '初始化完成',
    'progress.already_initialized': 'Wine 已初始化',
    'progress.skip_windows': '無需初始化',
    
    // Error messages
    'error.auth_failed': '帳號或密碼錯誤',
    'error.invalid_otp': 'OTP 驗證碼錯誤',
    'error.captcha_failed': '人機驗證失敗，請重試',
    'error.network_error': '網路連線錯誤',
    'error.invalid_request': '請求格式錯誤',
    'error.server_unavailable': '無法連線到伺服器，請檢查網路連線',
    'error.unknown': '發生未知錯誤',
    'error.init_failed': '初始化失敗：{message}',
    'error.init_exception': '初始化異常：{message}',
    
    // API Error codes - Authentication
    'error.auth_invalid_credentials': '帳號或密碼錯誤',
    'error.auth_invalid_otp': 'OTP 格式錯誤',
    'error.auth_session_expired': '登入階段已過期，請重新登入',
    
    // API Error codes - Game
    'error.game_path_not_configured': '尚未設定遊戲路徑',
    'error.game_path_invalid': '遊戲路徑無效',
    'error.game_launch_failed': '遊戲啟動失敗',
    'error.game_already_running': '遊戲已在執行中',
    
    // API Error codes - Update
    'error.update_check_failed': '檢查更新失敗',
    'error.update_download_failed': '下載更新失敗',
    'error.update_install_failed': '安裝更新失敗',
    
    // API Error codes - Config
    'error.config_load_failed': '載入設定失敗',
    'error.config_save_failed': '儲存設定失敗',
    'error.config_invalid': '設定格式錯誤',
    
    // API Error codes - Wine
    'error.wine_init_failed': 'Wine 初始化失敗',
    'error.wine_config_failed': 'Wine 設定失敗',
    'error.wine_tool_launch_failed': '無法啟動 Wine 工具',
    
    // API Error codes - Dalamud
    'error.dalamud_update_failed': 'Dalamud 更新失敗',
    'error.dalamud_install_failed': 'Dalamud 安裝失敗',
    'error.dalamud_inject_failed': 'Dalamud 注入失敗',
    
    // API Error codes - Environment
    'error.env_init_failed': '環境初始化失敗',
    
    // API Error codes - System
    'error.internal_error': '系統內部錯誤',
    'error.validation_failed': '資料驗證失敗',
    'error.service_unavailable': '服務暫時無法使用'
  },
  
  'en-US': {
    // Application
    'app.title': 'XIV The Calamity',
    'app.subtitle': 'Final Fantasy XIV Cross-Platform Launcher',
    
    // Buttons
    'button.launch': 'Launch Game',
    'button.settings': 'Settings',
    'button.cancel': 'Cancel',
    'button.confirm': 'Confirm',
    'button.save': 'Save',
    'button.apply': 'Apply',
    'button.ok': 'OK',
    'button.close': 'Close',
    'button.select': 'Select',
    'button.create': 'Create',
    
    // Account
    'account.login': 'Login',
    'account.logout': 'Logout',
    'account.username': 'Username',
    'account.password': 'Password',
    'account.otp': 'One-Time Password',
    'account.remember': 'Remember Account',
    'account.management': 'Account Management',
    
    // Game
    'game.launching': 'Launching game...',
    'game.running': 'Game is running',
    'game.stopped': 'Game stopped',
    
    // Update
    'update.checking': 'Checking for updates...',
    'update.available': 'Update available',
    'update.downloading': 'Downloading...',
    'update.installing': 'Installing...',
    'update.completed': 'Update completed',
    
    // Settings
    'settings.title': 'Settings',
    'settings.save_success': 'Settings saved',
    'settings.save_failed': 'Failed to save',
    'settings.load_failed': 'Failed to load settings',
    'settings.applying': 'Applying settings...',
    'settings.apply_wine_failed': 'Failed to apply Wine settings',
    
    // Settings tabs
    'settings.tab.general': 'General',
    'settings.tab.wine': 'Wine',
    'settings.tab.dalamud': 'Dalamud',
    'settings.tab.about': 'About',
    
    // General settings
    'settings.general.basic': 'Basic Settings',
    'settings.general.language': 'Language',
    'settings.general.language_help': 'Select interface language',
    'settings.general.debug': 'Debug Logging',
    'settings.general.debug_help': 'Enable verbose logging for troubleshooting',
    'settings.general.open_log': 'Open Log Folder',
    'settings.general.game': 'Game Settings',
    'settings.general.game_path': 'Game Path',
    'settings.general.game_path_help': 'Specify game installation directory',
    'settings.general.browse': 'Browse',
    'settings.general.account': 'Account Settings',
    'settings.general.remember_password': 'Remember Password',
    'settings.general.remember_password_help': 'Store encrypted password',
    
    // Wine settings
    'settings.wine.graphics': 'Graphics',
    'settings.wine.dxmt': 'Enable DXMT',
    'settings.wine.dxmt_help': 'macOS Metal translation layer, auto-switches to DXVK when disabled',
    'settings.wine.metalfx': 'Enable MetalFX',
    'settings.wine.metalfx_help': 'Apple upscaling technology (requires DXMT)',
    'settings.wine.metalfx_factor': 'MetalFX Factor',
    'settings.wine.metalfx_factor_help': 'Upscaling factor (1x-4x, integer multiples)',
    'settings.wine.hud': 'HUD Display',
    'settings.wine.hud_help': 'Show performance monitoring overlay',
    'settings.wine.hud_scale': 'HUD Scale',
    'settings.wine.native_resolution': 'Native Resolution',
    'settings.wine.native_resolution_help': 'Use Retina native resolution (higher quality but more demanding)',
    'settings.wine.max_framerate': 'Max Framerate',
    'settings.wine.max_framerate_help': 'Limit maximum FPS (30-240)',
    'settings.wine.audio': 'Audio',
    'settings.wine.audio_routing': 'Audio Routing',
    'settings.wine.audio_routing_help': 'Improve audio compatibility',
    'settings.wine.advanced': 'Advanced',
    'settings.wine.esync': 'Esync',
    'settings.wine.esync_help': 'Event synchronization',
    'settings.wine.fsync': 'Fsync',
    'settings.wine.fsync_help': 'Fast synchronization (Linux)',
    'settings.wine.msync': 'Msync',
    'settings.wine.msync_help': 'macOS synchronization',
    'settings.wine.wine_debug': 'Wine Debug Parameters',
    'settings.wine.wine_debug_help': 'Wine debug parameters (e.g., -all,+module, leave empty to disable)',
    'settings.wine.keyboard_mapping': 'Keyboard Mapping',
    'settings.wine.left_option': 'Left Option Key',
    'settings.wine.right_option': 'Right Option Key',
    'settings.wine.left_command': 'Left Command Key',
    'settings.wine.right_command': 'Right Command Key',
    'settings.wine.key_as_alt': 'Map as Alt',
    'settings.wine.key_as_option': 'Keep as Option',
    'settings.wine.key_as_ctrl': 'Map as Ctrl',
    'settings.wine.key_as_alt_key': 'Map as Alt',
    'settings.wine.tools': 'Wine Tools',
    'settings.wine.open_winecfg': 'Open Winecfg',
    'settings.wine.open_regedit': 'Open Registry Editor',
    'settings.wine.open_cmd': 'Open Command Prompt',
    'settings.wine.tool_failed': 'Failed to open {tool}',
    'settings.wine.launching_tool': 'Launching Wine tool...',
    
    // Dalamud settings
    'settings.dalamud.basic': 'Basic Settings',
    'settings.dalamud.enable': 'Enable Dalamud',
    'settings.dalamud.enable_help': 'Load game plugin framework',
    'settings.dalamud.version': 'Dalamud Version',
    'settings.dalamud.path': 'Dalamud Path',
    'settings.dalamud.path_help': 'Fixed in Application Support directory',
    'settings.dalamud.injection': 'Injection Settings',
    'settings.dalamud.inject_delay': 'Inject Delay',
    'settings.dalamud.inject_delay_help': 'Wait time after game starts (milliseconds)',
    'settings.dalamud.advanced_settings': 'Advanced Settings',
    'settings.dalamud.safe_mode': 'Safe Mode',
    'settings.dalamud.safe_mode_help': 'Disable all third-party plugins',
    'settings.dalamud.plugin_repo': 'Plugin Repository URL',
    'settings.dalamud.plugin_repo_help': 'Custom plugin source',
    'settings.dalamud.test_section': 'Test Features',
    'settings.dalamud.test_launch': 'Test Launch Game',
    'settings.dalamud.test_launch_help': 'Test game launch without login, cannot connect to server',
    'settings.dalamud.test_launching': 'Launching...',
    'settings.dalamud.test_success': 'Test Complete',
    'settings.dalamud.test_failed': 'Launch Failed',
    'settings.dalamud.game_running': 'Game Running...',
    'settings.dalamud.game_exited': 'Game Exited',
    'settings.dalamud.abnormal_exit': 'Game exited abnormally, Exit Code: {{code}}',
    
    // Settings general
    'settings.applied': 'Settings applied',
    
    // About page
    'settings.about.app_info': 'Application Information',
    'settings.about.version': 'Version',
    'settings.about.tech_info': 'Technical Information',
    'settings.about.electron': 'Electron',
    'settings.about.dotnet': '.NET',
    'settings.about.wine': 'Wine',
    'settings.about.platform': 'Platform',
    'settings.about.links': 'Links',
    'settings.about.github': 'GitHub Repository',
    'settings.about.license': 'License',
    'settings.about.third_party': 'Third Party Licenses',
    'settings.about.show_license': 'Show License',
    'settings.about.show_third_party': 'Show Third Party Licenses',
    
    // Messages
    'message.welcome': 'Welcome to Eorzea',
    'message.error': 'Error occurred',
    'message.success': 'Operation successful',
    
    // Login page
    'login.email': 'Account (Email)',
    'login.password': 'Password',
    'login.otp': 'OTP Code',
    'login.otp.placeholder': '000000',
    'login.button': 'Login to Game',
    'login.loading': 'Logging in...',
    'login.success': 'Login Successful!',
    'login.autoFillOTP': 'Auto-fill OTP',
    'login.rememberPassword': 'Remember Account & Password',
    'login.system_error': 'System error: Cannot connect to backend. Please restart the application.',
    'login.invalid_email': 'Invalid email format',
    'login.password_too_short': 'Password too short',
    'login.invalid_otp': 'OTP must be 6 digits',
    'login.no_session': 'No valid login session',
    'login.launching': 'Launching...',
    'login.wine_launched': 'Wine config launched',
    'login.launch_failed': 'Launch failed: {message}',
    'login.launch_error': 'Error launching game',
    'login.wine_config_failed': 'Cannot launch Wine config',
    'login.game_launched': 'Game launched',
    'login.game_running': 'Game running',
    'login.game_exit_abnormal': 'Game exited abnormally (Exit Code: {exitCode})',
    'login.game_exit_warning_title': 'Game Exited Abnormally',
    'login.preparing': 'Preparing...',
    'login.checking_updates': 'Checking for game updates...',
    'login.downloading_patches': 'Downloading patches',
    'login.patch_progress': 'Patch {current}/{total}',
    'login.download_progress': '{downloaded}/{total} MB',
    'login.download_speed': '{speed} MB/s',
    'login.time_remaining': 'Time remaining: {time}',
    'login.update_complete': 'Update complete',
    'login.updating': 'Update',
    'login.game_updating': 'Updating Game',
    'login.patch_progress': 'Patches',
    'login.threads': 'Threads',
    'login.remaining': 'ETA',
    'login.downloading_short': 'DL',
    'login.installing_short': 'Inst',
    'login.remaining_short': 'ETA',
    
    // Dalamud update
    'login.dalamud_checking': 'Checking Dalamud updates...',
    'login.dalamud_downloading': 'Downloading Dalamud...',
    'login.dalamud_extracting': 'Extracting Dalamud...',
    'login.dalamud_runtime': 'Downloading .NET Runtime...',
    'login.dalamud_assets': 'Downloading Dalamud assets...',
    'login.dalamud_verifying': 'Verifying assets...',
    'login.dalamud_complete': 'Dalamud update complete',
    'login.dalamud_failed': 'Dalamud update failed',
    
    'login.sub_crystal': 'Crystal Subscription',
    'login.sub_credit': 'Credit Card Subscription',
    'login.sub_unknown': 'Unknown',
    'login.time_less_1h': 'Less than 1 hour',
    'login.days': 'days',
    'login.hours': 'hours',
    'login.sub_type': 'Subscription Type: ',
    'login.remain_time': 'Remaining Time: ',
    'login.init_env': 'Initializing environment...',
    'login.init_complete': '✓ Environment initialized',
    'login.init_failed': 'Initialization failed: {message}',
    'login.env_init_failed': 'Environment initialization failed',
    'login.backend_disconnected': 'Backend disconnected, please check backend service',
    'login.backend_failed': 'Backend connection failed',
    'login.connection_failed': 'Cannot establish connection',
    'login.game_setup.title': 'Game Directory Setup',
    'login.game_setup.description': 'First-time setup requires game directory configuration',
    'login.game_setup.existing': 'I have existing game',
    'login.game_setup.install': 'Install new game',
    'login.game_setup.select_existing': 'Select existing game directory',
    'login.game_setup.select_install': 'Select game installation location',
    'login.game_setup.error_select': 'Failed to select directory',
    'login.game_setup.error_invalid': 'Invalid game directory: {reason}',
    'login.game_setup.error_create': 'Failed to create game directory',
    'login.game_setup.error_general': 'Error occurred during setup',
    'login.game_setup.validation.not_exist': 'Directory does not exist',
    'login.game_setup.validation.missing_subdirs': 'Missing required subdirectories (game, boot)',
    
    // Progress messages
    'progress.checking': 'Checking Wine Prefix...',
    'progress.creating_prefix': 'Creating Wine Prefix...',
    'progress.installing_fonts': 'Installing fonts...',
    'progress.setting_locale': 'Setting locale...',
    'progress.configuring_media': 'Configuring MediaFoundation...',
    'progress.complete': 'Initialization complete',
    'progress.already_initialized': 'Wine already initialized',
    'progress.skip_windows': 'No initialization needed',
    
    // Error messages
    'error.auth_failed': 'Invalid username or password',
    'error.invalid_otp': 'Invalid OTP code',
    'error.captcha_failed': 'CAPTCHA verification failed, please retry',
    'error.network_error': 'Network error',
    'error.invalid_request': 'Invalid request format',
    'error.server_unavailable': 'Cannot connect to server, please check network',
    'error.unknown': 'Unknown error occurred',
    'error.init_failed': 'Initialization failed: {message}',
    'error.init_exception': 'Initialization exception: {message}',
    
    // API Error codes - Authentication
    'error.auth_invalid_credentials': 'Invalid username or password',
    'error.auth_invalid_otp': 'Invalid OTP format',
    'error.auth_session_expired': 'Session expired, please login again',
    
    // API Error codes - Game
    'error.game_path_not_configured': 'Game path not configured',
    'error.game_path_invalid': 'Invalid game path',
    'error.game_launch_failed': 'Failed to launch game',
    'error.game_already_running': 'Game is already running',
    
    // API Error codes - Update
    'error.update_check_failed': 'Failed to check for updates',
    'error.update_download_failed': 'Failed to download update',
    'error.update_install_failed': 'Failed to install update',
    
    // API Error codes - Config
    'error.config_load_failed': 'Failed to load configuration',
    'error.config_save_failed': 'Failed to save configuration',
    'error.config_invalid': 'Invalid configuration',
    
    // API Error codes - Wine
    'error.wine_init_failed': 'Wine initialization failed',
    'error.wine_config_failed': 'Wine configuration failed',
    'error.wine_tool_launch_failed': 'Failed to launch Wine tool',
    
    // API Error codes - Dalamud
    'error.dalamud_update_failed': 'Dalamud update failed',
    'error.dalamud_install_failed': 'Dalamud installation failed',
    'error.dalamud_inject_failed': 'Dalamud injection failed',
    
    // API Error codes - Environment
    'error.env_init_failed': 'Environment initialization failed',
    
    // API Error codes - System
    'error.internal_error': 'Internal server error',
    'error.validation_failed': 'Validation failed',
    'error.service_unavailable': 'Service temporarily unavailable'
  }
};

class I18n {
  constructor() {
    // Get saved locale or default to Traditional Chinese
    this.locale = localStorage.getItem('locale') || 'zh-TW';
    this.translations = translations;
  }
  
  /**
   * Translate a key to current locale
   * @param {string} key - Translation key
   * @param {Object} params - Optional parameters for interpolation
   * @returns {string} Translated string
   */
  t(key, params = {}) {
    let text = this.translations[this.locale]?.[key] || key;
    
    // Simple parameter interpolation
    Object.keys(params).forEach(param => {
      text = text.replace(`{${param}}`, params[param]);
    });
    
    return text;
  }
  
  /**
   * Set current locale
   * @param {string} locale - Locale code (zh-TW or en-US)
   */
  setLocale(locale) {
    if (!this.translations[locale]) {
      console.warn(`Locale ${locale} not supported, falling back to zh-TW`);
      locale = 'zh-TW';
    }
    
    this.locale = locale;
    localStorage.setItem('locale', locale);
    
    // Update all elements with data-i18n attribute
    this.updateElements();
    
    // Dispatch event for manual listeners
    window.dispatchEvent(new CustomEvent('locale-changed', { 
      detail: { locale } 
    }));
  }
  
  /**
   * Get current locale
   * @returns {string} Current locale code
   */
  getLocale() {
    return this.locale;
  }
  
  /**
   * Update all elements with data-i18n attribute
   */
  updateElements() {
    document.querySelectorAll('[data-i18n]').forEach(element => {
      const key = element.getAttribute('data-i18n');
      const translated = this.t(key);
      
      // Update text content or specific attribute
      const attr = element.getAttribute('data-i18n-attr');
      if (attr) {
        element.setAttribute(attr, translated);
      } else {
        element.textContent = translated;
      }
    });
  }
  
  /**
   * Initialize i18n for the page
   */
  init() {
    // Update all translatable elements on page load
    this.updateElements();
    
    // Watch for dynamically added elements
    const observer = new MutationObserver((mutations) => {
      mutations.forEach((mutation) => {
        mutation.addedNodes.forEach((node) => {
          if (node.nodeType === 1 && node.hasAttribute('data-i18n')) {
            const key = node.getAttribute('data-i18n');
            const translated = this.t(key);
            node.textContent = translated;
          }
        });
      });
    });
    
    observer.observe(document.body, {
      childList: true,
      subtree: true
    });
  }
}

// Export singleton instance
const i18n = new I18n();

export default i18n;
