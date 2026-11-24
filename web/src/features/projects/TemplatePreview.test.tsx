import { render, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Template } from '../../api/types';
import { TemplatePreview } from './TemplatePreview';
import { hierarchyChains, workflowParts } from './templatePreviewParts';

const bugTemplate: Template = {
  key: 'bug_tracking',
  name: '缺陷追蹤',
  workflows: [
    {
      name: '缺陷流程',
      itemTypes: ['缺陷'],
      states: ['新回報', '已確認', '已關閉'],
      stateCategories: ['not_started', 'in_progress', 'done'],
      canceledStates: ['不處理', '無法重現'],
    },
    {
      name: '任務流程',
      itemTypes: ['任務', '子任務'],
      states: ['待辦', '完成', '暫停'],
      stateCategories: ['not_started', 'done', 'backlog'],
      canceledStates: [],
    },
  ],
  parents: [
    { itemType: '決議', parents: ['會議'] },
    { itemType: '待辦', parents: ['會議', '決議'] },
  ],
  fields: ['嚴重度', '影響範圍'],
  exampleCount: 0,
};

function renderPreview(template: Template): HTMLElement {
  const { container } = render(<TemplatePreview id="preview" template={template} />);
  const preview = container.querySelector<HTMLElement>('#preview');
  if (preview === null) throw new Error('沒有畫出預覽');
  return preview;
}

/** 報讀軟體唸得到的字：aria-hidden 的圖示不算，visually-hidden 的標點算。 */
function spokenText(element: Element): string {
  return Array.from(element.childNodes)
    .map((node) => (node instanceof Element ? (node.getAttribute('aria-hidden') === 'true' ? '' : spokenText(node)) : (node.textContent ?? '')))
    .join('')
    .replace(/\s+/g, '');
}

describe('流程拆成主線與其他', () => {
  it('取消類與「完成」之後的狀態不在主線上', () => {
    // Arrange / Act
    const [, taskFlow] = bugTemplate.workflows;
    if (taskFlow === undefined) throw new Error('範例少了任務流程');
    const parts = workflowParts(taskFlow);

    // Assert
    expect(parts.main.map((state) => state.name)).toEqual(['待辦', '完成']);
    expect(parts.others).toEqual([{ name: '暫停', category: 'backlog' }]);
  });

  it('沒有「完成」類的狀態：整條都是主線，取消類另外列', () => {
    // Arrange
    const workflow = { name: '流程', itemTypes: ['項目'], states: ['構想', '進行中'], stateCategories: ['backlog', 'in_progress'] as const, canceledStates: ['擱置'] };

    // Act
    const parts = workflowParts(workflow);

    // Assert
    expect(parts.main.map((state) => state.name)).toEqual(['構想', '進行中']);
    expect(parts.others).toEqual([{ name: '擱置', category: 'canceled' }]);
  });
});

describe('流程拆成主線與其他：資料不齊', () => {
  it('類別比狀態少：少的當成未開始，不會整個壞掉', () => {
    // Arrange
    const workflow = { name: '流程', itemTypes: ['項目'], states: ['甲', '乙', '丙'], stateCategories: ['in_progress', 'done'] as const, canceledStates: [] };

    // Act
    const parts = workflowParts(workflow);

    // Assert
    expect(parts.main.map((state) => state.name)).toEqual(['甲', '乙']);
    expect(parts.others).toEqual([{ name: '丙', category: 'not_started' }]);
  });
});

