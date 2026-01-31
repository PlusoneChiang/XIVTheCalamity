/**
 * Game Update Manager
 * Handles game patch checking, downloading, and installation
 * Uses Taiwan official API (no login required)
 */

import i18n from '../../i18n/index.js';
import { showTitleBarProgress, hideTitleBarProgress } from './login.js';
import { handleApiResponse, getErrorMessage } from '../../utils/apiError.js';

let isUpdateChecking = false;
let updateCheckCancelled = false;
let progressEventSource = null;
let onUpdateCompleteCallback = null;

/**
 * 設定更新完成回調（用於觸發 Dalamud 更新）
 */
export function setOnUpdateComplete(callback) {
  onUpdateCompleteCallback = callback;
}

/**
 * 觸發更新完成回調
 */
function triggerOnUpdateComplete() {
  if (onUpdateCompleteCallback) {
    console.log('[UPDATE] Triggering update complete callback');
    setTimeout(() => {
      onUpdateCompleteCallback();
    }, 500);
  }
}

/**
 * 處理設定變更事件
 * 只有遊戲路徑變更時需要中斷並重新檢查更新
 */
export async function handleConfigChanged(data) {
  console.log('[UPDATE] Config changed:', data);
  
  // 只有遊戲路徑變更才需要重新檢查
  if (data.gamePathChanged && data.newGamePath) {
    console.log('[UPDATE] Game path changed, restarting update check');
    await cancelUpdate();
    
    // 稍微延遲以確保設定已保存
    setTimeout(() => {
      startUpdate();
    }, 500);
  }
}

/**
 * 開始遊戲更新檢查與安裝
 */
export async function startUpdate() {
  console.log('[UPDATE] ========== Starting game update check ==========');
  
  if (isUpdateChecking) {
    console.log('[UPDATE] Update check already in progress, skipping');
    return;
  }
  
  isUpdateChecking = true;
  updateCheckCancelled = false;
  
  try {
    // Get game path from config
    const configResponse = await window.electronAPI.backend.call('/api/config', {
      method: 'GET'
    });
    
    let configData;
    try {
      configData = await handleApiResponse(configResponse);
    } catch (error) {
      console.error('[UPDATE] Failed to load config:', getErrorMessage(error, i18n));
      isUpdateChecking = false;
      triggerOnUpdateComplete();
      return;
    }
    
    const gamePath = configData.game?.gamePath;
    if (!gamePath) {
      console.log('[UPDATE] No game path configured, skipping update check');
      isUpdateChecking = false;
      triggerOnUpdateComplete();
      return;
    }
    
    // 驗證遊戲路徑是否有效（需包含 boot 和 game 子目錄）
    try {
      const validation = await window.electronAPI.validateGameDirectory(gamePath);
      console.log('[UPDATE] Game directory validation:', validation);
      
      if (!validation.valid) {
        console.log('[UPDATE] Game path invalid:', gamePath, 'Reason:', validation.reason);
        hideTitleBarProgress();
        isUpdateChecking = false;
        triggerOnUpdateComplete();
        return;
      }
    } catch (pathError) {
      console.warn('[UPDATE] Failed to verify game path, continuing anyway:', pathError);
    }
    
    console.log('[UPDATE] Game path:', gamePath);
    
    // Show update progress in title bar
    showTitleBarProgress(0, 'login.checking_updates');
    
    // 開啟 SSE 監聽進度
    startProgressMonitoring();
    
    // 發起更新請求
    window.electronAPI.backend.call('/api/update/check', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: { gamePath: gamePath }
    }).then(async response => {
      try {
        const data = await handleApiResponse(response);
        handleUpdateCheckComplete(data);
      } catch (error) {
        console.error('[UPDATE] Update check error:', getErrorMessage(error, i18n));
        stopProgressMonitoring();
        hideTitleBarProgress();
        isUpdateChecking = false;
        triggerOnUpdateComplete();
      }
    }).catch(error => {
      console.error('[UPDATE] Update check error:', error);
      stopProgressMonitoring();
      hideTitleBarProgress();
      isUpdateChecking = false;
      triggerOnUpdateComplete();
    });
    
  } catch (error) {
    console.error('[UPDATE] Update check error:', error);
    hideTitleBarProgress();
    isUpdateChecking = false;
    triggerOnUpdateComplete();
  }
}

/**
 * 處理更新檢查完成
 */
function handleUpdateCheckComplete(result) {
  console.log('[UPDATE] Update check API returned');
  
  if (updateCheckCancelled) {
    console.log('[UPDATE] Update check was cancelled');
    return;
  }
  
  console.log('[UPDATE] Update check result:', result);
  
  // 檢查錯誤訊息
  if (result.errorMessage) {
    console.error('[UPDATE] Update check error:', result.errorMessage);
    stopProgressMonitoring();
    hideTitleBarProgress();
    isUpdateChecking = false;
    triggerOnUpdateComplete();
    return;
  }
  
  if (result.cancelled) {
    console.log('[UPDATE] Update was cancelled');
    stopProgressMonitoring();
    hideTitleBarProgress();
    isUpdateChecking = false;
    return;
  }
  
  if (!result.needsUpdate) {
    console.log('[UPDATE] Game is up to date!');
    showTitleBarProgress(100, 'login.update_complete');
    setTimeout(() => {
      stopProgressMonitoring();
      hideTitleBarProgress();
      isUpdateChecking = false;
      triggerOnUpdateComplete();
    }, 2000);
    return;
  }
  
  console.log('[UPDATE] Update complete!');
  console.log('[UPDATE] Installed patches:', result.requiredPatches?.length || 0);
  
  showTitleBarProgress(100, 'login.update_complete');
  setTimeout(() => {
    stopProgressMonitoring();
    hideTitleBarProgress();
    isUpdateChecking = false;
    triggerOnUpdateComplete();
  }, 2000);
}

