# CityPulse — pendências conhecidas

## Entregas implementadas

- [x] Painel operacional, gestão de ativos, incidentes, dependências e triagem ativa com sondagens reais.
- [x] Disponibilidade observada por tempo, verificações acumuladas e endpoint `/api/indicator` para apoiar a apuração do indicador 10.04 da ISO 37120.
- [x] Descoberta manual e autorizada de redes IPv4 privadas, com revisão antes da inclusão no inventário.
- [x] Empacotamento inicial e configurações de implantação local/Docker; ainda requer validação operacional antes de uso em produção.

Este arquivo acompanha trabalho que ainda não está implementado. O escopo e as capacidades atuais estão descritos no [README principal](./README.md).

## Identidade e governança

- [ ] Implementar autenticação individual OIDC/SSO antes de expor o serviço a vários técnicos.
- [ ] Definir perfis de acesso e autorização por operação, organização e inventário.
- [ ] Registrar auditoria de ações com operador, horário e resultado, sem guardar segredos ou conteúdo desnecessário.
- [ ] Definir retenção, exportação e exclusão de dados com o controlador responsável, conforme a finalidade e a base legal aplicável.

## Operação e integrações

- [ ] Criar testes automatizados para políticas de alvos, transições de estado, disponibilidade e migração de dados.
- [ ] Criar um fluxo de primeira configuração e modelos simples de verificação, com linguagem acessível e limites de coleta explícitos.
- [ ] Adicionar alertas configuráveis e integrações autorizadas.
- [ ] Planejar a migração para PostgreSQL, paginação, agendamento distribuído e retenção antes de buscar milhares de ativos.
- [ ] Implementar coleta autenticada de métricas de host e SNMPv3 somente leitura para equipamentos e redes autorizados.
- [ ] Adicionar testes de carga e publicar limites medidos antes de afirmar capacidade ou escala.

## Limites atuais

- A descoberta atual é manual, limitada a IPv4 privado `/24` a `/32` e a portas TCP comuns; não consulta SNMP, MAC/fabricante, não coleta payload e não garante identificação. A classificação por serviço é aproximada.
- O monitoramento atual faz somente verificações de disponibilidade ICMP/HTTP; não mede CPU/memória e não executa scripts customizados.
- O uso atual de SQLite não foi dimensionado nem testado para milhares de ativos.
- O backend ainda não autentica usuários nem distingue operadores; em produção deve ficar atrás de um proxy autenticado e HTTPS.
- Endereços IP, nomes e localizações podem ser dados pessoais dependendo do contexto. Colete apenas o necessário e restrinja acesso e retenção.
- O sistema não declara conformidade automática com a LGPD; a organização responsável deve definir finalidade, base legal, transparência e atendimento aos direitos aplicáveis.
