const VALID_THEMES = ['dark', 'light', 'valentine'];

/**
 * Apply a theme by setting data-theme attribute on <html>.
 * Falls back to 'dark' if the theme name is unknown.
 * @param {string} theme - 'dark' | 'light' | 'valentine'
 */
export function applyTheme(theme) {
  const t = VALID_THEMES.includes(theme) ? theme : 'dark';
  document.documentElement.setAttribute('data-theme', t);
}
