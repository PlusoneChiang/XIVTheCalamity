# XIVTheCalamity Windows Build Script
# All-in-one script: Check environment, build backend, package frontend, create installer

$ErrorActionPreference = "Stop"

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$PROJECT_ROOT = Split-Path -Parent $SCRIPT_DIR
$BACKEND_DIR = Join-Path $PROJECT_ROOT "backend"
$FRONTEND_DIR = Join-Path $PROJECT_ROOT "frontend"
$RELEASE_DIR = Join-Path $PROJECT_ROOT "Release"
$BUILD_DIR = Join-Path $FRONTEND_DIR "build"

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "  XIVTheCalamity - Windows Build" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host ""

function Test-Command {
    param($Command)
    try {
        if (Get-Command $Command -ErrorAction SilentlyContinue) {
            return $true
        }
    } catch {
        return $false
    }
    return $false
}

# ================== Pre-flight Checks ==================
Write-Host "🔍 Pre-flight checks..." -ForegroundColor Yellow
Write-Host ""

$allChecksPass = $true

# Check Node.js
if (Test-Command "node") {
    $nodeVersion = (node --version)
    Write-Host "   ✅ Node.js $nodeVersion" -ForegroundColor Green
} else {
    Write-Host "   ❌ Node.js not installed" -ForegroundColor Red
    Write-Host "      Install from: https://nodejs.org/" -ForegroundColor Gray
    $allChecksPass = $false
}

# Check npm
if (Test-Command "npm") {
    $npmVersion = (npm --version)
    Write-Host "   ✅ npm $npmVersion" -ForegroundColor Green
} else {
    Write-Host "   ❌ npm not installed" -ForegroundColor Red
    $allChecksPass = $false
}

# Check .NET
if (Test-Command "dotnet") {
    $dotnetVersion = (dotnet --version)
    Write-Host "   ✅ .NET $dotnetVersion" -ForegroundColor Green
} else {
    Write-Host "   ❌ .NET not installed" -ForegroundColor Red
    Write-Host "      Install from: https://dotnet.microsoft.com/download" -ForegroundColor Gray
    $allChecksPass = $false
}

# Check backend project
$backendProject = Join-Path $BACKEND_DIR "src\XIVTheCalamity.Api.NativeAOT\XIVTheCalamity.Api.NativeAOT.csproj"
if (Test-Path $backendProject) {
    Write-Host "   ✅ Backend project found" -ForegroundColor Green
} else {
    Write-Host "   ❌ Backend project not found" -ForegroundColor Red
    Write-Host "      Expected: $backendProject" -ForegroundColor Gray
    $allChecksPass = $false
}

