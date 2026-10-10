CREATE TABLE products (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT    NOT NULL,
    brand       TEXT    NOT NULL,
    category    TEXT    NOT NULL CHECK (category IN ('Electric', 'Acoustic', 'Bass', 'Classical')),
    price_cents INTEGER NOT NULL CHECK (price_cents > 0),
    stock       INTEGER NOT NULL CHECK (stock >= 0),
    image_file  TEXT
);

CREATE TABLE customers (
    id      INTEGER PRIMARY KEY AUTOINCREMENT,
    name    TEXT NOT NULL,
    email   TEXT NOT NULL,
    phone   TEXT NOT NULL,
    address TEXT NOT NULL
);

CREATE TABLE orders (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    customer_id INTEGER NOT NULL REFERENCES customers (id),
    order_date  TEXT    NOT NULL,
    status      TEXT    NOT NULL CHECK (status IN ('Pending', 'Dispatched', 'Delivered', 'Cancelled')),
    total_cents INTEGER NOT NULL CHECK (total_cents >= 0)
);

CREATE TABLE order_lines (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    order_id         INTEGER NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
    product_id       INTEGER NOT NULL REFERENCES products (id),
    quantity         INTEGER NOT NULL CHECK (quantity > 0),
    unit_price_cents INTEGER NOT NULL CHECK (unit_price_cents > 0)
);

CREATE TABLE admin_users (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    email           TEXT    NOT NULL COLLATE NOCASE,
    password_hash   TEXT    NOT NULL,
    role            TEXT    NOT NULL CHECK (role IN ('Level1', 'Level2')),
    failed_attempts INTEGER NOT NULL DEFAULT 0,
    lockout_until   TEXT
);

-- Every column is here from the start, including the chat origin columns, because a real
-- audit design plans for more than one origin. The chat never alters this table.
CREATE TABLE audit_entries (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    occurred_at        TEXT    NOT NULL,
    user_id            INTEGER NOT NULL,
    user_email         TEXT    NOT NULL,
    user_role          TEXT    NOT NULL,
    source             TEXT    NOT NULL CHECK (source IN ('AdminPanel', 'Chat')),
    entity_type        TEXT    NOT NULL CHECK (entity_type IN ('Order', 'Product')),
    entity_id          INTEGER NOT NULL,
    action             TEXT    NOT NULL CHECK (action IN ('OrderStatusChanged', 'ProductCreated', 'ProductUpdated')),
    summary            TEXT    NOT NULL,
    changes_json       TEXT    NOT NULL,
    conversation_id    INTEGER,
    chat_message_id    INTEGER,
    proposed_action_id TEXT,
    chat_request_text  TEXT,
    confirmation_count INTEGER CHECK (confirmation_count IN (1, 2))
);

CREATE INDEX ix_orders_order_date ON orders (order_date);
CREATE INDEX ix_orders_status ON orders (status);
CREATE INDEX ix_order_lines_order ON order_lines (order_id);
CREATE UNIQUE INDEX ux_admin_users_email ON admin_users (email COLLATE NOCASE);
CREATE INDEX ix_audit_occurred_at ON audit_entries (occurred_at);
CREATE INDEX ix_audit_entity ON audit_entries (entity_type, entity_id);

CREATE TRIGGER audit_entries_no_update BEFORE UPDATE ON audit_entries
BEGIN
    SELECT RAISE(ABORT, 'audit entries are append-only');
END;

CREATE TRIGGER audit_entries_no_delete BEFORE DELETE ON audit_entries
BEGIN
    SELECT RAISE(ABORT, 'audit entries are append-only');
END;
