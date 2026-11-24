import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type RefObject, type SubmitEvent } from 'react';
import { createPerson } from '../../api/people';
import { queryKeys } from '../../api/queryKeys';
import type { NewPersonRequest, Person } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { SelectField, TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { ORGANIZATION_ROLE_LABELS } from '../../domain/labels';
import { firstInvalidField, hasErrors, mapApiErrorToFields, type FieldErrors } from '../../lib/formErrors';
import {
  assignableRoles,
  DISPLAY_NAME_MAX_LENGTH,
  EMAIL_MAX_LENGTH,
  PASSWORD_HINT,
  PASSWORD_MAX_LENGTH,
  PERSON_ERROR_MAPPING,
  PERSON_FIELDS,
  validatePerson,
  type PersonField,
} from './personForm';

type FieldRefs = Readonly<Record<PersonField, RefObject<HTMLElement | null>>>;

function focusFirstInvalid(refs: FieldRefs, errors: FieldErrors<PersonField>): void {
  const field = firstInvalidField(PERSON_FIELDS, errors);
  if (field !== undefined) {
    refs[field].current?.focus();
  }
}

/** 送出前先檢查，後端的欄位錯誤對到欄位；成功時先讓人員清單重抓完，呼叫端拿到的清單裡就有這個人。 */
function useCreatePerson(refs: FieldRefs, onCreated: (person: Person) => void) {
  const queryClient = useQueryClient();
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<PersonField>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const mutation = useMutation({
    mutationFn: (request: NewPersonRequest) => createPerson(request),
    onSuccess: async (person) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.people });
      onCreated(person);
    },
    onError: (error) => {
      const mapped = mapApiErrorToFields(error, PERSON_ERROR_MAPPING);
      setFieldErrors(mapped.fieldErrors);
      setFormError(mapped.formError);
      focusFirstInvalid(refs, mapped.fieldErrors);
    },
  });

  function submit(request: NewPersonRequest) {
    const errors = validatePerson(request);
    setFormError(null);
    setFieldErrors(errors);
    if (hasErrors(errors)) {
      focusFirstInvalid(refs, errors);
      return;
    }
    mutation.mutate(request);
  }

  function clearError(field: PersonField) {
    setFieldErrors((errors) => ({ ...errors, [field]: undefined }));
  }

  return { submit, clearError, fieldErrors, formError, isPending: mutation.isPending };
}

/**
 * 新增人員的表單狀態：名稱、Email、初始密碼、組織角色（預設是成員）。人員頁的對話框與成員對話框裡就地展開的表單共用，
 * 檢查與錯誤的對應（personForm.ts）只有一份。
 */
export function useNewPersonForm(onCreated: (person: Person) => void) {
  const nameRef = useRef<HTMLInputElement>(null);
  const emailRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const roleRef = useRef<HTMLButtonElement>(null);
  const create = useCreatePerson({ displayName: nameRef, email: emailRef, password: passwordRef, role: roleRef }, onCreated);
  const [draft, setDraft] = useState<NewPersonRequest>({ displayName: '', email: '', password: '', role: 'member' });

  function update(field: PersonField, value: string) {
    setDraft((current) => ({ ...current, [field]: value }));
    create.clearError(field);
  }

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    create.submit({ ...draft, displayName: draft.displayName.trim(), email: draft.email.trim() });
  }

  return { draft, update, handleSubmit, create, nameRef, emailRef, passwordRef, roleRef, isPending: create.isPending };
}

export type NewPersonForm = ReturnType<typeof useNewPersonForm>;

/** 表單上方的錯誤與四個欄位；送出與取消的按鈕由呼叫端放（對話框與就地展開的長相不同）。 */
export function NewPersonFields({ form }: { readonly form: NewPersonForm }) {
  const user = useCurrentUser();
  const { draft, update, create, nameRef, emailRef, passwordRef, roleRef } = form;
  const { fieldErrors } = create;
  const roleOptions = assignableRoles(user).map((role) => ({ value: role, label: ORGANIZATION_ROLE_LABELS[role] }));
  return (
    <>
      <FormAlert message={create.formError} />
      <TextField ref={nameRef} label="名稱" value={draft.displayName} maxLength={DISPLAY_NAME_MAX_LENGTH} autoComplete="off" error={fieldErrors.displayName} onChange={(event) => { update('displayName', event.target.value); }} />
      <TextField ref={emailRef} label="Email" type="email" inputMode="email" value={draft.email} maxLength={EMAIL_MAX_LENGTH} autoComplete="off" error={fieldErrors.email} onChange={(event) => { update('email', event.target.value); }} />
      <TextField ref={passwordRef} label="密碼" type="password" value={draft.password} maxLength={PASSWORD_MAX_LENGTH} autoComplete="new-password" hint={PASSWORD_HINT} error={fieldErrors.password} onChange={(event) => { update('password', event.target.value); }} />
      <SelectField
        ref={roleRef}
        label="組織角色"
        value={draft.role}
        options={roleOptions}
        error={fieldErrors.role}
        onChange={(role) => { update('role', role); }}
      />
    </>
  );
}