describe('上下層畫成樹', () => {
  it('自己可以放在自己底下（任務底下還有任務）：一棵、一層', () => {
    // Arrange / Act
    const chains = hierarchyChains([{ itemType: '任務', parents: ['任務'] }]);

    // Assert
    expect(chains).toEqual([{ roots: ['任務'], levels: ['任務'] }]);
  });

  it('上層只有一種而且是上一筆的類型：接在它底下，再縮一層', () => {
    // Arrange / Act
    const chains = hierarchyChains([
      { itemType: '任務', parents: ['需求', '需求變更單'] },
      { itemType: '子任務', parents: ['任務'] },
    ]);

    // Assert
    expect(chains).toEqual([{ roots: ['需求', '需求變更單'], levels: ['任務', '子任務'] }]);
  });

  it('上層有好幾種：另起一棵', () => {
    // Arrange / Act
    const chains = hierarchyChains(bugTemplate.parents);

    // Assert
    expect(chains).toEqual([
      { roots: ['會議'], levels: ['決議'] },
      { roots: ['會議', '決議'], levels: ['待辦'] },
    ]);
  });
});

describe('TemplatePreview', () => {
  it('每個流程一行類型，下面是和清單一樣的狀態標籤（形狀跟著類別），箭頭接起來', () => {
    // Arrange / Act
    const preview = renderPreview(bugTemplate);

    // Assert
    const [bugChain] = within(preview).getAllByRole('list');
    if (bugChain === undefined) throw new Error('沒有畫出狀態');
    const steps = within(bugChain).getAllByRole('listitem');
    expect(steps.map((step) => step.querySelector('[data-tone]')?.textContent)).toEqual(['新回報', '已確認', '已關閉', '不處理']);
    expect(steps[0]?.querySelector('[data-icon="stateNotStarted"]')).not.toBeNull();
    expect(steps[2]?.querySelector('[data-icon="stateDone"]')).not.toBeNull();
    expect(steps.slice(0, 2).every((step) => step.querySelector('[data-icon="chevronRight"]') !== null)).toBe(true);
    expect(steps[2]?.querySelector('[data-icon="chevronRight"]')).toBeNull();
  });

  it('報讀時：類型、狀態之間唸「、」，主線之外的唸「另外」', () => {
    // Arrange / Act
    const preview = renderPreview(bugTemplate);

    // Assert
    const workflows = Array.from(preview.children).slice(0, 2);
    expect(workflows.map(spokenText)).toEqual([
      '缺陷：新回報、已確認、已關閉；另外：不處理、無法重現。',
      '任務、子任務：待辦、完成；另外：暫停。',
    ]);
  });

  it('上下層與欄位排在流程後面：上下層唸成「底下是」，欄位是一個個標籤', () => {
    // Arrange / Act
    const preview = renderPreview(bugTemplate);

    // Assert
    const facts = preview.querySelector('dl');
    if (facts === null) throw new Error('沒有畫出上下層與欄位');
    expect(spokenText(facts)).toBe('上下層：會議，底下是決議；會議或決議，底下是待辦。欄位：嚴重度、影響範圍');
    // 畫面上是「會議／決議」（「／」aria-hidden），報讀唸「會議或決議」。
    expect(facts.textContent).toContain('會議／或決議');
    expect(facts.querySelectorAll('[data-icon="subItem"]')).toHaveLength(2);
  });

  it('沒有 parents 時不顯示上下層', () => {
    // Arrange / Act
    const preview = renderPreview({ ...bugTemplate, parents: [] });

    // Assert
    expect(preview).not.toHaveTextContent('上下層');
    expect(preview).toHaveTextContent('欄位');
  });

  it('沒有欄位時不顯示欄位', () => {
    // Arrange / Act
    const preview = renderPreview({ ...bugTemplate, fields: [] });

    // Assert
    expect(preview).not.toHaveTextContent('欄位');
    expect(preview).toHaveTextContent('上下層');
  });

  it('什麼都沒有：不畫', () => {
    // Arrange / Act
    const { container } = render(<TemplatePreview id="preview" template={{ ...bugTemplate, workflows: [], parents: [], fields: [] }} />);

    // Assert
    expect(container).toBeEmptyDOMElement();
  });

  it('是一般區塊：沒有 role 與 aria-label（aria-label 會蓋掉內容）', () => {
    // Arrange / Act
    const preview = renderPreview(bugTemplate);

    // Assert
    expect(preview).not.toHaveAttribute('role');
    expect(preview).not.toHaveAttribute('aria-label');
  });
});
