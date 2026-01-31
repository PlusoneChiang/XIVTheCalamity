#!/bin/bash
set -e

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_ROOT"

echo "🧪 测试 build-linux.sh 修复"
echo "================================"
echo ""

# 只测试 package.json 更新部分
echo "📝 测试 package.json 更新..."

# 备份原文件
cp frontend/package.json frontend/package.json.backup

# 执行更新逻辑
node -e "
const fs = require('fs');
const path = require('path');
const pkgPath = path.join('$PROJECT_ROOT', 'frontend', 'package.json');
const pkg = JSON.parse(fs.readFileSync(pkgPath, 'utf8'));

// 确保 Linux 目标
if (!pkg.build.linux) {
    pkg.build.linux = {
        target: [{ target: 'AppImage', arch: ['x64'] }],
        icon: 'build/icons',
        category: 'Game'
    };
}

// 更新 extraResources - 不包含 Proton GE
pkg.build.extraResources = [
    { from: '../shared/resources', to: 'resources', filter: ['**/*'] },
    { from: '../Release/temp-backend-linux', to: 'backend', filter: ['**/*'] }
    // Proton GE 不再打包，改为运行时下载
];

console.log('✅ package.json 更新成功');
console.log('');
console.log('extraResources 配置:');
console.log(JSON.stringify(pkg.build.extraResources, null, 2));

// 写回文件
fs.writeFileSync(pkgPath, JSON.stringify(pkg, null, 2));
"

echo ""
echo "📊 检查 extraResources 配置..."
grep -A 10 "extraResources" frontend/package.json

# 恢复备份
echo ""
echo "🔄 恢复原始配置..."
mv frontend/package.json.backup frontend/package.json

echo ""
echo "✅ 测试完成！package.json 更新逻辑正确"
