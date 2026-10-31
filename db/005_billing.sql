-- Stripe subscriptions (paid on a Stripe-hosted page in the browser, not via in-app purchase).
-- One row per user: the Stripe customer we created for them and their current subscription.
-- Rows are written by the API (checkout creates the customer) and the Stripe webhook
-- (which keeps STATUS / CURRENT_PERIOD_END / CANCEL_AT_PERIOD_END in sync).
CREATE TABLE CARD_SUBSCRIPTIONS (
    USER_ID                NUMBER NOT NULL CONSTRAINT PK_CARD_SUBSCRIPTIONS PRIMARY KEY
                           CONSTRAINT FK_CARD_SUBSCRIPTIONS_USER REFERENCES CARD_USERS (ID),
    STRIPE_CUSTOMER_ID     VARCHAR2(100) NOT NULL CONSTRAINT UQ_CARD_SUBSCRIPTIONS_CUSTOMER UNIQUE,
    STRIPE_SUBSCRIPTION_ID VARCHAR2(100),
    STRIPE_PRICE_ID        VARCHAR2(100),
    -- 'month' / 'year'
    BILLING_INTERVAL       VARCHAR2(10),
    -- Derived from STATUS when written: PRO while active or trialing, else FREE
    PLAN                   VARCHAR2(10) DEFAULT 'FREE' NOT NULL
                           CONSTRAINT CK_CARD_SUBSCRIPTIONS_PLAN CHECK (PLAN IN ('FREE', 'PRO')),
    -- Stripe subscription status: active, trialing, past_due, canceled, unpaid, incomplete, incomplete_expired, paused
    STATUS                 VARCHAR2(30),
    CURRENT_PERIOD_END     TIMESTAMP,
    CANCEL_AT_PERIOD_END   CHAR(1) DEFAULT 'N' NOT NULL CHECK (CANCEL_AT_PERIOD_END IN ('Y', 'N')),
    LAST_PAYMENT_FAILED_AT TIMESTAMP,
    CREATED_AT             TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    UPDATED_AT             TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
);

CREATE INDEX IX_CARD_SUBSCRIPTIONS_SUB ON CARD_SUBSCRIPTIONS (STRIPE_SUBSCRIPTION_ID);

-- Stripe webhook event ids already handled, so retries and duplicate deliveries are no-ops
CREATE TABLE CARD_STRIPE_EVENTS (
    EVENT_ID     VARCHAR2(100) NOT NULL CONSTRAINT PK_CARD_STRIPE_EVENTS PRIMARY KEY,
    EVENT_TYPE   VARCHAR2(100) NOT NULL,
    USER_ID      NUMBER,
    PROCESSED_AT TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
);
