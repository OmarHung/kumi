import type { DescriptionTemplate } from '../api/types';

/** 後端內建的「網站案子」格式（internal/HANDOFF-project-info.md 4.6）；測試用縮短的內容。 */
export const webProjectTemplate: DescriptionTemplate = {
  id: 'web_project',
  name: '網站案子',
  body: '## 基本資料\n- 客戶：\n\n## 網址\n- 正式：\n- 測試：\n',
  isBuiltIn: true,
  isDefault: true,
};
