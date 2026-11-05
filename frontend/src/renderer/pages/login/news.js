import { apiFetch } from '../../utils/polyfill.js';
import i18n from '../../i18n/index.js';

const OFFICIAL_NEWS_URL = 'https://user-cdn.ffxiv.com.tw/news/251115/launcher_left.html';
const LOCAL_LINK_HOSTS = new Set(['www.ffxiv.com.tw', 'event.ffxiv.com.tw']);
const LOCAL_IMAGE_HOSTS = new Set(['file.ffxiv.com.tw']);
const CATEGORIES = ['最新', '活動', '更新', '維護', '其他'];

function isLocalUrl(value, hosts) {
  try {
    const url = new URL(value);
    return url.protocol === 'https:'
      && url.port === ''
      && url.username === ''
      && url.password === ''
      && hosts.has(url.hostname);
  } catch {
    return false;
  }
}

function isLocalLink(value) {
  return isLocalUrl(value, LOCAL_LINK_HOSTS);
}

function isLocalImage(value) {
  return isLocalUrl(value, LOCAL_IMAGE_HOSTS);
}

function isExternalUrl(value) {
  try {
    const url = new URL(value);
    return (url.protocol === 'https:' || url.protocol === 'http:') && url.username === '' && url.password === '';
  } catch {
    return false;
  }
}

function openExternal(url, validator = isLocalLink) {
  if (validator(url)) window.xivtc.openExternal(url);
}

function localizedText(key) {
  const english = i18n.locale === 'en-US';
  return english
    ? ({ loading: 'Loading official announcements…', unavailable: 'Official announcements are currently unavailable.', retry: 'Retry', empty: 'No announcements are available.' })[key]
    : ({ loading: '載入官方公告中…', unavailable: '目前無法載入官方公告。', retry: '重新整理', empty: '目前沒有可顯示的公告。' })[key];
}

function button(text, className, onClick) {
  const element = document.createElement('button');
  element.type = 'button';
  element.className = className;
  element.textContent = text;
  element.addEventListener('click', onClick);
  return element;
}

function showError(container) {
  container.replaceChildren();
  const status = document.createElement('div');
  status.className = 'local-news-status local-news-error';
  status.textContent = localizedText('unavailable');
  status.append(document.createElement('br'), button(localizedText('retry'), '', () => loadLocalNews(container)));
  container.append(status);
}

