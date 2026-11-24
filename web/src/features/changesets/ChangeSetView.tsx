import { useEffect, useId, useRef, type ReactNode, type RefObject } from 'react';
import { Link } from 'react-router';
import type { ChangeSet, ChangeSetDetail, ProjectDetail } from '../../api/types';
import { Button, buttonClassName } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { focusPageHeading } from '../../lib/pageTop';
import { ArchivedNotice } from '../projects/ArchivedNotice';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { changeSetsPath, projectSettingsPath } from '../work-items/itemLinks';
import { ChangeSetActions } from './ChangeSetActions';
import { decidedText, proposedText } from './changeSetPeople';
import { BlockerList, ChangeList, ChangeSetOriginBadge, ChangeSetOverview, ChangeSetStatusBadge, ImpactList } from './ChangeSetSections';
import { useChangeSetActions, type ActionProblem } from './useChangeSetActions';
import { useReviseChangeSet } from './useReviseChangeSet';
import styles from './ChangeSets.module.css';

type Revise = ReturnType<typeof useReviseChangeSet>;

const REVISE_HINT = '這是你剛從設定頁送出的修改。還想再改的話，「回去修改」會先放棄這份變更集，再帶著你的修改回到設定頁。';

/**
 * 「回去修改」放在內容裡的提示，不放頁列：頁列最多兩顆按鈕（套用、放棄，docs/DESIGN.md 6.1）；
 * 而且按下去會先放棄這份變更集，旁邊要先寫清楚。一開始就在、不必唸：role="none"。
 * 送出中用 aria-disabled 而不是 disabled：按鈕停得到，焦點留在它身上（disabled 會讓焦點掉到頁面本體）；重複按下由 revise 忽略。
 */
function ReviseNotice({ revise }: { readonly revise: Revise }) {
  const action = (
    <Button size="sm" aria-disabled={revise.isPending || undefined} onClick={revise.revise}>
      {revise.isPending ? '放棄中…' : '回去修改'}
    </Button>
  );
  return (
    <Notice tone="info" role="none" actions={action}>
      <p>{REVISE_HINT}</p>
    </Notice>
  );
}

function Section({ title, children }: { readonly title: string; readonly children: ReactNode }) {
  const headingId = useId();
  return (
    <section className={styles.section} aria-labelledby={headingId}>
      <h2 id={headingId} className={styles.sectionTitle}>
        {title}
      </h2>
      {children}
    </section>
  );
}

/** 提示裡的「到設定頁重新編輯」：放在提示的動作欄，長得像按鈕（點擊範圍和其他按鈕一樣大）。 */
function EditSettingsLink({ projectKey }: { readonly projectKey: string }) {
  return (
    <Link to={projectSettingsPath(projectKey)} className={buttonClassName('secondary', 'sm')}>
      到設定頁重新編輯
    </Link>
  );
}

/**
 * 套用或放棄被擋下時的說明。對話框關上、按鈕可能跟著不見，焦點移到這裡（提示本身，tabIndex={-1}），
 * 鍵盤使用者才知道發生什麼事、接著能按「知道了」。
 */
function ProblemNotice({ problem, projectKey, onDismiss }: { readonly problem: ActionProblem; readonly projectKey: string; readonly onDismiss: () => void }) {
  const noticeRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    noticeRef.current?.focus();
  }, [problem]);
  const edit = problem.suggestsEditing ? <EditSettingsLink projectKey={projectKey} /> : undefined;
  return (
    <Notice ref={noticeRef} tabIndex={-1} tone="danger" role="alert" actions={edit} onDismiss={onDismiss}>
      <p>{problem.message}</p>
      {problem.reasons.length > 0 && (
        <ul className={styles.dialogList}>
          {problem.reasons.map((reason, index) => (
            // 原因的順序由伺服器決定，不會重排。
            <li key={index}>{reason}</li>
          ))}
        </ul>
      )}
    </Notice>
  );
}

interface ChangeSetNoticesProps {
  readonly changeSet: ChangeSet;
  readonly projectKey: string;
  readonly actions: ReturnType<typeof useChangeSetActions>;
  readonly revise: Revise;
  readonly onDismissProblem: () => void;
  /** 封存的專案：放棄、回去修改都會被擋（409），入口不給。 */
  readonly isArchived: boolean;
}

/** 頁首下面的提示：一直掛著的狀態列（放棄的結果）、套用或放棄被擋下的說明、回去修改、過期的說明。 */
function ChangeSetNotices({ changeSet, projectKey, actions, revise, onDismissProblem, isArchived }: ChangeSetNoticesProps) {
  return (
    <>
      <Notice tone="success" role="status">
        {actions.notice}
      </Notice>
      {actions.problem !== null && <ProblemNotice problem={actions.problem} projectKey={projectKey} onDismiss={onDismissProblem} />}
      {revise.canRevise && !isArchived && <ReviseNotice revise={revise} />}
      {changeSet.status === 'expired' && actions.problem === null && (
        <Notice tone="warning" role="none" actions={<EditSettingsLink projectKey={projectKey} />}>
          別的變更已經先套用，這份已經過期，不能再套用。
        </Notice>
      )}
    </>
  );
}

