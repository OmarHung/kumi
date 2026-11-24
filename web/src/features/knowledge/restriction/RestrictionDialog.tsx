import { useRef, useState, type SubmitEvent } from 'react';
import { Link } from 'react-router';
import { errorMessage, isApiError } from '../../../api/http';
import type { KnowledgePageRestriction } from '../../../api/knowledgeTypes';
import { usePeople } from '../../../api/queries';
import type { PersonRef } from '../../../api/types';
import { useCurrentUser } from '../../../auth/CurrentUser';
import { Button } from '../../../components/Button/Button';
import { Dialog, DialogActions, DialogFormActions } from '../../../components/Dialog/Dialog';
import { FormAlert } from '../../../components/FormAlert/FormAlert';
import { Icon } from '../../../components/Icon/Icon';
import { ErrorState, LoadingState } from '../../../components/StatusViews/StatusViews';
import { updateThenFocus } from '../../../lib/updateThenFocus';
import { knowledgePageUrl } from '../knowledgeLinks';
import { RESTRICTED_ICON } from '../restrictedIcon';
import { RestrictionPeopleList } from './RestrictionPeopleList';
import { candidatesOf, isSameList, problemsOf, withSelfFirst } from './restrictionPeople';
import { useIsSavingRestriction, useProjectMemberIds, useRestrictionQuery, useSaveRestriction } from './useRestriction';
import styles from './RestrictionDialog.module.css';

const EXPLANATION = '只有名單上的人看得到這一頁與底下的頁面；組織管理員一律看得到。';

export interface RestrictionDialogProps {
  readonly spaceKey: string;
  readonly pageNumber: number;
  readonly pageTitle: string;
  /** 專案空間綁的專案代號：專案成員排在選人清單的前面。 */
  readonly projectKey: string | null;
  /** 能改名單；沒有時只看。 */
  readonly canEdit: boolean;
  readonly onClose: () => void;
}

function InheritedNote({ spaceKey, ancestors, onNavigate }: { readonly spaceKey: string; readonly ancestors: KnowledgePageRestriction['restrictedAncestors']; readonly onNavigate: () => void }) {
  if (ancestors.length === 0) return null;
  return (
    <p className={styles.note}>
      上層也有限制，要同時在每一份名單裡才看得到：
      {ancestors.map((ancestor, index) => (
        <span key={ancestor.id}>
          {index > 0 && '、'}
          <Link to={knowledgePageUrl(spaceKey, ancestor.number)} onClick={onNavigate}>{`「${ancestor.title}」`}</Link>
        </span>
      ))}
    </p>
  );
}

type RestrictionFormProps = RestrictionDialogProps & { readonly data: KnowledgePageRestriction };

function useRestrictionDraft({ data, spaceKey, pageNumber, onClose }: RestrictionFormProps) {
  const user = useCurrentUser();
  const self: PersonRef = { personId: user.personId, displayName: user.displayName };
  const [draft, setDraft] = useState<readonly PersonRef[] | null>(data.people);
  const [sent, setSent] = useState<readonly PersonRef[]>([]);
  const save = useSaveRestriction(spaceKey, pageNumber);
  const pickerRef = useRef<HTMLButtonElement>(null);
  const isChanged = draft !== null && (data.people === null || !isSameList(draft, data.people));

  // 改了名單就不再顯示上一次送出的錯誤：它指的是那一份名單。
  function change(next: readonly PersonRef[] | null) {
    save.reset();
    setDraft(next);
  }
  function submit(people: readonly PersonRef[] | null) {
    if (save.isPending) return;
    const list = people === null ? null : withSelfFirst(people, self);
    setSent(list ?? []);
    save.mutate(list === null ? null : list.map((person) => person.personId), { onSuccess: onClose });
  }
  return { self, draft, change, submit, isChanged, save, sent, pickerRef };
}

type DraftState = ReturnType<typeof useRestrictionDraft>;

