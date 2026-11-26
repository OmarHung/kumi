import { useId, useRef, useState, type RefObject, type SubmitEvent } from 'react';
import { errorMessage } from '../../api/http';
import { usePeople } from '../../api/queries';
import type { Person, ProjectDetail } from '../../api/types';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { Avatar } from '../../components/Avatar/Avatar';
import { Button } from '../../components/Button/Button';
import { SelectField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Notice } from '../../components/Notice/Notice';
import type { SelectOption } from '../../components/Select/Select';
import { mapApiErrorToFields, type FieldErrors, type FieldMapping } from '../../lib/formErrors';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { InlineNewPerson } from './InlineNewPerson';
import { roleDescription, roleOptions, useSetMemberRole, type MemberFeedback, type RoleOption } from './memberRoles';
import styles from './MembersDialog.module.css';

type AddMemberField = 'person' | 'role';

const ERROR_MAPPING: FieldMapping<AddMemberField> = {
  fields: ['person', 'role'],
  codes: { 'person.not_found': 'person', 'project.member_unknown_role': 'role' },
};

/** 新成員預設是編輯者：能新增、修改、改狀態，是最常見的需求。 */
const DEFAULT_ROLE = 'editor';

const NO_ONE_LEFT = '組織裡的人都已經是成員了。';

function useAddMember(detail: ProjectDetail, onSaved: MemberFeedback['onSaved']) {
  const mutation = useSetMemberRole(detail.project.key);
  const [errors, setErrors] = useState<FieldErrors<AddMemberField>>({});
  const [formError, setFormError] = useState<string | null>(null);

  function add(person: Person | undefined, role: RoleOption, onAdded: () => void) {
    setFormError(null);
    if (person === undefined) {
      setErrors({ person: '請選擇要加入的人。' });
      return;
    }
    setErrors({});
    mutation.mutate(
      { personId: person.personId, role: role.value },
      {
        onSuccess: () => {
          onAdded();
          onSaved(`已加入${person.displayName}（${role.label}）。`);
        },
        onError: (error) => {
          const mapped = mapApiErrorToFields(error, ERROR_MAPPING);
          setErrors(mapped.fieldErrors);
          setFormError(mapped.formError);
        },
      },
    );
  }

  return { add, errors, setErrors, formError, isPending: mutation.isPending };
}

function personLabel(person: Person): string {
  return person.email === null ? person.displayName : `${person.displayName}（${person.email}）`;
}

/**
 * 人員選單下面的說明（docs/DESIGN.md 8.1 第 3 點：要的人不在清單裡也有下一步）。組織的管理員有「新增人員…」，
 * 不必多說；其他人說要找誰。人員頁不一定看得到（訪客），而且看得到也不能新增，所以只給文字、不給連結。
 */
function personHint(noOneLeft: boolean, canCreatePeople: boolean): string | undefined {
  if (canCreatePeople) return noOneLeft ? NO_ONE_LEFT : undefined;
  return noOneLeft
    ? `${NO_ONE_LEFT}要加別人，請組織的管理員先到「人員」新增。`
    : '要加的人不在清單裡？請組織的管理員先到「人員」新增。';
}

/** 「新增人員…」就地展開的表單開著沒有。建好之後選起來、焦點回到人員選單；取消時回到「新增人員…」。 */
function useNewPersonToggle(personSelectRef: RefObject<HTMLButtonElement | null>, onCreated: (person: Person) => void) {
  const [isOpen, setIsOpen] = useState(false);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const formId = useId();
  return {
    isOpen,
    formId,
    toggleRef,
    toggle: () => {
      setIsOpen((open) => !open);
    },
    cancel: () => {
      updateThenFocus(() => {
        setIsOpen(false);
      }, [toggleRef]);
    },
    created: (person: Person) => {
      updateThenFocus(() => {
        setIsOpen(false);
        onCreated(person);
      }, [personSelectRef]);
    },
  };
}

type NewPersonToggle = ReturnType<typeof useNewPersonToggle>;

/** 人員選單下面的「新增人員…」：就地展開的表單開著時 aria-expanded，並用 aria-controls 指過去。 */
function NewPersonButton({ toggle }: { readonly toggle: NewPersonToggle }) {
  const { toggleRef, isOpen, formId } = toggle;
  return (
    <Button ref={toggleRef} size="sm" variant="ghost" className={styles.newPersonToggle} aria-expanded={isOpen} aria-controls={isOpen ? formId : undefined} onClick={toggle.toggle}>
      新增人員…
    </Button>
  );
}

interface PersonSelectProps {
  readonly selectRef: RefObject<HTMLButtonElement | null>;
  readonly value: string;
  readonly candidates: readonly Person[];
  readonly isLoading: boolean;
  readonly hint: string | undefined;
  readonly error: string | undefined;
  readonly onChange: (personId: string) => void;
}

