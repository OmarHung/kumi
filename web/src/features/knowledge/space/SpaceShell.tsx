import { useCallback, useId, useMemo, useRef, useState, type KeyboardEvent, type ReactNode, type RefObject } from 'react';
import { Link, useLocation } from 'react-router';
import { errorMessage } from '../../../api/http';
import type { KnowledgeSpace, KnowledgePageTree } from '../../../api/knowledgeTypes';
import { Button, buttonClassName } from '../../../components/Button/Button';
import { Icon } from '../../../components/Icon/Icon';
import { Notice } from '../../../components/Notice/Notice';
import { PageBar, type Crumb } from '../../../components/PageBar/PageBar';
import { PageLayout } from '../../../components/PageLayout/PageLayout';
import { useCloseOnOutsidePointer } from '../../../lib/useCloseOnOutsidePointer';
import { useIsBelowWide } from '../../../lib/useMediaQuery';
import { KNOWLEDGE_HOME, knowledgePageUrl, knowledgeSettingsUrl, knowledgeSpaceUrl, knowledgeTrashUrl, pageNumberOf } from '../knowledgeLinks';
import { DeletePageDialog } from '../page/DeletePageDialog';
import { KnowledgePageView, PageNotFound } from '../page/KnowledgePageView';
import { TrashedArrival, type InPlaceTrashed } from '../page/TrashedArrival';
import { RESTRICTED_ICON } from '../restrictedIcon';
import { RestrictionDialog } from '../restriction/RestrictionDialog';
import { MovePageDialog } from '../tree/MovePageDialog';
import { PageTree, type PageTreeProps } from '../tree/PageTree';
import { ancestorsOf, buildTreeModel, type TreeModel } from '../tree/treeModel';
import { SpaceHome } from './SpaceHome';
import { useSpaceActions, type SpaceActions } from './useSpaceActions';
import styles from './SpaceShell.module.css';

const READ_ONLY_TEXT = '這個空間已經封存，只能看。';
const PROJECT_ARCHIVED_TEXT = '這個空間綁的專案已經封存，只能看。';

type TreeDialog = { readonly kind: 'move' | 'delete' | 'restrict'; readonly pageNumber: number };

interface SpaceShellProps {
  readonly tree: KnowledgePageTree;
  /** 網址上 :pageNumber 的那一段；空間首頁沒有。 */
  readonly pageSegment: string | undefined;
  /** 重抓失敗的提示（畫面上可能是舊的）。 */
  readonly notice?: ReactNode;
}

interface CompactTreeProps {
  readonly isOpen: boolean;
  readonly onToggle: () => void;
  readonly onClose: () => void;
  readonly children: ReactNode;
}

/** 窄螢幕的「頁面」面板：按鈕與浮在它下面的樹。Esc、點外面、選了頁面都收起來，Esc 之後焦點回到按鈕。 */
function CompactTree({ isOpen, onToggle, onClose, children }: CompactTreeProps) {
  const panelId = useId();
  const buttonRef = useRef<HTMLButtonElement>(null);
  const rootRef = useCloseOnOutsidePointer<HTMLDivElement>(isOpen, onClose);
  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== 'Escape' || !isOpen) return;
    event.stopPropagation();
    onClose();
    buttonRef.current?.focus();
  }
  return (
    <div ref={rootRef} className={styles.compact} onKeyDown={handleKeyDown}>
      <Button ref={buttonRef} size="sm" aria-expanded={isOpen} aria-controls={isOpen ? panelId : undefined} onClick={onToggle}>
        <Icon name="sidebar" />
        頁面
      </Button>
      {isOpen && (
        <div id={panelId} className={styles.panel}>
          {children}
        </div>
      )}
    </div>
  );
}

/** 位置：知識庫 › 空間 › 上層們 › 這一頁（這一頁沒有連結）。空間首頁時空間是最後一個。 */
function crumbsOf(space: KnowledgeSpace, model: TreeModel, pageNumber: number | null): readonly Crumb[] {
  const node = pageNumber === null ? undefined : model.nodeOf(pageNumber);
  const trail =
    node === undefined
      ? []
      : [...ancestorsOf(model, node).map((ancestor): Crumb => ({ label: ancestor.title, to: knowledgePageUrl(space.key, ancestor.number) })), { label: node.title }];
  return [{ label: '知識庫', to: KNOWLEDGE_HOME }, { label: space.name, to: pageNumber === null ? undefined : knowledgeSpaceUrl(space.key) }, ...trail];
}

