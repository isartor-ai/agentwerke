FROM python:3.12-slim

ARG OPENSANDBOX_SERVER_VERSION=

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1
ENV SANDBOX_CONFIG_PATH=/etc/opensandbox/sandbox.toml
ENV OPENSANDBOX_INSECURE_SERVER=YES

RUN pip install --no-cache-dir --upgrade pip \
    && if [ -n "$OPENSANDBOX_SERVER_VERSION" ]; then \
        pip install --no-cache-dir "opensandbox-server==$OPENSANDBOX_SERVER_VERSION"; \
    else \
        pip install --no-cache-dir opensandbox-server; \
    fi

COPY docker/opensandbox/sandbox.toml /etc/opensandbox/sandbox.toml

EXPOSE 8080

CMD ["opensandbox-server", "--config", "/etc/opensandbox/sandbox.toml"]
