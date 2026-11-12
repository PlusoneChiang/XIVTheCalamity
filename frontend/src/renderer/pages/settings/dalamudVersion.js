const legacyVersionPattern = /^v?(?<upstream>\d+\.\d+\.\d+\.\d+)-tc\.(?<tc>[1-9]\d*)(?:-rc\.(?<preRelease>[1-9]\d*))?$/;

function positiveInteger(value) {
  return Number.isInteger(value) && value > 0 ? value : null;
}

/** 將本機已安裝版本狀態轉為可顯示欄位。 */
export function getDalamudVersionDisplay(status) {
  if (!status || typeof status !== 'object' || Array.isArray(status)) return { error: true };

  const full = typeof status?.localVersion === 'string' ? status.localVersion.trim() : '';
  if (!full) return { installed: false };

  const legacy = legacyVersionPattern.exec(full)?.groups;
  const info = status.localVersionInfo || {};
  const upstream = typeof info.upstreamVersion === 'string' && info.upstreamVersion.trim()
    ? info.upstreamVersion.trim()
    : legacy?.upstream || null;

  return {
    installed: true,
    upstream,
    tcRevision: positiveInteger(info.tcRevision) ?? (legacy ? Number(legacy.tc) : null),
    preReleaseRevision: positiveInteger(info.preReleaseRevision) ?? (legacy?.preRelease ? Number(legacy.preRelease) : null)
  };
}
