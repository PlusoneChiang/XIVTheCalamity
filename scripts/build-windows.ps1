# XIVTheCalamity Windows Build Script (Photino Version)
# All-in-one script: Check environment, build frontend, compile backend (NativeAOT)

$ErrorActionPreference = "Stop"

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$PROJECT_ROOT = Split-Path -Parent $SCRIPT_DIR
$BACKEND_DIR = Join-Path $PROJECT_ROOT "backend"
$FRONTEND_DIR = Join-Path $PROJECT_ROOT "frontend"
$RELEASE_DIR = Join-Path $PROJECT_ROOT "Release"
$OUTPUT_DIR = Join-Path $RELEASE_DIR "win-x64"

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "  XIVTheCalamity - Windows Build (Photino)" -ForegroundColor Cyan
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
$backendProject = Join-Path $BACKEND_DIR "src\XIVTheCalamity\XIVTheCalamity.csproj"
if (Test-Path $backendProject) {
    Write-Host "   ✅ Backend project found" -ForegroundColor Green
} else {
    Write-Host "   ❌ Backend project not found" -ForegroundColor Red
    Write-Host "      Expected: $backendProject" -ForegroundColor Gray
    $allChecksPass = $false
}

if (-not $allChecksPass) {
    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Red
    Write-Host "❌ Pre-flight checks failed!" -ForegroundColor Red
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host "✅ All checks passed!" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host ""

# ================== Clean Release Directory ==================
Write-Host "🧹 Cleaning Release directory..." -ForegroundColor Yellow

if (Test-Path $OUTPUT_DIR) {
    Remove-Item -Path $OUTPUT_DIR -Recurse -Force -ErrorAction SilentlyContinue | Out-Null
    Write-Host "   ✅ Cleaned output directory" -ForegroundColor Green
} else {
    New-Item -ItemType Directory -Path $RELEASE_DIR -Force | Out-Null
}

# ================== Check Frontend Dependencies ==================
Write-Host ""
Write-Host "📦 Checking frontend dependencies..." -ForegroundColor Yellow
Set-Location $FRONTEND_DIR

if (Test-Path "node_modules") {
    Write-Host "   ✅ Dependencies already installed" -ForegroundColor Green
} else {
    Write-Host "   Installing dependencies..." -ForegroundColor Gray
    npm install --silent
    Write-Host "   ✅ Dependencies installed" -ForegroundColor Green
}

# ================== Build Frontend ==================
Write-Host ""
Write-Host "📦 Building Frontend (Vite)..." -ForegroundColor Yellow
npm run build:renderer

# ================== Copy Frontend to Backend ==================
Write-Host ""
Write-Host "   Copying frontend assets to C# wwwroot..." -ForegroundColor Gray
$wwwrootPath = Join-Path $BACKEND_DIR "src\XIVTheCalamity\wwwroot"
if (-not (Test-Path $wwwrootPath)) {
    New-Item -ItemType Directory -Path $wwwrootPath -Force | Out-Null
}
Copy-Item -Path (Join-Path $FRONTEND_DIR "dist\*") -Destination $wwwrootPath -Recurse -Force

# ================== Compile Backend ==================
Write-Host ""
Write-Host "🔨 Compiling backend (NativeAOT win-x64)..." -ForegroundColor Yellow
Write-Host "   This may take 2-5 minutes..." -ForegroundColor Gray

Set-Location $BACKEND_DIR

try {
    dotnet publish src\XIVTheCalamity\XIVTheCalamity.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        /p:PublishAot=true `
        -o $OUTPUT_DIR `
        --nologo
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✅ Backend compiled successfully" -ForegroundColor Green
    } else {
        Write-Host "   ❌ Backend compilation failed" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "   ❌ Backend compilation failed: $_" -ForegroundColor Red
    exit 1
}

# ================== Copy Shared Resources ==================
Write-Host ""
Write-Host "📦 Copying shared resources..." -ForegroundColor Yellow
$destResources = Join-Path $OUTPUT_DIR "resources"
Copy-Item -Path (Join-Path $PROJECT_ROOT "shared\resources\*") -Destination $destResources -Recurse -Force
Write-Host "   ✅ Resources copied" -ForegroundColor Green

# ================== Run Test ==================
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host "✅ Build completed successfully!" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host ""
Write-Host "📦 Location: $OUTPUT_DIR" -ForegroundColor Cyan
Write-Host ""

$runChoice = Read-Host "🧪 Run application now for testing? [y/N]"

if ($runChoice -match '^[Yy]$') {
    $configFile = Join-Path $env:APPDATA "XIVTheCalamity\config.json"
    if (Test-Path $configFile) {
        Write-Host "🔧 Enabling development mode in config..." -ForegroundColor Gray
        $configJson = Get-Content $configFile -Raw | ConvertFrom-Json
        $configJson.launcher.developmentMode = $true
        $configJson | ConvertTo-Json -Depth 10 | Out-File $configFile -Encoding utf8 -Force
        Write-Host "   ✅ Development mode enabled" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "🚀 Starting application..." -ForegroundColor Yellow
    
    $appExe = Join-Path $OUTPUT_DIR "XIVTheCalamity.exe"
    if (Test-Path $appExe) {
        Start-Process -FilePath $appExe
        Write-Host "   ✅ Application started!" -ForegroundColor Green
    } else {
        Write-Host "   ❌ Executable not found" -ForegroundColor Red
    }
} else {
    Write-Host ""
    Write-Host "💡 You can run it manually with:" -ForegroundColor Cyan
    Write-Host "   $(Join-Path $OUTPUT_DIR "XIVTheCalamity.exe")" -ForegroundColor White
}

Set-Location $PROJECT_ROOT
Write-Host ""
Write-Host "✅ All done!" -ForegroundColor Green
Write-Host ""