/**
 * 人員的選項：前面是頭像（裝飾，Select 包成 aria-hidden），每一個都有，清單裡的字才對齊。
 * 人多時清單有篩選框（規範 7.3）：Email、職稱與負責範圍放進 keywords，用它們也找得到；職稱寫在名稱下面。
 */
function personOptions(candidates: readonly Person[]): SelectOption[] {
  return candidates.map((person) => ({
    value: person.personId,
    label: personLabel(person),
    leading: <Avatar name={person.displayName} size="sm" decorative />,
    description: person.jobTitle ?? undefined,
    keywords: [person.email, person.jobTitle, person.responsibilities].filter((word): word is string => word !== null),
  }));
}

/**
 * 還沒選人時按鈕上是灰字的「選擇人員」（必選，規範 7.3 的 placeholder）。讀人員中照模板的選單（TemplateField）：
 * 停用、灰字「載入中…」，打開也沒有東西可以選。
 */
function PersonSelect({ selectRef, value, candidates, isLoading, hint, error, onChange }: PersonSelectProps) {
  return (
    <SelectField
      ref={selectRef}
      label="人員"
      value={value}
      placeholder={isLoading ? '載入中…' : '選擇人員'}
      disabled={isLoading}
      options={personOptions(candidates)}
      error={error}
      hint={hint}
      onChange={onChange}
    />
  );
}

interface AddMemberFormProps {
  readonly detail: ProjectDetail;
  readonly onSaved: MemberFeedback['onSaved'];
}

/** 加入成員的欄位：挑的人、角色，以及組織的管理員建好新的人之後把他選起來。 */
function useAddMemberForm({ detail, onSaved }: AddMemberFormProps, personSelectRef: RefObject<HTMLButtonElement | null>) {
  const people = usePeople();
  const adding = useAddMember(detail, onSaved);
  const [personId, setPersonId] = useState('');
  const [role, setRole] = useState(DEFAULT_ROLE);
  const options = roleOptions(detail.blueprint);
  // 停用的人不列：他不能登入，加進來也用不了（internal/HANDOFF-security-local-ops.md 第 2 節第 3 點）。
  // 已經是成員的停用者照樣在上面的成員清單裡，這一輪不另外處理。
  const candidates = (people.data ?? []).filter(
    (person) => !person.disabled && !detail.members.some((member) => member.personId === person.personId),
  );
  const choosePerson = (next: string) => {
    setPersonId(next);
    adding.setErrors({ ...adding.errors, person: undefined });
  };
  const newPerson = useNewPersonToggle(personSelectRef, (person) => {
    choosePerson(person.personId);
    onSaved(`已新增人員「${person.displayName}」，選好角色後按「加入」。`);
  });

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (adding.isPending) return;
    const person = candidates.find((candidate) => candidate.personId === personId);
    const roleOption = options.find((option) => option.value === role) ?? { value: role, label: role };
    adding.add(person, roleOption, () => {
      setPersonId('');
    });
  }

  return { people, adding, personId, choosePerson, role, setRole, options, candidates, newPerson, handleSubmit };
}

/** 從組織的人員裡挑還不是成員的人，指定角色後加進專案。要的人還不在組織裡時，組織的管理員可以當場新增。 */
export function AddMemberForm(props: AddMemberFormProps) {
  const canCreatePeople = isOrganizationAdmin(useCurrentUser());
  const personSelectRef = useRef<HTMLButtonElement>(null);
  const { people, adding, personId, choosePerson, role, setRole, options, candidates, newPerson, handleSubmit } = useAddMemberForm(props, personSelectRef);

  // 讀不到組織的人員就挑不了人：說明原因，上面的成員清單照樣可以用。
  if (people.isError) {
    return <Notice tone="danger">{errorMessage(people.error)}</Notice>;
  }
  const hint = personHint(people.isSuccess && candidates.length === 0, canCreatePeople);
  return (
    <div className={styles.addSection}>
      <form className={styles.addForm} aria-label="加入成員" noValidate onSubmit={handleSubmit}>
        <h3 className={styles.subheading}>加入成員</h3>
        <FormAlert message={adding.formError} />
        <div className={styles.addRow}>
          <PersonSelect selectRef={personSelectRef} value={personId} candidates={candidates} isLoading={people.isPending} hint={hint} error={adding.errors.person} onChange={choosePerson} />
          <SelectField label="角色" hint={roleDescription(role)} value={role} options={options} error={adding.errors.role} onChange={setRole} />
          {/* 送出中停用但停得到（aria-disabled）：用 disabled 的話，按下的按鈕一停用焦點就掉回頁面本體，Esc 也關不掉對話框。 */}
          <Button type="submit" variant="primary" className={styles.addButton} aria-disabled={adding.isPending || undefined}>
            {adding.isPending ? '加入中…' : '加入'}
          </Button>
        </div>
        {canCreatePeople && <NewPersonButton toggle={newPerson} />}
      </form>
      {newPerson.isOpen && <InlineNewPerson id={newPerson.formId} onCreated={newPerson.created} onCancel={newPerson.cancel} />}
    </div>
  );
}
