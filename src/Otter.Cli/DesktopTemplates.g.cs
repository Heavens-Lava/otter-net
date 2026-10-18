// Generated from otterPS 1.0.1 src/Otter.Desktop.psm1 (Start-OtterDesktopApplication's
// $injectionScript here-string). Do not edit by hand.
namespace Otter.Cli;

static class DesktopTemplates
{
    // {{PORT}} and {{TOKEN}} are the bridge's port and session token.
    public const string InjectionScript = """
<style id="otter-desktop-window-foundation">
  html, body {
    margin: 0 !important;
    padding: 0 !important;
    width: 100vw !important;
    height: 100vh !important;
    max-width: 100vw !important;
    max-height: 100vh !important;
    overflow: hidden !important;
    background: #f0f4f9 !important;
  }
  body.otter-has-page {
    margin: 0 !important;
    padding: 0 !important;
    display: flex !important;
    flex-direction: column !important;
    height: 100vh !important;
    max-height: 100vh !important;
    width: 100vw !important;
    max-width: 100vw !important;
    overflow: hidden !important;
  }
  main.otter-page {
    margin: 0 !important;
    padding: 8px 12px 6px 12px !important;
    width: 100vw !important;
    max-width: 100vw !important;
    height: 100vh !important;
    max-height: 100vh !important;
    display: flex !important;
    flex-direction: column !important;
    box-sizing: border-box !important;
    border-radius: 0 !important;
    overflow: hidden !important;
    flex: 1 1 0% !important;
  }
  .otter-page-content {
    display: flex !important;
    flex-direction: column !important;
    height: 100% !important;
    max-height: 100% !important;
    flex: 1 1 0% !important;
    min-height: 0 !important;
    width: 100% !important;
    overflow: hidden !important;
  }
  #workspaceRow {
    flex: 1 1 0% !important;
    min-height: 0 !important;
    height: 100% !important;
    overflow: hidden !important;
  }
  #leftSidebar, #centerCol, #rightSidebar {
    height: 100% !important;
    min-height: 0 !important;
  }
  #statusBar {
    flex-shrink: 0 !important;
  }
  #headerBar {
    flex-shrink: 0 !important;
  }
  #problemsDrawer {
    flex-shrink: 0 !important;
  }
  #winControls {
    display: none !important;
  }
</style>
<script id="otter-desktop-session-bridge">
window.__OTTER_DESKTOP_BRIDGE__ = {
  port: {{PORT}},
  token: "{{TOKEN}}",
  async readFile(filePath) {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/fs/read', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Otter-Token': this.token
      },
      body: JSON.stringify({ path: filePath })
    });
    if (!res.ok) {
      throw new Error('Failed to read file ' + filePath + ': HTTP ' + res.status);
    }
    const data = await res.json();
    return data.content;
  },
  async writeFile(filePath, content) {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/fs/write', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Otter-Token': this.token
      },
      body: JSON.stringify({ path: filePath, content: content })
    });
    if (!res.ok) {
      throw new Error('Failed to write file ' + filePath + ': HTTP ' + res.status);
    }
    return await res.json();
  },
  async downloadFile(url, filePath) {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/fs/download', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Otter-Token': this.token
      },
      body: JSON.stringify({ url: url, path: filePath })
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || ('Failed to download file ' + filePath + ': HTTP ' + res.status));
    }
    return await res.json();
  },
  async exec(command, shell = null, cwd = null) {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/terminal/exec', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Otter-Token': this.token
      },
      body: JSON.stringify({ command: command, shell: shell, cwd: cwd })
    });
    if (!res.ok) {
      throw new Error('Terminal execution failed: HTTP ' + res.status);
    }
    return await res.json();
  },
  async getProfiles() {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/terminal/profiles', {
      method: 'GET',
      headers: { 'X-Otter-Token': this.token }
    });
    if (!res.ok) return [];
    return await res.json();
  },
  async getFiles(folderPath, includeSubfolders = false) {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/fs/files', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Otter-Token': this.token
      },
      body: JSON.stringify({ path: folderPath, recursive: includeSubfolders })
    });
    if (!res.ok) {
      throw new Error('Failed to get files in ' + folderPath + ': HTTP ' + res.status);
    }
    return await res.json();
  },
  async getFolders(folderPath, includeSubfolders = false) {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/fs/folders', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Otter-Token': this.token
      },
      body: JSON.stringify({ path: folderPath, recursive: includeSubfolders })
    });
    if (!res.ok) {
      throw new Error('Failed to get folders in ' + folderPath + ': HTTP ' + res.status);
    }
    return await res.json();
  },
  async fileOperation(operation, payload = {}) {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/fs/operate', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Otter-Token': this.token
      },
      body: JSON.stringify(Object.assign({ operation: operation }, payload))
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      throw new Error(data.error || 'The filesystem operation could not be completed.');
    }
    return data;
  },
  async wait(seconds) {
    const ms = Math.max(0, Number(seconds) * 1000);
    return new Promise(resolve => setTimeout(resolve, ms));
  },
  async delay(ms) {
    const delayMs = Math.max(0, Number(ms));
    return new Promise(resolve => setTimeout(resolve, delayMs));
  },
  async clipboard(action = 'paste', text = '') {
    const isCopy = action === 'copy';
    const method = isCopy ? 'POST' : 'GET';
    const body = isCopy ? JSON.stringify({ text: text }) : null;
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/system/clipboard', {
      method: method,
      headers: {
        'Content-Type': 'application/json',
        'X-Otter-Token': this.token
      },
      body: body
    });
    return await res.json();
  },
  async notify(title, message) {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/system/notify', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Otter-Token': this.token
      },
      body: JSON.stringify({ title: title, message: message })
    });
    return await res.json();
  },
  async getEnv(name) {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/system/env', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Otter-Token': this.token
      },
      body: JSON.stringify({ name: name })
    });
    const data = await res.json();
    return data.value;
  },
  async getSystemPaths() {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/system/env', {
      method: 'GET',
      headers: {
        'X-Otter-Token': this.token
      }
    });
    return await res.json();
  },
  async chooseFile() {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/dialog/open-file', {
      method: 'POST',
      headers: { 'X-Otter-Token': this.token }
    });
    const data = await res.json();
    return data.path || '';
  },
  async chooseFolder() {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/dialog/folder', {
      method: 'POST',
      headers: { 'X-Otter-Token': this.token }
    });
    const data = await res.json();
    return data.path || '';
  },
  async saveFileDialog() {
    const res = await fetch('http://127.0.0.1:' + this.port + '/api/dialog/save-file', {
      method: 'POST',
      headers: { 'X-Otter-Token': this.token }
    });
    const data = await res.json();
    return data.path || '';
  }
};
window.otterReadFile = function(filePath) {
  if (window.__OTTER_DESKTOP_BRIDGE__ && typeof window.__OTTER_DESKTOP_BRIDGE__.readFile === 'function') {
    return window.__OTTER_DESKTOP_BRIDGE__.readFile(filePath);
  }
  throw new Error('Desktop Bridge is not available for file operations');
};
window.otterWriteFile = function(filePath, content) {
  if (window.__OTTER_DESKTOP_BRIDGE__ && typeof window.__OTTER_DESKTOP_BRIDGE__.writeFile === 'function') {
    return window.__OTTER_DESKTOP_BRIDGE__.writeFile(filePath, content);
  }
  throw new Error('Desktop Bridge is not available for file operations');
};
window.otterDownloadFile = function(url, filePath) {
  if (window.__OTTER_DESKTOP_BRIDGE__ && typeof window.__OTTER_DESKTOP_BRIDGE__.downloadFile === 'function') {
    return window.__OTTER_DESKTOP_BRIDGE__.downloadFile(url, filePath);
  }
  throw new Error('Desktop Bridge is not available for file download in this browser.');
};
window.otterRunCommand = function(command) {
  if (window.__OTTER_DESKTOP_BRIDGE__ && typeof window.__OTTER_DESKTOP_BRIDGE__.exec === 'function') {
    return window.__OTTER_DESKTOP_BRIDGE__.exec(command).then(res => ({
      __otterThing: true,
      typeName: 'command result',
      props: {
        output: res.stdout || '',
        'error output': res.stderr || '',
        'exit code': Number.isFinite(Number(res.exitCode)) ? Number(res.exitCode) : -1
      },
      order: ['output', 'error output', 'exit code']
    }));
  }
  throw new Error('Desktop Bridge is not available for command execution');
};
window.otterGetFiles = function(folderPath, includeSubfolders = false) {
  if (window.__OTTER_DESKTOP_BRIDGE__ && typeof window.__OTTER_DESKTOP_BRIDGE__.getFiles === 'function') {
    return window.__OTTER_DESKTOP_BRIDGE__.getFiles(folderPath, includeSubfolders);
  }
  throw new Error('Desktop Bridge is not available for file discovery');
};
window.otterGetFolders = function(folderPath, includeSubfolders = false) {
  if (window.__OTTER_DESKTOP_BRIDGE__ && typeof window.__OTTER_DESKTOP_BRIDGE__.getFolders === 'function') {
    return window.__OTTER_DESKTOP_BRIDGE__.getFolders(folderPath, includeSubfolders);
  }
  throw new Error('Desktop Bridge is not available for folder discovery');
};
window.otterFileExists = function(filePath) {
  if (window.__OTTER_DESKTOP_BRIDGE__ && typeof window.__OTTER_DESKTOP_BRIDGE__.fileOperation === 'function') {
    return window.__OTTER_DESKTOP_BRIDGE__.fileOperation('file-exists', { path: filePath }).then(result => Boolean(result.exists));
  }
  throw new Error('Desktop Bridge is not available for file operations');
};
window.otterAppendFile = function(filePath, content) { return window.__OTTER_DESKTOP_BRIDGE__.fileOperation('append-file', { path: filePath, content: content }); };
window.otterCopyFile = function(source, destination) { return window.__OTTER_DESKTOP_BRIDGE__.fileOperation('copy-file', { source: source, destination: destination }); };
window.otterMoveFile = function(source, destination) { return window.__OTTER_DESKTOP_BRIDGE__.fileOperation('move-file', { source: source, destination: destination }); };
window.otterDeleteFile = function(filePath) { return window.__OTTER_DESKTOP_BRIDGE__.fileOperation('delete-file', { path: filePath }); };
window.otterCreateFolder = function(folderPath) { return window.__OTTER_DESKTOP_BRIDGE__.fileOperation('create-folder', { path: folderPath }); };
window.otterDeleteFolder = function(folderPath) { return window.__OTTER_DESKTOP_BRIDGE__.fileOperation('delete-folder', { path: folderPath }); };
window.otterCopyFolder = function(source, destination) { return window.__OTTER_DESKTOP_BRIDGE__.fileOperation('copy-folder', { source: source, destination: destination }); };
window.otterMoveFolder = function(source, destination) { return window.__OTTER_DESKTOP_BRIDGE__.fileOperation('move-folder', { source: source, destination: destination }); };
window.otterWait = function(seconds) {
  const ms = Math.max(0, Number(seconds) * 1000);
  return new Promise(resolve => setTimeout(resolve, ms));
};
window.otterDelay = function(ms) {
  const delayMs = Math.max(0, Number(ms));
  return new Promise(resolve => setTimeout(resolve, delayMs));
};
window.wait = window.otterWait;
window.delay = window.otterDelay;
window.otterClipboard = {
  copy: function(text) {
    if (window.__OTTER_DESKTOP_BRIDGE__) {
      return window.__OTTER_DESKTOP_BRIDGE__.clipboard('copy', text);
    }
    if (navigator.clipboard) {
      return navigator.clipboard.writeText(text);
    }
    return Promise.resolve(false);
  },
  paste: function() {
    if (window.__OTTER_DESKTOP_BRIDGE__) {
      return window.__OTTER_DESKTOP_BRIDGE__.clipboard('paste').then(r => r.text || '');
    }
    if (navigator.clipboard) {
      return navigator.clipboard.readText();
    }
    return Promise.resolve('');
  }
};
window.otterNotify = function(title, message) {
  if (window.__OTTER_DESKTOP_BRIDGE__) {
    return window.__OTTER_DESKTOP_BRIDGE__.notify(title, message);
  }
  if (typeof Notification !== 'undefined' && Notification.permission === 'granted') {
    new Notification(title, { body: message });
  }
  return Promise.resolve({ completed: true });
};
window.otterGetEnv = function(name) {
  if (window.__OTTER_DESKTOP_BRIDGE__) {
    return window.__OTTER_DESKTOP_BRIDGE__.getEnv(name);
  }
  return Promise.resolve(null);
};
window.otterGetSystemPaths = function() {
  if (window.__OTTER_DESKTOP_BRIDGE__) {
    return window.__OTTER_DESKTOP_BRIDGE__.getSystemPaths();
  }
  return Promise.resolve({});
};
window.otterChooseFile = function() {
  if (window.__OTTER_DESKTOP_BRIDGE__) {
    return window.__OTTER_DESKTOP_BRIDGE__.chooseFile();
  }
  return Promise.resolve('');
};
window.otterChooseFolder = function() {
  if (window.__OTTER_DESKTOP_BRIDGE__) {
    return window.__OTTER_DESKTOP_BRIDGE__.chooseFolder();
  }
  return Promise.resolve('');
};
window.otterSaveFileDialog = function() {
  if (window.__OTTER_DESKTOP_BRIDGE__) {
    return window.__OTTER_DESKTOP_BRIDGE__.saveFileDialog();
  }
  return Promise.resolve('');
};
window.copyToClipboard = window.otterClipboard.copy;
window.getClipboard = window.otterClipboard.paste;
window.notify = window.otterNotify;
window.chooseFile = window.otterChooseFile;
window.chooseFolder = window.otterChooseFolder;
window.saveFileDialog = window.otterSaveFileDialog;
window.otterStorage = {
  get: function(key, defaultVal = null) {
    try {
      const val = localStorage.getItem(key);
      if (val === null) return defaultVal;
      try { return JSON.parse(val); } catch(_) { return val; }
    } catch(_) { return defaultVal; }
  },
  set: function(key, val) {
    try {
      const serialized = (typeof val === 'object' && val !== null) ? JSON.stringify(val) : String(val);
      localStorage.setItem(key, serialized);
      return true;
    } catch(_) { return false; }
  },
  remove: function(key) {
    try { localStorage.removeItem(key); return true; } catch(_) { return false; }
  },
  clear: function() {
    try { localStorage.clear(); return true; } catch(_) { return false; }
  },
  session: {
    get: function(key, defaultVal = null) {
      try {
        const val = sessionStorage.getItem(key);
        if (val === null) return defaultVal;
        try { return JSON.parse(val); } catch(_) { return val; }
      } catch(_) { return defaultVal; }
    },
    set: function(key, val) {
      try {
        const serialized = (typeof val === 'object' && val !== null) ? JSON.stringify(val) : String(val);
        sessionStorage.setItem(key, serialized);
        return true;
      } catch(_) { return false; }
    },
    remove: function(key) {
      try { sessionStorage.removeItem(key); return true; } catch(_) { return false; }
    }
  }
};
window.getStorage = window.otterStorage.get;
window.setStorage = window.otterStorage.set;
window.removeStorage = window.otterStorage.remove;
window.otterFetch = async function(url, options = {}) {
  const resp = await fetch(url, options);
  const contentType = resp.headers.get('content-type') || '';
  let data;
  if (contentType.includes('application/json')) {
    data = await resp.json();
  } else {
    data = await resp.text();
  }
  return {
    ok: resp.ok,
    status: resp.status,
    statusText: resp.statusText,
    data: data,
    headers: Object.fromEntries(resp.headers.entries())
  };
};
window.otterGetJson = async function(url, headers = {}) {
  const res = await window.otterFetch(url, { method: 'GET', headers: Object.assign({ 'Accept': 'application/json' }, headers) });
  return res.data;
};
window.otterPostJson = async function(url, body = {}, headers = {}) {
  const res = await window.otterFetch(url, {
    method: 'POST',
    headers: Object.assign({ 'Content-Type': 'application/json', 'Accept': 'application/json' }, headers),
    body: JSON.stringify(body)
  });
  return res.data;
};
window.httpGet = window.otterGetJson;
window.httpPost = window.otterPostJson;

// D62: durable session lifetime. This is generic desktop-runtime
// infrastructure - it knows nothing about Studio or any other
// specific application, and must stay that way (no Studio-specific
// behavior belongs in this shared injection script). A grace-timeout
// design on the server side (OtterTerminalBridgeSession.IsSessionAlive)
// is the only thing keeping the bridge alive past this heartbeat, so
// no explicit close signal is sent here - the server simply notices
// the beats stop and cleans up after its own grace window elapses.
(function startOtterHeartbeat() {
  function beat() {
    fetch(
      'http://127.0.0.1:' +
      window.__OTTER_DESKTOP_BRIDGE__.port +
      '/api/session/heartbeat',
      {
        method: 'POST',
        headers: {
          'X-Otter-Token':
            window.__OTTER_DESKTOP_BRIDGE__.token
        }
      }
    ).catch(() => {});
  }

  beat();
  setInterval(beat, 2000);
})();
</script>
""";
}
