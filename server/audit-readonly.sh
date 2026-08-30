#!/usr/bin/env bash
set -Eeuo pipefail

echo '== OS =='
uname -a
test -r /etc/os-release && sed -n 's/^\(NAME\|VERSION\|ID\)=/\1=/p' /etc/os-release
echo '== CPU / RAM / disk =='
nproc
free -h
df -h --output=source,size,used,avail,pcent,target
echo '== Docker =='
docker --version 2>/dev/null || true
systemctl is-active docker 2>/dev/null || true
docker ps --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}' 2>/dev/null || true
docker system df 2>/dev/null || true
echo '== PM2 =='
pm2 list 2>/dev/null || true
echo '== Listening ports =='
ss -lntup 2>/dev/null || true
echo '== Nginx =='
systemctl is-active nginx 2>/dev/null || true
find /etc/nginx/sites-enabled -maxdepth 1 -type l -printf '%f\n' 2>/dev/null || true
nginx -t 2>&1 || true
