import { useAllConversations } from '../../api/assistant';
import { useProjects } from '../../api/queries';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { IconButton, IconLink } from '../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { Select, type SelectOption } from '../../components/Select/Select';
import { NO_PROJECT_LABEL } from '../ai/aiLabels';
import { ItemPicker } from '../assistant/ItemPicker';
import { assistantPagePath } from '../work-items/itemLinks';
import { groupConversations } from './conversationGroups';
import type { PanelScope } from './panelMemory';
import styles from './AssistantPanel.module.css';

const NEW_CONVERSATION = '新的對話';
/** 還沒有標題的對話（標題要等第一輪回覆之後才有）：不能也叫「新的對話」，不然和上面開新對話的那一項分不出來。 */
const UNTITLED_CONVERSATION = '（還沒有標題）';
const BUSY_REASON = '回覆還在進行，等它結束或先按「停止」。';

/** 選單裡「不選專案」那一項的值：專案代號只有英數字，不會撞到。 */
const NO_PROJECT_VALUE = '@none';

interface ProjectPickerProps {
  /** null 是「不選專案」；undefined 是還沒選。 */
  readonly projectKey: PanelScope;
  readonly isBusy: boolean;
  readonly onChoose: (projectKey: string | null) => void;
}

/**
 * 自己畫的選單，列出看得到、沒有封存的專案；一律可以選（2026-11-27 使用者要求）。在專案頁預設是那一頁的專案，
 * 換成別的專案或不選專案之後，面板留在選的那個，換到另一個專案的頁面時才再跟上（useAssistantProject）。回覆中不能換。
 */
function ProjectPicker({ projectKey, isBusy, onChoose }: ProjectPickerProps) {
  const projects = useProjects();
  const listed = (projects.data ?? []).filter((project) => project.archivedAt === null || project.key === projectKey);
  // 第一項「不選專案」：問模板、規劃，請助理開新專案（組織的對話）。
  const options: readonly SelectOption[] = [
    { value: NO_PROJECT_VALUE, label: NO_PROJECT_LABEL },
    ...listed.map((project, index) => ({
      value: project.key,
      label: `${project.name}（${project.key}）`,
      keywords: [project.key],
      ...(index === 0 ? { startsGroup: true } : {}),
    })),
  ];
  return (
    <Select
      aria-label="專案"
      size="sm"
      width="content"
      value={projectKey === null ? NO_PROJECT_VALUE : (projectKey ?? '')}
      options={options}
      placeholder="選一個專案"
      disabled={isBusy}
      onChange={(value) => {
        onChoose(value === NO_PROJECT_VALUE ? null : value);
      }}
    />
  );
}

interface ConversationMenuProps {
  readonly projectKey: string | null;
  readonly conversationId: string | undefined;
  readonly isBusy: boolean;
  readonly onSelect: (id: string | undefined) => void;
  readonly onOpen: (projectKey: string | null, id: string) => void;
}

/**
 * 「新的對話」（目前的專案）加上自己所有的對話，依專案分組（組名是專案名稱，不選專案的叫「不選專案」）。
 * 選了別的專案的對話，面板連專案一起換。回覆中不能換（換了回覆就被中斷）。
 */
function ConversationMenu({ projectKey, conversationId, isBusy, onSelect, onOpen }: ConversationMenuProps) {
  const conversations = useAllConversations();
  const projects = useProjects();
  const nameOf = (key: string | null) =>
    key === null ? NO_PROJECT_LABEL : (projects.data?.find((project) => project.key === key)?.name ?? key);
  const blocked = isBusy ? { disabledReason: BUSY_REASON } : {};
  const items: readonly MenuItem[] = [
    { key: 'new', label: NEW_CONVERSATION, icon: 'add', isChecked: conversationId === undefined, onSelect: () => { onSelect(undefined); }, ...blocked },
    ...groupConversations(conversations.data ?? [], projectKey).flatMap((group) =>
      group.conversations.map(
        (conversation, index): MenuItem => ({
          key: conversation.id,
          label: `${conversation.title ?? UNTITLED_CONVERSATION}${conversation.isRunning ? '（回覆中）' : ''}`,
          isChecked: conversation.id === conversationId,
          ...(index === 0 ? { groupLabel: nameOf(group.projectKey) } : {}),
          onSelect: () => {
            if (group.projectKey === projectKey) onSelect(conversation.id);
            else onOpen(group.projectKey, conversation.id);
          },
          ...blocked,
        }),
      ),
    ),
  ];
  return (
    <Menu
      menuLabel="對話"
      align="end"
      items={items}
      renderTrigger={(trigger) => (
        <Button {...trigger} size="sm">
          <Icon name="menu" />
          對話
        </Button>
      )}
    />
  );
}

const OPEN_PAGE_REASON_ID = 'assistant-panel-open-page-reason';

/**
 * 「開成整頁」：回覆中不能開——整頁版掛上時會重抓這則對話，伺服器那份還沒有正在長的訊息，會蓋掉面板串流寫進快取的內容，
 * 整頁版的「停止」也停不了面板這一條。回覆中標 aria-disabled 並說明原因（和對話選單同一套）。
 */
function OpenPageLink({ projectKey, conversationId, itemKey, isBusy }: { readonly projectKey: string | null; readonly conversationId: string | undefined; readonly itemKey: string | null; readonly isBusy: boolean }) {
  if (!isBusy) return <IconLink to={assistantPagePath(projectKey, conversationId, itemKey)} icon="expand" label="開成整頁" />;
  return (
    <>
      <IconButton icon="expand" label="開成整頁" description={BUSY_REASON} aria-disabled aria-describedby={OPEN_PAGE_REASON_ID} onClick={() => undefined} />
      <span id={OPEN_PAGE_REASON_ID} className="visually-hidden">
        {BUSY_REASON}
      </span>
    </>
  );
}

export interface PanelHeaderProps extends ProjectPickerProps {
  readonly conversationId: string | undefined;
  readonly itemKey: string | null;
  readonly onChooseItem: (itemKey: string | null) => void;
  readonly onSelectConversation: (id: string | undefined) => void;
  readonly onOpenConversation: (projectKey: string | null, id: string) => void;
  readonly onClose: () => void;
}

/** 頁首：標題與「開成整頁」「關閉」；下面一列是專案與對話選單。 */
export function PanelHeader({ projectKey, isBusy, onChoose, conversationId, itemKey, onChooseItem, onSelectConversation, onOpenConversation, onClose }: PanelHeaderProps) {
  return (
    <header className={styles.header}>
      <div className={styles.titleRow}>
        <h2 className={styles.title} id="assistant-panel-title" tabIndex={-1}>
          <Icon name="sparkles" />
          AI 助理
        </h2>
        {projectKey !== undefined && (
          <OpenPageLink projectKey={projectKey} conversationId={conversationId} itemKey={itemKey} isBusy={isBusy} />
        )}
        <IconButton icon="close" label="關閉" onClick={onClose} />
      </div>
      <div className={styles.contextRow}>
        <ProjectPicker projectKey={projectKey} isBusy={isBusy} onChoose={onChoose} />
        {projectKey !== undefined && (
          <ConversationMenu projectKey={projectKey} conversationId={conversationId} isBusy={isBusy} onSelect={onSelectConversation} onOpen={onOpenConversation} />
        )}
        {typeof projectKey === 'string' && <ItemPicker projectKey={projectKey} itemKey={itemKey} onChange={onChooseItem} />}
      </div>
    </header>
  );
}