/**
 * 取消更新（遊戲路徑變更時使用）
 */
export async function cancelUpdate() {
  if (!isUpdateChecking) {
    return;
  }
  
  console.log('[UPDATE] Cancelling update...');
  updateCheckCancelled = true;
  
  stopProgressMonitoring();
  
  try {
    await window.electronAPI.backend.call('/api/update/cancel', {
      method: 'POST'
    });
    console.log('[UPDATE] Update cancelled');
  } catch (error) {
    console.error('[UPDATE] Failed to cancel update:', error);
  }
  
  isUpdateChecking = false;
  hideTitleBarProgress();
}

/**
 * 開始監聽下載進度
 */
function startProgressMonitoring() {
  if (progressEventSource) {
    stopProgressMonitoring();
  }
  
  console.log('[UPDATE] Starting progress monitoring via SSE');
  progressEventSource = new EventSource('http://localhost:5050/api/update/progress-stream');
  
  progressEventSource.addEventListener('progress', (event) => {
    try {
      const progress = JSON.parse(event.data);
      
      // 安全取得數值
      const installedPatches = typeof progress.installedPatches === 'number' ? progress.installedPatches : 0;
      const totalPatches = typeof progress.totalPatches === 'number' ? progress.totalPatches : 1;
      const downloadingCount = typeof progress.downloadingCount === 'number' ? progress.downloadingCount : 0;
      const installingCount = typeof progress.installingCount === 'number' ? progress.installingCount : 0;
      const downloadSpeed = typeof progress.downloadSpeedBytesPerSecond === 'number' ? progress.downloadSpeedBytesPerSecond : 0;
      const totalBytesDownloaded = typeof progress.totalBytesDownloaded === 'number' ? progress.totalBytesDownloaded : 0;
      const totalBytes = typeof progress.totalBytes === 'number' && progress.totalBytes > 0 ? progress.totalBytes : 1;
      
      // 計算百分比（基於下載的位元組數，而非檔案數）
      const percentage = totalBytes > 0 ? (totalBytesDownloaded * 100 / totalBytes) : 0;
      
      // 格式化大小 (自動選擇 MB 或 GB)
      const formatSize = (bytes) => {
        if (bytes >= 1024 * 1024 * 1024) {
          return (bytes / 1024 / 1024 / 1024).toFixed(1) + ' GB';
        }
        return (bytes / 1024 / 1024).toFixed(1) + ' MB';
      };
      
      // 格式化速度
      const speedMBps = (downloadSpeed / 1024 / 1024).toFixed(0);
      
      // 解析剩餘時間（強制顯示為 hh:mm:ss 格式）
      let timeRemaining = '00:00:00';
      if (progress.estimatedTimeRemaining && typeof progress.estimatedTimeRemaining === 'string') {
        const timeMatch = progress.estimatedTimeRemaining.match(/^(\d+):(\d+):(\d+)/);
        if (timeMatch) {
          const hours = timeMatch[1].padStart(2, '0');
          const minutes = timeMatch[2].padStart(2, '0');
          const seconds = timeMatch[3].padStart(2, '0');
          timeRemaining = `${hours}:${minutes}:${seconds}`;
        }
      }
      
      // 組合新格式的進度訊息
      // 遊戲更新中 - 更新檔 15/91 | 線程 4 | 60 MB/s | 剩餘 01:12:30 | 14.8/85.5 GB - 17%
      const downloadedSize = formatSize(totalBytesDownloaded);
      const totalSize = formatSize(totalBytes);
      const percentText = Math.round(percentage) + '%';
      
      const message = `${i18n.t('login.game_updating')} - ` +
        `${i18n.t('login.patch_progress')} ${installedPatches}/${totalPatches} | ` +
        `${i18n.t('login.threads')} ${downloadingCount} | ` +
        `${speedMBps} MB/s | ` +
        `${i18n.t('login.remaining')} ${timeRemaining} | ` +
        `${downloadedSize}/${totalSize} - ${percentText}`;
      
      showTitleBarProgress(percentage, message);
      
      // 下載完成
      if (progress.isCompleted) {
        console.log('[UPDATE] Download completed!');
        showTitleBarProgress(100, 'login.update_complete');
        setTimeout(() => {
          stopProgressMonitoring();
          hideTitleBarProgress();
        }, 2000);
      }
    } catch (error) {
      console.error('[UPDATE] Failed to parse progress:', error);
    }
  });
  
  progressEventSource.addEventListener('error', (error) => {
    console.error('[UPDATE] SSE error:', error);
    if (progressEventSource?.readyState === 2) {
      console.log('[UPDATE] SSE connection closed, stopping monitoring');
      stopProgressMonitoring();
    }
  });
  
  progressEventSource.addEventListener('open', () => {
    console.log('[UPDATE] SSE connection opened');
  });
}

/**
 * 停止進度監聽
 */
function stopProgressMonitoring() {
  if (progressEventSource) {
    console.log('[UPDATE] Stopping progress monitoring');
    progressEventSource.close();
    progressEventSource = null;
  }
}

/**
 * 檢查是否正在更新
 */
export function isUpdating() {
  return isUpdateChecking;
}

// Backward compatibility exports
export const startBackgroundUpdate = startUpdate;
export const startLoginUpdate = startUpdate;
export const cancelBackgroundUpdate = cancelUpdate;
export function setLoggedIn() {} // No longer needed
export function setLaunchButtonEnabled() {} // No longer needed
export function setAppVersionText(versionText) {
  window._appVersionText = versionText;
}
