export const TEMPLATES_PATH = '/description-templates';

export function editTemplatePath(id: string): string {
  return `${TEMPLATES_PATH}/${encodeURIComponent(id)}`;
}

/** 新增頁；給了 from 就帶入那一份的名稱與內容（複製內建的）。 */
export function newTemplatePath(from?: string): string {
  return from === undefined ? `${TEMPLATES_PATH}/new` : `${TEMPLATES_PATH}/new?from=${encodeURIComponent(from)}`;
}
