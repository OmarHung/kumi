import type { SubNavGroup } from '../../components/SubNav/SubNav';

export const HELP_PATH = '/help';
export const HELP_LABEL = '使用說明';

export type HelpChapterKey = 'quick-start';

export interface HelpChapter {
  readonly key: HelpChapterKey;
  /** 次導覽的名稱，也是頁列的 h1 與瀏覽器分頁的標題。 */
  readonly label: string;
  readonly path: string;
}

interface HelpGroup extends SubNavGroup {
  readonly items: readonly HelpChapter[];
}

/** 使用說明的每一章，依組、照這個順序排在次導覽；第一組的第一章是 /help 打開時看到的。 */
const HELP_GROUPS: readonly HelpGroup[] = [
  {
    key: 'start',
    label: '開始使用',
    items: [{ key: 'quick-start', label: '快速上手', path: `${HELP_PATH}/quick-start` }],
  },
];

export const HELP_CHAPTERS: readonly HelpChapter[] = HELP_GROUPS.flatMap((group) => group.items);

/** 網址的那一段對到哪一章；沒有那一段是第一章，不認得的是 undefined（找不到的頁）。 */
export function findHelpChapter(segment: string | undefined): HelpChapter | undefined {
  if (segment === undefined) return HELP_CHAPTERS[0];
  return HELP_CHAPTERS.find((chapter) => chapter.key === segment);
}

export function helpNavGroups(): readonly SubNavGroup[] {
  return HELP_GROUPS;
}
