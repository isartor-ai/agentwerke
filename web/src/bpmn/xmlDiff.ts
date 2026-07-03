export interface DiffLine {
  lineNumber: number;
  kind: 'added' | 'removed' | 'unchanged';
  text: string;
}

/**
 * Re-indent XML so that formatting differences (bpmn-js emits unformatted
 * markup, persisted definitions may be formatted differently) don't dominate a
 * line diff. Pure text transform — never throws, returns the input on trouble.
 */
export function formatXml(xml: string): string {
  const trimmed = xml.trim();
  if (!trimmed) {
    return '';
  }
  try {
    const withBreaks = trimmed.replace(/\r\n/g, '\n').replace(/>\s*</g, '>\n<');
    const lines = withBreaks.split('\n');
    let indent = 0;
    const out: string[] = [];
    for (const raw of lines) {
      const line = raw.trim();
      if (!line) {
        continue;
      }
      const isClosing = line.startsWith('</');
      const isSelfClosing = line.endsWith('/>');
      const isDeclaration = line.startsWith('<?') || line.startsWith('<!');
      // <a>text</a> on a single line should not change indentation.
      const isInlinePair = /^<[^!?>][^>]*>.*<\/[^>]+>$/.test(line);
      const isOpening =
        line.startsWith('<') && !isClosing && !isSelfClosing && !isDeclaration && !isInlinePair;

      if (isClosing) {
        indent = Math.max(0, indent - 1);
      }
      out.push('  '.repeat(indent) + line);
      if (isOpening) {
        indent += 1;
      }
    }
    return out.join('\n');
  } catch {
    return trimmed;
  }
}

/**
 * Naive positional line diff. Kept intentionally simple — a real LCS/semantic
 * diff is tracked separately (issue #195, Option A). Callers should format both
 * inputs first so the comparison isn't swamped by whitespace differences.
 */
export function buildBpmnDiff(previousXml: string, currentXml: string): DiffLine[] {
  const previousLines = previousXml.split('\n');
  const currentLines = currentXml.split('\n');
  const maxLength = Math.max(previousLines.length, currentLines.length);
  const result: DiffLine[] = [];

  for (let index = 0; index < maxLength; index += 1) {
    const previousLine = previousLines[index] ?? null;
    const currentLine = currentLines[index] ?? null;

    if (previousLine === currentLine && currentLine !== null) {
      result.push({ lineNumber: index + 1, kind: 'unchanged', text: currentLine });
      continue;
    }
    if (previousLine !== null) {
      result.push({ lineNumber: index + 1, kind: 'removed', text: previousLine });
    }
    if (currentLine !== null) {
      result.push({ lineNumber: index + 1, kind: 'added', text: currentLine });
    }
  }

  return result;
}
