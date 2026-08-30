#!/usr/bin/env bash
set -Eeuo pipefail

test "$(id -u)" -eq 0 || { echo 'Run as root.' >&2; exit 1; }
install -d -o root -g root -m 0755 /opt/sael
install -d -o root -g root -m 0700 /etc/sael
install -d -o root -g root -m 0700 /var/lib/sael
echo 'Prepared isolated /opt/sael and /etc/sael. No existing service was modified.'
echo 'Copy backend files to /opt/sael and secrets to /etc/sael/backend.env manually.'
