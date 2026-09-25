import { Paperclip } from 'lucide-react';
import { hasFileContent } from '../../utils/fileUtils';

const LINK_CLASS =
  'inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-[11px] font-semibold bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-blue-700 dark:text-blue-300 hover:bg-blue-50 dark:hover:bg-slate-800 transition-colors max-w-full';

/**
 * One attachment slot, used everywhere files are shown: the file (opened in a preview when `onPreview` is given,
 * otherwise downloaded), or "No files uploaded" when there is no file or the file is empty.
 */
export const AttachmentLink = ({ url, name, label, onPreview }) => {
  if (!hasFileContent(url)) {
    return (
      <span className="inline-flex items-center gap-1.5 text-[11px] italic text-slate-400 dark:text-slate-500">
        <Paperclip className="w-3 h-3" /> {label ? `${label}: ` : ''}No files uploaded
      </span>
    );
  }

  const text = label ? `${label}: ${name || 'attachment'}` : name || 'Attachment';
  if (onPreview) {
    return (
      <button type="button" onClick={() => onPreview({ url, name: name || 'attachment', title: label || name })} className={LINK_CLASS}>
        <Paperclip className="w-3 h-3 shrink-0" /> <span className="truncate">{text}</span>
      </button>
    );
  }
  return (
    <a href={url} download={name || 'attachment'} className={LINK_CLASS}>
      <Paperclip className="w-3 h-3 shrink-0" /> <span className="truncate">{text}</span>
    </a>
  );
};

export default AttachmentLink;
