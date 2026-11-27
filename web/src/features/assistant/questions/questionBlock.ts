/** 助理回覆最後的問題區塊（internal/HANDOFF-ai-composer.md 第 2、3 節）：語言標記 kumi-questions 的程式碼區塊，內容是 JSON。 */
export interface AssistantQuestionOption {
  readonly label: string;
  readonly description?: string;
}

export interface AssistantQuestion {
  readonly question: string;
  readonly options: readonly AssistantQuestionOption[];
  /** 某個選項的 label；對不到時讀的時候就拿掉。 */
  readonly recommended?: string;
  readonly multiSelect?: boolean;
}

export interface ParsedQuestionBlock {
  /** 區塊之前的文字；讀不出區塊時是整段文字。 */
  readonly before: string;
  /** 讀不出來（沒有區塊、JSON 壞了、格式或上限不對）是 null。 */
  readonly questions: readonly AssistantQuestion[] | null;
  readonly after: string;
}

/** 使用者對一題的回答：選了哪些選項、「其他」自己寫的字（沒有是 null）、或略過。 */
export interface QuestionAnswer {
  readonly labels: readonly string[];
  readonly other: string | null;
  readonly isSkipped: boolean;
}

const MAX_QUESTIONS = 5;
const MAX_OPTIONS = 6;
const ANSWER_HEADER = '回答你的問題：';
const SKIPPED_TEXT = '（略過）';
const LIST_SEPARATOR = '、';

// 只認單獨一行的圍欄；內容不含圍欄本身（非貪婪）。
const BLOCK_PATTERN = /^```kumi-questions[ \t]*\r?\n([\s\S]*?)\r?\n```[ \t]*$/gm;

type Json = Record<string, unknown>;

function isRecord(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readText(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}

function readOption(raw: unknown): AssistantQuestionOption | null {
  if (!isRecord(raw)) return null;
  const label = readText(raw['label']);
  if (label === null) return null;
  if (raw['description'] === undefined) return { label };
  if (typeof raw['description'] !== 'string') return null;
  const description = raw['description'].trim();
  return description === '' ? { label } : { label, description };
}

function readOptions(raw: unknown): readonly AssistantQuestionOption[] | null {
  if (raw === undefined) return [];
  if (!Array.isArray(raw) || raw.length > MAX_OPTIONS) return null;
  const options = raw.map(readOption);
  return options.every((option) => option !== null) ? options : null;
}

function readQuestion(raw: unknown): AssistantQuestion | null {
  if (!isRecord(raw)) return null;
  const question = readText(raw['question']);
  const options = readOptions(raw['options']);
  if (question === null || options === null) return null;
  const multi = raw['multiSelect'];
  if (multi !== undefined && typeof multi !== 'boolean') return null;
  const recommended = typeof raw['recommended'] === 'string' ? raw['recommended'].trim() : '';
  return {
    question,
    options,
    ...(options.some((option) => option.label === recommended) ? { recommended } : {}),
    ...(multi === undefined ? {} : { multiSelect: multi }),
  };
}

function readQuestions(json: string): readonly AssistantQuestion[] | null {
  let data: unknown;
  try {
    data = JSON.parse(json);
  } catch {
    return null;
  }
  if (!isRecord(data) || !Array.isArray(data['questions'])) return null;
  const raw: unknown[] = data['questions'];
  if (raw.length < 1 || raw.length > MAX_QUESTIONS) return null;
  const questions = raw.map(readQuestion);
  return questions.every((question) => question !== null) ? questions : null;
}

/** 找最後一個完整的 kumi-questions 區塊。最後一個讀不出來就整段照原樣（不退回前面的，前面的是舊的問題）。 */
export function parseQuestionBlock(text: string): ParsedQuestionBlock {
  const matches = [...text.matchAll(BLOCK_PATTERN)];
  const last = matches.at(-1);
  const questions = last === undefined ? null : readQuestions(last[1] ?? '');
  if (last === undefined || questions === null) return { before: text, questions: null, after: '' };
  const start = last.index;
  return { before: text.slice(0, start), questions, after: text.slice(start + last[0].length) };
}

function answerText(answer: QuestionAnswer): string {
  if (answer.isSkipped) return SKIPPED_TEXT;
  const parts = [...answer.labels];
  if (answer.other !== null) parts.push(`其他：${answer.other}`);
  return parts.join(LIST_SEPARATOR);
}

/** 回答訊息：「回答你的問題：」開頭，每題一行題目、下一行縮排的「→ 回答」。 */
export function formatAnswers(questions: readonly AssistantQuestion[], answers: readonly QuestionAnswer[]): string {
  const lines = questions.flatMap((question, index) => {
    const answer = answers[index];
    return [`${String(index + 1)}. ${question.question}`, `   → ${answer === undefined ? SKIPPED_TEXT : answerText(answer)}`];
  });
  return [ANSWER_HEADER, ...lines].join('\n');
}
