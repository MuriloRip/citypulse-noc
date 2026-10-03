
## Triagem Ativa

Quando um operador inicia o teste no Console de Operações, o ativo passa para `pendingTriage`. A interface mostra o contador de cinco minutos e duas resoluções: `POWER_OUTAGE` muda para `noPower`, encerra o incidente sem acionar SLA de TI e não derruba filhos; `NETWORK_FAULT` muda para `down`, registra incidente técnico e marca os filhos como `unreachable`. Sem resposta durante cinco minutos, o `PollingWorker` aplica automaticamente o fallback para `down`.

O endpoint é `POST /api/assets/{id}/status`. Para iniciar a triagem, envie `{ "resolution": null }`; para resolver, use `{ "resolution": "POWER_OUTAGE" }` ou `{ "resolution": "NETWORK_FAULT" }`.
