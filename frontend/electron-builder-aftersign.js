// After sign hook - fix designated requirements for Electron frameworks
// Electron's bundled frameworks have embedded designated requirements pointing to Electron's certificate
// We need to re-sign them with a generic designated requirement to pass Apple notarization

const { execSync } = require('child_process');
const path = require('path');
const fs = require('fs');

exports.default = async function(context) {
  if (context.electronPlatformName !== 'darwin') {
    return;
  }

  const signingIdentity = process.env.CSC_NAME;
  if (!signingIdentity) {
    console.log('[AfterSign] No CSC_NAME set, skipping designated requirement fix');
    return;
  }

  console.log('[AfterSign] Fixing designated requirements for Electron frameworks...');

  const appPath = path.join(context.appOutDir, `${context.packager.appInfo.productFilename}.app`);
  const frameworksPath = path.join(appPath, 'Contents/Frameworks');
  const entitlementsPath = path.join(__dirname, 'build', 'entitlements.mac.plist');
  const useTimestamp = process.env.NOTARIZE !== '0';
  const timestampFlag = useTimestamp ? '--timestamp' : '--timestamp=none';

  // Frameworks that have embedded designated requirements from Electron team
  const frameworksToFix = [
    'Electron Framework.framework/Versions/A/Electron Framework',
    'Electron Framework.framework/Versions/A/Helpers/chrome_crashpad_handler',
    'Mantle.framework/Versions/A/Mantle',
    'ReactiveObjC.framework/Versions/A/ReactiveObjC',
    'Squirrel.framework/Versions/A/Squirrel',
    'Squirrel.framework/Versions/A/Resources/ShipIt',
  ];

  // Helper apps
  const helpersToFix = [
    'XIVTheCalamity Helper.app',
    'XIVTheCalamity Helper (GPU).app',
    'XIVTheCalamity Helper (Renderer).app',
    'XIVTheCalamity Helper (Plugin).app',
  ];

  // Re-sign frameworks with generic designated requirement
  for (const fw of frameworksToFix) {
    const fwPath = path.join(frameworksPath, fw);
    if (fs.existsSync(fwPath)) {
      try {
        console.log(`[AfterSign] Re-signing ${path.basename(fw)}...`);
        execSync(
          `codesign --force ${timestampFlag} --options runtime --entitlements "${entitlementsPath}" -r='designated => anchor apple generic' --sign "${signingIdentity}" "${fwPath}"`,
          { stdio: 'pipe' }
        );
      } catch (err) {
        console.error(`[AfterSign] Failed to re-sign ${fw}: ${err.message}`);
        throw err;
      }
    }
  }

  // Re-sign the framework bundles (after their internals are signed)
  const frameworkBundles = [
    'Electron Framework.framework',
    'Mantle.framework',
    'ReactiveObjC.framework',
    'Squirrel.framework',
  ];

  for (const fb of frameworkBundles) {
    const fbPath = path.join(frameworksPath, fb);
    if (fs.existsSync(fbPath)) {
      try {
        console.log(`[AfterSign] Re-signing ${fb} bundle...`);
        execSync(
          `codesign --force ${timestampFlag} --options runtime --entitlements "${entitlementsPath}" -r='designated => anchor apple generic' --sign "${signingIdentity}" "${fbPath}"`,
          { stdio: 'pipe' }
        );
      } catch (err) {
        console.error(`[AfterSign] Failed to re-sign ${fb}: ${err.message}`);
        throw err;
      }
    }
  }

  // Re-sign helper apps with generic designated requirement
  for (const helper of helpersToFix) {
    const helperPath = path.join(frameworksPath, helper);
    if (fs.existsSync(helperPath)) {
      try {
        console.log(`[AfterSign] Re-signing ${helper}...`);
        execSync(
          `codesign --force --deep ${timestampFlag} --options runtime --entitlements "${entitlementsPath}" -r='designated => anchor apple generic' --sign "${signingIdentity}" "${helperPath}"`,
          { stdio: 'pipe' }
        );
      } catch (err) {
        console.error(`[AfterSign] Failed to re-sign ${helper}: ${err.message}`);
        throw err;
      }
    }
  }

  // Finally, re-sign the main app with generic designated requirement
  console.log('[AfterSign] Re-signing main app bundle...');
  try {
    execSync(
      `codesign --force ${timestampFlag} --options runtime --entitlements "${entitlementsPath}" -r='designated => anchor apple generic' --sign "${signingIdentity}" "${appPath}"`,
      { stdio: 'pipe' }
    );
  } catch (err) {
    console.error(`[AfterSign] Failed to re-sign main app: ${err.message}`);
    throw err;
  }

  console.log('[AfterSign] ✅ Designated requirements fixed!');

  // Verify the final signature
  console.log('[AfterSign] Verifying final signature...');
  try {
    execSync(`codesign --verify --deep --verbose=2 "${appPath}"`, { stdio: 'pipe' });
    console.log('[AfterSign] ✅ Signature verification passed!');
  } catch (err) {
    console.error('[AfterSign] ❌ Signature verification failed!');
    console.error(err.stderr?.toString() || err.message);
    throw err;
  }
};
