import { useState, type SubmitEvent } from 'react';
import type { UseMutationResult } from '@tanstack/react-query';
import { errorMessage } from '../../../api/http';
import type { KnowledgePageNode, KnowledgePageTree } from '../../../api/knowledgeTypes';
import { Dialog, DialogFormActions } from '../../../components/Dialog/Dialog';
import { SelectField } from '../../../components/Field/Field';
import type { SelectOption } from '../../../components/Select/Select';
import { FormAlert } from '../../../components/FormAlert/FormAlert';
import { moveBlock, validMove, type MoveBlock, type MovePlan } from './treeDrop';
import { pathTitle, type TreeModel } from './treeModel';
import styles from './MovePageDialog.module.css';

const ROOT = 'root';
const END = 'end';

const BLOCK_REASON: Readonly<Record<MoveBlock, string>> = {
  self: '自己',
  inside: '在這一頁底下',
  too_deep: '會超過 10 層',
};

type MoveMutation = UseMutationResult<KnowledgePageTree, Error, { readonly pageNumber: number; readonly plan: MovePlan }>;

interface MovePageDialogProps {
  readonly node: KnowledgePageNode;
  readonly model: TreeModel;
  readonly move: MoveMutation;
  readonly onClose: () => void;
}

function parentOptions(model: TreeModel, node: KnowledgePageNode): readonly SelectOption[] {
  const pages = model.pages.map((page): SelectOption => {
    const block = moveBlock(model, node, page.number);
    return { value: String(page.number), label: pathTitle(model, page), disabledReason: block === null ? undefined : BLOCK_REASON[block] };
  });
  return [{ value: ROOT, label: '（最上層）' }, ...pages];
}

/** 這個上層底下除了自己以外的頁面，各一個「放在它前面」，最後一個是「放在最後」。 */
function positionOptions(model: TreeModel, node: KnowledgePageNode, parentValue: string): readonly SelectOption[] {
  const parent = parentValue === ROOT ? null : (model.nodeOf(Number(parentValue)) ?? null);
  const siblings = model.childrenOf(parent).filter((sibling) => sibling.number !== node.number);
  return [...siblings.map((sibling): SelectOption => ({ value: String(sibling.number), label: `放在「${sibling.title}」前面` })), { value: END, label: '放在最後' }];
}

function initialChoice(model: TreeModel, node: KnowledgePageNode) {
  const parent = model.parentOf(node);
  const siblings = model.childrenOf(parent);
  const next = siblings[siblings.findIndex((sibling) => sibling.number === node.number) + 1];
  return { parent: parent === null ? ROOT : String(parent.number), before: next === undefined ? END : String(next.number) };
}

/**
 * 「搬移…」：拖曳的替代操作（WCAG 2.5.7，不靠拖曳也能搬）。選上層與位置，不能放的上層列出來但停用並寫原因（自己、自己底下、超過 10 層）。
 * 沒換位置就送出只是關掉，不送請求。
 */
export function MovePageDialog({ node, model, move, onClose }: MovePageDialogProps) {
  const [choice, setChoice] = useState(() => initialChoice(model, node));
  const positions = positionOptions(model, node, choice.parent);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (move.isPending) return;
    const plan = validMove(model, node, {
      parentNumber: choice.parent === ROOT ? null : Number(choice.parent),
      beforeNumber: choice.before === END ? null : Number(choice.before),
    });
    if (plan === null) {
      onClose();
      return;
    }
    move.mutate({ pageNumber: node.number, plan }, { onSuccess: onClose });
  }

  return (
    <Dialog title={`搬移「${node.title}」`} onClose={move.isPending ? () => undefined : onClose}>
      <form className={styles.form} onSubmit={handleSubmit}>
        <SelectField
          label="上層頁面"
          value={choice.parent}
          options={parentOptions(model, node)}
          onChange={(parent) => {
            setChoice({ parent, before: END });
          }}
        />
        <SelectField
          label="位置"
          value={choice.before}
          options={positions}
          onChange={(before) => {
            setChoice({ ...choice, before });
          }}
        />
        <FormAlert message={move.isError ? errorMessage(move.error) : null} />
        <DialogFormActions submitLabel="搬移" pendingLabel="搬移中…" isPending={move.isPending} onCancel={onClose} />
      </form>
    </Dialog>
  );
}
