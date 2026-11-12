const apiToken = document.querySelector('meta[name="xivtc-api-token"]')?.content || '';
const readyTimeoutMs = 10000;
const newId = () => globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random()}`;
const message = (value) => window.external.sendMessage(JSON.stringify({ ...value, token: apiToken }));

function event(type, data) {
  return { type, data: JSON.stringify(data ?? null) };
}

function errorData(data) {
  const message = data?.error?.message || data?.message || data?.errorMessage;
  return message ? { ...data, message, errorMessage: data.errorMessage || message } : data;
}

function createClient(send, clientId = newId()) {
  const pending = new Map();
  const streams = new Map();
  let released = false;

  const notify = (value, shouldSend) => {
    try { Promise.resolve(send(value, shouldSend)).catch(() => {}); } catch { /* 頁面卸載時原生橋接可能已移除。 */ }
  };
  const cancel = (id) => notify({ type: 'cancel', id, clientId });
  const fail = (error) => {
    pending.forEach(({ reject }) => reject(error));
    pending.clear();
    streams.forEach(stream => {
      stream.dispatch('error', event('error', { message: error.message, errorMessage: error.message }));
      stream.finish();
    });
    streams.clear();
  };

  const receive = (data) => {
    if (data.clientId !== clientId) return;
    if (data.type === 'response') {
      notify({ type: 'ack', id: data.id, clientId });
      const request = pending.get(data.id);
      if (!request) return;
      pending.delete(data.id);
      request.resolve(response(data.status, data.data, data.statusText));
      return;
    }
    if (data.type === 'end') notify({ type: 'ack', id: data.id, clientId });
    const stream = streams.get(data.id);
    if (!stream) return;
    if (data.type === 'event') {
      const eventType = data.eventType || 'message';
      if (eventType === 'open') {
        if (stream.readyState !== 0) return;
        stream.readyState = 1;
      }
      stream.dispatch(eventType, event(eventType, eventType === 'error' ? errorData(data.data) : data.data));
    }
    if (data.type === 'end') {
      streams.delete(data.id);
      stream.ending = true;
      if (data.status < 200 || data.status >= 300) stream.dispatch('error', event('error', errorData(data.data)));
      stream.finish();
    }
  };

  const request = async (method, path, body, signal) => {
    if (released) throw new Error('IPC 頁面已關閉。');
    const id = newId();
    if (signal?.aborted) throw new DOMException('作業已取消。', 'AbortError');
    return new Promise((resolve, reject) => {
      const state = {
        sent: false,
        resolve: value => { signal?.removeEventListener('abort', abort); resolve(value); },
        reject: error => { signal?.removeEventListener('abort', abort); reject(error); }
      };
      const abort = () => {
        const active = pending.get(id);
        pending.delete(id);
        if (active?.sent) cancel(id);
        reject(new DOMException('作業已取消。', 'AbortError'));
      };
      signal?.addEventListener('abort', abort, { once: true });
      pending.set(id, state);
      Promise.resolve(send({ type: 'request', id, clientId, method, path, ...(body === undefined ? {} : { body }) }, () => {
        const active = pending.get(id);
        if (!active) return false;
        active.sent = true;
        return true;
      })).catch(error => {
        if (pending.get(id) !== state) return;
        pending.delete(id);
        state.reject(error);
      });
    });
  };

  const openEventStream = (path) => {
    if (released) throw new Error('IPC 頁面已關閉。');
    const id = newId();
    const listeners = new Map();
    const stream = {
      CONNECTING: 0,
      OPEN: 1,
      CLOSED: 2,
      readyState: 0,
      onopen: null,
      onmessage: null,
      onerror: null,
      sent: false,
      ending: false,
      addEventListener(type, listener) {
        if (!listeners.has(type)) listeners.set(type, new Set());
        listeners.get(type).add(listener);
      },
      removeEventListener(type, listener) { listeners.get(type)?.delete(listener); },
      dispatch(type, value) {
        listeners.get(type)?.forEach(listener => listener(value));
        const handler = type === 'message' ? this.onmessage : type === 'error' ? this.onerror : type === 'open' ? this.onopen : null;
        handler?.(value);
      },
      finish() { this.readyState = 2; },
      close() {
        if (this.readyState === 2) return;
        this.readyState = 2;
        streams.delete(id);
        if (this.sent && !this.ending) cancel(id);
      }
    };
    streams.set(id, stream);
    Promise.resolve(send({ type: 'stream', id, clientId, method: 'GET', path }, () => {
      if (!streams.has(id)) return false;
      stream.sent = true;
      return true;
    })).catch(error => {
      if (!streams.has(id)) return;
      streams.delete(id);
      stream.dispatch('error', event('error', { message: error.message }));
      stream.finish();
    });
    return stream;
  };

  const release = () => {
    if (released) return;
    released = true;
    streams.forEach((stream, id) => { stream.finish(); if (stream.sent) cancel(id); });
    streams.clear();
    pending.forEach((state, id) => { if (state.sent) cancel(id); });
    fail(new Error('IPC 頁面已關閉。'));
    notify({ type: 'release', id: newId(), clientId });
  };

  return { clientId, receive, request, openEventStream, release, fail };
}

function response(status = 200, data = null, statusText = '') {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText,
    json: async () => data,
    text: async () => JSON.stringify(data)
  };
}

function isSettingsFrame() {
  return window.parent !== window && window.frameElement?.id === 'settingsFrame' && window.location.pathname.endsWith('/settings.html');
}

let client;
if (window.parent !== window) {
  if (!isSettingsFrame()) throw new Error('只有設定頁 iframe 可以使用 IPC 橋接。');
  client = window.parent.__xivtcCreateIpcClient?.(window);
  if (!client) throw new Error('設定頁的 IPC 父頁通道尚未就緒。');
} else {
  const clients = new Map();
  let ready;
  const send = value => message(value);
  const queue = (value, shouldSend = () => true) => ready.then(() => shouldSend() && send(value));
  const topClient = createClient(queue);
  const readyId = newId();
  clients.set(topClient.clientId, topClient);

  window.__xivtcCreateIpcClient = source => {
    const frame = document.getElementById('settingsFrame');
    if (frame?.contentWindow !== source || !source.location.pathname.endsWith('/settings.html')) return null;
    const child = createClient(queue);
    clients.set(child.clientId, child);
    const release = child.release;
    child.release = () => {
      release();
      clients.delete(child.clientId);
    };
    return child;
  };

  window.external.receiveMessage(raw => {
    let data;
    try { data = JSON.parse(raw); } catch { return; }
    if (data.type === 'ready' && data.id === readyId && data.clientId === topClient.clientId) {
      readyResolve?.();
      return;
    }
    clients.get(data.clientId)?.receive(data);
  });

  let readyResolve;
  let readyReject;
  ready = new Promise((resolve, reject) => {
    readyResolve = resolve;
    readyReject = reject;
  });
  const timeout = setTimeout(() => readyReject(new Error('IPC 初始化逾時')), readyTimeoutMs);
  ready.then(
    () => clearTimeout(timeout),
    error => {
      clearTimeout(timeout);
      clients.forEach(value => value.fail(error));
    }
  );
  message({ type: 'ready', id: readyId, clientId: topClient.clientId });
  client = topClient;
}

window.addEventListener('pagehide', () => client.release(), { once: true });

export function ipcFetch(path, options = {}) {
  const method = options.method || 'GET';
  let body = options.body;
  if (typeof body === 'string') {
    try { body = JSON.parse(body); } catch { /* IPC body 僅支援 JSON。 */ }
  }
  return client.request(method, path, body, options.signal);
}

export function openEventStream(path) {
  return client.openEventStream(path);
}
