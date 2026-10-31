-- Schema do módulo Propostas. Idempotente: pode rodar a cada subida (IF NOT EXISTS).
-- É exatamente o que o PropostasDbContext espera (validado em PostgreSQL 16).
-- Não existe coluna "versao": a concorrência otimista usa a coluna de sistema xmin.
CREATE SCHEMA IF NOT EXISTS propostas;

CREATE TABLE IF NOT EXISTS propostas.propostas (
    id                 uuid           PRIMARY KEY,
    cliente_id         uuid           NOT NULL,          -- id do módulo Clientes (sem FK entre schemas, de propósito)
    solicitante_id     varchar(64)    NOT NULL,          -- claim sub do Keycloak
    simulacao_id       uuid           NOT NULL,          -- documento no MongoDB (simulacoes)
    valor_solicitado   numeric(14,2)  NOT NULL,
    valor_liberado     numeric(14,2)  NOT NULL,          -- solicitado - IOF: o valor do PIX
    prazo_meses        integer        NOT NULL,
    taxa_mensal        numeric(12,10) NOT NULL,          -- fração: 0,0199 = 1,99% a.m.
    cet_anual          numeric(12,10) NOT NULL,
    sistema            varchar(10)    NOT NULL,          -- Price | SAC
    parcelas           jsonb          NOT NULL,          -- [{"numero":1,"valor_centavos":51234}, ...]
    score_na_analise   integer        NOT NULL,
    renda_na_analise   numeric(14,2)  NOT NULL,
    comprometimento    numeric(8,4)   NOT NULL,
    status             varchar(20)    NOT NULL,
    decisao_automatica boolean        NOT NULL,
    codigo_motivo      varchar(60)    NULL,
    justificativa      varchar(1000)  NULL,
    analisado_por      varchar(120)   NULL,
    chave_pix          varchar(77)    NULL,
    criada_em          timestamptz    NOT NULL,
    decidida_em        timestamptz    NULL,
    valida_ate         timestamptz    NULL,
    contratada_em      timestamptz    NULL,
    finalizada_em      timestamptz    NULL,
    CONSTRAINT ck_propostas_prazo CHECK (prazo_meses BETWEEN 6 AND 48),
    CONSTRAINT ck_propostas_valor CHECK (valor_solicitado BETWEEN 1000 AND 50000),
    CONSTRAINT ck_propostas_liberado CHECK (valor_liberado > 0 AND valor_liberado <= valor_solicitado),
    CONSTRAINT ck_propostas_status CHECK (status IN
        ('EmAnalise','Aprovada','Recusada','Contratada','Liberada','DesembolsoFalhou','Cancelada','Expirada')),
    CONSTRAINT ck_propostas_parcelas CHECK (jsonb_typeof(parcelas) = 'array' AND jsonb_array_length(parcelas) = prazo_meses),
    CONSTRAINT ck_propostas_contratada_tem_pix CHECK (contratada_em IS NULL OR chave_pix IS NOT NULL)
);

-- No máximo UMA proposta ativa por cliente. Índice único PARCIAL: só as linhas ativas entram nele,
-- então o histórico (recusadas, liberadas...) não conta. É a regra garantida entre réplicas.
CREATE UNIQUE INDEX IF NOT EXISTS ux_propostas_cliente_ativa
    ON propostas.propostas (cliente_id)
    WHERE status IN ('EmAnalise', 'Aprovada', 'Contratada');

-- Fila do analista (WHERE status = 'EmAnalise' ORDER BY criada_em) e "minhas propostas".
CREATE INDEX IF NOT EXISTS ix_propostas_status_criada_em ON propostas.propostas (status, criada_em);
CREATE INDEX IF NOT EXISTS ix_propostas_solicitante_criada_em ON propostas.propostas (solicitante_id, criada_em);
