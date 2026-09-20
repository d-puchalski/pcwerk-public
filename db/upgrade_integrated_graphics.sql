BEGIN;

CREATE TABLE IF NOT EXISTS main.motherboard_video_outputs (
    product_id bigint NOT NULL REFERENCES main.motherboard_specs(product_id) ON DELETE CASCADE,
    output_type varchar(24) NOT NULL,
    version varchar(16) NOT NULL DEFAULT '',
    quantity integer NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (product_id, output_type, version)
);

UPDATE main.categories
SET is_required = false
WHERE code = 'GPU';

COMMIT;