function renderNews(container, news) {
  const banners = (news.banners || []).filter(banner => isLocalImage(banner.imageUrl) && isLocalLink(banner.url));
  const items = (news.items || []).filter(item => typeof item.title === 'string' && isLocalLink(item.url));
  if (items.length === 0) return showError(container);

  container.replaceChildren();
  if (banners.length) {
    let index = 0;
    const banner = document.createElement('div');
    banner.className = 'local-news-banner';
    const link = document.createElement('a');
    let image = document.createElement('img');
    let transitioning = false;
    let dots;
    image.alt = '';
    link.href = '#';
    link.setAttribute('aria-label', i18n.locale === 'en-US' ? 'Open event announcement' : '查看活動公告');
    link.append(image);
    link.addEventListener('click', event => { event.preventDefault(); openExternal(banners[index].url); });
    const showBanner = async nextIndex => {
      const targetIndex = (nextIndex + banners.length) % banners.length;
      if (transitioning || (image.hasAttribute('src') && targetIndex === index)) return;
      transitioning = true;
      const incoming = document.createElement('img');
      incoming.alt = '';
      incoming.src = banners[targetIndex].imageUrl;
      try {
        await incoming.decode();
      } catch (error) {
        console.warn('[News] Failed to load banner:', error);
        transitioning = false;
        return;
      }
      const outgoing = image;
      const direction = nextIndex < index ? -1 : 1;
      index = targetIndex;
      link.href = banners[index].url;
      link.append(incoming);
      image = incoming;
      dots.querySelectorAll('button').forEach((dot, dotIndex) => {
        dot.classList.toggle('active', dotIndex === index);
        dot.setAttribute('aria-pressed', String(dotIndex === index));
      });
      if (outgoing.hasAttribute('src') && typeof incoming.animate === 'function' && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        const options = { duration: 400, easing: 'ease-in-out', fill: 'forwards' };
        const animations = [
          outgoing.animate([{ opacity: 1, transform: 'translateX(0)' }, { opacity: 0, transform: `translateX(${-24 * direction}px)` }], options),
          incoming.animate([{ opacity: 0, transform: `translateX(${24 * direction}px)` }, { opacity: 1, transform: 'translateX(0)' }], options)
        ];
        await Promise.all(animations.map(animation => animation.finished.catch(() => {})));
        animations.forEach(animation => animation.cancel());
      }
      outgoing.remove();
      transitioning = false;
    };
    banner.append(link);
    if (banners.length > 1) {
      const previous = button('‹', 'previous', () => showBanner(index - 1));
      previous.setAttribute('aria-label', i18n.locale === 'en-US' ? 'Previous banner' : '上一則橫幅');
      const next = button('›', 'next', () => showBanner(index + 1));
      next.setAttribute('aria-label', i18n.locale === 'en-US' ? 'Next banner' : '下一則橫幅');
      banner.append(previous, next);
      dots = document.createElement('div');
      dots.className = 'local-news-dots';
      banners.forEach((_, dotIndex) => {
        const dot = button('', dotIndex === 0 ? 'active' : '', () => showBanner(dotIndex));
        dot.setAttribute('aria-label', i18n.locale === 'en-US' ? `Banner ${dotIndex + 1}` : `第 ${dotIndex + 1} 則橫幅`);
        dot.setAttribute('aria-pressed', String(dotIndex === 0));
        dots.append(dot);
      });
      banner.append(dots);
    }
    dots ??= document.createElement('div');
    showBanner(0);
    if (banners.length > 1 && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      const timer = setInterval(() => {
        if (!banner.isConnected) clearInterval(timer);
        else if (!document.hidden && !banner.matches(':hover, :focus-within')) showBanner(index + 1);
      }, 5000);
    }
    container.append(banner);
  }

  const groups = new Map(CATEGORIES.map(category => [category, category === '最新'
    ? items.slice(0, 10)
    : items.filter(item => item.category === category)]));
  const tabs = document.createElement('div');
  tabs.className = 'local-news-tabs';
  const list = document.createElement('div');
  list.className = 'local-news-list';
  const renderItems = category => {
    list.replaceChildren();
    const selectedItems = groups.get(category);
    if (selectedItems.length === 0) {
      const empty = document.createElement('p');
      empty.className = 'local-news-status';
      empty.textContent = localizedText('empty');
      list.append(empty);
    }
    for (const item of selectedItems) {
      const row = document.createElement('div');
      row.className = 'local-news-item';
      const date = document.createElement('span'); date.className = 'local-news-date'; date.textContent = item.date || '';
      const type = document.createElement('span'); type.className = 'local-news-category'; type.textContent = item.category || '';
      const title = button('', 'local-news-title', () => openExternal(item.url));
      title.title = item.title;
      if (item.isPinned === true) {
        const badge = document.createElement('span');
        badge.className = 'local-news-pinned';
        badge.textContent = '置頂';
        title.append(badge);
      }
      const text = document.createElement('span');
      text.className = 'local-news-title-text';
      text.textContent = item.title;
      title.append(text);
      row.append(date, type, title);
      list.append(row);
    }
    tabs.querySelectorAll('button').forEach(tab => {
      const selected = tab.dataset.category === category;
      tab.classList.toggle('active', selected);
      tab.setAttribute('aria-pressed', String(selected));
    });
  };
  for (const category of CATEGORIES) {
    const tab = button(category, '', () => renderItems(category));
    tab.dataset.category = category;
    tab.setAttribute('aria-pressed', 'false');
    tabs.append(tab);
  }
  renderItems('最新');
  container.append(tabs, list);
  if (isLocalLink(news.moreUrl)) {
    const footer = document.createElement('div');
    footer.className = 'local-news-footer';
    footer.append(button('更多公告 ›', 'local-news-more', () => openExternal(news.moreUrl)));
    container.append(footer);
  }
}

async function loadLocalNews(container) {
  container.replaceChildren();
  const status = document.createElement('p');
  status.className = 'local-news-status';
  status.textContent = localizedText('loading');
  container.append(status);
  try {
    const response = await apiFetch('http://localhost:5050/api/news');
    const payload = await response.json();
    if (!response.ok || !payload.success || !payload.data) throw new Error('News request failed');
    renderNews(container, payload.data);
  } catch (error) {
    console.warn('[News] Failed to load local news:', error);
    showError(container);
  }
}

function setupOfficialNews(section) {
  section.replaceChildren();
  const frame = document.createElement('iframe');
  frame.id = 'newsFrame';
  frame.src = OFFICIAL_NEWS_URL;
  frame.setAttribute('frameborder', '0');
  frame.setAttribute('allowtransparency', 'true');
  section.append(frame);
  const attachListener = () => {
    try {
      const documentInFrame = frame.contentDocument || frame.contentWindow.document;
      if (!documentInFrame?.documentElement || documentInFrame.documentElement.dataset.clickIntercepted) return;
      documentInFrame.documentElement.dataset.clickIntercepted = 'true';
      documentInFrame.addEventListener('click', event => {
        const anchor = event.target.closest('a');
        if (anchor?.href && isExternalUrl(anchor.href)) { event.preventDefault(); openExternal(anchor.href, isExternalUrl); }
      });
    } catch (error) { console.warn('[News] Cannot intercept official announcement links:', error); }
  };
  frame.addEventListener('load', attachListener);
  attachListener();
  let attempts = 0;
  const interval = setInterval(() => {
    attachListener();
    if (++attempts >= 20) clearInterval(interval);
  }, 1000);
}

export function initNews() {
  const section = document.getElementById('newsSection');
  const local = document.getElementById('localNews');
  if (!section || !local) return;
  const mode = document.querySelector('meta[name="xivtc-announcement-mode"]')?.content;
  if (mode === 'official') setupOfficialNews(section);
  else loadLocalNews(local);
}
