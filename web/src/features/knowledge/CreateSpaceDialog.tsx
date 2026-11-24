import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type SubmitEvent } from 'react';
import { useNavigate } from 'react-router';
import { createSpace, KNOWLEDGE_SPACE_DESCRIPTION_MAX, KNOWLEDGE_SPACE_NAME_MAX } from '../../api/knowledge';
import type { NewKnowledgeSpace } from '../../api/knowledgeTypes';
import { queryKeys } from '../../api/queryKeys';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { FieldShell, SelectField, TextField, fieldControlClassName } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { SegmentedControl } from '../../components/SegmentedControl/SegmentedControl';
import { firstInvalidField, hasErrors, mapApiErrorToFields, type FieldErrors } from '../../lib/formErrors';
import { knowledgeSpaceUrl } from './knowledgeLinks';
import {
  SPACE_ERROR_MAPPING,
  SPACE_FIELDS,
  SPACE_KEY_HINT,
  SPACE_KEY_MAX_LENGTH,
  suggestSpaceKey,
  validateSpaceDescription,
  validateSpaceKey,
  validateSpaceName,
  withoutErrorOf,
  type SpaceField,
} from './spaceForm';
import type { SpaceCreation } from './useSpaceCreation';
import styles from './Knowledge.module.css';

type SpaceKind = 'organization' | 'project';

interface Draft {
  readonly kind: SpaceKind;
  readonly projectKey: string | null;
  readonly name: string;
  /** null：照專案代號或名稱推出來的建議；使用者改過就是他打的。 */
  readonly key: string | null;
  readonly description: string;
}

interface CreateSpaceDialogProps {
  readonly creation: SpaceCreation;
  readonly onClose: () => void;
}

function useDraft(creation: SpaceCreation) {
  const [draft, setDraft] = useState<Draft>({
    kind: creation.canCreateOrganizationSpace ? 'organization' : 'project',
    projectKey: null,
    name: '',
    key: null,
    description: '',
  });
  const projectKey = draft.projectKey ?? creation.projectChoices[0]?.key ?? '';
  const suggestedKey = draft.kind === 'project' ? projectKey : suggestSpaceKey(draft.name);
  const key = draft.key ?? suggestedKey;
  const toRequest = (): NewKnowledgeSpace => ({
    key,
    name: draft.name.trim(),
    description: draft.description.trim() === '' ? null : draft.description.trim(),
    projectKey: draft.kind === 'project' ? projectKey : null,
  });
  return { draft, projectKey, key, update: (change: Partial<Draft>) => { setDraft((current) => ({ ...current, ...change })); }, toRequest };
}

function validate(request: NewKnowledgeSpace, description: string): FieldErrors<SpaceField> {
  return {
    projectKey: request.projectKey === '' ? '請選擇專案。' : undefined,
    name: validateSpaceName(request.name),
    key: validateSpaceKey(request.key),
    description: validateSpaceDescription(description),
  };
}

function useCreateSpace() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const projectKeyRef = useRef<HTMLButtonElement>(null);
  const nameRef = useRef<HTMLInputElement>(null);
  const keyRef = useRef<HTMLInputElement>(null);
  const descriptionRef = useRef<HTMLTextAreaElement>(null);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<SpaceField>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const focusFirst = (errors: FieldErrors<SpaceField>) => {
    const refs = { projectKey: projectKeyRef, name: nameRef, key: keyRef, description: descriptionRef };
    const field = firstInvalidField(SPACE_FIELDS, errors);
    if (field !== undefined) refs[field].current?.focus();
  };
  const mutation = useMutation({
    mutationFn: (request: NewKnowledgeSpace) => createSpace(request),
    onSuccess: async (space) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeSpaces });
      await navigate(knowledgeSpaceUrl(space.key));
    },
    onError: (error) => {
      const mapped = mapApiErrorToFields(error, SPACE_ERROR_MAPPING);
      setFieldErrors(mapped.fieldErrors);
      setFormError(mapped.formError);
      focusFirst(mapped.fieldErrors);
    },
  });
  function submit(request: NewKnowledgeSpace, description: string) {
    const errors = validate(request, description);
    setFormError(null);
    setFieldErrors(errors);
    if (hasErrors(errors)) focusFirst(errors);
    else mutation.mutate(request);
  }
  const clearError = (field: SpaceField) => {
    setFieldErrors((errors) => withoutErrorOf(errors, field));
  };
  return { projectKeyRef, nameRef, keyRef, descriptionRef, fieldErrors, formError, isPending: mutation.isPending, submit, clearError };
}

