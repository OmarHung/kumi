import type { AssistantAttachmentKind, AiOptions, AiSelection } from '../../api/aiTypes';
import { ASSISTANT_ATTACHMENT_LIMITS as LIMITS } from '../../api/assistantAttachments';
import { formatAttachmentSize } from '../../domain/attachments';

const IMAGE_EXTENSIONS: ReadonlySet<string> = new Set(['png', 'jpg', 'jpeg', 'gif', 'webp']);
const TEXT_EXTENSIONS: ReadonlySet<string> = new Set([
  'txt', 'md', 'markdown', 'csv', 'tsv', 'json', 'xml', 'yaml', 'yml', 'toml', 'ini', 'log', 'sql', 'html', 'css',
  'js', 'jsx', 'ts', 'tsx', 'py', 'cs', 'java', 'go', 'rs', 'sh', 'rb', 'php', 'swift', 'kt', 'c', 'h', 'cpp',
]);

/** 選檔視窗的 accept：圖片、PDF 與常見的文字副檔名（伺服器看內容判斷，這只是縮小選檔視窗的範圍）。 */
export const FILE_ACCEPT = [
  'image/png',
  'image/jpeg',
  'image/gif',
  'image/webp',
  'application/pdf',
  ...[...TEXT_EXTENSIONS].map((extension) => `.${extension}`),
].join(',');

const ENGINE_LABELS = { claude_code: 'Claude Code', codex: 'Codex' } as const;

/** 沒有檔名的貼上（截圖）用的名稱。 */
export const PASTED_IMAGE_NAME = '貼上的圖片.png';

function extensionOf(fileName: string): string {
  const dot = fileName.lastIndexOf('.');
  return dot <= 0 ? '' : fileName.slice(dot + 1).toLowerCase();
}

/** 依類型與副檔名猜附件的種類；猜不出來是 null（伺服器看內容判斷，這裡只用來先擋明顯太大的）。 */
export function guessKind(file: Pick<File, 'name' | 'type'>): AssistantAttachmentKind | null {
  const extension = extensionOf(file.name);
  if (file.type === 'application/pdf' || extension === 'pdf') return 'pdf';
  if (file.type.startsWith('image/') || IMAGE_EXTENSIONS.has(extension)) return 'image';
  if (file.type.startsWith('text/') || TEXT_EXTENSIONS.has(extension)) return 'text';
  return null;
}

function limitOf(kind: AssistantAttachmentKind | null): number {
  if (kind === 'image') return LIMITS.maxImageBytes;
  if (kind === 'text') return LIMITS.maxTextBytes;
  // 猜不出來照 PDF 的上限：不比伺服器最寬的那個更嚴。
  return LIMITS.maxPdfBytes;
}

/** 這個檔案已經知道一定會被伺服器退回的原因（超過數量、太大、合計太大）；沒有問題是 null。 */
export function rejectionReason(file: Pick<File, 'name' | 'type' | 'size'>, acceptedCount: number, acceptedBytes: number): string | null {
  if (acceptedCount >= LIMITS.maxCount) return `一則訊息最多 ${String(LIMITS.maxCount)} 個附件。`;
  const limit = limitOf(guessKind(file));
  if (file.size > limit) return `檔案超過 ${formatAttachmentSize(limit)}。`;
  if (acceptedBytes + file.size > LIMITS.maxTotalBytes) return `附件合計超過 ${formatAttachmentSize(LIMITS.maxTotalBytes)}。`;
  return null;
}

const NAME_HEAD = 14;
const NAME_TAIL = 10;

/** 檔名太長時中間省略，保留開頭與結尾（副檔名）；完整的名稱放在 title。 */
export function shortenFileName(name: string): string {
  const chars = Array.from(name);
  if (chars.length <= NAME_HEAD + NAME_TAIL + 1) return name;
  return `${chars.slice(0, NAME_HEAD).join('')}…${chars.slice(-NAME_TAIL).join('')}`;
}

/** 選的來源是本機或主機上的引擎（Claude Code、Codex）；API 的模型是 null（圖片、PDF 都收）。 */
export interface EngineSource {
  readonly engine: 'claude_code' | 'codex';
  /** 連線的名稱（本機是電腦名稱、主機是來源名稱），照伺服器給的顯示。 */
  readonly name: string;
}

export function engineSourceOf(options: AiOptions, selection: AiSelection | null): EngineSource | null {
  if (selection === null) return null;
  const connection = options.connections.find((candidate) => candidate.id === selection.connectionId);
  if (connection === undefined) return null;
  if (connection.kind !== 'claude_code' && connection.kind !== 'codex') return null;
  return { engine: connection.kind, name: connection.name };
}

/** 這個來源讀不到這種附件：Codex 讀不到 PDF，其他（Claude Code、API 的模型）都收。 */
export function isUnreadable(source: EngineSource | null, kind: AssistantAttachmentKind | null): boolean {
  return source?.engine === 'codex' && kind === 'pdf';
}

/** 小卡上的短字與報讀用。 */
export function unreadableShortNote(source: EngineSource): string {
  return `${ENGINE_LABELS[source.engine]} 讀不到 PDF`;
}

/** 輸入框上方的說明：是哪一個來源、能怎麼做。連線名稱本身就帶引擎（例如「Codex（某台電腦）」），不再另外寫引擎。 */
export function unreadableNote(source: EngineSource): string {
  return `你選的「${source.name}」讀不到 PDF：移除它，或改選 Claude Code。`;
}
