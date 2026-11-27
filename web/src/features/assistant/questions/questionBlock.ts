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
const MAX_QUESTION_LENGTH = 200;
const MAX_LABEL_LENGTH = 200;
const MAX_DESCRIPTION_LENGTH = 300;
/** 伺服器收一則訊息的字數上限（AiRules.MaxMessageLength）；回答整則不能超過。 */
const MAX_MESSAGE_LENGTH = 8000;
const ELLIPSIS = '…';
const ANSWER_HEADER = '回答你的問題：';
const SKIPPED_TEXT = '（略過）';
const LIST_SEPARATOR = '、';

// 只認單獨一行的圍欄；內容不含圍欄本身（非貪婪）。
const BLOCK_PATTERN = /^```kumi-questions[ \t]*\r?\n([\s\S]*?)\r?\n```[ \t]*$/gm;

type Json = Record<string, unknown>;

function isRecord(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readText(value: unknown, maxLength: number): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  return trimmed === '' || trimmed.length > maxLength ? null : trimmed;
}

function readOption(raw: unknown): AssistantQuestionOption | null {
  if (!isRecord(raw)) return null;
  const label = readText(raw['label'], MAX_LABEL_LENGTH);
  if (label === null) return null;
  if (raw['description'] === undefined) return { label };
  if (typeof raw['description'] !== 'string') return null;
  const description = raw['description'].trim();
  if (description.length > MAX_DESCRIPTION_LENGTH) return null;
  return description === '' ? { label } : { label, description };
}

function readOptions(raw: unknown): readonly AssistantQuestionOption[] | null {
  if (raw === undefined) return [];
  if (!Array.isArray(raw) || raw.length > MAX_OPTIONS) return null;
  const options = raw.map(readOption);
  if (!options.every((option) => option !== null)) return null;
  // label 是回答的依據，重複的選項分不出選了哪一個。
  return new Set(options.map((option) => option.label)).size === options.length ? options : null;
}

function readQuestion(raw: unknown): AssistantQuestion | null {
  if (!isRecord(raw)) return null;
  const question = readText(raw['question'], MAX_QUESTION_LENGTH);
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

const oneLine = (text: string): string => text.replace(/\s*[\r\n]+\s*/g, ' ');

function truncated(text: string, maxLength: number): string {
  return text.length <= maxLength ? text : `${text.slice(0, Math.max(0, maxLength - 1))}${ELLIPSIS}`;
}

function answerLines(questions: readonly string[], answers: readonly QuestionAnswer[]): string {
  const lines = questions.flatMap((question, index) => {
    const answer = answers[index];
    return [`${String(index + 1)}. ${question}`, `   → ${answer === undefined ? SKIPPED_TEXT : answerText(answer)}`];
  });
  return [ANSWER_HEADER, ...lines].join('\n');
}

/**
 * 回答訊息：「回答你的問題：」開頭，每題一行題目（換行換成空白）、下一行縮排的「→ 回答」。
 * 整則超過伺服器的字數上限時只把題目截短加「…」，回答本身不截。
 */
export function formatAnswers(questions: readonly AssistantQuestion[], answers: readonly QuestionAnswer[]): string {
  const texts = questions.map((question) => oneLine(question.question));
  const full = answerLines(texts, answers);
  if (full.length <= MAX_MESSAGE_LENGTH) return full;
  const room = MAX_MESSAGE_LENGTH - answerLines(texts.map(() => ''), answers).length;
  const each = Math.max(1, Math.floor(room / Math.max(1, texts.length)));
  return answerLines(texts.map((text) => truncated(text, each)), answers);
}

const OPEN_PATTERN = /^```kumi-questions[ \t]*(?:\r?\n|$)/gm;
const CLOSING_FENCE = /^```[ \t]*$/m;

/** 串流到一半、尾端還沒收尾的 kumi-questions 區塊：回傳區塊之前的文字，讓畫面先不要閃出原始 JSON；沒有這種區塊是 null。 */
export function withoutOpenQuestionBlock(text: string): string | null {
  const last = [...text.matchAll(OPEN_PATTERN)].at(-1);
  if (last === undefined) return null;
  const rest = text.slice(last.index + last[0].length);
  return CLOSING_FENCE.test(rest) ? null : text.slice(0, last.index);
}

function questionListText(questions: readonly AssistantQuestion[]): string {
  return questions
    .flatMap((question, index) => [
      `${String(index + 1)}. ${oneLine(question.question)}`,
      ...question.options.map((option) => `   - ${option.label}${option.description === undefined ? '' : `：${option.description}`}`),
    ])
    .join('\n');
}

/** 複製助理的回覆用：可以畫成卡片的問題區塊換成純文字的問題清單，不複製 JSON。 */
export function withQuestionsAsList(text: string): string {
  const { before, questions, after } = parseQuestionBlock(text);
  if (questions === null) return text;
  return [before.trim(), questionListText(questions), after.trim()].filter((part) => part !== '').join('\n\n');
}
