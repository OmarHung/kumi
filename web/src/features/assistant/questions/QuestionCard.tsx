import { useState } from 'react';
import { Button } from '../../../components/Button/Button';
import { Icon } from '../../../components/Icon/Icon';
import { Notice } from '../../../components/Notice/Notice';
import { formatAnswers, type AssistantQuestion, type AssistantQuestionOption, type QuestionAnswer } from './questionBlock';
import styles from './QuestionCard.module.css';

/** 能作答時才有：送出訊息的函式、送出中、送出失敗的說明（沒有是 null）。沒有這個就是唯讀。 */
export interface QuestionAnswering {
  readonly isSending: boolean;
  readonly error: string | null;
  readonly onSubmit: (text: string) => void;
}

interface DraftAnswer {
  readonly labels: readonly string[];
  readonly isOtherOn: boolean;
  readonly otherText: string;
  readonly isSkipped: boolean;
}

const EMPTY_DRAFT: DraftAnswer = { labels: [], isOtherOn: false, otherText: '', isSkipped: false };

function isAnswered(draft: DraftAnswer): boolean {
  return draft.isSkipped || draft.labels.length > 0 || (draft.isOtherOn && draft.otherText.trim() !== '');
}

function toAnswer(draft: DraftAnswer): QuestionAnswer {
  if (draft.isSkipped) return { labels: [], other: null, isSkipped: true };
  const other = draft.isOtherOn && draft.otherText.trim() !== '' ? draft.otherText.trim() : null;
  return { labels: draft.labels, other, isSkipped: false };
}

function ReadOnlyCard({ questions }: { readonly questions: readonly AssistantQuestion[] }) {
  return (
    <section className={styles.card} aria-label="助理的問題">
      <ol className={styles.readOnlyList}>
        {questions.map((question) => (
          <li key={question.question}>
            <p className={styles.question}>{question.question}</p>
            {question.options.length > 0 && (
              <ul className={styles.readOnlyOptions}>
                {question.options.map((option) => (
                  <li key={option.label}>
                    {option.label}
                    {option.description !== undefined && `（${option.description}）`}
                    {option.label === question.recommended && '　建議'}
                  </li>
                ))}
              </ul>
            )}
          </li>
        ))}
      </ol>
    </section>
  );
}

interface PaneProps {
  readonly question: AssistantQuestion;
  readonly draft: DraftAnswer;
  readonly isCurrent: boolean;
  readonly isLocked: boolean;
  readonly onChange: (next: DraftAnswer, advance: boolean) => void;
}

/** 點選項之後的答案：多選切換，單選換掉「其他」。 */
function afterPick(draft: DraftAnswer, label: string, isMulti: boolean): DraftAnswer {
  if (!isMulti) return { ...EMPTY_DRAFT, labels: [label] };
  const labels = draft.labels.includes(label) ? draft.labels.filter((each) => each !== label) : [...draft.labels, label];
  return { ...draft, labels, isSkipped: false };
}

function afterToggleOther(draft: DraftAnswer, isMulti: boolean): DraftAnswer {
  const isOtherOn = !draft.isOtherOn;
  return { ...draft, isOtherOn, isSkipped: false, labels: isMulti || !isOtherOn ? draft.labels : [] };
}

interface OptionButtonProps {
  readonly option: AssistantQuestionOption;
  readonly isRecommended: boolean;
  readonly isOn: boolean;
  readonly isMulti: boolean;
  readonly isLocked: boolean;
  readonly onPick: () => void;
}

function OptionButton({ option, isRecommended, isOn, isMulti, isLocked, onPick }: OptionButtonProps) {
  return (
    <button type="button" className={styles.option} aria-pressed={isOn} aria-disabled={isLocked || undefined} onClick={onPick}>
      {isMulti && <Icon name="check" className={styles.check} />}
      <span className={styles.optionBody}>
        <span>
          {option.label}
          {isRecommended && <span className={styles.recommended}>建議</span>}
        </span>
        {option.description !== undefined && <span className={styles.optionMeta}>{option.description}</span>}
      </span>
    </button>
  );
}

function Pane({ question, draft, isCurrent, isLocked, onChange }: PaneProps) {
  const isMulti = question.multiSelect === true;
  return (
    <div className={styles.pane} data-current={isCurrent} aria-hidden={isCurrent ? undefined : true} inert={!isCurrent}>
      <p className={styles.question}>{question.question}</p>
      <ul className={styles.options}>
        {question.options.map((option) => (
          <li key={option.label}>
            <OptionButton
              option={option}
              isRecommended={option.label === question.recommended}
              isOn={draft.labels.includes(option.label)}
              isMulti={isMulti}
              isLocked={isLocked}
              onPick={() => {
                // 單選：選了直接到下一題（少點一次）。
                if (!isLocked) onChange(afterPick(draft, option.label, isMulti), !isMulti);
              }}
            />
          </li>
        ))}
        <li>
          <button
            type="button"
            className={styles.option}
            aria-pressed={draft.isOtherOn}
            aria-disabled={isLocked || undefined}
            onClick={() => {
              if (!isLocked) onChange(afterToggleOther(draft, isMulti), false);
            }}
          >
            其他
          </button>
        </li>
      </ul>
      {draft.isOtherOn && (
        <input
          className={styles.otherInput}
          aria-label="其他的回答"
          value={draft.otherText}
          readOnly={isLocked}
          onChange={(event) => {
            onChange({ ...draft, otherText: event.target.value }, false);
          }}
        />
      )}
    </div>
  );
}

