import { useMemo, useState } from 'react';
import { buildBpmnDiff, formatXml } from '../bpmn/xmlDiff';

export interface WorkflowDiffModalProps {
  currentXml: string;
  publishedXml: string;
  fileName: string;
  onClose: () => void;
}

type DiffTab = 'changes' | 'preview';

export function WorkflowDiffModal({ currentXml, publishedXml, fileName, onClose }: WorkflowDiffModalProps) {
  const [tab, setTab] = useState<DiffTab>('changes');
  const [copied, setCopied] = useState(false);

  const formattedCurrent = useMemo(() => formatXml(currentXml), [currentXml]);
  const formattedPublished = useMemo(() => formatXml(publishedXml), [publishedXml]);
  const diff = useMemo(
    () => buildBpmnDiff(formattedPublished, formattedCurrent),
    [formattedPublished, formattedCurrent],
  );
  const changedCount = useMemo(() => diff.filter((line) => line.kind !== 'unchanged').length, [diff]);

  const onBackdrop = (event: React.MouseEvent<HTMLDivElement>) => {
    if (event.target === event.currentTarget) {
      onClose();
    }
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      onClose();
    }
  };

  const onCopy = async () => {
    try {
      await navigator.clipboard.writeText(formattedCurrent);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard unavailable (e.g. non-secure context) — ignore.
    }
  };

  const onDownload = () => {
    const blob = new Blob([formattedCurrent], { type: 'application/xml;charset=utf-8' });
    const objectUrl = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = fileName.endsWith('.bpmn') ? fileName : `${fileName}.bpmn`;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(objectUrl);
  };

  const onOpenInNewTab = () => {
    const blob = new Blob([formattedCurrent], { type: 'application/xml;charset=utf-8' });
    const objectUrl = URL.createObjectURL(blob);
    window.open(objectUrl, '_blank', 'noopener,noreferrer');
    // Give the new tab a tick to grab the URL before revoking.
    setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
  };

  return (
    <div
      className="diff-modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-label="Workflow changes"
      onClick={onBackdrop}
      onKeyDown={onKeyDown}
    >
      <div className="diff-modal workflow-diff-modal">
        <div className="diff-modal-header">
          <h2>Workflow Changes</h2>
          <button type="button" className="btn-icon" aria-label="Close changes" onClick={onClose}>
            ×
          </button>
        </div>

        <div className="workflow-diff-tabs" role="tablist" aria-label="Workflow change view">
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'changes'}
            className={`tab ${tab === 'changes' ? 'tab-active' : ''}`}
            onClick={() => setTab('changes')}
          >
            Diff
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'preview'}
            className={`tab ${tab === 'preview' ? 'tab-active' : ''}`}
            onClick={() => setTab('preview')}
          >
            Current XML
          </button>
        </div>

        <div className="diff-modal-body workflow-diff-body">
          {tab === 'changes' ? (
            changedCount > 0 ? (
              <div className="workflow-diff-scroll" role="region" aria-label="BPMN line diff" tabIndex={0}>
                {diff.map((entry, index) => (
                  <div
                    key={`${entry.lineNumber}-${entry.kind}-${index}`}
                    className={`workflow-diff-row workflow-diff-row-${entry.kind}`}
                  >
                    <span className="workflow-diff-lineno" aria-hidden="true">
                      {entry.lineNumber}
                    </span>
                    <span className="workflow-diff-glyph" aria-hidden="true">
                      {entry.kind === 'added' ? '+' : entry.kind === 'removed' ? '-' : ' '}
                    </span>
                    <code>{entry.text || ' '}</code>
                  </div>
                ))}
              </div>
            ) : (
              <p className="workflow-diff-empty">
                No line differences after normalizing formatting — the canvas matches the published version.
              </p>
            )
          ) : (
            <div className="workflow-xml-preview" role="region" aria-label="Current BPMN XML" tabIndex={0}>
              <pre>
                <code>{formattedCurrent || '<!-- empty diagram -->'}</code>
              </pre>
            </div>
          )}
        </div>

        <div className="diff-modal-footer workflow-diff-footer">
          <div className="workflow-diff-actions">
            <button type="button" className="btn btn-secondary" onClick={() => void onCopy()}>
              {copied ? 'Copied' : 'Copy XML'}
            </button>
            <button type="button" className="btn btn-secondary" onClick={onDownload}>
              Download .bpmn
            </button>
            <button type="button" className="btn btn-secondary" onClick={onOpenInNewTab}>
              Open in new tab
            </button>
          </div>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
