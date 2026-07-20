-- Migration: Widen users table varchar limits
--
-- Problem: email/username were capped at varchar(30) and first/middle/last
-- name at varchar(20). Many real emails and names exceed these limits,
-- causing fn_usersinsertupdate to throw "value too long for type character
-- varying" (Postgres error 22001) on INSERT/UPDATE — surfaced to callers as
-- a bare 500 with no detail. This silently breaks user provisioning for
-- anyone whose email/name doesn't fit, which in turn breaks the new hdav2
-- app's OCR-Backend user sync (see hda-harikrishna-digital-archive's
-- backend/api/services/ocr_backend_sync.py).
--
-- Fix: widen the columns. Purely additive — widening a varchar column
-- never truncates or affects existing data.

ALTER TABLE public.users ALTER COLUMN email SET DATA TYPE character varying(255);
ALTER TABLE public.users ALTER COLUMN username SET DATA TYPE character varying(255);
ALTER TABLE public.users ALTER COLUMN firstname SET DATA TYPE character varying(100);
ALTER TABLE public.users ALTER COLUMN middlename SET DATA TYPE character varying(100);
ALTER TABLE public.users ALTER COLUMN lastname SET DATA TYPE character varying(100);
