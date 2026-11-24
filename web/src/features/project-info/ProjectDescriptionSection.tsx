import { useId, useRef, useState, type RefObject } from 'react';
import { useProjectDescription, useDescriptionTemplates } from '../../api/projectDescription';
import type { DescriptionTemplate, ProjectDescription } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { IconButton } from '../../components/IconButton/IconButton';
import { SelectField } from '../../components/Field/Field';
import { Markdown } from '../../components/Markdown/Markdown';
import { ErrorState, LoadingState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { formatInstant } from '../../domain/time';
import { ProjectDescriptionForm } from './ProjectDescriptionEditor';
import { useProjectDescriptionEditor } from './useProjectDescriptionEditor';
import styles from './ProjectInfo.module.css';

/** 專案說明沒有成員的提及（不通知人），所以「@名稱」一律照原文。 */
const NO_MENTIONS: ReadonlyMap<string, string> = new Map();
const BLANK_FORMAT = '__blank__';

function UpdatedLine({ description }: { readonly description: ProjectDescription }) {
  if (description.updatedAt === null) return null;
  const when = formatInstant(description.updatedAt);
  const text = description.updatedBy === null ? `更新於 ${when}` : `最後由 ${description.updatedBy.displayName} 於 ${when} 更新`;
  return <p className={styles.updated}>{text}</p>;
}

function bodyOf(templates: readonly DescriptionTemplate[], formatId: string): string {
  return templates.find((template) => template.id === formatId)?.body ?? '';
}

interface StartWritingProps {
  readonly entryRef: RefObject<HTMLButtonElement | null>;
  readonly onStart: (initial: string) => void;
}

/** 讀不到說明格式：說一聲、給「重試」；下拉只剩「空白」，照樣能從空白開始寫。 */
function TemplatesFailed({ onRetry }: { readonly onRetry: () => void }) {
  return (
    <p className={styles.templatesFailed}>
      說明格式讀不到。
      <Button size="sm" variant="ghost" onClick={onRetry}>
        重試
      </Button>
    </p>
  );
}

/**
 * 能改的人看到的空白狀態：選說明格式（預設是 isDefault 那一份，另有「空白」）再「開始寫」；格式只是填進編輯器的起點。
 * 載入中下拉只有「載入中…」、不能開始寫（不先顯示「空白」再跳成預設的格式）；讀不到時只剩「空白」。
 */
function StartWriting({ entryRef, onStart }: StartWritingProps) {
  const templates = useDescriptionTemplates();
  const [choice, setChoice] = useState<string | null>(null);
  const list = templates.data ?? [];
  const defaultId = list.find((template) => template.isDefault)?.id ?? BLANK_FORMAT;
  const formatId = choice ?? defaultId;
  const blank = { value: BLANK_FORMAT, label: '空白', startsGroup: list.length > 0 };
  const options = templates.isPending ? [] : [...list.map((template) => ({ value: template.id, label: template.name })), blank];
  return (
    <>
      <div className={styles.start}>
        <SelectField
          className={styles.format}
          label="說明格式"
          value={templates.isPending ? '' : formatId}
          options={options}
          placeholder={templates.isPending ? '載入中…' : undefined}
          disabled={templates.isPending}
          onChange={setChoice}
        />
        <Button
          ref={entryRef}
          variant="primary"
          disabled={templates.isPending}
          onClick={() => {
            onStart(bodyOf(list, formatId));
          }}
        >
          開始寫
        </Button>
      </div>
      {templates.isError && (
        <TemplatesFailed
          onRetry={() => {
            void templates.refetch();
          }}
        />
      )}
    </>
  );
}

interface LoadedProps {
  readonly projectKey: string;
  readonly description: ProjectDescription;
  readonly canEdit: boolean;
}

function LoadedDescription({ projectKey, description, canEdit }: LoadedProps) {
  const headingId = useId();
  const entryRef = useRef<HTMLButtonElement>(null);
  const editor = useProjectDescriptionEditor(projectKey, description, entryRef);
  // 沒有 project.manage 的人（權限剛被拿掉）不還原：存著的草稿留在分頁裡，不顯示成能編輯。
  const isEditing = canEdit && editor.draft !== null;
  const text = description.text;
  const editButton = canEdit && !isEditing && text !== null && (
    <IconButton
      ref={entryRef}
      icon="edit"
      label="編輯專案說明"
      size="sm"
      variant="ghost"
      onClick={() => {
        editor.start(text);
      }}
    />
  );
  return (
    <section className={styles.section} aria-labelledby={headingId}>
      <div className={styles.header}>
        <h2 id={headingId} className={styles.heading}>
          專案說明
        </h2>
        {editButton}
      </div>
      {isEditing && editor.draft !== null ? (
        <ProjectDescriptionForm editor={editor} draft={editor.draft} />
      ) : text !== null ? (
        <>
          <Markdown text={text} mentionNames={NO_MENTIONS} hasAttachments={false} />
          <UpdatedLine description={description} />
        </>
      ) : (
        <div className={styles.empty}>
          <p className={styles.emptyText}>還沒有專案說明。</p>
          {canEdit && <StartWriting entryRef={entryRef} onStart={editor.start} />}
        </div>
      )}
    </section>
  );
}

/** 專案說明一節：載入、失敗、看、編輯、空白時套用格式。換了專案就重新掛上（草稿與編輯狀態跟著專案）。 */
export function ProjectDescriptionSection({ projectKey, canEdit }: { readonly projectKey: string; readonly canEdit: boolean }) {
  const query = useProjectDescription(projectKey);
  if (query.isPending) return <LoadingState label="正在載入專案說明…" />;
  if (query.isLoadingError) {
    return (
      <ErrorState
        error={query.error}
        title="專案說明載入失敗"
        onRetry={() => {
          void query.refetch();
        }}
      />
    );
  }
  return (
    <>
      <RefetchErrorNotice query={query} message="無法更新專案說明，畫面上可能是舊的。" />
      <LoadedDescription key={projectKey} projectKey={projectKey} description={query.data} canEdit={canEdit} />
    </>
  );
}
