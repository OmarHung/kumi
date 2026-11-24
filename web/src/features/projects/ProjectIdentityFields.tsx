import type { Ref } from 'react';
import { TextField } from '../../components/Field/Field';
import type { FieldErrors } from '../../lib/formErrors';
import { PROJECT_KEY_HINT, PROJECT_KEY_MAX_LENGTH, type ProjectField } from './projectForm';

interface ProjectIdentityFieldsProps {
  readonly keyRef: Ref<HTMLInputElement>;
  readonly nameRef: Ref<HTMLInputElement>;
  readonly projectKey: string;
  readonly name: string;
  readonly errors: FieldErrors<ProjectField>;
  readonly onChange: (field: 'key' | 'name', value: string) => void;
}

/** 專案的代號與名稱。代號一律轉成大寫：格式只接受大寫，不必讓使用者自己切換。 */
export function ProjectIdentityFields({
  keyRef,
  nameRef,
  projectKey,
  name,
  errors,
  onChange,
}: ProjectIdentityFieldsProps) {
  return (
    <>
      <TextField
        ref={keyRef}
        label="代號"
        value={projectKey}
        maxLength={PROJECT_KEY_MAX_LENGTH}
        autoComplete="off"
        spellCheck={false}
        hint={PROJECT_KEY_HINT}
        error={errors.key}
        onChange={(event) => {
          onChange('key', event.target.value.toUpperCase());
        }}
      />
      <TextField
        ref={nameRef}
        label="名稱"
        value={name}
        autoComplete="off"
        error={errors.name}
        onChange={(event) => {
          onChange('name', event.target.value);
        }}
      />
    </>
  );
}
