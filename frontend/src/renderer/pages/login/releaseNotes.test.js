import assert from 'node:assert/strict';
import { parseReleaseNotes } from './releaseNotes.js';

const notes = `#### 🇹🇼 zh-TW
### 🐛 錯誤修正
- **macOS**：修復 \`codec\` <error>

#### 🇺🇸 English
### 🐛 Bug Fixes
- Fixed playback
---
Download guide`;

assert.equal(parseReleaseNotes(notes, 'zh-TW'), '<div class="notes-heading">🐛 錯誤修正</div><div class="notes-item">• <strong>macOS</strong>：修復 <code>codec</code> &lt;error&gt;</div>');
assert.equal(parseReleaseNotes(notes, 'en-US'), '<div class="notes-heading">🐛 Bug Fixes</div><div class="notes-item">• Fixed playback</div>');
assert.equal(parseReleaseNotes('no localized notes', 'zh-TW'), '');
