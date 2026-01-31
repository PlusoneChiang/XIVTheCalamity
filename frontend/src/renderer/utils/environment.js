/**
 * 環境初始化工具
 */

const API_BASE_URL = 'http://localhost:5050';

/**
 * 初始化環境
 * @param {Function} onProgress - 進度回調函數 (progress) => void
 * @param {Function} onComplete - 完成回調函數 () => void
 * @param {Function} onError - 錯誤回調函數 (error) => void
 */
export function initializeEnvironment(onProgress, onComplete, onError) {
  const eventSource = new EventSource(`${API_BASE_URL}/api/environment/initialize`);
  
  eventSource.addEventListener('progress', (event) => {
    try {
      const data = JSON.parse(event.data);
      console.log('[Environment] Progress:', data);
      if (onProgress) {
        onProgress({
          stage: data.stage,
          message: data.message,
          isComplete: false,
          hasError: false
        });
      }
    } catch (err) {
      console.error('[Environment] Failed to parse progress:', err);
    }
  });
  
  eventSource.addEventListener('complete', (event) => {
    try {
      const data = JSON.parse(event.data);
      console.log('[Environment] Complete:', data);
      if (onProgress) {
        onProgress({
          stage: 'complete',
          message: data.message || '初始化完成',
          isComplete: true,
          hasError: false
        });
      }
      if (onComplete) {
        onComplete();
      }
      eventSource.close();
    } catch (err) {
      console.error('[Environment] Failed to parse complete:', err);
    }
  });
  
  eventSource.addEventListener('error', (event) => {
    try {
      const data = event.data ? JSON.parse(event.data) : {};
      const errorMessage = data.errorMessage || '初始化失敗';
      console.error('[Environment] Error:', errorMessage);
      if (onError) {
        onError(errorMessage);
      }
      eventSource.close();
    } catch (err) {
      console.error('[Environment] Connection error');
      if (onError) {
        onError('無法連線到後端');
      }
      eventSource.close();
    }
  });
  
  eventSource.onerror = () => {
    console.error('[Environment] EventSource error');
    if (onError) {
      onError('無法連線到後端');
    }
    eventSource.close();
  };
  
  return eventSource;
}

/**
 * 啟動 Wine 配置
 * @returns {Promise<{success: boolean, message: string}>}
 */
export async function launchWineConfig() {
  try {
    const response = await fetch(`${API_BASE_URL}/api/wine/config`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      }
    });
    
    const data = await response.json();
    console.log('[Wine] Config response:', data);
    return data;
  } catch (err) {
    console.error('[Wine] Failed to launch config:', err);
    return {
      success: false,
      message: err.message || '啟動失敗'
    };
  }
}
