# CityPulse — entregas

- [x] **NOC operacional:** exibir estado geral, disponibilidade média, MTTR, incidentes abertos, ativos por status e lista de incidentes com localização e tipo de problema.
- [x] **Gestão de ativos:** permitir cadastro de alvo com nome, endereço, categoria, tier e relação opcional com ativo pai; validar campos obrigatórios no cliente.
- [x] **Polling e máquina de estados:** atualizar ativos em ciclo periódico; exigir 3 falhas consecutivas para marcar `down`; manter `degraded` para erro HTTP e propagar `unreachable` a filhos quando o pai estiver indisponível.
- [x] **Métricas e histórico:** calcular disponibilidade e MTTR a partir dos incidentes simulados e exibir janela de 24 horas.
- [x] **Segurança demonstrável:** aceitar somente endereços HTTP(S) ou IPv4 privados/municipais no CRUD; não executar shell ping; limitar concorrência no ciclo do motor.
- [x] **Portabilidade:** manter o domínio de polling isolado e documentar o mapeamento para .NET 8, SQLite e serviço Windows/Linux.
