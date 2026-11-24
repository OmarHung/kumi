import { useId, useState, type KeyboardEvent } from 'react';
import { useLocation } from 'react-router';
import { useProjectDetails, useProjects } from '../../api/queries';
import { useCurrentUser } from '../../auth/CurrentUser';
import { Button } from '../../components/Button/Button';
import { Dialog } from '../../components/Dialog/Dialog';
import { TextField } from '../../components/Field/Field';
import { Notice } from '../../components/Notice/Notice';
import { buildJumpGroups, flattenGroups, type JumpAction } from './jumpGroups';
import { currentProjectKey, jumpPages } from './jumpPages';
import { JumpResults, jumpOptionId } from './JumpResults';
import { readRecentItems } from './recentItems';
import { useItemSearch, type ItemSearchStatus } from './useItemSearch';
import { useProjectSearch } from './useProjectSearch';
import { useJumpCombobox } from './useJumpCombobox';
import styles from './JumpDialog.module.css';

/**
 * 對話框的標題，也是輸入框的名稱（標題就是看得到的標籤）。不帶省略號：已經在這裡了，不是「按了還會出現什麼」；
 * 側欄的按鈕與快捷鍵的說明才是「搜尋或跳到…」（SHORTCUTS.openJump.description）。
 */
export const JUMP_TITLE = '搜尋或跳到';

// 範圍要說清楚：項目是跨專案搜尋，在不在專案裡都一樣，所以提示只有一句。
const HINT = '輸入編號、標題、專案、主機名稱或頁面';

/** 兩個伺服器搜尋（項目、專案說明）合起來的狀態：任何一個還在等就是 pending；失敗的是哪一個決定提示怎麼寫。 */
interface JumpSearch {
  readonly status: 'off' | 'pending' | 'done';
  readonly isSettled: boolean;
  readonly failure: string | null;
}

function failureMessage(items: ItemSearchStatus, projects: ItemSearchStatus): string | null {
  if (items === 'error' && projects === 'error') return '搜尋失敗，請稍後再試。';
  if (items === 'error') return '項目搜尋失敗，請稍後再試。';
  if (projects === 'error') return '專案說明搜尋失敗，請稍後再試。';
  return null;
}

function combineSearches(items: ItemSearchStatus, projects: ItemSearchStatus, isSettled: boolean): JumpSearch {
  const isPending = items === 'pending' || projects === 'pending';
  const isOff = items === 'off' && projects === 'off';
  return {
    status: isPending ? 'pending' : isOff ? 'off' : 'done',
    isSettled,
    failure: failureMessage(items, projects),
  };
}

function statusText(count: number, search: JumpSearch): string {
  const found = `找到 ${String(count)} 筆`;
  if (search.status === 'pending') return count > 0 ? `${found}，正在搜尋…` : '正在搜尋…';
  return count > 0 ? `${found}。` : '沒有符合的結果。';
}

/**
 * 結果的說明（role="status"）打字中不換：每打一個字就換，報讀軟體會和字元的回音搶著唸。
 * 停下來（和送出搜尋同一個 300ms）才換成新的；伺服器的結果回來時再換一次。
 */
function useSettledStatus(message: string, isSettled: boolean): string {
  const [shown, setShown] = useState(message);
  if (isSettled && shown !== message) {
    setShown(message);
  }
  return isSettled ? message : shown;
}

