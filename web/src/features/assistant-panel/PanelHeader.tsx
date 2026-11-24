import { useConversations } from '../../api/assistant';
import { useProjects } from '../../api/queries';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { IconButton, IconLink } from '../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { Select, type SelectOption } from '../../components/Select/Select';
import { ItemPicker } from '../assistant/ItemPicker';
import { projectAssistantPath } from '../work-items/itemLinks';
import styles from './AssistantPanel.module.css';

const NEW_CONVERSATION = '新的對話';
/** 還沒有標題的對話（標題要等第一輪回覆之後才有）：不能也叫「新的對話」，不然和上面開新對話的那一項分不出來。 */
const UNTITLED_CONVERSATION = '（還沒有標題）';
const MAX_LISTED_CONVERSATIONS = 30;
const BUSY_REASON = '回覆還在進行，等它結束或先按「停止」。';

interface ProjectPickerProps {
  readonly projectKey: string | null;
  /** 在專案頁：面板跟著路由，不能自己選。 */
  readonly isFollowingRoute: boolean;
  readonly isBusy: boolean;
  readonly onChoose: (projectKey: string) => void;
}

/** 在專案頁是專案的名稱；不在專案頁是自己畫的選單，列出看得到、沒有封存的專案。 */
function ProjectPicker({ projectKey, isFollowingRoute, isBusy, onChoose }: ProjectPickerProps) {
  const projects = useProjects();
  const listed = (projects.data ?? []).filter((project) => project.archivedAt === null || project.key === projectKey);
  const current = listed.find((project) => project.key === projectKey);
  if (isFollowingRoute) {
    return (
      <span className={styles.projectName} aria-label="目前的專案">
        {current?.name ?? projectKey}
      </span>
    );
  }
  const options: readonly SelectOption[] = listed.map((project) => ({
    value: project.key,
    label: `${project.name}（${project.key}）`,
    keywords: [project.key],
  }));
  return (
    <Select
      aria-label="專案"
      size="sm"
      width="content"
      value={projectKey ?? ''}
      options={options}
      placeholder="選一個專案"
      disabled={isBusy}
      onChange={onChoose}
    />
  );
}

interface ConversationMenuProps {
  readonly projectKey: string;
  readonly conversationId: string | undefined;
  readonly isBusy: boolean;
  readonly onSelect: (id: string | undefined) => void;
}

/** 這個專案的對話：「新的對話」加最近的幾則。回覆中不能換（換了回覆就被中斷）。 */
function ConversationMenu({ projectKey, conversationId, isBusy, onSelect }: ConversationMenuProps) {
  const conversations = useConversations(projectKey);
  const blocked = isBusy ? { disabledReason: BUSY_REASON } : {};
  const items: readonly MenuItem[] = [
    { key: 'new', label: NEW_CONVERSATION, icon: 'add', isChecked: conversationId === undefined, onSelect: () => { onSelect(undefined); }, ...blocked },
    ...(conversations.data ?? []).slice(0, MAX_LISTED_CONVERSATIONS).map(
      (conversation, index): MenuItem => ({
        key: conversation.id,
        label: `${conversation.title ?? UNTITLED_CONVERSATION}${conversation.isRunning ? '（回覆中）' : ''}`,
        isChecked: conversation.id === conversationId,
        startsGroup: index === 0,
        onSelect: () => {
          onSelect(conversation.id);
        },
        ...blocked,
      }),
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
function OpenPageLink({ projectKey, conversationId, itemKey, isBusy }: { readonly projectKey: string; readonly conversationId: string | undefined; readonly itemKey: string | null; readonly isBusy: boolean }) {
  if (!isBusy) return <IconLink to={projectAssistantPath(projectKey, conversationId, itemKey)} icon="expand" label="開成整頁" />;
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
  readonly onClose: () => void;
}

/** 頁首：標題與「開成整頁」「關閉」；下面一列是專案與對話選單。 */
export function PanelHeader({ projectKey, isFollowingRoute, isBusy, onChoose, conversationId, itemKey, onChooseItem, onSelectConversation, onClose }: PanelHeaderProps) {
  return (
    <header className={styles.header}>
      <div className={styles.titleRow}>
        <h2 className={styles.title} id="assistant-panel-title" tabIndex={-1}>
          <Icon name="sparkles" />
          AI 助理
        </h2>
        {projectKey !== null && (
          <OpenPageLink projectKey={projectKey} conversationId={conversationId} itemKey={itemKey} isBusy={isBusy} />
        )}
        <IconButton icon="close" label="關閉" onClick={onClose} />
      </div>
      <div className={styles.contextRow}>
        <ProjectPicker projectKey={projectKey} isFollowingRoute={isFollowingRoute} isBusy={isBusy} onChoose={onChoose} />
        {projectKey !== null && (
          <ConversationMenu projectKey={projectKey} conversationId={conversationId} isBusy={isBusy} onSelect={onSelectConversation} />
        )}
        {projectKey !== null && <ItemPicker projectKey={projectKey} itemKey={itemKey} onChange={onChooseItem} />}
      </div>
    </header>
  );
}
