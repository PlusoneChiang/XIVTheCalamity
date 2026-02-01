/**
 * Dalamud Update Manager
 * Handles Dalamud download, update and status checking
 */

import i18n from '../../i18n/index.js';
import { showTitleBarProgress, hideTitleBarProgress } from './login.js';
import { handleApiResponse, getErrorMessage } from '../../utils/apiError.js';

let isDalamudChecking = false;
let dalamudCheckCancelled = false;
let dalamudEventSource = null;
let onDalamudCompleteCallback = null;

/**
 * 設定 Dalamud 更新完成回調
 */
export function setOnDalamudComplete(callback) {
  onDalamudCompleteCallback = callback;
}

/**
 * 觸發 Dalamud 更新完成回調
 */
function triggerOnDalamudComplete() {
  if (onDalamudCompleteCallback) {
    console.log('[DALAMUD] Triggering dalamud complete callback');
    onDalamudCompleteCallback();
  }
}

/**
 * 檢查 Dalamud 是否啟用
 */
async function isDalamudEnabled() {
  try {
    const configResponse = await window.electronAPI.backend.call('/api/config', {
      method: 'GET'
    });
    let config;
    try {
      config = await handleApiResponse(configResponse);
    } catch (error) {
      console.log('[DALAMUD] Failed to load config:', getErrorMessage(error, i18n), '- assuming disabled');
      return false;
    }
    return config.dalamud?.enabled === true;
  } catch (error) {
    console.error('[DALAMUD] Error checking config:', error);
    return false;
  }
}

/**
 * 取得 Dalamud 狀態
 */
export async function getDalamudStatus() {
  try {
    const response = await window.electronAPI.backend.call('/api/dalamud/status', {
      method: 'GET'
    });
    let data;
    try {
      data = await handleApiResponse(response);
    } catch (error) {
      console.error('[DALAMUD] Failed to get status:', getErrorMessage(error, i18n));
      return null;
    }
    return data;
  } catch (error) {
    console.error('[DALAMUD] Error getting status:', error);
    return null;
  }
}

/**
 * 開始 Dalamud 更新檢查
 * @returns {Promise<boolean>} 是否成功完成
 */
export async function startDalamudUpdate() {
  console.log('[DALAMUD] ========== Starting Dalamud update check ==========');
  
  if (isDalamudChecking) {
    console.log('[DALAMUD] Update check already in progress, skipping');
    return false;
  }
  
  // 檢查是否啟用
  const enabled = await isDalamudEnabled();
  if (!enabled) {
    console.log('[DALAMUD] Dalamud is disabled in settings, skipping');
    return true; // 不是錯誤，只是跳過
  }
  
  isDalamudChecking = true;
  dalamudCheckCancelled = false;
  
  try {
    // 先取得狀態
    const status = await getDalamudStatus();
    if (!status) {
      console.error('[DALAMUD] Failed to get status');
      isDalamudChecking = false;
      return false;
    }
    
    console.log('[DALAMUD] Current status:', status);
    
    // 如果已是最新，不需要更新
    if (status.state === 1) { // UpToDate
      console.log('[DALAMUD] Dalamud is up to date');
      isDalamudChecking = false;
      return true;
    }
    
    // 需要安裝或更新
    console.log('[DALAMUD] Starting update...');
    showTitleBarProgress(0, 'login.dalamud_checking');
    
    // 使用 SSE 監聽進度
    return await startDalamudUpdateWithSSE();
    
  } catch (error) {
    console.error('[DALAMUD] Update check error:', error);
    hideTitleBarProgress();
    isDalamudChecking = false;
    return false;
  }
}

/**
 * 使用 SSE 執行 Dalamud 更新
 */