function BarActions({ space }: { readonly space: KnowledgeSpace }) {
  return (
    <>
      <Link to={knowledgeTrashUrl(space.key)} className={buttonClassName('ghost', 'sm')}>
        垃圾桶
      </Link>
      {space.myPermissions.canManage && (
        <Link to={knowledgeSettingsUrl(space.key)} className={buttonClassName('ghost', 'sm')}>
          空間設定
        </Link>
      )}
    </>
  );
}

/** 拖曳搬移被擋下（對話框沒開著才在這裡說，開著的話對話框自己說）：樹已經換回原樣。 */
function MoveErrorNotice({ move }: { readonly move: SpaceActions['move'] }) {
  return (
    <Notice tone="danger" onDismiss={move.reset}>
      {errorMessage(move.error)}
    </Notice>
  );
}

function ReadOnlyNotice({ space }: { readonly space: KnowledgeSpace }) {
  const text = space.archivedAt !== null ? READ_ONLY_TEXT : space.project?.isArchived === true ? PROJECT_ARCHIVED_TEXT : null;
  return text === null ? null : (
    <Notice tone="info" role="none">
      {text}
    </Notice>
  );
}

interface ShellContentProps {
  readonly space: KnowledgeSpace;
  readonly model: TreeModel;
  readonly pageSegment: string | undefined;
  readonly pageNumber: number | null;
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
  readonly onNewPage: () => void;
}

function ShellContent({ space, model, pageSegment, pageNumber, headingRef, onNewPage }: ShellContentProps) {
  if (pageSegment === undefined) return <SpaceHome space={space} model={model} headingRef={headingRef} onNewPage={onNewPage} />;
  if (pageNumber === null) return <PageNotFound spaceKey={space.key} headingRef={headingRef} />;
  return <KnowledgePageView spaceKey={space.key} pageNumber={pageNumber} headingRef={headingRef} />;
}

interface ShellDialogsProps {
  readonly space: KnowledgeSpace;
  readonly model: TreeModel;
  readonly dialog: TreeDialog | null;
  readonly actions: SpaceActions;
  readonly onClose: () => void;
}

function ShellDialogs({ space, model, dialog, actions, onClose }: ShellDialogsProps) {
  const node = dialog === null ? undefined : model.nodeOf(dialog.pageNumber);
  if (dialog === null || node === undefined) return null;
  if (dialog.kind === 'move') return <MovePageDialog node={node} model={model} move={actions.move} onClose={onClose} />;
  if (dialog.kind === 'restrict') {
    return (
      <RestrictionDialog
        spaceKey={space.key}
        pageNumber={node.number}
        pageTitle={node.title}
        projectKey={space.project?.key ?? null}
        canEdit={space.myPermissions.canEdit}
        onClose={onClose}
      />
    );
  }
  return <DeletePageDialog spaceKey={space.key} node={node} model={model} remove={actions.remove} onClose={onClose} />;
}

/** 窄螢幕的頁面面板與樹上的對話框（搬移、刪除）：對話框放在外框、不放在樹裡，面板收起來時它不會跟著不見。 */
function useShellState(actions: SpaceActions, isBelowWide: boolean, setIsPanelOpen: (isOpen: boolean | ((was: boolean) => boolean)) => void, isPanelOpen: boolean) {
  const [dialog, setDialog] = useState<TreeDialog | null>(null);
  const { cancelNewPage, startNewPage } = actions;
  const closePanel = useCallback(() => {
    setIsPanelOpen(false);
    cancelNewPage();
  }, [cancelNewPage, setIsPanelOpen]);
  return {
    isPanelOpen,
    dialog,
    closePanel,
    togglePanel: () => { setIsPanelOpen((open) => !open); },
    closeDialog: () => { setDialog(null); },
    openDialog: (kind: TreeDialog['kind'], pageNumber: number) => {
      if (kind === 'move') actions.move.reset();
      if (kind === 'delete') actions.remove.reset();
      setDialog({ kind, pageNumber });
    },
    // 窄螢幕時樹收在面板裡：新增頁面的輸入框在樹上，先把面板打開。
    startNewPage: (parentNumber: number | null) => {
      if (isBelowWide) setIsPanelOpen(true);
      startNewPage(parentNumber);
    },
  };
}

type ShellState = ReturnType<typeof useShellState>;

interface ShellTreeProps {
  readonly space: KnowledgeSpace;
  readonly model: TreeModel;
  readonly pageNumber: number | null;
  readonly actions: SpaceActions;
  readonly shell: ShellState;
}

