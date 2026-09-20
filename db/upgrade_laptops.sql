BEGIN;

ALTER TABLE main.products
    ADD COLUMN IF NOT EXISTS selling_price numeric(12,2);

INSERT INTO main.categories (code, name, display_order, is_required, is_enabled)
VALUES ('LAPTOP', 'Laptop', 120, false, true)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    display_order = EXCLUDED.display_order,
    is_required = EXCLUDED.is_required,
    is_enabled = EXCLUDED.is_enabled;

INSERT INTO main.source_categories (
    category_id,
    external_category_id,
    url,
    product_subtype,
    top_limit,
    is_enabled)
SELECT
    id,
    'c13',
    'https://www.toppreise.ch/top-products/Computers-accessories/Notebooks-tablets-eReaders/Notebooks-c13',
    NULL,
    100,
    true
FROM main.categories
WHERE code = 'LAPTOP'
ON CONFLICT (external_category_id) DO UPDATE SET
    category_id = EXCLUDED.category_id,
    url = EXCLUDED.url,
    product_subtype = EXCLUDED.product_subtype,
    top_limit = EXCLUDED.top_limit,
    is_enabled = EXCLUDED.is_enabled;

COMMIT;