function startDalamudUpdateWithSSE() {
  return new Promise((resolve) => {
    const sseUrl = 'http://localhost:5050/api/dalamud/update-stream';
    console.log('[DALAMUD] Connecting to SSE:', sseUrl);
    
    dalamudEventSource = new EventSource(sseUrl);
    
    dalamudEventSource.addEventListener('progress', (event) => {
      if (dalamudCheckCancelled) {
        closeDalamudSSE();
        resolve(false);
        return;
      }
      
      try {
        const progress = JSON.parse(event.data);
        console.log('[DALAMUD] Progress event:', progress);
        
        // 取得階段訊息
        const stageMessage = getDalamudStageMessage(progress.stage);
        let percentage = progress.percentage || 0;
        
        // 組合進度訊息
        let message = stageMessage;
        
        // 優先顯示 bytes 進度 (下載大檔案時)
        if (progress.totalBytes > 0 && progress.bytesDownloaded >= 0) {
          const downloadedMB = (progress.bytesDownloaded / 1024 / 1024).toFixed(1);
          const totalMB = (progress.totalBytes / 1024 / 1024).toFixed(1);
          message += ` (${downloadedMB}/${totalMB} MB)`;
          percentage = Math.round((progress.bytesDownloaded / progress.totalBytes) * 100);
        }
        // 其次顯示檔案數量進度 (Assets 下載)
        else if (progress.totalItems > 0) {
          message += ` (${progress.completedItems}/${progress.totalItems})`;
          percentage = Math.round((progress.completedItems / progress.totalItems) * 100);
        }
        
        // 顯示當前檔案名稱
        if (progress.currentFile) {
          message += ` - ${progress.currentFile}`;
        }
        
        showTitleBarProgress(percentage, message);
      } catch (err) {
        console.error('[DALAMUD] Failed to parse progress:', err);
      }
    });
    
    dalamudEventSource.addEventListener('complete', (event) => {
      try {
        const progress = JSON.parse(event.data);
        console.log('[DALAMUD] Complete event:', progress);
        
        showTitleBarProgress(100, 'login.dalamud_complete');
        setTimeout(() => {
          closeDalamudSSE();
          hideTitleBarProgress();
          isDalamudChecking = false;
          triggerOnDalamudComplete();
          resolve(true);
        }, 1500);
      } catch (err) {
        console.error('[DALAMUD] Failed to parse complete event:', err);
      }
    });
    
    dalamudEventSource.addEventListener('error', (event) => {
      // readyState 2 = CLOSED，這是正常的連接關閉
      if (dalamudEventSource?.readyState === 2) {
        console.log('[DALAMUD] SSE connection closed (normal)');
        closeDalamudSSE();
        // 如果已經完成，不需要再處理
        if (!isDalamudChecking) {
          return;
        }
        hideTitleBarProgress();
        isDalamudChecking = false;
        resolve(false);
      } else {
        console.error('[DALAMUD] SSE error:', event);
      }
    });
    
    dalamudEventSource.addEventListener('open', () => {
      console.log('[DALAMUD] SSE connection opened');
    });
  });
}

/**
 * 取得階段訊息
 */
function getDalamudStageMessage(stage) {
  const stageMessages = {
    0: i18n.t('login.dalamud_checking'),       // CheckingVersion
    1: i18n.t('login.dalamud_downloading'),    // DownloadingDalamud
    2: i18n.t('login.dalamud_extracting'),     // ExtractingDalamud
    3: i18n.t('login.dalamud_runtime'),        // DownloadingRuntime
    4: i18n.t('login.dalamud_runtime'),        // ExtractingRuntime
    5: i18n.t('login.dalamud_assets'),         // DownloadingAssets
    6: i18n.t('login.dalamud_verifying'),      // VerifyingAssets
    7: i18n.t('login.dalamud_complete'),       // Complete
    8: i18n.t('login.dalamud_failed')          // Failed
  };
  return stageMessages[stage] || i18n.t('login.dalamud_checking');
}

/**
 * 關閉 SSE 連接
 */
function closeDalamudSSE() {
  if (dalamudEventSource) {
    console.log('[DALAMUD] Closing SSE connection');
    dalamudEventSource.close();
    dalamudEventSource = null;
  }
}

/**
 * 取消 Dalamud 更新
 */
export async function cancelDalamudUpdate() {
  if (!isDalamudChecking) {
    return;
  }
  
  console.log('[DALAMUD] Cancelling update...');
  dalamudCheckCancelled = true;
  closeDalamudSSE();
  
  try {
    await window.electronAPI.backend.call('/api/dalamud/cancel', {
      method: 'POST'
    });
    console.log('[DALAMUD] Update cancelled');
  } catch (error) {
    console.error('[DALAMUD] Failed to cancel update:', error);
  }
  
  isDalamudChecking = false;
  hideTitleBarProgress();
}

/**
 * 檢查是否正在更新
 */
export function isDalamudUpdating() {
  return isDalamudChecking;
}

/**
 * 處理設定變更
 */
export async function handleDalamudConfigChanged(data) {
  console.log('[DALAMUD] Config changed:', data);
  
  // 如果 Dalamud 啟用狀態變更
  if (data.dalamudEnabledChanged !== undefined) {
    if (data.dalamudEnabledChanged) {
      // 從關閉變成開啟，觸發更新檢查
      console.log('[DALAMUD] Dalamud enabled, starting update check');
      await cancelDalamudUpdate();
      setTimeout(() => {
        startDalamudUpdate();
      }, 500);
    } else {
      // 從開啟變成關閉，取消更新
      console.log('[DALAMUD] Dalamud disabled, cancelling update');
      await cancelDalamudUpdate();
    }
  }
}
