import { useEffect, useId, useRef, type ReactNode, type Ref } from 'react';
import { Link } from 'react-router';
import type { ProjectDetail } from '../../api/types';
import { buttonClassName } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { validateDraft } from '../../domain/settingsValidation';
import { useArrivalNotice } from '../../lib/useArrivalNotice';
import { ArchivedNotice } from '../projects/ArchivedNotice';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { changeSetsPath, settingsPlaceOf, type SettingsSection } from '../work-items/itemLinks';
import { FieldsSection } from './FieldsSection';
import { ItemTypesSection } from './ItemTypesSection';
import { RolesSection } from './RolesSection';
import { SettingsEditorProvider, useOpenDialogs, type SettingsEditorValue } from './SettingsContext';
import { AppliedNotice, OutdatedNotice, PendingNotice } from './SettingsNotices';
import { ElsewhereIssues, SettingsTabs, type SectionTallies } from './SettingsSectionNav';
import { SECTION_LABELS, sectionChangeCounts, sectionHeadingId, sectionIssueCounts } from './settingsSections';
import { StatesSection } from './StatesSection';
import { SubmitBar } from './SubmitBar';
import { useSettingsForm } from './useSettingsForm';
import styles from './Settings.module.css';

const READ_ONLY = '你可以看這個專案的設定，但你的角色不能修改設定。需要的話，請專案的管理員調整你的角色。';

interface SettingsBarProps {
  readonly detail: ProjectDetail;
  readonly section: SettingsSection;
  readonly headingRef: Ref<HTMLHeadingElement>;
}

/**
 * 頁列：專案 / 設定（h1），右邊「所有變更集」。專案的連結照這個專案上次用的檢視開。
 * 頁面的標題（h1）是「設定」；分頁標題帶上區塊（documentTitle），瀏覽紀錄與好幾個分頁才分得出是哪一區（docs/DESIGN.md 8.3）。
 */
function SettingsBar({ detail, section, headingRef }: SettingsBarProps) {
  const actions = (
    <Link to={changeSetsPath(detail.project.key)} className={buttonClassName('secondary')}>
      所有變更集
    </Link>
  );
  const crumbs = [{ label: detail.project.name, to: useProjectPageLink(detail.project.key) }];
  return (
    <PageBar
      headingRef={headingRef}
      title="設定"
      documentTitle={`設定 · ${SECTION_LABELS[section]}`}
      crumbs={crumbs}
      actions={actions}
    />
  );
}

/** 閱讀頁的說明放在標題下面、跟著內容捲走（docs/DESIGN.md 6.2），不放頁列。 */
function SettingsIntro({ baseVersion }: { readonly baseVersion: number }) {
  return (
    <p className={styles.intro}>{`藍圖第 ${String(baseVersion)} 版。修改會先送出成一份變更集，預覽差異與影響之後再套用。`}</p>
  );
}

function SectionContent({ section }: { readonly section: SettingsSection }) {
  switch (section) {
    case 'workflows':
      return <StatesSection />;
    case 'types':
      return <ItemTypesSection />;
    case 'fields':
      return <FieldsSection />;
    case 'roles':
      return <RolesSection />;
  }
}

type SettingsForm = ReturnType<typeof useSettingsForm>;

interface EditorSubmitBarProps {
  readonly form: SettingsForm;
  readonly issues: SettingsEditorValue['issues'];
  readonly elsewhere: ReactNode;
}

/** 送出列；唯讀時沒有。對不到任何一列的問題列在送出列的「其他問題」，別的區塊的問題連過去。 */
function EditorSubmitBar({ form, issues, elsewhere }: EditorSubmitBarProps) {
  const { editing, proposal } = form;
  if (!form.canEdit) return null;
  return (
    <SubmitBar
      count={editing.count}
      summary={form.summary}
      onSummaryChange={form.setSummary}
      onSubmit={() => { proposal.submit(form.summary); }}
      onDiscard={form.discard}
      isPending={proposal.isPending}
      alert={proposal.alert}
      summaryError={proposal.summaryProblem}
      unplaced={issues.filter((issue) => issue.target === null)}
      elsewhere={elsewhere}
    />
  );
}

