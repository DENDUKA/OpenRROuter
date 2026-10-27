-- Robokassa pay links carry a url-encoded receipt and ResultUrl2, so they exceed 512 chars.
alter table billing.payments alter column pay_url type text;
