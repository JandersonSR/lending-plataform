# ADR 0002: Perfis de execução: uma imagem, vários deployments

- **Status:** aceita
- **Data:** 2026-10-30
- **Contexto do curso:** módulo 1 (lição 1.3) e módulo 8

## Contexto

O monolito modular (ADR 0001) resolve o custo de desenvolvimento, mas não o de escala: Simulação é
pública e consome CPU em rajadas; o worker de Pagamentos precisa acompanhar o atraso (lag) do Kafka;
o resto tem carga baixa e estável. Escalar o processo inteiro para atender a Simulação desperdiça
memória e conexões de banco em módulos que não precisam.

## Decisão

A mesma imagem sobe com duas variáveis de ambiente:

- `CREDITFLOW__MODULOS`: lista de módulos que este processo executa (vazia = todos, o modo local);
- `CREDITFLOW__PAPEL`: `api` (mapeia endpoints HTTP) ou `worker` (só consome mensagens e roda jobs).

| Perfil | Módulos | Papel | Escala por |
|---|---|---|---|
| `api-geral` | Clientes, Propostas, Notificacoes | api | réplicas fixas |
| `api-simulacao` | Simulacao | api | CPU (HPA) |
| `api-pagamentos` | Pagamentos | api | réplicas fixas |
| `worker-pagamentos` | Pagamentos | worker | lag do Kafka (KEDA) |

O Ingress roteia por caminho: `/v1/simulacoes` → api-simulacao, `/v1/pagamentos` → api-pagamentos, resto → api-geral.

Implementação (`src/BuildingBlocks/CreditFlow.BuildingBlocks/Modulos`):

- `PerfilDeExecucao.Ler` valida a configuração na subida: módulo desconhecido ou papel inválido derruba o
  processo (um erro de digitação no Helm não pode virar um pod saudável que não atende nada);
- o Host mantém um catálogo explícito de módulos em `Program.cs` e chama `Registrar`/`Mapear` só dos ativos;
- **contratos fora do perfil:** um módulo que oferece interface síncrona implementa `IModuloComContratos`.
  Quando ele NÃO está no perfil, o Host chama só `RegistrarContratos`, que registra o mínimo para atender a
  interface (repositório de leitura), sem endpoints, consumidores nem jobs. Exemplo: o pod api-simulacao
  consulta `IConsultaDeClientes` lendo o schema `clientes` diretamente.

## Alternativas consideradas para o "contrato fora do perfil"

1. **Chamada HTTP para o pod que tem o módulo.** Desacopla o deploy, mas troca uma chamada em memória por rede
   (latência, timeout, retry, circuit breaker) e cria dependência de disponibilidade entre pods.
2. **Cópia local por eventos** (Simulação guarda o score que recebe por `ClienteAtualizado`). O mais
   desacoplado e o caminho de uma futura extração; custa consistência eventual e mais um consumidor.
3. **Registrar só os contratos** (escolhida). Simples e consistente; custa o pod api-simulacao precisar da
   conexão com o Postgres e de permissão de leitura no schema `clientes`.

## Consequências

- Um build, uma imagem, uma tag; o Helm tem um Deployment por perfil com valores diferentes.
- A configuração vira parte da arquitetura: precisa de validação na subida e de teste.
- Os módulos precisam funcionar com qualquer combinação de perfis: nenhum pode supor que outro está ativo no
  mesmo processo (exceto pelo que vem de `*.Contracts`).
- Hosted services e consumidores devem ser registrados só em `Registrar` (nunca em `RegistrarContratos`), e
  consumidores só quando o papel é `worker` (ou conforme a decisão do módulo).
