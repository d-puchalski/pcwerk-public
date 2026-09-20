-- PCWerk MVP database. This script intentionally replaces the BuildCores schema.
-- The catalog is relational and typed; there are no JSON/JSONB columns.

DROP SCHEMA IF EXISTS main CASCADE;
CREATE SCHEMA main;

CREATE TABLE main.categories (
    id smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code varchar(32) NOT NULL UNIQUE,
    name varchar(100) NOT NULL,
    display_order integer NOT NULL,
    is_required boolean NOT NULL,
    is_enabled boolean NOT NULL DEFAULT true
);

CREATE TABLE main.source_categories (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    category_id smallint NOT NULL REFERENCES main.categories(id),
    external_category_id varchar(32) NOT NULL UNIQUE,
    url varchar(1000) NOT NULL,
    product_subtype varchar(32),
    top_limit integer NOT NULL DEFAULT 100 CHECK (top_limit BETWEEN 1 AND 100),
    is_enabled boolean NOT NULL DEFAULT true
);

CREATE TABLE main.import_runs (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    started_at timestamptz NOT NULL,
    completed_at timestamptz,
    status varchar(24) NOT NULL CHECK (status IN ('running', 'completed', 'partial', 'failed')),
    parser_version varchar(40) NOT NULL,
    category_count integer NOT NULL DEFAULT 0,
    product_count integer NOT NULL DEFAULT 0,
    offer_count integer NOT NULL DEFAULT 0,
    error_count integer NOT NULL DEFAULT 0,
    error_message text
);

CREATE TABLE main.products (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    category_id smallint NOT NULL REFERENCES main.categories(id),
    source_category_id integer NOT NULL REFERENCES main.source_categories(id),
    toppreise_product_id bigint NOT NULL UNIQUE,
    name varchar(500) NOT NULL,
    manufacturer varchar(120),
    manufacturer_part_number varchar(160),
    ean varchar(32),
    source_url varchar(1000) NOT NULL,
    image_url varchar(1000),
    selling_price numeric(12,2),
    lifecycle_status varchar(24) NOT NULL CHECK (lifecycle_status IN ('active', 'retired', 'rejected')),
    specification_status varchar(24) NOT NULL CHECK (specification_status IN ('pending', 'valid', 'incomplete', 'invalid')),
    is_visible boolean NOT NULL DEFAULT false,
    current_rank integer NOT NULL CHECK (current_rank BETWEEN 0 AND 100),
    ranked_at timestamptz,
    first_seen_at timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL,
    specifications_observed_at timestamptz,
    updated_at timestamptz NOT NULL,
    last_seen_import_run_id bigint REFERENCES main.import_runs(id) ON DELETE SET NULL
);
CREATE INDEX ix_products_catalog ON main.products(category_id, is_visible, current_rank);
CREATE INDEX ix_products_mpn ON main.products(manufacturer_part_number);

