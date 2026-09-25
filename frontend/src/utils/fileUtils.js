/**
 * Uploaded files travel and are stored as data URLs ("data:<type>;base64,<payload>").
 * A file with no content counts as "no file": it is refused when chosen and never offered for download.
 */
export const MAX_FILE_BYTES = 2 * 1024 * 1024; // 2 MB, enforced by the backend too
export const EMPTY_FILE_MESSAGE = 'The selected file is empty. Please choose a file that has content.';

export const hasFileContent = (url) => {
  if (!url || typeof url !== 'string') return false;
  if (!url.startsWith('data:')) return url.trim().length > 0;
  const comma = url.indexOf(',');
  return comma >= 0 && url.slice(comma + 1).trim().length > 0;
};

/** Reads a chosen file as `{ url, name }`; rejects empty files and files over 2 MB with a readable message. */
export const readFileAsAttachment = (file, maxBytes = MAX_FILE_BYTES) =>
  new Promise((resolve, reject) => {
    if (!file) {
      reject(new Error('No file selected.'));
      return;
    }
    if (file.size === 0) {
      reject(new Error(EMPTY_FILE_MESSAGE));
      return;
    }
    if (file.size > maxBytes) {
      reject(new Error('Files cannot exceed 2 MB.'));
      return;
    }
    const reader = new FileReader();
    reader.onload = () =>
      hasFileContent(reader.result) ? resolve({ url: reader.result, name: file.name }) : reject(new Error(EMPTY_FILE_MESSAGE));
    reader.onerror = () => reject(new Error('The file could not be read.'));
    reader.readAsDataURL(file);
  });
