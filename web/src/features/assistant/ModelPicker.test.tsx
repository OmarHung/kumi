import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { AiOptions } from '../../api/aiTypes';
import { makeAiOptions } from '../../test/assistant';
import { openOptions } from '../../test/select';
import { ModelPicker } from './ModelPicker';

const BASE = makeAiOptions();
const [anthropic] = BASE.connections;
if (anthropic === undefined) throw new Error('測試資料缺連線');

/** 組織的 Anthropic，加上自己電腦上的 Claude Code 與 Codex（personal: true，模型第一個是「預設」）。 */
const WITH_LOCAL: AiOptions = {
  ...BASE,
  connections: [
    anthropic,
    {
      id: 'local-1',
      name: 'Claude Code（範例筆電）',
      kind: 'claude_code',
      personal: true, hosted: false, notice: null,
      models: [
        { id: 'default', displayName: '預設', efforts: [], defaultEffort: null },
        { id: 'opus', displayName: 'Opus', efforts: ['low', 'high', 'max'], defaultEffort: null },
      ],
    },
    {
      id: 'local-2',
      name: 'Codex（範例筆電）',
      kind: 'codex',
      personal: true, hosted: false, notice: null,
      models: [{ id: 'default', displayName: '預設', efforts: [], defaultEffort: null }],
    },
  ],
};

describe('模型選單：自己電腦上的引擎', () => {
  it('本機的引擎各自一組，組名是連線的名稱加「（我的）」，選項是引擎回報的模型', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<ModelPicker options={WITH_LOCAL} selection={{ connectionId: 'conn-1', model: 'claude-haiku-5', effort: null }} onChange={vi.fn()} />);

    // Act
    const options = await openOptions(user, screen.getByRole('combobox', { name: '模型' }));

    // Assert
    expect(options.map((option) => option.dataset.value)).toEqual([
      'conn-1|claude-opus-5-5',
      'conn-1|claude-haiku-5',
      'local-1|default',
      'local-1|opus',
      'local-2|default',
    ]);
    const listbox = screen.getByRole('listbox');
    expect(within(listbox).getByText('Claude Code（範例筆電）（我的）')).toBeInTheDocument();
    expect(within(listbox).getByText('Codex（範例筆電）（我的）')).toBeInTheDocument();
    expect(within(listbox).getByText('Anthropic')).toBeInTheDocument();
  });

  it('選本機引擎的模型：回傳連線編號與模型代號；有思考深度的才出現第二個選單', async () => {
    // Arrange
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ModelPicker options={WITH_LOCAL} selection={{ connectionId: 'local-1', model: 'default', effort: null }} onChange={onChange} />);
    expect(screen.queryByRole('combobox', { name: '思考深度' })).not.toBeInTheDocument();

    // Act
    const options = await openOptions(user, screen.getByRole('combobox', { name: '模型' }));
    await user.click(options.find((option) => option.dataset.value === 'local-1|opus') ?? options[0] ?? document.body);

    // Assert
    expect(onChange).toHaveBeenCalledWith({ connectionId: 'local-1', model: 'opus', effort: null });
  });

  it('思考深度：本機引擎的模型回報的深度照樣列出', () => {
    // Arrange / Act
    render(<ModelPicker options={WITH_LOCAL} selection={{ connectionId: 'local-1', model: 'opus', effort: 'max' }} onChange={vi.fn()} />);

    // Assert
    expect(screen.getByRole('combobox', { name: '思考深度' })).toHaveTextContent('最高');
  });
});

describe('思考深度的「預設」', () => {
  it('知道實際用哪一級時，按鈕與「預設」那一項都寫「預設（中）」', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<ModelPicker options={WITH_LOCAL} selection={{ connectionId: 'conn-1', model: 'claude-opus-5-5', effort: null }} onChange={vi.fn()} />);

    // Act
    const combobox = screen.getByRole('combobox', { name: '思考深度' });
    const options = await openOptions(user, combobox);

    // Assert
    expect(combobox).toHaveTextContent('預設（中）');
    expect(options.map((option) => option.textContent)).toEqual(expect.arrayContaining(['預設（中）']));
  });

  it('不知道時只寫「預設」；換到別的模型跟著變', () => {
    // Arrange
    const selection = { connectionId: 'local-1', model: 'opus', effort: null };
    const { rerender } = render(<ModelPicker options={WITH_LOCAL} selection={selection} onChange={vi.fn()} />);
    const combobox = screen.getByRole('combobox', { name: '思考深度' });
    expect(combobox).toHaveTextContent(/^預設$/);

    // Act
    rerender(<ModelPicker options={WITH_LOCAL} selection={{ connectionId: 'conn-1', model: 'claude-opus-5-5', effort: null }} onChange={vi.fn()} />);

    // Assert
    expect(screen.getByRole('combobox', { name: '思考深度' })).toHaveTextContent('預設（中）');
  });
});

/** 組織擁有者開的主機來源（hosted）：組名就是來源的名稱，不加「（我的）」；不能用時（notice）整組都不能選。 */
function withHosted(notice: string | null): AiOptions {
  return {
    ...BASE,
    connections: [
      ...BASE.connections,
      {
        id: 'src-1',
        name: '主機上的 Claude Code',
        kind: 'claude_code',
        personal: false,
        hosted: true,
        notice,
        models: [{ id: 'opus', displayName: 'Opus', efforts: ['low', 'high'], defaultEffort: 'high' }],
      },
    ],
  };
}

describe('模型選單：主機上的來源', () => {
  it('主機來源自成一組，組名是來源的名稱', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<ModelPicker options={withHosted(null)} selection={{ connectionId: 'conn-1', model: 'claude-haiku-5', effort: null }} onChange={vi.fn()} />);

    // Act
    const options = await openOptions(user, screen.getByRole('combobox', { name: '模型' }));

    // Assert
    expect(options.map((option) => option.dataset.value)).toContain('src-1|opus');
    expect(within(screen.getByRole('listbox')).getByText('主機上的 Claude Code')).toBeInTheDocument();
  });

  it('來源現在不能用：那一組顯示伺服器給的說明、模型選不了', async () => {
    // Arrange
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ModelPicker options={withHosted('主機離線中，請聯絡組織擁有者。')} selection={{ connectionId: 'conn-1', model: 'claude-haiku-5', effort: null }} onChange={onChange} />);

    // Act
    const options = await openOptions(user, screen.getByRole('combobox', { name: '模型' }));
    const hosted = options.find((option) => option.dataset.value === 'src-1|opus');
    await user.click(hosted ?? document.body);

    // Assert
    expect(hosted).toHaveAttribute('aria-disabled', 'true');
    expect(within(screen.getByRole('listbox')).getByText('主機離線中，請聯絡組織擁有者。')).toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
  });
});
