-- Scope changed to stand-alone Part D plans (PDPs) only on 2026-09-30.
-- Medicare Advantage service areas and landscape columns are no longer loaded.

drop table cms.spuf_plan_county;

alter table cms.spuf_plan
    drop column ma_region_code,
    drop column snp;

alter table cms.landscape_plan
    drop column contract_category,
    drop column plan_type,
    drop column snp_type,
    drop column has_part_d,
    drop column part_c_premium,
    drop column consolidated_premium,
    drop column moop_in_network,
    drop column star_part_c,
    drop column star_overall,
    drop column ma_region_code;
