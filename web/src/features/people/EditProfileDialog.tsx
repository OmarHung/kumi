import { useRef, useState, type SubmitEvent } from 'react';
import type { Person } from '../../api/types';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { mapApiErrorToFields, type FieldMapping } from '../../lib/formErrors';
import { useSetPersonProfile } from './personMutations';
import { PROFILE_LIMITS } from './personProfile';
import styles from './PeoplePage.module.css';

type ProfileField = 'jobTitle' | 'responsibilities';

/** 細項的路徑和欄位同名；伺服器沒指到欄位的錯誤（403、404）寫在表單上方。 */
const MAPPING: FieldMapping<ProfileField> = {
  fields: ['jobTitle', 'responsibilities'],
  codes: {},
};

interface EditProfileDialogProps {
  readonly person: Person;
  readonly onClose: () => void;
  /** 存好了（內容沒變也一樣）：呼叫端關上對話框、在頁面上方說明結果。 */
  readonly onSaved: (person: Person) => void;
}

function useProfileForm(person: Person, onSaved: (person: Person) => void) {
  const jobTitleRef = useRef<HTMLInputElement>(null);
  const responsibilitiesRef = useRef<HTMLInputElement>(null);
  const [jobTitle, setJobTitle] = useState(person.jobTitle ?? '');
  const [responsibilities, setResponsibilities] = useState(person.responsibilities ?? '');
  const [errors, setErrors] = useState<Partial<Record<ProfileField, string>>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const save = useSetPersonProfile();

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    // 送出中按鈕只標 aria-disabled（焦點留在上面），再按一次照樣觸發 submit：這裡擋下重送。
    if (save.isPending) return;
    setErrors({});
    setFormError(null);
    // 空白當作沒有：伺服器也會去掉前後空白、空字串當 null，這裡先照同樣的規則送。
    save.mutate(
      { personId: person.personId, jobTitle: jobTitle.trim() || null, responsibilities: responsibilities.trim() || null },
      {
        onSuccess: onSaved,
        onError: (error) => {
          const mapped = mapApiErrorToFields(error, MAPPING);
          setErrors(mapped.fieldErrors);
          setFormError(mapped.formError);
          if (mapped.fieldErrors.jobTitle !== undefined) jobTitleRef.current?.focus();
          else if (mapped.fieldErrors.responsibilities !== undefined) responsibilitiesRef.current?.focus();
        },
      },
    );
  }

  return { jobTitleRef, responsibilitiesRef, jobTitle, setJobTitle, responsibilities, setResponsibilities, errors, formError, handleSubmit, isPending: save.isPending };
}

/**
 * 「編輯職稱…」：職稱與負責範圍，兩個都是一行字，空著就是沒有。AI 助理靠這兩個欄位判斷任務該指派給誰，
 * 所以提示寫的是負責什麼。送出中 Esc 與「取消」都不作用；儲存鈕只標 aria-disabled，焦點留在它身上。
 */
export function EditProfileDialog({ person, onClose, onSaved }: EditProfileDialogProps) {
  const { jobTitleRef, responsibilitiesRef, jobTitle, setJobTitle, responsibilities, setResponsibilities, errors, formError, handleSubmit, isPending } =
    useProfileForm(person, onSaved);
  const close = isPending ? () => undefined : onClose;
  return (
    <Dialog title={`編輯「${person.displayName}」的職稱`} onClose={close}>
      <form className={styles.form} noValidate onSubmit={handleSubmit}>
        <FormAlert message={formError} />
        <TextField
          ref={jobTitleRef}
          label="職稱"
          value={jobTitle}
          maxLength={PROFILE_LIMITS.jobTitle}
          hint="例如「前端工程師」。"
          error={errors.jobTitle}
          onChange={(event) => {
            setJobTitle(event.target.value);
          }}
        />
        <TextField
          ref={responsibilitiesRef}
          label="負責範圍"
          value={responsibilities}
          maxLength={PROFILE_LIMITS.responsibilities}
          hint="一行字，例如「WordPress 維運、範例客戶的網站」。AI 助理會依此判斷該指派給誰。"
          error={errors.responsibilities}
          onChange={(event) => {
            setResponsibilities(event.target.value);
          }}
        />
        <DialogFormActions submitLabel="儲存" pendingLabel="儲存中…" isPending={isPending} keepsFocusWhilePending onCancel={close} />
      </form>
    </Dialog>
  );
}
