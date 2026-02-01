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
    
    // 使用新的 SSE endpoint 進行更新（單一連接）
    startUpdateWithSSE(gamePath);
    
  } catch (error) {
    console.error('[UPDATE] Update check error:', error);
    hideTitleBarProgress();
    isUpdateChecking = false;
    triggerOnUpdateComplete();
  }
}

/**
 * 使用 SSE endpoint 進行更新
 */
function startUpdateWithSSE(gamePath) {
  if (progressEventSource) {
    console.log('[UPDATE] Closing existing EventSource');
    stopProgressMonitoring();
  }
  
  console.log('[UPDATE] Starting update via SSE endpoint');
  
  // 編碼路徑參數
  const encodedPath = encodeURIComponent(gamePath);
  const url = `http://localhost:5050/api/update/install?gamePath=${encodedPath}`;
  console.log('[UPDATE] SSE URL:', url);
  
  progressEventSource = new EventSource(url);
  console.log('[UPDATE] EventSource created, readyState:', progressEventSource.readyState);
  
  // 處理進度事件
  progressEventSource.addEventListener('progress', (event) => {
    console.log('[UPDATE] Progress event received, raw data:', event.data);
    try {
      const progress = JSON.parse(event.data);
      handleProgressEvent(progress);
    } catch (error) {
      console.error('[UPDATE] Failed to parse progress:', error);
    }
  });
  
  // 處理完成事件
  progressEventSource.addEventListener('complete', (event) => {
    console.log('[UPDATE] Complete event received');
    try {
      const data = JSON.parse(event.data);
      console.log('[UPDATE] Update complete:', data);
      
      showTitleBarProgress(100, 'login.update_complete');
      setTimeout(() => {
        stopProgressMonitoring();
        hideTitleBarProgress();
        isUpdateChecking = false;
        triggerOnUpdateComplete();
      }, 2000);
    } catch (error) {
      console.error('[UPDATE] Failed to parse complete event:', error);
    }
  });
  
  // 處理取消事件
  progressEventSource.addEventListener('cancelled', (event) => {
    console.log('[UPDATE] Cancelled event received');
    try {
      const data = event.data ? JSON.parse(event.data) : {};
      console.log('[UPDATE] Update cancelled:', data);
      
      stopProgressMonitoring();
      hideTitleBarProgress();
      isUpdateChecking = false;
      triggerOnUpdateComplete();
    } catch (error) {
      console.error('[UPDATE] Failed to parse cancelled event:', error);
    }
  });
  
  // 處理錯誤事件
  progressEventSource.addEventListener('error', (event) => {
    console.error('[UPDATE] Error event triggered, readyState:', progressEventSource?.readyState);
    console.error('[UPDATE] Error event object:', event);
    
    try {
      if (event.data) {
        const errorData = JSON.parse(event.data);
        console.error('[UPDATE] Update error:', errorData);
        
        // 顯示錯誤訊息
        if (errorData.message) {
          console.error('[UPDATE] Error message:', errorData.message);
        }
      } else {
        console.error('[UPDATE] SSE connection error (no data)');
      }
    } catch (parseError) {
      console.error('[UPDATE] SSE error event (parse failed):', parseError);
    }
    
    // 檢查連接狀態
    if (progressEventSource?.readyState === 2) {
      console.log('[UPDATE] SSE connection closed, stopping monitoring');
      stopProgressMonitoring();
      hideTitleBarProgress();
      isUpdateChecking = false;
      triggerOnUpdateComplete();
    }
  });
  
  progressEventSource.addEventListener('open', () => {
    console.log('[UPDATE] SSE connection opened successfully');
  });
}

/**
 * 處理進度事件
 */
