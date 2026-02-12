// After pack hook - clean up temp-backend and optimize locales
const fs = require('fs');
const path = require('path');

exports.default = async function(context) {
  console.log('[AfterPack] Starting post-build optimization...');
  
  // 1. Clean up temp-backend
  const projectRoot = path.join(context.appOutDir, '..', '..');
  const tempBackend = path.join(projectRoot, 'Release', 'temp-backend');
  
  console.log('[AfterPack] Cleaning up temp-backend:', tempBackend);
  
  if (fs.existsSync(tempBackend)) {
    fs.rmSync(tempBackend, { recursive: true, force: true });
    console.log('✅ temp-backend cleaned');
  } else {
    console.log('ℹ️  temp-backend already cleaned or does not exist');
  }
  
  // 2. Remove unnecessary app-level locales (keep only en and zh variants)
  // NOTE: Electron Framework locales are handled by electron-builder's 'electronLanguages' config
  // Do NOT modify Electron Framework internals in afterPack - it breaks codesigning!
  if (context.electronPlatformName === 'darwin') {
    console.log('\n[AfterPack] Cleaning app-level locales...');
    
    const appLocalesPath = path.join(context.appOutDir, 'XIVTheCalamity.app/Contents/Resources');
    if (fs.existsSync(appLocalesPath)) {
      const appKeepLocales = ['en.lproj', 'zh_TW.lproj', 'zh_CN.lproj'];
      const appFiles = fs.readdirSync(appLocalesPath);
      let removedCount = 0;
      
      appFiles.forEach(file => {
        if (file.endsWith('.lproj') && !appKeepLocales.includes(file)) {
          const fullPath = path.join(appLocalesPath, file);
          try {
            fs.rmSync(fullPath, { recursive: true, force: true });
            removedCount++;
          } catch (err) {
            // Ignore errors
          }
        }
      });
      
      if (removedCount > 0) {
        console.log(`✅ Removed ${removedCount} unused app-level locales`);
      }
    }
  }
  
  // 3. Codesign additional binaries for notarization (XTCAudioRouter, Backend API)
  if (context.electronPlatformName === 'darwin') {
    const signingIdentity = process.env.CSC_NAME;
    if (signingIdentity) {
      const { execSync } = require('child_process');
      const entitlementsPath = path.join(
        context.appOutDir,
        '..',
        '..',
        'frontend',
        'build',
        'entitlements.mac.plist'
      );
      const useTimestamp = process.env.NOTARIZE !== '0';
      const timestampFlag = useTimestamp ? '--timestamp' : '--timestamp=none';
      const appPath = path.join(context.appOutDir, 'XIVTheCalamity.app');

      // Sign XTCAudioRouter
      console.log('\n[AfterPack] Signing additional binaries...');
      const audioRouterPath = path.join(appPath, 'Contents/Resources/resources/bin/XTCAudioRouter');
      if (fs.existsSync(audioRouterPath)) {
        try {
          execSync(
            `codesign --force ${timestampFlag} --options runtime --entitlements "${entitlementsPath}" --sign "${signingIdentity}" "${audioRouterPath}"`,
            { stdio: 'inherit' }
          );
          console.log('✅ XTCAudioRouter signed');
        } catch (err) {
          console.error('❌ Failed to sign XTCAudioRouter:', err.message);
          throw err;
        }
      } else {
        console.log('ℹ️  XTCAudioRouter not found, skipping');
      }

      // Sign Backend API (NativeAOT)
      const backendApiPath = path.join(appPath, 'Contents/Resources/backend/XIVTheCalamity.Api.NativeAOT');
      if (fs.existsSync(backendApiPath)) {
        try {
          execSync(
            `codesign --force ${timestampFlag} --options runtime --entitlements "${entitlementsPath}" --sign "${signingIdentity}" "${backendApiPath}"`,
            { stdio: 'inherit' }
          );
          console.log('✅ Backend API (NativeAOT) signed');
        } catch (err) {
          console.error('❌ Failed to sign Backend API:', err.message);
          throw err;
        }
      } else {
        console.log('ℹ️  Backend API not found, skipping');
      }
    }
  }
  
  console.log('\n[AfterPack] ✅ Post-build optimization complete!\n');
};