# Check shared resources (optional)
$sharedResourcesDir = Join-Path $PROJECT_ROOT "shared\resources"
if (Test-Path $sharedResourcesDir) {
    $resourceCount = (Get-ChildItem -Path $sharedResourcesDir -Recurse -File).Count
    Write-Host "   ✅ Shared resources ($resourceCount files)" -ForegroundColor Green
} else {
    Write-Host "   ⚠️  Shared resources not found (optional)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "   💡 Note: icon.ico should be in frontend/build/" -ForegroundColor Gray
Write-Host "      (electron-builder will check it during build)" -ForegroundColor Gray

if (-not $allChecksPass) {
    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Red
    Write-Host "❌ Pre-flight checks failed!" -ForegroundColor Red
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please fix the issues above and try again." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host "✅ All checks passed!" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host ""

# ================== Clean Release Directory ==================
Write-Host ""
Write-Host "🧹 Cleaning Release directory..." -ForegroundColor Yellow

if (Test-Path $RELEASE_DIR) {
    # Remove old Windows build
    $winUnpackedPath = Join-Path $RELEASE_DIR "win-unpacked"
    if (Test-Path $winUnpackedPath) {
        Remove-Item -Path $winUnpackedPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    # Remove old installers
    Get-ChildItem -Path $RELEASE_DIR -Filter "*.exe" | Remove-Item -Force -ErrorAction SilentlyContinue
    
    # Remove old temp backend
    $tempBackendPath = Join-Path $RELEASE_DIR "temp-backend-windows"
    if (Test-Path $tempBackendPath) {
        Remove-Item -Path $tempBackendPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    Write-Host "   ✅ Cleaned release directory" -ForegroundColor Green
} else {
    New-Item -ItemType Directory -Path $RELEASE_DIR -Force | Out-Null
    Write-Host "   ✅ Created Release directory" -ForegroundColor Green
}

# ================== Compile Backend ==================
Write-Host ""
Write-Host "🔨 Compiling backend (NativeAOT win-x64)..." -ForegroundColor Yellow
Write-Host "   This may take 2-5 minutes..." -ForegroundColor Gray

Set-Location $BACKEND_DIR

$publishOutput = Join-Path $RELEASE_DIR "temp-backend-windows"

try {
    dotnet publish src/XIVTheCalamity.Api.NativeAOT/XIVTheCalamity.Api.NativeAOT.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $publishOutput `
        /p:PublishTrimmed=true `
        /p:PublishSingleFile=true `
        --nologo
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✅ Backend compiled successfully" -ForegroundColor Green
        
        # Remove PDB files (NativeAOT always generates them despite settings)
        $pdbFiles = Get-ChildItem -Path $publishOutput -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue
        if ($pdbFiles) {
            foreach ($pdb in $pdbFiles) {
                Remove-Item $pdb.FullName -Force -ErrorAction SilentlyContinue
                Write-Host "   🗑️  Removed: $($pdb.Name)" -ForegroundColor Gray
            }
        }
        
        # Check backend executable
        $backendExe = Join-Path $publishOutput "XIVTheCalamity.Api.NativeAOT.exe"
        if (Test-Path $backendExe) {
            $backendSize = [math]::Round((Get-Item $backendExe).Length / 1MB, 2)
            Write-Host "   📦 Backend size: $backendSize MB" -ForegroundColor Cyan
        }
    } else {
        Write-Host "   ❌ Backend compilation failed" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "   ❌ Backend compilation failed: $_" -ForegroundColor Red
    exit 1
}

# ================== Configure Build ==================
Write-Host ""
Write-Host "📝 Configuring build..." -ForegroundColor Yellow

Set-Location $FRONTEND_DIR

# Read version from package.json
$packageJson = Get-Content "package.json" -Raw | ConvertFrom-Json
$VERSION = $packageJson.version
Write-Host "   📦 Version: $VERSION" -ForegroundColor Cyan

# Create inline electron-builder config for Windows
# Use single quotes to avoid PowerShell variable expansion
$builderConfig = @'
module.exports = {
    appId: 'com.xivthecalamity.launcher',
    productName: 'XIVTheCalamity',
    artifactName: '${productName}-${version}-${os}-${arch}.${ext}',
    electronVersion: '40.0.0',
    directories: {
        output: '../Release',
        buildResources: 'build'
    },
    files: [
        'src/**/*',
        'resources/**/*',
        'package.json',
        'node_modules/**/*'
    ],
    win: {
        target: [{ target: 'nsis', arch: ['x64'] }],
        icon: 'build/icon.ico'
    },
    nsis: {
        oneClick: false,
        allowToChangeInstallationDirectory: true,
        allowElevation: true,
        createDesktopShortcut: true,
        createStartMenuShortcut: true,
        perMachine: false,
        deleteAppDataOnUninstall: false,
        runAfterFinish: true,
        menuCategory: false,
        shortcutName: 'XIV The Calamity'
    },
    extraResources: [
        {
            from: '../Release/temp-backend-windows',
            to: 'backend',
            filter: ['**/*']
        },
        {
            from: '../shared/resources',
            to: 'resources',
            filter: ['**/*']
        }
    ]
};
'@

# Write config file
$builderConfig | Out-File -FilePath "electron-builder.config.js" -Encoding utf8 -Force
Write-Host "   ✅ Build configuration created" -ForegroundColor Green

# ================== Check Frontend Dependencies ==================
Write-Host ""
Write-Host "📦 Checking frontend dependencies..." -ForegroundColor Yellow

if (Test-Path "node_modules") {
    Write-Host "   ✅ Dependencies already installed" -ForegroundColor Green
} else {
    Write-Host "   Installing dependencies..." -ForegroundColor Gray
    npm install --silent 2>&1 | Out-Null
    Write-Host "   ✅ Dependencies installed" -ForegroundColor Green
}

# ================== Build Installer ==================
Write-Host ""
Write-Host "📦 Building Windows installer..." -ForegroundColor Yellow
Write-Host "   Version: $VERSION" -ForegroundColor Cyan
Write-Host "   This may take 5-10 minutes..." -ForegroundColor Gray
Write-Host ""

try {
    # Build without code signing
    $env:CSC_IDENTITY_AUTO_DISCOVERY = "false"
    
    npx electron-builder --win --x64 --config electron-builder.config.js
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
        Write-Host "✅ Build completed successfully!" -ForegroundColor Green
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
        Write-Host ""
        
        # Find the installer
        $installerPattern = "XIVTheCalamity-*-win-*.exe"
        $installer = Get-ChildItem -Path $RELEASE_DIR -Filter $installerPattern | Select-Object -First 1
        
        if ($installer) {
            $installerSize = [math]::Round($installer.Length / 1MB, 2)
            Write-Host "📦 Installer: $($installer.Name)" -ForegroundColor Cyan
            Write-Host "📏 Size: $installerSize MB" -ForegroundColor Cyan
            Write-Host "📂 Location: $($installer.FullName)" -ForegroundColor Cyan
            Write-Host ""
            
            # Check unpacked directory
            $unpackedDir = Join-Path $RELEASE_DIR "win-unpacked"
            if (Test-Path $unpackedDir) {
                $unpackedSize = [math]::Round((Get-ChildItem -Path $unpackedDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
                Write-Host "📊 Unpacked size: $unpackedSize MB" -ForegroundColor Cyan
                
                # Verify backend
                $backendExe = Join-Path $unpackedDir "resources\backend\XIVTheCalamity.Api.NativeAOT.exe"
                if (Test-Path $backendExe) {
                    Write-Host "✅ Backend verified" -ForegroundColor Green
                } else {
                    Write-Host "⚠️  Backend not found in package" -ForegroundColor Yellow
                }
            }
            
            Write-Host ""
            Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
            Write-Host ""
            
            # Ask to run unpacked version
            $runChoice = Read-Host "🧪 Run unpacked version for testing? [y/N]"
            
            if ($runChoice -match '^[Yy]$') {
                Write-Host ""
                Write-Host "🚀 Starting unpacked application..." -ForegroundColor Yellow
                Write-Host ""
                
                $unpackedExe = Join-Path $unpackedDir "XIVTheCalamity.exe"
                if (Test-Path $unpackedExe) {
                    Start-Process -FilePath $unpackedExe
                    Write-Host "✅ Application started!" -ForegroundColor Green
                    Write-Host ""
                    Write-Host "   Logs: $env:APPDATA\XIVTheCalamity\logs\" -ForegroundColor Cyan
                } else {
                    Write-Host "❌ Unpacked executable not found" -ForegroundColor Red
                }
            } else {
                Write-Host ""
                Write-Host "💡 Manual testing options:" -ForegroundColor Cyan
                Write-Host ""
                Write-Host "   Run installer:" -ForegroundColor Gray
                Write-Host "   $($installer.FullName)" -ForegroundColor White
                Write-Host ""
                Write-Host "   Test without installing:" -ForegroundColor Gray
                $unpackedExe = Join-Path $unpackedDir "XIVTheCalamity.exe"
                if (Test-Path $unpackedExe) {
                    Write-Host "   $unpackedExe" -ForegroundColor White
                }
            }
        } else {
            Write-Host "⚠️  Installer not found in expected location" -ForegroundColor Yellow
            Write-Host "   Check $RELEASE_DIR for output files" -ForegroundColor Gray
        }
    } else {
        Write-Host ""
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Red
        Write-Host "❌ Build failed!" -ForegroundColor Red
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Red
        Write-Host ""
        Write-Host "Please check the error messages above." -ForegroundColor Yellow
        
        # Clean up temp config on failure
        Remove-Item -Path "electron-builder.config.js" -Force -ErrorAction SilentlyContinue
        Set-Location $PROJECT_ROOT
        
        exit 1
    }
} catch {
    Write-Host ""
    Write-Host "❌ Build failed: $_" -ForegroundColor Red
    
    # Clean up temp config on failure
    Set-Location $FRONTEND_DIR
    Remove-Item -Path "electron-builder.config.js" -Force -ErrorAction SilentlyContinue
    Set-Location $PROJECT_ROOT
    
    exit 1
}

# ================== Clean Temporary Files ==================
Write-Host ""
Write-Host "🧹 Cleaning temporary files..." -ForegroundColor Yellow

# Remove temp backend
$tempBackendPath = Join-Path $RELEASE_DIR "temp-backend-windows"
if (Test-Path $tempBackendPath) {
    Remove-Item -Path $tempBackendPath -Recurse -Force -ErrorAction SilentlyContinue
}

# Remove temp config
Set-Location $FRONTEND_DIR
$tempConfigPath = "electron-builder.config.js"
if (Test-Path $tempConfigPath) {
    Remove-Item -Path $tempConfigPath -Force -ErrorAction SilentlyContinue
}

Write-Host "   ✅ Cleanup complete" -ForegroundColor Green

Set-Location $PROJECT_ROOT

Write-Host ""
Write-Host "✅ All done!" -ForegroundColor Green
Write-Host ""
