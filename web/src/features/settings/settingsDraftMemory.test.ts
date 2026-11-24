import { QueryClient } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';
import { draftFromBlueprint } from '../../domain/settingsDraft';
import { simpleTasksBlueprint } from '../../test/blueprints';
import {
  draftToResume,
  forgetChangeSetDraft,
  forgetDraft,
  forgetProjectDrafts,
  hasProposedDraft,
  releaseProposedDraft,
  rememberProposedDraft,
  type ProposedDraft,
} from './settingsDraftMemory';

const ME = 'person-me';
const SOMEONE_ELSE = 'person-other';

function proposed(overrides: Partial<ProposedDraft> = {}): ProposedDraft {
  return {
    personId: ME,
    projectKey: 'SMK',
    session: { base: simpleTasksBlueprint, baseVersion: 3, draft: draftFromBlueprint(simpleTasksBlueprint) },
    summary: '拆開審查',
    section: 'fields',
    ...overrides,
  };
}

describe('設定頁草稿的記憶體', () => {
  it('送出的草稿要同一個專案、同一個基準版本、同一份變更集、同一個人才找得到', () => {
    // Arrange
    const client = new QueryClient();

    // Act
    rememberProposedDraft(client, proposed(), 7);

    // Assert
    expect(hasProposedDraft(client, 'SMK', 3, 7, ME)).toBe(true);
    expect(hasProposedDraft(client, 'SMK', 2, 7, ME)).toBe(false);
    expect(hasProposedDraft(client, 'SMK', 3, 8, ME)).toBe(false);
    expect(hasProposedDraft(client, 'AGY', 3, 7, ME)).toBe(false);
    expect(hasProposedDraft(client, 'SMK', 3, 7, SOMEONE_ELSE)).toBe(false);
  });

  it('同一個專案再送出一次：換成新的那一份，連不同基準版本的舊草稿也一起換掉', () => {
    // Arrange
    const client = new QueryClient();
    rememberProposedDraft(client, proposed(), 7);
    const older = proposed({ session: { ...proposed().session, baseVersion: 2 } });
    rememberProposedDraft(client, older, 5);

    // Act
    rememberProposedDraft(client, proposed({ summary: '再改一次' }), 8);

    // Assert
    expect(hasProposedDraft(client, 'SMK', 3, 7, ME)).toBe(false);
    expect(hasProposedDraft(client, 'SMK', 2, 5, ME)).toBe(false);
    expect(hasProposedDraft(client, 'SMK', 3, 8, ME)).toBe(true);
  });

  it('按「回去修改」放棄之後：變更集那一份找不到，設定頁接得到（回到原本的區塊）；接回去之後就沒有了', () => {
    // Arrange
    const client = new QueryClient();
    const draft = proposed();
    rememberProposedDraft(client, draft, 7);

    // Act
    const section = releaseProposedDraft(client, 'SMK', 3, 7);

    // Assert
    expect(section).toBe('fields');
    expect(hasProposedDraft(client, 'SMK', 3, 7, ME)).toBe(false);
    expect(draftToResume(client, 'SMK', SOMEONE_ELSE)).toBeUndefined();
    const resumed = draftToResume(client, 'SMK', ME);
    expect(resumed?.summary).toBe('拆開審查');
    expect(resumed?.session).toBe(draft.session);
    if (resumed === undefined) throw new Error('應該接得到草稿。');
    forgetDraft(client, resumed);
    expect(draftToResume(client, 'SMK', ME)).toBeUndefined();
  });

  it('沒有記下的變更集放棄後不能回去修改：回傳 null，設定頁也接不到', () => {
    // Arrange
    const client = new QueryClient();
    rememberProposedDraft(client, proposed(), 7);

    // Act
    const section = releaseProposedDraft(client, 'SMK', 3, 6);

    // Assert
    expect(section).toBeNull();
    expect(draftToResume(client, 'SMK', ME)).toBeUndefined();
    expect(hasProposedDraft(client, 'SMK', 3, 7, ME)).toBe(true);
  });

  it('還在等待核准的草稿，設定頁不會自己接回去', () => {
    // Arrange
    const client = new QueryClient();

    // Act
    rememberProposedDraft(client, proposed(), 7);

    // Assert
    expect(draftToResume(client, 'SMK', ME)).toBeUndefined();
  });

  it('套用或放棄只清掉那一份變更集的草稿', () => {
    // Arrange
    const client = new QueryClient();
    rememberProposedDraft(client, proposed(), 7);

    // Act
    forgetChangeSetDraft(client, 'SMK', 3, 6);
    const afterOther = hasProposedDraft(client, 'SMK', 3, 7, ME);
    forgetChangeSetDraft(client, 'SMK', 3, 7);

    // Assert
    expect(afterOther).toBe(true);
    expect(hasProposedDraft(client, 'SMK', 3, 7, ME)).toBe(false);
  });

  it('設定頁捨棄修改：清掉這個專案的草稿，別的專案的不動', () => {
    // Arrange
    const client = new QueryClient();
    rememberProposedDraft(client, proposed(), 7);
    rememberProposedDraft(client, proposed({ projectKey: 'AGY' }), 2);

    // Act
    forgetProjectDrafts(client, 'SMK');

    // Assert
    expect(hasProposedDraft(client, 'SMK', 3, 7, ME)).toBe(false);
    expect(hasProposedDraft(client, 'AGY', 3, 2, ME)).toBe(true);
  });

  it('每個 QueryClient 各記各的', () => {
    // Arrange
    const first = new QueryClient();
    const second = new QueryClient();

    // Act
    rememberProposedDraft(first, proposed(), 7);

    // Assert
    expect(hasProposedDraft(first, 'SMK', 3, 7, ME)).toBe(true);
    expect(hasProposedDraft(second, 'SMK', 3, 7, ME)).toBe(false);
  });
});
