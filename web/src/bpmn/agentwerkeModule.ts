import { AgentwerkePaletteModule } from './agentwerkePaletteProvider';
import { AgentwerkeMarkersModule } from './agentwerkeMarkers';
import { AgentwerkePropertiesProviderModule } from './properties/agentwerkePropertiesProvider';

/**
 * The full set of Agentwerke diagram-js modules: custom palette entries, extension
 * markers, and the properties-panel provider. Passed to the modeler via
 * `additionalModules`.
 */
export const agentwerkeModules = [
  AgentwerkePaletteModule,
  AgentwerkeMarkersModule,
  AgentwerkePropertiesProviderModule,
];
