#!/bin/sh
# 路徑一律由參數傳入，避免使用者自訂檔名被當成 Shell 指令。
old_pid=$1
staged=$2
target=$3
log=$4
downloaded=$5

exec >> "$log" 2>&1 < /dev/null
cleanup() {
    rm -f -- "$staged" "$0"
    rmdir -- "$(dirname "$0")"
}
trap cleanup EXIT
printf '\n[%s] 等待舊程序 %s 結束，更新目標：%s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "$old_pid" "$target"
attempt=0
while kill -0 "$old_pid" 2>/dev/null; do
    if [ "$attempt" -ge 60 ]; then
        echo '更新失敗：等待舊程序退出逾時，未替換原檔。'
        exit 1
    fi
    sleep 1
    attempt=$((attempt + 1))
done

if [ ! -f "$target" ] || [ -L "$target" ] || [ ! -s "$staged" ] || [ ! -x "$staged" ]; then
    echo '更新失敗：原始檔或新版檔案狀態異常，未替換原檔。'
    exit 1
fi
# 新版已準備在原檔所在檔案系統，重新命名時不必跨檔案系統複製。
if ! mv -f -- "$staged" "$target"; then
    echo '更新失敗：無法替換原始 AppImage，不會啟動暫存檔。'
    exit 1
fi
printf '[%s] 已替換 AppImage，準備啟動：%s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "$target"
if [ "$downloaded" != "$target" ]; then
    rm -f -- "$downloaded" || echo '無法清除下載暫存檔；不影響已完成的替換。'
fi
cleanup
trap - EXIT
exec "$target"
