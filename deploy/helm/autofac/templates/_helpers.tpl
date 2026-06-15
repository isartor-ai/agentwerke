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
