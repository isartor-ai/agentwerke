import { AutofacPaletteModule } from './autofacPaletteProvider';
import { AutofacMarkersModule } from './autofacMarkers';
import { AutofacPropertiesProviderModule } from './properties/autofacPropertiesProvider';

/**
 * The full set of Autofac diagram-js modules: custom palette entries, extension
 * markers, and the properties-panel provider. Passed to the modeler via
 * `additionalModules`.
 */
export const autofacModules = [
  AutofacPaletteModule,
  AutofacMarkersModule,
  AutofacPropertiesProviderModule,
];
