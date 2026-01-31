const axios = require('axios');

const baseUrl = process.env.API_URL || 'http://localhost:5050';

/**
 * 取得當前配置
 */
async function getConfig() {
  const response = await axios.get(`${baseUrl}/api/config`);
  return response.data;
}

/**
 * 更新配置（完整替換）
 */
async function updateConfig(config) {
  const response = await axios.put(`${baseUrl}/api/config`, config);
  return response.data;
}

/**
 * 部分更新配置
 */
async function patchConfig(config) {
  const response = await axios.patch(`${baseUrl}/api/config`, config);
  return response.data;
}

/**
 * 重置配置為預設值
 */
async function resetConfig() {
  const response = await axios.post(`${baseUrl}/api/config/reset`);
  return response.data;
}

/**
 * 取得配置檔案路徑
 */
async function getConfigPath() {
  const response = await axios.get(`${baseUrl}/api/config/path`);
  return response.data.path;
}

module.exports = {
  getConfig,
  updateConfig,
  patchConfig,
  resetConfig,
  getConfigPath
};
