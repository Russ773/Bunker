const $ = (id) => document.getElementById(id);
const out = $('console'), statusEl = $('status');
const MAX_LINES = 5000;
let ws, history = [], hIdx = 0;

// Remember connection details (never the password)
for (const k of ['host', 'port']) {
  const v = localStorage.getItem('chaos.' + k);
  if (v) $(k).value = v;
}

function addLine(text, cls = '', time = Date.now()) {
  const d = document.createElement('div');
  d.className = 'line ' + cls;
  const t = document.createElement('span');
  t.className = 't';
  t.textContent = new Date(time).toLocaleTimeString();
  d.append(t, document.createTextNode(text));
  out.appendChild(d);
  while (out.childElementCount > MAX_LINES) out.removeChild(out.firstChild);
  if ($('scroll').checked) out.scrollTop = out.scrollHeight;
}

function setStatus(state, text) {
  statusEl.className = state;
  statusEl.textContent = text;
}

function open() {
  ws = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws`);
  ws.onclose = () => { setStatus('error', 'Backend lost'); setTimeout(open, 2000); };
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.type === 'status') {
      setStatus(m.state, m.text);
      addLine(m.text, 'system');
    } else if (m.type === 'console') {
      const k = (m.kind || '').toLowerCase();
      const cls = k === 'chat' ? 'chat' : k === 'warning' ? 'warning' : k === 'error' ? 'error' : '';
      addLine(String(m.text).replace(/\n$/, ''), cls, m.time);
    }
  };
}
open();

$('connect').onclick = () => {
  localStorage.setItem('chaos.host', $('host').value);
  localStorage.setItem('chaos.port', $('port').value);
  ws.send(JSON.stringify({ type: 'connect', host: $('host').value.trim(), port: $('port').value.trim(), password: $('password').value }));
};
$('disconnect').onclick = () => ws.send(JSON.stringify({ type: 'disconnect' }));
$('clear').onclick = () => { out.innerHTML = ''; };

$('cmdform').onsubmit = (e) => {
  e.preventDefault();
  const c = $('cmd').value.trim();
  if (!c) return;
  ws.send(JSON.stringify({ type: 'command', command: c }));
  addLine('> ' + c, 'system');
  history.push(c);
  hIdx = history.length;
  $('cmd').value = '';
};
$('cmd').onkeydown = (e) => {
  if (e.key === 'ArrowUp' && hIdx > 0) { $('cmd').value = history[--hIdx]; e.preventDefault(); }
  else if (e.key === 'ArrowDown' && hIdx < history.length) { $('cmd').value = history[++hIdx] || ''; e.preventDefault(); }
};
