import type { AssistantAttachment } from './aiTypes';
import { uploadFile, type UploadOptions } from './attachments';
import { apiRequest } from './http';

const MIB = 1024 * 1024;
const KIB = 1024;

/** 伺服器的 AssistantAttachmentRules：前端先擋，不必傳完才被退回（伺服器照樣擋）。 */
export const ASSISTANT_ATTACHMENT_LIMITS = {
  /** 一則訊息最多幾個。 */
  maxCount: 5,
  /** 一則訊息的附件合計。 */
  maxTotalBytes: 15 * MIB,
  maxImageBytes: 5 * MIB,
  maxPdfBytes: 10 * MIB,
  maxTextBytes: 256 * KIB,
} as const;

const TOO_LARGE_MESSAGE = '檔案太大，無法上傳。';

const attachmentsPath = (projectKey: string) => `/v1/projects/${encodeURIComponent(projectKey)}/assistant/attachments`;

/**
 * 上傳一個待送的附件（multipart，欄位 file）。失敗：413 `ai.attachment_too_large`、422 `ai.attachment_type_not_allowed`、
 * 422 `ai.attachment_name_invalid`、409 `ai.too_many_attachments`、409 `ai.attachment_quota_exceeded`／`ai.attachment_daily_quota_exceeded`。
 */
export function uploadAssistantAttachment(projectKey: string, file: File, options: UploadOptions = {}): Promise<AssistantAttachment> {
  return uploadFile<AssistantAttachment>(attachmentsPath(projectKey), file, { tooLargeMessage: TOO_LARGE_MESSAGE, ...options });
}

/** 移除還沒送出的附件。404 `ai.attachment_not_found`（別人的、已經送出的、不存在的）。 */
export function deleteAssistantAttachment(projectKey: string, attachmentId: string): Promise<null> {
  return apiRequest<null>(`${attachmentsPath(projectKey)}/${encodeURIComponent(attachmentId)}`, { method: 'DELETE' });
}

/** 下載的網址（同網域，Cookie 照常帶）：四種點陣圖直接顯示，其他由伺服器標成下載。 */
export function assistantAttachmentUrl(projectKey: string, attachmentId: string): string {
  return `${attachmentsPath(projectKey)}/${encodeURIComponent(attachmentId)}/content`;
}