/** 放棄之後按鈕都不見了：焦點移到頁面的標題、畫面捲回最上面，狀態列接著唸出結果（一直掛著的即時區域，內容是後來才填的）。 */
function useFocusAfterNotice(notice: string) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    if (notice !== '') focusPageHeading(headingRef.current);
  }, [notice]);
  return headingRef;
}

/** 專案的連結照這個專案上次用的檢視開（projectLink，和側欄、項目頁的麵包屑一樣）。 */
function changeSetCrumbs(project: ProjectDetail, number: number, projectLink: string) {
  const projectKey = project.project.key;
  return [
    { label: project.project.name, to: projectLink },
    { label: '設定', to: projectSettingsPath(projectKey) },
    { label: '變更集', to: changeSetsPath(projectKey) },
    { label: `#${String(number)}` },
  ];
}

interface ChangeSetHeadingProps {
  readonly changeSet: ChangeSet;
  readonly title: string;
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/** 內容區最上面：編號（h1，放棄之後焦點到這裡）、摘要、狀態、誰在何時提出與決定。 */
function ChangeSetHeading({ changeSet, title, headingRef }: ChangeSetHeadingProps) {
  const decided = decidedText(changeSet);
  return (
    <>
      <div className={styles.titleBlock}>
        <h1 ref={headingRef} tabIndex={-1} className={styles.title}>
          {title}
        </h1>
        <p className={styles.summary}>{changeSet.summary ?? '沒有摘要'}</p>
      </div>
      <p className={styles.meta}>
        <ChangeSetStatusBadge status={changeSet.status} />
        <span>{proposedText(changeSet)}</span>
        <ChangeSetOriginBadge origin={changeSet.origin} />
        {decided !== null && <span>{decided}</span>}
      </p>
    </>
  );
}

interface ChangeSetViewProps {
  readonly project: ProjectDetail;
  readonly detail: ChangeSetDetail;
  /** 背景重抓失敗的提示：放在內容最上面固定的位置，出現或消失都不會讓頁面重新掛載。 */
  readonly notice?: ReactNode;
}

/**
 * 變更集的內容（閱讀頁）：誰提出、狀態、一眼看得出的摘要、差異、對既有資料的影響、擋住的原因。
 * 變更集編號是內容區的大標題（docs/DESIGN.md 第 3 節），摘要接在下面；頁列只有位置與套用、放棄。
 * 差異的「改之前」是基準版本的藍圖（baseBlueprint）：已套用的變更集，專案目前的藍圖已經是改之後的樣子。
 */
export function ChangeSetView({ project, detail, notice }: ChangeSetViewProps) {
  const { changeSet } = detail;
  const projectKey = project.project.key;
  const names = { before: detail.baseBlueprint, after: detail.blueprint };
  const actions = useChangeSetActions(projectKey, changeSet.number);
  const revise = useReviseChangeSet(projectKey, changeSet, actions);
  const headingRef = useFocusAfterNotice(actions.notice);
  const dismissProblem = () => {
    focusPageHeading(headingRef.current);
    actions.clearProblem();
  };
  const title = `變更集 #${String(changeSet.number)}`;
  const projectLink = useProjectPageLink(projectKey);
  const bar = (
    <PageBar
      title={title}
      titleInBar={false}
      crumbs={changeSetCrumbs(project, changeSet.number, projectLink)}
      actions={<ChangeSetActions project={project} detail={detail} names={names} actions={actions} />}
    />
  );
  return (
    <PageLayout variant="reading" bar={bar}>
      <div className={styles.page}>
        {notice}
        <ArchivedNotice detail={project} />
        <ChangeSetHeading changeSet={changeSet} title={title} headingRef={headingRef} />
        <ChangeSetNotices changeSet={changeSet} projectKey={projectKey} actions={actions} revise={revise} isArchived={project.project.archivedAt !== null} onDismissProblem={dismissProblem} />
        <ChangeSetOverview detail={detail} />
        <Section title="設定的差異">
          <ChangeList detail={detail} names={names} />
        </Section>
        <Section title="對既有資料的影響">
          <ImpactList detail={detail} names={names} />
        </Section>
        {detail.blockers.length > 0 && (
          <Section title="擋住套用的原因">
            <BlockerList detail={detail} />
          </Section>
        )}
      </div>
    </PageLayout>
  );
}
