-- SportsCardsPro's product API also returns condition-N-price fields beyond the seven
-- documented tiers. Matching them against a known card (1978 Topps Eddie Murray #36) and
-- its live PSA 6 sales:
--   condition-9 = Grade 1, condition-10 = Grade 2, condition-13 = Grade 3, condition-14 = Grade 4,
--   condition-15 = Grade 5, condition-16 = Grade 6, condition-17 = CGC 10, condition-18 = SGC 10.
-- Without these, any card graded below 7 was priced at the raw (ungraded) comp.
ALTER TABLE COMP_CACHE ADD (
    GRADE1_PRICE  NUMBER(12,2),
    GRADE2_PRICE  NUMBER(12,2),
    GRADE3_PRICE  NUMBER(12,2),
    GRADE4_PRICE  NUMBER(12,2),
    GRADE5_PRICE  NUMBER(12,2),
    GRADE6_PRICE  NUMBER(12,2),
    CGC10_PRICE   NUMBER(12,2),
    SGC10_PRICE   NUMBER(12,2),
    -- Number of sales SportsCardsPro has recorded for the product (all grades)
    SALES_VOLUME  NUMBER(10)
);

-- Grading company certification number from the slab label (e.g. PSA cert 12345678)
ALTER TABLE CARD_INVENTORY ADD (
    CERT_NUMBER VARCHAR2(30)
);