/**
 * 動作與外框的狀態。面板的開關與原地的刪除提示放在動作的外面：建好頁面（換頁之後）要收面板、刪別頁要就地提示，
 * 動作自己在 shell 之前就要用到。
 */
function useSpaceShell(spaceKey: string, model: TreeModel, pageNumber: number | null, isBelowWide: boolean) {
  const [isPanelOpen, setIsPanelOpen] = useState(false);
  const [inPlace, setInPlace] = useState<InPlaceTrashed | null>(null);
  const location = useLocation();
  const actions = useSpaceActions(spaceKey, model, pageNumber, {
    onCreated: () => {
      setIsPanelOpen(false);
    },
    onTrashedInPlace: (trashed) => {
      setInPlace((previous) => ({ id: (previous?.id ?? 0) + 1, locationKey: location.key, trashed }));
    },
  });
  const shell = useShellState(actions, isBelowWide, setIsPanelOpen, isPanelOpen);
  return { actions, shell, inPlace };
}

/** 樹的選單在「搬移…」與「刪除…」之間多一個「限制…」：和頁面上那一行的入口打開同一個對話框。 */
function restrictionItems(shell: ShellState): PageTreeProps['extraItems'] {
  return (node) => [
    {
      key: 'restrict',
      label: '限制…',
      icon: RESTRICTED_ICON,
      onSelect: () => {
        shell.openDialog('restrict', node.number);
      },
    },
  ];
}

function ShellTree({ space, model, pageNumber, actions, shell }: ShellTreeProps) {
  return (
    <PageTree
      spaceKey={space.key}
      model={model}
      currentNumber={pageNumber}
      canEdit={space.myPermissions.canEdit}
      newPage={actions.newPage}
      onStartNewPage={shell.startNewPage}
      onSubmitNewPage={actions.submitNewPage}
      onCancelNewPage={actions.cancelNewPage}
      onAddChild={(node) => { shell.startNewPage(node.number); }}
      onMove={(node) => { shell.openDialog('move', node.number); }}
      onDelete={(node) => { shell.openDialog('delete', node.number); }}
      extraItems={restrictionItems(shell)}
      onDropMove={(movedNumber, plan) => { actions.move.mutate({ pageNumber: movedNumber, plan }); }}
      isMoving={actions.move.isPending}
      onNavigate={shell.closePanel}
    />
  );
}

/** 空間的外框：頁列（位置、垃圾桶、空間設定）、左邊的頁面樹、右邊的內容（空間首頁或一頁）；搬移與刪除的對話框也在這裡。 */
export function SpaceShell({ tree, pageSegment, notice }: SpaceShellProps) {
  const { space } = tree;
  const model = useMemo(() => buildTreeModel(tree.pages), [tree.pages]);
  const pageNumber = pageSegment === undefined ? null : pageNumberOf(pageSegment);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const isBelowWide = useIsBelowWide();
  const { actions, shell, inPlace } = useSpaceShell(space.key, model, pageNumber, isBelowWide);

  const treeView = <ShellTree space={space} model={model} pageNumber={pageNumber} actions={actions} shell={shell} />;

  return (
    <PageLayout
      variant="reading"
      bar={
        <PageBar
          title={(pageNumber === null ? undefined : model.nodeOf(pageNumber)?.title) ?? space.name}
          titleInBar={false}
          crumbs={crumbsOf(space, model, pageNumber)}
          actions={<BarActions space={space} />}
        />
      }
      toolbar={
        isBelowWide ? (
          <CompactTree isOpen={shell.isPanelOpen} onToggle={shell.togglePanel} onClose={shell.closePanel}>
            {treeView}
          </CompactTree>
        ) : undefined
      }
    >
      <div className={styles.shell} data-has-tree={!isBelowWide}>
        {!isBelowWide && <aside className={styles.tree}>{treeView}</aside>}
        <div className={styles.main}>
          {notice}
          <TrashedArrival headingRef={headingRef} inPlace={inPlace} />
          <ReadOnlyNotice space={space} />
          {actions.move.isError && shell.dialog?.kind !== 'move' && <MoveErrorNotice move={actions.move} />}
          <ShellContent
            space={space}
            model={model}
            pageSegment={pageSegment}
            pageNumber={pageNumber}
            headingRef={headingRef}
            onNewPage={() => { shell.startNewPage(null); }}
          />
        </div>
      </div>
      <ShellDialogs space={space} model={model} dialog={shell.dialog} actions={actions} onClose={shell.closeDialog} />
    </PageLayout>
  );
}
