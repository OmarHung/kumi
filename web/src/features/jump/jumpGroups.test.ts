import { describe, expect, it } from 'vitest';
import type { Project, ProjectSearchHit } from '../../api/types';
import { agyItem, agyProject, smkItem, smkProject, webProject } from '../../test/fixtures';
import { saveProjectView } from '../projects/projectViewPreference';
import { buildJumpGroups, flattenGroups, itemKeyTarget, PROJECT_LIMIT, type JumpOption, type JumpSources } from './jumpGroups';
import type { RecentItem } from './recentItems';

const myWorkPage: JumpOption = {
  key: 'page:my-work',
  label: '我的工作',
  icon: 'myWork',
  action: { kind: 'navigate', to: '/my-work' },
};
const projectsPage: JumpOption = {
  key: 'page:projects',
  label: '專案',
  icon: 'projects',
  action: { kind: 'navigate', to: '/projects' },
};

const recentAgy3: RecentItem = {
  key: 'AGY-3',
  projectKey: 'AGY',
  title: '首頁設計稿',
};
const recentSmk1: RecentItem = {
  key: 'SMK-1',
  projectKey: 'SMK',
  title: '登入流程',
};

function sources(overrides: Partial<JumpSources> = {}): JumpSources {
  return {
    query: '',
    recent: [recentAgy3, recentSmk1],
    projects: [agyProject, smkProject],
    pages: [myWorkPage, projectsPage],
    searchResults: [],
    descriptionHits: [],
    currentProjectKey: null,
    ...overrides,
  };
}

function labelsOf(groups: ReturnType<typeof buildJumpGroups>) {
  return groups.map((group) => [group.label, group.options.map((option) => option.label)]);
}

describe('itemKeyTarget', () => {
  it('像項目編號的輸入：正規化成大寫，專案代號是連字號前面那一段', () => {
    // Arrange
    const inputs = ['AGY-12', 'agy-12', '  Agy-12 ', 'R2D2-7'];

    // Act
    const targets = inputs.map(itemKeyTarget);

    // Assert
    expect(targets).toEqual([
      { itemKey: 'AGY-12', projectKey: 'AGY' },
      { itemKey: 'AGY-12', projectKey: 'AGY' },
      { itemKey: 'AGY-12', projectKey: 'AGY' },
      { itemKey: 'R2D2-7', projectKey: 'R2D2' },
    ]);
  });

  it('不像編號的輸入不算', () => {
    // Arrange
    const inputs = ['', 'AGY', 'AGY-', '12', '-12', 'AGY 12', '1AGY-2', 'AGY-12a', 'AGY-1-2', '首頁-1'];

    // Act
    const targets = inputs.map(itemKeyTarget);

    // Assert
    expect(targets.every((target) => target === null)).toBe(true);
  });
});

