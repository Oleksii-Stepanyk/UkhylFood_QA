FROM rabbitmq:3-management-alpine

RUN apk add --no-cache curl \
    && curl -L -o /opt/rabbitmq/plugins/rabbitmq_delayed_message_exchange-3.13.0.ez https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v3.13.0/rabbitmq_delayed_message_exchange-3.13.0.ez \
    && rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange \
    && apk del curl
