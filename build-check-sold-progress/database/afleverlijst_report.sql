-- Afleverlijst SQLite rapportage-opbouw
--
-- Dit is een SQL-template. Vervang __SET_ID__ vóór uitvoering door de
-- waarde van de URL-parameter _set (1 of 3). Bijvoorbeeld:
--   __SET_ID__ = 3
-- waarna de brontabel source_3_vehicles_initial wordt gebruikt.
--
-- De webapp voert dezelfde set-parameterisatie automatisch uit vanuit
-- LoadCoordinator.ProcessSetAsync(_set). Dit losse bestand is bedoeld voor
-- handmatige SQLite-controle en moet eerst worden ingevuld.
-- Bestaande databases met customer_internal_name kunnen eenmalig worden
-- gemigreerd met database/migrate_internal_name_to_invoice_name.sql.

BEGIN TRANSACTION;

CREATE TABLE IF NOT EXISTS delivery_records (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    set_id INTEGER NOT NULL,
    vsb_number TEXT NOT NULL,
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

CREATE INDEX IF NOT EXISTS ix_delivery_records_set_delivery
    ON delivery_records (set_id, delivery_date, vsb_number);

CREATE INDEX IF NOT EXISTS ix_delivery_records_set_vehicle
    ON delivery_records (set_id, vsb_number);

CREATE INDEX IF NOT EXISTS ix_delivery_records_set_status
    ON delivery_records (set_id, delivery_status);

CREATE INDEX IF NOT EXISTS ix_delivery_records_set_salesperson
    ON delivery_records (set_id, salesperson);

CREATE INDEX IF NOT EXISTS ix_delivery_records_set_location
    ON delivery_records (set_id, sales_location, prep_location);

-- Werk de actuele rapportageregels voor deze set bij. Oude voertuigen blijven behouden.

INSERT INTO delivery_records (
    set_id, vsb_number, commission_number, license_plate, brand,
    vehicle_type, model, salesperson, sales_location, prep_location,
    customer_name, expected_arrival, actual_arrival, delivery_date,
    delivery_status, order_notes
)
SELECT DISTINCT
    __SET_ID__,
    CAST(json_extract(payload_json, '$.VEHICLE') AS TEXT),
    '',
    NULLIF(CAST(json_extract(payload_json, '$.REGN') AS TEXT), ''),
    '',
    CASE WHEN lower(selection_name) LIKE '%used%' THEN 'U' ELSE 'N' END,
    COALESCE(CAST(json_extract(payload_json, '$.DESC_') AS TEXT), ''),
    COALESCE(CAST(json_extract(payload_json, '$.exec001') AS TEXT), ''),
    COALESCE(CAST(json_extract(payload_json, '$.SELLLOCN') AS TEXT), ''),
    COALESCE(CAST(json_extract(payload_json, '$.BUYLOCN') AS TEXT), ''),
    COALESCE(CAST(json_extract(payload_json, '$.NAME_') AS TEXT), ''),
    CASE WHEN lower(selection_name) LIKE '%used%'
         THEN CAST(json_extract(payload_json, '$.SALEDATE') AS TEXT)
         ELSE CAST(json_extract(payload_json, '$.RECDATEE') AS TEXT) END,
    CASE WHEN lower(selection_name) LIKE '%used%'
         THEN CAST(json_extract(payload_json, '$.SINVDATE') AS TEXT)
         ELSE CAST(json_extract(payload_json, '$.RECDATE') AS TEXT) END,
    CASE WHEN lower(selection_name) LIKE '%used%'
         THEN CAST(json_extract(payload_json, '$.DELDATE') AS TEXT)
         ELSE CAST(json_extract(payload_json, '$.DELDATEE') AS TEXT) END,
    '',
    ''
FROM source___SET_ID___vehicles_initial
WHERE json_extract(payload_json, '$.VEHICLE') IS NOT NULL
ON CONFLICT(set_id, vsb_number, vehicle_type) DO UPDATE SET
    license_plate = excluded.license_plate,
    model = excluded.model,
    salesperson = excluded.salesperson,
    sales_location = excluded.sales_location,
    prep_location = excluded.prep_location,
    customer_name = excluded.customer_name,
    expected_arrival = excluded.expected_arrival,
    actual_arrival = excluded.actual_arrival,
    delivery_date = excluded.delivery_date;

COMMIT;

-- Controle:
-- SELECT vehicle_type, COUNT(*) FROM delivery_records WHERE set_id = __SET_ID__ GROUP BY vehicle_type;
-- SELECT * FROM delivery_records WHERE set_id = __SET_ID__ ORDER BY delivery_date, vsb_number LIMIT 20;
