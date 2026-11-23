#!/bin/bash
set -e
cd "$(dirname "$0")"
APP="Plana.app"

if [ ! -d "$APP" ]; then
  echo "未找到 Plana.app，请确认压缩包已完整解压。"
  read -n 1 -s -r -p "按任意键退出..."
  exit 1
fi

echo "正在为 Plana 做首次启动准备..."

# 移除隔离属性
xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true
xattr -cr "$APP" 2>/dev/null || true

# 压缩包跨平台打包，补上可执行权限
chmod +x "$APP/Contents/MacOS/"* 2>/dev/null || true

# Apple Silicon 要求所有 Mach-O 至少有 ad-hoc 签名
find "$APP/Contents" -type f -print0 | while IFS= read -r -d '' file; do
  if file "$file" | grep -q "Mach-O"; then
    codesign --force --sign - "$file" >/dev/null 2>&1 || true
  fi
done

# 对整个 .app 再签一次
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true

echo "准备完成，正在启动 Plana..."
open "$APP"
