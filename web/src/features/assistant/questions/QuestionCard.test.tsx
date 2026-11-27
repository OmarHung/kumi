import { render, screen, within } from '@testing-library/react';
import { useState } from 'react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { QuestionCard, type QuestionAnswering } from './QuestionCard';
import type { AssistantQuestion } from './questionBlock';
import { QuestionDraftProvider } from './QuestionDrafts';

const QUESTIONS: readonly AssistantQuestion[] = [
  { question: '要做到什麼程度？', options: [{ label: '只評估與報價', description: '先給工時' }, { label: '完整做完' }], recommended: '只評估與報價' },
  { question: '誰負責？', options: [{ label: '阿哲' }, { label: 'Ivan' }] },
  { question: '要補哪些？', options: [{ label: 'A' }, { label: 'B' }], multiSelect: true },
];

function renderCard(overrides: Partial<QuestionAnswering> = {}, questions = QUESTIONS) {
  const onSubmit = vi.fn();
  const answering: QuestionAnswering = { isSending: false, error: null, onSubmit, ...overrides };
  const user = userEvent.setup();
  render(<QuestionCard questions={questions} answering={answering} />);
  return { user, onSubmit };
}

const progress = (text: string) => screen.getByText(text);

describe('問題卡片：一題一題作答', () => {
  it('一次顯示一題，寫「第 1／3 題」，建議的選項標「建議」，選項的說明看得到', () => {
    renderCard();

    expect(progress('第 1／3 題')).toBeInTheDocument();
    expect(screen.getByText('要做到什麼程度？')).toBeVisible();
    // 別題疊在同一格（高度不變）但對報讀與鍵盤都是隱藏的。
    expect(screen.queryByRole('button', { name: '阿哲' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /只評估與報價/ })).toHaveTextContent('建議');
    expect(screen.getByText('先給工時')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '其他' })).toBeInTheDocument();
  });

  it('單選：點選項就記下並直接到下一題；上一題回來答案還在', async () => {
    const { user } = renderCard();

    await user.click(screen.getByRole('button', { name: /完整做完/ }));

    expect(progress('第 2／3 題')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '上一題' }));
    expect(progress('第 1／3 題')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /完整做完/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('還沒回答的這一題不能按「下一題」，略過算回答過', async () => {
    const { user } = renderCard();
    const next = screen.getByRole('button', { name: '下一題' });

    await user.click(next);
    expect(progress('第 1／3 題')).toBeInTheDocument();
    expect(next).toHaveAttribute('aria-disabled', 'true');

    await user.click(screen.getByRole('button', { name: '略過' }));
    expect(progress('第 2／3 題')).toBeInTheDocument();
  });

  it('多選：可以勾好幾個，不會自動跳題，要按「下一題」才走', async () => {
    const { user } = renderCard({}, [QUESTIONS[2] as AssistantQuestion, QUESTIONS[1] as AssistantQuestion]);

    await user.click(screen.getByRole('button', { name: 'A' }));
    await user.click(screen.getByRole('button', { name: 'B' }));

    expect(progress('第 1／2 題')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'A' })).toHaveAttribute('aria-pressed', 'true');
    await user.click(screen.getByRole('button', { name: 'A' }));
    expect(screen.getByRole('button', { name: 'A' })).toHaveAttribute('aria-pressed', 'false');
    await user.click(screen.getByRole('button', { name: '下一題' }));
    expect(progress('第 2／2 題')).toBeInTheDocument();
  });

  it('「其他」展開輸入框；單選時換掉原本選的選項，空白的其他不算回答', async () => {
    const { user } = renderCard();
    await user.click(screen.getByRole('button', { name: /完整做完/ }));
    await user.click(screen.getByRole('button', { name: '上一題' }));

    await user.click(screen.getByRole('button', { name: '其他' }));

    expect(screen.getByRole('button', { name: /完整做完/ })).toHaveAttribute('aria-pressed', 'false');
    const input = screen.getByRole('textbox', { name: '其他的回答' });
    expect(screen.getByRole('button', { name: '下一題' })).toHaveAttribute('aria-disabled', 'true');
    await user.type(input, '先不做');
    expect(screen.getByRole('button', { name: '下一題' })).not.toHaveAttribute('aria-disabled');
  });

  it('全部答完才能送出；送出的文字照規格', async () => {
    const { user, onSubmit } = renderCard();

    await user.click(screen.getByRole('button', { name: /只評估與報價/ }));
    await user.click(screen.getByRole('button', { name: '其他' }));
    await user.type(screen.getByRole('textbox', { name: '其他的回答' }), '先不指派');
    await user.click(screen.getByRole('button', { name: '下一題' }));
    const submit = screen.getByRole('button', { name: '送出回答' });
    expect(submit).toHaveAttribute('aria-disabled', 'true');
    await user.click(submit);
    expect(onSubmit).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'A' }));
    await user.click(screen.getByRole('button', { name: 'B' }));
    await user.click(screen.getByRole('button', { name: '送出回答' }));

    expect(onSubmit).toHaveBeenCalledWith(
      ['回答你的問題：', '1. 要做到什麼程度？', '   → 只評估與報價', '2. 誰負責？', '   → 其他：先不指派', '3. 要補哪些？', '   → A、B'].join('\n'),
    );
  });

  it('最後一題略過：送出的文字寫（略過）', async () => {
    const { user, onSubmit } = renderCard({}, [{ question: '要不要分階段？', options: [] }]);

    await user.click(screen.getByRole('button', { name: '略過' }));
    await user.click(screen.getByRole('button', { name: '送出回答' }));

    expect(onSubmit).toHaveBeenCalledWith('回答你的問題：\n1. 要不要分階段？\n   → （略過）');
    expect(progress('問題')).toBeInTheDocument();
  });

  it('送出中：按鈕寫送出中、不能再按；失敗的說明顯示在卡片裡', async () => {
    const { user, onSubmit } = renderCard({ isSending: true, error: '送不出去' }, [{ question: 'q', options: [{ label: 'A' }] }]);

    const submit = screen.getByRole('button', { name: '送出中…' });
    await user.click(submit);

    expect(onSubmit).not.toHaveBeenCalled();
    expect(submit).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByText('送不出去')).toBeInTheDocument();
  });
});

