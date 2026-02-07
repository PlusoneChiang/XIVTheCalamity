import i18n from './i18n/index.js';
import { initializeEnvironment, launchWineConfig } from './utils/environment.js';

console.log('XIVTheCalamity Launcher - Renderer Process Started');

let isEnvironmentInitialized = false;
let isInitializing = false;

// Initialize i18n
document.addEventListener('DOMContentLoaded', () => {
  // Initialize i18n system
  i18n.init();
  
  // Language switcher buttons
  const langZhBtn = document.getElementById('lang-zh-tw');
  const langEnBtn = document.getElementById('lang-en-us');
  
  // Set initial active state
  const currentLocale = i18n.getLocale();
  if (currentLocale === 'zh-TW') {
    langZhBtn.classList.add('active');
    langEnBtn.classList.remove('active');
  } else {
    langEnBtn.classList.add('active');
    langZhBtn.classList.remove('active');
  }
  
  // Language switch handlers
  langZhBtn.addEventListener('click', () => {
    i18n.setLocale('zh-TW');
    langZhBtn.classList.add('active');
    langEnBtn.classList.remove('active');
  });
  
  langEnBtn.addEventListener('click', () => {
    i18n.setLocale('en-US');
    langEnBtn.classList.add('active');
    langZhBtn.classList.remove('active');
  });
  
  // Listen for locale changes
  window.addEventListener('locale-changed', (event) => {
    console.log('Locale changed to:', event.detail.locale);
  });
  
  // Get UI elements
  const launchBtn = document.getElementById('launch-btn');
  const settingsBtn = document.querySelector('.btn-secondary');
  const progressCard = document.getElementById('progress-card');
  const progressBarFill = document.getElementById('progress-bar-fill');
  const progressMessage = document.getElementById('progress-message');
  const statusMessage = document.getElementById('status-message');
  
  // Check if user is logged in
  const sessionId = localStorage.getItem('sessionId');
  if (!sessionId) {
    console.log('[Main] No session found, redirecting to login...');
    window.location.href = 'pages/login/index.html';
    return;
  }
  
  console.log('[Main] Session found, initializing environment...');
  
  // Disable launch button initially
  launchBtn.disabled = true;
  launchBtn.textContent = '準備中...';
  
  // Start environment initialization
  startEnvironmentInitialization();
  
  // Launch button handler
  launchBtn.addEventListener('click', async () => {
    console.log('Launch Game clicked');
    
    if (!isEnvironmentInitialized) {
      showStatus('環境尚未初始化完成', 'error');
      return;
    }
    
    // Launch Wine config for testing
    launchBtn.disabled = true;
    launchBtn.textContent = '啟動中...';
    
    try {
      const result = await launchWineConfig();
      if (result.success) {
        console.log('Wine config launched successfully');
        showStatus('Wine 配置已啟動', 'success');
      } else {
        console.error('Failed to launch Wine config:', result.message);
        showStatus(`啟動失敗: ${result.message}`, 'error');
      }
    } catch (err) {
      console.error('Launch error:', err);
      showStatus('啟動失敗', 'error');
    } finally {
      launchBtn.disabled = false;
      launchBtn.textContent = '啟動遊戲';
    }
  });

  settingsBtn.addEventListener('click', () => {
    console.log('Settings clicked');
    showStatus('設定功能開發中', 'info');
  });
  
  /**
   * 開始環境初始化
   */
  function startEnvironmentInitialization() {
    if (isInitializing) return;
    
    isInitializing = true;
    console.log('[Environment] Starting initialization...');
    
    // Show progress card
    progressCard.style.display = 'block';
    progressBarFill.classList.add('indeterminate');
    progressMessage.textContent = '正在初始化WINE環境...';
    
    initializeEnvironment(
      // onProgress
      (progress) => {
        console.log('[Environment] Progress:', progress);
        progressMessage.textContent = progress.message;
        
        if (progress.isComplete) {
          // Complete
          progressBarFill.classList.remove('indeterminate');
          progressBarFill.style.width = '100%';
          setTimeout(() => {
            progressCard.style.display = 'none';
            showStatus('✓ WINE環境初始化完成', 'success');
          }, 1000);
        }
      },
      // onComplete
      () => {
        console.log('[Environment] Initialization complete');
        isEnvironmentInitialized = true;
        isInitializing = false;
        launchBtn.disabled = false;
        launchBtn.textContent = '啟動遊戲';
      },
      // onError
      (error) => {
        console.error('[Environment] Initialization failed:', error);
        isInitializing = false;
        progressCard.style.display = 'none';
        showStatus(`✗ 初始化失敗: ${error}`, 'error');
        launchBtn.disabled = false;
        launchBtn.textContent = '啟動遊戲';
      }
    );
  }
  
  /**
   * 顯示狀態訊息
   */
  function showStatus(message, type = 'info') {
    statusMessage.textContent = message;
    statusMessage.className = `status-message ${type}`;
    statusMessage.style.display = 'block';
    
    setTimeout(() => {
      statusMessage.style.display = 'none';
    }, 5000);
  }
});

  settingsBtn.addEventListener('click', () => {
    console.log('Settings clicked');
    // TODO: Implement settings window
  });
