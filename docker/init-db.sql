-- Runs automatically the first time the postgres container starts (empty data volume).
-- To run it again: docker compose -f docker-compose.dev.yml down -v, then up -d.

CREATE DATABASE foodordering_client;
CREATE DATABASE foodordering_admin;
