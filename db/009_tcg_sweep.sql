-- Trading card game deals: TcgSweepWorker saves the newest Pokemon / Magic / Yu-Gi-Oh! / One Piece /
-- Lorcana listings to EBAY_LISTINGS alongside the sports ones. CATEGORY ('sports' / 'tcg') and GAME
-- tell MatcherService which matcher to use (PriceCharting for TCG). NULL = sports (rows before this script).
ALTER TABLE EBAY_LISTINGS ADD (
    CATEGORY VARCHAR2(10),
    GAME     VARCHAR2(20)
);
