#!/usr/bin/env node

/**
 * Sync version from version.json to package.json
 * This ensures version.json is the single source of truth
 */

const fs = require('fs');
const path = require('path');

const frontendDir = path.join(__dirname, '../frontend');
const versionJsonPath = path.join(frontendDir, 'src/renderer/version.json');
const packageJsonPath = path.join(frontendDir, 'package.json');

try {
  // Read version.json (source of truth)
  const versionInfo = JSON.parse(fs.readFileSync(versionJsonPath, 'utf8'));
  const version = versionInfo.version;
  
  // Read package.json
  const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, 'utf8'));
  
  // Check if version needs update
  if (packageJson.version !== version) {
    console.log(`📦 Syncing version: ${packageJson.version} → ${version}`);
    packageJson.version = version;
    
    // Write back to package.json
    fs.writeFileSync(packageJsonPath, JSON.stringify(packageJson, null, 2) + '\n');
    console.log('✅ package.json version synced');
  } else {
    console.log(`✅ Version already synced: ${version}`);
  }
} catch (error) {
  console.error('❌ Failed to sync version:', error.message);
  process.exit(1);
}
