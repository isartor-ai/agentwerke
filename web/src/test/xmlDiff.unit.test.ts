import { describe, expect, it } from 'vitest';
import { buildBpmnDiff, formatXml } from '../bpmn/xmlDiff';

describe('formatXml', () => {
  it('returns an empty string for blank input', () => {
    expect(formatXml('')).toBe('');
    expect(formatXml('   \n  ')).toBe('');
  });

  it('re-indents a single-line document into a nested tree', () => {
    const formatted = formatXml('<a><b><c>text</c></b></a>');
    expect(formatted).toBe(['<a>', '  <b>', '    <c>text</c>', '  </b>', '</a>'].join('\n'));
  });

  it('normalizes differing whitespace to an identical result', () => {
    const compact = '<a><b id="1"/></a>';
    const spaced = '<a>\n  <b id="1"/>\n</a>\n';
    expect(formatXml(compact)).toBe(formatXml(spaced));
  });

  it('keeps declarations and self-closing tags at the same depth', () => {
    const formatted = formatXml('<?xml version="1.0"?><root><item id="1"/><item id="2"/></root>');
    expect(formatted).toBe(
      ['<?xml version="1.0"?>', '<root>', '  <item id="1"/>', '  <item id="2"/>', '</root>'].join('\n'),
    );
  });
});

describe('buildBpmnDiff', () => {
  it('marks identical documents entirely unchanged', () => {
    const xml = formatXml('<a><b/></a>');
    const diff = buildBpmnDiff(xml, xml);
    expect(diff.every((line) => line.kind === 'unchanged')).toBe(true);
  });

  it('reports added and removed lines for a formatted change', () => {
    const previous = formatXml('<a><b/></a>');
    const next = formatXml('<a><b/><c/></a>');
    const diff = buildBpmnDiff(previous, next);
    const kinds = diff.map((line) => line.kind);
    expect(kinds).toContain('added');
    expect(diff.some((line) => line.kind === 'added' && line.text.includes('<c/>'))).toBe(true);
  });
});
