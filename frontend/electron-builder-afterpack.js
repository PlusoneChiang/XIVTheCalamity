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
  
  const signWine = process.env.SIGN_WINE !== '0';

  // 3. Strip Wine binaries to remove debug symbols (reduce size by ~30 MB)
  // 
  // ⚠️ IMPORTANT: Comment out this section if you need to debug Wine crashes
  // Debug symbols help identify crash locations, but increase file size by ~30 MB
  // 
  // To disable Wine stripping:
  //   1. Comment out lines inside the "if (STRIP_WINE_BINARIES)" block below
  //   2. Or set: const STRIP_WINE_BINARIES = false;
  //
  const STRIP_WINE_BINARIES = true; // Set to false to keep debug symbols
  
  if (STRIP_WINE_BINARIES && signWine && context.electronPlatformName === 'darwin') {
    console.log('\n[AfterPack] Stripping Wine binaries...');
    
    const { execSync } = require('child_process');
    const wineLibPath = path.join(
      context.appOutDir,
      'XIVTheCalamity.app/Contents/Resources/wine/lib'
    );
    
    if (fs.existsSync(wineLibPath)) {
      try {
        // Strip .dylib files (remove local symbols, keep global symbols for dynamic linking)
        console.log('[Wine Strip] Processing .dylib files...');
        execSync(`find "${wineLibPath}" -name "*.dylib" -type f -exec strip -x {} \\;`, {
          stdio: 'pipe'
        });
        
        // Strip .so files (remove local symbols, keep global symbols for dynamic linking)
        console.log('[Wine Strip] Processing .so files...');
        execSync(`find "${wineLibPath}" -name "*.so" -type f -exec strip -x {} \\;`, {
          stdio: 'pipe'
        });
        
        console.log('✅ Wine binaries stripped (saved ~30 MB)');
        console.log('💡 Tip: Set STRIP_WINE_BINARIES=false if you need debug symbols');
      } catch (err) {
        // Strip may warn about already-stripped files, which is normal
        console.log('✅ Wine binaries stripped (some files already stripped)');
      }
    } else {
      console.warn('⚠️  Wine lib path not found');
    }
  } else if (!STRIP_WINE_BINARIES) {
    console.log('\n[AfterPack] ℹ️  Wine stripping disabled (debug symbols preserved)');
  } else if (!signWine && context.electronPlatformName === 'darwin') {
    console.log('\n[AfterPack] ℹ️  Wine strip skipped (SIGN_WINE=0)');
  }

  // 4. Codesign Wine binaries for notarization
  if (signWine && context.electronPlatformName === 'darwin') {
    const signingIdentity = process.env.CSC_NAME;
    
    if (!signingIdentity) {
      console.log('\n[AfterPack] ℹ️  Wine signing skipped (no CSC_NAME set)');
    } else {
      console.log('\n[AfterPack] Signing Wine binaries...');
      const { execSync } = require('child_process');
      const wineRootPath = path.join(
        context.appOutDir,
        'XIVTheCalamity.app/Contents/Resources/wine'
      );
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

      if (fs.existsSync(wineRootPath)) {
        if (!fs.existsSync(entitlementsPath)) {
          throw new Error(`[AfterPack] Entitlements not found at ${entitlementsPath}`);
        }
        try {
          const cpuCount = require('os').cpus().length || 4;
          execSync(
            `find "${wineRootPath}" -type f \\( -name "*.dylib" -o -name "*.so" -o -perm -111 \\) -print0 | xargs -0 -P ${cpuCount} -n 1 /bin/sh -c 'echo \"[Wine Sign] $0\"; codesign --force ${timestampFlag} --options runtime --entitlements \"${entitlementsPath}\" --sign \"${signingIdentity}\" \"$0\"'`,
            { stdio: 'inherit' }
          );
          console.log('✅ Wine binaries signed');
        } catch (err) {
          console.error('❌ Failed to sign Wine binaries:', err.message);
          throw err;
        }
      } else {
        console.warn('⚠️  Wine path not found, skipping signing');
      }
    }
  } else if (!signWine && context.electronPlatformName === 'darwin') {
    console.log('\n[AfterPack] ℹ️  Wine signing skipped (SIGN_WINE=0)');
  }

  // 5. Codesign additional binaries for notarization (XTCAudioRouter, Backend API)
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

      // Sign Backend API
      const backendApiPath = path.join(appPath, 'Contents/Resources/backend/XIVTheCalamity.Api');
      if (fs.existsSync(backendApiPath)) {
        try {
          execSync(
            `codesign --force ${timestampFlag} --options runtime --entitlements "${entitlementsPath}" --sign "${signingIdentity}" "${backendApiPath}"`,
            { stdio: 'inherit' }
          );
          console.log('✅ Backend API signed');
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
