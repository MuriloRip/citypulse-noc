# CityPulse — entregas

- [x] **NOC operacional:** exibir estado geral, disponibilidade média, MTTR, incidentes abertos, ativos por status e lista de incidentes com localização e tipo de problema.
- [x] **Gestão de ativos:** permitir cadastro de alvo com nome, endereço, categoria, tier e relação opcional com ativo pai; validar campos obrigatórios no cliente.
- [x] **Polling e máquina de estados real:** executar ICMP com `System.Net.NetworkInformation.Ping` e HTTP com `HttpClient`; atualizar ativos em ciclo periódico; exigir 3 falhas consecutivas para marcar `down`; manter `degraded` para erro HTTP e propagar `unreachable` a filhos quando o pai estiver indisponível.
- [x] **Métricas e histórico persistentes:** calcular MTTR e disponibilidade a partir dos incidentes e ativos gravados no SQLite via EF Core.
- [x] **Segurança demonstrável:** aceitar somente endereços HTTP(S) autorizados ou IPv4 privados/municipais no CRUD; não executar shell ping; limitar concorrência no ciclo do motor.
- [x] **Portabilidade .NET:** manter o domínio de polling em serviço hospedado ASP.NET Core, compilável para Windows Service ou daemon Linux, com Swagger e API documentada.
- [x] **Triagem Ativa:** transitar `ONLINE -> PENDING_TRIAGE -> NO_POWER` ou `OFFLINE`; oferecer `POST /api/assets/{id}/status` com `POWER_OUTAGE` e `NETWORK_FAULT`; aplicar fallback automático para `OFFLINE` após 5 minutos sem resposta; não acionar dependências em falta de energia e acionar cascata/SLA em falha de rede.
- [x] **Console de Operações:** orientar o operador em um card contextual com timer, microcopy de WhatsApp simulado e botões largos de resolução; refletir os estados na tabela e na topologia com badges sóbrios e pulse sutil de triagem.