interface ActionBarProps {
  readonly index: number;
  readonly isLast: boolean;
  readonly isSending: boolean;
  readonly currentDone: boolean;
  readonly canSubmit: boolean;
  readonly onMove: (delta: number) => void;
  readonly onSkip: () => void;
  readonly onSubmit: () => void;
}

function ActionBar({ index, isLast, isSending, currentDone, canSubmit, onMove, onSkip, onSubmit }: ActionBarProps) {
  return (
    <div className={styles.actions}>
      {index > 0 && (
        <Button
          size="sm"
          aria-disabled={isSending || undefined}
          onClick={() => {
            if (!isSending) onMove(-1);
          }}
        >
          上一題
        </Button>
      )}
      <span className={styles.spacer} />
      <Button size="sm" variant="ghost" aria-disabled={isSending || undefined} onClick={onSkip}>
        略過
      </Button>
      {isLast ? (
        <Button variant="primary" size="sm" aria-disabled={isSending || !canSubmit || undefined} onClick={onSubmit}>
          {isSending ? '送出中…' : '送出回答'}
        </Button>
      ) : (
        <Button
          variant="primary"
          size="sm"
          aria-disabled={isSending || !currentDone || undefined}
          onClick={() => {
            if (!isSending && currentDone) onMove(1);
          }}
        >
          下一題
        </Button>
      )}
    </div>
  );
}

interface PanesProps {
  readonly questions: readonly AssistantQuestion[];
  readonly drafts: readonly DraftAnswer[];
  readonly index: number;
  readonly isLocked: boolean;
  readonly onChange: (at: number, next: DraftAnswer, advance: boolean) => void;
}

function Panes({ questions, drafts, index, isLocked, onChange }: PanesProps) {
  return (
    <div className={styles.panes}>
      {questions.map((question, at) => (
        <Pane
          key={`${String(at)}-${question.question}`}
          question={question}
          draft={drafts[at] ?? EMPTY_DRAFT}
          isCurrent={at === index}
          isLocked={isLocked}
          onChange={(next, advance) => {
            onChange(at, next, advance);
          }}
        />
      ))}
    </div>
  );
}

function Editor({ questions, answering }: { readonly questions: readonly AssistantQuestion[]; readonly answering: QuestionAnswering }) {
  const [index, setIndex] = useState(0);
  const [drafts, setDrafts] = useState<readonly DraftAnswer[]>(() => questions.map(() => EMPTY_DRAFT));
  const last = questions.length - 1;
  const isLast = index === last;
  const { isSending } = answering;
  const currentDone = isAnswered(drafts[index] ?? EMPTY_DRAFT);
  const canSubmit = drafts.every(isAnswered);
  const change = (at: number, next: DraftAnswer, advance: boolean) => {
    setDrafts((current) => current.map((draft, position) => (position === at ? next : draft)));
    if (advance && at < last) setIndex(at + 1);
  };
  const skip = () => {
    if (isSending) return;
    change(index, { ...EMPTY_DRAFT, isSkipped: true }, true);
  };
  const submit = () => {
    if (isSending || !canSubmit) return;
    answering.onSubmit(formatAnswers(questions, drafts.map(toAnswer)));
  };
  return (
    <section className={styles.card} aria-label="助理的問題">
      <p className={styles.progress} aria-live="polite">
        {questions.length > 1 ? `第 ${String(index + 1)}／${String(questions.length)} 題` : '問題'}
      </p>
      <Panes
        questions={questions}
        drafts={drafts}
        index={index}
        isLocked={isSending}
        onChange={change}
      />
      <ActionBar
        index={index}
        isLast={isLast}
        isSending={isSending}
        currentDone={currentDone}
        canSubmit={canSubmit}
        onMove={(delta) => {
          setIndex(index + delta);
        }}
        onSkip={skip}
        onSubmit={submit}
      />
      <Notice tone="danger">{answering.error}</Notice>
    </section>
  );
}

/** 助理回覆裡的問題卡片：一次一題；沒有 answering 時是唯讀（舊的回覆、回覆中、後面已有使用者的訊息）。 */
export function QuestionCard({ questions, answering }: { readonly questions: readonly AssistantQuestion[]; readonly answering?: QuestionAnswering }) {
  if (answering === undefined) return <ReadOnlyCard questions={questions} />;
  return <Editor questions={questions} answering={answering} />;
}
