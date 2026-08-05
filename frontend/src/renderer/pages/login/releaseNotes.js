function formatInline(text) {
  return text
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/`(.+?)`/g, '<code>$1</code>');
}

/** Extract the selected locale from a GitHub Release Markdown body. */
export function parseReleaseNotes(releaseNotes, locale) {
  if (Array.isArray(releaseNotes)) {
    releaseNotes = releaseNotes[0]?.note || releaseNotes[0] || '';
  }
  if (typeof releaseNotes !== 'string') return '';

  const changelog = releaseNotes.split(/\n---\s*(?:\n|$)/)[0];
  const headings = [...changelog.matchAll(/^####\s+(?:🇹🇼\s*zh-TW|🇺🇸\s*English)\s*$/gmi)];
  const pattern = locale === 'en' || locale === 'en-US' ? /🇺🇸\s*English/i : /🇹🇼\s*zh-TW/i;
  const headingIndex = headings.findIndex(heading => pattern.test(heading[0]));
  if (headingIndex < 0) return '';

  const start = headings[headingIndex].index + headings[headingIndex][0].length;
  const end = headings[headingIndex + 1]?.index ?? changelog.length;

  return changelog.substring(start, end).trim().split('\n').map(line => {
    if (/^###\s+/.test(line)) return `<div class="notes-heading">${formatInline(line.replace(/^###\s+/, ''))}</div>`;
    if (/^-\s+/.test(line)) return `<div class="notes-item">• ${formatInline(line.replace(/^-\s+/, ''))}</div>`;
    return line.trim() ? `<div>${formatInline(line)}</div>` : '';
  }).join('');
}
