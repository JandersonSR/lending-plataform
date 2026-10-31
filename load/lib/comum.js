// Utilitários compartilhados pelos testes de carga do CreditFlow (k6).
// k6 roda JavaScript ES2015+ num runtime próprio (não é Node): nada de require/npm, só módulos k6/*.
import http from "k6/http";
import { check, fail } from "k6";

export const BASE_URL = __ENV.BASE_URL || "http://localhost:8080";
export const KEYCLOAK_URL = __ENV.KEYCLOAK_URL || "http://localhost:8081";
export const REALM = __ENV.REALM || "creditflow";
// Client do realm de DESENVOLVIMENTO com password grant (creditflow-testes). Nunca existe em produção:
// lá o teste usa um token obtido fora (TOKEN_CLIENTE / TOKEN_ANALISTA) ou client credentials.
export const CLIENT_ID = __ENV.CLIENT_ID || "creditflow-testes";

export function token(usuario, senha) {
  const r = http.post(`${KEYCLOAK_URL}/realms/${REALM}/protocol/openid-connect/token`, {
    grant_type: "password",
    client_id: CLIENT_ID,
    username: usuario,
    password: senha,
    scope: "openid",
  }, { tags: { name: "keycloak-token" } });
  if (!check(r, { "token obtido": (x) => x.status === 200 })) fail(`token de ${usuario}: HTTP ${r.status} ${r.body}`);
  return r.json("access_token");
}

export function jsonAuth(tokenAcesso, extras = {}) {
  return { headers: Object.assign({ "Content-Type": "application/json", Authorization: `Bearer ${tokenAcesso}` }, extras) };
}

// CPF sintético com dígitos verificadores válidos (o domínio de Clientes valida o DV).
export function cpfValido() {
  const n = [];
  for (let i = 0; i < 9; i++) n.push(Math.floor(Math.random() * 10));
  if (n.every((d) => d === n[0])) n[8] = (n[0] + 1) % 10; // 111.111.111-xx é inválido
  const dv = (base) => {
    const soma = base.reduce((s, d, i) => s + d * (base.length + 1 - i), 0);
    const resto = soma % 11;
    return resto < 2 ? 0 : 11 - resto;
  };
  n.push(dv(n));
  n.push(dv(n));
  return n.join("");
}

export function aleatorio(min, max) {
  return Math.floor(min + Math.random() * (max - min + 1));
}
