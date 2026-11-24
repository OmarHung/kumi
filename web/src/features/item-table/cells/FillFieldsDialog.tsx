import { Fragment, useId, type ReactNode } from 'react';
import type { Item } from '../../../api/types';
import { Dialog, DialogFormActions } from '../../../components/Dialog/Dialog';
import { Notice } from '../../../components/Notice/Notice';
import { stateLabel } from '../../../domain/blueprint';
import { preventComposingSubmit } from '../../../lib/keyboard';
import { useProjectTable } from '../ProjectTableContext';
import { missingFieldPaths } from './fillPlan';
import { TransitionProblem } from './TransitionProblem';
import { useFillSubmit, type FillProblem, type FillRequest, type SendTransition } from './useFillSubmit';
import styles from './FillFieldsDialog.module.css';

interface ProblemNoticeProps {
  readonly problem: FillProblem | null;
  readonly target: string;
  readonly hasSaved: boolean;
  readonly item: Item;
  readonly to: string;
  readonly onOpenMembers: () => void;
}

function problemTitle(problem: FillProblem | null, target: string, hasSaved: boolean): string | undefined {
  if (problem === null) return undefined;
  if (problem.kind === 'patch') return `欄位沒有存好，還沒有移到「${target}」。`;
  return hasSaved ? `欄位已經存好，但還沒有移到「${target}」。` : `還沒有移到「${target}」。`;
}

/**
 * 對話框上方的說明（role="alert"）：一直掛著、沒有問題時是空的，內容填進來時報讀軟體才唸得到。
 * 欄位存好了、改狀態又被別的原因擋下時，原因和表格同一套（TransitionProblem），並說填的值已經存好、不用重填。
 * 又是缺欄位時不說「只會重送改狀態」：清單換成新缺的欄位，那些要先填、會先送 PATCH。
 */
function ProblemNotice({ problem, target, hasSaved, item, to, onOpenMembers }: ProblemNoticeProps) {
  let body: ReactNode = null;
  if (problem?.kind === 'patch') {
    body = problem.message;
  } else if (problem?.kind === 'blocked') {
    body = (
      <div className={styles.problemBody}>
        <TransitionProblem error={problem.error} item={item} to={to} onOpenMembers={onOpenMembers} />
        {hasSaved && missingFieldPaths(problem.error) === null && (
          <p>{`不用重填，再按「填好並移到『${target}』」只會重送改狀態。`}</p>
        )}
      </div>
    );
  }
  return (
    <Notice tone="danger" role="alert" title={problemTitle(problem, target, hasSaved)}>
      {body}
    </Notice>
  );
}

type FillSubmit = ReturnType<typeof useFillSubmit>;

/**
 * 缺的欄位，照後端回的順序；每一個是表格的那個編輯器（fillFields.tsx）。
 * 又因缺欄位被擋下時（round 換了）欄位重新掛上，草稿回到清空的基準。
 */
function FillFieldList({ fill, idOf }: { readonly fill: FillSubmit; readonly idOf: (key: string) => string }) {
  return fill.fields.map((field) => (
    <Fragment key={`${field.key}:${String(fill.round)}`}>
      {field.render({
        id: idOf(field.key),
        error: fill.errors[field.key],
        isSaving: fill.isSending,
        onCommand: (next) => {
          fill.setCommand(field.key, next);
        },
        onSubmit: fill.submit,
      })}
    </Fragment>
  ));
}

/** 為什麼關上：沒有值是取消（按鈕、Esc）；members 是按了說明裡的「到『成員』調整角色」，接著要開成員。 */
export type FillCloseReason = 'members';

interface FillFieldsDialogProps {
  readonly request: FillRequest;
  /** 取消、Esc（送出中不關）。焦點回到打開它的元素：表格與項目頁由對話框自己放回狀態格，看板由呼叫端放到卡片。 */
  readonly onClose: (reason?: FillCloseReason) => void;
  /** 欄位存好、也移過去了：filled 是填好的欄位名稱，給呼叫端唸結果。 */
  readonly onMoved: (moved: Item, filled: readonly string[]) => void;
  /** 送改狀態的方式；沒給時用 useItemCommand（表格與項目頁）。 */
  readonly transition?: SendTransition;
}

/**
 * 補填再前進（docs/DESIGN.md 8.3）：改狀態被「要先填欄位」擋下時，原地開這個對話框，只列後端回應裡缺的欄位，
 * 用表格的編輯器填；「填好並移到…」先 PATCH 欄位，成功才送改狀態。表格、項目頁（含面板）、看板都用它。
 */
export function FillFieldsDialog({ request, onClose, onMoved, transition }: FillFieldsDialogProps) {
  const { detail, onOpenMembers } = useProjectTable();
  const baseId = useId();
  const idOf = (key: string) => `${baseId}-${key}`;
  const fill = useFillSubmit({ request, transition, onMoved, idOf });
  const { item, to } = request;
  const target = stateLabel(detail.blueprint, item.type, to, item.stateCategory);
  const close = () => {
    if (!fill.isSending) onClose();
  };
  return (
    <Dialog
      title={`移到「${target}」之前要先填`}
      description={`填好之後接著把 ${item.key}「${item.title}」移到「${target}」。`}
      onClose={close}
    >
      <form
        className={styles.form}
        noValidate
        onKeyDown={preventComposingSubmit}
        onSubmit={(event) => {
          event.preventDefault();
          fill.submit();
        }}
      >
        <ProblemNotice
          problem={fill.problem}
          target={target}
          hasSaved={fill.hasSaved}
          item={fill.latest}
          to={to}
          // 不在對話框上再開對話框（6.2）：先關上這一個，再開成員。
          onOpenMembers={() => {
            onClose('members');
            onOpenMembers();
          }}
        />
        <FillFieldList fill={fill} idOf={idOf} />
        {/* 送出中主要按鈕不停用（焦點在它身上），標 aria-disabled，再按也不會重送（useFillSubmit 自己擋）。 */}
        <DialogFormActions
          submitLabel={`填好並移到「${target}」`}
          pendingLabel="送出中…"
          isPending={fill.isSending}
          keepsFocusWhilePending
          onCancel={close}
        />
      </form>
    </Dialog>
  );
}
