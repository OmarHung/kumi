import remarkBreaks from 'remark-breaks';
import remarkGfm from 'remark-gfm';
import { remarkCjkAutolinks } from './remarkCjkAutolinks';
import { remarkMentions } from './remarkMentions';

/**
 * 顯示 Markdown 用的 remark 外掛，順序有意義（中文標點的切斷在 GFM 的自動連結之後）。
 * 顯示（MarkdownContent）與「這段文字裡有哪些連結」（markdownLinks.ts）共用這一份：兩邊各自維護的話，
 * 一邊多認一種寫法，使用者在套用之前看到的摘要就和畫出來的連結對不上。
 */
export const REMARK_PLUGINS = [remarkGfm, remarkCjkAutolinks, remarkBreaks, remarkMentions];
