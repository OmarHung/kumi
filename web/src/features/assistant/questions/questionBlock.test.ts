import { describe, expect, it } from 'vitest';
import { formatAnswers, parseQuestionBlock, type AssistantQuestion } from './questionBlock';

function block(json: string): string {
  return '```kumi-questions\n' + json + '\n```';
}

const ONE = '{"questions":[{"question":"誰負責？","options":[{"label":"阿哲"},{"label":"Ivan","description":"後端"}],"recommended":"Ivan"}]}';

describe('parseQuestionBlock', () => {
  it('讀出區塊，前後的文字分開回傳', () => {
    // Act
    const result = parseQuestionBlock(`先確認兩件事。\n\n${block(ONE)}\n\n謝謝`);

    // Assert
    expect(result.before).toBe('先確認兩件事。\n\n');
    expect(result.after).toBe('\n\n謝謝');
    expect(result.questions).toEqual([
      {
        question: '誰負責？',
        options: [{ label: '阿哲' }, { label: 'Ivan', description: '後端' }],
        recommended: 'Ivan',
      },
    ]);
  });

  it('沒有區塊時 questions 是 null、整段文字留在 before', () => {
    expect(parseQuestionBlock('只是一般的回覆')).toEqual({ before: '只是一般的回覆', questions: null, after: '' });
  });

  it('JSON 壞掉時 questions 是 null', () => {
    expect(parseQuestionBlock(block('{"questions":[')).questions).toBeNull();
  });

  it('串流到一半（還沒有結尾的圍欄）讀不出來', () => {
    expect(parseQuestionBlock('```kumi-questions\n{"questions":[{"question":"a"').questions).toBeNull();
  });

  it('多個區塊取最後一個完整的', () => {
    const first = block('{"questions":[{"question":"舊的"}]}');
    const last = block('{"questions":[{"question":"新的"}]}');

    const result = parseQuestionBlock(`${first}\n\n${last}`);

    expect(result.questions?.[0]?.question).toBe('新的');
    expect(result.before).toContain('舊的');
  });

  it('最後一個區塊壞掉就整段照原樣（不退回前一個）', () => {
    const result = parseQuestionBlock(`${block('{"questions":[{"question":"舊的"}]}')}\n\n${block('壞')}`);

    expect(result.questions).toBeNull();
  });

  it('options 省略是 0 個選項；multiSelect 讀進來', () => {
    const result = parseQuestionBlock(block('{"questions":[{"question":"備註？","multiSelect":true}]}'));

    expect(result.questions).toEqual([{ question: '備註？', options: [], multiSelect: true }]);
  });

  it('recommended 對不到任何選項時略過它', () => {
    const result = parseQuestionBlock(block('{"questions":[{"question":"q","options":[{"label":"A"}],"recommended":"B"}]}'));

    expect(result.questions?.[0]?.recommended).toBeUndefined();
  });

  it('超過 5 題、選項超過 6 個、沒有題目都讀不出來', () => {
    const q = (n: number) => `{"question":"q${String(n)}"}`;
    const six = Array.from({ length: 6 }, (_, i) => q(i)).join(',');
    const sevenOptions = Array.from({ length: 7 }, (_, i) => `{"label":"o${String(i)}"}`).join(',');

    expect(parseQuestionBlock(block(`{"questions":[${six}]}`)).questions).toBeNull();
    expect(parseQuestionBlock(block(`{"questions":[{"question":"q","options":[${sevenOptions}]}]}`)).questions).toBeNull();
    expect(parseQuestionBlock(block('{"questions":[]}')).questions).toBeNull();
  });

  it('題目或選項的字是空的、型別不對都讀不出來', () => {
    expect(parseQuestionBlock(block('{"questions":[{"question":"  "}]}')).questions).toBeNull();
    expect(parseQuestionBlock(block('{"questions":[{"question":"q","options":[{"label":""}]}]}')).questions).toBeNull();
    expect(parseQuestionBlock(block('{"questions":[{"question":"q","options":"A"}]}')).questions).toBeNull();
    expect(parseQuestionBlock(block('{"questions":[{"question":"q","multiSelect":"yes"}]}')).questions).toBeNull();
    expect(parseQuestionBlock(block('[1]')).questions).toBeNull();
  });

  it('其他語言標記的程式碼區塊不算', () => {
    expect(parseQuestionBlock('```json\n' + ONE + '\n```').questions).toBeNull();
  });
});

describe('formatAnswers', () => {
  const questions: readonly AssistantQuestion[] = [
    { question: '要做到什麼程度？', options: [{ label: '只評估與報價' }, { label: '完整做完' }] },
    { question: '誰負責？', options: [{ label: '阿哲' }, { label: 'Ivan' }] },
    { question: '要不要分階段？', options: [] },
    { question: '要補哪些？', options: [{ label: 'A' }, { label: 'B' }], multiSelect: true },
  ];

  it('照規格的格式：選項、其他、略過、多選用頓號', () => {
    // Act
    const text = formatAnswers(questions, [
      { labels: ['只評估與報價'], other: null, isSkipped: false },
      { labels: [], other: '先不指派', isSkipped: false },
      { labels: [], other: null, isSkipped: true },
      { labels: ['A', 'B'], other: null, isSkipped: false },
    ]);

    // Assert
    expect(text).toBe(
      [
        '回答你的問題：',
        '1. 要做到什麼程度？',
        '   → 只評估與報價',
        '2. 誰負責？',
        '   → 其他：先不指派',
        '3. 要不要分階段？',
        '   → （略過）',
        '4. 要補哪些？',
        '   → A、B',
      ].join('\n'),
    );
  });

  it('多選加其他：選項之後接「其他：…」', () => {
    const text = formatAnswers([questions[3] as AssistantQuestion], [{ labels: ['A'], other: '還有 C', isSkipped: false }]);

    expect(text).toContain('→ A、其他：還有 C');
  });
});