/** 結果的來源：目前所在的專案（網址）、已經載入的專案清單、權限、最近看過的項目、跨專案的項目搜尋、專案說明的搜尋。 */
function useJumpSources(query: string) {
  const { pathname } = useLocation();
  const projectKey = currentProjectKey(pathname);
  const details = useProjectDetails(projectKey === null ? [] : [projectKey]);
  const project = [...details.values()][0] ?? null;
  const projects = useProjects().data ?? [];
  const user = useCurrentUser();
  // 打開時讀一次：對話框開著的期間不會再看別的項目。
  const [recent] = useState(readRecentItems);
  const itemSearch = useItemSearch(query);
  const projectSearch = useProjectSearch(query);
  const search = combineSearches(itemSearch.status, projectSearch.status, itemSearch.isSettled && projectSearch.isSettled);
  const pages = jumpPages({ user, project });
  const groups = buildJumpGroups({
    query,
    recent,
    projects,
    pages,
    searchResults: itemSearch.items,
    descriptionHits: projectSearch.hits,
    currentProjectKey: project?.project.key ?? null,
  });
  return { groups, search };
}

interface JumpInputProps {
  readonly listboxId: string;
  readonly hint: string;
  readonly query: string;
  readonly isExpanded: boolean;
  readonly activeId: string | undefined;
  readonly onQueryChange: (query: string) => void;
  readonly onKeyDown: (event: KeyboardEvent<HTMLInputElement>) => void;
}

/** combobox 的輸入框：前面是放大鏡，名稱和對話框的標題同一句（標題就是看得到的標籤）。 */
function JumpInput({ listboxId, hint, query, isExpanded, activeId, onQueryChange, onKeyDown }: JumpInputProps) {
  const hintId = useId();
  return (
    <>
      <TextField
        label={JUMP_TITLE}
        isLabelHidden
        leadingIcon="search"
        type="text"
        role="combobox"
        aria-expanded={isExpanded}
        aria-controls={listboxId}
        aria-autocomplete="list"
        aria-activedescendant={activeId}
        aria-describedby={hintId}
        placeholder={hint}
        autoComplete="off"
        spellCheck={false}
        enterKeyHint="go"
        value={query}
        onChange={(event) => {
          onQueryChange(event.target.value);
        }}
        onKeyDown={onKeyDown}
      />
      {/* placeholder 打了字就不見：範圍的說明另外用 aria-describedby 連著（hidden 的元素照樣算得進說明）。 */}
      <span id={hintId} hidden>
        {hint}
      </span>
    </>
  );
}

interface JumpDialogProps {
  readonly onClose: () => void;
  readonly onChoose: (action: JumpAction) => void;
}

/**
 * 快速跳轉（docs/DESIGN.md 8.3）：對話框裡的 combobox。輸入框加 listbox，焦點一直在輸入框；
 * ↑／↓ 選、Enter 前往、Esc 關上（Dialog），結果的數量與搜尋的進度用 status 說明。
 */
export function JumpDialog({ onClose, onChoose }: JumpDialogProps) {
  const [query, setQuery] = useState('');
  const { groups, search } = useJumpSources(query);
  const options = flattenGroups(groups);
  const combobox = useJumpCombobox(options, (option) => {
    onChoose(option.action);
  });
  const listboxId = useId();
  const status = useSettledStatus(statusText(options.length, search), search.isSettled);

  return (
    <Dialog title={JUMP_TITLE} size="lg" onClose={onClose}>
      <div className={styles.body}>
        <JumpInput
          listboxId={listboxId}
          hint={HINT}
          query={query}
          isExpanded={options.length > 0}
          activeId={combobox.activeIndex < 0 ? undefined : jumpOptionId(listboxId, combobox.activeIndex)}
          onQueryChange={(next) => {
            setQuery(next);
            combobox.reset();
          }}
          onKeyDown={combobox.handleKeyDown}
        />
        <JumpResults
          id={listboxId}
          groups={groups}
          activeIndex={combobox.activeIndex}
          onActivate={combobox.activate}
          onPick={(option) => {
            onChoose(option.action);
          }}
        />
        <Notice tone="danger">{search.failure}</Notice>
        <div className={styles.footer}>
          <p role="status" className={styles.status}>
            {status}
          </p>
          <Button size="sm" onClick={onClose}>
            關閉
          </Button>
        </div>
      </div>
    </Dialog>
  );
}
