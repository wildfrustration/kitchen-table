-- Fuzzy drug search ("lipiter", "eliquis 5") and brand → generic equivalents for the "generic or brand?" question.
create extension if not exists pg_trgm;

drop index cms.rx_concept_name;
create index rx_concept_name_trgm on cms.rx_concept using gin (lower(name) gin_trgm_ops);

-- A branded product (SBD/BPCK) and its generic equivalent (SCD/GPCK), from RXNREL "tradename_of".
create table cms.rx_generic (
    release_id    int  not null references cms.release (id) on delete cascade,
    brand_rxcui   text not null,
    generic_rxcui text not null,
    primary key (release_id, brand_rxcui, generic_rxcui)
);
create index on cms.rx_generic (release_id, generic_rxcui);
