-- Schema do módulo Clientes. Idempotente: pode rodar a cada subida (IF NOT EXISTS).
-- É exatamente o que o ClientesDbContext espera. No módulo 4 do curso este script dá lugar
-- a migrações do EF Core aplicadas por um Job antes do deploy.
CREATE SCHEMA IF NOT EXISTS clientes;

CREATE TABLE IF NOT EXISTS clientes.clientes (
    id             uuid           PRIMARY KEY,
    nome           varchar(120)   NOT NULL,
    cpf            varchar(11)    NOT NULL,
    renda_mensal   numeric(14,2)  NOT NULL CHECK (renda_mensal > 0),
    score          integer        NOT NULL CHECK (score BETWEEN 0 AND 1000),
    cadastrado_em  timestamptz    NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_clientes_cpf ON clientes.clientes (cpf);

CREATE TABLE IF NOT EXISTS clientes.consentimentos (
    id              uuid         PRIMARY KEY,
    cliente_id      uuid         NOT NULL REFERENCES clientes.clientes (id) ON DELETE CASCADE,
    finalidade      varchar(40)  NOT NULL,
    versao_do_termo varchar(40)  NOT NULL,
    concedido_em    timestamptz  NOT NULL,
    revogado_em     timestamptz  NULL
);

CREATE INDEX IF NOT EXISTS ix_consentimentos_cliente ON clientes.consentimentos (cliente_id);

-- No máximo um consentimento ATIVO por finalidade (índice parcial): a regra do agregado, garantida também no banco.
CREATE UNIQUE INDEX IF NOT EXISTS uq_consentimento_ativo
    ON clientes.consentimentos (cliente_id, finalidade) WHERE revogado_em IS NULL;