describe('buildJumpGroups', () => {
  it('沒有輸入：最近看過、專案、頁面，照這個順序', () => {
    // Arrange、Act
    const groups = buildJumpGroups(sources());

    // Assert
    expect(labelsOf(groups)).toEqual([
      ['最近看過', ['首頁設計稿', '登入流程']],
      ['專案', ['接案測試', '冒煙測試']],
      ['頁面', ['我的工作', '專案']],
    ]);
  });

  it('輸入像編號時，第一筆是「前往」那個項目，連到它的項目頁', () => {
    // Arrange、Act
    const groups = buildJumpGroups(sources({ query: 'agy-12' }));

    // Assert
    const [first] = flattenGroups(groups);
    expect(groups[0]?.label).toBe('編號');
    expect(first).toMatchObject({
      label: '前往 AGY-12',
      action: { kind: 'navigate', to: '/projects/AGY/items/AGY-12' },
    });
  });

  it('最近看過的項目照編號或標題比對，不分大小寫', () => {
    // Arrange
    const byTitle = sources({ query: '登入' });
    const byKey = sources({ query: 'agy' });

    // Act
    const titleGroups = buildJumpGroups(byTitle);
    const keyGroups = buildJumpGroups(byKey);

    // Assert
    expect(titleGroups.find((group) => group.key === 'recent')?.options.map((option) => option.code)).toEqual(['SMK-1']);
    expect(keyGroups.find((group) => group.key === 'recent')?.options.map((option) => option.code)).toEqual(['AGY-3']);
  });

  it('最近看過的項目連到它的項目頁，編號是等寬的代號', () => {
    // Arrange、Act
    const groups = buildJumpGroups(sources({ query: '首頁' }));

    // Assert
    const recent = groups.find((group) => group.key === 'recent')?.options[0];
    expect(recent).toMatchObject({
      code: 'AGY-3',
      label: '首頁設計稿',
      action: { kind: 'navigate', to: '/projects/AGY/items/AGY-3' },
    });
  });

  it('專案照代號或名稱比對，連到專案頁', () => {
    // Arrange
    const projects = [agyProject, smkProject, webProject];

    // Act
    const byKey = buildJumpGroups(sources({ query: 'smk', projects }));
    const byName = buildJumpGroups(sources({ query: '官網', projects }));

    // Assert
    expect(byKey.find((group) => group.key === 'projects')?.options).toEqual([
      {
        key: 'project:SMK',
        code: 'SMK',
        label: '冒煙測試',
        action: { kind: 'navigate', to: '/projects/SMK' },
      },
    ]);
    expect(byName.find((group) => group.key === 'projects')?.options.map((option) => option.code)).toEqual(['WEB']);
  });

  it('專案照上次用的檢視開（和側欄的專案連結一樣）', () => {
    // Arrange
    saveProjectView('SMK', 'board');

    // Act
    const groups = buildJumpGroups(sources({ query: '' }));

    // Assert
    const options = groups.find((group) => group.key === 'projects')?.options ?? [];
    expect(options.map((option) => option.action)).toEqual([
      { kind: 'navigate', to: '/projects/AGY' },
      { kind: 'navigate', to: '/projects/SMK?view=board' },
    ]);
  });

  it(`專案最多列 ${String(PROJECT_LIMIT)} 個`, () => {
    // Arrange
    const projects: Project[] = Array.from({ length: PROJECT_LIMIT + 3 }, (_, index) => ({
      ...agyProject,
      id: `p-${String(index)}`,
      key: `P${String(index)}`,
      name: `專案 ${String(index)}`,
    }));

    // Act
    const groups = buildJumpGroups(sources({ projects }));

    // Assert
    expect(groups.find((group) => group.key === 'projects')?.options).toHaveLength(PROJECT_LIMIT);
  });

  it('封存的專案排在沒封存的後面，加「已封存」的標示', () => {
    // Arrange
    const archived: Project = {
      ...smkProject,
      archivedAt: '2026-11-18T08:00:00Z',
    };

    // Act
    const options = buildJumpGroups(sources({ projects: [archived, agyProject] })).find((group) => group.key === 'projects')?.options ?? [];

    // Assert
    expect(options.map((option) => [option.code, option.tag])).toEqual([
      ['AGY', undefined],
      ['SMK', '已封存'],
    ]);
  });

  it('專案太多放不下時，先捨去封存的：封存的不會擠掉進行中的專案', () => {
    // Arrange
    const archived: Project = {
      ...smkProject,
      archivedAt: '2026-11-18T08:00:00Z',
    };
    const active: Project[] = Array.from({ length: PROJECT_LIMIT }, (_, index) => ({
      ...agyProject,
      id: `p-${String(index)}`,
      key: `P${String(index)}`,
      name: `專案 ${String(index)}`,
    }));

    // Act
    const options = buildJumpGroups(sources({ projects: [archived, ...active] })).find((group) => group.key === 'projects')?.options ?? [];

    // Assert
    expect(options).toHaveLength(PROJECT_LIMIT);
    expect(options.some((option) => option.code === 'SMK')).toBe(false);
  });

  describe('說明比對到的專案', () => {
    const hit = (project: Project, snippet: string | null): ProjectSearchHit => ({ project, snippet });

    it('接在前端比對到的專案後面，靠右是說明的摘錄', () => {
      // Arrange
      const hits = [hit(webProject, '主機：web01.example.com')];

      // Act
      const options = buildJumpGroups(sources({ query: 'example', descriptionHits: hits })).find(
        (group) => group.key === 'projects',
      )?.options;

      // Assert
      expect(options).toEqual([
        {
          key: 'project:WEB',
          code: 'WEB',
          label: '官網改版',
          hint: '主機：web01.example.com',
          action: { kind: 'navigate', to: '/projects/WEB' },
        },
      ]);
    });

    it('前端已經列出的專案不重複，只有說明比對到的（snippet 不是 null）才加進來', () => {
      // Arrange
      const hits = [hit(smkProject, null), hit(webProject, '網址：https://www.example.com'), hit(agyProject, '備註：agy 的主機')];

      // Act
      const options = buildJumpGroups(sources({ query: 'agy', descriptionHits: hits })).find((group) => group.key === 'projects')?.options;

      // Assert
      expect(options?.map((option) => [option.code, option.hint])).toEqual([
        ['AGY', undefined],
        ['WEB', '網址：https://www.example.com'],
      ]);
    });

    it('封存的專案帶「已封存」的標籤，和摘錄分開（摘錄被截斷時標籤還在）', () => {
      // Arrange
      const archived: Project = {
        ...webProject,
        archivedAt: '2026-11-18T08:00:00Z',
      };

      // Act
      const options = buildJumpGroups(
        sources({
          query: 'x',
          descriptionHits: [hit(archived, '主機：web01')],
        }),
      ).find((group) => group.key === 'projects')?.options;

      // Assert
      expect(options?.[0]).toMatchObject({
        hint: '主機：web01',
        tag: '已封存',
      });
    });

    it(`組內總共最多 ${String(PROJECT_LIMIT)} 個：前端比對的先佔位，說明的補滿剩下的`, () => {
      // Arrange
      const named = (index: number): Project => ({
        ...agyProject,
        id: `n-${String(index)}`,
        key: `N${String(index)}`,
        name: `x ${String(index)}`,
      });
      const frontEnd = Array.from({ length: PROJECT_LIMIT - 1 }, (_, index) => named(index));
      const hits = [hit(webProject, '主機：x1'), hit({ ...webProject, id: 'w2', key: 'WB2' }, '主機：x2')];

      // Act
      const options = buildJumpGroups(sources({ query: 'x', projects: frontEnd, descriptionHits: hits })).find(
        (group) => group.key === 'projects',
      )?.options;

      // Assert
      expect(options).toHaveLength(PROJECT_LIMIT);
      expect(options?.at(-1)?.code).toBe('WEB');
    });
  });

  it('頁面照名稱比對；沒有符合的分組不出現', () => {
    // Arrange、Act
    const groups = buildJumpGroups(sources({ query: '工作' }));

    // Assert
    expect(labelsOf(groups)).toEqual([['頁面', ['我的工作']]]);
  });

  it('這個專案的搜尋結果放在最後，照伺服器的順序', () => {
    // Arrange
    const searchResults = [agyItem({ number: 9, title: '首頁的橫幅' }), agyItem({ number: 4, title: '首頁的頁尾' })];

    // Act
    const groups = buildJumpGroups(sources({ query: '首頁', recent: [], searchResults }));

    // Assert
    const last = groups.at(-1);
    expect(last?.label).toBe('項目');
    expect(last?.options.map((option) => [option.code, option.label])).toEqual([
      ['AGY-9', '首頁的橫幅'],
      ['AGY-4', '首頁的頁尾'],
    ]);
  });

  it('別的專案的搜尋結果靠右帶專案名稱，目前專案的不帶；順序照伺服器的，不把目前專案提前', () => {
    // Arrange
    const searchResults = [smkItem({ number: 7, title: '首頁看板' }), agyItem({ number: 9, title: '首頁的橫幅' })];

    // Act
    const groups = buildJumpGroups(
      sources({
        query: '首頁',
        recent: [],
        searchResults,
        currentProjectKey: 'AGY',
      }),
    );

    // Assert
    const options = groups.find((group) => group.key === 'search')?.options;
    expect(options?.map((option) => [option.code, option.hint])).toEqual([
      ['SMK-7', '冒煙測試'],
      ['AGY-9', undefined],
    ]);
  });

  it('不在專案裡時，每一筆搜尋結果都帶專案名稱；專案清單裡找不到就用代號', () => {
    // Arrange
    const searchResults = [agyItem({ number: 9, title: '首頁的橫幅' }), smkItem({ number: 7, title: '首頁看板' })];

    // Act
    const groups = buildJumpGroups(
      sources({
        query: '首頁',
        recent: [],
        projects: [agyProject],
        searchResults,
        currentProjectKey: null,
      }),
    );

    // Assert
    const options = groups.find((group) => group.key === 'search')?.options;
    expect(options?.map((option) => option.hint)).toEqual(['接案測試', 'SMK']);
  });

  it('同一個項目只出現一次：最近看過的項目不會在搜尋結果裡再出現', () => {
    // Arrange
    const searchResults = [agyItem({ number: 3, title: '首頁設計稿' }), agyItem({ number: 5, title: '首頁的頁尾' })];

    // Act
    const groups = buildJumpGroups(sources({ query: '首頁', searchResults }));

    // Assert
    const codes = flattenGroups(groups).map((option) => option.code);
    expect(codes.filter((code) => code === 'AGY-3')).toHaveLength(1);
    expect(groups.find((group) => group.key === 'search')?.options.map((option) => option.code)).toEqual(['AGY-5']);
  });

  it('「前往」的項目已經知道標題時帶上標題，後面的分組不再重複它', () => {
    // Arrange、Act
    const groups = buildJumpGroups(
      sources({
        query: 'AGY-3',
        searchResults: [agyItem({ number: 3, title: '首頁設計稿' })],
      }),
    );

    // Assert
    expect(flattenGroups(groups)).toEqual([
      expect.objectContaining({
        key: 'item:AGY-3',
        label: '前往 AGY-3',
        hint: '首頁設計稿',
      }),
    ]);
  });

  it('輸入前後的空白不算', () => {
    // Arrange、Act
    const groups = buildJumpGroups(sources({ query: '  冒煙 ' }));

    // Assert
    expect(labelsOf(groups)).toEqual([['專案', ['冒煙測試']]]);
  });

  it('什麼都沒有符合時是空的', () => {
    // Arrange、Act
    const groups = buildJumpGroups(sources({ query: '找不到的字' }));

    // Assert
    expect(groups).toEqual([]);
  });
});
