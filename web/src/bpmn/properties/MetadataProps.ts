import { html } from 'htm/preact';
import { TextFieldEntry, isTextFieldEntryEdited, ListGroup } from '@bpmn-io/properties-panel';
import { useService } from 'bpmn-js-properties-panel';
import {
  getMetadataItems,
  addMetadataItem,
  removeMetadataItem,
  updateMetadataItem,
} from './extensionUtil';

/* eslint-disable @typescript-eslint/no-explicit-any */

/** A key or value text entry bound to a single `<agentwerke:metadata>` row. */
function metadataField(item: any, property: 'key' | 'value', label: string, placeholder: string) {
  return function MetadataField(props: any) {
    const { element, id } = props;
    const modeling = useService('modeling');
    const translate = useService('translate');
    const debounce = useService('debounceInput');

    const getValue = () => (item.get ? item.get(property) : item[property]) ?? '';
    const setValue = (value: string) =>
      updateMetadataItem(element, item, property, value ?? '', { modeling });

    return html`<${TextFieldEntry}
      id=${id}
      element=${element}
      label=${translate(label)}
      getValue=${getValue}
      setValue=${setValue}
      debounce=${debounce}
      placeholder=${placeholder}
    />`;
  };
}

function metadataItemEntries(item: any, index: number) {
  return [
    {
      id: `agentwerke-metadata-${index}-key`,
      component: metadataField(item, 'key', 'Key', 'e.g. phase or traceability.produces'),
      isEdited: isTextFieldEntryEdited,
    },
    {
      id: `agentwerke-metadata-${index}-value`,
      component: metadataField(item, 'value', 'Value', 'e.g. requirements_baseline'),
      isEdited: isTextFieldEntryEdited,
    },
  ];
}

/**
 * Compact key/value table for `agentwerke:metadata` on an agent task (rec #3 of
 * docs/evaluations/v-model-process-test.md): each row edits one metadata element, with
 * add/remove. Requires the Metadata type modeled in agentwerkeModdle so edits round-trip.
 */
export function metadataGroup(element: any, injector: any) {
  const translate = injector.get('translate');
  const modeling = injector.get('modeling');
  const moddle = injector.get('moddle');

  const items = getMetadataItems(element).map((item: any, index: number) => ({
    id: `agentwerke-metadata-${index}`,
    label: (item.get ? item.get('key') : item.key) || translate('New metadata entry'),
    entries: metadataItemEntries(item, index),
    autoFocusEntry: `agentwerke-metadata-${index}-key`,
    remove: () => removeMetadataItem(element, item, { modeling }),
  }));

  return {
    id: 'agentwerkeMetadata',
    label: translate('Agentwerke — Metadata'),
    component: ListGroup,
    items,
    add: (event: Event) => {
      event.stopPropagation();
      addMetadataItem(element, { modeling, moddle });
    },
  };
}
