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
  
  // 2. Remove unnecessary Electron locales (keep only en-US and zh-TW)
  if (context.electronPlatformName === 'darwin') {
    console.log('\n[AfterPack] Optimizing Electron locales...');
    
    const frameworkPath = path.join(
      context.appOutDir,
      'XIVTheCalamity.app/Contents/Frameworks/Electron Framework.framework/Versions/A/Resources'
    );
    
    // Locales to keep (including variants)
    const keepLocales = [
      'en.lproj', 'en_AU.lproj', 'en_CA.lproj', 'en_GB.lproj', 'en_IN.lproj', 'en_NZ.lproj', 'en_US.lproj',
      'zh_TW.lproj', 'zh_HK.lproj',
      // Keep gender variants for kept locales
      'en_FEMININE.lproj', 'en_MASCULINE.lproj', 'en_NEUTER.lproj',
      'zh_TW_FEMININE.lproj', 'zh_TW_MASCULINE.lproj', 'zh_TW_NEUTER.lproj'
    ];
    
    let removedCount = 0;
    let savedBytes = 0;
    
    try {
      if (fs.existsSync(frameworkPath)) {
        const files = fs.readdirSync(frameworkPath);
        
        files.forEach(file => {
          if (file.endsWith('.lproj') && !keepLocales.includes(file)) {
            const fullPath = path.join(frameworkPath, file);
            try {
              const stats = fs.statSync(fullPath);
              const size = stats.isDirectory() 
                ? getDirSize(fullPath)
                : stats.size;
              
              fs.rmSync(fullPath, { recursive: true, force: true });
              removedCount++;
              savedBytes += size;
            } catch (err) {
              console.warn(`⚠️  Failed to remove locale ${file}:`, err.message);
            }
          }
        });
        
        console.log(`✅ Removed ${removedCount} unused locales`);
        console.log(`💾 Saved ${(savedBytes / 1024 / 1024).toFixed(2)} MB`);
      } else {
        console.warn('⚠️  Electron Framework path not found');
      }
    } catch (err) {
      console.error('❌ Error optimizing locales:', err);
    }
    
    // Also clean app-level locales
    const appLocalesPath = path.join(context.appOutDir, 'XIVTheCalamity.app/Contents/Resources');
    if (fs.existsSync(appLocalesPath)) {
      const appKeepLocales = ['en.lproj', 'zh_TW.lproj', 'zh_CN.lproj'];
      const appFiles = fs.readdirSync(appLocalesPath);
      
      appFiles.forEach(file => {
        if (file.endsWith('.lproj') && !appKeepLocales.includes(file)) {
          const fullPath = path.join(appLocalesPath, file);
          try {
            fs.rmSync(fullPath, { recursive: true, force: true });
          } catch (err) {
            // Ignore errors
          }
        }
      });
    }
  }
  
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
  
  if (STRIP_WINE_BINARIES && context.electronPlatformName === 'darwin') {
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
  }
  
  console.log('\n[AfterPack] ✅ Post-build optimization complete!\n');
};

// Helper function to calculate directory size
function getDirSize(dirPath) {
  let size = 0;
  
  try {
    const files = fs.readdirSync(dirPath);
    
    files.forEach(file => {
      const filePath = path.join(dirPath, file);
      const stats = fs.statSync(filePath);
      
      if (stats.isDirectory()) {
        size += getDirSize(filePath);
      } else {
        size += stats.size;
      }
    });
  } catch (err) {
    // Ignore errors
  }
  
  return size;
}
