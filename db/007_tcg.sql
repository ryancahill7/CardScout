-- Trading card games (Pokemon, Magic, Yu-Gi-Oh!, One Piece, Lorcana...) alongside sports cards.
-- All columns are nullable; NULL CATEGORY means a sports card (every row saved before this script).
-- For TCG cards the existing columns are reused: PLAYER = card name, SET_NAME = set/expansion,
-- CARD_NUMBER = collector number as printed ("4/102", "LOB-EN001"), PARALLEL = variant/finish
-- ("1st Edition", "Reverse Holo"), MANUFACTURER = publisher.

ALTER TABLE CARD_INVENTORY ADD (
    CATEGORY      VARCHAR2(10),
    GAME          VARCHAR2(20),
    SET_CODE      VARCHAR2(30),
    RARITY        VARCHAR2(60),
    CARD_LANGUAGE VARCHAR2(30)
);

-- Which eBay category the watchlist searches and how listing titles are matched to comps
ALTER TABLE CARD_WATCHLISTS ADD (
    CATEGORY VARCHAR2(10),
    GAME     VARCHAR2(20)
);
