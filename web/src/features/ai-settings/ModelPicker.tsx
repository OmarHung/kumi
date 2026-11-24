import { useState } from 'react';
import { Button } from '../../components/Button/Button';
import { CheckboxField } from '../../components/CheckboxField/CheckboxField';
import { TextField } from '../../components/Field/Field';
import { IconButton } from '../../components/IconButton/IconButton';
import { Notice } from '../../components/Notice/Notice';
import { effortLabel } from '../ai/aiLabels';
import { MODEL_FILTER_THRESHOLD, matchesFilter, type ListedModel } from './connectionDraft';
import type { ConnectionForm } from './useConnectionForm';
import styles from './AiSettings.module.css';

/** 能選的思考深度，由淺到深：「低／中／高」；不能選的不寫。 */
function effortsText(model: ListedModel): string | null {
  return model.efforts.length === 0 ? null : model.efforts.map((effort) => effortLabel(effort)).join('／');
}

function ModelRow({ model, isEnabled, onToggle, onRemove }: {
  readonly model: ListedModel;
  readonly isEnabled: boolean;
  readonly onToggle: () => void;
  readonly onRemove: (() => void) | null;
}) {
  const efforts = effortsText(model);
  const detail = [model.id !== model.displayName ? model.id : null, efforts === null ? null : `思考深度 ${efforts}`].filter((part) => part !== null).join(' · ');
  return (
    <li className={styles.modelRow}>
      <CheckboxField
        label={
          <span className={styles.modelName}>
            {model.displayName}
            {detail !== '' && <small className={styles.modelDetail}>{detail}</small>}
          </span>
        }
        checked={isEnabled}
        onChange={onToggle}
      />
      {onRemove !== null && (
        <IconButton icon="close" size="sm" label={`移除 ${model.id}`} onClick={onRemove} />
      )}
    </li>
  );
}

/** 讀到的與手動加的模型：勾選開放給成員；超過 12 個時上面有篩選框。手動加的列在最後，可以移除。 */
function ModelList({ form }: { readonly form: ConnectionForm }) {
  const [filter, setFilter] = useState('');
  const { draft } = form;
  const total = draft.listed.length + draft.custom.length;
  const isFilterable = total > MODEL_FILTER_THRESHOLD;
  const listed = draft.listed.filter((model) => matchesFilter(model, filter));
  const custom = draft.custom.filter((model) => matchesFilter(model, filter));
  if (total === 0) return <p className={styles.note}>還沒有模型。按「讀取模型」，或在下面手動加。</p>;
  return (
    <>
      {isFilterable && (
        <TextField label="篩選模型" type="search" autoComplete="off" value={filter} onChange={(event) => { setFilter(event.target.value); }} />
      )}
      <ul className={styles.modelList} aria-label="開放給成員的模型">
        {listed.map((model) => (
          <ModelRow key={model.id} model={model} isEnabled={draft.enabled.includes(model.id)} onToggle={() => { form.toggleModel(model.id); }} onRemove={null} />
        ))}
        {custom.map((model) => (
          <ModelRow key={model.id} model={model} isEnabled={draft.enabled.includes(model.id)} onToggle={() => { form.toggleModel(model.id); }} onRemove={() => { form.removeCustom(model.id); }} />
        ))}
      </ul>
      {filter !== '' && listed.length + custom.length === 0 && <p className={styles.note}>沒有符合的模型。</p>}
    </>
  );
}

/** 手動加模型（清單還沒列的新模型，或相容端點讀不到清單時）：輸入代號加「加入」。 */
function CustomModelAdd({ form }: { readonly form: ConnectionForm }) {
  return (
    <div className={styles.customAdd}>
      <TextField
        label="手動加模型"
        isLabelHidden
        placeholder="手動加模型：輸入模型代號"
        autoComplete="off"
        spellCheck={false}
        maxLength={200}
        value={form.customId}
        error={form.customError ?? undefined}
        onChange={(event) => {
          form.setCustomId(event.target.value);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Enter' && !event.nativeEvent.isComposing) {
            event.preventDefault();
            form.addCustom();
          }
        }}
      />
      <Button onClick={form.addCustom}>加入</Button>
    </div>
  );
}

/** 讀取模型、模型的勾選與手動加。 */
export function ModelPicker({ form }: { readonly form: ConnectionForm }) {
  const modelsError = form.errors.enabledModels ?? form.errors.customModels;
  return (
    <section className={styles.models} aria-label="模型">
      <div className={styles.modelsHead}>
        <h3 className={styles.sectionTitle}>模型</h3>
        <Button aria-disabled={form.isDiscovering || undefined} onClick={form.discover}>
          {form.isDiscovering ? '讀取中…' : '讀取模型'}
        </Button>
      </div>
      <Notice tone="danger">{form.discoverError}</Notice>
      <ModelList form={form} />
      <CustomModelAdd form={form} />
      <Notice tone="danger">{modelsError}</Notice>
    </section>
  );
}
