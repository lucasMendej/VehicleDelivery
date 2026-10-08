-- Eenmalige SQLite-initialisatie voor de afleverlijst.
-- Dit script maakt alleen tabellen en indexen aan als die nog niet bestaan.
-- Ruwe source_* tabellen worden door de ODBC-load aangemaakt.

BEGIN TRANSACTION;

CREATE TABLE IF NOT EXISTS delivery_records (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    set_id INTEGER NOT NULL,
    vsb_number TEXT NOT NULL,
    chassis TEXT NOT NULL DEFAULT '',
    commission_number TEXT NOT NULL DEFAULT '',
    license_plate TEXT,
    brand TEXT NOT NULL DEFAULT '',
    vehicle_type TEXT NOT NULL DEFAULT '',
    model TEXT NOT NULL DEFAULT '',
    salesperson TEXT NOT NULL DEFAULT '',
    salesperson_name TEXT NOT NULL DEFAULT '',
    sales_location TEXT NOT NULL DEFAULT '',
    sales_location_name TEXT NOT NULL DEFAULT '',
    prep_location TEXT NOT NULL DEFAULT '',
    prep_location_name TEXT NOT NULL DEFAULT '',
    customer_name TEXT NOT NULL DEFAULT '',
    invoice_name TEXT NOT NULL DEFAULT '',
    expected_arrival TEXT,
    actual_arrival TEXT,
    delivery_date TEXT,
    delivery_status TEXT NOT NULL DEFAULT '',
    workshop_rules INTEGER NOT NULL DEFAULT 0,
    workshop_forwarded INTEGER NOT NULL DEFAULT 0,
    workshop_on_order INTEGER NOT NULL DEFAULT 0,
    workshop_invoiced INTEGER NOT NULL DEFAULT 0,
    workshop_tip TEXT NOT NULL DEFAULT '',
    workshop_locations TEXT NOT NULL DEFAULT '',
    workshop_orders TEXT NOT NULL DEFAULT '',
    order_notes TEXT NOT NULL DEFAULT '',
    selected INTEGER NOT NULL DEFAULT 0,
    polished INTEGER NOT NULL DEFAULT 0,
    user_note TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS delivery_record_fields (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    activestate_1 INTEGER NOT NULL DEFAULT 0,
    activestate_2 INTEGER NOT NULL DEFAULT 0,
    text TEXT NOT NULL DEFAULT '',
    _set INTEGER NOT NULL,
    vtype TEXT NOT NULL,
    vehicle TEXT NOT NULL,
    UNIQUE (_set, vtype, vehicle)
);

CREATE TABLE IF NOT EXISTS delivery_polished_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    _set INTEGER NOT NULL,
    vtype TEXT NOT NULL,
    vehicle TEXT NOT NULL,
    selected INTEGER NOT NULL,
    user_name TEXT NOT NULL,
    changed_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS delivery_ready_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    _set INTEGER NOT NULL,
    vtype TEXT NOT NULL,
    vehicle TEXT NOT NULL,
    selected INTEGER NOT NULL,
    user_name TEXT NOT NULL,
    changed_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_delivery_records_set_vehicle_type
    ON delivery_records (set_id, vsb_number, vehicle_type);

CREATE INDEX IF NOT EXISTS ix_delivery_records_set_delivery
    ON delivery_records (set_id, delivery_date, vsb_number);

CREATE INDEX IF NOT EXISTS ix_delivery_records_set_status
    ON delivery_records (set_id, delivery_status);

CREATE INDEX IF NOT EXISTS ix_delivery_records_set_salesperson
    ON delivery_records (set_id, salesperson);

CREATE INDEX IF NOT EXISTS ix_delivery_records_set_location
    ON delivery_records (set_id, sales_location, prep_location);

CREATE INDEX IF NOT EXISTS ix_delivery_record_fields_lookup
    ON delivery_record_fields (_set, vtype, vehicle);

CREATE INDEX IF NOT EXISTS ix_delivery_ready_log_lookup
    ON delivery_ready_log (_set, vtype, vehicle, id);

CREATE INDEX IF NOT EXISTS ix_delivery_polished_log_lookup
    ON delivery_polished_log (_set, vtype, vehicle, id);

COMMIT;
