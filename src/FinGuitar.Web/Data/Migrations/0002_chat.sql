CREATE TABLE chat_conversations (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id    INTEGER NOT NULL,
    title      TEXT    NOT NULL,
    created_at TEXT    NOT NULL,
    updated_at TEXT    NOT NULL
);

CREATE TABLE chat_messages (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    conversation_id INTEGER NOT NULL REFERENCES chat_conversations (id) ON DELETE CASCADE,
    role            TEXT    NOT NULL CHECK (role IN ('User', 'Assistant', 'ActionResult')),
    text            TEXT    NOT NULL,
    payload_kind    TEXT,
    payload_json    TEXT,
    is_error        INTEGER NOT NULL DEFAULT 0 CHECK (is_error IN (0, 1)),
    created_at      TEXT    NOT NULL
);

CREATE TABLE proposed_actions (
    id                 TEXT    PRIMARY KEY,
    user_id            INTEGER NOT NULL,
    conversation_id    INTEGER NOT NULL REFERENCES chat_conversations (id) ON DELETE CASCADE,
    message_id         INTEGER REFERENCES chat_messages (id) ON DELETE SET NULL,
    request_message_id INTEGER NOT NULL REFERENCES chat_messages (id) ON DELETE CASCADE,
    action_type        TEXT    NOT NULL CHECK (action_type IN ('OrderStatusChange', 'ProductUpdate')),
    arguments_json     TEXT    NOT NULL,
    summary            TEXT    NOT NULL,
    is_critical        INTEGER NOT NULL CHECK (is_critical IN (0, 1)),
    critical_warning   TEXT,
    status             TEXT    NOT NULL CHECK (status IN ('Pending', 'AwaitingFinalConfirm', 'Applying', 'Confirmed', 'Cancelled', 'Expired', 'Failed')),
    created_at         TEXT    NOT NULL,
    first_confirmed_at TEXT,
    resolved_at        TEXT,
    result_text        TEXT
);

CREATE INDEX ix_chat_conversations_user ON chat_conversations (user_id, updated_at);
CREATE INDEX ix_chat_messages_conversation ON chat_messages (conversation_id, created_at);
CREATE INDEX ix_proposed_actions_conversation ON proposed_actions (conversation_id, status);
