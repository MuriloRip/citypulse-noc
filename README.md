# CityPulse NOC

MVP demonstrável do projeto CityPulse — monitoramento inteligente de infraestrutura municipal para o Ideathon.

## Executar

Requer Node.js 18+ (não há dependências externas):

```bash
npm run check
npm start
# abrir http://localhost:4173
```

O motor usa um cenário demonstrativo determinístico para mostrar estados online, degradado, queda confirmada e falha em cascata. O botão **Simular incidente** acelera a visualização de uma indisponibilidade.

## Evolução para produção

- substituir `state` em memória por EF Core + SQLite;
- substituir `runPollCycle` pela implementação C# com `Parallel.ForEachAsync`, `Ping` e `HttpClient`;
- aplicar whitelist de sub-redes municipais/domínios autorizados no serviço de cadastro;
- proteger a API com autenticação local/VPN e servir atrás de HTTPS;
- trocar o cenário simulado por workers Windows Service/systemd.
