# ADR 0001: Monolito modular com DDD, e não microsserviços

- **Status:** aceita
- **Data:** 2026-10-30
- **Contexto do curso:** módulo 1, lições 1.1 e 1.4

## Contexto

O CreditFlow tem cinco capacidades de negócio (Clientes, Simulação, Propostas, Pagamentos, Notificações)
com ritmos e cargas diferentes: Simulação é pública e CPU-bound; Pagamentos mexe com dinheiro e depende
de um gateway externo; o resto é CRUD com regras. O time que mantém o sistema é pequeno (uma pessoa
estudando, ou um squad de 4 a 8 pessoas na empresa).

Opções consideradas:

1. **Monolito em camadas** (um projeto, pastas Controllers/Services/Repositories).
2. **Microsserviços** (um serviço por módulo, cada um com banco, pipeline e deploy próprios).
3. **Monolito modular** (um processo, módulos com fronteiras fortes, verificadas por compilador e teste).

## Decisão

Opção 3. Cada módulo é um par de projetos:

- `CreditFlow.Modules.X`: Domain, Application, Infrastructure, Api e `XModule` (o único tipo que o Host chama).
- `CreditFlow.Modules.X.Contracts`: o que os outros módulos podem ver (interfaces de consulta, DTOs, eventos).

Regras (todas verificadas por `tests/CreditFlow.ArchitectureTests`):

- um módulo só referencia `CreditFlow.BuildingBlocks` e projetos `*.Contracts`;
- um `*.Contracts` só referencia `CreditFlow.BuildingBlocks`;
- `Domain` não depende de EF Core, MongoDB, Kafka, ASP.NET Core nem das camadas de fora do próprio módulo;
- `BuildingBlocks` não conhece nenhum módulo;
- cada módulo tem seu schema no Postgres (ou sua coleção no MongoDB), e nenhum lê o schema de outro.

Comunicação: síncrona e in-process por interfaces de `*.Contracts` (ex.: `IConsultaDeClientes`);
assíncrona por eventos de integração no Kafka, com outbox no Postgres para os que não podem se perder.

## Consequências

**Positivas**
- Um repositório, um build, uma imagem: o custo operacional de um sistema só.
- Refatorar entre módulos é um refactor da IDE, não uma migração de contrato entre serviços.
- Transação local onde ela é necessária (estado da proposta + outbox na mesma transação).
- Extrair um módulo para um serviço depois é barato, porque a fronteira já existe e já é testada.

**Negativas**
- Um bug que derruba o processo derruba todos os módulos do processo (mitigado pelos perfis de execução, ADR 0002).
- Todos os módulos sobem na mesma versão do .NET e das bibliotecas compartilhadas.
- A fronteira depende de disciplina de revisão no que os testes não pegam (ex.: SQL escrito à mão no schema de outro módulo).

## Quando revisitar

Quando um módulo precisar de um ritmo de deploy, uma tecnologia ou um time realmente separados, ou
quando os perfis de execução (ADR 0002) não bastarem para isolar a carga.
