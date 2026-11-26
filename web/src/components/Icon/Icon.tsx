// 圖示套件只在這個檔案匯入（docs/DESIGN.md 第 7、13 節）：畫面用語意名稱，換圖或換套件只改這張對照表。
// 一律具名匯入，打包時才只帶用到的圖示。
import {
  ArrowDown,
  ArrowDownWideNarrow,
  ArrowRightLeft,
  ArrowUp,
  AppWindow,
  ArrowUpNarrowWide,
  Bell,
  Bold,
  BookOpen,
  BellRing,
  Calendar,
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Circle,
  CircleAlert,
  CircleCheck,
  CircleDashed,
  CircleSlash,
  Columns3,
  CircleUserRound,
  Contrast,
  Copy,
  CornerDownRight,
  Code,
  Ellipsis,
  ExternalLink,
  File,
  FileArchive,
  FileImage,
  FileSpreadsheet,
  FileText,
  FileType,
  Eye,
  EyeOff,
  FolderKanban,
  Heading2,
  Inbox,
  Info,
  Italic,
  KeyRound,
  LayoutDashboard,
  Link,
  List,
  Lock,
  ListFilter,
  ListOrdered,
  ListTodo,
  LogOut,
  Mail,
  Maximize2,
  Menu,
  Monitor,
  Moon,
  PanelLeft,
  Paperclip,
  PanelRight,
  Pencil,
  Plus,
  Search,
  Settings,
  Sparkles,
  Square,
  SquareCode,
  SquareKanban,
  Strikethrough,
  Sun,
  Table2,
  TextQuote,
  Trash,
  TriangleAlert,
  Undo2,
  Upload,
  UserRoundCheck,
  UserRoundX,
  Users,
  UsersRound,
  X,
  type LucideIcon,
  Share2,
} from 'lucide-react';
import { cx } from '../../lib/cx';
import styles from './Icon.module.css';

const ICONS = {
  add: Plus,
  close: X,
  more: Ellipsis,
  menu: Menu,
  sidebar: PanelLeft,
  search: Search,
  filter: ListFilter,
  columns: Columns3,
  chevronDown: ChevronDown,
  chevronLeft: ChevronLeft,
  chevronRight: ChevronRight,
  arrowUp: ArrowUp,
  arrowDown: ArrowDown,
  sortAsc: ArrowUpNarrowWide,
  sortDesc: ArrowDownWideNarrow,
  move: ArrowRightLeft,
  edit: Pencil,
  delete: Trash,
  restore: Undo2,
  check: Check,
  share: Share2,
  externalLink: ExternalLink,
  upload: Upload,
  // AI 助理的輸入框與訊息：附加檔案、複製訊息、停止回覆（方形，填滿）。
  attach: Paperclip,
  copy: Copy,
  stop: Square,
  // 附件的檔案類型（PM-5）：圖片、PDF、文件、試算表、壓縮檔、其他。
  fileImage: FileImage,
  filePdf: FileText,
  fileDocument: FileType,
  fileSpreadsheet: FileSpreadsheet,
  fileArchive: FileArchive,
  fileOther: File,
  expand: Maximize2, // 面板展開成整頁（F 輪）
  // 項目面板的兩種樣子：右邊的側邊面板、置中的浮動視窗（標題列的切換按鈕，圖示跟著目前的樣子）。
  panelSide: PanelRight,
  panelFloating: AppWindow,
  calendar: Calendar,
  info: Info,
  success: CircleCheck,
  warning: TriangleAlert,
  error: CircleAlert,
  myWork: Inbox,
  // 專案總覽（側欄）：一次看多個專案的進度。
  overview: LayoutDashboard,
  // 收件匣（PM-1 協作）與關注：側欄的「收件匣」、項目的「關注」切換（不用 Inbox：那是「我的工作」）。
  notifications: Bell,
  following: BellRing,
  projects: FolderKanban,
  // 知識庫（側欄、快速跳轉、專案頁列）。
  knowledge: BookOpen,
  people: Users,
  // 寄信設定（Email 通知那一輪）：側欄的「寄信設定」。
  mail: Mail,
  // AI 設定與專案的 AI 助理。
  sparkles: Sparkles,
  members: UsersRound,
  settings: Settings,
  table: Table2,
  board: SquareKanban,
  signOut: LogOut,
  // 我的帳號與帳號的管理（帳號安全那一輪）：帳號選單的「我的帳號」、人員頁每一列的動作、密碼欄位的顯示切換。
  account: CircleUserRound,
  resetPassword: KeyRound,
  disableAccount: UserRoundX,
  enableAccount: UserRoundCheck,
  showPassword: Eye,
  hidePassword: EyeOff,
  lock: Lock,
  themeSystem: Monitor,
  themeLight: Sun,
  themeDark: Moon,
  // 編輯器的工具列（features/rich-text）：格式、區塊、連結。
  bold: Bold,
  italic: Italic,
  strike: Strikethrough,
  inlineCode: Code,
  heading: Heading2,
  bulletList: List,
  orderedList: ListOrdered,
  taskList: ListTodo,
  quote: TextQuote,
  codeBlock: SquareCode,
  link: Link,
  // 上下層（建立專案的模板預覽）：這個類型放在上一行的類型底下。
  subItem: CornerDownRight,
  // 五個狀態類別的形狀（第 4 節）：虛線圓、空心圓、半滿的圓、打勾的圓、劃掉的圓。
  stateBacklog: CircleDashed,
  stateNotStarted: Circle,
  stateInProgress: Contrast,
  stateDone: CircleCheck,
  stateCanceled: CircleSlash,
} as const satisfies Record<string, LucideIcon>;

/** 圖示的語意名稱。名稱打錯時型別檢查會擋下。 */
export type IconName = keyof typeof ICONS;

/** 所有的名稱（給 /design 展示頁與測試）。 */
export const ICON_NAMES = Object.keys(ICONS) as readonly IconName[];

// lucide 的「contrast」是外圈加半個圓的輪廓；把半個圓填滿，才是「半滿的圓」。
const HALF_FILLED: ReadonlySet<IconName> = new Set(['stateInProgress']);
// 停止鈕的方形整個填滿：一眼看得出是「停」，不是空的框。
const FILLED: ReadonlySet<IconName> = new Set(['stop']);

export interface IconProps {
  readonly name: IconName;
  readonly size?: 16 | 20;
  readonly className?: string;
}

/**
 * 裝飾用的圖示：一律 aria-hidden，意思要寫在旁邊的文字或按鈕的名稱裡。顏色跟著文字（currentColor）。
 * data-icon 是語意名稱，在開發者工具與測試裡看得出是哪一個形狀。
 */
export function Icon({ name, size = 16, className }: IconProps) {
  const Glyph = ICONS[name];
  return (
    <Glyph
      size={size}
      aria-hidden="true"
      focusable="false"
      data-icon={name}
      className={cx(styles.icon, HALF_FILLED.has(name) && styles.halfFilled, FILLED.has(name) && styles.filled, className)}
    />
  );
}