type CreateState = ReturnType<typeof useCreateSpace>;
type DraftState = ReturnType<typeof useDraft>;

interface FieldsProps {
  readonly creation: SpaceCreation;
  readonly state: DraftState;
  readonly create: CreateState;
  readonly onEdit: (field: SpaceField, change: Partial<Draft>) => void;
}

function KindFields({ creation, state, create, onEdit }: FieldsProps) {
  const { draft, projectKey } = state;
  const { projectKeyRef } = create;
  const canChooseKind = creation.canCreateOrganizationSpace && creation.projectChoices.length > 0;
  return (
    <>
      {canChooseKind && (
        <SegmentedControl
          label="種類"
          value={draft.kind}
          options={[
            { value: 'organization', label: '組織空間' },
            { value: 'project', label: '專案空間' },
          ]}
          onChange={(kind) => { onEdit('projectKey', { kind }); }}
        />
      )}
      {draft.kind === 'project' && (
        <SelectField
          ref={projectKeyRef}
          label="專案"
          value={projectKey}
          options={creation.projectChoices.map((project) => ({ value: project.key, label: `${project.key} ${project.name}` }))}
          error={create.fieldErrors.projectKey}
          hint="專案空間的成員就是專案的成員；一個專案只能有一個空間。"
          onChange={(value) => { onEdit('projectKey', { projectKey: value }); }}
        />
      )}
    </>
  );
}

function TextFields({ state, create, onEdit }: Omit<FieldsProps, 'creation'>) {
  const { nameRef, keyRef, descriptionRef, fieldErrors: errors } = create;
  return (
    <>
      <TextField
        ref={nameRef}
        label="名稱"
        value={state.draft.name}
        maxLength={KNOWLEDGE_SPACE_NAME_MAX}
        autoComplete="off"
        error={errors.name}
        onChange={(event) => { onEdit('name', { name: event.target.value }); }}
      />
      <TextField
        ref={keyRef}
        label="代號"
        value={state.key}
        maxLength={SPACE_KEY_MAX_LENGTH}
        autoComplete="off"
        spellCheck={false}
        hint={SPACE_KEY_HINT}
        error={errors.key}
        onChange={(event) => { onEdit('key', { key: event.target.value.toUpperCase() }); }}
      />
      <FieldShell label="簡介" hint="可以不填：這個空間放什麼、給誰看。" error={errors.description}>
        {(control) => (
          <textarea
            ref={descriptionRef}
            id={control.id}
            className={fieldControlClassName('textarea', errors.description !== undefined)}
            rows={3}
            maxLength={KNOWLEDGE_SPACE_DESCRIPTION_MAX}
            value={state.draft.description}
            aria-invalid={errors.description === undefined ? undefined : true}
            aria-describedby={control.describedBy}
            onChange={(event) => { onEdit('description', { description: event.target.value }); }}
          />
        )}
      </FieldShell>
    </>
  );
}

/**
 * 新增空間：組織空間（組織管理員）或專案空間（專案的管理員）。只能建一種時不顯示種類的選擇；
 * 專案空間的代號預設帶專案代號（空間的網址與專案對得起來），組織空間的名稱就是英文字時帶名稱。建立後直接進到那個空間。
 */
export function CreateSpaceDialog({ creation, onClose }: CreateSpaceDialogProps) {
  const state = useDraft(creation);
  const create = useCreateSpace();
  const { isPending, clearError } = create;

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    create.submit(state.toRequest(), state.draft.description);
  }
  function onEdit(field: SpaceField, change: Partial<Draft>) {
    state.update(change);
    clearError(field);
  }
  // 送出中不讓 Esc 或「取消」關掉，結果才不會沒人看到。
  const close = isPending ? () => undefined : onClose;

  return (
    <Dialog title="新增空間" description="空間放一組相關的頁面：組織的手冊、某個專案的文件。" onClose={close}>
      <form className={styles.form} noValidate onSubmit={handleSubmit}>
        <FormAlert message={create.formError} />
        <KindFields creation={creation} state={state} create={create} onEdit={onEdit} />
        <TextFields state={state} create={create} onEdit={onEdit} />
        <DialogFormActions submitLabel="建立" pendingLabel="建立中…" isPending={isPending} onCancel={close} />
      </form>
    </Dialog>
  );
}
