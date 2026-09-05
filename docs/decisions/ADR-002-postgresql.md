# ADR-002: Why PostgreSQL
Strong relational guarantees (FKs, check constraints, transactions) fit the
domain (orgs, policies, incidents) better than a document store; mature EF
Core provider; per-schema-per-module keeps a monolith database organized.
