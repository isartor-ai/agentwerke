{{/*
Expand the name of the chart.
*/}}
{{- define "autofac.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
*/}}
{{- define "autofac.fullname" -}}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels
*/}}
{{- define "autofac.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}

{{/*
Selector labels for a component
*/}}
{{- define "autofac.selectorLabels" -}}
app.kubernetes.io/name: {{ include "autofac.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end }}

{{/*
Postgres connection string
*/}}
{{- define "autofac.postgresConnectionString" -}}
{{- $host := printf "%s-postgres" (include "autofac.fullname" .) }}
Host={{ $host }};Port=5432;Database={{ .Values.postgres.credentials.database }};Username={{ .Values.postgres.credentials.username }};Password=$(POSTGRES_PASSWORD)
{{- end }}

{{/*
Sandbox provider env vars, shared between the api and worker deployments.
See values.yaml `sandbox` block and ADR-003.
*/}}
{{- define "autofac.sandboxEnv" -}}
- name: Sandboxes__Provider
  value: {{ .Values.sandbox.provider | quote }}
- name: Sandboxes__OpenSandbox__Enabled
  value: {{ .Values.sandbox.openSandbox.enabled | quote }}
- name: Sandboxes__OpenSandbox__ServerUrl
  value: {{ .Values.sandbox.openSandbox.serverUrl | quote }}
- name: Sandboxes__OpenSandbox__UseServerProxy
  value: {{ .Values.sandbox.openSandbox.useServerProxy | quote }}
- name: Sandboxes__OpenSandbox__DefaultImage
  value: {{ .Values.sandbox.openSandbox.defaultImage | quote }}
- name: Sandboxes__OpenSandbox__DefaultTimeoutSeconds
  value: {{ .Values.sandbox.openSandbox.defaultTimeoutSeconds | quote }}
- name: Sandboxes__OpenSandbox__ReadinessTimeoutSeconds
  value: {{ .Values.sandbox.openSandbox.readinessTimeoutSeconds | quote }}
{{- if .Values.sandbox.openSandbox.enabled }}
- name: Sandboxes__OpenSandbox__ApiKey
  valueFrom:
    secretKeyRef:
      name: {{ .Values.secretName }}
      key: OPEN_SANDBOX_API_KEY
      optional: true
{{- end }}
{{- end }}