function handleProgressEvent(progress) {
  console.log('[UPDATE] Progress event received:', JSON.stringify(progress, null, 2));
  
  // 安全取得數值
  const installedPatches = typeof progress.installedPatches === 'number' ? progress.installedPatches : 0;
  const totalPatches = typeof progress.totalPatches === 'number' ? progress.totalPatches : 1;
  const downloadingCount = typeof progress.downloadingCount === 'number' ? progress.downloadingCount : 0;
  const installingCount = typeof progress.installingCount === 'number' ? progress.installingCount : 0;
  const downloadSpeed = typeof progress.downloadSpeedBytesPerSec === 'number' ? progress.downloadSpeedBytesPerSec : 0;
  const totalBytesDownloaded = typeof progress.totalBytesDownloaded === 'number' ? progress.totalBytesDownloaded : 0;
  const totalBytes = typeof progress.totalBytes === 'number' && progress.totalBytes > 0 ? progress.totalBytes : 1;
  const phase = progress.phase || 'downloading';
  
  // 計算百分比
  let percentage = 0;
  if (progress.percentage > 0) {
    percentage = progress.percentage;
  } else if (totalBytes > 0) {
    percentage = (totalBytesDownloaded * 100 / totalBytes);
  }
  
  // 格式化大小 (自動選擇 MB 或 GB)
  const formatSize = (bytes) => {
    if (bytes >= 1024 * 1024 * 1024) {
      return (bytes / 1024 / 1024 / 1024).toFixed(1) + ' GB';
    }
    return (bytes / 1024 / 1024).toFixed(1) + ' MB';
  };
  
  // 格式化速度
  const speedMBps = (downloadSpeed / 1024 / 1024).toFixed(0);
  
  // 解析剩餘時間
  let timeRemaining = '00:00:00';
  if (progress.estimatedTimeRemaining) {
    // 如果是 TimeSpan 字符串格式 (e.g., "01:23:45")
    if (typeof progress.estimatedTimeRemaining === 'string') {
      const timeMatch = progress.estimatedTimeRemaining.match(/^(\d+):(\d+):(\d+)/);
      if (timeMatch) {
        const hours = timeMatch[1].padStart(2, '0');
        const minutes = timeMatch[2].padStart(2, '0');
        const seconds = timeMatch[3].padStart(2, '0');
        timeRemaining = `${hours}:${minutes}:${seconds}`;
      }
    }
  }
  
  // 根據階段顯示不同的訊息
  let message = '';
  
  if (phase === 'downloading') {
    // 下載階段：顯示完整資訊
    const downloadedSize = formatSize(totalBytesDownloaded);
    const totalSize = formatSize(totalBytes);
    const percentText = Math.round(percentage) + '%';
    
    message = `${i18n.t('login.game_updating')} - ` +
      `${i18n.t('login.patch_progress')} ${installedPatches}/${totalPatches} | ` +
      `${i18n.t('login.threads')} ${downloadingCount} | ` +
      `${speedMBps} MB/s | ` +
      `${i18n.t('login.remaining')} ${timeRemaining} | ` +
      `${downloadedSize}/${totalSize} - ${percentText}`;
  } else if (phase === 'installing') {
    // 安裝階段
    const percentText = Math.round(percentage) + '%';
    const installingFileName = progress.installingFileName || '';
    
    message = `${i18n.t('login.installing_patches')} - ` +
      `${installedPatches}/${totalPatches} - ${percentText}` +
      (installingFileName ? ` - ${installingFileName}` : '');
  } else if (phase === 'cleanup') {
    // 清理階段
    message = i18n.t('login.cleaning_up');
  } else if (phase === 'checking') {
    // 檢查階段
    message = i18n.t('login.checking_updates');
  } else {
    // 預設
    message = i18n.t('login.game_updating');
  }
  
  showTitleBarProgress(percentage, message);
}

/**
 * 處理更新檢查完成（舊方法，保留用於向後兼容）
 * 注意：新的 SSE endpoint 不再使用這個方法
 */
function handleUpdateCheckComplete(result) {
  console.log('[UPDATE] Update check API returned (legacy)');
  
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
  
  isUpdateChecking = false;
  hideTitleBarProgress();
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