CREATE TABLE main.pc_presets (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    slug varchar(120) NOT NULL UNIQUE,
    name varchar(160) NOT NULL,
    description_de varchar(1000) NOT NULL,
    description_en varchar(1000) NOT NULL,
    description_it varchar(1000) NOT NULL,
    badge_de varchar(80),
    badge_en varchar(80),
    badge_it varchar(80),
    image_url varchar(1000),
    display_order integer NOT NULL DEFAULT 0,
    is_enabled boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_pc_presets_enabled_order ON main.pc_presets(is_enabled, display_order);

CREATE TABLE main.pc_preset_products (
    pc_preset_id bigint NOT NULL REFERENCES main.pc_presets(id) ON DELETE CASCADE,
    product_id bigint NOT NULL REFERENCES main.products(id) ON DELETE CASCADE,
    quantity smallint NOT NULL DEFAULT 1 CHECK (quantity > 0),
    display_order integer NOT NULL DEFAULT 0,
    PRIMARY KEY (pc_preset_id, product_id)
);
CREATE INDEX ix_pc_preset_products_product ON main.pc_preset_products(product_id);

CREATE TABLE main.ranking_snapshots (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source_category_id integer NOT NULL REFERENCES main.source_categories(id),
    import_run_id bigint NOT NULL REFERENCES main.import_runs(id),
    observed_at timestamptz NOT NULL,
    item_count integer NOT NULL CHECK (item_count BETWEEN 0 AND 100)
);
CREATE INDEX ix_ranking_snapshots_category_time ON main.ranking_snapshots(source_category_id, observed_at DESC);

CREATE TABLE main.ranking_items (
    ranking_snapshot_id bigint NOT NULL REFERENCES main.ranking_snapshots(id) ON DELETE CASCADE,
    rank integer NOT NULL CHECK (rank BETWEEN 1 AND 100),
    product_id bigint NOT NULL REFERENCES main.products(id),
    listed_product_price numeric(12,2),
    listed_total_price numeric(12,2),
    currency varchar(3) NOT NULL DEFAULT 'CHF',
    PRIMARY KEY (ranking_snapshot_id, rank),
    UNIQUE (ranking_snapshot_id, product_id)
);

CREATE TABLE main.import_errors (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    import_run_id bigint NOT NULL REFERENCES main.import_runs(id) ON DELETE CASCADE,
    stage varchar(40) NOT NULL,
    source_url varchar(1000),
    message varchar(4000) NOT NULL,
    occurred_at timestamptz NOT NULL
);

CREATE TABLE main.cpu_specs (
    product_id bigint PRIMARY KEY REFERENCES main.products(id) ON DELETE CASCADE,
    socket varchar(40) NOT NULL,
    generation varchar(80),
    cores integer,
    threads integer,
    base_clock_ghz numeric(6,2),
    boost_clock_ghz numeric(6,2),
    tdp_watts integer,
    has_integrated_graphics boolean
);

CREATE TABLE main.motherboard_specs (
    product_id bigint PRIMARY KEY REFERENCES main.products(id) ON DELETE CASCADE,
    socket varchar(40) NOT NULL,
    chipset varchar(80),
    form_factor varchar(40) NOT NULL,
    memory_type varchar(20) NOT NULL,
    memory_slots integer,
    maximum_memory_gb integer,
    m2_slot_count integer,
    sata_port_count integer,
    supports_ecc boolean
);
CREATE TABLE main.motherboard_cpu_generations (
    product_id bigint NOT NULL REFERENCES main.motherboard_specs(product_id) ON DELETE CASCADE,
    generation varchar(80) NOT NULL,
    PRIMARY KEY (product_id, generation)
);
CREATE TABLE main.motherboard_video_outputs (
    product_id bigint NOT NULL REFERENCES main.motherboard_specs(product_id) ON DELETE CASCADE,
    output_type varchar(24) NOT NULL,
    version varchar(16) NOT NULL DEFAULT '',
    quantity integer NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (product_id, output_type, version)
);

CREATE TABLE main.ram_specs (
    product_id bigint PRIMARY KEY REFERENCES main.products(id) ON DELETE CASCADE,
    memory_type varchar(20) NOT NULL,
    module_form_factor varchar(20),
    capacity_gb integer NOT NULL,
    module_count integer NOT NULL,
    speed_mt_per_second integer,
    is_ecc boolean,
    is_registered boolean,
    height_mm numeric(8,2)
);

CREATE TABLE main.gpu_specs (
    product_id bigint PRIMARY KEY REFERENCES main.products(id) ON DELETE CASCADE,
    chipset varchar(120),
    memory_gb integer,
    memory_type varchar(20),
    tdp_watts integer,
    recommended_psu_watts integer,
    length_mm numeric(8,2) NOT NULL,
    height_mm numeric(8,2),
    slot_width numeric(4,2)
);
CREATE TABLE main.gpu_power_connectors (
    product_id bigint NOT NULL REFERENCES main.gpu_specs(product_id) ON DELETE CASCADE,
    connector_type varchar(32) NOT NULL,
    quantity integer NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (product_id, connector_type)
);

CREATE TABLE main.storage_specs (
    product_id bigint PRIMARY KEY REFERENCES main.products(id) ON DELETE CASCADE,
    storage_type varchar(24) NOT NULL,
    interface_type varchar(40) NOT NULL,
    protocol varchar(24),
    form_factor varchar(24),
    capacity_gb integer NOT NULL,
    m2_length_mm integer
);

CREATE TABLE main.cooler_specs (
    product_id bigint PRIMARY KEY REFERENCES main.products(id) ON DELETE CASCADE,
    cooler_type varchar(20) NOT NULL CHECK (cooler_type IN ('air', 'aio')),
    height_mm numeric(8,2),
    radiator_size_mm integer,
    tdp_capacity_watts integer
);
CREATE TABLE main.cooler_sockets (
    product_id bigint NOT NULL REFERENCES main.cooler_specs(product_id) ON DELETE CASCADE,
    socket varchar(40) NOT NULL,
    PRIMARY KEY (product_id, socket)
);

CREATE TABLE main.psu_specs (
    product_id bigint PRIMARY KEY REFERENCES main.products(id) ON DELETE CASCADE,
    wattage integer NOT NULL CHECK (wattage > 0),
    form_factor varchar(24) NOT NULL,
    length_mm numeric(8,2),
    efficiency_rating varchar(40),
    atx_standard varchar(24)
);
CREATE TABLE main.psu_power_connectors (
    product_id bigint NOT NULL REFERENCES main.psu_specs(product_id) ON DELETE CASCADE,
    connector_type varchar(32) NOT NULL,
    quantity integer NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (product_id, connector_type)
);

CREATE TABLE main.case_specs (
    product_id bigint PRIMARY KEY REFERENCES main.products(id) ON DELETE CASCADE,
    maximum_gpu_length_mm numeric(8,2) NOT NULL,
    maximum_gpu_height_mm numeric(8,2),
    maximum_gpu_slot_width numeric(4,2),
    maximum_cpu_cooler_height_mm numeric(8,2),
    maximum_psu_length_mm numeric(8,2)
);
CREATE TABLE main.case_motherboard_form_factors (
    product_id bigint NOT NULL REFERENCES main.case_specs(product_id) ON DELETE CASCADE,
    form_factor varchar(40) NOT NULL,
    PRIMARY KEY (product_id, form_factor)
);
CREATE TABLE main.case_psu_form_factors (
    product_id bigint NOT NULL REFERENCES main.case_specs(product_id) ON DELETE CASCADE,
    form_factor varchar(40) NOT NULL,
    PRIMARY KEY (product_id, form_factor)
);
CREATE TABLE main.case_radiator_support (
    product_id bigint NOT NULL REFERENCES main.case_specs(product_id) ON DELETE CASCADE,
    location varchar(40) NOT NULL,
    size_mm integer NOT NULL,
    PRIMARY KEY (product_id, location, size_mm)
);

CREATE TABLE main.monitor_specs (
    product_id bigint PRIMARY KEY REFERENCES main.products(id) ON DELETE CASCADE,
    screen_size_inches numeric(6,2),
    resolution_width integer,
    resolution_height integer,
    refresh_rate_hz integer,
    panel_type varchar(40)
);
CREATE TABLE main.mouse_specs (
    product_id bigint PRIMARY KEY REFERENCES main.products(id) ON DELETE CASCADE,
    maximum_dpi integer,
    weight_grams numeric(8,2),
    connectivity varchar(80)
);
CREATE TABLE main.keyboard_specs (
    product_id bigint PRIMARY KEY REFERENCES main.products(id) ON DELETE CASCADE,
    layout varchar(40),
    size varchar(40),
    switch_type varchar(120),
    connectivity varchar(80)
);

CREATE TABLE main.retailers (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    external_key varchar(160) NOT NULL UNIQUE,
    name varchar(200) NOT NULL,
    is_enabled boolean NOT NULL DEFAULT true,
    priority integer NOT NULL DEFAULT 0
);
CREATE TABLE main.offers (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_id bigint NOT NULL REFERENCES main.products(id) ON DELETE CASCADE,
    retailer_id integer NOT NULL REFERENCES main.retailers(id),
    external_offer_key varchar(240) NOT NULL,
    retailer_sku varchar(160),
    source_url varchar(1000) NOT NULL,
    product_price numeric(12,2) NOT NULL,
    shipping_price numeric(12,2) NOT NULL,
    total_price numeric(12,2) NOT NULL,
    currency varchar(3) NOT NULL DEFAULT 'CHF',
    availability varchar(24) NOT NULL CHECK (availability IN ('in_stock', 'one_week', 'two_weeks', 'four_weeks', 'unknown', 'unavailable')),
    delivery_min_business_days integer,
    delivery_max_business_days integer,
    observed_at timestamptz NOT NULL,
    is_current boolean NOT NULL DEFAULT true,
    UNIQUE (product_id, retailer_id, external_offer_key)
);
CREATE INDEX ix_offers_best ON main.offers(product_id, is_current, total_price);

CREATE TABLE main.offer_observations (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    offer_id bigint NOT NULL REFERENCES main.offers(id) ON DELETE CASCADE,
    import_run_id bigint NOT NULL REFERENCES main.import_runs(id),
    product_price numeric(12,2) NOT NULL,
    shipping_price numeric(12,2) NOT NULL,
    total_price numeric(12,2) NOT NULL,
    currency varchar(3) NOT NULL DEFAULT 'CHF',
    availability varchar(24) NOT NULL,
    delivery_min_business_days integer,
    delivery_max_business_days integer,
    observed_at timestamptz NOT NULL
);
CREATE INDEX ix_offer_observations_offer_time ON main.offer_observations(offer_id, observed_at DESC);

CREATE TABLE main.orders (
    id uuid PRIMARY KEY,
    number varchar(40) NOT NULL UNIQUE,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    status varchar(24) NOT NULL,
    email_notification_status varchar(24) NOT NULL DEFAULT 'pending',
    email_notification_error varchar(2000),
    email_sent_at timestamptz,
    customer_first_name varchar(120) NOT NULL,
    customer_last_name varchar(120) NOT NULL,
    customer_email varchar(320) NOT NULL,
    customer_phone varchar(60),
    company varchar(200),
    street varchar(240),
    postal_code varchar(24),
    city varchar(120),
    notes text,
    terms_accepted boolean NOT NULL,
    language varchar(8) NOT NULL,
    subtotal numeric(12,2) NOT NULL,
    shipping numeric(12,2) NOT NULL,
    total numeric(12,2) NOT NULL,
    currency varchar(3) NOT NULL DEFAULT 'CHF'
);
CREATE TABLE main.order_items (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_id uuid NOT NULL REFERENCES main.orders(id) ON DELETE CASCADE,
    position integer NOT NULL,
    item_type varchar(40) NOT NULL,
    name varchar(300) NOT NULL,
    image_path varchar(1000),
    unit_price numeric(12,2) NOT NULL,
    quantity integer NOT NULL CHECK (quantity > 0),
    configuration_order_type varchar(40),
    service_package_code varchar(40),
    pc_preset_id bigint REFERENCES main.pc_presets(id) ON DELETE SET NULL,
    UNIQUE (order_id, position)
);
CREATE INDEX ix_order_items_pc_preset ON main.order_items(pc_preset_id);
CREATE TABLE main.order_item_details (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_item_id bigint NOT NULL REFERENCES main.order_items(id) ON DELETE CASCADE,
    position integer NOT NULL,
    label varchar(160) NOT NULL,
    value varchar(1000) NOT NULL
);
CREATE TABLE main.order_item_components (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_item_id bigint NOT NULL REFERENCES main.order_items(id) ON DELETE CASCADE,
    position integer NOT NULL,
    product_id bigint REFERENCES main.products(id) ON DELETE SET NULL,
    category_code varchar(32) NOT NULL,
    product_name varchar(500) NOT NULL,
    manufacturer varchar(120),
    manufacturer_part_number varchar(160),
    retailer_name varchar(200),
    unit_price numeric(12,2) NOT NULL,
    currency varchar(3) NOT NULL DEFAULT 'CHF',
    quantity smallint NOT NULL DEFAULT 1 CHECK (quantity > 0),
    UNIQUE (order_item_id, position)
);

INSERT INTO main.categories (code, name, display_order, is_required) VALUES
('CPU', 'Processor', 10, true),
('MOTHERBOARD', 'Motherboard', 20, true),
('RAM', 'Memory', 30, true),
('GPU', 'Graphics card', 40, false),
('STORAGE', 'Storage', 50, true),
('COOLING', 'CPU cooling', 60, true),
('PSU', 'Power supply', 70, true),
('CASE', 'Computer case', 80, true),
('MONITOR', 'Monitor', 90, false),
('MOUSE', 'Mouse', 100, false),
('KEYBOARD', 'Keyboard', 110, false),
('LAPTOP', 'Laptop', 120, false);

INSERT INTO main.source_categories (category_id, external_category_id, url, product_subtype, top_limit)
SELECT id, 'c145', 'https://www.toppreise.ch/top-products/Computers-accessories/PC-components/Processors-c145', NULL, 100 FROM main.categories WHERE code = 'CPU'
UNION ALL SELECT id, 'c140', 'https://www.toppreise.ch/top-products/Computers-accessories/PC-components/Motherboards/Motherboards-c140', NULL, 100 FROM main.categories WHERE code = 'MOTHERBOARD'
UNION ALL SELECT id, 'c147', 'https://www.toppreise.ch/top-products/Computers-accessories/PC-components/Memory-c147', NULL, 100 FROM main.categories WHERE code = 'RAM'
UNION ALL SELECT id, 'c37', 'https://www.toppreise.ch/top-products/Computers-accessories/PC-components/Graphics-cards-accessories/Graphics-cards-c37', NULL, 100 FROM main.categories WHERE code = 'GPU'
UNION ALL SELECT id, 'c317', 'https://www.toppreise.ch/top-products/Computers-accessories/PC-components/Hard-drives-SSD/Solid-State-Drives-SSD-c317', NULL, 100 FROM main.categories WHERE code = 'STORAGE'
UNION ALL SELECT id, 'c203', 'https://www.toppreise.ch/top-products/Computers-accessories/PC-components/Cooling/CPU-coolers-c203', 'air', 100 FROM main.categories WHERE code = 'COOLING'
UNION ALL SELECT id, 'c210', 'https://www.toppreise.ch/top-products/Computers-accessories/PC-components/Cooling/Complete-water-cooling-sets-c210', 'aio', 100 FROM main.categories WHERE code = 'COOLING'
UNION ALL SELECT id, 'c57', 'https://www.toppreise.ch/top-products/Computers-accessories/PC-components/Cases-power-supplies/PC-power-supplies-c57', NULL, 100 FROM main.categories WHERE code = 'PSU'
UNION ALL SELECT id, 'c52', 'https://www.toppreise.ch/top-products/Computers-accessories/PC-components/Cases-power-supplies/Computer-cases-c52', NULL, 100 FROM main.categories WHERE code = 'CASE'
UNION ALL SELECT id, 'c194', 'https://www.toppreise.ch/top-products/Computers-accessories/Monitors/Monitors-c194', NULL, 100 FROM main.categories WHERE code = 'MONITOR'
UNION ALL SELECT id, 'c180', 'https://www.toppreise.ch/top-products/Computers-accessories/Input-devices/Mice-c180', NULL, 100 FROM main.categories WHERE code = 'MOUSE'
UNION ALL SELECT id, 'c179', 'https://www.toppreise.ch/top-products/Computers-accessories/Input-devices/Keyboards-c179', NULL, 100 FROM main.categories WHERE code = 'KEYBOARD'
UNION ALL SELECT id, 'c13', 'https://www.toppreise.ch/top-products/Computers-accessories/Notebooks-tablets-eReaders/Notebooks-c13', NULL, 100 FROM main.categories WHERE code = 'LAPTOP';
