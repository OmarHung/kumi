import { CheckboxField, CheckboxGroup } from '../../components/CheckboxField/CheckboxField';
import { SelectField, TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { EXPIRY_CHOICES, SCOPE_OPTIONS, scopeConflictNote } from './accessTokenText';
import type { CreateTokenForm } from './createAccessTokenForm';

/** 建立對話框的欄位：名稱、範圍（五個勾選框，預設勾前四個）、期限（預設 90 天）。 */
export function CreateTokenFields({ form }: { readonly form: CreateTokenForm }) {
  const { draft, errors } = form;
  return (
    <>
      <FormAlert message={form.formError} />
      <TextField
        ref={form.register('name')}
        label="名稱"
        value={draft.name}
        autoComplete="off"
        hint="寫出用在哪裡，例如「我的筆電的 CLI」，之後才分得出要撤銷哪一把。"
        error={errors.name}
        onChange={(event) => { form.change({ name: event.target.value }, 'name'); }}
      />
      <CheckboxGroup legend="範圍" layout="column" hint="權杖能做的事，不會超過你自己的權限。" error={errors.scopes}>
        {SCOPE_OPTIONS.map((option, index) => {
          const note = scopeConflictNote(option.scope, draft.scopes);
          return (
            <CheckboxField
              key={option.scope}
              ref={index === 0 ? form.register('scopes') : undefined}
              label={option.label}
              hint={note === null ? option.description : `${option.description}${note}`}
              checked={draft.scopes.includes(option.scope)}
              disabled={note !== null}
              onChange={(event) => { form.toggleScope(option.scope, event.target.checked); }}
            />
          );
        })}
      </CheckboxGroup>
      <SelectField
        ref={form.register('expiresInDays')}
        label="期限"
        value={draft.expiry}
        options={EXPIRY_CHOICES.map(({ value, label }) => ({ value, label }))}
        error={errors.expiresInDays}
        onChange={(value) => { form.change({ expiry: value }, 'expiresInDays'); }}
      />
    </>
  );
}
