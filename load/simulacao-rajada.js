// Teste de carga: RAJADA na simulação (o perfil api-simulacao, CPU-bound, escalado por HPA).
//
// O que ele responde: quando o tráfego sobe de 20 para 300 simulações/s em 10 segundos (campanha na TV,
// push no app), a api-simulacao aguenta enquanto o HPA cria réplicas? Onde a latência estoura?
//
// Por que "arrival rate" (modelo ABERTO) e não N usuários em laço (modelo FECHADO): na vida real os
// clientes não esperam a resposta do anterior para chegar. Num modelo fechado, se a API fica lenta, o k6
// manda MENOS requisições e esconde o problema. Aqui a taxa é imposta; se a API não dá conta, o k6
// precisa de mais VUs, e quando acabam, conta "dropped_iterations": a demanda que você não atendeu.
//
// Rodar:  k6 run load/simulacao-rajada.js
//         k6 run -e BASE_URL=https://api.exemplo.com.br -e PICO=500 load/simulacao-rajada.js
// No cluster, em outro terminal:  kubectl get hpa,pods -n creditflow -w
// ATENÇÃO: a simulação é pública com rate limit por IP; num teste de um IP só, suba o limite
// do ambiente de teste (Simulacao__RequisicoesPorMinuto, padrão 30 por IP POR POD) ou rode o k6 de vários
// IPs; senão você mede o 429, não a API.
import http from "k6/http";
import { check } from "k6";
import { Trend, Rate } from "k6/metrics";
import { BASE_URL, aleatorio } from "./lib/comum.js";

const BASE = Number(__ENV.BASE || 20); // simulações/s antes e depois da rajada
const PICO = Number(__ENV.PICO || 300); // simulações/s no pico

const latenciaSimulacao = new Trend("simulacao_latencia", true);
const limitadas = new Rate("simulacao_429");

export const options = {
  scenarios: {
    rajada: {
      executor: "ramping-arrival-rate",
      startRate: BASE,
      timeUnit: "1s",
      preAllocatedVUs: 50,
      maxVUs: Number(__ENV.MAX_VUS || 600),
      stages: [
        { target: BASE, duration: "1m" },   // linha de base: latência com as réplicas mínimas
        { target: PICO, duration: "10s" },  // a rajada: 15x em 10 s, mais rápido que o HPA reage
        { target: PICO, duration: "3m" },   // sustenta: dá tempo de o HPA (e talvez o autoscaler de nós) agir
        { target: BASE, duration: "30s" },  // volta
        { target: BASE, duration: "5m" },   // observe a janela de 5 min do scaleDown antes de reduzir
      ],
    },
  },
  thresholds: {
    // O SLO do laboratório escrito em código: passou ou reprovou.
    http_req_failed: ["rate<0.01"],
    simulacao_latencia: ["p(95)<400", "p(99)<1200"],
    // Latência só da janela de pico, para não diluir no tempo calmo (tag "fase" abaixo).
    "simulacao_latencia{fase:pico}": ["p(95)<800"],
    simulacao_429: ["rate<0.001"],
    dropped_iterations: ["count<100"],
  },
  summaryTrendStats: ["avg", "med", "p(90)", "p(95)", "p(99)", "max"],
};

const SISTEMAS = ["Price", "SAC"];
const inicio = Date.now();

function fase() {
  const s = (Date.now() - inicio) / 1000;
  return s < 60 ? "base" : s < 250 ? "pico" : "retorno";
}

export default function () {
  const corpo = JSON.stringify({
    valor: aleatorio(1000, 50000),
    prazoMeses: aleatorio(6, 48),
    sistema: SISTEMAS[aleatorio(0, 1)],
  });
  const tags = { name: "POST /v1/simulacoes", fase: fase() };
  const r = http.post(`${BASE_URL}/v1/simulacoes`, corpo, { headers: { "Content-Type": "application/json" }, tags });

  latenciaSimulacao.add(r.timings.duration, tags);
  limitadas.add(r.status === 429, tags);
  check(r, {
    "201 Created": (x) => x.status === 201,
    "tem tabela de parcelas": (x) => x.status !== 201 || (x.json("parcelas") || []).length > 0,
  });
}