interface EditorNoticesProps {
  readonly form: SettingsForm;
  readonly detail: ProjectDetail;
  readonly arrival: string;
  readonly outdatedRef: Ref<HTMLDivElement>;
}

/** 說明與內容之間的提示：套用後導回的結果、等待核准的變更集、唯讀、別人先改了設定。 */
function EditorNotices({ form, detail, arrival, outdatedRef }: EditorNoticesProps) {
  const { editing } = form;
  return (
    <>
      <AppliedNotice message={arrival} />
      <PendingNotice projectKey={detail.project.key} />
      {!form.canEdit && detail.project.archivedAt === null && <Notice tone="info" role="none">{READ_ONLY}</Notice>}
      {form.isOutdated && (
        <OutdatedNotice
          ref={outdatedRef}
          baseVersion={editing.session.baseVersion}
          latestVersion={editing.latestVersion}
          error={form.restartError}
          onRestart={() => { void form.restart(); }}
        />
      )}
    </>
  );
}

/**
 * 從送出列連到別的區塊、或確認關掉對話框換區塊之後：焦點放到新區塊的標題（按的連結、對話框都跟著不見，
 * 焦點不能掉到頁面本體）。用 ref 記住要求，區塊真的換了（畫面更新完）才放；區塊一換就清掉，
 * 沒用掉的要求（例如按著 ⌘ 點、開在新分頁）不會留到之後。
 */
function useSectionFocus(idPrefix: string, section: SettingsSection) {
  const requested = useRef<SettingsSection | null>(null);
  useEffect(() => {
    const target = requested.current;
    requested.current = null;
    if (target === section) document.getElementById(sectionHeadingId(idPrefix, section))?.focus();
  }, [idPrefix, section]);
  return (next: SettingsSection | null) => {
    requested.current = next;
  };
}

interface SettingsEditorProps {
  readonly detail: ProjectDetail;
  /** 網址上的區塊：只畫這一個，草稿是整頁共用的。 */
  readonly section: SettingsSection;
  readonly notice?: ReactNode;
}

/**
 * 設定頁的內容（閱讀頁）：區塊的分頁（流程、類型、欄位、角色）與目前那一個區塊。依目前的藍圖建一份草稿，
 * 各區塊改完一次送出成變更集，成功後到那份變更集的預覽頁；沒有 blueprint.propose 時整頁唯讀。
 */
export function SettingsEditor({ detail, section, notice }: SettingsEditorProps) {
  const idPrefix = useId();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const outdatedRef = useRef<HTMLDivElement>(null);
  const requestFocus = useSectionFocus(idPrefix, section);
  const { hasOpenDialog, trackDialog } = useOpenDialogs();
  const onSwitchSection = (pathname: string) => { requestFocus(settingsPlaceOf(pathname)?.section ?? null); };
  const form = useSettingsForm(detail, section, { headingRef, outdatedRef }, { hasOpenDialog, onSwitchSection });
  const arrival = useArrivalNotice(headingRef);
  const { base, draft } = form.editing.session;
  const local = form.proposal.showErrors ? validateDraft(base, draft) : [];
  const issues = [...local, ...form.proposal.serverIssues];
  const value: SettingsEditorValue = { base, draft, canEdit: form.canEdit, issues, update: form.editing.update, idPrefix, members: detail.members, trackDialog };
  const tallies: SectionTallies = { changes: sectionChangeCounts(base, draft), issues: sectionIssueCounts(issues) };
  const elsewhere = <ElsewhereIssues section={section} issues={tallies.issues} onGo={requestFocus} />;
  return (
    <SettingsEditorProvider value={value}>
      <PageLayout
        variant="reading"
        bar={<SettingsBar detail={detail} section={section} headingRef={headingRef} />}
        toolbar={<SettingsTabs section={section} tallies={tallies} />}
      >
        <div className={styles.page}>
          <SettingsIntro baseVersion={form.editing.session.baseVersion} />
          {notice}
          <ArchivedNotice detail={detail} />
          <EditorNotices form={form} detail={detail} arrival={arrival} outdatedRef={outdatedRef} />
          <SectionContent section={section} />
          <EditorSubmitBar form={form} issues={issues} elsewhere={elsewhere} />
        </div>
      </PageLayout>
      {form.leaveDialog}
    </SettingsEditorProvider>
  );
}
