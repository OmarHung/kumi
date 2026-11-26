import { screen } from '@testing-library/react';
import type { AiOptions, AssistantAttachment } from '../../api/aiTypes';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { makeAiOptions, serveAiOptions, serveConversations } from '../../test/assistant';

/** 開 SMK 的 AI 助理整頁（沒有任何對話）。 */
export function openAssistant(path = '/projects/SMK/assistant', options: AiOptions = makeAiOptions()) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveAiOptions(options);
  serveConversations('SMK', []);
  return renderApp(path);
}

export async function findInput() {
  return screen.findByRole('textbox', { name: '訊息' });
}

export async function typeMessage(user: ReturnType<typeof renderApp>['user'], text: string) {
  const input = await findInput();
  await user.click(input);
  await user.type(input, text);
  return input;
}

/** 伺服器收下附件之後回的 JSON（kind 依檔名猜）。 */
export function uploadedAs(name: string, overrides: Partial<AssistantAttachment> = {}): AssistantAttachment {
  const isImage = /\.(png|jpe?g|gif|webp)$/i.test(name);
  const isPdf = /\.pdf$/i.test(name);
  return {
    id: `att-${name}`,
    fileName: name,
    contentType: isImage ? 'image/png' : isPdf ? 'application/pdf' : 'text/plain',
    sizeBytes: 2048,
    kind: isImage ? 'image' : isPdf ? 'pdf' : 'text',
    uploadedAt: '2026-11-26T02:00:00Z',
    ...overrides,
  };
}

/** 選到本機（或主機）的引擎：預設是本機的 Claude Code；Codex 讀不到 PDF。 */
export function localEngineOptions(engine: 'claude_code' | 'codex' = 'claude_code', hosted = false): AiOptions {
  return makeAiOptions({
    default: { connectionId: 'src-1', model: 'opus', effort: null },
    connections: [
      {
        id: 'src-1',
        name: hosted ? '主機上的來源' : 'Example-PC',
        kind: engine,
        personal: false,
        hosted,
        notice: null,
        models: [{ id: 'opus', displayName: 'Opus', efforts: [], defaultEffort: null }],
      },
    ],
  });
}
