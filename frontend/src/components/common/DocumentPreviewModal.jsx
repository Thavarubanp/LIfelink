import React from 'react';
import { X, ExternalLink, Download, FileText } from 'lucide-react';

export const DocumentPreviewModal = ({ isOpen, onClose, title, documentUrl, documentName }) => {
  if (!isOpen) return null;

  const isPdf = documentUrl?.startsWith('data:application/pdf') || documentUrl?.endsWith('.pdf');
  const isImage = documentUrl?.startsWith('data:image/') || /\.(png|jpe?g|webp|gif)$/i.test(documentUrl || '');

  const handleDownload = () => {
    if (!documentUrl) return;
    const a = document.createElement('a');
    a.href = documentUrl;
    a.download = documentName || 'Document';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm animate-in fade-in">
      <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-3xl overflow-hidden shadow-2xl flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className="p-4 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <FileText className="w-5 h-5 text-cyan-400" />
            <h3 className="font-bold text-slate-100 text-sm">{title || 'Document Preview'}</h3>
            {documentName && (
              <span className="text-xs text-slate-400 font-mono">({documentName})</span>
            )}
          </div>
          <div className="flex items-center gap-2">
            {documentUrl && (
              <button
                type="button"
                onClick={handleDownload}
                className="px-2.5 py-1 bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs rounded-lg border border-slate-700 flex items-center gap-1.5 transition-colors"
                title="Download Document"
              >
                <Download className="w-3.5 h-3.5" />
                <span>Download</span>
              </button>
            )}
            <button
              type="button"
              onClick={onClose}
              className="p-1 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition-colors"
            >
              <X className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Content Viewer */}
        <div className="p-4 overflow-y-auto flex-1 flex items-center justify-center bg-slate-950/50 min-h-[350px]">
          {isPdf ? (
            <iframe
              src={documentUrl}
              title={title}
              className="w-full h-[550px] rounded-lg border border-slate-800"
            />
          ) : isImage ? (
            <img
              src={documentUrl}
              alt={title}
              className="max-h-[550px] max-w-full rounded-lg object-contain border border-slate-800 shadow-md"
            />
          ) : documentUrl ? (
            <div className="text-center p-8 space-y-4">
              <FileText className="w-16 h-16 text-cyan-400/80 mx-auto animate-pulse" />
              <div>
                <p className="text-sm font-semibold text-slate-200">{documentName || 'Document File'}</p>
                <p className="text-xs text-slate-400 mt-1">This document can be downloaded and opened locally.</p>
              </div>
              <button
                type="button"
                onClick={handleDownload}
                className="px-4 py-2 bg-cyan-600 hover:bg-cyan-500 text-white font-semibold text-xs rounded-xl shadow-lg transition-colors inline-flex items-center gap-2"
              >
                <Download className="w-4 h-4" /> Download File
              </button>
            </div>
          ) : (
            <div className="text-center p-8 text-slate-400 text-xs">
              <FileText className="w-12 h-12 text-slate-600 mx-auto mb-2" />
              <p>No document attachment provided.</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default DocumentPreviewModal;
