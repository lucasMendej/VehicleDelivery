-- Eenmalige migratie voor bestaande SQLite-databases.
-- Nieuwe installaties gebruiken invoice_name direct.
-- Voer dit alleen uit als delivery_records nog customer_internal_name bevat.

ALTER TABLE delivery_records
    RENAME COLUMN customer_internal_name TO invoice_name;
