// Teste de carga: o FLUXO COMPLETO da saga, de ponta a ponta.
//   cadastrar cliente → simular → criar proposta → (aprovar, se cair na fila do analista)
//   → contratar → [Kafka: ContratoAssinado → worker-pagamentos → PIX → DesembolsoConcluido] → Liberada
//
// A métrica que importa aqui NÃO é a latência HTTP: é "tempo_ate_desembolso", do 200 do contratar até o
// desembolso aparecer Concluido em /v1/pagamentos. Ela soma: relay da outbox de Propostas (intervalo
// ocioso), fila no tópico (lag), consumo, gateway PIX simulado (latência + retries) e o relay de volta.
// Com o worker escalando por lag (KEDA), é o número que mostra se a escala acompanha a demanda.
//
// Rodar local (docker compose + Host com todos os módulos):
//   k6 run load/fluxo-completo.js
//   k6 run -e TAXA=5 -e DURACAO=10m load/fluxo-completo.js
// Para ver o worker escalar no cluster:  kubectl get scaledobject,hpa,pods -n creditflow -w
// e o lag:  kafka-consumer-groups.sh --describe --group creditflow-pagamentos
import http from "k6/http";
import { check, sleep, fail } from "k6";
import { Trend, Counter, Rate } from "k6/metrics";
import { BASE_URL, token, jsonAuth, cpfValido, aleatorio } from "./lib/comum.js";

const TAXA = Number(__ENV.TAXA || 2); // contratos por segundo
const DURACAO = __ENV.DURACAO || "5m";
const ESPERA_MAXIMA_S = Number(__ENV.ESPERA_MAXIMA_S || 120);

const tempoAteDesembolso = new Trend("tempo_ate_desembolso", true);
const desembolsosConcluidos = new Counter("desembolsos_concluidos");
const desembolsosFalhos = new Counter("desembolsos_falhos");
const sagaNaoTerminou = new Rate("saga_nao_terminou");

export const options = {
  scenarios: {
    contratos: {
      executor: "constant-arrival-rate",
      rate: TAXA,
      timeUnit: "1s",
      duration: DURACAO,
      preAllocatedVUs: 20,
      // Cada iteração espera o desembolso (segundos): precisa de VUs = taxa × duração da iteração.
      maxVUs: Number(__ENV.MAX_VUS || 400),
    },
  },
  thresholds: {
    "http_req_failed{grupo:api}": ["rate<0.01"],
    tempo_ate_desembolso: ["p(95)<15000"], // 15 s do "assinei" ao "dinheiro saiu", com o gateway padrão
    saga_nao_terminou: ["rate<0.005"],
  },
  summaryTrendStats: ["avg", "med", "p(90)", "p(95)", "p(99)", "max"],
};

// Uma vez, antes do teste: tokens dos papéis (o password grant é caro; não faça por iteração).
export function setup() {
  return {
    cliente: __ENV.TOKEN_CLIENTE || token(__ENV.USUARIO_CLIENTE || "ana.cliente", __ENV.SENHA || "Senha@123"),
    analista: __ENV.TOKEN_ANALISTA || token(__ENV.USUARIO_ANALISTA || "carla.analista", __ENV.SENHA || "Senha@123"),
  };
}

const api = (nome) => ({ name: nome, grupo: "api" });

export default function (tokens) {
  // 1. cliente novo por iteração: a regra "uma proposta ativa por cliente" bloquearia a segunda.
  let r = http.post(`${BASE_URL}/v1/clientes`, JSON.stringify({
    nome: `Carga ${__VU}-${__ITER}`, cpf: cpfValido(), rendaMensal: aleatorio(4000, 30000),
    aceitaAnaliseDeCredito: true, versaoDoTermo: "2026-01",
  }), Object.assign(jsonAuth(tokens.cliente), { tags: api("POST /v1/clientes") }));
  if (!check(r, { "cliente criado": (x) => x.status === 201 })) return;
  const clienteId = r.json("id");

  // 2. simulação (pública)
  r = http.post(`${BASE_URL}/v1/simulacoes`, JSON.stringify({
    valor: aleatorio(1000, 20000), prazoMeses: aleatorio(6, 36), sistema: "Price", clienteId,
  }), { headers: { "Content-Type": "application/json" }, tags: api("POST /v1/simulacoes") });
  if (!check(r, { "simulação criada": (x) => x.status === 201 })) return;
  const simulacaoId = r.json("id");

  // 3. proposta
  r = http.post(`${BASE_URL}/v1/propostas`, JSON.stringify({ clienteId, simulacaoId }),
    Object.assign(jsonAuth(tokens.cliente), { tags: api("POST /v1/propostas") }));
  if (!check(r, { "proposta criada": (x) => x.status === 201 })) return;
  const propostaId = r.json("id");
  let status = r.json("status");

  // 4. caiu na fila do analista? aprova com If-Match (concorrência otimista da Propostas)
  if (status === "EmAnalise") {
    const g = http.get(`${BASE_URL}/v1/propostas/${propostaId}`, Object.assign(jsonAuth(tokens.analista), { tags: api("GET /v1/propostas/{id}") }));
    r = http.post(`${BASE_URL}/v1/propostas/${propostaId}/aprovar`, JSON.stringify({ texto: "teste de carga" }),
      Object.assign(jsonAuth(tokens.analista, { "If-Match": g.headers["Etag"] || g.headers["ETag"] || "" }), { tags: api("POST /v1/propostas/{id}/aprovar") }));
    status = r.status === 200 ? r.json("status") : status;
  }
  if (status !== "Aprovada") return; // recusada pela regra: faz parte, não é erro do teste

  // 5. contratar: aqui nasce o ContratoAssinado (outbox de Propostas)
  // Chave PIX "indisponivel@creditflow.dev" força o caminho de falha; troque com -e CHAVE_PIX=... no lab.
  r = http.post(`${BASE_URL}/v1/propostas/${propostaId}/contratar`, JSON.stringify({ chavePix: __ENV.CHAVE_PIX || `carga-${__VU}@pix.dev` }),
    Object.assign(jsonAuth(tokens.cliente), { tags: api("POST /v1/propostas/{id}/contratar") }));
  if (!check(r, { "contratada": (x) => x.status === 200 })) return;
  const assinadoEm = Date.now();

  // 6. espera a saga: polling com recuo (não martele a API que você está medindo)
  let espera = 0.25;
  while ((Date.now() - assinadoEm) / 1000 < ESPERA_MAXIMA_S) {
    sleep(espera);
    espera = Math.min(espera * 1.5, 2);
    const d = http.get(`${BASE_URL}/v1/pagamentos/desembolsos?propostaId=${propostaId}`,
      Object.assign(jsonAuth(tokens.cliente), { tags: { name: "GET /v1/pagamentos/desembolsos", grupo: "polling" } }));
    if (d.status === 404) continue; // o ContratoAssinado ainda não foi consumido
    if (d.status !== 200) continue;
    const s = d.json("status");
    if (s === "Concluido" || s === "Falhou") {
      tempoAteDesembolso.add(Date.now() - assinadoEm, { desfecho: s });
      (s === "Concluido" ? desembolsosConcluidos : desembolsosFalhos).add(1);
      sagaNaoTerminou.add(false);
      return;
    }
  }
  sagaNaoTerminou.add(true);
  if (__ENV.FALHAR_SE_TRAVAR === "1") fail(`saga da proposta ${propostaId} não terminou em ${ESPERA_MAXIMA_S}s`);
}