describe('問題卡片：唯讀', () => {
  it('沒有 answering：只列出問題與選項，沒有可按的作答控制項', () => {
    render(<QuestionCard questions={QUESTIONS} />);

    const card = screen.getByRole('region', { name: '助理的問題' });
    expect(within(card).getByText('誰負責？')).toBeInTheDocument();
    expect(within(card).getByText(/只評估與報價/)).toHaveTextContent('建議');
    expect(within(card).queryByRole('button')).not.toBeInTheDocument();
  });
});

describe('問題卡片：焦點', () => {
  it('單選自動跳題、上一題、下一題之後，焦點在新那一題的第一個選項', async () => {
    const { user } = renderCard();

    await user.click(screen.getByRole('button', { name: /完整做完/ }));
    expect(screen.getByRole('button', { name: '阿哲' })).toHaveFocus();

    await user.click(screen.getByRole('button', { name: '上一題' }));
    expect(screen.getByRole('button', { name: /只評估與報價/ })).toHaveFocus();

    await user.click(screen.getByRole('button', { name: '略過' }));
    expect(screen.getByRole('button', { name: '阿哲' })).toHaveFocus();
    await user.click(screen.getByRole('button', { name: 'Ivan' }));
    expect(screen.getByRole('button', { name: 'A' })).toHaveFocus();
  });

  it('第一題的「上一題」還在、只是停用，不會整顆卸載讓焦點掉到頁面本體', async () => {
    const { user } = renderCard();
    const back = screen.getByRole('button', { name: '上一題' });

    back.focus();
    await user.click(back);

    expect(back).toHaveAttribute('aria-disabled', 'true');
    expect(back).toHaveFocus();
    expect(progress('第 1／3 題')).toBeInTheDocument();
  });
});

describe('問題卡片：作答到一半的內容', () => {
  function Toggling({ onSubmit }: { readonly onSubmit: () => void }) {
    const [isAnswering, setIsAnswering] = useState(true);
    return (
      <QuestionDraftProvider>
        <button type="button" onClick={() => { setIsAnswering(!isAnswering); }}>切換</button>
        <QuestionCard questions={QUESTIONS} draftKey="m1:0" answering={isAnswering ? { isSending: false, error: null, onSubmit } : undefined} />
      </QuestionDraftProvider>
    );
  }

  it('卡片在唯讀與可作答之間切換（回覆被接受後又失敗）：作答的草稿與題號保留', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<Toggling onSubmit={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: /完整做完/ }));
    await user.click(screen.getByRole('button', { name: 'Ivan' }));
    expect(progress('第 3／3 題')).toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '切換' }));
    expect(screen.queryByText('第 3／3 題')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '切換' }));

    // Assert
    expect(progress('第 3／3 題')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '上一題' }));
    expect(screen.getByRole('button', { name: 'Ivan' })).toHaveAttribute('aria-pressed', 'true');
  });
});

describe('問題卡片：唯讀的 key', () => {
  it('兩題文字相同也不會出重複 key 的警告', () => {
    render(<QuestionCard questions={[{ question: '同一題', options: [] }, { question: '同一題', options: [] }]} />);

    expect(screen.getAllByText('同一題')).toHaveLength(2);
  });
});
