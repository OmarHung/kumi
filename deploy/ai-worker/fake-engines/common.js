'use strict';
// 假引擎的共用部分（只給測試與演練，不進正式映像檔）。
// 真的用工作的權杖打 /mcp（Streamable HTTP：POST、Accept: application/json, text/event-stream、JSON-RPC），
// 再依提示裡的關鍵字呼叫工具；輸出照 claude stream-json／codex exec --json 的格式。
const fs = require('fs');
const os = require('os');
const path = require('path');

const stateDir = process.env.FAKE_ENGINE_STATE_DIR || path.join(os.homedir(), '.fake-engines');

function readJson(file, fallback) {
  try { return JSON.parse(fs.readFileSync(path.join(stateDir, file), 'utf8')); } catch { return fallback; }
}

function writeJson(file, value) {
  fs.mkdirSync(stateDir, { recursive: true });
  fs.writeFileSync(path.join(stateDir, file), JSON.stringify(value));
}

function readStdin() {
  try { return fs.readFileSync(0, 'utf8'); } catch { return ''; }
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function mcpSession(url, token, project) {
  const base = {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
    Accept: 'application/json, text/event-stream',
  };
  if (project) base['X-Kumi-Project'] = project;
  let nextId = 1;
  let sessionId = null;
  let protocol = null;

  async function post(body) {
    const headers = { ...base };
    if (sessionId) headers['Mcp-Session-Id'] = sessionId;
    if (protocol) headers['MCP-Protocol-Version'] = protocol;
    const response = await fetch(url, { method: 'POST', headers, body: JSON.stringify(body) });
    sessionId = response.headers.get('mcp-session-id') || sessionId;
    return response;
  }

  async function readMessage(response, id) {
    const text = await response.text();
    if (!response.ok) throw new Error(`MCP ${response.status}`);
    if ((response.headers.get('content-type') || '').includes('text/event-stream')) {
      for (const block of text.split(/\r?\n\r?\n/)) {
        const data = block.split(/\r?\n/).filter((line) => line.startsWith('data:')).map((line) => line.slice(5).trim()).join('\n');
        if (!data) continue;
        const message = JSON.parse(data);
        if (message.id === id) return message;
      }
      throw new Error('MCP 的回應裡沒有對應的訊息');
    }
    return JSON.parse(text);
  }

  async function request(method, params) {
    const id = nextId++;
    const message = await readMessage(await post({ jsonrpc: '2.0', id, method, params }), id);
    if (message.error) throw new Error(`${method}: ${message.error.message}`);
    return message.result;
  }

  const initialized = request('initialize', {
    protocolVersion: '2025-06-18',
    capabilities: {},
    clientInfo: { name: 'kumi-fake-engine', version: '0.0.0' },
  }).then(async (result) => {
    protocol = result.protocolVersion || null;
    await post({ jsonrpc: '2.0', method: 'notifications/initialized' }).then((r) => r.text()).catch(() => {});
  });

  return {
    async listTools() { await initialized; return (await request('tools/list', {})).tools; },
    async callTool(name, args) {
      await initialized;
      const result = await request('tools/call', { name, arguments: args });
      const text = (result.content || []).filter((part) => part.type === 'text').map((part) => part.text).join('\n');
      return { text, isError: !!result.isError };
    },
  };
}

// 提示裡的關鍵字決定做什麼；提示是「系統提示＋整段對話」排成的文字。
function plan(prompt, project) {
  const itemKey = (prompt.match(/\b[A-Z][A-Z0-9]{0,19}-\d+\b/) || [])[0];
  const steps = [];
  if (/藍圖|blueprint/i.test(prompt)) steps.push({ tool: 'get_blueprint', input: { projectKey: project } });
  if (itemKey && /讀|get_item|看一下|read/i.test(prompt)) steps.push({ tool: 'get_item', input: { itemKey } });
  if (/搜尋|search/i.test(prompt)) steps.push({ tool: 'search_items', input: { query: itemKey || 'a' } });
  if (itemKey && /提出|propose|修改/i.test(prompt)) {
    steps.push({
      tool: 'propose_item_changes',
      input: { projectKey: project, itemKey, changes: [{ field: 'title', value: '假引擎提出的標題' }], comment: '假引擎提出的' },
    });
  }
  return steps;
}

async function runScenario({ prompt, url, token, project, sink, received }) {
  const session = await mcpSession(url, token, project);
  const tools = await session.listTools();
  const names = tools.map((tool) => tool.name);
  sink.init(names);
  if (/慢一點/.test(prompt)) {
    for (let index = 1; index <= 20; index++) {
      sink.text(`第 ${index} 段。`);
      await sleep(1000);
    }
  }
  sink.text('我來看一下。');
  for (const step of plan(prompt, project)) {
    if (!names.includes(step.tool)) {
      sink.toolStart(step.tool, step.input);
      sink.toolEnd(step.tool, '這件工作不能用這個工具', true);
      continue;
    }
    sink.toolStart(step.tool, step.input);
    let result;
    try { result = await session.callTool(step.tool, step.input); } catch (error) { result = { text: String(error.message), isError: true }; }
    sink.toolEnd(step.tool, result.text, result.isError);
  }
  if (received && received.images + received.pdfs > 0) sink.text(`收到 ${received.images} 張圖片、${received.pdfs} 個 PDF。`);
  sink.text(`工具清單：${names.join('、')}。`);
  sink.done();
}

module.exports = { readJson, writeJson, readStdin, runScenario, stateDir };
