<script setup lang="ts">
import { computed } from 'vue';
import { useProgressStore } from '../stores/progress';

const progressStore = useProgressStore();

const progressLabel = computed(() => {
  if (!progressStore.currentProgress) return '';
  
  const { type, message, percentage } = progressStore.currentProgress;
  
  // 下載類型顯示百分比
  if (type === 'download' && percentage !== undefined) {
    return `${message} (${percentage}%)`;
  }
  
  // 其他類型只顯示訊息
  return message;
});
</script>

<template>
  <div v-if="progressStore.isActive" class="progress-card">
    <div class="progress-bar">
      <div 
        class="progress-bar-fill" 
        :class="{ 
          'indeterminate': progressStore.currentProgress?.percentage === undefined,
          'determinate': progressStore.currentProgress?.percentage !== undefined
        }"
        :style="{ 
          width: progressStore.currentProgress?.percentage ? `${progressStore.currentProgress.percentage}%` : '100%' 
        }"
      ></div>
    </div>
    <p class="progress-message">{{ progressLabel }}</p>
  </div>
</template>

<style scoped>
.progress-card {
  margin-top: 1rem;
  width: 100%;
}

.progress-bar {
  width: 100%;
  height: 4px;
  background: rgba(255, 255, 255, 0.1);
  border-radius: 2px;
  overflow: hidden;
}

.progress-bar-fill {
  height: 100%;
  background: linear-gradient(90deg, #667eea 0%, #764ba2 100%);
}

.progress-bar-fill.determinate {
  animation: none;
  transition: width 0.3s ease;
}

.progress-bar-fill.indeterminate {
  width: 100%;
  animation: indeterminate 1.5s infinite;
}

@keyframes indeterminate {
  0% {
    transform: translateX(-100%);
  }
  100% {
    transform: translateX(100%);
  }
}

.progress-message {
  margin-top: 0.5rem;
  font-size: 0.875rem;
  color: rgba(255, 255, 255, 0.7);
  text-align: center;
}
</style>
