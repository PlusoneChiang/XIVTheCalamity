const axios = require('axios');

const baseUrl = process.env.API_URL || 'http://localhost:5050';

/**
 * 啟動 winecfg
 */
async function launchWineConfig() {
  const response = await axios.post(`${baseUrl}/api/wine/config`);
  return response.data;
}

module.exports = {
  launchWineConfig
};