/** 選人的來源與加減：人員清單（停用的不列）、專案成員排前面、加進名單與移除（移除後焦點回到選人的下拉）。 */
function usePeopleEditing(props: RestrictionFormProps, state: DraftState) {
  const { projectKey, canEdit } = props;
  const { draft, change, pickerRef } = state;
  const people = usePeople(canEdit && draft !== null);
  const memberIds = useProjectMemberIds(projectKey);
  const candidates = draft !== null && people.data !== undefined ? candidatesOf(people.data, memberIds, draft, projectKey === null) : null;
  return {
    candidates,
    loadError: people.isError ? errorMessage(people.error) : null,
    add: (personId: string) => {
      const person = people.data?.find((candidate) => candidate.personId === personId);
      if (draft === null || person === undefined) return;
      change([...draft, { personId, displayName: person.displayName }]);
    },
    remove: (personId: string) => {
      if (draft === null) return;
      updateThenFocus(() => { change(draft.filter((person) => person.personId !== personId)); }, [pickerRef]);
    },
  };
}

function FormActions({ canEdit, state, onClose }: { readonly canEdit: boolean; readonly state: DraftState; readonly onClose: () => void }) {
  const { draft, change, self, isChanged, save } = state;
  if (!canEdit) {
    return (
      <DialogActions>
        <Button onClick={onClose}>關閉</Button>
      </DialogActions>
    );
  }
  if (draft === null) {
    return (
      <DialogActions>
        <Button onClick={onClose}>關閉</Button>
        <Button variant="primary" onClick={() => { change([self]); }}>
          <Icon name={RESTRICTED_ICON} />
          限制這一頁
        </Button>
      </DialogActions>
    );
  }
  return <DialogFormActions submitLabel="儲存" pendingLabel="儲存中…" isPending={save.isPending} canSubmit={isChanged} onCancel={onClose} />;
}

function RestrictionForm(props: RestrictionFormProps) {
  const { data, spaceKey, canEdit, onClose } = props;
  const state = useRestrictionDraft(props);
  const { self, draft, submit, isChanged, save, sent, pickerRef } = state;
  const editing = usePeopleEditing(props, state);
  const problems = save.isError
    ? problemsOf(isApiError(save.error) ? save.error.details : [], errorMessage(save.error), sent)
    : { byPerson: new Map<string, string>(), general: null };
  // 自己在名單上時固定排最前面；管理員不在名單上就照名單（儲存時伺服器才把他補進去）。
  const shown = draft === null ? null : draft.some((person) => person.personId === self.personId) ? withSelfFirst(draft, self) : draft;

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (draft !== null && isChanged) submit(draft);
  }

  return (
    <form className={styles.form} noValidate aria-busy={save.isPending} onSubmit={handleSubmit}>
      <InheritedNote spaceKey={spaceKey} ancestors={data.restrictedAncestors} onNavigate={onClose} />
      {shown === null ? (
        <p className={styles.note}>{canEdit ? '這一頁沒有自己的名單，看得到空間的人都看得到（上層有限制的話照上層）。' : '這一頁沒有自己的名單。'}</p>
      ) : (
        <RestrictionPeopleList
          people={shown}
          selfId={self.personId}
          canEdit={canEdit}
          candidates={editing.candidates}
          loadError={editing.loadError}
          problems={problems.byPerson}
          pickerRef={pickerRef}
          onAdd={editing.add}
          onRemove={editing.remove}
          onRemoveAll={data.people === null ? null : () => { submit(null); }}
        />
      )}
      <FormAlert message={problems.general} />
      <FormActions canEdit={canEdit} state={state} onClose={onClose} />
    </form>
  );
}

/**
 * 「限制…」（internal/HANDOFF-knowledge-base.md 6.6）：這一頁自己的名單、上層繼承來的限制、拿掉限制。
 * 打開時讀最新的名單；沒有 canEdit 時只看名單。送出中不能關。
 */
export function RestrictionDialog(props: RestrictionDialogProps) {
  const { spaceKey, pageNumber, pageTitle, onClose } = props;
  const query = useRestrictionQuery(spaceKey, pageNumber);
  // 儲存中不能關（和搬移、新增空間的對話框一樣）：關掉了就看不到結果，失敗也無處說。
  const isSaving = useIsSavingRestriction(spaceKey, pageNumber);
  return (
    <Dialog title={`限制「${pageTitle}」`} description={EXPLANATION} onClose={isSaving ? () => undefined : onClose}>
      {query.isPending ? (
        <LoadingState label="正在載入名單…" />
      ) : query.isError ? (
        <>
          <ErrorState error={query.error} title="名單載入失敗" onRetry={() => { void query.refetch(); }} />
          <DialogActions>
            <Button onClick={onClose}>關閉</Button>
          </DialogActions>
        </>
      ) : (
        <RestrictionForm {...props} data={query.data} />
      )}
    </Dialog>
  );
}
