import { myWorkFilterEntries, type MyWorkFilters } from '../../domain/myWorkView';

/** 條件在網址上用的參數（domain/myWorkView.ts 的 myWorkFilterEntries 會寫的那幾個）；舊網址的 assignee 也算，換條件時順便拿掉。 */
const FILTER_PARAMS: readonly string[] = ['q', 'type', 'category', 'state', 'assignee', 'due', 'priority', 'project', 'mine'];

/**
 * 換上新的條件：版面、分組、行事曆的位置、開著的面板（view、group、month、item…）都留著，
 * 只換條件那幾個參數；預設的條件不寫進網址。
 */
export function withMyWorkFilters(params: URLSearchParams, filters: MyWorkFilters): URLSearchParams {
  const next = new URLSearchParams([...params].filter(([name]) => !FILTER_PARAMS.includes(name)));
  for (const [name, value] of myWorkFilterEntries(filters)) next.append(name, value);
  return next;
}
