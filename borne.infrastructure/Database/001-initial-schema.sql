-- Run against the centrally selected Borne.Core database.
-- The script is safe to rerun for initial setup; it does not create a database.

CREATE TABLE IF NOT EXISTS borne (
    id_borne INT NOT NULL AUTO_INCREMENT,
    code_borne VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    PRIMARY KEY (id_borne),
    UNIQUE KEY uq_borne_code_borne (code_borne)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS tarif (
    id_tarif INT NOT NULL AUTO_INCREMENT,
    type_energie VARCHAR(32) NOT NULL,
    PRIMARY KEY (id_tarif),
    KEY ix_tarif_type_energie (type_energie)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS reservation (
    id_reservation INT NOT NULL AUTO_INCREMENT,
    ref_id INT NOT NULL,
    date_de DATETIME(6) NOT NULL,
    date_fin DATETIME(6) NOT NULL,
    type_energie VARCHAR(32) NOT NULL,
    tag VARCHAR(255) NULL,
    id_borne INT NOT NULL,
    PRIMARY KEY (id_reservation),
    KEY ix_reservation_borne_dates (id_borne, date_de, date_fin),
    CONSTRAINT fk_reservation_borne
        FOREIGN KEY (id_borne) REFERENCES borne (id_borne)
        ON UPDATE CASCADE ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS tranche (
    id_tranche INT NOT NULL AUTO_INCREMENT,
    val_min DECIMAL(18,4) NOT NULL,
    val_max DECIMAL(18,4) NOT NULL,
    prix DECIMAL(18,4) NOT NULL,
    id_tarif INT NOT NULL,
    PRIMARY KEY (id_tranche),
    KEY ix_tranche_tarif_min (id_tarif, val_min),
    CONSTRAINT fk_tranche_tarif
        FOREIGN KEY (id_tarif) REFERENCES tarif (id_tarif)
        ON UPDATE CASCADE ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS consommation (
    id_consommation INT NOT NULL AUTO_INCREMENT,
    reading_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    date_consommation DATETIME(6) NOT NULL,
    quantite DECIMAL(18,4) NOT NULL,
    montant DECIMAL(18,4) NOT NULL,
    tarif_id INT NOT NULL,
    reservation_id INT NOT NULL,
    PRIMARY KEY (id_consommation),
    UNIQUE KEY uq_consommation_reading_id (reading_id),
    KEY ix_consommation_reservation_date (reservation_id, date_consommation),
    KEY ix_consommation_tarif (tarif_id),
    CONSTRAINT fk_consommation_tarif
        FOREIGN KEY (tarif_id) REFERENCES tarif (id_tarif)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    CONSTRAINT fk_consommation_reservation
        FOREIGN KEY (reservation_id) REFERENCES reservation (id_reservation)
        ON UPDATE CASCADE ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS seuil (
    id_seuil INT NOT NULL AUTO_INCREMENT,
    valeur DECIMAL(18,4) NOT NULL,
    reservation_id INT NOT NULL,
    type_seuil VARCHAR(32) NOT NULL,
    PRIMARY KEY (id_seuil),
    KEY ix_seuil_reservation (reservation_id),
    CONSTRAINT fk_seuil_reservation
        FOREIGN KEY (reservation_id) REFERENCES reservation (id_reservation)
        ON UPDATE CASCADE ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
